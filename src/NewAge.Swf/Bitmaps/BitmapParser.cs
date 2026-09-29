using System.IO.Compression;

namespace NewAge.Swf.Bitmaps;

public enum SwfBitmapFormat
{
    RawBgra,
    Jpeg,
    Png,
    Gif,
}

public sealed class SwfBitmap
{
    public int CharacterId { get; init; }
    public SwfBitmapFormat Format { get; init; }
    public byte[] Data { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    public byte[] Alpha { get; init; }

    public override string ToString() =>
        Format == SwfBitmapFormat.RawBgra
            ? $"#{CharacterId} {Format} {Width}x{Height}"
            : $"#{CharacterId} {Format} {Data.Length} байт{(Alpha is null ? "" : " + альфа")}";
}

public static class BitmapParser
{
    public static SwfBitmap Parse(SwfFile file, in SwfTag tag, byte[] jpegTables = null) => tag.Code switch
    {
        SwfTagCode.DefineBits => ParseDefineBits(file, tag, jpegTables),
        SwfTagCode.DefineBitsJpeg2 => ParseJpeg2(file, tag),
        SwfTagCode.DefineBitsJpeg3 => ParseJpeg3(file, tag, hasDeblock: false),
        SwfTagCode.DefineBitsJpeg4 => ParseJpeg3(file, tag, hasDeblock: true),
        SwfTagCode.DefineBitsLossless => ParseLossless(file, tag, withAlpha: false),
        SwfTagCode.DefineBitsLossless2 => ParseLossless(file, tag, withAlpha: true),
        _ => throw new ArgumentException($"{tag.Code} — не растр", nameof(tag)),
    };

    public static byte[] ReadJpegTables(SwfFile file, in SwfTag tag) =>
        file.Body[tag.BodyStart..tag.BodyEnd];

    private static SwfBitmap ParseDefineBits(SwfFile file, in SwfTag tag, byte[] jpegTables)
    {
        var reader = file.ReaderAt(tag);
        int id = reader.ReadUI16();
        byte[] image = file.Body[reader.Position..tag.BodyEnd];

        byte[] data = jpegTables is { Length: > 2 }
            ? Concat(TrimEndMarker(jpegTables), TrimStartMarker(image))
            : image;

        return new SwfBitmap { CharacterId = id, Format = SwfBitmapFormat.Jpeg, Data = FixJpeg(data) };
    }

    private static SwfBitmap ParseJpeg2(SwfFile file, in SwfTag tag)
    {
        var reader = file.ReaderAt(tag);
        int id = reader.ReadUI16();
        byte[] data = file.Body[reader.Position..tag.BodyEnd];

        return new SwfBitmap { CharacterId = id, Format = DetectFormat(data), Data = FixJpeg(data) };
    }

    private static SwfBitmap ParseJpeg3(SwfFile file, in SwfTag tag, bool hasDeblock)
    {
        var reader = file.ReaderAt(tag);
        int id = reader.ReadUI16();
        int alphaOffset = (int)reader.ReadUI32();
        if (hasDeblock) reader.ReadUI16();

        int imageStart = reader.Position;
        int imageEnd = imageStart + alphaOffset;
        if (imageEnd > tag.BodyEnd) imageEnd = tag.BodyEnd;

        byte[] image = file.Body[imageStart..imageEnd];
        var format = DetectFormat(image);

        byte[] alpha = null;
        if (format == SwfBitmapFormat.Jpeg && imageEnd < tag.BodyEnd)
            alpha = Inflate(file.Body, imageEnd, tag.BodyEnd - imageEnd);

        return new SwfBitmap
        {
            CharacterId = id,
            Format = format,
            Data = FixJpeg(image),
            Alpha = alpha,
        };
    }

    private static SwfBitmapFormat DetectFormat(byte[] data)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
            return SwfBitmapFormat.Png;
        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return SwfBitmapFormat.Gif;
        return SwfBitmapFormat.Jpeg;
    }

