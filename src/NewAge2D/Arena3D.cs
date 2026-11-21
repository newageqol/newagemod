using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D;

internal sealed partial class Arena3D
{
    private static readonly (string Map, string Bundle)[] Maps = { ("arenaCombatMap1", "arena1"), ("arenaCombatMap2", "arena2"), ("arenaCombatMap3", "arena3"), ("arenaCombatMap4", "arena4"), ("arenaCombatMap5", "arena5"), ("arenaCombatMap6", "arena6"), ("arenaCombatMap8", "arena8"), ("arenaCombatMap9", "arena9"), ("arenaCombatMap7", "arena7"), ("21x21_arena", "arena21"), ("Bugurt", "arenabugurt") };

    private sealed class Scene
    {
        public AssetBundle Bundle;
        public GameObject Prefab;
        public ArenaCfg Cfg;
        public long Stamp;
        public GameObject Ready;
        public readonly Dictionary<string, ArenaBake> Bakes = new();
    }

    private static readonly Dictionary<string, Scene> Scenes = new();
    private static AssetBundle _fxBundle;
    private static Material _flame;
    private static Material _grade;
    private static Material _post;
    private static bool _loading;

    private readonly string _bundle;
    private GameObject _root;
    private ArenaGrade _gradeOn;
    private ArenaPost _postOn;
    private Camera _eye;
    private bool _eyeHdr;
    private readonly List<Light> _lights = new();
    private bool _saved;
    private AmbientMode _ambientMode;
    private Color _ambientSky, _ambientEquator, _ambientGround, _fogColor;
    private bool _fog;
    private FogMode _fogMode;
    private float _fogStart, _fogEnd, _shadowDistance;
    private int _shadowCascades, _pixelLights;
    private ShadowResolution _shadowResolution;
    private float _lodBias;
    private AnisotropicFiltering _aniso;
    private int _frameRate;
    private ArenaBake.Applied _bakeOn;
    private const int FrameCap = 60;

    internal Arena3D(string bundle) { _bundle = bundle; }

    internal bool Shown => _root != null;

    internal ArenaCfg Cfg { get; private set; } = new ArenaCfg();

    internal static bool Any;
    internal static bool EdgeRing = true;
    internal static Color DollTint = Color.white;
    private static readonly Color FieldWarm = new(1f, 0.82f, 0.55f);
    private static readonly Color DollWarm = new(0.97f, 0.94f, 0.88f);

    internal static float TestHour = -1f;

    private static string Dir
    {
        get
        {
            string home = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
            string shared = Path.Combine(Path.GetDirectoryName(home) ?? home, "Arenas");
            return File.Exists(Path.Combine(shared, "arena_fx")) ? shared : Path.Combine(home, "arena");
        }
    }

