Imports System.Globalization
Imports System.IO
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports VCUT.Core

<Assembly: DoNotParallelize>

<TestClass>
Public Class CoreTests
    Private Shared Function Video() As VideoInfo
        Return New VideoInfo(Path.Combine(Path.GetTempPath(), "vídeo de prueba.mp4"), TimeSpan.FromSeconds(120), "h264", 1920, 1080)
    End Function

    <TestMethod>
    Public Sub OutputStaysBesideSourceAndPreservesExtension()
        Dim request As New CutRequest(Video(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20))
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "vídeo de prueba_cut.mp4"), request.OutputPath)
        Assert.AreEqual(TimeSpan.FromSeconds(15), request.Duration)
    End Sub

    <TestMethod>
    Public Sub MatroskaOutputStaysBesideSourceAndUsesMkvExtension()
        Dim request As New CutRequest(Video(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), OutputContainer.Matroska)
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "vídeo de prueba_cut.mkv"), request.OutputPath)
        Assert.AreEqual(OutputContainer.Matroska, request.Container)
        Assert.AreEqual("copy", request.BuildArguments(request.OutputPath)(request.BuildArguments(request.OutputPath).ToList().IndexOf("-c") + 1))
    End Sub

    <TestMethod>
    <DataRow("NVIDIA GeForce RTX 4070", True)>
    <DataRow("AMD Radeon RX 7800 XT", True)>
    <DataRow("Intel(R) Arc(TM) A770", True)>
    <DataRow("Microsoft Basic Display Adapter", False)>
    <DataRow("Microsoft Remote Display Adapter", False)>
    <DataRow("Indirect Display Driver", False)>
    Public Sub GpuDetectorFiltersSoftwareAndRemoteAdapters(adapterName As String, expected As Boolean)
        Assert.AreEqual(expected, GpuDetector.IsHardwareAdapterName(adapterName))
    End Sub

    <TestMethod>
    <DataRow(-1.0, 5.0)>
    <DataRow(5.0, 5.0)>
    <DataRow(10.0, 5.0)>
    <DataRow(0.0, 121.0)>
    Public Sub InvalidMarksAreRejected(startSeconds As Double, endSeconds As Double)
        Assert.Throws(Of ArgumentException)(Function() New CutRequest(Video(), TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)))
    End Sub

    <TestMethod>
    Public Sub WholeVideoIsAValidRange()
        Dim request As New CutRequest(Video(), TimeSpan.Zero, Video().Duration)
        Assert.AreEqual(120.0, request.Duration.TotalSeconds)
    End Sub

    <TestMethod>
    Public Sub ArgumentsUseStreamCopyAllTracksAndInvariantDecimals()
        Dim previous = CultureInfo.CurrentCulture
        Try
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES")
            Dim request As New CutRequest(Video(), TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(4.75))
            Dim args = request.BuildArguments("C:\salida con espacios\temporal.mp4").ToList()
            Assert.AreEqual("copy", args(args.IndexOf("-c") + 1))
            Assert.AreEqual("0", args(args.IndexOf("-map") + 1))
            Assert.AreEqual("1.25", args(args.IndexOf("-ss") + 1))
            Assert.AreEqual("3.5", args(args.IndexOf("-t") + 1))
            Assert.AreEqual(Video().FilePath, args(args.IndexOf("-i") + 1))
            Assert.IsTrue(args.IndexOf("-ss") < args.IndexOf("-i"))
            Assert.AreEqual("C:\salida con espacios\temporal.mp4", args.Last())
        Finally
            CultureInfo.CurrentCulture = previous
        End Try
    End Sub

    <TestMethod>
    <DataRow("00:00:00.000", 0.0)>
    <DataRow("01:02:03.456", 3723.456)>
    <DataRow("00:00:01,5", 1.5)>
    <DataRow("25:00:00", 90000.0)>
    <DataRow("00:00:01.05", 1.05)>
    Public Sub TimecodesSupportMillisecondsAndLongVideos(text As String, seconds As Double)
        Dim value As TimeSpan
        Assert.IsTrue(Timecode.TryParse(text, value))
        Assert.AreEqual(seconds, value.TotalSeconds, 0.000001)
        Dim roundTrip As TimeSpan
        Assert.IsTrue(Timecode.TryParse(Timecode.Format(value), roundTrip))
        Assert.AreEqual(value, roundTrip)
    End Sub

    <TestMethod>
    <DataRow("")>
    <DataRow("-00:00:01")>
    <DataRow("00:60:00")>
    <DataRow("00:00:60")>
    <DataRow("garbage")>
    <DataRow("00:00:01.1234")>
    <DataRow("9999999:00:00")>
    Public Sub InvalidTimecodesAreRejected(text As String)
        Dim value As TimeSpan
        Assert.IsFalse(Timecode.TryParse(text, value))
    End Sub

    <TestMethod>
    Public Sub ProbeReadsVideoAndInvariantDuration()
        Dim json = "{""streams"":[{""codec_type"":""audio""},{""codec_type"":""video"",""codec_name"":""hevc"",""width"":3840,""height"":2160}],""format"":{""duration"":""12.500""}}"
        Dim info = VideoInfo.FromProbeJson(Video().FilePath, json)
        Assert.AreEqual(12.5, info.Duration.TotalSeconds)
        Assert.AreEqual("hevc", info.Codec)
        Assert.AreEqual(3840, info.Width)
    End Sub

    <TestMethod>
    Public Sub ProbeFallsBackToStreamDuration()
        Dim json = "{""streams"":[{""codec_type"":""video"",""duration"":""7.25""}],""format"":{""duration"":""N/A""}}"
        Assert.AreEqual(7.25, VideoInfo.FromProbeJson(Video().FilePath, json).Duration.TotalSeconds)
    End Sub

    <TestMethod>
    <DataRow("{""streams"":[{""codec_type"":""audio""}],""format"":{""duration"":""12""}}")>
    <DataRow("{""streams"":[{""codec_type"":""video"",""disposition"":{""attached_pic"":1}}],""format"":{""duration"":""12""}}")>
    <DataRow("{""streams"":[{""codec_type"":""video""}],""format"":{""duration"":""NaN""}}")>
    <DataRow("{""streams"":[{""codec_type"":""video""}],""format"":{""duration"":""0""}}")>
    Public Sub ProbeRejectsAudioArtworkAndUnknownDuration(json As String)
        Assert.ThrowsExactly(Of InvalidDataException)(Function() VideoInfo.FromProbeJson(Video().FilePath, json))
    End Sub

    <TestMethod>
    Public Sub ProgressEstimatesRemainingFromProcessedTime()
        Dim parser As New ProgressParser(TimeSpan.FromSeconds(100))
        Assert.IsNull(parser.Parse("out_time_us=25000000", TimeSpan.FromSeconds(5)))
        Dim update = parser.Parse("progress=continue", TimeSpan.FromSeconds(5))
        Assert.AreEqual(25.0, update.Percent)
        Assert.AreEqual(TimeSpan.FromSeconds(15), update.Remaining.Value)
    End Sub

    <TestMethod>
    Public Sub ProgressHandlesInvalidNegativeAndExcessTimestamps()
        Dim parser As New ProgressParser(TimeSpan.FromSeconds(10))
        parser.Parse("out_time_us=N/A", TimeSpan.Zero)
        parser.Parse("out_time_us=-1000000", TimeSpan.Zero)
        Dim initial = parser.Parse("progress=continue", TimeSpan.Zero)
        Assert.AreEqual(0.0, initial.Percent)
        Assert.IsFalse(initial.Remaining.HasValue)
        parser.Parse("out_time_us=20000000", TimeSpan.FromSeconds(1))
        Dim final = parser.Parse("progress=end", TimeSpan.FromSeconds(1))
        Assert.AreEqual(99.9, final.Percent)
        Assert.AreEqual(TimeSpan.Zero, final.Remaining.Value)
    End Sub
End Class