    private static byte[] FixJpeg(byte[] data)
    {
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD9 && data[2] == 0xFF && data[3] == 0xD8)
            data = data[4..];
        for (int i = 2; i + 3 < data.Length; i++)
        {
            if (data[i] != 0xFF || data[i + 1] != 0xD9 || data[i + 2] != 0xFF || data[i + 3] != 0xD8) continue;
            var joined = new byte[data.Length - 4];
            Buffer.BlockCopy(data, 0, joined, 0, i);
            Buffer.BlockCopy(data, i + 4, joined, i, data.Length - i - 4);
            data = joined;
            i--;
        }
        return data;
    }

    private static byte[] TrimEndMarker(byte[] data) =>
        data.Length >= 2 && data[^2] == 0xFF && data[^1] == 0xD9 ? data[..^2] : data;

    private static byte[] TrimStartMarker(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8 ? data[2..] : data;

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        a.CopyTo(r, 0);
        b.CopyTo(r, a.Length);
        return r;
    }

    private static SwfBitmap ParseLossless(SwfFile file, in SwfTag tag, bool withAlpha)
    {
        var reader = file.ReaderAt(tag);
        int id = reader.ReadUI16();
        int format = reader.ReadUI8();
        int width = reader.ReadUI16();
        int height = reader.ReadUI16();
        int paletteSize = format == 3 ? reader.ReadUI8() + 1 : 0;

        byte[] raw = Inflate(file.Body, reader.Position, tag.BodyEnd - reader.Position);
        byte[] bgra = new byte[width * height * 4];

        switch (format)
        {
            case 3:
            {
                int entry = withAlpha ? 4 : 3;
                int paletteBytes = paletteSize * entry;
                int stride = (width + 3) & ~3;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int index = raw[paletteBytes + y * stride + x];
                        int p = index * entry;
                        byte r = raw[p], g = raw[p + 1], b = raw[p + 2];
                        byte a = withAlpha ? raw[p + 3] : (byte)255;
                        WriteUnpremultiplied(bgra, (y * width + x) * 4, b, g, r, a, withAlpha);
                    }
                }
                break;
            }

            case 4:
            {
                int stride = (width * 2 + 3) & ~3;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int p = y * stride + x * 2;
                        int v = (raw[p] << 8) | raw[p + 1];
                        byte r = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                        byte g = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                        byte b = (byte)((v & 0x1F) * 255 / 31);
                        int o = (y * width + x) * 4;
                        bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
                    }
                }
                break;
            }

            case 5:
            {
                for (int i = 0, n = width * height; i < n; i++)
                {
                    int p = i * 4;
                    byte a = withAlpha ? raw[p] : (byte)255;
                    byte r = raw[p + 1], g = raw[p + 2], b = raw[p + 3];
                    WriteUnpremultiplied(bgra, i * 4, b, g, r, a, withAlpha);
                }
                break;
            }

            default:
                throw new InvalidDataException($"неизвестный формат lossless: {format}");
        }

        return new SwfBitmap
        {
            CharacterId = id,
            Format = SwfBitmapFormat.RawBgra,
            Data = bgra,
            Width = width,
            Height = height,
        };
    }

    private static void WriteUnpremultiplied(byte[] dst, int offset, byte b, byte g, byte r, byte a, bool premultiplied)
    {
        if (premultiplied && a is > 0 and < 255)
        {
            b = (byte)Math.Min(255, b * 255 / a);
            g = (byte)Math.Min(255, g * 255 / a);
            r = (byte)Math.Min(255, r * 255 / a);
        }
        dst[offset] = b;
        dst[offset + 1] = g;
        dst[offset + 2] = r;
        dst[offset + 3] = a;
    }

    private static byte[] Inflate(byte[] source, int offset, int length)
    {
        using var input = new MemoryStream(source, offset, length);
        using var zlib = SwfZlib.Open(input);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