    internal static string For(Field.Ground ground)
    {
        if (ground?.File == null || !Plugin.CfgArena3D.Value || !QolAllows()) return null;
        foreach (var (map, bundle) in Maps)
            if (ground.File.IndexOf(map, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (!File.Exists(Path.Combine(Dir, "arena_fx"))) return null;
                return File.Exists(Path.Combine(Dir, bundle)) ? bundle : null;
            }
        return null;
    }

    private static Func<bool> _qolArenas;
    private static bool _qolLooked;

    private static bool QolAllows()
    {
        if (!_qolLooked)
        {
            _qolLooked = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var prop = asm.GetType("NewAgeQoL.Plugin", false)?.GetProperty("NewArenas", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop == null) continue;
                    _qolArenas = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), prop.GetGetMethod());
                    break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[arena3d] mod switch: " + e.Message); }
        }
        try { return _qolArenas == null || _qolArenas(); }
        catch { return true; }
    }

    private sealed class Loader : MonoBehaviour { }

    private static long Stamp(string bundle)
    {
        try
        {
            var f = new FileInfo(Path.Combine(Dir, bundle));
            var c = new FileInfo(Path.Combine(Dir, bundle + ".cam.json"));
            return (f.Exists ? f.LastWriteTimeUtc.Ticks ^ f.Length : 0L) ^ (c.Exists ? c.LastWriteTimeUtc.Ticks : 0L);
        }
        catch { return 0L; }
    }

    private static Loader _host;
    private static GameObject _shelf;
    private static int _upSlice, _upBuffer;
    private static bool _upKeep;

    private static Loader Host()
    {
        if (_host != null) return _host;
        var go = new GameObject("NewAge2D.Arena3DLoader");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        _host = go.AddComponent<Loader>();
        return _host;
    }

    private static GameObject Shelf()
    {
        if (_shelf != null) return _shelf;
        _shelf = new GameObject("NewAge2D.Arena3DShelf");
        UnityEngine.Object.DontDestroyOnLoad(_shelf);
        _shelf.hideFlags = HideFlags.HideAndDontSave;
        _shelf.SetActive(false);
        return _shelf;
    }

    internal static void Preload(Field.Ground ground)
    {
        if (_loading) return;
        string bundle;
        try { bundle = For(ground); }
        catch (Exception e) { Plugin.Log.LogWarning("[arena3d] preload: " + e.Message); return; }
        if (bundle == null || Ready(bundle)) return;
        Host().StartCoroutine(Prepare(bundle));
    }

    private static bool Ready(string bundle) =>
        Scenes.TryGetValue(bundle, out var scene) && scene.Stamp == Stamp(bundle) && scene.Prefab != null && scene.Ready != null && LightReady(bundle, scene);

    private static float HourNow(ArenaCfg cfg)
    {
        float hour = TestHour >= 0f ? TestHour : (float)Kyiv.Now.TimeOfDay.TotalHours;
        if (cfg != null && cfg.dusk >= 0f) hour = cfg.dusk;
        return hour;
    }

    private static bool LightReady(string bundle, Scene scene)
    {
        string preset = ArenaBake.Preset(HourNow(scene.Cfg));
        return scene.Bakes.ContainsKey(preset) || !File.Exists(ArenaBake.File(Dir, bundle, preset));
    }

    private static IEnumerator LoadLight(string bundle, Scene scene)
    {
        string preset = ArenaBake.Preset(HourNow(scene.Cfg));
        if (scene.Bakes.ContainsKey(preset)) yield break;
        ArenaBake got = null;
        yield return ArenaBake.Load(ArenaBake.File(Dir, bundle, preset), b => got = b);
        if (got != null) scene.Bakes[preset] = got;
    }

    private static void Drop(string bundle)
    {
        if (!Scenes.TryGetValue(bundle, out var scene)) return;
        if (scene.Ready != null) UnityEngine.Object.Destroy(scene.Ready);
        foreach (var bake in scene.Bakes.Values) bake.Unload();
        if (scene.Bundle != null) scene.Bundle.Unload(true);
        Scenes.Remove(bundle);
    }

    private static IEnumerator Prepare(string bundle)
    {
        _loading = true;
        try
        {
            if (Scenes.TryGetValue(bundle, out var old) && old.Stamp != Stamp(bundle))
            {
                Drop(bundle);
                Plugin.Log.LogInfo($"[arena3d] {bundle} changed on disk, reloading");
            }
            if (!Scenes.ContainsKey(bundle)) yield return Load(bundle);
            if (Scenes.TryGetValue(bundle, out var scene) && scene.Prefab != null)
            {
                if (scene.Ready == null) Stock(bundle, scene);
                yield return LoadLight(bundle, scene);
            }
        }
        finally { _loading = false; }
    }

    private static void FastUpload(bool on)
    {
        if (on)
        {
            _upSlice = QualitySettings.asyncUploadTimeSlice;
            _upBuffer = QualitySettings.asyncUploadBufferSize;
            _upKeep = QualitySettings.asyncUploadPersistentBuffer;
            QualitySettings.asyncUploadTimeSlice = Math.Max(_upSlice, 8);
            QualitySettings.asyncUploadBufferSize = Math.Max(_upBuffer, SystemInfo.graphicsMemorySize >= 2000 ? 64 : 16);
            QualitySettings.asyncUploadPersistentBuffer = true;
            return;
        }
        QualitySettings.asyncUploadTimeSlice = _upSlice;
        QualitySettings.asyncUploadBufferSize = _upBuffer;
        QualitySettings.asyncUploadPersistentBuffer = _upKeep;
    }

    private static IEnumerator Lean(string bundle, GameObject prefab)
    {
        int colliders = 0;
        foreach (var c in prefab.GetComponentsInChildren<Collider>(true))
        {
            UnityEngine.Object.DestroyImmediate(c, true);
            colliders++;
        }
        var keep = new HashSet<Mesh>();
        foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
        {
            var shape = ps.shape;
            if (shape.mesh != null) keep.Add(shape.mesh);
            if (ps.TryGetComponent<ParticleSystemRenderer>(out var pr) && pr.mesh != null) keep.Add(pr.mesh);
        }
        var solid = new List<Mesh>();
        foreach (var mf in ArenaOcclude.Filters(prefab.transform))
            if (mf.sharedMesh != null && mf.sharedMesh.isReadable && keep.Add(mf.sharedMesh)) solid.Add(mf.sharedMesh);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        if (solid.Count > 0)
        {
            var probe = new GameObject("NewAge2D.ArenaProbe");
            var options = probe.AddComponent<MeshCollider>().cookingOptions;
            UnityEngine.Object.DestroyImmediate(probe);
            var ids = solid.Select(m => m.GetInstanceID()).ToArray();
            var job = System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var id in ids) Physics.BakeMesh(id, false, options);
            });
            while (!job.IsCompleted) yield return null;
            if (job.Exception != null) Plugin.Log.LogWarning("[arena3d] occluder bake: " + job.Exception.GetBaseException().Message);
        }
        long baked = clock.ElapsedMilliseconds;
        int unread = 0;
        var seen = new HashSet<Mesh>();
        var slice = System.Diagnostics.Stopwatch.StartNew();
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || !mesh.isReadable || keep.Contains(mesh) || !seen.Add(mesh)) continue;
            mesh.UploadMeshData(true);
            unread++;
            if (slice.ElapsedMilliseconds > 6)
            {
                yield return null;
                slice.Restart();
            }
        }
        Plugin.Log.LogInfo($"[arena3d] {bundle} lean: colliders removed {colliders}, occluders baked {solid.Count} in {baked} ms, meshes unread {unread}");
    }

    private static void Stock(string bundle, Scene scene)
    {
        foreach (var other in Scenes.Values)
            if (other != scene && other.Ready != null)
            {
                UnityEngine.Object.Destroy(other.Ready);
                other.Ready = null;
            }
        var shelf = Shelf();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var copy = UnityEngine.Object.Instantiate(scene.Prefab, shelf.transform, false);
        scene.Ready = copy;
        Plugin.Log.LogInfo($"[arena3d] {bundle} ready: copy {clock.ElapsedMilliseconds} ms");
    }

    private static IEnumerator Load(string bundle)
    {
        float t0 = Time.realtimeSinceStartup;
        var scene = new Scene();
        FastUpload(true);
        try
        {
            var sceneReq = AssetBundle.LoadFromFileAsync(Path.Combine(Dir, bundle));
            yield return sceneReq;
            scene.Bundle = sceneReq.assetBundle;
            if (_fxBundle == null)
            {
                var fxReq = AssetBundle.LoadFromFileAsync(Path.Combine(Dir, "arena_fx"));
                yield return fxReq;
                _fxBundle = fxReq.assetBundle;
                if (_fxBundle != null)
                {
                    _flame = _fxBundle.LoadAsset<Material>("flame");
                    _grade = _fxBundle.LoadAsset<Material>("grade");
                    _post = _fxBundle.LoadAsset<Material>("post");
                    ArenaLook.FireField = _fxBundle.LoadAsset<GameObject>("fire_field");
                    ArenaLook.FireWall = _fxBundle.LoadAsset<GameObject>("fire_wall");
                }
            }
            if (scene.Bundle != null)
            {
                var prefabReq = scene.Bundle.LoadAllAssetsAsync<GameObject>();
                yield return prefabReq;
                scene.Prefab = prefabReq.allAssets.Length > 0 ? prefabReq.allAssets[0] as GameObject : null;
                var cfg = scene.Bundle.LoadAllAssets<TextAsset>();
                scene.Cfg = cfg.Length > 0 ? ArenaCfg.Parse(cfg[0].text) : new ArenaCfg();
                var camPath = Path.Combine(Dir, bundle + ".cam.json");
                if (File.Exists(camPath))
                {
                    try { JsonUtility.FromJsonOverwrite(File.ReadAllText(camPath), scene.Cfg); }
                    catch (Exception e) { Plugin.Log.LogWarning("[arena3d] camera file: " + e.Message); }
                }
                if (scene.Prefab != null) yield return Lean(bundle, scene.Prefab);
            }
            scene.Stamp = Stamp(bundle);
            Scenes[bundle] = scene;
        }
        finally
        {
            FastUpload(false);
        }
        Plugin.Log.LogInfo($"[arena3d] {bundle} loaded in {Time.realtimeSinceStartup - t0:0.00} s, model {(scene.Prefab != null ? "ok" : "missing")}");
    }

    internal IEnumerator Attach(Transform grid, Camera eye, Func<bool> alive, Action shown)
    {
        EdgeRing = Scenes.TryGetValue(_bundle, out var known) && known.Cfg != null && known.Cfg.edgeRing;
        while (_loading) yield return null;
        if (!Ready(_bundle))
        {
            Host().StartCoroutine(Prepare(_bundle));
            while (_loading) yield return null;
        }
        if (!Scenes.TryGetValue(_bundle, out var scene) || scene.Prefab == null || grid == null || !alive())
        {
            EdgeRing = true;
            yield break;
        }
        Cfg = scene.Cfg ?? new ArenaCfg();
        EdgeRing = Cfg.edgeRing;

        Save();
        var steps = new System.Text.StringBuilder();
        var step = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string name)
        {
            steps.Append(' ').Append(name).Append(' ').Append(step.ElapsedMilliseconds);
            step.Restart();
        }
        bool reused = scene.Ready != null;
        if (scene.Ready != null)
        {
            _root = scene.Ready;
            scene.Ready = null;
            _root.transform.SetParent(grid, false);
        }
        else _root = UnityEngine.Object.Instantiate(scene.Prefab, grid, false);
        _root.name = "NewAge2D.Arena3D";
        _root.layer = grid.gameObject.layer;
        _root.transform.localPosition = Vector3.zero;
        _root.transform.localRotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;
        Mark(reused ? "activate" : "instantiate");
        var views = _root.GetComponentsInChildren<Renderer>(true);
        DimMapLights();
        QualitySettings.shadowDistance = 120f;
        QualitySettings.shadowCascades = 4;
        if (Cfg.shadowDist > 0f) QualitySettings.shadowDistance = Cfg.shadowDist;
        if (Cfg.shadowCascades > 0) QualitySettings.shadowCascades = Cfg.shadowCascades;
        QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, 32);
        QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        if (Application.targetFrameRate <= 0 || Application.targetFrameRate > FrameCap) Application.targetFrameRate = FrameCap;
        float hour = HourNow(Cfg);
        scene.Bakes.TryGetValue(ArenaBake.Preset(hour), out var bake);
        if (bake != null) hour = ArenaBake.Hour(bake.Name);
        bool night = ArenaLook.IsNight(hour);
        ArenaLook.Use(Cfg);
        var look = _root.AddComponent<ArenaLook>();
        look.Apply(_root.transform, hour, _flame);
        Mark("look");
        float ground = _root.transform.position.y;
        var mid = _root.transform.position;
        if (eye != null)
        {
            var ray = eye.transform.forward;
            float run = ray.y < -0.05f ? (eye.transform.position.y - ground) / -ray.y : 20f;
            mid = eye.transform.position + ray * run;
        }
        if (Cfg.fieldGlow > 0f && look.NightGrade > 0.01f && eye != null && !_root.GetComponentsInChildren<Light>(true).Any(l => l.type == LightType.Spot && ArenaNight.Flood(l)))
        {
            var glow = new GameObject("NewAge2D.FieldGlow").AddComponent<Light>();
            glow.transform.SetParent(_root.transform, false);
            glow.transform.position = new Vector3(mid.x, ground + Cfg.fieldGlowHigh, mid.z);
            glow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            glow.type = LightType.Spot;
            glow.spotAngle = Cfg.fieldGlowAngle;
            glow.range = Cfg.fieldGlowHigh * 2.5f;
            glow.color = FieldWarm;
            glow.intensity = Cfg.fieldGlow * look.NightGrade;
            glow.shadows = LightShadows.None;
            glow.renderMode = LightRenderMode.ForcePixel;
        }
        _root.AddComponent<ArenaNight>().Init(_root.transform, eye, Cfg, look.NightGrade, night, mid);
        Mark("night");
        int vertexLights = LightBudget(_root.transform, mid);
        _root.AddComponent<ArenaLite>().Init(_root.transform, mid);
        Mark("lights");
        if (_fxBundle != null) _root.AddComponent<ArenaFx>().Init(_root.transform, _fxBundle, Cfg, look.SunDir, night, look.SunColor);
        if (_fxBundle != null && Cfg.storm)
            _root.AddComponent<ArenaStorm>().Init(_root.transform, Cfg, _fxBundle.LoadAsset<Material>("storm_streak"), _fxBundle.LoadAsset<Material>("storm_cloud"), night, look.SunColor);
        Mark("fx");
        if (eye != null) eye.backgroundColor = ArenaCfg.Hex(night ? Cfg.bgNight : Cfg.bgDay);
        if (eye != null) _root.AddComponent<ArenaOcclude>().Init(_root.transform, eye, Cfg);
        Mark("occlude");
        if (eye != null && _post != null)
        {
            _eye = eye;
            _eyeHdr = eye.allowHDR;
            eye.allowHDR = true;
            _postOn = eye.gameObject.AddComponent<ArenaPost>();
            _postOn.Setup(_post, hour, look.NightGrade, Cfg.nightVig, Cfg.nightCold, Cfg.nightLevel);
        }
        else if (eye != null && _grade != null)
        {
            _gradeOn = eye.gameObject.AddComponent<ArenaGrade>();
            _gradeOn.Mat = _grade;
            _gradeOn.Night = look.NightGrade;
        }
        DollTint = Color.Lerp(Color.white, _postOn != null ? DollWarm : Field.Night, look.NightGrade);
        int lowered = BelowDolls(_root);
        float doll = Mathf.Lerp(1f, Mathf.Clamp(Cfg.nightDoll, 0.2f, 1.5f), look.NightGrade);
        DollTint = new Color(DollTint.r * doll, DollTint.g * doll, DollTint.b * doll, DollTint.a);
        if (bake != null) _bakeOn = bake.Apply(views, _root.transform);
        Mark("rest");
        Any = true;
        EdgeRing = Cfg.edgeRing;
        shown?.Invoke();
        Plugin.Log.LogInfo($"[arena3d] {_bundle} shown, storm {Cfg.storm}, game hour {hour:0.0}, night grade {look.NightGrade:0.00}, post {(_postOn != null ? "on" : "off")}, map lights dimmed {_lights.Count}, effects under dolls {lowered}, vertex lights {vertexLights}, ms:{steps}");
    }

    private const int PixelLights = 8;

    private static int LightBudget(Transform root, Vector3 mid)
    {
        var points = root.GetComponentsInChildren<Light>()
            .Where(l => l.isActiveAndEnabled && l.type == LightType.Point && l.renderMode != LightRenderMode.ForceVertex && !l.name.StartsWith("collapse"))
            .OrderBy(l => (l.transform.position - mid).sqrMagnitude)
            .ToList();
        int demoted = 0;
        foreach (var light in points.Skip(PixelLights))
        {
            light.renderMode = LightRenderMode.ForceVertex;
            light.range *= 1.3f;
            light.intensity *= 1.2f;
            demoted++;
        }
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            if (light.renderMode == LightRenderMode.ForceVertex && light.TryGetComponent<Animation>(out var flicker))
                flicker.enabled = false;
        return demoted;
    }

    private static int BelowDolls(GameObject root)
    {
        int lowered = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.renderQueue <= 2500 || material.renderQueue >= 4000) continue;
                renderer.sortingOrder = short.MinValue;
                lowered++;
                break;
            }
        return lowered;
    }

    private void DimMapLights()
    {
        foreach (var root in Field.Scenery)
        {
            if (root == null) continue;
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light == null || !light.enabled) continue;
                if (_root != null && light.transform.IsChildOf(_root.transform)) continue;
                light.enabled = false;
                _lights.Add(light);
            }
        }
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
        {
            if (light == null || !light.enabled || light.type != LightType.Directional) continue;
            if (_root != null && light.transform.IsChildOf(_root.transform)) continue;
            light.enabled = false;
            _lights.Add(light);
        }
    }

    private void Save()
    {
        _saved = true;
        _ambientMode = RenderSettings.ambientMode;
        _ambientSky = RenderSettings.ambientSkyColor;
        _ambientEquator = RenderSettings.ambientEquatorColor;
        _ambientGround = RenderSettings.ambientGroundColor;
        _fog = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogColor = RenderSettings.fogColor;
        _fogStart = RenderSettings.fogStartDistance;
        _fogEnd = RenderSettings.fogEndDistance;
        _shadowDistance = QualitySettings.shadowDistance;
        _shadowCascades = QualitySettings.shadowCascades;
        _pixelLights = QualitySettings.pixelLightCount;
        _shadowResolution = QualitySettings.shadowResolution;
        _lodBias = QualitySettings.lodBias;
        _aniso = QualitySettings.anisotropicFiltering;
        _frameRate = Application.targetFrameRate;
    }

    internal void Detach()
    {
        ArenaBake.Undo(_bakeOn);
        _bakeOn = null;
        Any = false;
        EdgeRing = true;
        DollTint = Color.white;
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
        if (_gradeOn != null) UnityEngine.Object.Destroy(_gradeOn);
        _gradeOn = null;
        if (_postOn != null) UnityEngine.Object.Destroy(_postOn);
        _postOn = null;
        if (_eye != null) _eye.allowHDR = _eyeHdr;
        _eye = null;
        foreach (var light in _lights)
            if (light != null) light.enabled = true;
        _lights.Clear();
        if (!_saved) return;
        _saved = false;
        RenderSettings.ambientMode = _ambientMode;
        RenderSettings.ambientSkyColor = _ambientSky;
        RenderSettings.ambientEquatorColor = _ambientEquator;
        RenderSettings.ambientGroundColor = _ambientGround;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogColor = _fogColor;
        RenderSettings.fogStartDistance = _fogStart;
        RenderSettings.fogEndDistance = _fogEnd;
        QualitySettings.shadowDistance = _shadowDistance;
        QualitySettings.shadowCascades = _shadowCascades;
        QualitySettings.pixelLightCount = _pixelLights;
        QualitySettings.shadowResolution = _shadowResolution;
        QualitySettings.lodBias = _lodBias;
        QualitySettings.anisotropicFiltering = _aniso;
        Application.targetFrameRate = _frameRate;
    }
}
