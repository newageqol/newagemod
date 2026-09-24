using UnityEngine;

namespace NewAge2D;

internal sealed class Silhouette
{
    private const int Block = 4;
    private const int Solid = 48;

    private readonly int _wide;
    private readonly int _high;
    private readonly byte[] _bits;

    private Silhouette(int wide, int high, byte[] bits)
    {
        _wide = wide;
        _high = high;
        _bits = bits;
    }

    internal static Silhouette From(DollPicture picture)
    {
        if (picture == null || picture.Width <= 0 || picture.Height <= 0) return null;
        try
        {
            int wide = (picture.Width + Block - 1) / Block;
            int high = (picture.Height + Block - 1) / Block;
            var solid = new bool[wide * high];
            if (picture.Dxt != null)
            {
                if (picture.Width % Block != 0 || picture.Height % Block != 0) return null;
                if (!Dxt(picture.Dxt, wide, high, solid)) return null;
            }
            else if (picture.Rgba != null && picture.Rgba.Length >= picture.Width * picture.Height * 4)
                Rgba(picture.Rgba, picture.Width, picture.Height, wide, solid);
            else return null;
            var bits = new byte[(wide * high + 7) / 8];
            for (int y = 0; y < high; y++)
                for (int x = 0; x < wide; x++)
                {
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < wide && ny < high && solid[ny * wide + nx]) near = true;
                        }
                    if (near) bits[(y * wide + x) >> 3] |= (byte)(1 << ((y * wide + x) & 7));
                }
            return new Silhouette(wide, high, bits);
        }
        catch
        {
            return null;
        }
    }

    internal bool At(float u, float v)
    {
        int x = Mathf.Clamp((int)(u * _wide), 0, _wide - 1);
        int y = Mathf.Clamp((int)(v * _high), 0, _high - 1);
        int i = y * _wide + x;
        return (_bits[i >> 3] & (1 << (i & 7))) != 0;
    }

    private static void Rgba(byte[] rgba, int width, int height, int wide, bool[] solid)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            int cell = (y / Block) * wide;
            for (int x = 0; x < width; x++)
                if (rgba[row + x * 4 + 3] > Solid) solid[cell + x / Block] = true;
        }
    }

    private static bool Dxt(byte[] dxt, int wide, int high, bool[] solid)
    {
        if (dxt.Length < wide * high * 16) return false;
        var palette = new int[8];
        for (int block = 0; block < wide * high; block++)
        {
            int at = block * 16;
            int a0 = dxt[at];
            int a1 = dxt[at + 1];
            if (a0 > a1 && a0 <= Solid) continue;
            palette[0] = a0;
            palette[1] = a1;
            if (a0 > a1)
                for (int k = 1; k <= 6; k++) palette[k + 1] = ((7 - k) * a0 + k * a1) / 7;
            else
            {
                for (int k = 1; k <= 4; k++) palette[k + 1] = ((5 - k) * a0 + k * a1) / 5;
                palette[6] = 0;
                palette[7] = 255;
            }
            ulong index = 0;
            for (int b = 0; b < 6; b++) index |= (ulong)dxt[at + 2 + b] << (8 * b);
            for (int t = 0; t < 16; t++)
                if (palette[(int)((index >> (3 * t)) & 7)] > Solid) { solid[block] = true; break; }
        }
        return true;
    }
}
