using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VCUT.Core;

public sealed class VideoThumbnailService : IDisposable
{
    public const double BucketIntervalSeconds = 0.5;
    private const int MaxPrewarmBuckets = 24000;

    private static readonly int MaxInteractiveConcurrency = Math.Clamp(Environment.ProcessorCount, 8, 16);

    private readonly string _ffmpeg;
    private readonly SemaphoreSlim _interactiveConcurrency = new(MaxInteractiveConcurrency, MaxInteractiveConcurrency);
    private volatile byte[][] _frames = Array.Empty<byte[]>();
    private volatile byte[][] _exactFrames = Array.Empty<byte[]>();
    private int _cachedCount;
    private int _lastBurstCenterBucket = -1000;
    private string _currentFilePath;
    private double _currentDurationSeconds;
    private bool _disposed;

    public VideoThumbnailService(string toolsDirectory)
    {
        _ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
    }

    public int CachedFrameCount => Volatile.Read(ref _cachedCount);

    public int TotalHalfSecondBuckets => _frames.Length;

    public void Reset(string filePath, TimeSpan duration, int thumbWidth = 0)
    {
        _currentFilePath = string.IsNullOrWhiteSpace(filePath) ? null : Path.GetFullPath(filePath);
        _currentDurationSeconds = Math.Max(0.0, duration.TotalSeconds);
        int bucketCount = _currentDurationSeconds <= 0
            ? 0
            : Math.Clamp((int)Math.Ceiling(_currentDurationSeconds / BucketIntervalSeconds), 1, MaxPrewarmBuckets);
        _frames = bucketCount > 0 ? new byte[bucketCount][] : Array.Empty<byte[]>();
        _exactFrames = bucketCount > 0 ? new byte[bucketCount][] : Array.Empty<byte[]>();
        Volatile.Write(ref _cachedCount, 0);
        Volatile.Write(ref _lastBurstCenterBucket, -1000);
    }

    public byte[] TryGetFrameByBucket(int bucket)
    {
        byte[][] exact = _exactFrames;
        byte[][] frames = _frames;
        if ((uint)bucket >= (uint)frames.Length)
        {
            return null;
        }

        if ((uint)bucket < (uint)exact.Length)
        {
            byte[] hq = Volatile.Read(ref exact[bucket]);
            if (hq is not null)
            {
                return hq;
            }
        }

        return Volatile.Read(ref frames[bucket]);
    }

    public bool TryGetFullQualityFrame(double fraction, out byte[] jpegBytes)
    {
        jpegBytes = null;
        byte[][] exact = _exactFrames;
        byte[][] frames = _frames;
        if (frames.Length == 0 || _currentDurationSeconds <= 0)
        {
            return false;
        }

        int targetBucket = Math.Clamp(FractionToHalfSecondBucket(fraction), 0, frames.Length - 1);
        if (targetBucket < exact.Length)
        {
            byte[] hq = Volatile.Read(ref exact[targetBucket]);
            if (hq is not null)
            {
                jpegBytes = hq;
                return true;
            }
        }

        byte[] fast = Volatile.Read(ref frames[targetBucket]);
        if (fast is not null)
        {
            jpegBytes = fast;
            return true;
        }

        return false;
    }

