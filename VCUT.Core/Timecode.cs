using System.Globalization;
using System.Text.RegularExpressions;

namespace VCUT.Core;

public static class Timecode
{
    public static string Format(TimeSpan value)
    {
        return $"{(long)Math.Floor(value.TotalHours):00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }

    public static bool TryParse(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        Match match = Regex.Match(text.Trim(), @"^(\d{1,6}):([0-5]\d):([0-5]\d)(?:[.,](\d{1,3}))?$");
        if (!match.Success)
        {
            return false;
        }

        long hours = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        int minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        int milliseconds = match.Groups[4].Success
            ? int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture)
            : 0;

        value = TimeSpan.FromMilliseconds(((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds);
        return true;
    }
}

