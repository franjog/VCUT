Imports System.Diagnostics
Imports System.IO
Imports System.Threading

Public NotInheritable Class VideoService
    Private ReadOnly _ffmpeg As String
    Private ReadOnly _ffprobe As String

    Public Sub New(toolsDirectory As String)
        _ffmpeg = Path.Combine(toolsDirectory, "ffmpeg.exe")
        _ffprobe = Path.Combine(toolsDirectory, "ffprobe.exe")
    End Sub

    Public Async Function ProbeAsync(filePath As String, cancellationToken As CancellationToken, Optional progress As IProgress(Of Double) = Nothing) As Task(Of VideoInfo)
        CheckTools()
        filePath = Path.GetFullPath(filePath)
        If Not File.Exists(filePath) Then Throw New FileNotFoundException("No se encuentra el archivo de vídeo.", filePath)
        progress?.Report(0.15)
        Dim result = Await MediaProcess.RunAsync(_ffprobe, {"-hide_banner", "-v", "error", "-show_format", "-show_streams", "-of", "json", filePath}, cancellationToken).ConfigureAwait(False)
        progress?.Report(0.85)
        If result.ExitCode <> 0 Then Throw New InvalidDataException("No se puede abrir este archivo con FFprobe." & Environment.NewLine & result.Errors)
        Dim info = VideoInfo.FromProbeJson(filePath, result.Output)
        progress?.Report(1.0)
        Return info
    End Function

    Public Async Function CutAsync(request As CutRequest, progress As IProgress(Of CutProgress), cancellationToken As CancellationToken) As Task(Of String)
        ArgumentNullException.ThrowIfNull(request)
        CheckTools()
        cancellationToken.ThrowIfCancellationRequested()
        If Not File.Exists(request.Video.FilePath) Then Throw New FileNotFoundException("El vídeo de origen ya no está disponible.", request.Video.FilePath)
        Dim output = request.OutputPath
        If File.Exists(output) Then Throw New IOException("Ya existe " & Path.GetFileName(output) & ". Renómbralo o muévelo antes de volver a recortar; no se sobrescribe ningún vídeo.")
        If String.IsNullOrEmpty(Path.GetExtension(output)) Then Throw New InvalidDataException("El archivo necesita una extensión de contenedor (por ejemplo .mp4 o .mkv) para guardar el recorte en el mismo formato.")
        Dim temporary = Path.Combine(Path.GetDirectoryName(output), "." & Path.GetFileNameWithoutExtension(output) & "." & Guid.NewGuid().ToString("N") & ".partial" & Path.GetExtension(output))
        Dim watch = Stopwatch.StartNew()
        Dim parser As New ProgressParser(request.Duration)
        Try
            Dim result = Await MediaProcess.RunAsync(_ffmpeg, request.BuildArguments(temporary), cancellationToken,
                Sub(line)
                    Dim update = parser.Parse(line, watch.Elapsed)
                    If update IsNot Nothing Then progress?.Report(update)
                End Sub).ConfigureAwait(False)
            If result.ExitCode <> 0 Then Throw New IOException("FFmpeg no pudo guardar el recorte sin recodificar. El contenedor debe admitir todas las pistas del archivo." & Environment.NewLine & result.Errors)
            If Not File.Exists(temporary) OrElse New FileInfo(temporary).Length = 0 Then Throw New IOException("El recorte no ha producido un archivo válido.")
            Await ProbeAsync(temporary, cancellationToken).ConfigureAwait(False)
            cancellationToken.ThrowIfCancellationRequested()
            File.Move(temporary, output, overwrite:=False)
            progress?.Report(New CutProgress(100, watch.Elapsed, TimeSpan.Zero))
            Return output
        Finally
            ' This exact temporary path belongs to this operation; never delete the source or a previous cut.
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Function

    Private Sub CheckTools()
        If Not File.Exists(_ffmpeg) OrElse Not File.Exists(_ffprobe) Then Throw New FileNotFoundException("Faltan FFmpeg o FFprobe. Ejecuta scripts\Setup-FFmpeg.ps1 y vuelve a compilar.")
    End Sub
End Class
