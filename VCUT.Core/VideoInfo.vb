Imports System.Globalization
Imports System.IO
Imports System.Text.Json

Public NotInheritable Class VideoInfo
    Public Sub New(filePath As String, duration As TimeSpan, codec As String, width As Integer, height As Integer, Optional audioCodec As String = "", Optional fileSize As Long = 0)
        Me.FilePath = IO.Path.GetFullPath(filePath)
        Me.Duration = duration
        Me.Codec = codec
        Me.Width = width
        Me.Height = height
        Me.AudioCodec = If(audioCodec, String.Empty)
        Me.FileSize = If(fileSize > 0, fileSize, If(File.Exists(filePath), (New FileInfo(filePath)).Length, 0L))
    End Sub

    Public ReadOnly Property FilePath As String
    Public ReadOnly Property Duration As TimeSpan
    Public ReadOnly Property Codec As String
    Public ReadOnly Property Width As Integer
    Public ReadOnly Property Height As Integer
    Public ReadOnly Property AudioCodec As String
    Public ReadOnly Property FileSize As Long

    Public Shared Function FromProbeJson(filePath As String, json As String) As VideoInfo
        Using document = JsonDocument.Parse(json)
            Dim root = document.RootElement
            Dim streams As JsonElement
            If Not root.TryGetProperty("streams", streams) Then Throw New InvalidDataException("El archivo no contiene pistas de vídeo.")
            Dim selected As JsonElement = Nothing
            Dim found = False
            Dim longestDuration As Double = 0
            Dim audioCodecName As String = String.Empty
            For Each stream In streams.EnumerateArray()
                Dim kind As JsonElement
                If stream.TryGetProperty("codec_type", kind) Then
                    Dim kindText = kind.GetString()
                    If kindText = "audio" AndAlso String.IsNullOrEmpty(audioCodecName) Then
                        Dim aCodec As JsonElement
                        If stream.TryGetProperty("codec_name", aCodec) Then audioCodecName = aCodec.GetString()
                    ElseIf kindText = "video" Then
                        Dim disposition As JsonElement
                        Dim picture As JsonElement
                        If stream.TryGetProperty("disposition", disposition) AndAlso disposition.TryGetProperty("attached_pic", picture) AndAlso picture.GetInt32() = 1 Then Continue For
                        If Not found Then
                            selected = stream
                            found = True
                        End If
                        longestDuration = Math.Max(longestDuration, ReadSeconds(stream))
                    End If
                End If
            Next
            If Not found Then Throw New InvalidDataException("El archivo no contiene vídeo reproducible (puede ser audio o una carátula).")
            Dim format As JsonElement
            Dim seconds As Double = 0
            Dim sizeBytes As Long = 0
            If root.TryGetProperty("format", format) Then
                seconds = ReadSeconds(format)
                Dim sizeElem As JsonElement
                If format.TryGetProperty("size", sizeElem) Then Long.TryParse(sizeElem.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, sizeBytes)
            End If
            If seconds <= 0 Then seconds = longestDuration
            If seconds <= 0 OrElse seconds > TimeSpan.MaxValue.TotalSeconds Then Throw New InvalidDataException("No se puede determinar la duración de este vídeo. Puede estar incompleto o ser un flujo sin duración.")
            Dim codec As JsonElement
            Dim width As JsonElement
            Dim height As JsonElement
            Return New VideoInfo(filePath, TimeSpan.FromSeconds(seconds),
                                 If(selected.TryGetProperty("codec_name", codec), codec.GetString(), "desconocido"),
                                 If(selected.TryGetProperty("width", width), width.GetInt32(), 0),
                                 If(selected.TryGetProperty("height", height), height.GetInt32(), 0),
                                 audioCodecName, sizeBytes)
        End Using
    End Function

    Private Shared Function ReadSeconds(element As JsonElement) As Double
        Dim value As JsonElement
        Dim seconds As Double
        If element.TryGetProperty("duration", value) AndAlso Double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, seconds) AndAlso Double.IsFinite(seconds) AndAlso seconds > 0 Then Return seconds
        Return 0
    End Function
End Class