    public bool TryGetClosestFrame(double fraction, out byte[] jpegBytes, int maxDistanceBuckets = int.MaxValue)
    {
        jpegBytes = null;
        byte[][] frames = _frames;
        if (frames.Length == 0 || _currentDurationSeconds <= 0 || Volatile.Read(ref _cachedCount) <= 0)
        {
            return false;
        }

        int targetBucket = Math.Clamp(FractionToHalfSecondBucket(fraction), 0, frames.Length - 1);
        byte[] exact = Volatile.Read(ref frames[targetBucket]);
        if (exact is not null)
        {
            jpegBytes = exact;
            return true;
        }

        int limit = Math.Min(maxDistanceBuckets, frames.Length);
        for (int d = 1; d <= limit; d++)
        {
            int left = targetBucket - d;
            int right = targetBucket + d;
            if (left < 0 && right >= frames.Length)
            {
                break;
            }

            if (left >= 0)
            {
                byte[] candidate = Volatile.Read(ref frames[left]);
                if (candidate is not null)
                {
                    jpegBytes = candidate;
                    return true;
                }
            }

            if (right < frames.Length)
            {
                byte[] candidate = Volatile.Read(ref frames[right]);
                if (candidate is not null)
                {
                    jpegBytes = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public async Task<byte[]> GetFrameAsync(double fraction, CancellationToken cancellationToken)
    {
        byte[][] frames = _frames;
        string filePath = _currentFilePath;
        double durationSeconds = _currentDurationSeconds;
        if (string.IsNullOrEmpty(filePath) || durationSeconds <= 0 || frames.Length == 0 || !File.Exists(filePath))
        {
            return null;
        }

        int bucketKey = Math.Clamp(FractionToHalfSecondBucket(fraction), 0, frames.Length - 1);
        byte[] cached = Volatile.Read(ref frames[bucketKey]);
        if (cached is not null)
        {
            return cached;
        }

        double targetSeconds = Math.Clamp(bucketKey * BucketIntervalSeconds, 0.0, Math.Max(0.0, durationSeconds - 0.05));
        await _interactiveConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = Volatile.Read(ref frames[bucketKey]);
            if (cached is not null)
            {
                return cached;
            }

            byte[] extracted = await ExtractFrameJpegCoreAsync(filePath, targetSeconds, width: 0, accurateSeek: false, cancellationToken).ConfigureAwait(false);
            if (extracted is { Length: > 0 } && ReferenceEquals(_frames, frames))
            {
                StoreFrame(frames, bucketKey, extracted, overwrite: false);
            }

            return extracted;
        }
        finally
        {
            _interactiveConcurrency.Release();
        }
    }

    public async Task<byte[]> GetFullQualityFrameAsync(double fraction, bool accurateSeek, CancellationToken cancellationToken)
    {
        byte[][] frames = _frames;
        byte[][] exact = _exactFrames;
        string filePath = _currentFilePath;
        double durationSeconds = _currentDurationSeconds;
        if (string.IsNullOrEmpty(filePath) || durationSeconds <= 0 || frames.Length == 0 || !File.Exists(filePath))
        {
            return null;
        }

        int bucketKey = Math.Clamp(FractionToHalfSecondBucket(fraction), 0, frames.Length - 1);
        byte[] cachedExact = Volatile.Read(ref exact[bucketKey]);
        if (cachedExact is not null)
        {
            return cachedExact;
        }

        if (!accurateSeek)
        {
            byte[] cachedFast = Volatile.Read(ref frames[bucketKey]);
            if (cachedFast is not null)
            {
                return cachedFast;
            }

            if (frames.Length > 60 && Math.Abs(bucketKey - Volatile.Read(ref _lastBurstCenterBucket)) > 24)
            {
                Volatile.Write(ref _lastBurstCenterBucket, bucketKey);
                int burstStart = Math.Max(0, bucketKey - 20);
                int burstCount = Math.Min(120, frames.Length - burstStart);
                bool skipNonKey = durationSeconds >= 20.0;
                _ = Task.Run(() => StreamHalfSecondSegmentAsync(filePath, frames, burstStart, burstCount, skipNonKey, cancellationToken), cancellationToken);
            }
        }

        double targetSeconds = Math.Clamp(fraction * durationSeconds, 0.0, Math.Max(0.0, durationSeconds - 0.04));

        await _interactiveConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cachedExact = Volatile.Read(ref exact[bucketKey]);
            if (cachedExact is not null)
            {
                return cachedExact;
            }

            if (!accurateSeek)
            {
                byte[] cachedFast = Volatile.Read(ref frames[bucketKey]);
                if (cachedFast is not null)
                {
                    return cachedFast;
                }
            }

            // width = 0 extracts at 100% native video resolution with maximum JPEG quality (-q:v 1 -qmin 1)
            byte[] extracted = await ExtractFrameJpegCoreAsync(filePath, targetSeconds, width: 0, accurateSeek: accurateSeek, cancellationToken).ConfigureAwait(false);
            if (extracted is { Length: > 0 } && ReferenceEquals(_frames, frames))
            {
                if (accurateSeek)
                {
                    Volatile.Write(ref exact[bucketKey], extracted);
                }

                StoreFrame(frames, bucketKey, extracted, overwrite: accurateSeek);
            }

            return extracted;
        }
        finally
        {
            _interactiveConcurrency.Release();
        }
    }

    private void StoreFrame(byte[][] frames, int bucket, byte[] jpeg, bool overwrite)
    {
        if ((uint)bucket >= (uint)frames.Length || jpeg is null || jpeg.Length == 0)
        {
            return;
        }

        if (overwrite)
        {
            if (Interlocked.Exchange(ref frames[bucket], jpeg) is null)
            {
                Interlocked.Increment(ref _cachedCount);
            }
        }
        else
        {
            if (Interlocked.CompareExchange(ref frames[bucket], jpeg, null) is null)
            {
                Interlocked.Increment(ref _cachedCount);
            }
        }
    }

    public Task PrewarmAllSecondsAsync(CancellationToken cancellationToken)
    {
        return PrewarmAllHalfSecondsAsync(cancellationToken);
    }

    public async Task PrewarmAllHalfSecondsAsync(CancellationToken cancellationToken)
    {
        string filePath = _currentFilePath;
        double durationSeconds = _currentDurationSeconds;
        byte[][] frames = _frames;
        if (string.IsNullOrEmpty(filePath) || durationSeconds <= 0 || frames.Length == 0 || !File.Exists(_ffmpeg) || !File.Exists(filePath))
        {
            return;
        }

        int totalBuckets = frames.Length;

        // Launch parallel native-resolution (-qmin 1 -q:v 1) streams across all CPU cores simultaneously
        int segmentCount = durationSeconds >= 30.0
            ? Math.Clamp(Environment.ProcessorCount / 2, 6, 10)
            : (durationSeconds >= 10.0 ? 2 : 1);
        bool skipNonKeyframes = durationSeconds >= 20.0;
        int bucketsPerSegment = (int)Math.Ceiling((double)totalBuckets / segmentCount);
        var segmentTasks = new List<Task>(segmentCount);

        for (int s = 0; s < segmentCount; s++)
        {
            int startBucket = s * bucketsPerSegment;
            int countBuckets = Math.Min(bucketsPerSegment, totalBuckets - startBucket);
            if (countBuckets <= 0)
            {
                continue;
            }

            segmentTasks.Add(Task.Run(
                () => StreamHalfSecondSegmentAsync(filePath, frames, startBucket, countBuckets, skipNonKeyframes, cancellationToken),
                cancellationToken));
        }

        await Task.WhenAll(segmentTasks).ConfigureAwait(false);

        // For short clips, ensure every single 0.5s bucket is populated if stream rounding skipped the tail frame
        if (totalBuckets <= 40 && ReferenceEquals(_frames, frames))
        {
            for (int b = 0; b < totalBuckets; b++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref frames[b]) is null)
                {
                    double frac = durationSeconds > 0 ? (b * BucketIntervalSeconds) / durationSeconds : 0.0;
                    await GetFrameAsync(Math.Clamp(frac, 0.0, 0.999), cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task StreamHalfSecondSegmentAsync(
        string filePath,
        byte[][] frames,
        int startBucket,
        int bucketCount,
        bool skipNonKeyframes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        double startSeconds = startBucket * BucketIntervalSeconds;
        double durationSeconds = bucketCount * BucketIntervalSeconds;

        var info = new ProcessStartInfo(_ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-nostdin");
        info.ArgumentList.Add("-v");
        info.ArgumentList.Add("error");
        if (skipNonKeyframes)
        {
            info.ArgumentList.Add("-discard");
            info.ArgumentList.Add("nokey");
            info.ArgumentList.Add("-skip_frame");
            info.ArgumentList.Add("nokey");
        }

        info.ArgumentList.Add("-threads");
        info.ArgumentList.Add("2");
        if (startSeconds > 0.001)
        {
            info.ArgumentList.Add("-noaccurate_seek");
            info.ArgumentList.Add("-ss");
            info.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        info.ArgumentList.Add("-i");
        info.ArgumentList.Add(filePath);
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(durationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-an");
        info.ArgumentList.Add("-sn");
        info.ArgumentList.Add("-dn");
        info.ArgumentList.Add("-vf");
        info.ArgumentList.Add("fps=2");
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add("image2pipe");
        info.ArgumentList.Add("-vcodec");
        info.ArgumentList.Add("mjpeg");
        info.ArgumentList.Add("-pix_fmt");
        info.ArgumentList.Add("yuvj420p");
        info.ArgumentList.Add("-qmin");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("-q:v");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("pipe:1");

        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch
        {
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        });

        Task drainErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            Stream stdout = process.StandardOutput.BaseStream;
            byte[] readBuffer = new byte[131072];
            using var frameStream = new MemoryStream(131072);
            bool inFrame = false;
            byte prevByte = 0;
            int currentBucket = startBucket;
            int endBucketExclusive = Math.Min(frames.Length, startBucket + bucketCount);
            byte[] lastJpeg = null;

            while (currentBucket < endBucketExclusive && ReferenceEquals(_frames, frames))
            {
                int bytesRead = await stdout.ReadAsync(readBuffer.AsMemory(0, readBuffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                for (int i = 0; i < bytesRead; i++)
                {
                    byte b = readBuffer[i];
                    if (!inFrame)
                    {
                        if (prevByte == 0xFF && b == 0xD8)
                        {
                            inFrame = true;
                            frameStream.SetLength(0);
                            frameStream.WriteByte(0xFF);
                            frameStream.WriteByte(0xD8);
                            prevByte = 0;
                            continue;
                        }

                        prevByte = b;
                    }
                    else
                    {
                        frameStream.WriteByte(b);
                        if (prevByte == 0xFF && b == 0xD9)
                        {
                            if (frameStream.Length > 64 && ReferenceEquals(_frames, frames))
                            {
                                byte[] jpeg = TryReuseIdenticalFrame(frameStream, lastJpeg);
                                lastJpeg = jpeg;
                                StoreFrame(frames, currentBucket, jpeg, overwrite: false);
                            }

                            currentBucket++;
                            inFrame = false;
                            prevByte = 0;
                            if (currentBucket >= endBucketExclusive)
                            {
                                break;
                            }

                            continue;
                        }

                        prevByte = b;
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            try
            {
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), drainErrTask).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static byte[] TryReuseIdenticalFrame(MemoryStream stream, byte[] previous)
    {
        int len = (int)stream.Length;
        if (previous is not null && previous.Length == len && stream.TryGetBuffer(out ArraySegment<byte> buffer))
        {
            ReadOnlySpan<byte> currentSpan = buffer.AsSpan(0, len);
            if (currentSpan.SequenceEqual(previous))
            {
                return previous;
            }
        }

        return stream.ToArray();
    }

    public static async Task<byte[]> ExtractFrameJpegAsync(string ffmpegPath, string filePath, double timestampSeconds, int width, CancellationToken cancellationToken)
    {
        return await ExtractFrameJpegCoreAsync(ffmpegPath, filePath, timestampSeconds, width, accurateSeek: false, cancellationToken).ConfigureAwait(false);
    }

    private Task<byte[]> ExtractFrameJpegCoreAsync(string filePath, double timestampSeconds, int width, bool accurateSeek, CancellationToken cancellationToken)
    {
        return ExtractFrameJpegCoreAsync(_ffmpeg, filePath, timestampSeconds, width, accurateSeek, cancellationToken);
    }

    private static async Task<byte[]> ExtractFrameJpegCoreAsync(
        string ffmpegPath,
        string filePath,
        double timestampSeconds,
        int width,
        bool accurateSeek,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(ffmpegPath) || !File.Exists(filePath))
        {
            return null;
        }

        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        string seekText = Math.Max(0.0, timestampSeconds).ToString("0.###", CultureInfo.InvariantCulture);
        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-nostdin");
        info.ArgumentList.Add("-v");
        info.ArgumentList.Add("error");
        info.ArgumentList.Add("-threads");
        info.ArgumentList.Add("4");
        if (!accurateSeek)
        {
            info.ArgumentList.Add("-noaccurate_seek");
        }

        info.ArgumentList.Add("-ss");
        info.ArgumentList.Add(seekText);
        info.ArgumentList.Add("-i");
        info.ArgumentList.Add(filePath);
        info.ArgumentList.Add("-an");
        info.ArgumentList.Add("-sn");
        info.ArgumentList.Add("-dn");
        info.ArgumentList.Add("-frames:v");
        info.ArgumentList.Add("1");
        if (width > 0)
        {
            info.ArgumentList.Add("-vf");
            info.ArgumentList.Add($"scale={width}:-2:flags=lanczos");
        }

        info.ArgumentList.Add("-f");
        info.ArgumentList.Add("image2pipe");
        info.ArgumentList.Add("-vcodec");
        info.ArgumentList.Add("mjpeg");
        info.ArgumentList.Add("-pix_fmt");
        info.ArgumentList.Add("yuvj420p");
        info.ArgumentList.Add("-qmin");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("-q:v");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("pipe:1");

        using var process = new Process { StartInfo = info };
        if (!process.Start())
        {
            return null;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch
        {
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        });

        using var memory = new MemoryStream(24576);
        Task copyTask = process.StandardOutput.BaseStream.CopyToAsync(memory, 16384, cancellationToken);
        Task drainErrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), copyTask, drainErrTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0 || memory.Length == 0)
        {
            return null;
        }

        return memory.ToArray();
    }

    public int FractionToHalfSecondBucket(double fraction)
    {
        if (_currentDurationSeconds <= 0)
        {
            return 0;
        }

        double sec = Math.Clamp(fraction * _currentDurationSeconds, 0.0, Math.Max(0.0, _currentDurationSeconds - 0.001));
        return (int)Math.Floor(sec / BucketIntervalSeconds);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frames = Array.Empty<byte[]>();
        _exactFrames = Array.Empty<byte[]>();
        Volatile.Write(ref _cachedCount, 0);
        _interactiveConcurrency.Dispose();
    }
}
