using System.Globalization;

namespace VCUT.Core;

public enum OutputContainer
{
    Original,
    Matroska
}

public sealed class CutRequest
{
    public CutRequest(VideoInfo video, TimeSpan startTime, TimeSpan endTime, OutputContainer outputContainer = OutputContainer.Original)
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
    }

    public VideoInfo Video { get; }
    public TimeSpan StartTime { get; }
    public TimeSpan EndTime { get; }
    public OutputContainer Container { get; }
    public TimeSpan Duration => EndTime - StartTime;

    public string OutputPath
    {
        get
        {
            string source = Video.FilePath;
            string extension = Container == OutputContainer.Matroska ? ".mkv" : Path.GetExtension(source);
            return Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, Path.GetFileNameWithoutExtension(source) + "_cut" + extension);
        }
    }

    public IReadOnlyList<string> BuildArguments(string temporaryOutput)
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
}

