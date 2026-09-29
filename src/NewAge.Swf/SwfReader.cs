using System.Text;

namespace NewAge.Swf;

public sealed class SwfReader
{
    private readonly byte[] _data;
    private int _pos;
    private int _bitBuf;
    private int _bitCount;

    public int Version { get; set; } = 6;

    public SwfReader(byte[] data, int pos = 0)
    {
        _data = data;
        _pos = pos;
    }

    public byte[] Data => _data;
    public int Length => _data.Length;

    public int Position
    {
        get => _pos;
        set { _pos = value; _bitCount = 0; }
    }

    public bool Eof => _pos >= _data.Length;

    public void Align() => _bitCount = 0;

    public byte ReadUI8()
    {
        _bitCount = 0;
        return _data[_pos++];
    }

    public sbyte ReadSI8() => (sbyte)ReadUI8();

    public ushort ReadUI16()
    {
        _bitCount = 0;
        ushort v = (ushort)(_data[_pos] | (_data[_pos + 1] << 8));
        _pos += 2;
        return v;
    }

    public short ReadSI16() => (short)ReadUI16();

    public uint ReadUI32()
    {
        _bitCount = 0;
        uint v = (uint)(_data[_pos] | (_data[_pos + 1] << 8) | (_data[_pos + 2] << 16) | (_data[_pos + 3] << 24));
        _pos += 4;
        return v;
    }

    public int ReadSI32() => (int)ReadUI32();

    public double ReadFixed() => ReadSI32() / 65536.0;

    public double ReadFixed8() => ReadSI16() / 256.0;

    public float ReadFloat()
    {
        _bitCount = 0;
        float v = BitConverter.ToSingle(_data, _pos);
        _pos += 4;
        return v;
    }

    public byte[] ReadBytes(int count)
    {
        _bitCount = 0;
        var r = new byte[count];
        Buffer.BlockCopy(_data, _pos, r, 0, count);
        _pos += count;
        return r;
    }

    public void Skip(int count)
    {
        _bitCount = 0;
        _pos += count;
    }

    public string ReadString()
    {
        _bitCount = 0;
        int start = _pos;
        while (_pos < _data.Length && _data[_pos] != 0) _pos++;
        int len = _pos - start;
        if (_pos < _data.Length) _pos++;
        return DecodeString(_data, start, len);
    }

    private string DecodeString(byte[] buf, int start, int len)
    {
        if (len == 0) return string.Empty;
        return Version >= 6
            ? Encoding.UTF8.GetString(buf, start, len)
            : SwfEncoding.Cp1251.GetString(buf, start, len);
    }

    public uint ReadUB(int bits)
    {
        uint result = 0;
        for (int i = 0; i < bits; i++)
        {
            if (_bitCount == 0)
            {
                _bitBuf = _data[_pos++];
                _bitCount = 8;
            }
            _bitCount--;
            result = (result << 1) | (uint)((_bitBuf >> _bitCount) & 1);
        }
        return result;
    }

    public int ReadSB(int bits)
    {
        if (bits == 0) return 0;
        uint raw = ReadUB(bits);
        int shift = 32 - bits;
        return (int)raw << shift >> shift;
    }

    public double ReadFB(int bits) => ReadSB(bits) / 65536.0;

    public bool ReadFlag() => ReadUB(1) != 0;

    public SwfRect ReadRect()
    {
        Align();
        int bits = (int)ReadUB(5);
        int xmin = ReadSB(bits), xmax = ReadSB(bits);
        int ymin = ReadSB(bits), ymax = ReadSB(bits);
        Align();
        return new SwfRect(xmin, xmax, ymin, ymax);
    }

    public SwfMatrix ReadMatrix()
    {
        Align();
        double sx = 1, sy = 1, r0 = 0, r1 = 0;

        if (ReadFlag())
        {
            int n = (int)ReadUB(5);
            sx = ReadFB(n);
            sy = ReadFB(n);
        }
        if (ReadFlag())
        {
            int n = (int)ReadUB(5);
            r0 = ReadFB(n);
            r1 = ReadFB(n);
        }
        int tn = (int)ReadUB(5);
        double tx = ReadSB(tn) / 20.0;
        double ty = ReadSB(tn) / 20.0;
        Align();

        return new SwfMatrix(sx, r0, r1, sy, tx, ty);
    }

