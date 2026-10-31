using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VCUT.Core;

namespace VCUT.Tests;

[TestClass]
public class VideoIntegrationTests
{
    private static readonly string ToolsPath = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
    private static readonly string Ffmpeg = Path.Combine(ToolsPath, "ffmpeg.exe");
    private static readonly VideoService Service = new(ToolsPath);
    private string _directory;

    [TestInitialize]
    public void Setup()
    {
        Assert.IsTrue(File.Exists(Ffmpeg), @"Ejecuta scripts\Setup-FFmpeg.ps1 antes de compilar.");
        _directory = Path.Combine(Path.GetTempPath(), "VCUT.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_directory is not null && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> CreateVideoAsync(string extension, bool withAudio = true)
    {
        string filePath = Path.Combine(_directory, "vídeo de prueba & original" + extension);
        var args = new List<string> { "-v", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25:duration=6" };
        if (withAudio)
        {
            args.AddRange(new[] { "-f", "lavfi", "-i", "sine=frequency=440:duration=6", "-map", "0:v", "-map", "1:a", "-map", "1:a" });
        }

        switch (extension)
        {
            case ".webm":
                args.AddRange(new[] { "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-g", "25", "-b:v", "250k" });
                break;
            case ".avi":
                args.AddRange(new[] { "-c:v", "mpeg4", "-q:v", "3", "-g", "25", "-bf", "0" });
                break;
            default:
                args.AddRange(new[] { "-c:v", "libx264", "-preset", "ultrafast", "-g", "25", "-bf", "0", "-pix_fmt", "yuv420p" });
                break;
        }

        if (withAudio)
        {
            args.AddRange(new[] { "-c:a", extension == ".webm" ? "libopus" : (extension == ".avi" ? "libmp3lame" : "aac") });
        }

        args.Add(filePath);
        ProcessResult result = await MediaProcess.RunAsync(Ffmpeg, args, CancellationToken.None);
        Assert.AreEqual(0, result.ExitCode, result.Errors);
        return filePath;
    }

    [TestMethod]
    [DataRow(".mp4", true)]
    [DataRow(".mkv", true)]
    [DataRow(".mov", false)]
    [DataRow(".avi", true)]
    [DataRow(".webm", true)]
    [DataRow(".ts", true)]
    public async Task CutsRealVideoPreservesTracksAndPacketBytes(string extension, bool withAudio)
    {
        string source = await CreateVideoAsync(extension, withAudio);
        byte[] originalHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        VideoInfo info = await Service.ProbeAsync(source, CancellationToken.None);
        string output = await Service.CutAsync(new CutRequest(info, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)), null, CancellationToken.None);
        Assert.AreEqual(Path.Combine(_directory, "vídeo de prueba & original_cut" + extension), output);
        VideoInfo cut = await Service.ProbeAsync(output, CancellationToken.None);
        Assert.AreEqual(info.Codec, cut.Codec);
        Assert.AreEqual(160, cut.Width);
        Assert.IsTrue(cut.Duration.TotalSeconds >= 1.9 && cut.Duration.TotalSeconds < 3.2, $"Duración inesperada: {cut.Duration}");
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        ProcessResult probe = await MediaProcess.RunAsync(Path.Combine(ToolsPath, "ffprobe.exe"), new[] { "-v", "error", "-show_streams", "-of", "json", output }, CancellationToken.None);
        using (JsonDocument doc = JsonDocument.Parse(probe.Output))
        {
            Assert.AreEqual(withAudio ? 3 : 1, doc.RootElement.GetProperty("streams").GetArrayLength());
        }

        HashSet<string> sourcePackets = await PacketHashesAsync(source);
        HashSet<string> cutPackets = await PacketHashesAsync(output);
        Assert.IsTrue(cutPackets.Count > 0);
        Assert.IsTrue(cutPackets.All(hash => sourcePackets.Contains(hash)), "Los paquetes del vídeo recortado deben conservar los bytes originales, sin recodificar.");
        ProcessResult decode = await MediaProcess.RunAsync(Ffmpeg, new[] { "-v", "error", "-xerror", "-i", output, "-f", "null", "-" }, CancellationToken.None);
        Assert.AreEqual(0, decode.ExitCode, decode.Errors);
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length);
    }

    private static async Task<HashSet<string>> PacketHashesAsync(string filePath)
    {
        ProcessResult result = await MediaProcess.RunAsync(
            Path.Combine(ToolsPath, "ffprobe.exe"),
            new[] { "-v", "error", "-select_streams", "v:0", "-show_packets", "-show_data_hash", "sha256", "-show_entries", "packet=data_hash", "-of", "json", filePath },
            CancellationToken.None);
        Assert.AreEqual(0, result.ExitCode, result.Errors);
        using JsonDocument doc = JsonDocument.Parse(result.Output);
        return doc.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()).ToHashSet();
    }

