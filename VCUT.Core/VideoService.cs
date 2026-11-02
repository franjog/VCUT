using System.Diagnostics;

namespace VCUT.Core;

public sealed class VideoService
{
    private readonly string _ffmpeg;
    private readonly string _ffprobe;

    public VideoService(string toolsDirectory)
    {
        _ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe");
        _ffprobe = Path.Combine(toolsDirectory, "ffprobe.exe");
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffprobe))
        {
            return;
        }

        try
        {
            await MediaProcess.RunAsync(_ffprobe, new[] { "-hide_banner", "-version" }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task<VideoInfo> ProbeAsync(
        string filePath,
        CancellationToken cancellationToken,
        IProgress<double> progress = null,
        bool primeRamHeaders = true,
        bool allowNativeFastPath = false)
    {
        CheckTools();
        filePath = Path.GetFullPath(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encuentra el archivo de vídeo.", filePath);
        }

        progress?.Report(0.1);

        if (primeRamHeaders)
        {
            try
            {
                VideoRamCache.PrimeHeaders(filePath);
            }
            catch
            {
            }
        }

        progress?.Report(0.25);

        if (allowNativeFastPath && VideoInfo.TryProbeFastIsoBmff(filePath, out VideoInfo nativeInfo))
        {
            progress?.Report(1.0);
            return nativeInfo;
        }

        string[] fastArgs =
        {
            "-hide_banner", "-v", "error",
            "-threads", "0",
            "-probesize", "5000000",
            "-analyzeduration", "2000000",
            "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,duration,disposition",
            "-of", "json",
            filePath
        };

        ProcessResult result = await MediaProcess.RunAsync(_ffprobe, fastArgs, cancellationToken).ConfigureAwait(false);
        progress?.Report(0.85);

        if (result.ExitCode == 0)
        {
            try
            {
                VideoInfo fastInfo = VideoInfo.FromProbeJson(filePath, result.Output);
                progress?.Report(1.0);
                return fastInfo;
            }
            catch (InvalidDataException)
            {
                // Fallback to full probe if container requires deeper stream inspection.
            }
        }

        ProcessResult fullResult = await MediaProcess.RunAsync(
            _ffprobe,
            new[] { "-hide_banner", "-v", "error", "-threads", "0", "-show_format", "-show_streams", "-of", "json", filePath },
            cancellationToken).ConfigureAwait(false);

        if (fullResult.ExitCode != 0)
        {
            throw new InvalidDataException("No se puede abrir este archivo con FFprobe." + Environment.NewLine + fullResult.Errors);
        }

        VideoInfo info = VideoInfo.FromProbeJson(filePath, fullResult.Output);
        progress?.Report(1.0);
        return info;
    }

    public async Task<string> CutAsync(CutRequest request, IProgress<CutProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CheckTools();
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(request.Video.FilePath))
        {
            throw new FileNotFoundException("El vídeo de origen ya no está disponible.", request.Video.FilePath);
        }

        string output = request.OutputPath;
        if (File.Exists(output))
        {
            throw new IOException("Ya existe " + Path.GetFileName(output) + ". Renómbralo o muévelo antes de volver a recortar; no se sobrescribe ningún vídeo.");
        }

        if (string.IsNullOrEmpty(Path.GetExtension(output)))
        {
            throw new InvalidDataException("El archivo necesita una extensión de contenedor (por ejemplo .mp4 o .mkv) para guardar el recorte en el mismo formato.");
        }

        string temporary = Path.Combine(
            Path.GetDirectoryName(output) ?? string.Empty,
            "." + Path.GetFileNameWithoutExtension(output) + "." + Guid.NewGuid().ToString("N") + ".partial" + Path.GetExtension(output));

        var watch = Stopwatch.StartNew();
        var parser = new ProgressParser(request.Duration);
        try
        {
            ProcessResult result;
            if (!request.LimitUnder200Mb)
            {
                result = await MediaProcess.RunAsync(
                    _ffmpeg,
                    request.BuildArguments(temporary),
                    cancellationToken,
                    line =>
                    {
                        CutProgress update = parser.Parse(line, watch.Elapsed);
                        if (update is not null)
                        {
                            progress?.Report(update);
                        }
                    }).ConfigureAwait(false);
            }
            else
            {
                result = await RunHighEfficiencyUnder200MbAsync(request, temporary, watch, parser, progress, cancellationToken).ConfigureAwait(false);
            }

            if (result.ExitCode != 0)
            {
                string errorTitle = request.LimitUnder200Mb
                    ? "FFmpeg no pudo exportar el vídeo reducido a menos de 200 MB."
                    : "FFmpeg no pudo guardar el recorte sin recodificar. El contenedor debe admitir todas las pistas del archivo.";
                throw new IOException(errorTitle + Environment.NewLine + result.Errors);
            }

            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0)
            {
                throw new IOException("El recorte no ha producido un archivo válido.");
            }

            await ProbeAsync(temporary, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: false);
            progress?.Report(new CutProgress(100, watch.Elapsed, TimeSpan.Zero));
            return output;
        }
        finally
        {
            // This exact temporary path belongs to this operation; never delete the source or a previous cut.
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task<ProcessResult> RunHighEfficiencyUnder200MbAsync(
        CutRequest request,
        string temporary,
        Stopwatch watch,
        ProgressParser parser,
        IProgress<CutProgress> progress,
        CancellationToken cancellationToken)
    {
        Action<string> onLine = line =>
        {
            CutProgress update = parser.Parse(line, watch.Elapsed);
            if (update is not null)
            {
                progress?.Report(update);
            }
        };

        string hwEncoder = SelectHardwareHevcEncoder(request.Video);
        if (!string.IsNullOrEmpty(hwEncoder))
        {
            if (string.Equals(hwEncoder, "hevc_nvenc", StringComparison.OrdinalIgnoreCase))
            {
                ProcessResult cudaRes = await MediaProcess.RunAsync(
                    _ffmpeg,
                    request.BuildArguments(temporary, hwEncoder, forceHighQualityAacAudio: false, useGpuHardwareDecode: true),
                    cancellationToken,
                    onLine).ConfigureAwait(false);

                if (cudaRes.ExitCode == 0 && File.Exists(temporary) && new FileInfo(temporary).Length > 0)
                {
                    return cudaRes;
                }

                DeleteTemporaryIfPresent(temporary);
            }

            ProcessResult hwRes = await MediaProcess.RunAsync(
                _ffmpeg,
                request.BuildArguments(temporary, hwEncoder, forceHighQualityAacAudio: false, useGpuHardwareDecode: false),
                cancellationToken,
                onLine).ConfigureAwait(false);

            if (hwRes.ExitCode == 0 && File.Exists(temporary) && new FileInfo(temporary).Length > 0)
            {
                return hwRes;
            }

            DeleteTemporaryIfPresent(temporary);
        }

        ProcessResult cpuRes = await MediaProcess.RunAsync(
            _ffmpeg,
            request.BuildArguments(temporary, "libx265", forceHighQualityAacAudio: false),
            cancellationToken,
            onLine).ConfigureAwait(false);

        if (cpuRes.ExitCode == 0 && File.Exists(temporary) && new FileInfo(temporary).Length > 0)
        {
            return cpuRes;
        }

        DeleteTemporaryIfPresent(temporary);

        return await MediaProcess.RunAsync(
            _ffmpeg,
            request.BuildArguments(temporary, "libx265", forceHighQualityAacAudio: true),
            cancellationToken,
            onLine).ConfigureAwait(false);
    }

    private static string SelectHardwareHevcEncoder(VideoInfo video)
    {
        if (video is null || (video.Width > 0 && video.Width < 160) || (video.Height > 0 && video.Height < 128))
        {
            return null;
        }

        try
        {
            IReadOnlyList<GpuInfo> gpus = GpuDetector.Detect();
            if (gpus.Count == 0)
            {
                return null;
            }

            return gpus[0].PreferredAccelerator switch
            {
                "cuda" => "hevc_nvenc",
                "amf" => "hevc_amf",
                "qsv" => "hevc_qsv",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteTemporaryIfPresent(string temporary)
    {
        try
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        catch
        {
        }
    }

    private void CheckTools()
    {
        if (!File.Exists(_ffmpeg) || !File.Exists(_ffprobe))
        {
            throw new FileNotFoundException("Faltan FFmpeg o FFprobe. Ejecuta scripts\\Setup-FFmpeg.ps1 y vuelve a compilar.");
        }
    }
}
