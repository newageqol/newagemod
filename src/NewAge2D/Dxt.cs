namespace NewAge2D;

internal static class Dxt
{
    private sealed class Scratch
    {
        public readonly int[] R = new int[16];
        public readonly int[] G = new int[16];
        public readonly int[] B = new int[16];
        public readonly int[] A = new int[16];
        public readonly int[] PaletteR = new int[4];
        public readonly int[] PaletteG = new int[4];
        public readonly int[] PaletteB = new int[4];
        public readonly int[] Alpha8 = new int[8];
        public readonly int[] Alpha6 = new int[8];
        public readonly int[] Indices8 = new int[16];
        public readonly int[] Indices6 = new int[16];
        public byte[] LevelA;
        public byte[] LevelB;
        public bool[] Solid;
    }

    [ThreadStatic] private static Scratch _scratch;

    private static Scratch Work => _scratch ??= new Scratch();

    public static void Pack(DollSequence sequence)
    {
        if (sequence?.Frames == null) return;
        foreach (var picture in sequence.Frames) PackOne(picture);
    }

    public static void PackOne(DollPicture picture)
    {
        if (picture == null || picture.Dxt != null || picture.Rgba == null || picture.Raw) return;
        Bleed(picture.Rgba, picture.Width, picture.Height);
        picture.Dxt = EncodeChain(picture.Rgba, picture.Width, picture.Height, out int mips, picture.Pooled, out int size);
        picture.DxtSize = size;
        picture.Mips = mips;
        if (picture.Pooled) Pixels.Return(picture.Rgba);
        picture.Rgba = null;
    }

    private static int BlockBytes(int width, int height) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 16;

    public static byte[] EncodeChain(byte[] rgba, int width, int height, out int mips) =>
        EncodeChain(rgba, width, height, out mips, false, out _);

