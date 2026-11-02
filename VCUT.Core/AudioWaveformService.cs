using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace VCUT.Core;

public sealed class AudioWaveformService
{
    private readonly string _ffmpeg;

    public AudioWaveformService(string toolsDirectory)
    {
        _ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
    }

    public async Task<float[]> AnalyzeAudioEnvelopeAsync(
        string filePath,
        TimeSpan duration,
        int bucketCount = 320,
        Action<float[]> onProgress = null,
        CancellationToken cancellationToken = default)
    {
        int clampedBuckets = Math.Clamp(bucketCount, 16, 2048);
        double totalSeconds = duration.TotalSeconds;
        if (string.IsNullOrWhiteSpace(filePath) ||
            totalSeconds <= 0.0 ||
            !File.Exists(filePath) ||
            !File.Exists(_ffmpeg))
        {
            return new float[clampedBuckets];
        }

        double[] rawEnergy = new double[clampedBuckets];
        int segmentCount = totalSeconds >= 30.0
            ? Math.Clamp(Environment.ProcessorCount / 2, 4, 8)
            : (totalSeconds >= 10.0 ? 2 : 1);
        segmentCount = Math.Min(segmentCount, clampedBuckets);

        int sampleRate = Math.Clamp((int)Math.Ceiling(clampedBuckets * 240.0 / Math.Max(0.5, totalSeconds)), 400, 8000);
        int bucketsPerSegment = (int)Math.Ceiling((double)clampedBuckets / segmentCount);
        long lastProgressTick = Environment.TickCount64;

        var tasks = new List<Task>(segmentCount);
        for (int s = 0; s < segmentCount; s++)
        {
            int startBucket = s * bucketsPerSegment;
            int countBuckets = Math.Min(bucketsPerSegment, clampedBuckets - startBucket);
            if (countBuckets <= 0)
            {
                continue;
            }

            tasks.Add(Task.Run(async () =>
            {
                await DecodeAudioSegmentAsync(
                    filePath,
                    totalSeconds,
                    sampleRate,
                    rawEnergy,
                    startBucket,
                    countBuckets,
                    () =>
                    {
                        if (onProgress is null)
                        {
                            return;
                        }

                        long now = Environment.TickCount64;
                        long prev = Interlocked.Read(ref lastProgressTick);
                        if (now - prev >= 180 && Interlocked.CompareExchange(ref lastProgressTick, now, prev) == prev)
                        {
                            onProgress(NormalizeEnvelope(rawEnergy));
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        float[] finalLevels = NormalizeEnvelope(rawEnergy);
        onProgress?.Invoke(finalLevels);
        return finalLevels;
    }

    private async Task DecodeAudioSegmentAsync(
        string filePath,
        double totalDurationSeconds,
        int sampleRate,
        double[] rawEnergy,
        int startBucket,
        int bucketCount,
        Action onChunkDecoded,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int totalBuckets = rawEnergy.Length;
        double startSeconds = (startBucket / (double)totalBuckets) * totalDurationSeconds;
        double segmentDurationSeconds = (bucketCount / (double)totalBuckets) * totalDurationSeconds;
        if (segmentDurationSeconds <= 0.001)
        {
            return;
        }

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
        if (startSeconds > 0.001)
        {
            info.ArgumentList.Add("-ss");
            info.ArgumentList.Add(startSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        }

        info.ArgumentList.Add("-i");
        info.ArgumentList.Add(filePath);
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(segmentDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-vn");
        info.ArgumentList.Add("-sn");
        info.ArgumentList.Add("-dn");
        info.ArgumentList.Add("-ac");
        info.ArgumentList.Add("1");
        info.ArgumentList.Add("-ar");
        info.ArgumentList.Add(sampleRate.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add("s16le");
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
            long expectedSamples = Math.Max(bucketCount, (long)Math.Round(segmentDurationSeconds * sampleRate));
            double[] sumSqByBucket = new double[bucketCount];
            int[] maxAbsByBucket = new int[bucketCount];
            int[] sampleCountByBucket = new int[bucketCount];

            Stream stdout = process.StandardOutput.BaseStream;
            byte[] buffer = new byte[32768];
            int leftover = 0;
            long sampleIndex = 0;
            int lastUpdatedBucket = -1;

            while (true)
            {
                int read = await stdout.ReadAsync(buffer.AsMemory(leftover, buffer.Length - leftover), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                int totalBytes = leftover + read;
                int usableBytes = totalBytes & ~1;
                ReadOnlySpan<byte> span = buffer.AsSpan(0, usableBytes);

                for (int offset = 0; offset < usableBytes; offset += 2)
                {
                    short pcm = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
                    int abs = pcm == short.MinValue ? 32768 : Math.Abs((int)pcm);
                    int localBucket = (int)((sampleIndex * bucketCount) / expectedSamples);
                    if (localBucket >= bucketCount)
                    {
                        localBucket = bucketCount - 1;
                    }

                    double normSample = abs / 32768.0;
                    sumSqByBucket[localBucket] += normSample * normSample;
                    if (abs > maxAbsByBucket[localBucket])
                    {
                        maxAbsByBucket[localBucket] = abs;
                    }

                    sampleCountByBucket[localBucket]++;
                    sampleIndex++;

                    if (localBucket > lastUpdatedBucket && lastUpdatedBucket >= 0)
                    {
                        CommitBucketEnergy(rawEnergy, startBucket + lastUpdatedBucket, sumSqByBucket[lastUpdatedBucket], maxAbsByBucket[lastUpdatedBucket], sampleCountByBucket[lastUpdatedBucket]);
                    }

                    lastUpdatedBucket = localBucket;
                }

                leftover = totalBytes - usableBytes;
                if (leftover == 1)
                {
                    buffer[0] = buffer[usableBytes];
                }

                onChunkDecoded?.Invoke();
            }

            for (int b = 0; b < bucketCount; b++)
            {
                if (sampleCountByBucket[b] > 0)
                {
                    CommitBucketEnergy(rawEnergy, startBucket + b, sumSqByBucket[b], maxAbsByBucket[b], sampleCountByBucket[b]);
                }
            }

            onChunkDecoded?.Invoke();
        }
        finally
        {
            try
            {
                await Task.WhenAll(process.WaitForExitAsync(cancellationToken), drainErrTask).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static void CommitBucketEnergy(double[] rawEnergy, int globalBucket, double sumSq, int maxAbs, int count)
    {
        if ((uint)globalBucket >= (uint)rawEnergy.Length || count <= 0)
        {
            return;
        }

        double rms = Math.Sqrt(sumSq / count);
        double peak = maxAbs / 32768.0;

        // Silence threshold (~ -52 dBFS): below this is digital silence / negligible floor
        if (rms < 0.0015 && peak < 0.0045)
        {
            Volatile.Write(ref rawEnergy[globalBucket], 0.0);
            return;
        }

        double combined = rms * 0.65 + peak * 0.35;
        Volatile.Write(ref rawEnergy[globalBucket], combined);
    }

    public static float[] NormalizeEnvelope(double[] rawEnergy)
    {
        if (rawEnergy is null || rawEnergy.Length == 0)
        {
            return Array.Empty<float>();
        }

        int n = rawEnergy.Length;
        var result = new float[n];
        var nonZero = new List<double>(n);
        double maxVal = 0.0;

        for (int i = 0; i < n; i++)
        {
            double val = Volatile.Read(ref rawEnergy[i]);
            if (val > 0.0015)
            {
                nonZero.Add(val);
                if (val > maxVal)
                {
                    maxVal = val;
                }
            }
        }

        if (nonZero.Count == 0 || maxVal <= 0.0045)
        {
            return result;
        }

        nonZero.Sort();
        int p95Index = Math.Clamp((int)Math.Floor(nonZero.Count * 0.95), 0, nonZero.Count - 1);
        double referencePeak = Math.Max(0.015, nonZero[p95Index] * 1.05);

        for (int i = 0; i < n; i++)
        {
            double val = Volatile.Read(ref rawEnergy[i]);
            if (val <= 0.0015)
            {
                result[i] = 0.0f;
            }
            else
            {
                double normalized = Math.Clamp(val / referencePeak, 0.0, 1.0);
                result[i] = (float)Math.Clamp(Math.Pow(normalized, 0.62), 0.05, 1.0);
            }
        }

        return result;
    }
}
