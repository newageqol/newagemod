using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class NightTown
    {
        private const int IlleniumLoc = 2;
        private const string SceneName = "Illenium_night";
        private static AssetBundle _bundle;
        private static AssetBundle _fx;
        private static Material _bloom;
        private static Material _labelMat;
        internal static readonly HashSet<string> Moved = new HashSet<string>();
        private static readonly Dictionary<string, Transform> Hits = new Dictionary<string, Transform>();
        private static Font _font;
        private static TMP_FontAsset _tmpFont;
        private static bool _busy;
        private static float _shadowDistance = -1;
        private static int _shadowCascades = -1;

        internal static string Folder => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "NightTown");

        internal static bool WillRun() =>
            !_busy && !_prepping && !_ready && File.Exists(Path.Combine(Folder, "illenium_night"));

        internal static bool SceneLoaded() => SceneManager.GetSceneByName(SceneName).isLoaded;

        internal static bool Held => _bundle != null || _ready || _prepping;

        internal static bool Busy() => SceneLoaded() || _busy || _prepping || _pending != null;

        internal static void Unload()
        {
            Kept.Clear();
            if (_bundle != null) { _bundle.Unload(true); _bundle = null; }
            if (_fx != null) { _fx.Unload(true); _fx = null; }
            _bloom = null;
            _labelMat = null;
            _font = null;
            _tmpFont = null;
        }

        private static bool Roomy() => SystemInfo.graphicsMemorySize >= 2000;

        internal static void Loaded()
        {
            try
            {
                var ud = Controllers.User;
                bool city = ud != null && ud.CurrentLocationId == IlleniumLoc;
                if (!city)
                {
                    Drop();
                    return;
                }
                if (_busy || AssetSync.Off("NightTown")) return;
                if (!_prepping && !_ready)
                {
                    if (!File.Exists(Path.Combine(Folder, "illenium_night")))
                    {
                        Plugin.Trace("[night] town file missing: " + Folder);
                        return;
                    }
                    Prepare();
                    if (!_prepping && !_ready) return;
                }
                var old = FindOld();
                if (old != null) HideOld(old);
                Plugin.Instance.StartCoroutine(Run());
            }
            catch (Exception e) { Plugin.Fault("[night] start: " + e); }
            finally { NightLoad.GameDone(_busy); }
        }

        private static bool _prepping;
        private static bool _ready;
        private static long _bundleMs;
        private static long _sceneMs;

        internal static void Prepare()
        {
            try
            {
                if (_prepping || _ready || _busy || Plugin.Instance == null || AssetSync.Off("NightTown")) return;
                AssetSync.Settle("NightTown");
                if (!File.Exists(Path.Combine(Folder, "illenium_night"))) return;
                _prepping = true;
                Phase("bundle");
                Watch(true);
                Plugin.Instance.StartCoroutine(Prep());
            }
            catch (Exception e) { _prepping = false; Plugin.Trace("[night] prepare: " + e.Message); }
        }

        private static void Drop()
        {
            if (!_ready) return;
            _ready = false;
            var scene = SceneManager.GetSceneByName(SceneName);
            if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            Plugin.Trace("[night] prepared town not needed here, unloaded");
        }

        private static IEnumerator Prep()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var priority = Application.backgroundLoadingPriority;
            bool done = false;
            try
            {
                Application.backgroundLoadingPriority = ThreadPriority.High;
                FastUpload(true);
                NightLoad.Night(0.02f);
                if (_bundle == null) yield return Fetch();
                _bundleMs = clock.ElapsedMilliseconds;
                NightLoad.Night(0.1f);
                Phase("fx");
                if (_fx == null && File.Exists(Path.Combine(Folder, "illenium_night_fx")))
                {
                    var req = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "illenium_night_fx"));
                    yield return req;
                    _fx = req.assetBundle;
                    if (_fx != null) { _bloom = _fx.LoadAllAssets<Material>().FirstOrDefault(m => m.name == "bloom" && m.shader != null && m.shader.name == "Hidden/CityBloom"); _font = _fx.LoadAllAssets<Font>().FirstOrDefault(); _labelMat = _fx.LoadAllAssets<Material>().FirstOrDefault(m => m.name == "label"); }
                }
                if (_bundle == null) { Plugin.Warn("[night] town bundle failed to load"); yield break; }
                long fxMs = clock.ElapsedMilliseconds - _bundleMs;
                Phase("scene read");
                var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
                if (op == null) { Plugin.Warn("[night] scene failed to load"); yield break; }
                var waited = System.Diagnostics.Stopwatch.StartNew();
                long readMs = -1;
                while (!op.isDone && waited.ElapsedMilliseconds < 20000)
                {
                    if (readMs < 0 && op.progress >= 0.9f) { readMs = waited.ElapsedMilliseconds; Phase("scene activate"); }
                    NightLoad.Night(0.1f + 0.85f * Mathf.Clamp01(op.progress / 0.9f));
                    yield return null;
                }
                if (!op.isDone) { Plugin.Warn("[night] scene still loading after 20 s"); yield return op; }
                long loadMs = waited.ElapsedMilliseconds;
                var scene = SceneManager.GetSceneByName(SceneName);
                if (!scene.IsValid() || !scene.isLoaded) yield break;
                Phase("scene hide");
                var step = System.Diagnostics.Stopwatch.StartNew();
                Remember(scene);
                long rememberMs = step.ElapsedMilliseconds;
                int kept = Keep(scene);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var cam in root.GetComponentsInChildren<Camera>(true)) cam.enabled = false;
                    root.SetActive(false);
                }
                _sceneMs = clock.ElapsedMilliseconds - _bundleMs;
                Phase("prepared");
                Plugin.Trace("[night] prepare steps: bundle " + _bundleMs + " ms, fx " + fxMs + " ms, scene read " + (readMs < 0 ? loadMs : readMs) + " ms, scene activate " + (readMs < 0 ? 0 : loadMs - readMs) + " ms, remember light " + rememberMs + " ms, kept in memory " + kept + " assets, hide " + (step.ElapsedMilliseconds - rememberMs) + " ms, " + Stats(scene));
                NightLoad.Night(0.95f);
                done = true;
            }
            finally
            {
                Application.backgroundLoadingPriority = priority;
                FastUpload(false);
                _prepping = false;
                _ready = done;
                if (!_busy) StopWatch(15f);
            }
        }

        private static readonly HashSet<UnityEngine.Object> Kept = new HashSet<UnityEngine.Object>();

        private static int Keep(Scene scene)
        {
            Kept.Clear();
            if (!Roomy()) return 0;
            try
            {
                void Add(UnityEngine.Object o) { if (o != null) Kept.Add(o); }
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            Add(m);
                            Add(m.shader);
                            foreach (int id in m.GetTexturePropertyNameIDs()) Add(m.GetTexture(id));
                        }
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Add(mf.sharedMesh);
                    foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true)) Add(mc.sharedMesh);
                    foreach (var pr in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) Add(pr.mesh);
                    foreach (var an in root.GetComponentsInChildren<Animation>(true))
                        foreach (AnimationState st in an) Add(st.clip);
                    foreach (var l in root.GetComponentsInChildren<Light>(true)) Add(l.cookie);
                }
                foreach (var d in _lit.Select(l => l.Map).Distinct())
                {
                    if (d == null) continue;
                    Add(d.lightmapColor);
                    Add(d.lightmapDir);
                    Add(d.shadowMask);
                }
            }
            catch (Exception e) { Plugin.Trace("[night] keep assets: " + e.Message); }
            return Kept.Count;
        }

        private static int _uploadSlice = -1;
        private static int _uploadBuffer = -1;
        private static bool _uploadPersistent;

        private static void FastUpload(bool on)
        {
            try
            {
                if (on)
                {
                    if (_uploadSlice >= 0) return;
                    _uploadSlice = QualitySettings.asyncUploadTimeSlice;
                    _uploadBuffer = QualitySettings.asyncUploadBufferSize;
                    _uploadPersistent = QualitySettings.asyncUploadPersistentBuffer;
                    QualitySettings.asyncUploadTimeSlice = Math.Max(_uploadSlice, 8);
                    QualitySettings.asyncUploadBufferSize = Math.Max(_uploadBuffer, Roomy() ? 64 : 16);
                    QualitySettings.asyncUploadPersistentBuffer = true;
                }
                else
                {
                    if (_uploadSlice < 0) return;
                    QualitySettings.asyncUploadTimeSlice = _uploadSlice;
                    QualitySettings.asyncUploadBufferSize = _uploadBuffer;
                    QualitySettings.asyncUploadPersistentBuffer = _uploadPersistent;
                    _uploadSlice = -1;
                }
            }
            catch (Exception e) { Plugin.Trace("[night] upload settings: " + e.Message); }
        }

        private static string _phase = "";
        private static bool _watching;
        private static float _watchUntil;

        private static void Phase(string name) => _phase = name;

        private static bool _restart;

        private static void Watch(bool fresh = false)
        {
            _watchUntil = Time.realtimeSinceStartup + 60f;
            if (_watching) { if (fresh) _restart = true; return; }
            if (Plugin.Instance == null) return;
            _watching = true;
            _restart = false;
            Plugin.Instance.StartCoroutine(Frames());
        }

        private static void StopWatch(float after) =>
            _watchUntil = Mathf.Min(_watchUntil, Time.realtimeSinceStartup + after);

        private sealed class FrameLog
        {
            public readonly Dictionary<string, float> Total = new Dictionary<string, float>();
            public readonly Dictionary<string, int> Count = new Dictionary<string, int>();
            public readonly List<string> Order = new List<string>();
            public readonly System.Text.StringBuilder Slow = new System.Text.StringBuilder();
            public int Frames;
            public float Worst;
            public string WorstAt = "";

            public void Add(string at, float ms)
            {
                Frames++;
                if (!Total.ContainsKey(at)) { Total[at] = 0f; Count[at] = 0; Order.Add(at); }
                Total[at] += ms;
                Count[at]++;
                if (ms > Worst) { Worst = ms; WorstAt = at; }
                if (ms >= 50f && Slow.Length < 2000) Slow.Append(at).Append(' ').Append(ms.ToString("0")).Append(" ms; ");
            }

            public void Report()
            {
                if (Frames == 0) return;
                Plugin.Trace("[night] frames: " + Frames + ", worst " + Worst.ToString("0") + " ms in '" + WorstAt + "'; by phase: "
                    + string.Join(", ", Order.Select(p => "'" + p + "' " + Count[p] + " fr " + Total[p].ToString("0") + " ms"))
                    + (Slow.Length > 0 ? "; slow frames: " + Slow : ""));
            }
        }

        private static IEnumerator Frames()
        {
            var log = new FrameLog();
            string was = _phase;
            float last = Time.realtimeSinceStartup;
            try
            {
                while (Time.realtimeSinceStartup < _watchUntil)
                {
                    yield return null;
                    float now = Time.realtimeSinceStartup;
                    float ms = (now - last) * 1000f;
                    last = now;
                    if (_restart)
                    {
                        _restart = false;
                        log.Report();
                        log = new FrameLog();
                        was = "bundle";
                    }
                    log.Add(was == _phase ? was : was + ">" + _phase, ms);
                    was = _phase;
                }
            }
            finally
            {
                _watching = false;
                log.Report();
            }
        }

        private static string Stats(Scene scene)
        {
            try
            {
                int renderers = 0, lights = 0, tris = 0, readable = 0;
                var meshes = new HashSet<Mesh>();
                var textures = new HashSet<Texture>();
                var heavy = new List<KeyValuePair<string, int>>();
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        renderers++;
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            foreach (int id in m.GetTexturePropertyNameIDs())
                            {
                                var t = m.GetTexture(id);
                                if (t != null) textures.Add(t);
                            }
                        }
                    }
                    lights += root.GetComponentsInChildren<Light>(true).Length;
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mesh = mf.sharedMesh;
                        if (mesh == null || !meshes.Add(mesh)) continue;
                        if (mesh.isReadable) readable++;
                        int n = 0;
                        for (int i = 0; i < mesh.subMeshCount; i++) n += (int)(mesh.GetIndexCount(i) / 3);
                        tris += n;
                        heavy.Add(new KeyValuePair<string, int>(mesh.name + (mesh.isReadable ? " rw" : "") + " v" + mesh.vertexCount + " a" + mesh.vertexAttributeCount, n));
                    }
                }
                foreach (var d in LightmapSettings.lightmaps)
                {
                    if (d == null) continue;
                    if (d.lightmapColor != null) textures.Add(d.lightmapColor);
                    if (d.shadowMask != null) textures.Add(d.shadowMask);
                }
                var formats = textures.OfType<Texture2D>()
                    .GroupBy(t => t.format + " " + Math.Max(t.width, t.height))
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.Key + " x" + g.Count());
                return "renderers " + renderers + ", lights " + lights + ", meshes " + meshes.Count + " (" + tris + " tris, readable " + readable + "), textures " + textures.Count + ": " + string.Join(", ", formats)
                    + "; heaviest meshes: " + string.Join(", ", heavy.OrderByDescending(h => h.Value).Take(8).Select(h => h.Key + " " + h.Value + " tris"));
            }
            catch (Exception e) { return "stats failed: " + e.Message; }
        }

        private sealed class Lit
        {
            public Renderer Renderer;
            public LightmapData Map;
            public Vector4 Offset;
        }

        private static readonly List<Lit> _lit = new List<Lit>();

        private static void Remember(Scene scene)
        {
            _lit.Clear();
            var all = LightmapSettings.lightmaps;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    int i = r.lightmapIndex;
                    if (i < 0 || i >= all.Length) continue;
                    _lit.Add(new Lit { Renderer = r, Map = all[i], Offset = r.lightmapScaleOffset });
                }
        }

        private static void Relight()
        {
            if (_lit.Count == 0) return;
            var maps = LightmapSettings.lightmaps.ToList();
            var slots = new Dictionary<Texture2D, int>();
            int added = 0;
            foreach (var lit in _lit)
            {
                if (lit.Renderer == null || lit.Map == null || lit.Map.lightmapColor == null) continue;
                var key = lit.Map.lightmapColor;
                if (!slots.TryGetValue(key, out int n))
                {
                    n = maps.FindIndex(d => d != null && d.lightmapColor == key);
                    if (n < 0) { maps.Add(lit.Map); n = maps.Count - 1; added++; }
                    slots[key] = n;
                }
                lit.Renderer.lightmapIndex = n;
                lit.Renderer.lightmapScaleOffset = lit.Offset;
            }
            LightmapSettings.lightmaps = maps.ToArray();
            Plugin.Trace("[night] baked light restored after the game's town: renderers " + _lit.Count + ", lightmaps added " + added + ", total " + maps.Count);
            _lit.Clear();
        }

        private static AssetBundleCreateRequest _pending;
        private static bool _warmed;

        internal static void Warm()
        {
            try
            {
                if (_warmed || _bundle != null || _pending != null || Plugin.Instance == null || AssetSync.Off("NightTown") || AssetSync.Asking) return;
                if (!SideButtons.InWorld()) return;
                _warmed = true;
                if (!File.Exists(Path.Combine(Folder, "illenium_night"))) return;
                Plugin.Instance.StartCoroutine(Fetch());
                Plugin.Trace("[night] preloading town bundle in background");
            }
            catch (Exception e) { Plugin.Trace("[night] preload: " + e.Message); }
        }

        private static IEnumerator Fetch()
        {
            if (_bundle != null) yield break;
            if (_pending == null) _pending = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "illenium_night"));
            var req = _pending;
            yield return req;
            if (_bundle == null) _bundle = req.assetBundle;
            _pending = null;
        }

        private static IEnumerator Run()
        {
            _busy = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Watch();
            try
            {
                if (_prepping) Phase("wait prepare");
                while (_prepping && clock.ElapsedMilliseconds < 25000) yield return null;
                long waitMs = clock.ElapsedMilliseconds;
                if (!_ready) { Plugin.Warn("[night] town not prepared, old town stays"); yield break; }
                _ready = false;
                var old = FindOld();
                var scene = SceneManager.GetSceneByName(SceneName);
                if (old == null || !scene.IsValid() || !scene.isLoaded) { Plugin.Warn("[night] old town or night scene not found"); yield break; }
                Phase("show");
                var step = System.Diagnostics.Stopwatch.StartNew();
                Relight();
                long relightMs = step.ElapsedMilliseconds;
                foreach (var root in scene.GetRootGameObjects()) root.SetActive(true);
                long activeMs = step.ElapsedMilliseconds;
                Apply(old, scene);
                long applyMs = step.ElapsedMilliseconds;
                GateBack(scene);
                long gateMs = step.ElapsedMilliseconds;
                Raise(scene);
                Judge(scene);
                long raiseMs = step.ElapsedMilliseconds;
                NightLoad.Release(true);
                Phase("shown");
                Plugin.Trace("[night] timing: bundle " + _bundleMs + " ms, scene " + _sceneMs + " ms (alongside the game), waited after the game " + waitMs + " ms, apply " + (clock.ElapsedMilliseconds - waitMs) + " ms");
                Plugin.Trace("[night] show steps: relight " + relightMs + " ms, activate " + (activeMs - relightMs) + " ms, apply " + (applyMs - activeMs) + " ms, gate " + (gateMs - applyMs) + " ms, click areas " + (raiseMs - gateMs) + " ms, release " + (step.ElapsedMilliseconds - raiseMs) + " ms");
            }
            finally
            {
                if (_labels != null) { _labels.enabled = true; _labels = null; }
                _busy = false;
                NightLoad.Release(false);
                StopWatch(3f);
            }
        }

        private static void Raise(Scene scene)
        {
            try
            {
                var data = ReadData();
                var renderers = new List<Renderer>();
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                        if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)) renderers.Add(r);
                int raised = 0;
                var said = new List<string>();
                var parts = new List<string>();
                foreach (var kv in data.Buildings)
                {
                    if (!Hits.TryGetValue(kv.Key, out var t) || t == null) continue;
                    var go = t.gameObject;
                    var box = kv.Value;
                    foreach (var r in renderers)
                    {
                        if (!r.name.StartsWith(kv.Key + "_") || r.name.EndsWith("_click")) continue;
                        var b = r.bounds;
                        var hit = new GameObject(r.name + "_hit");
                        hit.transform.SetParent(t, false);
                        hit.transform.position = b.center;
                        var bc = hit.AddComponent<BoxCollider>();
                        var sz = t.InverseTransformVector(b.size);
                        bc.size = new Vector3(Mathf.Max(0.05f, Mathf.Abs(sz.x)), Mathf.Max(0.05f, Mathf.Abs(sz.y)), Mathf.Max(0.05f, Mathf.Abs(sz.z)));
                        parts.Add(r.name);
                    }
                    if (t.GetComponentInChildren<MeshCollider>(false) != null) continue;
                    float hx = box.size.x * 0.5f, hz = box.size.z * 0.5f;
                    bool any = false;
                    float top = 0f;
                    foreach (var r in renderers)
                    {
                        var b = r.bounds;
                        if (b.size.x > box.size.x * 1.5f || b.size.z > box.size.z * 1.5f) continue;
                        if (Mathf.Abs(b.center.x - box.center.x) > hx || Mathf.Abs(b.center.z - box.center.z) > hz) continue;
                        top = any ? Mathf.Max(top, b.max.y) : b.max.y;
                        any = true;
                    }
                    if (!any) continue;
                    float have = 0f;
                    foreach (var c in t.GetComponentsInChildren<Collider>(false))
                        if (c.enabled) have = Mathf.Max(have, c.bounds.max.y);
                    if (top <= have + 0.15f) continue;
                    var add = go.AddComponent<BoxCollider>();
                    add.center = t.InverseTransformPoint(new Vector3(box.center.x, top * 0.5f, box.center.z));
                    var size = t.InverseTransformVector(new Vector3(box.size.x, top, box.size.z));
                    add.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                    raised++;
                    said.Add(kv.Key + " " + have.ToString("0.00") + "->" + top.ToString("0.00"));
                }
                if (raised > 0) Plugin.Trace("[night] click areas raised to the building tops: " + string.Join(", ", said));
                if (parts.Count > 0) Plugin.Trace("[night] building parts made clickable: " + string.Join(", ", parts));
            }
            catch (Exception e) { Plugin.Trace("[night] raise click areas: " + e.Message); }
        }

        private static Canvas _labels;

        private static bool _gateKnown;
        private static Vector3 _gatePos;
        private static Vector3 _gateSize;

        private static void GateBack(Scene scene)
        {
            try
            {
                MeshRenderer gate = null;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                        if (r.name == "gate") { gate = r; break; }
                var cam = Camera.main;
                var mf = gate != null ? gate.GetComponent<MeshFilter>() : null;
                if (gate == null || cam == null || mf == null || mf.sharedMesh == null) return;
                if (_gateKnown)
                {
                    if (_gateSize.x > 0f) Backing(scene, gate, _gatePos, _gateSize);
                    return;
                }
                var b = gate.bounds;
                var col = gate.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
                var back = new Plane(Vector3.forward, new Vector3(0, 0, b.max.z));
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                int holes = 0;
                const int N = 40;
                var eyes = new List<Vector3>();
                var c0 = cam.transform.position;
                var fw = cam.transform.forward;
                foreach (float away in new[] { 0f, 8f, 16f, 26f, -6f })
                    foreach (float side in new[] { -6f, 0f, 6f })
                        eyes.Add(c0 - fw * away + Vector3.right * side);
                foreach (var eye in eyes)
                for (int i = 0; i <= N; i++)
                    for (int j = 0; j <= N; j++)
                    {
                        var p = new Vector3(Mathf.Lerp(b.center.x - b.extents.x * 0.5f, b.center.x + b.extents.x * 0.5f, i / (float)N),
                                            Mathf.Lerp(b.min.y, b.min.y + b.size.y * 0.62f, j / (float)N), b.min.z);
                        var ray = new Ray(eye, (p - eye).normalized);
                        if (!b.IntersectRay(ray)) continue;
                        if (col.Raycast(ray, out _, 500f)) continue;
                        if (!back.Raycast(ray, out float d)) continue;
                        var q = ray.GetPoint(d);
                        if (q.y > b.min.y + b.size.y * 0.7f) continue;
                        holes++;
                        x0 = Mathf.Min(x0, q.x); x1 = Mathf.Max(x1, q.x);
                        y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y);
                    }
                UnityEngine.Object.Destroy(col);
                if (holes < 3) { _gateKnown = true; _gateSize = Vector3.zero; Plugin.Trace("[night] gate has no see-through opening from this camera"); return; }
                float mx = (x1 - x0) * 0.08f + 0.05f, my = (y1 - y0) * 0.05f + 0.05f;
                x0 -= mx; x1 += mx; y0 = Mathf.Min(y0, b.min.y) - 0.05f; y1 += my;
                _gatePos = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, b.max.z - 0.02f);
                _gateSize = new Vector3(x1 - x0, y1 - y0, 1f);
                _gateKnown = true;
                Backing(scene, gate, _gatePos, _gateSize);
                Plugin.Trace("[night] dark backing behind the gate: holes " + holes + ", x " + x0.ToString("0.00") + ".." + x1.ToString("0.00") + ", y " + y0.ToString("0.00") + ".." + y1.ToString("0.00") + ", z " + b.max.z.ToString("0.00"));
            }
            catch (Exception e) { Plugin.Trace("[night] gate backing: " + e.Message); }
        }

        private static void Backing(Scene scene, MeshRenderer gate, Vector3 pos, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "QoLGateBack";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            var sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh != null ? sh : gate.sharedMaterial.shader) { color = new Color(0.155f, 0.137f, 0.148f, 1f) };
            if (sh == null && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", Texture2D.blackTexture);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static void HideOld(GameObject old)
        {
            _labels = old.GetComponentInChildren<Canvas>(true);
            if (_labels != null) _labels.enabled = false;
            foreach (Transform ch in old.transform)
                if (ch.name == "illenium_fon" || ch.name == "illenium_floor" || ch.name == "illenium_flora" || ch.name == "illeniumBilding" || ch.name == "AnimationEffects")
                    ch.gameObject.SetActive(false);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != old && (root.name == "illenium_fon" || root.name == "illenium_floor" || root.name == "illenium_flora" || root.name == "illeniumBilding"))
                    root.SetActive(false);
            var cam = Camera.main;
            if (cam != null) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.035f, 0.035f, 0.07f); }
        }

        private static GameObject FindOld()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name.StartsWith("Illenium") && go.transform.Find("bilding_colliders") != null) return go;
            return null;
        }

        private static void Apply(GameObject old, Scene scene)
        {
            var data = ReadData();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    UnityEngine.Object.Destroy(cam.gameObject);
            LightProbes.TetrahedralizeAsync();

            foreach (Transform ch in old.transform)
            {
                if (ch.name == "illenium_fon" || ch.name == "illenium_floor" || ch.name == "illenium_flora" || ch.name == "illeniumBilding" || ch.name == "AnimationEffects")
                    ch.gameObject.SetActive(false);
            }
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != old && (root.name == "illenium_fon" || root.name == "illenium_floor" || root.name == "illenium_flora" || root.name == "illeniumBilding"))
                    root.SetActive(false);

            var colliders = old.transform.Find("bilding_colliders");
            var canvas = old.GetComponentInChildren<Canvas>(true);
            var spots = new List<Vector3>();
            foreach (var kv in data.Buildings)
            {
                var t = colliders != null ? colliders.Find(kv.Key) : null;
                if (t != null)
                {
                    foreach (var c in t.GetComponents<Collider>()) UnityEngine.Object.Destroy(c);
                    t.SetParent(null, false);
                    t.position = kv.Value.center;
                    t.rotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    Moved.Add(t.name);
                    Hits[t.name] = t;
                    var click = FindIn(scene, kv.Key + "_click");
                    if (click != null) click.transform.SetParent(t, true);
                    else t.gameObject.AddComponent<BoxCollider>().size = kv.Value.size;
                }
                string text = Text(canvas, kv.Key);
                if (string.IsNullOrEmpty(text)) continue;
                if (kv.Key == "magic_tower" && data.Obbs.TryGetValue(kv.Key, out var mt))
                {
                    Arc(scene, text, new Vector3(mt.cx, 0, mt.cz), Mathf.Max(mt.ex, mt.ez) + 0.35f, 236f, spots);
                    continue;
                }
                if (kv.Key == "arena" && data.Obbs.TryGetValue(kv.Key, out var ar))
                {
                    Arc(scene, text, new Vector3(ar.cx, 0, ar.cz), Mathf.Max(ar.ex, ar.ez) + 0.42f, 222f, spots);
                    continue;
                }
                if (kv.Key.StartsWith("undergroundGate")) continue;
                if (data.Obbs.TryGetValue(kv.Key, out var ob))
                {
                    var rot = Quaternion.Euler(0, ob.yaw, 0);
                    var faces = new[] { (rot * Vector3.back, ob.ez, ob.ex), (rot * Vector3.forward, ob.ez, ob.ex), (rot * Vector3.right, ob.ex, ob.ez), (rot * Vector3.left, ob.ex, ob.ez) };
                    var best = kv.Key == "armory" ? faces.Where(f => f.Item3 >= f.Item2).OrderByDescending(f => f.Item1.x).First() : faces.OrderBy(f => f.Item1.z).First();
                    var at = new Vector3(ob.cx, 0, ob.cz) + best.Item1 * (best.Item2 + 0.28f);
                    if (kv.Key == "blacksmith") at += Vector3.left * 0.55f;
                    Place(scene, text, at, best.Item1, best.Item3 * 2f, 3f, spots);
                }
                else
                {
                    var at = new Vector3(kv.Value.center.x, 0, kv.Value.center.z) + Vector3.back * (Mathf.Min(kv.Value.size.x, kv.Value.size.z) * 0.5f + 0.25f);
                    Place(scene, text, at, Vector3.back, 2f, 3f, spots);
                }
            }
            foreach (var kv in data.Dungeons)
            {
                if (kv.Key != "undergroundGate_l") continue;
                string text = Text(canvas, kv.Key);
                if (string.IsNullOrEmpty(text)) continue;
                var toC = new Vector3(-kv.Value.x, 0, -kv.Value.z).normalized;
                var at = new Vector3(kv.Value.x, 0, kv.Value.z) + toC * (kv.Value.w + 0.55f);
                Place(scene, text, at, Vector3.back, 3.2f, 3f, spots);
            }
            Clear(scene, spots);
            if (canvas != null) canvas.gameObject.SetActive(false);

            var camCtl = UnityEngine.Object.FindObjectOfType<CameraControl>();
            var camObj = Camera.main;
            if (data.HasCamera)
            {
                var con = camCtl != null ? Traverse.Create(camCtl).Field("constraint").GetValue<CameraConstraint>() : null;
                if (con == null) con = UnityEngine.Object.FindObjectOfType<CameraConstraint>();
                if (con != null)
                {
                    var fwd = Quaternion.Euler(data.CamRot) * Vector3.forward;
                    var start = data.CamPos - fwd * 9.5f;
                    float k = -fwd.z / fwd.y;
                    float top = start.y, low = 8f;
                    con.lowerMinPos = new Vector3(-6f, low, -3.5f - k * low);
                    con.minPos = new Vector3(-3f, low, -3.5f - k * top);
                    con.lowermaxPos = new Vector3(6f, top, 6f - k * low);
                    con.maxPos = new Vector3(3f, top, 6f - k * top);
                    con.minAngle = data.CamRot.x - 0.05f;
                    con.maxAngle = data.CamRot.x + 0.05f;
                    con.cameraPos = start;
                    con.cameraRotation = data.CamRot;
                    if (camCtl != null) camCtl.SetConstraint(con);
                }
                else if (camObj != null)
                {
                    camObj.transform.position = data.CamPos;
                    camObj.transform.eulerAngles = data.CamRot;
                }
                if (camObj != null) { camObj.fieldOfView = data.Fov; camObj.backgroundColor = new Color(0.035f, 0.035f, 0.07f); }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = data.Sky;
            RenderSettings.ambientEquatorColor = data.Equator;
            RenderSettings.ambientGroundColor = data.Ground;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = data.FogColor;
            RenderSettings.fogStartDistance = data.FogStart;
            RenderSettings.fogEndDistance = data.FogEnd;

            bool full = !_light;
            if (_shadowDistance < 0) { _shadowDistance = QualitySettings.shadowDistance; _shadowCascades = QualitySettings.shadowCascades; }
            QualitySettings.shadowDistance = 70;
            QualitySettings.shadowCascades = 2;
            QualitySettings.softParticles = true;
            _dimmed.Clear();
            if (camObj != null) { camObj.depthTextureMode |= DepthTextureMode.Depth; _hdr = camObj.allowHDR; }
            Look(scene, camObj, full);
            if (camObj != null && camObj.GetComponent<NightClock>() == null) camObj.gameObject.AddComponent<NightClock>();
            Plugin.Trace("[night] town replaced, quality " + (full ? "full" : "light") + ", buildings " + data.Buildings.Count);
        }

        private static bool _light;
        private static bool _judged;
        private static bool _judging;
        private static bool _hdr;
        private static readonly HashSet<Light> _dimmed = new HashSet<Light>();
        private const float SlowFrameMs = 22f;
        private const float GainNeeded = 0.75f;
        private const float RetryAfter = 900f;
        private static float _retryAt;

        private static void Look(Scene scene, Camera cam, bool full)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional) { l.shadows = full ? LightShadows.Soft : LightShadows.Hard; continue; }
                    if (full)
                    {
                        if (_dimmed.Remove(l)) l.enabled = true;
                        continue;
                    }
                    if (l.enabled && l.bakingOutput.lightmapBakeType == LightmapBakeType.Realtime && !(l.color.r > 0.9f && l.color.b < 0.4f))
                    {
                        l.enabled = false;
                        _dimmed.Add(l);
                    }
                }
            if (cam == null) return;
            var glow = cam.GetComponent<NightGlow>();
            if (full && _bloom != null)
            {
                cam.allowHDR = true;
                if (glow == null) glow = cam.gameObject.AddComponent<NightGlow>();
                glow.mat = _bloom;
                glow.enabled = true;
            }
            else
            {
                if (glow != null) glow.enabled = false;
                cam.allowHDR = _hdr;
            }
        }

        private static void Judge(Scene scene)
        {
            if (_judging || Plugin.Instance == null) return;
            if (_judged)
            {
                if (!_light || Time.realtimeSinceStartup < _retryAt) return;
                _judged = false;
                _light = false;
                Look(scene, Camera.main, true);
                Plugin.Trace("[night] quality check: light for 15 minutes, trying full again");
            }
            _judging = true;
            Plugin.Instance.StartCoroutine(Measure());
        }

        private static bool Watching() =>
            SceneLoaded() && !_busy && !_prepping && Application.isFocused && Camera.main != null && Camera.main.GetComponent<NightClock>() != null;

        private static IEnumerator Sample(List<float> into, int count, float settle, bool strict)
        {
            float calm = Time.realtimeSinceStartup + settle;
            float last = Time.realtimeSinceStartup;
            while (into.Count < count)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                float ms = (now - last) * 1000f;
                last = now;
                if (!Watching())
                {
                    if (strict) yield break;
                    calm = now + settle;
                    continue;
                }
                if (now < calm) continue;
                into.Add(ms);
            }
        }

        private static float Median(List<float> list)
        {
            var sorted = list.OrderBy(v => v).ToList();
            return sorted[sorted.Count / 2];
        }

        private static IEnumerator Measure()
        {
            try
            {
                var full = new List<float>();
                yield return Sample(full, 150, 3f, false);
                float fullMs = Median(full);
                if (fullMs <= SlowFrameMs)
                {
                    _judged = true;
                    Plugin.Trace("[night] quality check: full, median frame " + fullMs.ToString("0.0") + " ms over " + full.Count + " frames");
                    yield break;
                }
                var scene = SceneManager.GetSceneByName(SceneName);
                if (!Watching() || !scene.isLoaded) yield break;
                Look(scene, Camera.main, false);
                var light = new List<float>();
                yield return Sample(light, 90, 0.5f, true);
                scene = SceneManager.GetSceneByName(SceneName);
                if (light.Count < 90)
                {
                    var cam = Camera.main;
                    if (scene.isLoaded && !_busy && cam != null && cam.GetComponent<NightClock>() != null) Look(scene, cam, true);
                    Plugin.Trace("[night] quality check interrupted, will repeat on the next visit");
                    yield break;
                }
                float lightMs = Median(light);
                bool helps = lightMs <= fullMs * GainNeeded;
                _light = helps;
                _judged = true;
                if (helps) _retryAt = Time.realtimeSinceStartup + RetryAfter;
                if (!helps) Look(scene, Camera.main, true);
                Plugin.Trace("[night] quality check: " + (helps ? "light" : "full") + ", median frame full " + fullMs.ToString("0.0") + " ms, light " + lightMs.ToString("0.0") + " ms" + (helps ? ", next try of full in 15 minutes" : " (effects are not what slows it, kept full)"));
            }
            finally { _judging = false; }
        }

        internal static void Left()
        {
            if (_shadowDistance >= 0)
            {
                QualitySettings.shadowDistance = _shadowDistance;
                QualitySettings.shadowCascades = _shadowCascades;
                _shadowDistance = -1;
            }
        }

        private static Vector3 Door(string name, Vector3 center)
        {
            if (name == "armory" || name == "arena" || name == "new_illGate" || name == "portal" || name == "obelisk" || name == "cityHall") return Vector3.back;
            var d = new Vector3(0, 0, -10) - new Vector3(center.x, 0, center.z);
            return d.sqrMagnitude < 0.01f ? Vector3.back : d.normalized;
        }

        private static GameObject FindIn(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var t = root.transform.Find(name);
                if (t != null) return t.gameObject;
            }
            return null;
        }

        private static string Text(Canvas canvas, string name)
        {
            string text = null;
            try { if (ResourceStrings.IsKeyPresent("lc." + name + "_txt")) text = ResourceStrings.GetString("lc." + name + "_txt"); } catch { }
            if (string.IsNullOrEmpty(text) && canvas != null)
            {
                var src = canvas.transform.Find(name + "_txt");
                var ui = src != null ? src.GetComponentInChildren<UnityEngine.UI.Text>(true) : null;
                if (ui != null) text = ui.text;
            }
            return text;
        }

        private static TextMeshPro Make(Scene scene, string text, Vector3 ground, Vector3 front, float size)
        {
            if (_tmpFont == null && _font != null) _tmpFont = TMP_FontAsset.CreateFontAsset(_font);
            var go = new GameObject("night_label");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = ground + Vector3.up * 0.03f;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, -front);
            go.transform.localScale = new Vector3(1f, 1.3f, 1f);
            var tmp = go.AddComponent<TextMeshPro>();
            if (_tmpFont != null) tmp.font = _tmpFont;
            tmp.text = text;
            tmp.enableWordWrapping = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size;
            tmp.color = new Color(1f, 0.84f, 0.5f);
            tmp.rectTransform.sizeDelta = new Vector2(10, 2);
            if (_labelMat != null && _tmpFont != null)
            {
                var lm = new Material(_labelMat);
                lm.mainTexture = _tmpFont.atlasTexture;
                tmp.fontSharedMaterial = lm;
            }
            return tmp;
        }

        private static void Place(Scene scene, string text, Vector3 ground, Vector3 front, float width, float size, List<Vector3> spots)
        {
            try
            {
                var tmp = Make(scene, text, ground, front, size);
                tmp.ForceMeshUpdate();
                float pw = tmp.preferredWidth;
                if (pw > 0.01f && pw > width * 0.92f) { tmp.fontSize = Mathf.Max(1.2f, size * width * 0.92f / pw); tmp.ForceMeshUpdate(); pw = tmp.preferredWidth; }
                var along = Vector3.Cross(Vector3.up, front).normalized;
                for (float k = -0.5f; k <= 0.51f; k += 0.25f) spots.Add(ground + along * pw * k);
            }
            catch (Exception e) { Plugin.Trace("[night] label: " + e.Message); }
        }

        private static void Arc(Scene scene, string text, Vector3 center, float radius, float middleDeg, List<Vector3> spots)
        {
            try
            {
                float size = 3f;
                var widths = new float[text.Length];
                float total = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    var probe = Make(scene, text[i].ToString(), center, Vector3.back, size);
                    probe.ForceMeshUpdate();
                    widths[i] = Mathf.Max(probe.preferredWidth, 0.12f) * 1.05f;
                    total += widths[i];
                    UnityEngine.Object.Destroy(probe.gameObject);
                }
                float span = total / radius * Mathf.Rad2Deg;
                float a = middleDeg - span * 0.5f;
                for (int i = 0; i < text.Length; i++)
                {
                    float mid = (a + widths[i] * 0.5f / radius * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    var outward = new Vector3(Mathf.Cos(mid), 0, Mathf.Sin(mid));
                    var at = center + outward * radius;
                    Make(scene, text[i].ToString(), at, outward, size);
                    spots.Add(at);
                    a += widths[i] / radius * Mathf.Rad2Deg;
                }
            }
            catch (Exception e) { Plugin.Trace("[night] arc: " + e.Message); }
        }

        private static void Clear(Scene scene, List<Vector3> spots)
        {
            int n = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var g = mf.gameObject;
                    bool plant = g.name == "cypress" || g.name == "tree" || g.name == "willow";
                    bool house = g.name == "house" || g.name == "stall" || g.name == "cart" || g.name == "crates" || g.name == "barrels";
                    if (!plant && !house) continue;
                    var p = g.transform.position;
                    float reach = house ? 1.6f : 1.1f;
                    foreach (var s in spots)
                    {
                        bool onTop = plant && (new Vector2(p.x - s.x, p.z - s.z)).sqrMagnitude < 1.1f * 1.1f;
                        bool before = Mathf.Abs(p.x - s.x) < reach && p.z < s.z + 0.2f && p.z > s.z - (house ? 2.8f : 2.2f);
                        if (onTop || before) { g.SetActive(false); n++; break; }
                    }
                }
            Plugin.Trace("[night] trees removed from labels: " + n);
        }

        private class Box
        {
            public Vector3 center;
            public Vector3 size;
            public Vector3 front = Vector3.back;
        }

        private class Obb
        {
            public float cx, cz, ex, ez, yaw;
        }

        private class Data
        {
            public readonly Dictionary<string, Box> Buildings = new Dictionary<string, Box>();
            public readonly Dictionary<string, Vector3> Labels = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Obb> Obbs = new Dictionary<string, Obb>();
            public readonly Dictionary<string, Vector4> Dungeons = new Dictionary<string, Vector4>();
            public bool HasCamera;
            public Vector3 CamPos, CamRot;
            public float Fov = 21;
            public Color Sky = new Color(0.3f, 0.33f, 0.46f), Equator = new Color(0.2f, 0.21f, 0.28f), Ground = new Color(0.06f, 0.06f, 0.08f);
            public Color FogColor = new Color(0.16f, 0.18f, 0.24f);
            public float FogStart = 34, FogEnd = 70;
        }

        private static Data ReadData()
        {
            var d = new Data();
            var path = Path.Combine(Folder, "illenium_night.txt");
            if (!File.Exists(path)) return d;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split(' ');
                float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                try
                {
                    switch (p[0])
                    {
                        case "building":
                            var bx = new Box { center = new Vector3(F(2), F(3), F(4)), size = new Vector3(F(5), F(6), F(7)) };
                            if (p.Length >= 10) bx.front = new Vector3(F(8), 0, F(9)).normalized;
                            d.Buildings[p[1]] = bx;
                            break;
                        case "dungeon": d.Dungeons[p[1]] = new Vector4(F(2), 0, F(3), F(4)); break;
                        case "obb": d.Obbs[p[1]] = new Obb { cx = F(2), cz = F(3), ex = F(4), ez = F(5), yaw = F(6) }; break;
                        case "label": d.Labels[p[1]] = new Vector3(F(2), F(3), F(4)); break;
                        case "camera": d.HasCamera = true; d.CamPos = new Vector3(F(1), F(2), F(3)); d.CamRot = new Vector3(F(4), F(5), F(6)); d.Fov = F(7); break;
                        case "ambient": d.Sky = new Color(F(1), F(2), F(3)); d.Equator = new Color(F(4), F(5), F(6)); d.Ground = new Color(F(7), F(8), F(9)); break;
                        case "fog": d.FogColor = new Color(F(1), F(2), F(3)); d.FogStart = F(4); d.FogEnd = F(5); break;
                    }
                }
                catch (Exception e) { Plugin.Trace("[night] data line '" + line + "': " + e.Message); }
            }
            return d;
        }
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "AssetDataLoaded")]
    internal static class NightTownLoadPatch
    {
        private static void Postfix() => NightTown.Loaded();
    }

    [HarmonyPatch(typeof(ClearActionsOnLoadLevel), "OnLevelLoaded")]
    internal static class NightTownSceneGuard
    {
        private static bool Prefix(Scene scene, LoadSceneMode loadSceneMode) =>
            !(loadSceneMode == LoadSceneMode.Additive && scene.name == "Illenium_night");
    }

    [HarmonyPatch(typeof(ColliderNotifyObject), "OnPointerClick")]
    internal static class NightTownLeftOnly
    {
        private static bool Prefix(ColliderNotifyObject __instance, UnityEngine.EventSystems.PointerEventData eventData) =>
            eventData == null || eventData.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left || !NightTown.Moved.Contains(__instance.name);
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "OnDestroy")]
    internal static class NightTownLeavePatch
    {
        private static void Postfix() => NightTown.Left();
    }

    internal class NightClock : MonoBehaviour
    {
        private int _shown = -1;
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            var now = DateTime.UtcNow.AddHours(3);
            _next = Time.unscaledTime + (60 - now.Second);
            int stamp = now.Hour * 60 + now.Minute;
            if (stamp == _shown) return;
            _shown = stamp;
            Shader.SetGlobalFloat("_CityHour", now.Hour);
            Shader.SetGlobalFloat("_CityMin", now.Minute);
        }
    }

    internal class NightGlow : MonoBehaviour
    {
        public Material mat;
        public int iterations = 6;
        public float threshold = 1.0f;
        public float intensity = 0.6f;
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
            if (!SceneManager.GetSceneByName("Illenium_night").isLoaded) Destroy(this);
        }
    }
}
