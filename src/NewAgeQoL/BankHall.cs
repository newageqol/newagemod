using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class BankHall
    {
        private const string Prefab = "bank_Maybah";
        private static AssetBundle _fx;
        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static bool _busy, _warming, _warmed;
        private static bool _closeLater;
        private static float _holdUntil;

        internal static bool HoldCurtain()
        {
            if (!_busy || Time.unscaledTime > _holdUntil) return false;
            _closeLater = true;
            return true;
        }

        private static void Uncover()
        {
            if (!_closeLater) return;
            _closeLater = false;
            try { Preloader.Close(); }
            catch (Exception e) { Plugin.Trace("[bank] closing loading screen: " + e.Message); }
        }
        private static Texture2D _baseTex, _deltaTex, _fx2Tex, _depthTex, _idsTex;

        internal static string Folder => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "BankHall");

        internal static bool InUse() => _busy || _warming || Locations.PrerenderBusy || UnityEngine.Object.FindObjectOfType<BankLife>() != null;

        private static bool HaveFiles() => File.Exists(Path.Combine(Folder, "bank_base.png")) && File.Exists(Path.Combine(Folder, "bank_fx"));

        private static bool Ready => _fx != null && _baseTex != null;

        internal static void Tick()
        {
            try
            {
                if (_closeLater && (!_busy || Time.unscaledTime > _holdUntil)) Uncover();
                if (_warmed || _warming || _busy) return;
                if (!SideButtons.InWorld() || SideButtons.InCombat() || AssetSync.Loading) return;
                _warmed = true;
                if (Ready) return;
                AssetSync.Settle("BankHall");
                if (!HaveFiles()) return;
                Plugin.Instance.StartCoroutine(Warm());
            }
            catch (Exception e) { Plugin.Trace("[bank] preload: " + e.Message); }
        }

        private static IEnumerator Warm()
        {
            _warming = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { yield return Prepare(); }
            finally { _warming = false; }
            Plugin.Trace("[bank] files preloaded in " + clock.ElapsedMilliseconds + " ms, " + (Ready ? "ready" : "not ready"));
        }

        internal static void Unload()
        {
            _warmed = false;
            if (_fx != null) { _fx.Unload(true); _fx = null; }
            Mats.Clear();
            foreach (var t in new[] { _baseTex, _deltaTex, _fx2Tex, _depthTex, _idsTex }) Drop(t);
            _baseTex = _deltaTex = _fx2Tex = _depthTex = _idsTex = null;
        }

        internal static Material Mat(string name) => Mats.TryGetValue(name, out var m) ? m : null;

        internal static readonly Dictionary<UnityEngine.Events.UnityEvent, int> Ids = new Dictionary<UnityEngine.Events.UnityEvent, int>();

        private static GameObject FindOld()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith(Prefab, StringComparison.Ordinal) && go.transform.Find("fonSprite") != null) return go;
            return null;
        }

        internal static void Loaded()
        {
            try
            {
                if (_busy) return;
                var old = FindOld();
                if (old == null || old.GetComponentInChildren<BankLife>(true) != null) return;
                if (!_warming) AssetSync.Settle("BankHall");
                if (!HaveFiles())
                {
                    Plugin.Trace("[bank] bank files missing: " + Folder);
                    return;
                }
                Plugin.Instance.StartCoroutine(Run(old));
            }
            catch (Exception e) { Plugin.Fault("[bank] start: " + e); }
        }

        private static IEnumerator Run(GameObject old)
        {
            _busy = true;
            _holdUntil = Time.unscaledTime + 8f;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                bool waited = _warming || !Ready;
                while (_warming) yield return null;
                if (!Ready) yield return Prepare();
                if (waited) Plugin.Trace("[bank] files finished on entry in " + clock.ElapsedMilliseconds + " ms" + (_closeLater ? ", loading screen held meanwhile" : ""));
                if (old == null || !Ready) yield break;
                if (Mat("pre_composite") == null) { Plugin.Warn("[bank] composite material missing"); yield break; }
                Apply(old, _baseTex, _deltaTex, _fx2Tex, _depthTex, _idsTex);
            }
            finally
            {
                _busy = false;
                Uncover();
            }
        }

        internal static IEnumerator EnsureFx()
        {
            while (_warming || _busy) yield return null;
            if (_fx != null) yield break;
            if (!File.Exists(Path.Combine(Folder, "bank_fx"))) yield break;
            var req = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "bank_fx"));
            yield return req;
            if (_fx != null) yield break;
            _fx = req.assetBundle;
            if (_fx == null) yield break;
            Mats.Clear();
            foreach (var m in _fx.LoadAllAssets<Material>()) Mats[m.name] = m;
        }

        private static IEnumerator Prepare()
        {
            if (_fx == null)
            {
                var req = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "bank_fx"));
                yield return req;
                _fx = req.assetBundle;
                if (_fx == null) { Plugin.Warn("[bank] effects bundle failed to load"); yield break; }
                Mats.Clear();
                foreach (var m in _fx.LoadAllAssets<Material>()) Mats[m.name] = m;
                yield return null;
            }
            if (_baseTex == null) { _baseTex = Load("bank_base.png", false, false); yield return null; }
            if (_deltaTex == null) { _deltaTex = Load("bank_delta.png", false, true); yield return null; }
            if (_fx2Tex == null) { _fx2Tex = Load("bank_fx2.png", false, true); yield return null; }
            if (_depthTex == null) { _depthTex = Load("bank_depth.png", true, false); yield return null; }
            if (_idsTex == null) _idsTex = Load("bank_ids.png", false, false);
        }

        private static Texture2D Load(string file, bool linear, bool compress)
        {
            var path = Path.Combine(Folder, file);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = "bank_" + file, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (!tex.LoadImage(File.ReadAllBytes(path), false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            if (compress && tex.width % 4 == 0 && tex.height % 4 == 0)
            {
                try { tex.Compress(true); }
                catch (Exception e) { Plugin.Trace("[bank] compress " + file + ": " + e.Message); }
            }
            tex.Apply(!compress, true);
            return tex;
        }

        internal static void Drop(UnityEngine.Object o)
        {
            if (o != null) UnityEngine.Object.Destroy(o);
        }

        private static void Apply(GameObject old, Texture2D baseTex, Texture2D delta, Texture2D fx2, Texture2D depth, Texture2D ids)
        {
            var fon = old.transform.Find("fonSprite");
            var sr = fon.GetComponent<SpriteRenderer>();
            var b = sr.bounds;
            var host = new GameObject("BankHall");
            host.transform.SetParent(old.transform, false);
            host.transform.position = new Vector3(b.center.x, b.center.y, fon.position.z);
            var mat = new Material(Mat("pre_composite"));
            mat.SetTexture("_MainTex", baseTex);
            if (delta != null) mat.SetTexture("_Delta", delta);
            if (fx2 != null) mat.SetTexture("_Fx2", fx2);
            if (ids != null) mat.SetTexture("_Ids", ids);
            if (depth != null) mat.SetTexture("_DepthMask", depth);
            var mf = host.AddComponent<MeshFilter>();
            mf.sharedMesh = Quad(b.size.x, b.size.y);
            var mr = host.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingLayerID = sr.sortingLayerID;
            mr.sortingOrder = sr.sortingOrder;
            var cam = old.GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            var life = host.AddComponent<BankLife>();
            life.Setup(mat, mf.sharedMesh, new[] { baseTex, delta, fx2, depth, ids }, b, sr, ReadLayers(), cam);
            fon.gameObject.SetActive(false);
            Plugin.Trace("[bank] bank hall replaced, picture " + baseTex.width + "x" + baseTex.height + ", camera " + (cam != null ? cam.name : "none"));
        }

        internal static Mesh Quad(float w, float h)
        {
            var m = new Mesh { name = "bank_quad" };
            m.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(w / 2, h / 2, 0), new Vector3(-w / 2, h / 2, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }

        internal class Spot
        {
            public string Kind;
            public float U, V, H, D;
        }

        private static List<Spot> ReadLayers()
        {
            var list = new List<Spot>();
            try
            {
                var path = Path.Combine(Folder, "bank_layers.txt");
                if (!File.Exists(path)) return list;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split(' ');
                    if (p.Length < 3) continue;
                    float F(int i) => i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0f;
                    list.Add(new Spot { Kind = p[0], U = F(1), V = F(2), H = F(3), D = p.Length > 4 ? F(4) : -1f });
                }
            }
            catch (Exception e) { Plugin.Trace("[bank] reading layers: " + e.Message); }
            return list;
        }
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "RegisterInteractionHandlers")]
    internal static class BankHallButtonsResetPatch
    {
        private static void Prefix() => BankHall.Ids.Clear();
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "FindInteractionObject")]
    internal static class BankHallButtonsPatch
    {
        private static void Postfix(SceneObjectInfo objectInfo, UnityEngine.Events.UnityEvent __result)
        {
            if (objectInfo != null && __result != null) BankHall.Ids[__result] = objectInfo.Id;
        }
    }

    [HarmonyPatch(typeof(Preloader), "Close")]
    internal static class BankHallCurtainPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !BankHall.HoldCurtain();
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "AssetDataLoaded")]
    internal static class BankHallLoadPatch
    {
        private static void Postfix() => BankHall.Loaded();
    }

    internal class BankLife : MonoBehaviour
    {
        private Material _mat;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private readonly List<Renderer> _glows = new List<Renderer>();
        private readonly List<string> _glowGroup = new List<string>();
        private readonly List<Renderer> _glints = new List<Renderer>();
        private readonly List<float> _glintPhase = new List<float>();
        private MaterialPropertyBlock _block;
        private BankGlow _bloom;
        private bool _hdr;
        private Camera _cam;
        private float _torch, _chand, _candle;
        private float _nextBlink, _blinkStart = -10f;
        private float _nextGlow, _glowStart = -100f;
        private int _layer, _order;
        private float _z;
        private readonly List<Vector4> _heat = new List<Vector4>();
        private BottomPanelButton[] _buttons = new BottomPanelButton[0];
        private float _buttonsSeek;
        private bool _buttonsTold;
        private Vector3 _hover;

        internal void Setup(Material mat, Mesh quad, Texture2D[] textures, Bounds b, SpriteRenderer sr, List<BankHall.Spot> spots, Camera cam)
        {
            _mat = mat;
            _owned.Add(mat);
            _owned.Add(quad);
            _block = new MaterialPropertyBlock();
            _layer = sr.sortingLayerID;
            _order = sr.sortingOrder;
            _z = transform.position.z;
            var unit = BankHall.Quad(1f, 1f);
            _owned.Add(unit);
            var depthTex = textures.Length > 3 ? textures[3] : null;
            var fire = BankHall.Mat("pre_fire");
            var glint = BankHall.Mat("pre_glint");
            var glow = BankHall.Mat("pre_glow");
            float aspect = b.size.x / Mathf.Max(b.size.y, 0.001f);
            int eyes = 0, k = 0;
            var chandTops = new List<Vector3>();
            var head = spots.FirstOrDefault(x => x.Kind == "torchhead");
            foreach (var s in spots)
            {
                if (s.Kind == "torchhead") continue;
                var at = new Vector3(b.min.x + s.U * b.size.x, b.max.y - s.V * b.size.y, _z);
                if (s.Kind == "eye")
                {
                    if (eyes < 2) mat.SetVector(eyes == 0 ? "_Eye0" : "_Eye1", new Vector4(s.U, 1f - s.V, s.H, aspect));
                    eyes++;
                    continue;
                }
                if (s.Kind == "glint")
                {
                    if (glint == null) continue;
                    _glints.Add(Piece("glint" + _glints.Count, unit, glint, at + new Vector3(0, 0, -0.02f), Vector3.one * 0.16f, _order + 3));
                    _glintPhase.Add(UnityEngine.Random.Range(0f, 40f));
                    continue;
                }
                if (fire == null) continue;
                float h = Mathf.Max(s.H * b.size.y, 0.02f);
                bool torch = s.Kind == "torch";
                float fh = h * (torch ? 3.2f : 2.6f);
                float fw = fh * (torch ? 0.55f : 0.42f);
                Vector3? wrapAt = null;
                float wrap = 0f;
                if (torch && head != null)
                {
                    float hw = (head.H - head.U) * b.size.x;
                    float hh = (head.D - head.V) * b.size.y;
                    fh = hh * 2.9f;
                    fw = Mathf.Max(hw * 1.6f, hh * 0.8f);
                    wrap = hh * 1.05f / fh;
                    wrapAt = new Vector3(b.min.x + (head.U + head.H) * 0.5f * b.size.x, b.max.y - head.D * b.size.y - hh * 0.04f + fh * 0.5f, _z - 0.03f);
                }
                var flameMat = new Material(fire);
                flameMat.SetFloat("_Wrap", wrap);
                flameMat.SetFloat("_Seed", k * 0.37f + 0.11f);
                flameMat.SetFloat("_Strength", torch ? 3.4f : 2.6f);
                if (depthTex != null) flameMat.SetTexture("_DepthMask", depthTex);
                flameMat.SetVector("_Rect", new Vector4(b.min.x, b.min.y, b.size.x, b.size.y));
                flameMat.SetFloat("_FlameDepth", depthTex != null && s.D >= 0f ? s.D : -1f);
                _owned.Add(flameMat);
                var top = wrapAt ?? at + new Vector3(0, fh * 0.38f - h * 0.35f, -0.03f);
                Piece("flame" + k, unit, flameMat, top, new Vector3(fw, fh, 1), _order + 2);
                if (glow != null)
                {
                    float gs = fh * (torch ? 1.5f : 1.3f);
                    _glows.Add(Piece("flame_glow" + k, unit, glow, top + new Vector3(0, -fh * 0.12f, 0.005f), new Vector3(gs, gs, 1), _order + 1));
                    _glowGroup.Add(s.Kind);
                }
                if (torch) Sparks(top + new Vector3(0, fh * 0.35f, -0.01f), fw);
                if (s.Kind == "chand") chandTops.Add(top + new Vector3(0, fh * 0.45f, 0));
                if (_heat.Count < 16)
                {
                    float hv = fh / b.size.y;
                    float wu = fw * 0.8f / b.size.x;
                    float baseV = (top.y + fh * 0.3f - b.min.y) / b.size.y;
                    _heat.Add(new Vector4(s.U, baseV, hv, wu / Mathf.Max(hv, 0.0001f)));
                }
                k++;
            }
            foreach (var p in chandTops) Smoke(p, b.size.y);
            var dust = BankHall.Mat("pre_dust");
            if (dust != null) Dust(b, dust);
            mat.SetFloat("_Clouds", 1.4f);
            mat.SetFloat("_Glass", 0.32f);
            if (_heat.Count > 0)
            {
                var arr = new Vector4[16];
                for (int i = 0; i < _heat.Count; i++) arr[i] = _heat[i];
                mat.SetVectorArray("_Heat", arr);
                mat.SetFloat("_HeatCount", _heat.Count);
            }
            _cam = cam;
            var bloomMat = BankHall.Mat("pre_bloom");
            if (cam != null && bloomMat != null && SystemInfo.graphicsMemorySize >= 2000)
            {
                _hdr = cam.allowHDR;
                cam.allowHDR = true;
                _bloom = cam.gameObject.AddComponent<BankGlow>();
                _bloom.mat = bloomMat;
                _bloom.owner = this;
            }
            _nextBlink = Time.time + UnityEngine.Random.Range(4f, 9f);
            _nextGlow = Time.time + UnityEngine.Random.Range(25f, 45f);
        }

        private const int TreasuryLink = 280;
        private const int StorageLink = 278;
        private const int BankerNpc = 18;

        private static Vector3 Zone(BottomPanelButton btn)
        {
            if (btn.button == null || !BankHall.Ids.TryGetValue(btn.button.onClick, out int id)) return Vector3.zero;
            if (id == TreasuryLink) return new Vector3(1, 0, 0);
            if (id == StorageLink) return new Vector3(0, 1, 0);
            if (id == BankerNpc) return new Vector3(0, 0, 1);
            return Vector3.zero;
        }

        private Vector3 Hovered()
        {
            try
            {
                if (Time.time >= _buttonsSeek)
                {
                    _buttonsSeek = Time.time + 1f;
                    _buttons = UnityEngine.Object.FindObjectsOfType<BottomPanelButton>();
                    if (!_buttonsTold && _buttons.Length > 0)
                    {
                        _buttonsTold = true;
                        Plugin.Trace("[bank] buttons: " + string.Join(", ", _buttons.Select(x => x.ObjectType + " id " + (x.button != null && BankHall.Ids.TryGetValue(x.button.onClick, out int bid) ? bid.ToString() : "?"))));
                    }
                }
                Vector2 mouse = Input.mousePosition;
                foreach (var btn in _buttons)
                {
                    if (btn == null || !btn.isActiveAndEnabled) continue;
                    var rt = btn.transform as RectTransform;
                    if (rt == null) continue;
                    var canvas = btn.GetComponentInParent<Canvas>();
                    Camera eye = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    if (!RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, eye)) continue;
                    var zone = Zone(btn);
                    if (zone != Vector3.zero) return zone;
                }
            }
            catch { }
            return Vector3.zero;
        }

        private Renderer Piece(string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingLayerID = _layer;
            r.sortingOrder = order;
            return r;
        }

        private ParticleSystem Emitter(string name, Vector3 pos, Material mat, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.sortingLayerID = _layer;
            pr.sortingOrder = order;
            return ps;
        }

        private static void Fade(ParticleSystem ps, float peak)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new UnityEngine.Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(peak, 0.2f), new GradientAlphaKey(peak * 0.6f, 0.65f), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        private void Dust(Bounds b, Material mat)
        {
            var ps = Emitter("dust",new Vector3(b.center.x, b.center.y, _z - 0.05f), mat, _order + 4);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.65f, 1f), new Color(1f, 0.85f, 0.65f));
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.001f;
            var em = ps.emission;
            em.rateOverTime = 11f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(b.size.x * 0.75f, b.size.y * 0.8f, 0.01f);
            var nm = ps.noise;
            nm.enabled = true;
            nm.strength = 0.12f;
            nm.frequency = 0.2f;
            Fade(ps, 0.5f);
            ps.Play();
        }

        private void Sparks(Vector3 pos, float width)
        {
            var mat = BankHall.Mat("pre_spark");
            if (mat == null) return;
            var ps = Emitter("sparks",pos, mat, _order + 3);
            var main = ps.main;
            main.loop = true;
            main.duration = 4f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.028f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.12f));
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.04f;
            var em = ps.emission;
            em.rateOverTime = 1.2f;
            em.SetBursts(new[] { new ParticleSystem.Burst(1.3f, 2, 4, 3, 2.7f) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 18f;
            sh.radius = width * 0.2f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var nm = ps.noise;
            nm.enabled = true;
            nm.strength = 0.25f;
            nm.frequency = 1.4f;
            Fade(ps, 1f);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 0.2f)));
            ps.Play();
        }

        private void Smoke(Vector3 pos, float height)
        {
            var mat = BankHall.Mat("pre_smoke");
            if (mat == null) return;
            var ps = Emitter("smoke",pos, mat, _order + 1);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(height * 0.008f, height * 0.016f);
            main.startSize = new ParticleSystem.MinMaxCurve(height * 0.01f, height * 0.018f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.55f, 0.52f), new Color(0.45f, 0.42f, 0.42f));
            main.maxParticles = 14;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 2.4f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.005f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var nm = ps.noise;
            nm.enabled = true;
            nm.strength = height * 0.004f;
            nm.frequency = 0.6f;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(1, 2.6f)));
            Fade(ps, 0.55f);
            ps.Play();
        }

        private static float Flick(float t, float seed, float amount)
        {
            float n = Mathf.PerlinNoise(t * 3.1f, seed) - 0.5f;
            return amount * (0.55f * Mathf.Sin(t * 8.7f + seed) + 0.3f * Mathf.Sin(t * 13.3f + seed * 2.1f) + 1.4f * n);
        }

        private void Update()
        {
            float t = Time.time;
            _torch = Flick(t * 0.45f, 1.3f, 0.07f);
            _chand = Flick(t * 0.5f, 4.7f, 0.05f);
            _candle = Flick(t * 0.6f, 9.1f, 0.08f);
            float rooms = 0.05f * Mathf.Sin(t * 0.5f) + 0.035f * (Mathf.PerlinNoise(t * 0.25f, 17.3f) - 0.5f) * 2f;
            if (t >= _nextBlink)
            {
                _blinkStart = t;
                _nextBlink = t + UnityEngine.Random.Range(6f, 14f);
            }
            float bt = (t - _blinkStart) / 0.22f;
            float blink = bt >= 0f && bt <= 1f ? Mathf.Sin(bt * Mathf.PI) : 0f;
            if (t >= _nextGlow)
            {
                _glowStart = t;
                _nextGlow = t + UnityEngine.Random.Range(60f, 95f);
            }
            float gt = (t - _glowStart) / 4.5f;
            float eyeGlow = gt >= 0f && gt <= 1f ? Mathf.Sin(gt * Mathf.PI) * 0.55f : 0f;
            if (_mat != null)
            {
                _mat.SetVector("_Flick", new Vector4(_torch, _chand, _candle, rooms));
                _mat.SetFloat("_Shadow", _chand * 0.8f + 0.04f * Mathf.Sin(t * 0.55f) + 0.02f * Mathf.Sin(t * 1.3f + 0.6f));
                _mat.SetFloat("_Blink", blink);
                _mat.SetFloat("_EyeGlow", eyeGlow);
                var want = Hovered();
                _hover = Vector3.MoveTowards(_hover, want, Time.deltaTime * 3.5f);
                _mat.SetVector("_Hover", new Vector4(_hover.x, _hover.y, _hover.z, 0));
            }
            for (int i = 0; i < _glows.Count && i < _glowGroup.Count; i++)
            {
                float g = _glowGroup[i] == "torch" ? _torch : _glowGroup[i] == "chand" ? _chand : _candle;
                _block.SetFloat("_Strength", 0.22f + g * 0.5f);
                _glows[i].SetPropertyBlock(_block);
            }
            for (int i = 0; i < _glints.Count; i++)
            {
                float ph = (t + _glintPhase[i]) % 7f;
                float a = ph < 0.6f ? Mathf.Sin(ph / 0.6f * Mathf.PI) : 0f;
                _block.SetFloat("_Strength", a * 2.4f);
                _glints[i].SetPropertyBlock(_block);
                _glints[i].transform.localRotation = Quaternion.Euler(0, 0, ph * 40f);
            }
        }

        private void OnDestroy()
        {
            if (_bloom != null) Destroy(_bloom);
            if (_cam != null && _bloom != null) _cam.allowHDR = _hdr;
            foreach (var o in _owned) BankHall.Drop(o);
            _owned.Clear();
        }
    }

    internal class BankGlow : MonoBehaviour
    {
        public Material mat;
        public BankLife owner;
        public int iterations = 6;
        public float threshold = 0.95f;
        public float intensity = 0.14f;
        private readonly RenderTexture[] _chain = new RenderTexture[8];

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null) { Graphics.Blit(src, dst); return; }
            mat.SetFloat("_Threshold", threshold);
            mat.SetFloat("_Knee", threshold * 0.6f);
            mat.SetFloat("_Intensity", intensity);
            int w = src.width / 2, h = src.height / 2;
            var fmt = src.format;
            var cur = _chain[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, cur, mat, 0);
            int n = 1;
            for (; n < iterations && n < _chain.Length; n++)
            {
                w /= 2; h /= 2;
                if (h < 2) break;
                _chain[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(cur, _chain[n], mat, 1);
                cur = _chain[n];
            }
            for (int i = n - 2; i >= 0; i--)
            {
                Graphics.Blit(cur, _chain[i], mat, 2);
                RenderTexture.ReleaseTemporary(cur);
                cur = _chain[i];
            }
            mat.SetTexture("_BloomTex", cur);
            Graphics.Blit(src, dst, mat, 3);
            RenderTexture.ReleaseTemporary(cur);
        }

        private void Update()
        {
            if (owner == null) Destroy(this);
        }
    }
}