    public SwfColor ReadRgb() => new(ReadUI8(), ReadUI8(), ReadUI8(), 255);

    public SwfColor ReadRgba()
    {
        byte r = ReadUI8(), g = ReadUI8(), b = ReadUI8(), a = ReadUI8();
        return new SwfColor(r, g, b, a);
    }

    public SwfColor ReadArgb()
    {
        byte a = ReadUI8(), r = ReadUI8(), g = ReadUI8(), b = ReadUI8();
        return new SwfColor(r, g, b, a);
    }

    public SwfColorTransform ReadColorTransform() => ReadColorTransform(false);

    public SwfColorTransform ReadColorTransformWithAlpha() => ReadColorTransform(true);

    private SwfColorTransform ReadColorTransform(bool withAlpha)
    {
        Align();
        bool hasAdd = ReadFlag();
        bool hasMul = ReadFlag();
        int n = (int)ReadUB(4);

        double rm = 1, gm = 1, bm = 1, am = 1;
        double ra = 0, ga = 0, ba = 0, aa = 0;

        if (hasMul)
        {
            rm = ReadSB(n) / 256.0;
            gm = ReadSB(n) / 256.0;
            bm = ReadSB(n) / 256.0;
            if (withAlpha) am = ReadSB(n) / 256.0;
        }
        if (hasAdd)
        {
            ra = ReadSB(n);
            ga = ReadSB(n);
            ba = ReadSB(n);
            if (withAlpha) aa = ReadSB(n);
        }
        Align();

        return new SwfColorTransform(rm, gm, bm, am, ra, ga, ba, aa);
    }
}

internal static class SwfEncoding
{
    public static readonly Encoding Cp1251;

    static SwfEncoding()
    {
#if NETSTANDARD
        Encoding found = null;
        try { found = Encoding.GetEncoding(1251); } catch { }
        Cp1251 = found ?? new Cp1251Fallback();
#else
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
#endif
    }
}

internal sealed class Cp1251Fallback : Encoding
{
    private static readonly ushort[] High =
    {
        0x0402, 0x0403, 0x201A, 0x0453, 0x201E, 0x2026, 0x2020, 0x2021, 0x20AC, 0x2030, 0x0409, 0x2039, 0x040A, 0x040C, 0x040B, 0x040F,
        0x0452, 0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014, 0x0098, 0x2122, 0x0459, 0x203A, 0x045A, 0x045C, 0x045B, 0x045F,
        0x00A0, 0x040E, 0x045E, 0x0408, 0x00A4, 0x0490, 0x00A6, 0x00A7, 0x0401, 0x00A9, 0x0404, 0x00AB, 0x00AC, 0x00AD, 0x00AE, 0x0407,
        0x00B0, 0x00B1, 0x0406, 0x0456, 0x0491, 0x00B5, 0x00B6, 0x00B7, 0x0451, 0x2116, 0x0454, 0x00BB, 0x0458, 0x0405, 0x0455, 0x0457,
    };

    public static char Map(byte b) => b < 0x80 ? (char)b : b < 0xC0 ? (char)High[b - 0x80] : (char)(0x0410 + (b - 0xC0));

    private static byte Unmap(char c)
    {
        if (c < 0x80) return (byte)c;
        if (c >= 0x0410 && c <= 0x044F) return (byte)(0xC0 + (c - 0x0410));
        for (int i = 0; i < High.Length; i++) if (High[i] == c) return (byte)(0x80 + i);
        return (byte)'?';
    }

    public override int GetByteCount(char[] chars, int index, int count) => count;
    public override int GetCharCount(byte[] bytes, int index, int count) => count;
    public override int GetMaxByteCount(int charCount) => charCount;
    public override int GetMaxCharCount(int byteCount) => byteCount;

    public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
    {
        for (int i = 0; i < charCount; i++) bytes[byteIndex + i] = Unmap(chars[charIndex + i]);
        return charCount;
    }

    public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
    {
        for (int i = 0; i < byteCount; i++) chars[charIndex + i] = Map(bytes[byteIndex + i]);
        return byteCount;
    }
}
