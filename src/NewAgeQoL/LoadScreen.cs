using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(Preloader), "Awake")]
    internal static class LoadScreenPatch
    {
        private static void Postfix(Preloader __instance)
        {
            try { LoadScreen.Attach(__instance); }
            catch (Exception e) { Plugin.Trace("[loading] screen: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Preloader), "Close")]
    internal static class LoadScreenClosePatch
    {
        private static readonly FieldInfo Current = AccessTools.Field(typeof(Preloader), "_preloader");

        private static void Prefix(out Preloader __state) => __state = Current?.GetValue(null) as Preloader;

        private static void Postfix(Preloader __state)
        {
            if (__state == null || Current?.GetValue(null) != null) return;
            var screen = __state.GetComponentInChildren<LoadScreen>(true);
            if (screen != null) screen.Finish();
        }
    }

    internal sealed class LoadScreen : MonoBehaviour
    {
        private const float Design = 1080f;
        private const float Small = 0.2f;
        private static readonly FieldInfo GemsField = AccessTools.Field(typeof(Preloader), "Gems");
        private static readonly FieldInfo HintField = AccessTools.Field(typeof(Preloader), "HintText");
        private static readonly Color Gold = new Color(0.788f, 0.643f, 0.361f);
        private static readonly Color Hot = new Color(0.945f, 0.82f, 0.541f);

        private static Texture2D _bg, _dot, _glow, _arc, _ticks, _runesOuter, _runesInner;
        private static readonly Dictionary<int, Texture2D> Rings = new Dictionary<int, Texture2D>();

        private Image _gems;
        private Transform _owner;
        private int _hidden;
        private bool _told;
        private RectTransform _root;
        private RectTransform _emblem;
        private CanvasGroup _group;
        private RectTransform _outer, _inner, _head;
        private Image _fill, _fillGlow;
        private Text _percent;
        private readonly List<RawImage> _lines = new List<RawImage>();
        private RawImage _halo;
        private Spark[] _sparks;
        private float _shown;
        private float _finish = -1f;
        private float _start;

        private sealed class Spark
        {
            public RectTransform Rt;
            public RawImage Img;
            public float X, Y, V, R, A, W;
        }

        internal static void Attach(Preloader preloader)
        {
            if (preloader.GetComponentInChildren<LoadScreen>(true) != null) return;
            var go = new GameObject("QoLLoadScreen", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(preloader.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            var screen = go.AddComponent<LoadScreen>();
            screen.Build(preloader, rt);
        }

        private void Build(Preloader preloader, RectTransform root)
        {
            _root = root;
            _owner = preloader.transform;
            _start = Time.unscaledTime;
            _gems = GemsField?.GetValue(preloader) as Image;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = true;

            var bg = Raw("bg", root, Background(), Color.white);
            Stretch(bg.rectTransform);

            _sparks = new Spark[46];
            var sparkLayer = new GameObject("sparks", typeof(RectTransform)).GetComponent<RectTransform>();
            sparkLayer.SetParent(root, false);
            Stretch(sparkLayer);
            for (int i = 0; i < _sparks.Length; i++)
            {
                var img = Raw("spark", sparkLayer, Dot(), Hot);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                var s = new Spark { Rt = img.rectTransform, Img = img };
                Respawn(s, true);
                s.Rt.anchoredPosition = new Vector2(s.X, s.Y);
                s.Rt.sizeDelta = Vector2.zero;
                _sparks[i] = s;
            }

            _emblem = new GameObject("emblem", typeof(RectTransform)).GetComponent<RectTransform>();
            _emblem.SetParent(root, false);
            _emblem.anchorMin = _emblem.anchorMax = new Vector2(0.5f, 0.55f);
            _emblem.sizeDelta = new Vector2(600, 600);
            _emblem.localScale = Vector3.zero;

            _halo = Raw("halo", _emblem, Glow(), new Color(Gold.r, Gold.g, Gold.b, 0.1f));
            _halo.rectTransform.sizeDelta = new Vector2(600, 600);
            _lines.Add(Ring(236, 1.5f, 0.35f));
            _outer = Raw("runes_outer", _emblem, RunesOuter(), new Color(Gold.r, Gold.g, Gold.b, 0.55f)).rectTransform;
            _outer.sizeDelta = new Vector2(512, 512);
            _lines.Add(Ring(190, 1.5f, 0.3f));
            var ticks = Raw("ticks", _emblem, Ticks(), new Color(Gold.r, Gold.g, Gold.b, 0.6f));
            ticks.rectTransform.sizeDelta = new Vector2(512, 512);
            _lines.Add(ticks);
            _inner = Raw("runes_inner", _emblem, RunesInner(), new Color(Gold.r, Gold.g, Gold.b, 0.4f)).rectTransform;
            _inner.sizeDelta = new Vector2(512, 512);
            _lines.Add(Ring(114, 1f, 0.25f));

            var track = Raw("track", _emblem, Arc(), new Color(1f, 1f, 1f, 0.05f));
            track.rectTransform.sizeDelta = new Vector2(512, 512);
            _fillGlow = Filled("fill_glow", Glow(), new Color(Hot.r, Hot.g, Hot.b, 0f), 380);
            _fill = Filled("fill", Arc(), Hot, 512);

            _head = Raw("head", _emblem, Dot(), new Color(1f, 0.957f, 0.839f)).rectTransform;
            _head.sizeDelta = new Vector2(60, 60);

            _percent = new GameObject("percent", typeof(RectTransform)).AddComponent<Text>();
            _percent.rectTransform.SetParent(root, false);
            _percent.rectTransform.anchorMin = _percent.rectTransform.anchorMax = new Vector2(0.5f, 0.55f);
            _percent.alignment = TextAnchor.MiddleCenter;
            _percent.fontStyle = FontStyle.Bold;
            _percent.color = Hot;
            _percent.raycastTarget = false;
            var hint = HintField?.GetValue(preloader) as Text;
            _percent.font = hint != null && hint.font != null ? hint.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _percent.text = "";
            Fit();
            Hide();
            Canvas.willRenderCanvases += BeforeRender;
        }

        private RawImage Ring(float radius, float width, float alpha)
        {
            var img = Raw("ring", _emblem, RingTex(radius, width), new Color(Gold.r, Gold.g, Gold.b, alpha));
            img.rectTransform.sizeDelta = new Vector2(512, 512);
            return img;
        }

        private Image Filled(string name, Texture2D tex, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_emblem, false);
            var img = go.AddComponent<Image>();
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = 0f;
            img.color = color;
            img.raycastTarget = false;
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        internal void Finish()
        {
            if (_finish < 0f) _finish = Time.unscaledTime;
        }

        private void LateUpdate() => Hide();

        private void OnDestroy() => Canvas.willRenderCanvases -= BeforeRender;

        private void BeforeRender()
        {
            if (this == null || _root == null) { Canvas.willRenderCanvases -= BeforeRender; return; }
            Fit();
        }

        private void Hide()
        {
            if (_owner == null) return;
            int hidden = 0;
            foreach (var g in _owner.GetComponentsInChildren<Graphic>(true))
            {
                if (g.transform.IsChildOf(transform)) continue;
                if (g.enabled) { g.enabled = false; hidden++; }
            }
            foreach (var c in _owner.GetComponentsInChildren<Canvas>(true))
            {
                if (c.transform.IsChildOf(transform) || c.transform == _owner) continue;
                if (c.enabled) { c.enabled = false; hidden++; }
            }
            _hidden += hidden;
            if (hidden > 0 && !_told)
            {
                _told = true;
                Plugin.Trace("[loading] game loading picture hidden under our screen: parts " + hidden);
            }
        }

        private void Update()
        {
            if (_root == null) return;
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            float target = _gems != null ? _gems.fillAmount : 0f;
            _shown = target < _shown ? target : Mathf.MoveTowards(_shown, target, Mathf.Max(0.8f, (target - _shown) * 8f) * dt);
            if (_finish >= 0f) _shown = 1f;

            Fit();
            float k = Unit();
            float height = Mathf.Max(1f, _root.rect.height);

            float flash = _finish >= 0f ? Mathf.Clamp01(1f - (Time.unscaledTime - _finish) / 0.2f) : (_shown >= 0.999f ? 1f : 0f);
            _group.alpha = _finish >= 0f ? Mathf.Clamp01(1f - (Time.unscaledTime - _finish) / 0.2f) : 1f;

            float spin = Time.unscaledTime;
            _outer.localEulerAngles = new Vector3(0, 0, -spin * 0.12f * Mathf.Rad2Deg);
            _inner.localEulerAngles = new Vector3(0, 0, spin * 0.2f * Mathf.Rad2Deg);
            foreach (var line in _lines)
            {
                var c = line.color;
                line.color = new Color(c.r, c.g, c.b, Mathf.Min(1f, BaseAlpha(line) + flash * 0.5f));
            }
            _halo.color = new Color(Gold.r, Gold.g, Gold.b, 0.1f + flash * 0.35f);

            _fill.fillAmount = _shown;
            _fillGlow.fillAmount = _shown;
            _fillGlow.color = new Color(Hot.r, Hot.g, Hot.b, _shown > 0.002f ? 0.55f : 0f);
            float a = Mathf.PI / 2f - _shown * Mathf.PI * 2f;
            _head.anchoredPosition = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 158f;
            _head.gameObject.SetActive(_shown > 0.002f && _shown < 0.999f);
            _percent.text = Mathf.RoundToInt(_shown * 100f) + "%";

            float w = Mathf.Max(1f, _root.rect.width);
            foreach (var s in _sparks)
            {
                s.Y += s.V * dt * k;
                s.W += dt * 1.3f;
                s.X += Mathf.Sin(s.W) * 0.3f * k;
                if (s.Y > height + 20f) { Respawn(s, false); s.X = UnityEngine.Random.value * w; }
                float life = Mathf.Clamp01((height - s.Y) / height * 1.4f);
                s.Rt.anchoredPosition = new Vector2(s.X, s.Y);
                s.Rt.sizeDelta = new Vector2(s.R * 2f * k, s.R * 2f * k);
                s.Img.color = new Color(Hot.r, Hot.g, Hot.b, s.A * life);
            }
        }


        private Canvas _canvas;

        private float Unit()
        {
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            var top = _canvas != null ? _canvas.rootCanvas : null;
            float factor = top != null && top.scaleFactor > 0.0001f ? top.scaleFactor : 1f;
            return Screen.height / factor / Design;
        }

        private bool Fit()
        {
            if (_root == null || Screen.height < 10) return false;
            float k = Unit();
            _emblem.localScale = new Vector3(k * Small, k * Small, 1f);
            _percent.rectTransform.anchoredPosition = new Vector2(0f, -(250f * Small + 26f) * k);
            _percent.rectTransform.sizeDelta = new Vector2(240f * k, 50f * k);
            _percent.fontSize = Mathf.Max(8, Mathf.RoundToInt(26f * k));
            return true;
        }

        private readonly Dictionary<RawImage, float> _base = new Dictionary<RawImage, float>();

        private float BaseAlpha(RawImage img)
        {
            if (!_base.TryGetValue(img, out float a)) { a = img.color.a; _base[img] = a; }
            return a;
        }

        private void Respawn(Spark s, bool anywhere)
        {
            float w = _root != null ? Mathf.Max(1f, _root.rect.width) : 1920f;
            float h = _root != null ? Mathf.Max(1f, _root.rect.height) : 1080f;
            s.X = UnityEngine.Random.value * w;
            s.Y = anywhere ? UnityEngine.Random.value * h : -20f;
            s.V = 18f + UnityEngine.Random.value * 40f;
            s.R = 1.2f + UnityEngine.Random.value * 2.4f;
            s.A = 0.25f + UnityEngine.Random.value * 0.5f;
            s.W = UnityEngine.Random.value * Mathf.PI * 2f;
        }

        private static RawImage Raw(string name, Transform parent, Texture2D tex, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Texture2D New(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        private static Texture2D Background()
        {
            if (_bg != null) return _bg;
            const int n = 256;
            _bg = New(n);
            var c0 = new Color(0.114f, 0.106f, 0.133f);
            var c1 = new Color(0.067f, 0.063f, 0.082f);
            var c2 = new Color(0.024f, 0.02f, 0.031f);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.55f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 0.62f;
                    px[y * n + x] = d < 0.55f ? Color.Lerp(c0, c1, d / 0.55f) : Color.Lerp(c1, c2, Mathf.Clamp01((d - 0.55f) / 0.45f));
                }
            _bg.SetPixels(px);
            _bg.Apply(false, true);
            return _bg;
        }

        private static Texture2D Dot()
        {
            if (_dot != null) return _dot;
            const int n = 64;
            _dot = New(n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1, 1, 1, a * a * (3f - 2f * a));
                }
            _dot.SetPixels(px);
            _dot.Apply(false, true);
            return _dot;
        }

        private static Texture2D Glow()
        {
            if (_glow != null) return _glow;
            const int n = 256;
            _glow = New(n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    px[y * n + x] = new Color(1, 1, 1, a * a);
                }
            _glow.SetPixels(px);
            _glow.Apply(false, true);
            return _glow;
        }

        private const int Tex = 1024;
        private const float Px = Tex / 512f;

        private static Color[] Blank() => new Color[Tex * Tex];

        private static void Circle(Color[] px, float radius, float width, float alpha)
        {
            float r = radius * Px, half = Mathf.Max(0.75f, width * Px * 0.5f);
            float c = Tex / 2f;
            int lo = Mathf.Max(0, (int)(c - r - half - 2)), hi = Mathf.Min(Tex - 1, (int)(c + r + half + 2));
            for (int y = lo; y <= hi; y++)
                for (int x = lo; x <= hi; x++)
                {
                    float d = Mathf.Abs(Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c)) - r);
                    float a = Mathf.Clamp01(half + 0.5f - d) * alpha;
                    if (a > px[y * Tex + x].a) px[y * Tex + x] = new Color(1, 1, 1, a);
                }
        }

        private static void Segment(Color[] px, Vector2 p, Vector2 q, float width)
        {
            float half = width * Px * 0.5f;
            int x0 = Mathf.Max(0, (int)(Mathf.Min(p.x, q.x) - half - 2)), x1 = Mathf.Min(Tex - 1, (int)(Mathf.Max(p.x, q.x) + half + 2));
            int y0 = Mathf.Max(0, (int)(Mathf.Min(p.y, q.y) - half - 2)), y1 = Mathf.Min(Tex - 1, (int)(Mathf.Max(p.y, q.y) + half + 2));
            var pq = q - p;
            float len2 = Mathf.Max(0.0001f, pq.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var m = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(m - p, pq) / len2);
                    float d = Vector2.Distance(m, p + pq * t);
                    float a = Mathf.Clamp01(half + 0.5f - d);
                    if (a > px[y * Tex + x].a) px[y * Tex + x] = new Color(1, 1, 1, a);
                }
        }

        private static Texture2D Bake(Color[] px)
        {
            var tex = New(Tex);
            tex.SetPixels(px);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D RingTex(float radius, float width)
        {
            int key = Mathf.RoundToInt(radius * 10f) * 100 + Mathf.RoundToInt(width * 10f);
            if (Rings.TryGetValue(key, out var had) && had != null) return had;
            var px = Blank();
            Circle(px, radius, width * 3f, 1f);
            var tex = Bake(px);
            Rings[key] = tex;
            return tex;
        }

        private static Texture2D Arc()
        {
            if (_arc != null) return _arc;
            var px = Blank();
            Circle(px, 158f, 22f, 1f);
            _arc = Bake(px);
            return _arc;
        }

        private static Texture2D Ticks()
        {
            if (_ticks != null) return _ticks;
            var px = Blank();
            var c = new Vector2(Tex / 2f, Tex / 2f);
            for (int i = 0; i < 72; i++)
            {
                bool big = i % 6 == 0;
                float ang = i / 72f * Mathf.PI * 2f;
                var dir = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                var a = c + dir * 176f * Px;
                var b = c + dir * (176f - (big ? 16f : 7f)) * Px;
                Segment(px, a, b, big ? 7f : 3.6f);
            }
            _ticks = Bake(px);
            return _ticks;
        }

        private static Texture2D RunesOuter() => _runesOuter != null ? _runesOuter : (_runesOuter = Runes(212f, 34f, 36, 7));
        private static Texture2D RunesInner() => _runesInner != null ? _runesInner : (_runesInner = Runes(132f, 24f, 24, 13));

        private static Texture2D Runes(float radius, float size, int count, int seed)
        {
            var px = Blank();
            var rnd = new System.Random(seed);
            var c = new Vector2(Tex / 2f, Tex / 2f);
            float h = size * 0.5f * Px, wdt = size * 0.32f * Px;
            for (int i = 0; i < count; i++)
            {
                float ang = i / (float)count * Mathf.PI * 2f;
                var up = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang));
                var side = new Vector2(up.y, -up.x);
                var mid = c + up * radius * Px;
                Vector2 P(float sx, float sy) => mid + side * sx * wdt + up * sy * h;
                Segment(px, P(0, -1), P(0, 1), 3.4f);
                int strokes = 1 + rnd.Next(3);
                for (int s = 0; s < strokes; s++)
                {
                    float y0 = (float)rnd.NextDouble() * 1.6f - 0.8f;
                    float y1 = Mathf.Clamp(y0 + ((float)rnd.NextDouble() - 0.5f) * 1.4f, -1f, 1f);
                    float sx = rnd.Next(2) == 0 ? -1f : 1f;
                    Segment(px, P(0, y0), P(sx, y1), 3f);
                }
            }
            return Bake(px);
        }
    }
}
