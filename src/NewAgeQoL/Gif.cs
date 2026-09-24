using System.IO;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Gif
    {
        internal static Texture2D Read(byte[] data)
        {
            if (data == null || data.Length < 13 || data[0] != 'G' || data[1] != 'I' || data[2] != 'F') return null;
            int width = Word(data, 6);
            int height = Word(data, 8);
            if (width <= 0 || height <= 0 || width > 1024 || height > 1024) return null;
            int flags = data[10];
            int at = 13;
            Color32[] global = null;
            if ((flags & 0x80) != 0)
            {
                int size = 2 << (flags & 7);
                global = Palette(data, at, size);
                at += size * 3;
            }
            int clear = -1;
            while (at < data.Length)
            {
                int kind = data[at++];
                if (kind == 0x21)
                {
                    if (at >= data.Length) return null;
                    int label = data[at++];
                    if (label == 0xF9 && at + 4 < data.Length && (data[at + 1] & 1) != 0) clear = data[at + 4];
                    at = Skip(data, at);
                    continue;
                }
                if (kind != 0x2C || at + 9 > data.Length) return null;
                int left = Word(data, at);
                int top = Word(data, at + 2);
                int w = Word(data, at + 4);
                int h = Word(data, at + 6);
                int packed = data[at + 8];
                at += 9;
                var table = global;
                if ((packed & 0x80) != 0)
                {
                    int size = 2 << (packed & 7);
                    table = Palette(data, at, size);
                    at += size * 3;
                }
                if (table == null || w <= 0 || h <= 0 || at >= data.Length) return null;
                int least = data[at++];
                if (least < 2 || least > 11) return null;
                var codes = Lzw(Gather(data, ref at), least, w * h);
                int[] rows = Rows(h, (packed & 0x40) != 0);
                var pixels = new Color32[width * height];
                for (int i = 0; i < w * h; i++)
                {
                    int index = codes[i];
                    if (index == clear || index >= table.Length) continue;
                    int x = left + i % w;
                    int y = top + rows[i / w];
                    if (x >= width || y >= height) continue;
                    pixels[(height - 1 - y) * width + x] = table[index];
                }
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.SetPixels32(pixels);
                texture.Apply(true);
                return texture;
            }
            return null;
        }

        private static int Word(byte[] data, int at) => at + 1 < data.Length ? data[at] | data[at + 1] << 8 : 0;

        private static Color32[] Palette(byte[] data, int at, int size)
        {
            if (at + size * 3 > data.Length) return null;
            var colors = new Color32[size];
            for (int i = 0; i < size; i++)
                colors[i] = new Color32(data[at + i * 3], data[at + i * 3 + 1], data[at + i * 3 + 2], 255);
            return colors;
        }

        private static int Skip(byte[] data, int at)
        {
            while (at < data.Length)
            {
                int n = data[at++];
                if (n == 0) break;
                at += n;
            }
            return at;
        }

        private static byte[] Gather(byte[] data, ref int at)
        {
            using (var stream = new MemoryStream())
            {
                while (at < data.Length)
                {
                    int n = data[at++];
                    if (n == 0) break;
                    if (at + n > data.Length) n = data.Length - at;
                    stream.Write(data, at, n);
                    at += n;
                }
                return stream.ToArray();
            }
        }

        private static int[] Rows(int h, bool interlaced)
        {
            var rows = new int[h];
            if (!interlaced)
            {
                for (int i = 0; i < h; i++) rows[i] = i;
                return rows;
            }
            int n = 0;
            int[] starts = { 0, 4, 2, 1 };
            int[] steps = { 8, 8, 4, 2 };
            for (int pass = 0; pass < 4; pass++)
                for (int y = starts[pass]; y < h && n < h; y += steps[pass]) rows[n++] = y;
            return rows;
        }

        private static byte[] Lzw(byte[] input, int least, int count)
        {
            var output = new byte[count];
            int clearCode = 1 << least;
            int end = clearCode + 1;
            int size = least + 1;
            int mask = (1 << size) - 1;
            int next = end + 1;
            var prefix = new short[4096];
            var suffix = new byte[4096];
            var stack = new byte[4097];
            for (int i = 0; i < clearCode; i++) { prefix[i] = -1; suffix[i] = (byte)i; }
            int bits = 0, datum = 0, old = -1, pos = 0, done = 0;
            byte first = 0;
            while (done < count)
            {
                while (bits < size)
                {
                    if (pos >= input.Length) return output;
                    datum |= input[pos++] << bits;
                    bits += 8;
                }
                int code = datum & mask;
                datum >>= size;
                bits -= size;
                if (code == clearCode)
                {
                    size = least + 1;
                    mask = (1 << size) - 1;
                    next = end + 1;
                    old = -1;
                    continue;
                }
                if (code == end) break;
                if (old == -1)
                {
                    if (code >= clearCode) break;
                    output[done++] = suffix[code];
                    old = code;
                    first = (byte)code;
                    continue;
                }
                if (code > next) break;
                int current = code;
                int top = 0;
                if (code == next)
                {
                    stack[top++] = first;
                    code = old;
                }
                while (code >= clearCode && top < 4096)
                {
                    stack[top++] = suffix[code];
                    code = prefix[code];
                }
                first = suffix[code];
                stack[top++] = first;
                if (next < 4096)
                {
                    prefix[next] = (short)old;
                    suffix[next] = first;
                    next++;
                    if ((next & mask) == 0 && next < 4096)
                    {
                        size++;
                        mask = (1 << size) - 1;
                    }
                }
                old = current;
                while (top > 0 && done < count) output[done++] = stack[--top];
            }
            return output;
        }
    }
}