    [TestMethod]
    public async Task ExistingCutIsNeverOverwritten()
    {
        string source = await CreateVideoAsync(".mp4");
        var request = new CutRequest(await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        await File.WriteAllTextAsync(request.OutputPath, "contenido existente");
        await Assert.ThrowsExactlyAsync<IOException>(() => Service.CutAsync(request, null, CancellationToken.None));
        Assert.AreEqual("contenido existente", await File.ReadAllTextAsync(request.OutputPath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length);
    }

    [TestMethod]
    public async Task CancelledCutDoesNotCreateOutput()
    {
        string source = await CreateVideoAsync(".mp4");
        var request = new CutRequest(await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(1));
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => Service.CutAsync(request, null, cancellation.Token));
        }

        Assert.IsFalse(File.Exists(request.OutputPath));
    }

    [TestMethod]
    public async Task CancellationTerminatesAnActiveProcess()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            MediaProcess.RunAsync(Ffmpeg, new[] { "-v", "error", "-re", "-f", "lavfi", "-i", "color=duration=60", "-f", "null", "-" }, cancellation.Token));
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task CancellationAfterOutputStartedCleansPartialFile()
    {
        string source = await CreateVideoAsync(".mp4");
        var request = new CutRequest(await Service.ProbeAsync(source, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(5));
        using (var cancellation = new CancellationTokenSource())
        {
            var progress = new InlineProgress(_ => cancellation.Cancel());
            await Assert.ThrowsAsync<OperationCanceledException>(() => Service.CutAsync(request, progress, cancellation.Token));
        }

        Assert.IsTrue(File.Exists(source));
        Assert.IsFalse(File.Exists(request.OutputPath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length);
    }

    [TestMethod]
    public async Task CutBetweenKeyframesIsDecodableAndApproximate()
    {
        string source = await CreateVideoAsync(".mp4");
        VideoInfo info = await Service.ProbeAsync(source, CancellationToken.None);
        var request = new CutRequest(info, TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(3.75));
        string output = await Service.CutAsync(request, null, CancellationToken.None);
        VideoInfo cut = await Service.ProbeAsync(output, CancellationToken.None);
        Assert.IsTrue(cut.Duration.TotalSeconds >= request.Duration.TotalSeconds);
        Assert.IsTrue(cut.Duration.TotalSeconds < request.Duration.TotalSeconds + 1.2);
        ProcessResult decoded = await MediaProcess.RunAsync(Ffmpeg, new[] { "-v", "error", "-xerror", "-i", output, "-f", "null", "-" }, CancellationToken.None);
        Assert.AreEqual(0, decoded.ExitCode, decoded.Errors);
    }

    private sealed class InlineProgress : IProgress<CutProgress>
    {
        private readonly Action<CutProgress> _callback;

        public InlineProgress(Action<CutProgress> callback)
        {
            _callback = callback;
        }

        public void Report(CutProgress value)
        {
            _callback(value);
        }
    }

    [TestMethod]
    public async Task InvalidMediaIsRejected()
    {
        string filePath = Path.Combine(_directory, "invalid.mp4");
        await File.WriteAllTextAsync(filePath, "This is not a video");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Service.ProbeAsync(filePath, CancellationToken.None));
    }

    [TestMethod]
    public async Task FailedCutCleansTemporaryFile()
    {
        string source = await CreateVideoAsync(".mp4");
        string incompatible = Path.ChangeExtension(source, ".webm");
        File.Move(source, incompatible);
        var request = new CutRequest(await Service.ProbeAsync(incompatible, CancellationToken.None), TimeSpan.Zero, TimeSpan.FromSeconds(2));
        await Assert.ThrowsExactlyAsync<IOException>(() => Service.CutAsync(request, null, CancellationToken.None));
        Assert.IsFalse(File.Exists(request.OutputPath));
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.partial.*").Length);
    }

    [TestMethod]
    public async Task MatroskaAlternativeAcceptsTracksRejectedByOriginalExtensionWithoutRecoding()
    {
        string source = await CreateVideoAsync(".mp4");
        string incompatible = Path.ChangeExtension(source, ".webm");
        File.Move(source, incompatible);
        byte[] originalHash = SHA256.HashData(await File.ReadAllBytesAsync(incompatible));
        VideoInfo info = await Service.ProbeAsync(incompatible, CancellationToken.None);
        var request = new CutRequest(info, TimeSpan.Zero, TimeSpan.FromSeconds(2), OutputContainer.Matroska);
        string output = await Service.CutAsync(request, null, CancellationToken.None);

        Assert.AreEqual(Path.Combine(_directory, "vídeo de prueba & original_cut.mkv"), output);
        Assert.IsTrue(File.Exists(output));
        CollectionAssert.AreEqual(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(incompatible)));
        VideoInfo cut = await Service.ProbeAsync(output, CancellationToken.None);
        Assert.AreEqual(info.Codec, cut.Codec);
        ProcessResult probe = await MediaProcess.RunAsync(Path.Combine(ToolsPath, "ffprobe.exe"), new[] { "-v", "error", "-show_streams", "-of", "json", output }, CancellationToken.None);
        using (JsonDocument doc = JsonDocument.Parse(probe.Output))
        {
            Assert.AreEqual(3, doc.RootElement.GetProperty("streams").GetArrayLength());
        }

        HashSet<string> sourcePackets = await PacketHashesAsync(incompatible);
        HashSet<string> cutPackets = await PacketHashesAsync(output);
        Assert.IsTrue(cutPackets.Count > 0);
        Assert.IsTrue(cutPackets.All(hash => sourcePackets.Contains(hash)), "MKV debe conservar los paquetes originales sin recodificar.");
    }

    [TestMethod]
    public async Task BenchmarkAndVerifyRootTestMp4IfPresent()
    {
        string rootTestVideo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test.mp4"));
        if (!File.Exists(rootTestVideo))
        {
            return;
        }

        var swPrime = Stopwatch.StartNew();
        VideoRamCache.PrimeHeaders(rootTestVideo);
        swPrime.Stop();

        var swNative = Stopwatch.StartNew();
        bool fastOk = VideoInfo.TryProbeFastIsoBmff(rootTestVideo, out VideoInfo fastInfo);
        swNative.Stop();

        var swProbe = Stopwatch.StartNew();
        VideoInfo ffprobeInfo = await Service.ProbeAsync(rootTestVideo, CancellationToken.None, primeRamHeaders: false, allowNativeFastPath: false);
        swProbe.Stop();

        Assert.IsTrue(fastOk, "El analizador nativo ISO-BMFF debería poder leer test.mp4 directamente en memoria.");
        Assert.AreEqual(ffprobeInfo.Width, fastInfo.Width);
        Assert.AreEqual(ffprobeInfo.Height, fastInfo.Height);
        Assert.AreEqual(ffprobeInfo.Codec, fastInfo.Codec);
        Assert.AreEqual(ffprobeInfo.Duration.TotalSeconds, fastInfo.Duration.TotalSeconds, 0.25);

        using var ramCache = new VideoRamCache();
        RamCacheStatus ramStatus = await ramCache.PreloadAsync(rootTestVideo, CancellationToken.None);
        Assert.IsTrue(ramStatus.CachedBytes > 0);

        using var thumbService = new VideoThumbnailService(ToolsPath);
        thumbService.Reset(rootTestVideo, fastInfo.Duration, 320);
        var swThumb = Stopwatch.StartNew();
        byte[] jpeg = await thumbService.GetFrameAsync(0.5, CancellationToken.None);
        swThumb.Stop();
        Assert.IsNotNull(jpeg);
        Assert.IsTrue(jpeg.Length > 200 && jpeg[0] == 0xFF && jpeg[1] == 0xD8, "El fotograma extraído en memoria debe ser un JPEG válido.");
        Assert.IsTrue(thumbService.TryGetClosestFrame(0.50001, out byte[] cachedJpeg) && ReferenceEquals(jpeg, cachedJpeg));

        Console.WriteLine($"[BENCHMARK test.mp4] Tamaño={ramStatus.FileBytes / (1024 * 1024)} MB | PrimeHeaders={swPrime.Elapsed.TotalMilliseconds:0.00} ms | NativeProbe={swNative.Elapsed.TotalMilliseconds:0.00} ms | FFprobe={swProbe.Elapsed.TotalMilliseconds:0.00} ms | RamPreload={ramStatus.Elapsed.TotalMilliseconds:0.0} ms | HoverThumb={swThumb.Elapsed.TotalMilliseconds:0.0} ms ({jpeg.Length} bytes JPEG)");
    }

    [TestMethod]
    public async Task PrewarmAllHalfSecondsGeneratesTwoFramesPerSecond()
    {
        string mp4 = await CreateVideoAsync(".mp4");
        using var thumbService = new VideoThumbnailService(ToolsPath);
        thumbService.Reset(mp4, TimeSpan.FromSeconds(4.0), 320);
        Assert.AreEqual(8, thumbService.TotalHalfSecondBuckets);

        await thumbService.PrewarmAllHalfSecondsAsync(CancellationToken.None);

        Assert.AreEqual(8, thumbService.CachedFrameCount, "Deben generarse 2 fotogramas por segundo (8 fotogramas para 4 segundos).");
        for (int i = 0; i < 8; i++)
        {
            double frac = (i * 0.5 + 0.1) / 4.0;
            Assert.IsTrue(thumbService.TryGetClosestFrame(frac, out byte[] frame, maxDistanceBuckets: 0));
            Assert.IsNotNull(frame);
            Assert.AreSame(frame, thumbService.TryGetFrameByBucket(i));
            Assert.IsTrue(frame.Length > 100 && frame[0] == 0xFF && frame[1] == 0xD8);
        }
    }
}
