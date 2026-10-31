using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VCUT.Core;

public sealed class VideoInfo
{
    public VideoInfo(string filePath, TimeSpan duration, string codec, int width, int height, string audioCodec = "", long fileSize = 0)
    {
        FilePath = Path.GetFullPath(filePath);
        Duration = duration;
        Codec = codec;
        Width = width;
        Height = height;
        AudioCodec = audioCodec ?? string.Empty;
        FileSize = fileSize > 0 ? fileSize : (File.Exists(filePath) ? new FileInfo(filePath).Length : 0L);
    }

    public string FilePath { get; }
    public TimeSpan Duration { get; }
    public string Codec { get; }
    public int Width { get; }
    public int Height { get; }
    public string AudioCodec { get; }
    public long FileSize { get; }

    public static VideoInfo FromProbeJson(string filePath, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("streams", out JsonElement streams))
        {
            throw new InvalidDataException("El archivo no contiene pistas de vídeo.");
        }

        JsonElement selected = default;
        bool found = false;
        double longestDuration = 0;
        string audioCodecName = string.Empty;

        foreach (JsonElement stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("codec_type", out JsonElement kind))
            {
                string kindText = kind.GetString();
                if (kindText == "audio" && string.IsNullOrEmpty(audioCodecName))
                {
                    if (stream.TryGetProperty("codec_name", out JsonElement aCodec))
                    {
                        audioCodecName = aCodec.GetString() ?? string.Empty;
                    }
                }
                else if (kindText == "video")
                {
                    if (stream.TryGetProperty("disposition", out JsonElement disposition) &&
                        disposition.TryGetProperty("attached_pic", out JsonElement picture) &&
                        picture.GetInt32() == 1)
                    {
                        continue;
                    }

                    if (!found)
                    {
                        selected = stream;
                        found = true;
                    }

                    longestDuration = Math.Max(longestDuration, ReadSeconds(stream));
                }
            }
        }

        if (!found)
        {
            throw new InvalidDataException("El archivo no contiene vídeo reproducible (puede ser audio o una carátula).");
        }

        double seconds = 0;
        long sizeBytes = 0;
        if (root.TryGetProperty("format", out JsonElement format))
        {
            seconds = ReadSeconds(format);
            if (format.TryGetProperty("size", out JsonElement sizeElem))
            {
                long.TryParse(sizeElem.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sizeBytes);
            }
        }

        if (seconds <= 0)
        {
            seconds = longestDuration;
        }

        if (seconds <= 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            throw new InvalidDataException("No se puede determinar la duración de este vídeo. Puede estar incompleto o ser un flujo sin duración.");
        }

        return new VideoInfo(
            filePath,
            TimeSpan.FromSeconds(seconds),
            selected.TryGetProperty("codec_name", out JsonElement codec) ? (codec.GetString() ?? "desconocido") : "desconocido",
            selected.TryGetProperty("width", out JsonElement width) ? width.GetInt32() : 0,
            selected.TryGetProperty("height", out JsonElement height) ? height.GetInt32() : 0,
            audioCodecName,
            sizeBytes);
    }

    public static bool TryProbeFastIsoBmff(string filePath, out VideoInfo info)
    {
        info = null;
        try
        {
            string ext = Path.GetExtension(filePath);
            if (!ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".mov", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);
            long fileLength = stream.Length;
            if (fileLength < 32)
            {
                return false;
            }

            Span<byte> header = stackalloc byte[16];
            bool sawFtyp = false;
            while (stream.Position + 8 <= fileLength)
            {
                long boxStart = stream.Position;
                if (stream.Read(header.Slice(0, 8)) < 8)
                {
                    break;
                }

                uint size32 = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0, 4));
                string type = Encoding.ASCII.GetString(header.Slice(4, 4));
                long boxSize = size32;
                int headerSize = 8;

                if (size32 == 1)
                {
                    if (stream.Read(header.Slice(8, 8)) < 8)
                    {
                        break;
                    }

                    boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
                    headerSize = 16;
                }
                else if (size32 == 0)
                {
                    boxSize = fileLength - boxStart;
                }

                if (boxSize < headerSize || boxStart + boxSize > fileLength)
                {
                    break;
                }

                if (type == "ftyp")
                {
                    sawFtyp = true;
                }
                else if (type == "moov" && (sawFtyp || boxStart == 0))
                {
                    long payloadSize = boxSize - headerSize;
                    if (payloadSize > 0 && payloadSize <= 64 * 1024 * 1024)
                    {
                        byte[] moovBytes = new byte[payloadSize];
                        int readTotal = 0;
                        while (readTotal < moovBytes.Length)
                        {
                            int r = stream.Read(moovBytes, readTotal, moovBytes.Length - readTotal);
                            if (r <= 0)
                            {
                                break;
                            }

                            readTotal += r;
                        }

                        if (readTotal == moovBytes.Length && TryParseMoov(filePath, fileLength, moovBytes, out info))
                        {
                            return true;
                        }
                    }

                    break;
                }

                stream.Seek(boxStart + boxSize, SeekOrigin.Begin);
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool TryParseMoov(string filePath, long fileLength, ReadOnlySpan<byte> moov, out VideoInfo info)
    {
        info = null;
        double movieSeconds = 0;
        double longestVideoSeconds = 0;
        string videoCodec = null;
        string audioCodec = string.Empty;
        int videoWidth = 0;
        int videoHeight = 0;

        int pos = 0;
        while (pos + 8 <= moov.Length)
        {
            if (!TryReadBox(moov, pos, out string type, out int payloadOffset, out int nextPos))
            {
                break;
            }

            ReadOnlySpan<byte> payload = moov.Slice(payloadOffset, nextPos - payloadOffset);
            if (type == "mvhd")
            {
                movieSeconds = ParseDurationBox(payload);
            }
            else if (type == "trak")
            {
                ParseTrak(payload, ref videoCodec, ref videoWidth, ref videoHeight, ref longestVideoSeconds, ref audioCodec);
            }

            pos = nextPos;
        }

        double seconds = movieSeconds > 0 ? movieSeconds : longestVideoSeconds;
        if (string.IsNullOrEmpty(videoCodec) || seconds <= 0 || videoWidth <= 0 || videoHeight <= 0)
        {
            return false;
        }

        info = new VideoInfo(filePath, TimeSpan.FromSeconds(seconds), videoCodec, videoWidth, videoHeight, audioCodec, fileLength);
        return true;
    }

    private static void ParseTrak(
        ReadOnlySpan<byte> trak,
        ref string videoCodec,
        ref int videoWidth,
        ref int videoHeight,
        ref double longestVideoSeconds,
        ref string audioCodec)
    {
        int tkhdWidth = 0;
        int tkhdHeight = 0;
        string handlerType = null;
        double trackSeconds = 0;
        string foundFourCc = null;
        int stsdWidth = 0;
        int stsdHeight = 0;

        int pos = 0;
        while (pos + 8 <= trak.Length)
        {
            if (!TryReadBox(trak, pos, out string type, out int payloadOffset, out int nextPos))
            {
                break;
            }

            ReadOnlySpan<byte> payload = trak.Slice(payloadOffset, nextPos - payloadOffset);
            if (type == "tkhd" && payload.Length >= 84)
            {
                byte version = payload[0];
                int dimOffset = version == 1 ? 88 : 76;
                if (payload.Length >= dimOffset + 8)
                {
                    tkhdWidth = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(dimOffset, 4)) >> 16);
                    tkhdHeight = (int)(BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(dimOffset + 4, 4)) >> 16);
                }
            }
            else if (type == "mdia")
            {
                ParseMdia(payload, ref handlerType, ref trackSeconds, ref foundFourCc, ref stsdWidth, ref stsdHeight);
            }

            pos = nextPos;
        }

        if (handlerType == "vide" && !string.IsNullOrEmpty(foundFourCc))
        {
            longestVideoSeconds = Math.Max(longestVideoSeconds, trackSeconds);
            if (string.IsNullOrEmpty(videoCodec))
            {
                videoCodec = MapFourCcToCodecName(foundFourCc);
                videoWidth = stsdWidth > 0 ? stsdWidth : tkhdWidth;
                videoHeight = stsdHeight > 0 ? stsdHeight : tkhdHeight;
            }
        }
        else if (handlerType == "soun" && string.IsNullOrEmpty(audioCodec) && !string.IsNullOrEmpty(foundFourCc))
        {
            audioCodec = MapFourCcToCodecName(foundFourCc);
        }
    }

    private static void ParseMdia(
        ReadOnlySpan<byte> mdia,
        ref string handlerType,
        ref double trackSeconds,
        ref string foundFourCc,
        ref int stsdWidth,
        ref int stsdHeight)
    {
        int pos = 0;
        while (pos + 8 <= mdia.Length)
        {
            if (!TryReadBox(mdia, pos, out string type, out int payloadOffset, out int nextPos))
            {
                break;
            }

            ReadOnlySpan<byte> payload = mdia.Slice(payloadOffset, nextPos - payloadOffset);
            if (type == "mdhd")
            {
                trackSeconds = ParseDurationBox(payload);
            }
            else if (type == "hdlr" && payload.Length >= 12)
            {
                handlerType = Encoding.ASCII.GetString(payload.Slice(8, 4));
            }
            else if (type == "minf")
            {
                ParseMinf(payload, ref foundFourCc, ref stsdWidth, ref stsdHeight);
            }

            pos = nextPos;
        }
    }

    private static void ParseMinf(ReadOnlySpan<byte> minf, ref string foundFourCc, ref int stsdWidth, ref int stsdHeight)
    {
        int pos = 0;
        while (pos + 8 <= minf.Length)
        {
            if (!TryReadBox(minf, pos, out string type, out int payloadOffset, out int nextPos))
            {
                break;
            }

            if (type == "stbl")
            {
                ReadOnlySpan<byte> stbl = minf.Slice(payloadOffset, nextPos - payloadOffset);
                int sPos = 0;
                while (sPos + 8 <= stbl.Length)
                {
                    if (!TryReadBox(stbl, sPos, out string sType, out int sPayloadOffset, out int sNextPos))
                    {
                        break;
                    }

                    if (sType == "stsd")
                    {
                        ReadOnlySpan<byte> stsd = stbl.Slice(sPayloadOffset, sNextPos - sPayloadOffset);
                        if (stsd.Length >= 16)
                        {
                            uint entrySize = BinaryPrimitives.ReadUInt32BigEndian(stsd.Slice(8, 4));
                            foundFourCc = Encoding.ASCII.GetString(stsd.Slice(12, 4)).Trim();
                            if (entrySize >= 36 && stsd.Length >= 8 + 36)
                            {
                                stsdWidth = BinaryPrimitives.ReadUInt16BigEndian(stsd.Slice(8 + 32, 2));
                                stsdHeight = BinaryPrimitives.ReadUInt16BigEndian(stsd.Slice(8 + 34, 2));
                            }
                        }
                    }

                    sPos = sNextPos;
                }
            }

            pos = nextPos;
        }
    }

    private static double ParseDurationBox(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 20)
        {
            return 0;
        }

        byte version = payload[0];
        if (version == 1)
        {
            if (payload.Length < 32)
            {
                return 0;
            }

            uint timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(20, 4));
            ulong duration = BinaryPrimitives.ReadUInt64BigEndian(payload.Slice(24, 8));
            return timescale > 0 && duration != ulong.MaxValue ? (double)duration / timescale : 0;
        }
        else
        {
            uint timescale = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(12, 4));
            uint duration = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(16, 4));
            return timescale > 0 && duration != uint.MaxValue ? (double)duration / timescale : 0;
        }
    }

    private static bool TryReadBox(ReadOnlySpan<byte> buffer, int pos, out string type, out int payloadOffset, out int nextPos)
    {
        type = string.Empty;
        payloadOffset = 0;
        nextPos = 0;
        if (pos + 8 > buffer.Length)
        {
            return false;
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(pos, 4));
        type = Encoding.ASCII.GetString(buffer.Slice(pos + 4, 4));
        long boxSize = size32;
        int headerSize = 8;
        if (size32 == 1)
        {
            if (pos + 16 > buffer.Length)
            {
                return false;
            }

            boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(buffer.Slice(pos + 8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            boxSize = buffer.Length - pos;
        }

        if (boxSize < headerSize || pos + boxSize > buffer.Length)
        {
            return false;
        }

        payloadOffset = pos + headerSize;
        nextPos = (int)(pos + boxSize);
        return true;
    }

    private static string MapFourCcToCodecName(string fourCc)
    {
        return fourCc switch
        {
            "avc1" or "avc3" => "h264",
            "hev1" or "hvc1" => "hevc",
            "av01" => "av1",
            "vp09" => "vp9",
            "vp08" => "vp8",
            "mp4v" => "mpeg4",
            "apch" or "apcn" or "apcs" or "apco" or "ap4h" or "ap4x" => "prores",
            "mp4a" => "aac",
            "ac-3" => "ac3",
            "ec-3" => "eac3",
            "Opus" or "opus" => "opus",
            "fLaC" => "flac",
            "alac" => "alac",
            ".mp3" => "mp3",
            _ => fourCc.ToLowerInvariant()
        };
    }

    private static double ReadSeconds(JsonElement element)
    {
        if (element.TryGetProperty("duration", out JsonElement value) &&
            double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            double.IsFinite(seconds) &&
            seconds > 0)
        {
            return seconds;
        }

        return 0;
    }
}

