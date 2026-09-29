using System.IO.Compression;

namespace NewAge.Swf;

public sealed class SwfFile
{
    public byte[] Body { get; private init; }
    public int Version { get; private init; }
    public SwfRect FrameSize { get; private init; }
    public double FrameRate { get; private init; }
    public int FrameCount { get; private init; }
    public IReadOnlyList<SwfTag> Tags { get; private init; }
    public string SourcePath { get; private init; }

    private SwfFile() { }

    public static SwfFile Load(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        return Parse(raw, path);
    }

    public static SwfFile Parse(byte[] raw, string sourcePath = null)
    {
        byte[] body = Decompress(Locate(raw));

        int version = body[3];
        var reader = new SwfReader(body, 8) { Version = version };
        var frameSize = reader.ReadRect();
        double frameRate = reader.ReadUI8() / 256.0 + reader.ReadUI8();
        int frameCount = reader.ReadUI16();

        var tags = ReadTags(body, reader.Position, body.Length);

        return new SwfFile
        {
            Body = body,
            Version = version,
            FrameSize = frameSize,
            FrameRate = frameRate,
            FrameCount = frameCount,
            Tags = tags,
            SourcePath = sourcePath,
        };
    }

    public SwfReader ReaderAt(in SwfTag tag) => new(Body, tag.BodyStart) { Version = Version };

    public static List<SwfTag> ReadTags(byte[] body, int pos, int end)
    {
        var tags = new List<SwfTag>();
        while (pos + 2 <= end)
        {
            int codeAndLength = body[pos] | (body[pos + 1] << 8);
            var code = (SwfTagCode)(codeAndLength >> 6);
            int length = codeAndLength & 0x3F;
            int header = 2;

            if (length == 0x3F)
            {
                if (pos + 6 > end) break;
                length = BitConverter.ToInt32(body, pos + 2);
                header = 6;
            }

            int bodyStart = pos + header;
            if (length < 0 || bodyStart + length > end) break;

            tags.Add(new SwfTag(code, bodyStart, length));
            if (code == SwfTagCode.End) break;
            pos = bodyStart + length;
        }
        return tags;
    }

    public static byte[] Locate(byte[] raw)
    {
        if (raw.Length < 8)
            throw new InvalidDataException($"файл слишком мал для SWF ({raw.Length} байт)");

        if (IsSignature(raw, 0))
            return raw;

        {
            uint magic = BitConverter.ToUInt32(raw, raw.Length - 8);
            uint swfLength = BitConverter.ToUInt32(raw, raw.Length - 4);
            if (magic == 0xFA123456 && swfLength > 0 && swfLength <= raw.Length - 8)
            {
                int offset = raw.Length - 8 - (int)swfLength;
                if (offset >= 0 && IsSignature(raw, offset))
                    return raw[offset..(offset + (int)swfLength)];
            }
        }

        for (int i = raw.Length - 8; i >= 0; i--)
        {
            if (!IsSignature(raw, i)) continue;
            uint declared = BitConverter.ToUInt32(raw, i + 4);
            if (declared >= 8 && i + declared <= raw.Length)
                return raw[i..(i + (int)declared)];
        }

        throw new InvalidDataException("SWF не найден: ни сигнатуры в начале, ни футера проектора.");
    }

    private static bool IsSignature(byte[] b, int i) =>
        i + 3 <= b.Length &&
        (b[i] == 'F' || b[i] == 'C' || b[i] == 'Z') &&
        b[i + 1] == 'W' && b[i + 2] == 'S';

    private static byte[] Decompress(byte[] swf)
    {
        if (swf[0] == 'F') return swf;

        int uncompressedLength = BitConverter.ToInt32(swf, 4);
        var output = new MemoryStream(Math.Max(uncompressedLength, 1024));
        output.Write(swf, 0, 8);

        if (swf[0] == 'C')
        {
            using var input = new MemoryStream(swf, 8, swf.Length - 8);
            using var zlib = SwfZlib.Open(input);
            CopyWhilePossible(zlib, output);
        }
        else
        {
            throw new NotSupportedException("ZWS (LZMA) в файлах игры не встречается и не поддерживается.");
        }

        var result = output.ToArray();
        BitConverter.GetBytes(result.Length).CopyTo(result, 4);
        return result;
    }

    private static void CopyWhilePossible(Stream source, Stream destination)
    {
        var buffer = new byte[81920];
        while (true)
        {
            int read;
            try { read = source.Read(buffer, 0, buffer.Length); }
            catch (InvalidDataException) { break; }
            if (read <= 0) break;
            destination.Write(buffer, 0, read);
        }
    }
}

internal static class SwfZlib
{
    public static Stream Open(Stream input)
    {
#if NETSTANDARD
        input.ReadByte();
        input.ReadByte();
        return new DeflateStream(input, CompressionMode.Decompress);
#else
        return new ZLibStream(input, CompressionMode.Decompress);
#endif
    }
}
