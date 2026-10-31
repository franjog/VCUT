Imports System.Globalization

Public NotInheritable Class CutProgress
    Public Sub New(percent As Double, elapsed As TimeSpan, remaining As TimeSpan?)
        Me.Percent = percent
        Me.Elapsed = elapsed
        Me.Remaining = remaining
    End Sub
    Public ReadOnly Property Percent As Double
    Public ReadOnly Property Elapsed As TimeSpan
    Public ReadOnly Property Remaining As TimeSpan?
End Class

Public NotInheritable Class ProgressParser
    Private ReadOnly _duration As TimeSpan
    Private _processed As Double

    Public Sub New(duration As TimeSpan)
        If duration <= TimeSpan.Zero Then Throw New ArgumentOutOfRangeException(NameOf(duration))
        _duration = duration
    End Sub

    Public Function Parse(line As String, elapsed As TimeSpan) As CutProgress
        If line Is Nothing Then Return Nothing
        If line.StartsWith("out_time_us=", StringComparison.Ordinal) Then
            Dim microseconds As Long
            If Long.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, microseconds) Then
                _processed = Math.Max(_processed, Math.Clamp(microseconds / 1000000.0, 0, _duration.TotalSeconds))
            End If
        End If
        If Not line.StartsWith("progress=", StringComparison.Ordinal) Then Return Nothing
        Dim fraction = _processed / _duration.TotalSeconds
        Dim remaining As TimeSpan? = Nothing
        If _processed > 0.1 AndAlso elapsed.TotalSeconds >= 0.5 Then
            Dim estimate = elapsed.TotalSeconds * (1 - fraction) / fraction
            remaining = TimeSpan.FromSeconds(Math.Min(estimate, TimeSpan.MaxValue.TotalSeconds / 2))
        End If
        ' 100% is reserved for the verified file being committed to its final name.
        Return New CutProgress(Math.Min(fraction * 100, 99.9), elapsed, remaining)
    End Function
End Class
