using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class Locations
    {
        private const string DescFile = "location.txt";

        internal class Desc
        {
            public string Folder, Name, Prefab, Kind = "prerender", Scene, Bundle, Fx, Data;
            public bool Busy, Warming;
            public AssetBundle SceneBundle, FxBundle;
            public Material Bloom;
            public Texture2D Base, Delta, Fx2, Depth;
            public Saved Saved;
        }

        internal class Saved
        {
            public AmbientMode Mode;
            public Color Light, Sky, Equator, Ground;
            public bool Fog;
            public Camera Cam;
            public bool Ortho;
            public float Size, Fov, Near, Far;
            public Vector3 Pos;
            public Quaternion Rot;
            public CameraClearFlags Clear;
            public Color Back;
            public int Mask;
            public bool Hdr;
            public string Scene;
        }

        private static bool _warmed, _warming;

        internal static void Warm()
        {
            try
            {
                if (_warmed || _warming || Plugin.Instance == null) return;
                if (!SideButtons.InWorld() || SideButtons.InCombat() || AssetSync.Loading || AssetSync.Asking) return;
                _warmed = true;
                Plugin.Instance.StartCoroutine(Preload());
            }
            catch (Exception e) { Plugin.Trace("[places] preload: " + e.Message); }
        }

        private static IEnumerator Preload()
        {
            _warming = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int n = 0;
            try
            {
                foreach (var d in All().ToList())
                {
                    if (d.Kind != "prerender" || d.Busy || AssetSync.Off(d.Name)) continue;
                    if (!File.Exists(PathOf(d, "bank_base.png"))) continue;
                    yield return BankHall.EnsureFx();
                    d.Warming = true;
                    try { yield return LoadTextures(d); }
                    finally { d.Warming = false; }
                    n++;
                    yield return null;
                }
            }
            finally { _warming = false; }
            Plugin.Trace("[places] preloaded " + n + " pictures in " + clock.ElapsedMilliseconds + " ms");
        }

        private static IEnumerator LoadTextures(Desc d)
        {
            if (d.Base == null) { d.Base = Load(d, "bank_base.png", false, false); yield return null; }
            if (d.Delta == null) { d.Delta = Load(d, "bank_delta.png", false, true); yield return null; }
            if (d.Fx2 == null) { d.Fx2 = Load(d, "bank_fx2.png", false, true); yield return null; }
            if (d.Depth == null) { d.Depth = Load(d, "bank_depth.png", true, false); yield return null; }
        }

        private static void HideOld(GameObject old)
        {
            var fon = old.transform.Find("fonSprite");
            if (fon != null) fon.gameObject.SetActive(false);
        }

        private static void ShowOld(GameObject old)
        {
            if (old == null || old.GetComponentInChildren<PlaceMark>(true) != null) return;
            var fon = old.transform.Find("fonSprite");
            if (fon != null) fon.gameObject.SetActive(true);
        }

        private static readonly Dictionary<string, Desc> Known = new Dictionary<string, Desc>(StringComparer.OrdinalIgnoreCase);

        private static string Root => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";

        internal static IEnumerable<Desc> All()
        {
            Scan();
            return Known.Values;
        }

        private static void Scan()
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(Root))
                {
                    var name = Path.GetFileName(dir);
                    var file = Path.Combine(dir, DescFile);
                    if (!File.Exists(file))
                    {
                        if (Known.ContainsKey(name) && !Known[name].Busy && Known[name].Saved == null) Known.Remove(name);
                        continue;
                    }
                    if (!Known.TryGetValue(name, out var d))
                    {
                        d = new Desc { Folder = dir, Name = name };
                        Known[name] = d;
                    }
                    foreach (var raw in File.ReadAllLines(file))
                    {
                        var line = raw.Trim();
                        int sp = line.IndexOf(' ');
                        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || sp < 0) continue;
                        var key = line.Substring(0, sp);
                        var val = line.Substring(sp + 1).Trim();
                        switch (key)
                        {
                            case "prefab": d.Prefab = val; break;
                            case "kind": d.Kind = val; break;
                            case "scene": d.Scene = val; break;
                            case "bundle": d.Bundle = val; break;
                            case "fx": d.Fx = val; break;
                            case "data": d.Data = val; break;
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[places] scanning: " + e.Message); }
        }

        internal static Desc Find(string folder) => Known.TryGetValue(folder, out var d) ? d : null;

        internal static bool InUse(string folder)
        {
            var d = Find(folder);
            if (d == null) return false;
            if (d.Busy || d.Warming || d.Saved != null) return true;
            if (d.Kind == "scene" && !string.IsNullOrEmpty(d.Scene) && SceneManager.GetSceneByName(d.Scene).isLoaded) return true;
            return d.Kind == "prerender" && UnityEngine.Object.FindObjectsOfType<PlaceMark>().Any(m => m.Folder == folder);
        }

        internal static void Release(string folder)
        {
            var d = Find(folder);
            if (d == null) return;
            if (d.SceneBundle != null) { d.SceneBundle.Unload(true); d.SceneBundle = null; }
            if (d.FxBundle != null) { d.FxBundle.Unload(true); d.FxBundle = null; }
            d.Bloom = null;
            foreach (var t in new[] { d.Base, d.Delta, d.Fx2, d.Depth }) BankHall.Drop(t);
            d.Base = d.Delta = d.Fx2 = d.Depth = null;
        }

        internal static bool PrerenderBusy => Known.Values.Any(d => (d.Busy || d.Warming) && d.Kind == "prerender");

        internal static bool IsOurScene(string scene) =>
            Known.Values.Any(d => d.Kind == "scene" && string.Equals(d.Scene, scene, StringComparison.Ordinal));

        private static GameObject FindOld(string prefab)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith(prefab, StringComparison.Ordinal) && go.transform.Find("fonSprite") != null) return go;
            return null;
        }

        internal static void Loaded()
        {
            foreach (var d in All().ToList())
            {
                try
                {
                    if (d.Busy || string.IsNullOrEmpty(d.Prefab) || AssetSync.Off(d.Name)) continue;
                    var old = FindOld(d.Prefab);
                    if (old == null || old.GetComponentInChildren<PlaceMark>(true) != null) continue;
                    AssetSync.Settle(d.Name);
                    if (d.Kind == "scene" ? !string.IsNullOrEmpty(d.Bundle) && File.Exists(PathOf(d, d.Bundle)) : File.Exists(PathOf(d, "bank_base.png"))) HideOld(old);
                    if (d.Kind == "scene") Plugin.Instance.StartCoroutine(RunScene(d, old));
                    else Plugin.Instance.StartCoroutine(RunPrerender(d, old));
                }
                catch (Exception e) { Plugin.Fault("[places] " + d.Name + ": " + e); }
            }
        }

        private static string PathOf(Desc d, string file) => Path.Combine(d.Folder, file);

        private static IEnumerator RunPrerender(Desc d, GameObject old)
        {
            if (!File.Exists(PathOf(d, "bank_base.png"))) { Plugin.Trace("[places] " + d.Name + ": picture missing"); yield break; }
            while (d.Warming) yield return null;
            d.Busy = true;
            bool shown = false;
            try
            {
                yield return BankHall.EnsureFx();
                if (BankHall.Mat("pre_composite") == null) { Plugin.Warn("[places] " + d.Name + ": effects bundle missing"); yield break; }
                yield return LoadTextures(d);
                if (old == null || d.Base == null || BankHall.Mat("pre_composite") == null) yield break;
                ApplyPrerender(d, old);
                shown = true;
            }
            finally
            {
                d.Busy = false;
                if (!shown) ShowOld(old);
            }
        }

        private static Texture2D Load(Desc d, string file, bool linear, bool compress)
        {
            var path = PathOf(d, file);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = d.Name + "_" + file, hideFlags = HideFlags.DontUnloadUnusedAsset };
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
                catch (Exception e) { Plugin.Trace("[places] compress " + file + ": " + e.Message); }
            }
            tex.Apply(!compress, true);
            return tex;
        }

        private static void ApplyPrerender(Desc d, GameObject old)
        {
            var fon = old.transform.Find("fonSprite");
            var sr = fon.GetComponent<SpriteRenderer>();
            var b = sr.bounds;
            var host = new GameObject("Place_" + d.Name);
            host.transform.SetParent(old.transform, false);
            host.transform.position = new Vector3(b.center.x, b.center.y, fon.position.z);
            host.AddComponent<PlaceMark>().Folder = d.Name;
            var mat = new Material(BankHall.Mat("pre_composite"));
            mat.SetTexture("_MainTex", d.Base);
            if (d.Delta != null) mat.SetTexture("_Delta", d.Delta);
            if (d.Fx2 != null) mat.SetTexture("_Fx2", d.Fx2);
            mat.SetTexture("_DepthMask", d.Depth != null ? d.Depth : Texture2D.blackTexture);
            var mf = host.AddComponent<MeshFilter>();
            mf.sharedMesh = BankHall.Quad(b.size.x, b.size.y);
            var mr = host.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingLayerID = sr.sortingLayerID;
            mr.sortingOrder = sr.sortingOrder;
            var cam = old.GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            var life = host.AddComponent<BankLife>();
            life.Setup(mat, mf.sharedMesh, new[] { d.Base, d.Delta, d.Fx2, d.Depth, null }, b, sr, ReadSpots(d), cam);
            fon.gameObject.SetActive(false);
            Plugin.Trace("[places] " + d.Name + " shown as a picture " + d.Base.width + "x" + d.Base.height);
        }

        private static List<BankHall.Spot> ReadSpots(Desc d)
        {
            var list = new List<BankHall.Spot>();
            try
            {
                var path = PathOf(d, "bank_layers.txt");
                if (!File.Exists(path)) return list;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split(' ');
                    if (p.Length < 3) continue;
                    float F(int i) => i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0f;
                    list.Add(new BankHall.Spot { Kind = p[0], U = F(1), V = F(2), H = F(3), D = p.Length > 4 ? F(4) : -1f });
                }
            }
            catch (Exception e) { Plugin.Trace("[places] reading layers: " + e.Message); }
            return list;
        }

        private static IEnumerator RunScene(Desc d, GameObject old)
        {
            if (string.IsNullOrEmpty(d.Bundle) || string.IsNullOrEmpty(d.Scene) || !File.Exists(PathOf(d, d.Bundle)))
            {
                Plugin.Trace("[places] " + d.Name + ": scene files missing");
                yield break;
            }
            d.Busy = true;
            bool shown = false;
            try
            {
                if (d.SceneBundle == null)
                {
                    var req = AssetBundle.LoadFromFileAsync(PathOf(d, d.Bundle));
                    yield return req;
                    d.SceneBundle = req.assetBundle;
                }
                if (d.FxBundle == null && !string.IsNullOrEmpty(d.Fx) && File.Exists(PathOf(d, d.Fx)))
                {
                    var req = AssetBundle.LoadFromFileAsync(PathOf(d, d.Fx));
                    yield return req;
                    d.FxBundle = req.assetBundle;
                    if (d.FxBundle != null) d.Bloom = d.FxBundle.LoadAllAssets<Material>().FirstOrDefault(m => m.shader != null && m.shader.name.EndsWith("Bloom", StringComparison.Ordinal));
                }
                if (d.SceneBundle == null) { Plugin.Warn("[places] " + d.Name + ": scene bundle failed to load"); yield break; }
                if (old == null) yield break;
                if (!SceneManager.GetSceneByName(d.Scene).isLoaded)
                {
                    var op = SceneManager.LoadSceneAsync(d.Scene, LoadSceneMode.Additive);
                    if (op == null) { Plugin.Warn("[places] " + d.Name + ": scene failed to load"); yield break; }
                    yield return op;
                }
                var scene = SceneManager.GetSceneByName(d.Scene);
                if (!scene.IsValid() || old == null) yield break;
                ApplyScene(d, old, scene);
                shown = true;
            }
            finally
            {
                d.Busy = false;
                if (!shown) ShowOld(old);
            }
        }

        private class Data
        {
            public Vector3 CamPos = new Vector3(0, 1.75f, -4.4f);
            public Vector3 CamRot = Vector3.zero;
            public float Fov = 53.7f;
            public Color Sky = new Color(0.16f, 0.12f, 0.26f), Equator = new Color(0.1f, 0.08f, 0.15f), Ground = new Color(0.05f, 0.04f, 0.06f);
            public Color Back = new Color(0.02f, 0.015f, 0.03f);
        }

        private static Data ReadData(Desc d)
        {
            var data = new Data();
            try
            {
                var path = string.IsNullOrEmpty(d.Data) ? null : PathOf(d, d.Data);
                if (path == null || !File.Exists(path)) return data;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split(' ');
                    float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                    switch (p[0])
                    {
                        case "camera": data.CamPos = new Vector3(F(1), F(2), F(3)); data.CamRot = new Vector3(F(4), F(5), F(6)); data.Fov = F(7); break;
                        case "ambient": data.Sky = new Color(F(1), F(2), F(3)); data.Equator = new Color(F(4), F(5), F(6)); data.Ground = new Color(F(7), F(8), F(9)); break;
                        case "background": data.Back = new Color(F(1), F(2), F(3)); break;
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[places] reading " + d.Name + " data: " + e.Message); }
            return data;
        }

        private static void ApplyScene(Desc d, GameObject old, Scene scene)
        {
            var data = ReadData(d);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Camera>(true))
                    UnityEngine.Object.Destroy(c.gameObject);
            LightProbes.TetrahedralizeAsync();
            var fon = old.transform.Find("fonSprite");
            if (fon != null) fon.gameObject.SetActive(false);
            foreach (Transform ch in old.transform) if (ch.name.StartsWith("Particle", StringComparison.Ordinal)) ch.gameObject.SetActive(false);
            old.AddComponent<PlaceMark>().Folder = d.Name;

            var cam = old.GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            var s = new Saved
            {
                Mode = RenderSettings.ambientMode, Light = RenderSettings.ambientLight, Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor, Ground = RenderSettings.ambientGroundColor, Fog = RenderSettings.fog, Cam = cam, Scene = d.Scene,
            };
            if (cam != null)
            {
                s.Ortho = cam.orthographic; s.Size = cam.orthographicSize; s.Fov = cam.fieldOfView;
                s.Near = cam.nearClipPlane; s.Far = cam.farClipPlane; s.Pos = cam.transform.position; s.Rot = cam.transform.rotation;
                s.Clear = cam.clearFlags; s.Back = cam.backgroundColor; s.Mask = cam.cullingMask; s.Hdr = cam.allowHDR;
                cam.orthographic = false;
                cam.fieldOfView = data.Fov;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = 60f;
                cam.transform.position = data.CamPos;
                cam.transform.rotation = Quaternion.Euler(data.CamRot);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = data.Back;
                cam.cullingMask |= 1;
                if (SystemInfo.graphicsMemorySize >= 3500 && d.Bloom != null)
                {
                    cam.allowHDR = true;
                    var glow = cam.GetComponent<TowerGlow>();
                    if (glow == null) glow = cam.gameObject.AddComponent<TowerGlow>();
                    glow.mat = d.Bloom;
                    glow.scene = d.Scene;
                }
            }
            d.Saved = s;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = data.Sky;
            RenderSettings.ambientEquatorColor = data.Equator;
            RenderSettings.ambientGroundColor = data.Ground;
            RenderSettings.fog = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponent<TowerMotion>() == null && root.GetComponentInChildren<Light>(true) != null) root.AddComponent<TowerMotion>();
            Plugin.Trace("[places] " + d.Name + " shown as a scene, camera " + (cam != null ? cam.name : "none") + ", bloom " + (d.Bloom != null));
        }

        internal static void Left()
        {
            foreach (var d in Known.Values)
            {
                if (d.Saved == null) continue;
                try
                {
                    var s = d.Saved;
                    d.Saved = null;
                    RenderSettings.ambientMode = s.Mode;
                    RenderSettings.ambientLight = s.Light;
                    RenderSettings.ambientSkyColor = s.Sky;
                    RenderSettings.ambientEquatorColor = s.Equator;
                    RenderSettings.ambientGroundColor = s.Ground;
                    RenderSettings.fog = s.Fog;
                    if (s.Cam != null)
                    {
                        s.Cam.orthographic = s.Ortho; s.Cam.orthographicSize = s.Size; s.Cam.fieldOfView = s.Fov;
                        s.Cam.nearClipPlane = s.Near; s.Cam.farClipPlane = s.Far; s.Cam.transform.SetPositionAndRotation(s.Pos, s.Rot);
                        s.Cam.clearFlags = s.Clear; s.Cam.backgroundColor = s.Back; s.Cam.cullingMask = s.Mask; s.Cam.allowHDR = s.Hdr;
                        var glow = s.Cam.GetComponent<TowerGlow>();
                        if (glow != null) UnityEngine.Object.Destroy(glow);
                    }
                    if (SceneManager.GetSceneByName(s.Scene).isLoaded) SceneManager.UnloadSceneAsync(s.Scene);
                    Plugin.Trace("[places] left " + d.Name + ", settings restored");
                }
                catch (Exception e) { Plugin.Trace("[places] leaving " + d.Name + ": " + e.Message); }
            }
        }
    }

    internal class PlaceMark : MonoBehaviour
    {
        public string Folder;
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "AssetDataLoaded")]
    internal static class LocationsLoadPatch
    {
        private static void Postfix() => Locations.Loaded();
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "OnDestroy")]
    internal static class LocationsLeavePatch
    {
        private static void Postfix() => Locations.Left();
    }

    [HarmonyPatch(typeof(ClearActionsOnLoadLevel), "OnLevelLoaded")]
    internal static class LocationsSceneGuard
    {
        private static bool Prefix(Scene scene, LoadSceneMode loadSceneMode) =>
            !(loadSceneMode == LoadSceneMode.Additive && Locations.IsOurScene(scene.name));
    }

    internal class TowerMotion : MonoBehaviour
    {
        private readonly List<Light> _lights = new List<Light>();
        private readonly List<float> _base = new List<float>();

        private void Start()
        {
            foreach (var l in GetComponentsInChildren<Light>(true))
            {
                if (l.name == "orb_light" || l.name == "chandelier_light") { _lights.Add(l); _base.Add(l.intensity); }
            }
        }

        private void Update()
        {
            float t = Time.time;
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                float k = l.name == "orb_light"
                    ? 1f + 0.25f * Mathf.Sin(t * 2.1f) + 0.08f * Mathf.Sin(t * 7.3f)
                    : 1f + 0.08f * Mathf.Sin(t * 13.1f) + 0.05f * Mathf.Sin(t * 21.7f + 1.3f);
                l.intensity = _base[i] * k;
            }
        }
    }

    internal class TowerGlow : MonoBehaviour
    {
        public Material mat;
        public int iterations = 6;
        public float threshold = 1.35f;
        public float intensity = 0.38f;
        public string scene = "Tower_magic";
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
            if (!SceneManager.GetSceneByName(scene).isLoaded) Destroy(this);
        }
    }
}
