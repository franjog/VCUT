using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace VCUT.Core;

public sealed class RamCacheStatus
{
    public RamCacheStatus(string filePath, long fileBytes, long cachedBytes, long availableRamBytes, TimeSpan elapsed)
    {
        FilePath = filePath;
        FileBytes = Math.Max(0L, fileBytes);
        CachedBytes = Math.Max(0L, Math.Min(fileBytes, cachedBytes));
        AvailableRamBytes = Math.Max(0L, availableRamBytes);
        Elapsed = elapsed;
        IsFullyCached = FileBytes > 0 && CachedBytes >= FileBytes;
    }

    public string FilePath { get; }
    public long FileBytes { get; }
    public long CachedBytes { get; }
    public long AvailableRamBytes { get; }
    public TimeSpan Elapsed { get; }
    public bool IsFullyCached { get; }

    public double CachedMegabytes => CachedBytes / (1024.0 * 1024.0);

    public string Summary
    {
        get
        {
            if (CachedBytes <= 0)
            {
                return "Sin caché RAM";
            }

            double mb = CachedBytes / (1024.0 * 1024.0);
            string sizeText = mb >= 1024.0 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0} MB";
            return IsFullyCached ? $"{sizeText} en RAM (100%)" : $"{sizeText} en RAM";
        }
    }
}

public sealed class VideoRamCache : IDisposable
{
    private const int HeaderPrimeBytes = 8 * 1024 * 1024;
    private const int ChunkSizeBytes = 4 * 1024 * 1024;
    private const long MaxHardLimitBytes = 2L * 1024L * 1024L * 1024L;
    private const long MinimumFreeReserveBytes = 1200L * 1024L * 1024L;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private readonly object _sync = new();
    private List<byte[]> _chunks = new();
    private MemoryMappedFile _mappedFile;
    private RamCacheStatus _currentStatus;
    private bool _disposed;

    public RamCacheStatus CurrentStatus
    {
        get
        {
            lock (_sync)
            {
                return _currentStatus;
            }
        }
    }

    public static long GetAvailablePhysicalMemoryBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.ullAvailPhys > 0UL)
            {
                return (long)Math.Min(status.ullAvailPhys, (ulong)long.MaxValue);
            }
        }

        return 2L * 1024L * 1024L * 1024L;
    }

    public static long CalculateRamBudgetBytes(long availablePhysicalBytes)
    {
        if (availablePhysicalBytes <= MinimumFreeReserveBytes)
        {
            return 64L * 1024L * 1024L;
        }

        long usable = (long)((availablePhysicalBytes - MinimumFreeReserveBytes) * 0.65);
        return Math.Clamp(usable, 64L * 1024L * 1024L, MaxHardLimitBytes);
    }

    public static void PrimeHeaders(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        long length = stream.Length;
        if (length <= 0)
        {
            return;
        }

        int headCount = (int)Math.Min(length, HeaderPrimeBytes);
        byte[] buffer = new byte[Math.Min(headCount, 1024 * 1024)];
        int remainingHead = headCount;
        while (remainingHead > 0)
        {
            int read = stream.Read(buffer, 0, Math.Min(buffer.Length, remainingHead));
            if (read <= 0)
            {
                break;
            }

            remainingHead -= read;
        }

        if (length > HeaderPrimeBytes)
        {
            long tailOffset = Math.Max((long)HeaderPrimeBytes, length - HeaderPrimeBytes);
            stream.Seek(tailOffset, SeekOrigin.Begin);
            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }
            }
        }
    }

    public async Task<RamCacheStatus> PreloadAsync(string filePath, CancellationToken cancellationToken, IProgress<double> progress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("No se encuentra el archivo de vídeo para cargar en RAM.", fullPath);
        }

        return await Task.Run(() =>
        {
            var watch = Stopwatch.StartNew();
            ReleaseCacheCore();

            var info = new FileInfo(fullPath);
            long fileLength = info.Length;
            long availableRam = GetAvailablePhysicalMemoryBytes();
            long budget = CalculateRamBudgetBytes(availableRam);
            long targetBytes = Math.Min(fileLength, budget);

            var newChunks = new List<byte[]>();
            MemoryMappedFile mapped = null;
            long totalCached = 0;

            try
            {
                if (fileLength > 0)
                {
                    try
                    {
                        mapped = MemoryMappedFile.CreateFromFile(
                            new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess),
                            null,
                            0,
                            MemoryMappedFileAccess.Read,
                            HandleInheritability.None,
                            leaveOpen: false);
                    }
                    catch
                    {
                    }

                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ChunkSizeBytes, FileOptions.SequentialScan);
                    long headBudget = targetBytes >= fileLength ? fileLength : (long)(targetBytes * 0.85);
                    long tailBudget = targetBytes - headBudget;

                    while (totalCached < headBudget)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int toRead = (int)Math.Min(ChunkSizeBytes, headBudget - totalCached);
                        byte[] block = new byte[toRead];
                        int offset = 0;
                        while (offset < toRead)
                        {
                            int read = stream.Read(block, offset, toRead - offset);
                            if (read <= 0)
                            {
                                break;
                            }

                            offset += read;
                        }

                        if (offset <= 0)
                        {
                            break;
                        }

                        newChunks.Add(block);
                        totalCached += offset;
                        if (targetBytes > 0)
                        {
                            progress?.Report(Math.Clamp((double)totalCached / targetBytes, 0.0, 1.0));
                        }
                    }

                    if (tailBudget > 0 && stream.Position < fileLength - tailBudget)
                    {
                        stream.Seek(fileLength - tailBudget, SeekOrigin.Begin);
                        long tailRead = 0;
                        while (tailRead < tailBudget)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int toRead = (int)Math.Min(ChunkSizeBytes, tailBudget - tailRead);
                            byte[] block = new byte[toRead];
                            int offset = 0;
                            while (offset < toRead)
                            {
                                int read = stream.Read(block, offset, toRead - offset);
                                if (read <= 0)
                                {
                                    break;
                                }

                                offset += read;
                            }

                            if (offset <= 0)
                            {
                                break;
                            }

                            newChunks.Add(block);
                            tailRead += offset;
                            totalCached += offset;
                            if (targetBytes > 0)
                            {
                                progress?.Report(Math.Clamp((double)totalCached / targetBytes, 0.0, 1.0));
                            }
                        }
                    }
                }

                watch.Stop();
                var status = new RamCacheStatus(fullPath, fileLength, totalCached, availableRam, watch.Elapsed);
                lock (_sync)
                {
                    if (_disposed)
                    {
                        mapped?.Dispose();
                    }
                    else
                    {
                        _chunks = newChunks;
                        _mappedFile = mapped;
                        _currentStatus = status;
                    }
                }

                progress?.Report(1.0);
                return status;
            }
            catch
            {
                mapped?.Dispose();
                newChunks.Clear();
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Clear()
    {
        ReleaseCacheCore();
    }

    private void ReleaseCacheCore()
    {
        lock (_sync)
        {
            _chunks.Clear();
            _mappedFile?.Dispose();
            _mappedFile = null;
            _currentStatus = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _chunks.Clear();
            _mappedFile?.Dispose();
            _mappedFile = null;
            _currentStatus = null;
        }
    }
}

