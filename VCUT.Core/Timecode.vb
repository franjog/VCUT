Imports System.Globalization
Imports System.Text.RegularExpressions

Public NotInheritable Class Timecode
    Public Shared Function Format(value As TimeSpan) As String
        Return $"{CLng(Math.Floor(value.TotalHours)):00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}"
    End Function

    Public Shared Function TryParse(text As String, ByRef value As TimeSpan) As Boolean
        value = TimeSpan.Zero
        If String.IsNullOrWhiteSpace(text) Then Return False
        Dim match = Regex.Match(text.Trim(), "^(\d{1,6}):([0-5]\d):([0-5]\d)(?:[.,](\d{1,3}))?$")
        If Not match.Success Then Return False
        Dim hours = Long.Parse(match.Groups(1).Value, CultureInfo.InvariantCulture)
        Dim minutes = Integer.Parse(match.Groups(2).Value, CultureInfo.InvariantCulture)
        Dim seconds = Integer.Parse(match.Groups(3).Value, CultureInfo.InvariantCulture)
        Dim milliseconds = If(match.Groups(4).Success, Integer.Parse(match.Groups(4).Value.PadRight(3, "0"c), CultureInfo.InvariantCulture), 0)
        value = TimeSpan.FromMilliseconds(((hours * 60 + minutes) * 60 + seconds) * 1000 + milliseconds)
        Return True
    End Function
End Class
