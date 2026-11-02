using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VCUT.Core;

[assembly: DoNotParallelize]

namespace VCUT.Tests;

[TestClass]
public class CoreTests
{
    private static VideoInfo Video()
    {
        return new VideoInfo(Path.Combine(Path.GetTempPath(), "vídeo de prueba.mp4"), TimeSpan.FromSeconds(120), "h264", 1920, 1080);
    }

    [TestMethod]
    public void OutputStaysBesideSourceAndPreservesExtension()
    {
        var request = new CutRequest(Video(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20));
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "vídeo de prueba_cut.mp4"), request.OutputPath);
        Assert.AreEqual(TimeSpan.FromSeconds(15), request.Duration);
    }

    [TestMethod]
    public void MatroskaOutputStaysBesideSourceAndUsesMkvExtension()
    {
        var request = new CutRequest(Video(), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20), OutputContainer.Matroska);
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "vídeo de prueba_cut.mkv"), request.OutputPath);
        Assert.AreEqual(OutputContainer.Matroska, request.Container);
        var args = request.BuildArguments(request.OutputPath).ToList();
        Assert.AreEqual("copy", args[args.IndexOf("-c") + 1]);
    }

    [TestMethod]
    [DataRow("NVIDIA GeForce RTX 4070", true)]
    [DataRow("AMD Radeon RX 7800 XT", true)]
    [DataRow("Intel(R) Arc(TM) A770", true)]
    [DataRow("Microsoft Basic Display Adapter", false)]
    [DataRow("Microsoft Remote Display Adapter", false)]
    [DataRow("Indirect Display Driver", false)]
    public void GpuDetectorFiltersSoftwareAndRemoteAdapters(string adapterName, bool expected)
    {
        Assert.AreEqual(expected, GpuDetector.IsHardwareAdapterName(adapterName));
    }

    [TestMethod]
    [DataRow(-1.0, 5.0)]
    [DataRow(5.0, 5.0)]
    [DataRow(10.0, 5.0)]
    [DataRow(0.0, 121.0)]
    public void InvalidMarksAreRejected(double startSeconds, double endSeconds)
    {
        Assert.Throws<ArgumentException>(() => new CutRequest(Video(), TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds)));
    }

    [TestMethod]
    public void WholeVideoIsAValidRange()
    {
        var request = new CutRequest(Video(), TimeSpan.Zero, Video().Duration);
        Assert.AreEqual(120.0, request.Duration.TotalSeconds);
    }

    [TestMethod]
    public void ArgumentsUseStreamCopyAllTracksAndInvariantDecimals()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var request = new CutRequest(Video(), TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(4.75));
            var args = request.BuildArguments(@"C:\salida con espacios\temporal.mp4").ToList();
            Assert.AreEqual("copy", args[args.IndexOf("-c") + 1]);
            Assert.AreEqual("0", args[args.IndexOf("-map") + 1]);
            Assert.AreEqual("1.25", args[args.IndexOf("-ss") + 1]);
            Assert.AreEqual("3.5", args[args.IndexOf("-t") + 1]);
            Assert.AreEqual(Video().FilePath, args[args.IndexOf("-i") + 1]);
            Assert.IsTrue(args.IndexOf("-ss") < args.IndexOf("-i"));
            Assert.AreEqual(@"C:\salida con espacios\temporal.mp4", args.Last());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestMethod]
    [DataRow("00:00:00.000", 0.0)]
    [DataRow("01:02:03.456", 3723.456)]
    [DataRow("00:00:01,5", 1.5)]
    [DataRow("25:00:00", 90000.0)]
    [DataRow("00:00:01.05", 1.05)]
    public void TimecodesSupportMillisecondsAndLongVideos(string text, double seconds)
    {
        Assert.IsTrue(Timecode.TryParse(text, out TimeSpan value));
        Assert.AreEqual(seconds, value.TotalSeconds, 0.000001);
        Assert.IsTrue(Timecode.TryParse(Timecode.Format(value), out TimeSpan roundTrip));
        Assert.AreEqual(value, roundTrip);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-00:00:01")]
    [DataRow("00:60:00")]
    [DataRow("00:00:60")]
    [DataRow("garbage")]
    [DataRow("00:00:01.1234")]
    [DataRow("9999999:00:00")]
    public void InvalidTimecodesAreRejected(string text)
    {
        Assert.IsFalse(Timecode.TryParse(text, out _));
    }

    [TestMethod]
    public void ProbeReadsVideoAndInvariantDuration()
    {
        const string json = @"{""streams"":[{""codec_type"":""audio""},{""codec_type"":""video"",""codec_name"":""hevc"",""width"":3840,""height"":2160}],""format"":{""duration"":""12.500""}}";
        VideoInfo info = VideoInfo.FromProbeJson(Video().FilePath, json);
        Assert.AreEqual(12.5, info.Duration.TotalSeconds);
        Assert.AreEqual("hevc", info.Codec);
        Assert.AreEqual(3840, info.Width);
    }

    [TestMethod]
    public void ProbeFallsBackToStreamDuration()
    {
        const string json = @"{""streams"":[{""codec_type"":""video"",""duration"":""7.25""}],""format"":{""duration"":""N/A""}}";
        Assert.AreEqual(7.25, VideoInfo.FromProbeJson(Video().FilePath, json).Duration.TotalSeconds);
    }

    [TestMethod]
    [DataRow(@"{""streams"":[{""codec_type"":""audio""}],""format"":{""duration"":""12""}}")]
    [DataRow(@"{""streams"":[{""codec_type"":""video"",""disposition"":{""attached_pic"":1}}],""format"":{""duration"":""12""}}")]
    [DataRow(@"{""streams"":[{""codec_type"":""video""}],""format"":{""duration"":""NaN""}}")]
    [DataRow(@"{""streams"":[{""codec_type"":""video""}],""format"":{""duration"":""0""}}")]
    public void ProbeRejectsAudioArtworkAndUnknownDuration(string json)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => VideoInfo.FromProbeJson(Video().FilePath, json));
    }

    [TestMethod]
    public void ProgressEstimatesRemainingFromProcessedTime()
    {
        var parser = new ProgressParser(TimeSpan.FromSeconds(100));
        Assert.IsNull(parser.Parse("out_time_us=25000000", TimeSpan.FromSeconds(5)));
        CutProgress update = parser.Parse("progress=continue", TimeSpan.FromSeconds(5));
        Assert.AreEqual(25.0, update.Percent);
        Assert.AreEqual(TimeSpan.FromSeconds(15), update.Remaining.Value);
    }

    [TestMethod]
    public void ProgressHandlesInvalidNegativeAndExcessTimestamps()
    {
        var parser = new ProgressParser(TimeSpan.FromSeconds(10));
        parser.Parse("out_time_us=N/A", TimeSpan.Zero);
        parser.Parse("out_time_us=-1000000", TimeSpan.Zero);
        CutProgress initial = parser.Parse("progress=continue", TimeSpan.Zero);
        Assert.AreEqual(0.0, initial.Percent);
        Assert.IsFalse(initial.Remaining.HasValue);
        parser.Parse("out_time_us=20000000", TimeSpan.FromSeconds(1));
        CutProgress final = parser.Parse("progress=end", TimeSpan.FromSeconds(1));
        Assert.AreEqual(99.9, final.Percent);
        Assert.AreEqual(TimeSpan.Zero, final.Remaining.Value);
    }

    [TestMethod]
    [DataRow("NVIDIA GeForce RTX 5050 Laptop GPU", 8151L, "NVIDIA", "cuda", true)]
    [DataRow("AMD Radeon RX 7800 XT", 16384L, "AMD", "amf", true)]
    [DataRow("Intel(R) Arc(TM) A770", 8192L, "Intel", "qsv", true)]
    [DataRow("Intel(R) Graphics", 128L, "Intel", "qsv", false)]
    public void GpuInfoClassifiesVendorAcceleratorAndDiscretePriority(string adapterName, long vramMb, string expectedVendor, string expectedAccelerator, bool expectedDiscrete)
    {
        var info = new GpuInfo(adapterName, "0000", false, vramMb);
        Assert.AreEqual(expectedVendor, info.Vendor);
        Assert.AreEqual(expectedAccelerator, info.PreferredAccelerator);
        Assert.AreEqual(expectedDiscrete, info.IsDiscrete);
    }

    [TestMethod]
    public async Task VideoRamCachePreloadsAndReleasesTemporaryFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VCUT_RamCacheTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string tempFile = Path.Combine(tempDir, "sample.mp4");
            byte[] payload = new byte[2 * 1024 * 1024];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(tempFile, payload);

            using var cache = new VideoRamCache();
            VideoRamCache.PrimeHeaders(tempFile);
            RamCacheStatus status = await cache.PreloadAsync(tempFile, CancellationToken.None);
            Assert.IsTrue(status.IsFullyCached);
            Assert.AreEqual((long)payload.Length, status.CachedBytes);
            Assert.IsNotNull(cache.CurrentStatus);
            cache.Clear();
            Assert.IsNull(cache.CurrentStatus);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void AudioEnvelopeNormalizesSoundAndPreservesTrueSilence()
    {
        float[] allSilent = AudioWaveformService.NormalizeEnvelope(new double[] { 0.0, 0.0005, 0.0010 });
        Assert.AreEqual(3, allSilent.Length);
        Assert.IsTrue(allSilent.All(v => v == 0.0f));

        float[] mixed = AudioWaveformService.NormalizeEnvelope(new double[] { 0.0, 0.0008, 0.12, 0.35, 0.0 });
        Assert.AreEqual(0.0f, mixed[0]);
        Assert.AreEqual(0.0f, mixed[1]);
        Assert.IsTrue(mixed[2] > 0.30f && mixed[2] < mixed[3]);
        Assert.IsTrue(mixed[3] > 0.85f && mixed[3] <= 1.0f);
        Assert.AreEqual(0.0f, mixed[4]);
    }

    [TestMethod]
    public void LimitUnder200MbCalculatesHevcArgumentsAndPreservesAudio()
    {
        var video10Min = new VideoInfo(@"C:\videos\clip.mp4", TimeSpan.FromMinutes(10), "h264", 1920, 1080, "aac");
        var req10Min = new CutRequest(video10Min, TimeSpan.Zero, TimeSpan.FromMinutes(10), OutputContainer.Original, limitUnder200Mb: true);
        Assert.IsTrue(req10Min.LimitUnder200Mb);
        int kbps10 = req10Min.CalculateTargetVideoKbpsUnder200Mb(out bool copyAudio10);
        Assert.IsTrue(copyAudio10, "Para fragmentos de 10 minutos el audio debe copiarse 1:1 (-c:a copy).");
        Assert.IsTrue(kbps10 > 2000 && kbps10 < 3000);

        IReadOnlyList<string> nvencArgs = req10Min.BuildArguments(@"C:\videos\temp.mp4", "hevc_nvenc");
        CollectionAssert.Contains(nvencArgs.ToList(), "hevc_nvenc");
        CollectionAssert.Contains(nvencArgs.ToList(), "hvc1");
        CollectionAssert.Contains(nvencArgs.ToList(), "copy");

        var video100Min = new VideoInfo(@"C:\videos\long.webm", TimeSpan.FromMinutes(100), "vp9", 1920, 1080, "opus");
        var req100Min = new CutRequest(video100Min, TimeSpan.Zero, TimeSpan.FromMinutes(100), OutputContainer.Original, limitUnder200Mb: true);
        Assert.AreEqual(@"C:\videos\long_cut.mp4", req100Min.OutputPath);
        int kbps100 = req100Min.CalculateTargetVideoKbpsUnder200Mb(out bool copyAudio100, out int aacKbps100);
        Assert.IsFalse(copyAudio100);
        long estimatedTotalBytes = (long)((kbps100 + aacKbps100) * 1000.0 / 8.0 * 6000.0);
        Assert.IsTrue(estimatedTotalBytes < CutRequest.HardLimit200MbBytes);
    }
}

