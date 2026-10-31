Imports System.IO
Imports System.Security.Cryptography
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports VCUT.Core

<TestClass>
Public Class VideoIntegrationTests
    Private Shared ReadOnly ToolsPath As String = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg")
    Private Shared ReadOnly Ffmpeg As String = Path.Combine(ToolsPath, "ffmpeg.exe")
    Private Shared ReadOnly Service As New VideoService(ToolsPath)
    Private _directory As String

    <TestInitialize>
    Public Sub Setup()
        Assert.IsTrue(File.Exists(Ffmpeg), "Ejecuta scripts\Setup-FFmpeg.ps1 antes de compilar.")
        _directory = Path.Combine(Path.GetTempPath(), "VCUT.Tests", Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(_directory)
    End Sub

    <TestCleanup>
    Public Sub Cleanup()
        If _directory IsNot Nothing AndAlso Directory.Exists(_directory) Then Directory.Delete(_directory, recursive:=True)
    End Sub

    Private Async Function CreateVideoAsync(extension As String, Optional withAudio As Boolean = True) As Task(Of String)
        Dim filePath = Path.Combine(_directory, "vídeo de prueba & original" & extension)
        Dim args As New List(Of String) From {"-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25:duration=6"}
        If withAudio Then args.AddRange({"-f", "lavfi", "-i", "sine=frequency=440:duration=6", "-map", "0:v", "-map", "1:a", "-map", "1:a"})
        Select Case extension
            Case ".webm"
                args.AddRange({"-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-g", "25", "-b:v", "250k"})
            Case ".avi"
                args.AddRange({"-c:v", "mpeg4", "-q:v", "3", "-g", "25", "-bf", "0"})
            Case Else
                args.AddRange({"-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-bf", "0", "-pix_fmt", "yuv420p"})
        End Select
        If withAudio Then args.AddRange({"-c:a", If(extension = ".webm", "libopus", If(extension = ".avi", "libmp3lame", "aac"))})
        args.Add(filePath)
        Dim result = Await MediaProcess.RunAsync(Ffmpeg, args, CancellationToken.None)
        Assert.AreEqual(0, result.ExitCode, result.Errors)
        Return filePath
    End Function

    <TestMethod>
    <DataRow(".mp4", True)>
    <DataRow(".mkv", True)>
    <DataRow(".mov", False)>
    <DataRow(".avi", True)>
    <DataRow(".webm", True)>
    <DataRow(".ts", True)>
    Public Async Function CutsRealVideoPreservesTracksAndPacketBytes(extension As String, withAudio As Boolean) As Task
        Dim source = Await CreateVideoAsync(extension, withAudio)
        Dim originalHash = SHA256.HashData(Await File.ReadAllBytesAsync(source))
        Dim info = Await Service.ProbeAsync(source, CancellationToken.None)
        Dim output = Await Service.CutAsync(New CutRequest(info, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)), Nothing, CancellationToken.None)
        Assert.AreEqual(Path.Combine(_directory, "vídeo de prueba & original_cut" & extension), output)
        Dim cut = Await Service.ProbeAsync(output, CancellationToken.None)
        Assert.AreEqual(info.Codec, cut.Codec)
        Assert.AreEqual(160, cut.Width)
        Assert.IsTrue(cut.Duration.TotalSeconds >= 1.9 AndAlso cut.Duration.TotalSeconds < 3.2, $"Duración inesperada: {cut.Duration}")
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(Await File.ReadAllBytesAsync(source)))
        Dim probe = Await MediaProcess.RunAsync(Path.Combine(ToolsPath, "ffprobe.exe"), {"-v", "error", "-show_streams", "-of", "json", output}, CancellationToken.None)
        Using doc = JsonDocument.Parse(probe.Output)
            Assert.AreEqual(If(withAudio, 3, 1), doc.RootElement.GetProperty("streams").GetArrayLength())
        End Using
        Dim sourcePackets = Await PacketHashesAsync(source)
        Dim cutPackets = Await PacketHashesAsync(output)
        Assert.IsTrue(cutPackets.Count > 0)
        Assert.IsTrue(cutPackets.All(Function(hash) sourcePackets.Contains(hash)), "Los paquetes del vídeo recortado deben conservar los bytes originales, sin recodificar.")
        Dim decode = Await MediaProcess.RunAsync(Ffmpeg, {"-v", "error", "-xerror", "-i", output, "-f", "null", "-"}, CancellationToken.None)
        Assert.AreEqual(0, decode.ExitCode, decode.Errors)
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length)
    End Function

    Private Shared Async Function PacketHashesAsync(filePath As String) As Task(Of HashSet(Of String))
        Dim result = Await MediaProcess.RunAsync(Path.Combine(ToolsPath, "ffprobe.exe"), {"-v", "error", "-select_streams", "v:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", filePath}, CancellationToken.None)
        Assert.AreEqual(0, result.ExitCode, result.Errors)
        Using doc = JsonDocument.Parse(result.Output)
            Return doc.RootElement.GetProperty("packets").EnumerateArray().Select(Function(packet) packet.GetProperty("data_hash").GetString()).ToHashSet()
        End Using
    End Function

    <TestMethod>
    Public Async Function ExistingCutIsNeverOverwritten() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim request As New CutRequest(Await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(1))
        Await File.WriteAllTextAsync(request.OutputPath, "contenido existente")
        Await Assert.ThrowsExactlyAsync(Of IOException)(Function() Service.CutAsync(request, Nothing, CancellationToken.None))
        Assert.AreEqual("contenido existente", Await File.ReadAllTextAsync(request.OutputPath))
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length)
    End Function

    <TestMethod>
    Public Async Function CancelledCutDoesNotCreateOutput() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim request As New CutRequest(Await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(1))
        Using cancellation As New CancellationTokenSource()
            cancellation.Cancel()
            Await Assert.ThrowsAsync(Of OperationCanceledException)(Function() Service.CutAsync(request, Nothing, cancellation.Token))
        End Using
        Assert.IsFalse(File.Exists(request.OutputPath))
    End Function

    <TestMethod>
    Public Async Function CancellationTerminatesAnActiveProcess() As Task
        Using cancellation As New CancellationTokenSource(TimeSpan.FromMilliseconds(300))
            Dim watch = Diagnostics.Stopwatch.StartNew()
            Await Assert.ThrowsAsync(Of OperationCanceledException)(Function() MediaProcess.RunAsync(Ffmpeg, {"-v", "error", "-re", "-f", "lavfi", "-i", "color=duration=60", "-f", "null", "-"}, cancellation.Token))
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10))
        End Using
    End Function

    <TestMethod>
    Public Async Function CancellationAfterOutputStartedCleansPartialFile() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim request As New CutRequest(Await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(5))
        Using cancellation As New CancellationTokenSource()
            Dim progress As New InlineProgress(Sub(update) cancellation.Cancel())
            Await Assert.ThrowsAsync(Of OperationCanceledException)(Function() Service.CutAsync(request, progress, cancellation.Token))
        End Using
        Assert.IsTrue(File.Exists(source))
        Assert.IsFalse(File.Exists(request.OutputPath))
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length)
    End Function

    <TestMethod>
    Public Async Function CutBetweenKeyframesIsDecodableAndApproximate() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim info = Await Service.ProbeAsync(source, CancellationToken.None)
        Dim request As New CutRequest(info, TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(3.75))
        Dim output = Await Service.CutAsync(request, Nothing, CancellationToken.None)
        Dim cut = Await Service.ProbeAsync(output, CancellationToken.None)
        Assert.IsTrue(cut.Duration.TotalSeconds >= request.Duration.TotalSeconds)
        Assert.IsTrue(cut.Duration.TotalSeconds < request.Duration.TotalSeconds + 1.2)
        Dim decoded = Await MediaProcess.RunAsync(Ffmpeg, {"-v", "error", "-xerror", "-i", output, "-f", "null", "-"}, CancellationToken.None)
        Assert.AreEqual(0, decoded.ExitCode, decoded.Errors)
    End Function

    Private NotInheritable Class InlineProgress
        Implements IProgress(Of CutProgress)
        Private ReadOnly _callback As Action(Of CutProgress)
        Public Sub New(callback As Action(Of CutProgress))
            _callback = callback
        End Sub
        Public Sub Report(value As CutProgress) Implements IProgress(Of CutProgress).Report
            _callback(value)
        End Sub
    End Class

    <TestMethod>
    Public Async Function InvalidMediaIsRejected() As Task
        Dim filePath = Path.Combine(_directory, "invalid.mp4")
        Await File.WriteAllTextAsync(filePath, "This is not a video")
        Await Assert.ThrowsExactlyAsync(Of InvalidDataException)(Function() Service.ProbeAsync(filePath, CancellationToken.None))
    End Function

    <TestMethod>
    Public Async Function FailedCutCleansTemporaryFile() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim incompatible = Path.ChangeExtension(source, ".webm")
        File.Move(source, incompatible)
        Dim request As New CutRequest(Await Service.ProbeAsync(incompatible, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(2))
        Await Assert.ThrowsExactlyAsync(Of IOException)(Function() Service.CutAsync(request, Nothing, CancellationToken.None))
        Assert.IsFalse(File.Exists(request.OutputPath))
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length)
    End Function

    <TestMethod>
    Public Async Function MatroskaAlternativeAcceptsTracksRejectedByOriginalExtensionWithoutRecoding() As Task
        Dim source = Await CreateVideoAsync(".mp4")
        Dim incompatible = Path.ChangeExtension(source, ".webm")
        File.Move(source, incompatible)
        Dim originalHash = SHA256.HashData(Await File.ReadAllBytesAsync(incompatible))
        Dim info = Await Service.ProbeAsync(incompatible, CancellationToken.None)
        Dim request As New CutRequest(info, TimeSpan.Zero, TimeSpan.FromSeconds(2), OutputContainer.Matroska)
        Dim output = Await Service.CutAsync(request, Nothing, CancellationToken.None)

        Assert.AreEqual(Path.Combine(_directory, "vídeo de prueba & original_cut.mkv"), output)
        Assert.IsTrue(File.Exists(output))
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(Await File.ReadAllBytesAsync(incompatible)))
        Dim cut = Await Service.ProbeAsync(output, CancellationToken.None)
        Assert.AreEqual(info.Codec, cut.Codec)
        Dim probe = Await MediaProcess.RunAsync(Path.Combine(ToolsPath, "ffprobe.exe"), {"-v", "error", "-show_streams", "-of", "json", output}, CancellationToken.None)
        Using doc = JsonDocument.Parse(probe.Output)
            Assert.AreEqual(3, doc.RootElement.GetProperty("streams").GetArrayLength())
        End Using
        Dim sourcePackets = Await PacketHashesAsync(incompatible)
        Dim cutPackets = Await PacketHashesAsync(output)
        Assert.IsTrue(cutPackets.Count > 0)
        Assert.IsTrue(cutPackets.All(Function(hash) sourcePackets.Contains(hash)), "MKV debe conservar los paquetes originales sin recodificar.")
    End Function
End Class
