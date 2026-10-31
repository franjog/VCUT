Imports System.Globalization

Public Enum OutputContainer
    Original
    Matroska
End Enum

Public NotInheritable Class CutRequest
    Public Sub New(video As VideoInfo, startTime As TimeSpan, endTime As TimeSpan, Optional outputContainer As OutputContainer = OutputContainer.Original)
        ArgumentNullException.ThrowIfNull(video)
        If startTime < TimeSpan.Zero Then Throw New ArgumentOutOfRangeException(NameOf(startTime), "El inicio no puede ser negativo.")
        If endTime <= startTime Then Throw New ArgumentException("La marca de fin debe ser posterior a la de inicio.")
        If endTime > video.Duration Then Throw New ArgumentOutOfRangeException(NameOf(endTime), "El fin supera la duración del vídeo.")
        Me.Video = video
        Me.StartTime = startTime
        Me.EndTime = endTime
        Me.Container = outputContainer
    End Sub

    Public ReadOnly Property Video As VideoInfo
    Public ReadOnly Property StartTime As TimeSpan
    Public ReadOnly Property EndTime As TimeSpan
    Public ReadOnly Property Container As OutputContainer
    Public ReadOnly Property Duration As TimeSpan
        Get
            Return EndTime - StartTime
        End Get
    End Property
    Public ReadOnly Property OutputPath As String
        Get
            Dim source = Video.FilePath
            Dim extension = If(Container = OutputContainer.Matroska, ".mkv", IO.Path.GetExtension(source))
            Return IO.Path.Combine(IO.Path.GetDirectoryName(source), IO.Path.GetFileNameWithoutExtension(source) & "_cut" & extension)
        End Get
    End Property

    Public Function BuildArguments(temporaryOutput As String) As IReadOnlyList(Of String)
        Return New String() {
            "-hide_banner", "-nostdin", "-v", "error", "-n",
            "-ss", StartTime.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-i", Video.FilePath,
            "-t", Duration.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture),
            "-map", "0", "-c", "copy", "-map_metadata", "0", "-map_chapters", "-1",
            "-avoid_negative_ts", "make_zero", "-progress", "pipe:1", "-nostats", temporaryOutput
        }
    End Function
End Class
