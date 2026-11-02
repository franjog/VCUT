using System.Globalization;

namespace VCUT.Core;

public enum OutputContainer
{
    Original,
    Matroska
}

public sealed class CutRequest
{
    public const long HardLimit200MbBytes = 195_000_000L; // 195 MB decimal (185.9 MiB) - strictly < 200 MB under all definitions
    public const long MaxTargetBytesUnder200Mb = 182_000_000L; // 182 MB target budget (173.5 MiB) ensuring safety margin for muxing overhead

    public CutRequest(
        VideoInfo video,
        TimeSpan startTime,
        TimeSpan endTime,
        OutputContainer outputContainer = OutputContainer.Original,
        bool limitUnder200Mb = false)
    {
        ArgumentNullException.ThrowIfNull(video);
        if (startTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(startTime), "El inicio no puede ser negativo.");
        }

        if (endTime <= startTime)
        {
            throw new ArgumentException("La marca de fin debe ser posterior a la de inicio.");
        }

        if (endTime > video.Duration)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "El fin supera la duración del vídeo.");
        }

        Video = video;
        StartTime = startTime;
        EndTime = endTime;
        Container = outputContainer;
        LimitUnder200Mb = limitUnder200Mb;
    }

    public VideoInfo Video { get; }
    public TimeSpan StartTime { get; }
    public TimeSpan EndTime { get; }
    public OutputContainer Container { get; }
    public bool LimitUnder200Mb { get; }
    public TimeSpan Duration => EndTime - StartTime;

    public string OutputPath
    {
        get
        {
            string source = Video.FilePath;
            string origExt = Path.GetExtension(source);
            string extension;
            if (Container == OutputContainer.Matroska)
            {
                extension = ".mkv";
            }
            else if (LimitUnder200Mb && !SupportsHevcContainer(origExt))
            {
                extension = ".mp4";
            }
            else
            {
                extension = origExt;
            }

            return Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, Path.GetFileNameWithoutExtension(source) + "_cut" + extension);
        }
    }

    public static bool SupportsHevcContainer(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return extension.ToLowerInvariant() switch
        {
            ".mp4" or ".mkv" or ".mov" or ".m4v" or ".ts" or ".mts" or ".m2ts" => true,
            _ => false
        };
    }

    public int CalculateTargetVideoKbpsUnder200Mb(out bool copyAudioLossless, out int aacAudioKbps)
    {
        double seconds = Math.Max(1.0, Duration.TotalSeconds);
        // Net bit budget for media, accounting for container muxing overhead (2.5%)
        double netTargetBytes = MaxTargetBytesUnder200Mb * 0.975;
        int totalBudgetKbps = (int)Math.Floor((netTargetBytes * 8.0) / (1000.0 * seconds));

        // For shorter clips where bit budget is generous (>= 600 kbps, typically <= 40 minutes),
        // copy original audio lossless 1:1 without risking exceeding 200 MB.
        if (totalBudgetKbps >= 600)
        {
            copyAudioLossless = true;
            aacAudioKbps = 192;
            int availableVideoKbps = totalBudgetKbps - 192;
            return Math.Min(availableVideoKbps, 20000);
        }

        // For longer clips, re-encode audio to AAC to ensure it doesn't starve video or overshoot 200 MB
        copyAudioLossless = false;
        if (totalBudgetKbps >= 350)
        {
            aacAudioKbps = 128;
            return Math.Clamp(totalBudgetKbps - 128, 90, 20000);
        }
        else if (totalBudgetKbps >= 220)
        {
            aacAudioKbps = 96;
            return Math.Clamp(totalBudgetKbps - 96, 75, 20000);
        }
        else
        {
            aacAudioKbps = 64;
            return Math.Clamp(totalBudgetKbps - 64, 50, 20000);
        }
    }

    public int CalculateTargetVideoKbpsUnder200Mb(out bool copyAudioLossless)
    {
        return CalculateTargetVideoKbpsUnder200Mb(out copyAudioLossless, out _);
    }

    public IReadOnlyList<string> BuildArguments(
        string temporaryOutput,
        string videoEncoder = "libx265",
        bool forceHighQualityAacAudio = false,
        bool useGpuHardwareDecode = false,
        bool videoOnly = false,
        TimeSpan? segmentStart = null,
        TimeSpan? segmentDuration = null)
    {
        if (!LimitUnder200Mb)
        {
            return new[]
            {
                "-hide_banner", "-nostdin", "-v", "error", "-n",
                "-ss", StartTime.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
                "-i", Video.FilePath,
                "-t", Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
                "-map", "0", "-c", "copy", "-map_metadata", "0", "-map_chapters", "-1",
                "-avoid_negative_ts", "make_zero", "-progress", "pipe:1", "-nostats", temporaryOutput
            };
        }

        int videoKbps = CalculateTargetVideoKbpsUnder200Mb(out bool copyAudioLossless, out int aacKbps);
        int maxRateKbps = videoKbps;
        int bufSizeKbps = videoKbps * 2;
        string encoder = string.IsNullOrWhiteSpace(videoEncoder) ? "libx265" : videoEncoder.Trim();
        TimeSpan effectiveStart = segmentStart ?? StartTime;
        TimeSpan effectiveDuration = segmentDuration ?? Duration;
        bool isNvenc = string.Equals(encoder, "hevc_nvenc", StringComparison.OrdinalIgnoreCase);

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error", "-n",
            "-thread_queue_size", "8192"
        };

        if (useGpuHardwareDecode && isNvenc)
        {
            args.AddRange(new[] { "-hwaccel", "cuda", "-hwaccel_output_format", "cuda", "-extra_hw_frames", "16" });
        }
        else
        {
            args.AddRange(new[] { "-threads", "0" });
        }

        args.AddRange(new[]
        {
            "-ss", effectiveStart.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-i", Video.FilePath,
            "-t", effectiveDuration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-map", "0:v:0"
        });

        if (videoOnly)
        {
            args.Add("-an");
        }
        else
        {
            args.AddRange(new[] { "-map", "0:a?" });
        }

        if (isNvenc)
        {
            var nvencParams = new List<string>
            {
                "-c:v", "hevc_nvenc",
                "-preset", "p2",
                "-rc", "vbr",
                "-b:v", $"{videoKbps}k",
                "-maxrate", $"{maxRateKbps}k",
                "-bufsize", $"{bufSizeKbps}k"
            };

            if (videoKbps >= 3500)
            {
                nvencParams.AddRange(new[] { "-cq", "20" });
            }

            if (!useGpuHardwareDecode)
            {
                nvencParams.AddRange(new[] { "-pix_fmt", "nv12" });
            }

            args.AddRange(nvencParams);
        }
        else if (string.Equals(encoder, "hevc_qsv", StringComparison.OrdinalIgnoreCase))
        {
            var qsvParams = new List<string>
            {
                "-c:v", "hevc_qsv",
                "-preset", "veryfast",
                "-b:v", $"{videoKbps}k",
                "-maxrate", $"{maxRateKbps}k",
                "-bufsize", $"{bufSizeKbps}k",
                "-pix_fmt", "nv12"
            };

            if (videoKbps >= 3500)
            {
                qsvParams.AddRange(new[] { "-global_quality", "20" });
            }

            args.AddRange(qsvParams);
        }
        else if (string.Equals(encoder, "hevc_amf", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(new[]
            {
                "-c:v", "hevc_amf",
                "-quality", "speed",
                "-rc", "vbr_peak",
                "-b:v", $"{videoKbps}k",
                "-maxrate", $"{maxRateKbps}k",
                "-pix_fmt", "nv12"
            });
        }
        else
        {
            var x265Params = new List<string>
            {
                "-c:v", "libx265",
                "-preset", "ultrafast",
                "-b:v", $"{videoKbps}k",
                "-maxrate", $"{maxRateKbps}k",
                "-bufsize", $"{bufSizeKbps}k",
                "-pix_fmt", "yuv420p",
                "-x265-params", "frame-threads=4:wpp=1:pmode=1:log-level=error"
            };

            if (videoKbps >= 3500)
            {
                x265Params.AddRange(new[] { "-crf", "20" });
            }

            args.AddRange(x265Params);
        }

        string outExt = Path.GetExtension(temporaryOutput)?.ToLowerInvariant();
        if (outExt is ".mp4" or ".mov" or ".m4v")
        {
            if (videoOnly)
            {
                args.AddRange(new[] { "-tag:v", "hvc1" });
            }
            else
            {
                args.AddRange(new[] { "-tag:v", "hvc1", "-movflags", "+faststart" });
            }
        }

        if (!videoOnly)
        {
            if (copyAudioLossless && !forceHighQualityAacAudio)
            {
                args.AddRange(new[] { "-c:a", "copy" });
            }
            else
            {
                int audioBitrate = forceHighQualityAacAudio ? Math.Max(aacKbps, 128) : aacKbps;
                args.AddRange(new[] { "-c:a", "aac", "-b:a", $"{audioBitrate}k" });
            }
        }

        args.AddRange(new[]
        {
            "-max_muxing_queue_size", "4096",
            "-map_metadata", "0",
            "-map_chapters", "-1",
            "-avoid_negative_ts", "make_zero",
            "-fs", HardLimit200MbBytes.ToString(CultureInfo.InvariantCulture),
            "-progress", "pipe:1",
            "-nostats",
            temporaryOutput
        });

        return args;
    }

    public IReadOnlyList<string> BuildConcatMuxArguments(string concatListPath, string temporaryOutput, bool forceHighQualityAacAudio = false)
    {
        _ = CalculateTargetVideoKbpsUnder200Mb(out bool copyAudioLossless, out int aacKbps);
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error", "-n",
            "-f", "concat", "-safe", "0", "-i", concatListPath,
            "-thread_queue_size", "8192",
            "-ss", StartTime.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-t", Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-i", Video.FilePath,
            "-map", "0:v:0",
            "-map", "1:a?",
            "-c:v", "copy"
        };

        string outExt = Path.GetExtension(temporaryOutput)?.ToLowerInvariant();
        if (outExt is ".mp4" or ".mov" or ".m4v")
        {
            args.AddRange(new[] { "-tag:v", "hvc1", "-movflags", "+faststart" });
        }

        if (copyAudioLossless && !forceHighQualityAacAudio)
        {
            args.AddRange(new[] { "-c:a", "copy" });
        }
        else
        {
            int audioBitrate = forceHighQualityAacAudio ? Math.Max(aacKbps, 128) : aacKbps;
            args.AddRange(new[] { "-c:a", "aac", "-b:a", $"{audioBitrate}k" });
        }

        args.AddRange(new[]
        {
            "-max_muxing_queue_size", "4096",
            "-map_metadata", "1",
            "-map_chapters", "-1",
            "-avoid_negative_ts", "make_zero",
            "-fs", HardLimit200MbBytes.ToString(CultureInfo.InvariantCulture),
            temporaryOutput
        });

        return args;
    }
}