    private static byte[] EncodeChain(byte[] rgba, int width, int height, out int mips, bool pooled, out int total)
    {
        total = 0;
        mips = 0;
        for (int w = width, h = height; ; w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
        {
            total += BlockBytes(w, h);
            mips++;
            if (w == 1 && h == 1) break;
        }
        var chain = pooled ? Pixels.Rent(total) : new byte[total];
        var work = Work;
        var level = rgba;
        int lw = width, lh = height, offset = 0;
        while (true)
        {
            offset += EncodeInto(level, lw, lh, chain, offset, work);
            if (lw == 1 && lh == 1) break;
            int nw = Math.Max(1, lw / 2);
            int nh = Math.Max(1, lh / 2);
            int size = nw * nh * 4;
            byte[] target;
            if (ReferenceEquals(level, work.LevelA))
            {
                if (work.LevelB == null || work.LevelB.Length < size) work.LevelB = new byte[size];
                target = work.LevelB;
            }
            else
            {
                if (work.LevelA == null || work.LevelA.Length < size) work.LevelA = new byte[size];
                target = work.LevelA;
            }
            Downsample(level, lw, lh, target, nw, nh);
            level = target;
            lw = nw;
            lh = nh;
        }
        return chain;
    }

    public static void Bleed(byte[] rgba, int width, int height)
    {
        int count = width * height;
        var work = Work;
        if (work.Solid == null || work.Solid.Length < count) work.Solid = new bool[count];
        var solid = work.Solid;
        for (int i = 0; i < count; i++) solid[i] = rgba[i * 4 + 3] > 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                if (solid[i]) continue;
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= width) continue;
                        int j = yy * width + xx;
                        if (!solid[j]) continue;
                        r += rgba[j * 4];
                        g += rgba[j * 4 + 1];
                        b += rgba[j * 4 + 2];
                        n++;
                    }
                }
                if (n == 0) continue;
                rgba[i * 4] = (byte)(r / n);
                rgba[i * 4 + 1] = (byte)(g / n);
                rgba[i * 4 + 2] = (byte)(b / n);
            }
        }
    }

    private static void Downsample(byte[] source, int width, int height, byte[] target, int newWidth, int newHeight)
    {
        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                long r = 0, g = 0, b = 0, plainR = 0, plainG = 0, plainB = 0;
                int a = 0;
                for (int dy = 0; dy < 2; dy++)
                {
                    int sy = Math.Min(height - 1, y * 2 + dy);
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(width - 1, x * 2 + dx);
                        int p = (sy * width + sx) * 4;
                        int alpha = source[p + 3];
                        r += source[p] * alpha;
                        g += source[p + 1] * alpha;
                        b += source[p + 2] * alpha;
                        plainR += source[p];
                        plainG += source[p + 1];
                        plainB += source[p + 2];
                        a += alpha;
                    }
                }
                int q = (y * newWidth + x) * 4;
                if (a > 0)
                {
                    target[q] = (byte)(r / a);
                    target[q + 1] = (byte)(g / a);
                    target[q + 2] = (byte)(b / a);
                }
                else
                {
                    target[q] = (byte)(plainR / 4);
                    target[q + 1] = (byte)(plainG / 4);
                    target[q + 2] = (byte)(plainB / 4);
                }
                target[q + 3] = (byte)((a + 2) / 4);
            }
        }
    }

    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        var output = new byte[BlockBytes(width, height)];
        EncodeInto(rgba, width, height, output, 0, Work);
        return output;
    }

    private static int EncodeInto(byte[] rgba, int width, int height, byte[] output, int start, Scratch work)
    {
        int blocksX = Math.Max(1, (width + 3) / 4);
        int blocksY = Math.Max(1, (height + 3) / 4);
        var r = work.R;
        var g = work.G;
        var b = work.B;
        var a = work.A;
        var pr = work.PaletteR;
        var pg = work.PaletteG;
        var pb = work.PaletteB;
        var pa = work.Alpha8;
        var p6 = work.Alpha6;
        var indices8 = work.Indices8;
        var indices6 = work.Indices6;
        int o = start;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0, minA = 255, maxA = 0;
                bool any = false;
                for (int y = 0; y < 4; y++)
                {
                    int sy = Math.Min(height - 1, by * 4 + y);
                    for (int x = 0; x < 4; x++)
                    {
                        int sx = Math.Min(width - 1, bx * 4 + x);
                        int i = y * 4 + x;
                        int p = (sy * width + sx) * 4;
                        r[i] = rgba[p];
                        g[i] = rgba[p + 1];
                        b[i] = rgba[p + 2];
                        a[i] = rgba[p + 3];
                        if (a[i] < minA) minA = a[i];
                        if (a[i] > maxA) maxA = a[i];
                        if (a[i] == 0) continue;
                        any = true;
                        if (r[i] < minR) minR = r[i];
                        if (r[i] > maxR) maxR = r[i];
                        if (g[i] < minG) minG = g[i];
                        if (g[i] > maxG) maxG = g[i];
                        if (b[i] < minB) minB = b[i];
                        if (b[i] > maxB) maxB = b[i];
                    }
                }
                if (!any)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        if (r[i] < minR) minR = r[i];
                        if (r[i] > maxR) maxR = r[i];
                        if (g[i] < minG) minG = g[i];
                        if (g[i] > maxG) maxG = g[i];
                        if (b[i] < minB) minB = b[i];
                        if (b[i] > maxB) maxB = b[i];
                    }
                }
                int insetR = (maxR - minR) >> 4;
                int insetG = (maxG - minG) >> 4;
                int insetB = (maxB - minB) >> 4;
                minR += insetR;
                maxR -= insetR;
                minG += insetG;
                maxG -= insetG;
                minB += insetB;
                maxB -= insetB;

                int c0 = Rgb565(maxR, maxG, maxB);
                int c1 = Rgb565(minR, minG, minB);
                if (c0 < c1)
                {
                    int t = c0;
                    c0 = c1;
                    c1 = t;
                }
                Expand(c0, out pr[0], out pg[0], out pb[0]);
                Expand(c1, out pr[1], out pg[1], out pb[1]);
                pr[2] = (2 * pr[0] + pr[1]) / 3;
                pg[2] = (2 * pg[0] + pg[1]) / 3;
                pb[2] = (2 * pb[0] + pb[1]) / 3;
                pr[3] = (pr[0] + 2 * pr[1]) / 3;
                pg[3] = (pg[0] + 2 * pg[1]) / 3;
                pb[3] = (pb[0] + 2 * pb[1]) / 3;

                uint colorBits = 0;
                if (c0 != c1)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int best = 0;
                        int bestDistance = int.MaxValue;
                        for (int k = 0; k < 4; k++)
                        {
                            int dr = r[i] - pr[k];
                            int dg = g[i] - pg[k];
                            int db = b[i] - pb[k];
                            int distance = dr * dr + dg * dg + db * db;
                            if (distance < bestDistance)
                            {
                                bestDistance = distance;
                                best = k;
                            }
                        }
                        colorBits |= (uint)best << (2 * i);
                    }
                }

                int alpha0 = maxA, alpha1 = minA;
                ulong alphaBits = 0;
                if (maxA > minA)
                {
                    pa[0] = maxA;
                    pa[1] = minA;
                    for (int k = 1; k <= 6; k++) pa[k + 1] = ((7 - k) * maxA + k * minA) / 7;
                    long error8 = Match(a, pa, indices8);

                    int innerMin = 255, innerMax = 0;
                    for (int i = 0; i < 16; i++)
                    {
                        if (a[i] == 0 || a[i] == 255) continue;
                        if (a[i] < innerMin) innerMin = a[i];
                        if (a[i] > innerMax) innerMax = a[i];
                    }
                    if (innerMin > innerMax)
                    {
                        innerMin = 0;
                        innerMax = 0;
                    }
                    p6[0] = innerMin;
                    p6[1] = innerMax;
                    for (int k = 1; k <= 4; k++) p6[k + 1] = ((5 - k) * innerMin + k * innerMax) / 5;
                    p6[6] = 0;
                    p6[7] = 255;
                    long error6 = Match(a, p6, indices6);

                    int[] chosen = indices8;
                    if (error6 < error8)
                    {
                        chosen = indices6;
                        alpha0 = innerMin;
                        alpha1 = innerMax;
                    }
                    for (int i = 0; i < 16; i++) alphaBits |= (ulong)chosen[i] << (3 * i);
                }

                output[o] = (byte)alpha0;
                output[o + 1] = (byte)alpha1;
                for (int k = 0; k < 6; k++) output[o + 2 + k] = (byte)(alphaBits >> (8 * k));
                output[o + 8] = (byte)c0;
                output[o + 9] = (byte)(c0 >> 8);
                output[o + 10] = (byte)c1;
                output[o + 11] = (byte)(c1 >> 8);
                output[o + 12] = (byte)colorBits;
                output[o + 13] = (byte)(colorBits >> 8);
                output[o + 14] = (byte)(colorBits >> 16);
                output[o + 15] = (byte)(colorBits >> 24);
                o += 16;
            }
        }
        return blocksX * blocksY * 16;
    }

    private static long Match(int[] alpha, int[] palette, int[] indices)
    {
        long error = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int distance = Math.Abs(alpha[i] - palette[k]);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = k;
                }
            }
            indices[i] = best;
            error += (long)bestDistance * bestDistance;
        }
        return error;
    }

    private static int Rgb565(int r, int g, int b) =>
        (((r * 31 + 127) / 255) << 11) | (((g * 63 + 127) / 255) << 5) | ((b * 31 + 127) / 255);

    private static void Expand(int color, out int r, out int g, out int b)
    {
        int r5 = (color >> 11) & 31;
        int g6 = (color >> 5) & 63;
        int b5 = color & 31;
        r = (r5 << 3) | (r5 >> 2);
        g = (g6 << 2) | (g6 >> 4);
        b = (b5 << 3) | (b5 >> 2);
    }
}
