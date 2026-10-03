using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace NewAgeQoL
{
    internal class PlaceSprites : MonoBehaviour
    {
        internal static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "sheetfire", "swing", "smoke", "loop" };

        private class Anim
        {
            public SpriteRenderer R, Next;
            public Sprite[] Frames;
            public float Fps, Seed, Rate, Period;
            public bool Swing;
        }

        private readonly List<Anim> _anims = new List<Anim>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly Dictionary<string, Texture2D> _sheets = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private string _folder;
        private Bounds _b;
        private int _layer, _order;
        private float _z;

        internal static void Attach(GameObject host, string folder, Bounds b, int layer, int order)
        {
            try
            {
                var path = Path.Combine(folder, "bank_layers.txt");
                if (!File.Exists(path)) return;
                var lines = new List<string[]>();
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Trim().Split(' ');
                    if (p.Length > 1 && Kinds.Contains(p[0])) lines.Add(p);
                }
                if (lines.Count == 0) return;
                var c = host.AddComponent<PlaceSprites>();
                c._folder = folder;
                c._b = b;
                c._layer = layer;
                c._order = order;
                c._z = host.transform.position.z;
                foreach (var p in lines)
                {
                    try { c.Add(p); }
                    catch (Exception e) { Plugin.Trace("[places] sprite line " + string.Join(" ", p) + ": " + e.Message); }
                }
                Plugin.Trace("[places] animated pieces: " + c._anims.Count + " from " + lines.Count + " lines");
            }
            catch (Exception e) { Plugin.Trace("[places] sprites: " + e.Message); }
        }

        private static float F(string[] p, int i) => i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0f;

        private static int I(string[] p, int i) => i < p.Length ? int.Parse(p[i], CultureInfo.InvariantCulture) : 0;

        private Vector3 At(float u, float v) => new Vector3(_b.min.x + u * _b.size.x, _b.max.y - v * _b.size.y, _z);

        private Texture2D Sheet(string file)
        {
            if (_sheets.TryGetValue(file, out var have)) return have;
            var path = Path.Combine(_folder, file);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "place_" + file, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (!tex.LoadImage(File.ReadAllBytes(path), false))
            {
                Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 2;
            tex.Apply(true, true);
            _owned.Add(tex);
            _sheets[file] = tex;
            return tex;
        }

        private Sprite[] Cut(Texture2D tex, int cols, int frames, Vector2 pivot, float ppu)
        {
            int rows = (frames + cols - 1) / cols;
            int fw = tex.width / cols, fh = tex.height / rows;
            var list = new Sprite[frames];
            for (int i = 0; i < frames; i++)
            {
                int c = i % cols, r = i / cols;
                var s = Sprite.Create(tex, new Rect(c * fw, tex.height - (r + 1) * fh, fw, fh), pivot, ppu, 0, SpriteMeshType.FullRect);
                _owned.Add(s);
                list[i] = s;
            }
            return list;
        }

        private SpriteRenderer Holder(string name, Vector3 pos, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            var r = go.AddComponent<SpriteRenderer>();
            r.sortingLayerID = _layer;
            r.sortingOrder = order;
            return r;
        }

        private void Add(string[] p)
        {
            switch (p[0])
            {
                case "sheetfire":
                {
                    var tex = Sheet(p[1]);
                    if (tex == null) return;
                    int cols = I(p, 5), frames = I(p, 6);
                    float w = F(p, 4) * _b.size.x;
                    float ppu = tex.width / (float)cols / Mathf.Max(w, 0.0001f);
                    var frs = Cut(tex, cols, frames, new Vector2(0.5f, 1f - F(p, 8)), ppu);
                    var r = Holder("fire" + _anims.Count, At(F(p, 2), F(p, 3)) + new Vector3(0, 0, -0.03f), _order + 4);
                    float seed = F(p, 9);
                    _anims.Add(new Anim { R = r, Frames = frs, Fps = F(p, 7), Seed = seed, Rate = 0.92f + 0.16f * Mathf.Repeat(seed * 0.618f, 1f) });
                    break;
                }
                case "swing":
                {
                    var tex = Sheet(p[1]);
                    if (tex == null) return;
                    float u0 = F(p, 2), v0 = F(p, 3), u1 = F(p, 4);
                    int cols = I(p, 6), frames = I(p, 7);
                    float ppu = tex.width / (float)cols / Mathf.Max((u1 - u0) * _b.size.x, 0.0001f);
                    var frs = Cut(tex, cols, frames, new Vector2(0f, 1f), ppu);
                    var r = Holder("swing" + _anims.Count, At(u0, v0) + new Vector3(0, 0, -0.01f), _order + 1);
                    var next = Holder("swing_next" + _anims.Count, At(u0, v0) + new Vector3(0, 0, -0.011f), _order + 2);
                    _anims.Add(new Anim { R = r, Next = next, Frames = frs, Period = Mathf.Max(F(p, 8), 0.5f), Swing = true });
                    break;
                }
                case "loop":
                {
                    var tex = Sheet(p[1]);
                    if (tex == null) return;
                    float u0 = F(p, 2), v0 = F(p, 3), u1 = F(p, 4);
                    int cols = I(p, 6), frames = I(p, 7);
                    float ppu = tex.width / (float)cols / Mathf.Max((u1 - u0) * _b.size.x, 0.0001f);
                    var frs = Cut(tex, cols, frames, new Vector2(0f, 1f), ppu);
                    var r = Holder("loop" + _anims.Count, At(u0, v0) + new Vector3(0, 0, -0.03f), _order + 4);
                    _anims.Add(new Anim { R = r, Frames = frs, Fps = F(p, 8), Rate = 1f });
                    break;
                }
                case "smoke":
                    Smoke(At(F(p, 1), F(p, 2)), F(p, 3) * _b.size.y);
                    break;
            }
        }

        private void Smoke(Vector3 pos, float size)
        {
            var mat = BankHall.Mat("pre_smoke");
            if (mat == null) return;
            var go = new GameObject("smoke");
            go.transform.SetParent(transform, false);
            go.transform.position = pos + new Vector3(0, 0, -0.02f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.sortingLayerID = _layer;
            pr.sortingOrder = _order + 3;
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.35f, size * 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.37f, 0.33f), new Color(0.3f, 0.27f, 0.25f));
            main.maxParticles = 30;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 8f;
            sh.radius = size * 0.15f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var nm = ps.noise;
            nm.enabled = true;
            nm.strength = size * 0.25f;
            nm.frequency = 0.5f;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2.8f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new UnityEngine.Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.35f, 0.2f), new GradientAlphaKey(0.2f, 0.65f), new GradientAlphaKey(0, 1) });
            col.color = g;
            ps.Play();
        }

        private void Update()
        {
            float t = Time.time;
            foreach (var a in _anims)
            {
                if (a.R == null || a.Frames.Length == 0) continue;
                int i;
                if (a.Swing)
                {
                    float s = Mathf.Sin(t * Mathf.PI * 2f / a.Period);
                    float x = Mathf.Clamp((s + 1f) * 0.5f * (a.Frames.Length - 1), 0f, a.Frames.Length - 1);
                    i = Mathf.Min((int)x, a.Frames.Length - 1);
                    int j = Mathf.Min(i + 1, a.Frames.Length - 1);
                    if (a.Next != null)
                    {
                        if (a.Next.sprite != a.Frames[j]) a.Next.sprite = a.Frames[j];
                        a.Next.color = new Color(1f, 1f, 1f, x - i);
                    }
                }
                else
                {
                    i = (int)(t * a.Fps * a.Rate + a.Seed * 7.3f) % a.Frames.Length;
                }
                if (a.R.sprite != a.Frames[i]) a.R.sprite = a.Frames[i];
            }
        }

        private void OnDestroy()
        {
            foreach (var o in _owned) BankHall.Drop(o);
            _owned.Clear();
            _sheets.Clear();
        }
    }
}
