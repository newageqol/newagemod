using System;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Icons
    {
        private const int Side = 48;
        private const int Wide = 72;

        private static Sprite _drop, _bolt, _spark, _enter, _bin, _cap, _head, _mask, _mail, _burst, _scroll, _quest;
        private static Sprite _star, _starEmpty;
        private static Sprite _blades, _downward, _hex, _disc;

        private static readonly Vector2[] Zigzag =
        {
            new Vector2(0.32f, 1f), new Vector2(-0.52f, 0.08f), new Vector2(-0.06f, 0.08f),
            new Vector2(-0.32f, -1f), new Vector2(0.52f, -0.06f), new Vector2(0.06f, -0.06f),
        };

        private static readonly System.Collections.Generic.Dictionary<string, Sprite> Drawn =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        private static string[] _names;

        internal static Sprite Flash(string name)
        {
            Sprite sprite;
            if (Drawn.TryGetValue(name, out sprite) && sprite != null && sprite.texture != null) return sprite;
            sprite = null;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                if (_names == null) _names = asm.GetManifestResourceNames();
                string res = null;
                foreach (var one in _names)
                    if (one.EndsWith("." + name + ".png", StringComparison.OrdinalIgnoreCase)) { res = one; break; }
                if (res != null)
                    using (var stream = asm.GetManifestResourceStream(res))
                    using (var box = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(box);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                        if (ImageConversion.LoadImage(texture, box.ToArray()))
                            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                        else UnityEngine.Object.Destroy(texture);
                    }
                if (sprite == null) Plugin.Trace("[значки] картинка «" + name + "» не нашлась");
            }
            catch (Exception e) { Plugin.Trace("[значки] " + name + ": " + e.Message); }
            Drawn[name] = sprite;
            return sprite;
        }

        private static bool Gone(Sprite sprite)
        {
            return sprite == null || sprite.texture == null;
        }

        internal static Sprite Heart() => FighterHint.Heart();

        internal static Sprite Drop()
        {
            if (Gone(_drop))
                _drop = Shape((x, y) =>
                {
                    float dx = x, dy = y + 0.32f;
                    if (dx * dx + dy * dy <= 0.55f * 0.55f) return true;
                    if (y < -0.32f || y > 0.96f) return false;
                    float half = 0.55f * (0.96f - y) / 1.28f;
                    return Mathf.Abs(x) <= half;
                });
            return _drop;
        }

        internal static Sprite Bolt()
        {
            if (Gone(_bolt)) _bolt = Shape((x, y) => Inside(Zigzag, x, y));
            return _bolt;
        }

        internal static Sprite Spark()
        {
            if (Gone(_spark))
                _spark = Shape((x, y) =>
                {
                    float ax = Mathf.Abs(x) / 0.98f, ay = Mathf.Abs(y) / 0.98f;
                    return Mathf.Pow(ax, 0.6f) + Mathf.Pow(ay, 0.6f) <= 1f;
                });
            return _spark;
        }

        internal static Sprite Enter()
        {
            if (Gone(_enter))
                _enter = Shape((x, y) =>
                {
                    if (x >= -0.45f && x <= 0.5f && Mathf.Abs(y + 0.15f) <= 0.13f) return true;
                    if (x >= 0.5f && x <= 0.76f && y >= -0.28f && y <= 0.62f) return true;
                    if (x >= -0.95f && x <= -0.45f) return Mathf.Abs(y + 0.15f) <= (x + 0.95f) * 0.9f;
                    return false;
                });
            return _enter;
        }

        internal static Sprite Star(bool filled)
        {
            if (filled)
            {
                if (Gone(_star)) _star = Shape((x, y) => InStar(x, y + 0.06f, 0.98f));
                return _star;
            }
            if (Gone(_starEmpty)) _starEmpty = Shape((x, y) => InStar(x, y + 0.06f, 0.98f) && !InStar(x, y + 0.02f, 0.62f));
            return _starEmpty;
        }

        private static bool InStar(float x, float y, float outer)
        {
            float inner = outer * 0.42f;
            bool inside = false;
            for (int i = 0, j = 9; i < 10; j = i++)
            {
                float ri = i % 2 == 0 ? outer : inner;
                float rj = j % 2 == 0 ? outer : inner;
                float ai = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float aj = Mathf.PI * 0.5f + j * Mathf.PI / 5f;
                float xi = Mathf.Cos(ai) * ri, yi = Mathf.Sin(ai) * ri;
                float xj = Mathf.Cos(aj) * rj, yj = Mathf.Sin(aj) * rj;
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }

        internal static Sprite Bin()
        {
            if (Gone(_bin))
                _bin = Shape((x, y) =>
                {
                    if (Mathf.Abs(x) <= 0.72f && y >= 0.36f && y <= 0.56f) return true;
                    if (Mathf.Abs(x) <= 0.22f && y >= 0.56f && y <= 0.76f) return true;
                    if (Mathf.Abs(x) <= 0.55f && y >= -0.88f && y <= 0.24f)
                    {
                        bool slot = Mathf.Abs(Mathf.Abs(x) - 0.2f) <= 0.06f && y >= -0.72f && y <= 0.08f;
                        return !slot;
                    }
                    return false;
                });
            return _bin;
        }

        internal static Sprite Mail()
        {
            if (Gone(_mail))
                _mail = Shape((x, y) =>
                {
                    if (Mathf.Abs(x) > 0.92f || Mathf.Abs(y) > 0.64f) return false;
                    float flap = 0.64f - Mathf.Abs(x) * 0.7f;
                    if (Mathf.Abs(y - flap) <= 0.1f && y >= -0.05f) return false;
                    float lower = -0.64f + Mathf.Abs(x) * 0.7f;
                    if (Mathf.Abs(y - lower) <= 0.08f && y <= -0.05f && Mathf.Abs(x) > 0.25f) return false;
                    return true;
                });
            return _mail;
        }

        internal static Sprite Burst()
        {
            if (Gone(_burst))
                _burst = Shape((x, y) =>
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    if (r <= 0.26f) return true;
                    if (r > 0.98f || r < 0.4f) return false;
                    float ang = Mathf.Atan2(y, x);
                    float ray = Mathf.Abs(Mathf.Sin(ang * 4f));
                    return ray > 0.72f - (r - 0.4f) * 0.9f;
                });
            return _burst;
        }

        internal static Sprite Blades()
        {
            if (Gone(_blades))
                _blades = Shape((x, y) =>
                {
                    if (Mathf.Abs(x) > 0.9f || Mathf.Abs(y) > 0.9f) return false;
                    return Mathf.Abs(x - y) <= 0.24f || Mathf.Abs(x + y) <= 0.24f;
                });
            return _blades;
        }

        internal static Sprite Downward()
        {
            if (Gone(_downward))
                _downward = Shape((x, y) =>
                {
                    if (y >= -0.18f && y <= 0.9f) return Mathf.Abs(x) <= 0.2f;
                    if (y >= -0.9f && y < -0.18f) return Mathf.Abs(x) <= 0.62f * (y + 0.9f) / 0.72f;
                    return false;
                });
            return _downward;
        }

        internal static Sprite Hex()
        {
            if (Gone(_hex))
                _hex = Shape((x, y) =>
                {
                    const float side = 0.86f;
                    if (Mathf.Abs(x) > side) return false;
                    if (Mathf.Abs(0.5f * x + 0.8660254f * y) > side) return false;
                    if (Mathf.Abs(0.5f * x - 0.8660254f * y) > side) return false;
                    return true;
                });
            return _hex;
        }

        internal static Sprite Disc()
        {
            if (Gone(_disc)) _disc = Shape((x, y) => x * x + y * y <= 0.94f * 0.94f);
            return _disc;
        }

        private static Sprite Paint(Func<float, float, Color32> ink)
        {
            try
            {
                var tex = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
                var px = new Color32[Side * Side];
                const int sub = 3;
                for (int py = 0; py < Side; py++)
                    for (int pxx = 0; pxx < Side; pxx++)
                    {
                        float r = 0f, g = 0f, b = 0f, a = 0f;
                        for (int sy = 0; sy < sub; sy++)
                            for (int sx = 0; sx < sub; sx++)
                            {
                                float x = ((pxx + (sx + 0.5f) / sub) / Side) * 2f - 1f;
                                float y = ((py + (sy + 0.5f) / sub) / Side) * 2f - 1f;
                                var one = ink(x, y);
                                float weight = one.a / 255f;
                                r += one.r * weight;
                                g += one.g * weight;
                                b += one.b * weight;
                                a += weight;
                            }
                        int taken = sub * sub;
                        byte alpha = (byte)Mathf.RoundToInt(255f * a / taken);
                        px[py * Side + pxx] = a < 0.001f
                            ? new Color32(0, 0, 0, 0)
                            : new Color32((byte)Mathf.Clamp(r / a, 0f, 255f), (byte)Mathf.Clamp(g / a, 0f, 255f),
                                          (byte)Mathf.Clamp(b / a, 0f, 255f), alpha);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(tex, new Rect(0f, 0f, Side, Side), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Trace("[значки] цветной значок: " + e.Message); return null; }
        }

        internal static Sprite Quest()
        {
            if (Gone(_quest)) _quest = Colored(QuestDot);
            return _quest;
        }

        private static bool Roll(float x, float y, float middle, out float edge)
        {
            const float half = 0.60f;
            const float thick = 0.20f;
            float dx = Mathf.Max(0f, Mathf.Abs(x) - half);
            float dy = y - middle;
            float away = Mathf.Sqrt(dx * dx + dy * dy);
            edge = away / thick;
            return away <= thick;
        }

        private static Color32? QuestDot(float x, float y)
        {
            for (int end = 0; end < 2; end++)
            {
                float middle = end == 0 ? 0.70f : -0.70f;
                float edge;
                if (!Roll(x, y, middle, out edge)) continue;
                if (edge > 0.84f) return new Color32(70, 48, 22, 255);
                float deep = Mathf.Abs((y - middle) / 0.20f);
                return new Color32(Mix(232, 152, deep), Mix(190, 112, deep), Mix(108, 50, deep), 255);
            }
            if (Mathf.Abs(x) > 0.60f || Mathf.Abs(y) > 0.74f) return null;
            if (Mathf.Abs(x) > 0.53f) return new Color32(150, 118, 74, 255);
            for (int line = 0; line < 4; line++)
            {
                float at = 0.40f - line * 0.26f;
                float wide = line == 3 ? 0.24f : 0.40f;
                if (Mathf.Abs(y - at) <= 0.055f && Mathf.Abs(x) <= wide) return new Color32(96, 68, 38, 255);
            }
            float shade = Mathf.InverseLerp(0.74f, -0.74f, y);
            return new Color32(Mix(246, 214, shade), Mix(232, 190, shade), Mix(198, 146, shade), 255);
        }

        private static byte Mix(float from, float to, float part)
        {
            return (byte)Mathf.RoundToInt(Mathf.Lerp(from, to, Mathf.Clamp01(part)));
        }

        private static Sprite Colored(Func<float, float, Color32?> pick)
        {
            try
            {
                var tex = new Texture2D(Wide, Wide, TextureFormat.RGBA32, false);
                var px = new Color32[Wide * Wide];
                const int sub = 4;
                for (int py = 0; py < Wide; py++)
                    for (int pxx = 0; pxx < Wide; pxx++)
                    {
                        float r = 0f, g = 0f, b = 0f;
                        int hits = 0;
                        for (int sy = 0; sy < sub; sy++)
                            for (int sx = 0; sx < sub; sx++)
                            {
                                float x = ((pxx + (sx + 0.5f) / sub) / Wide) * 2f - 1f;
                                float y = ((py + (sy + 0.5f) / sub) / Wide) * 2f - 1f;
                                var got = pick(x, y);
                                if (!got.HasValue) continue;
                                r += got.Value.r;
                                g += got.Value.g;
                                b += got.Value.b;
                                hits++;
                            }
                        if (hits == 0) { px[py * Wide + pxx] = new Color32(0, 0, 0, 0); continue; }
                        byte a = (byte)Mathf.RoundToInt(255f * hits / (sub * sub));
                        px[py * Wide + pxx] = new Color32((byte)(r / hits), (byte)(g / hits), (byte)(b / hits), a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(tex, new Rect(0f, 0f, Wide, Wide), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Trace("[значки] цветной: " + e.Message); return null; }
        }

        internal static Sprite Scroll()
        {
            if (Gone(_scroll))
                _scroll = Shape((x, y) =>
                {
                    float ax = Mathf.Abs(x);
                    float ay = Mathf.Abs(y);
                    if (ax <= 0.88f && Mathf.Abs(ay - 0.76f) <= 0.16f) return true;
                    if (ax > 0.62f || ay > 0.62f) return false;
                    for (int line = -1; line <= 1; line++)
                        if (Mathf.Abs(y - line * 0.3f) <= 0.06f && ax <= 0.44f) return false;
                    return true;
                });
            return _scroll;
        }

        internal static Sprite Mask()
        {
            if (Gone(_mask))
                _mask = Shape((x, y) =>
                {
                    float fx = x / 0.74f, fy = y / 0.94f;
                    if (fx * fx + fy * fy > 1f) return false;
                    float lx = (x + 0.3f) / 0.2f, ly = (y - 0.3f) / 0.15f;
                    if (lx * lx + ly * ly <= 1f) return false;
                    float rx = (x - 0.3f) / 0.2f, ry = (y - 0.3f) / 0.15f;
                    if (rx * rx + ry * ry <= 1f) return false;
                    float mx = x / 0.44f, my = (y + 0.28f) / 0.26f;
                    if (y <= -0.28f && mx * mx + my * my <= 1f) return false;
                    return true;
                });
            return _mask;
        }

        internal static Sprite Head()
        {
            if (Gone(_head))
                _head = Shape((x, y) =>
                {
                    float hx = x + 0.3f, hy = y - 0.32f;
                    if (hx * hx + hy * hy <= 0.34f * 0.34f) return true;
                    float bx = (x + 0.3f) / 0.62f, by = (y + 0.98f) / 0.9f;
                    if (y <= -0.12f && y >= -0.98f && bx * bx + by * by <= 1f) return true;
                    if (x >= 0.3f && x <= 0.95f)
                        for (int i = 0; i < 3; i++)
                            if (Mathf.Abs(y - (0.58f - i * 0.3f)) <= 0.07f && x <= 0.95f - i * 0.12f) return true;
                    return false;
                });
            return _head;
        }

        internal static Sprite Mushroom()
        {
            if (Gone(_cap))
                _cap = Shape((x, y) =>
                {
                    float cx = x / 0.95f, cy = (y - 0.05f) / 0.8f;
                    if (y >= 0.05f && cx * cx + cy * cy <= 1f) return true;
                    if (y < 0.05f && y >= -0.1f && Mathf.Abs(x) <= 0.95f) return true;
                    if (y < -0.1f && y >= -0.92f && Mathf.Abs(x) <= 0.34f - Mathf.Max(0f, -0.72f - y) * 1.2f) return true;
                    return false;
                });
            return _cap;
        }

        private static bool Inside(Vector2[] poly, float x, float y)
        {
            bool hit = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                var a = poly[i];
                var b = poly[j];
                if ((a.y > y) == (b.y > y)) continue;
                float cross = (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x;
                if (x < cross) hit = !hit;
            }
            return hit;
        }

        private static Sprite Shape(Func<float, float, bool> inside)
        {
            try
            {
                var tex = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
                var px = new Color32[Side * Side];
                const int sub = 3;
                for (int py = 0; py < Side; py++)
                    for (int pxx = 0; pxx < Side; pxx++)
                    {
                        int hits = 0;
                        for (int sy = 0; sy < sub; sy++)
                            for (int sx = 0; sx < sub; sx++)
                            {
                                float x = ((pxx + (sx + 0.5f) / sub) / Side) * 2f - 1f;
                                float y = ((py + (sy + 0.5f) / sub) / Side) * 2f - 1f;
                                if (inside(x, y)) hits++;
                            }
                        byte a = (byte)Mathf.RoundToInt(255f * hits / (sub * sub));
                        px[py * Side + pxx] = new Color32(255, 255, 255, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(tex, new Rect(0f, 0f, Side, Side), new Vector2(0.5f, 0.5f), 100f);
            }
            catch (Exception e) { Plugin.Trace("[значки] " + e.Message); return null; }
        }
    }
}
