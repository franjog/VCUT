using System.Globalization;

namespace VCUT.Core;

public sealed class CutProgress
{
    public CutProgress(double percent, TimeSpan elapsed, TimeSpan? remaining)
    {
        Percent = percent;
        Elapsed = elapsed;
        Remaining = remaining;
    }

    public double Percent { get; }
    public TimeSpan Elapsed { get; }
    public TimeSpan? Remaining { get; }
}

public sealed class ProgressParser
{
    private readonly TimeSpan _duration;
    private double _processed;

    public ProgressParser(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _duration = duration;
    }

    public CutProgress Parse(string line, TimeSpan elapsed)
    {
        if (line is null)
        {
            return null;
        }

        if (line.StartsWith("out_time_us=", StringComparison.Ordinal))
        {
            if (long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long microseconds))
            {
                _processed = Math.Max(_processed, Math.Clamp(microseconds / 1000000.0, 0, _duration.TotalSeconds));
            }
        }

        if (!line.StartsWith("progress=", StringComparison.Ordinal))
        {
            return null;
        }

        double fraction = _processed / _duration.TotalSeconds;
        TimeSpan? remaining = null;
        if (_processed > 0.1 && elapsed.TotalSeconds >= 0.5)
        {
            double estimate = elapsed.TotalSeconds * (1 - fraction) / fraction;
            remaining = TimeSpan.FromSeconds(Math.Min(estimate, TimeSpan.MaxValue.TotalSeconds / 2));
        }

        // 100% is reserved for the verified file being committed to its final name.
        return new CutProgress(Math.Min(fraction * 100, 99.9), elapsed, remaining);
    }
}

