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
    internal static class MagicTower
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Asked;
        private const string SceneName = "Tower_magic";
        private const string Prefab = "magic_tower";
        private static AssetBundle _bundle;
        private static AssetBundle _fx;
        private static Material _bloom;
        private static bool _busy;
        private static Saved _saved;

        private class Saved
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
        }

        internal static string Folder => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "MagicTower");

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Town", "MagicTower3D", true,
                "Башня магии в 3D вместо плоской картинки (со следующего входа в башню).");
            Asked = cfg.Bind("Town", "MagicTower3DAsked", "",
                "Версия файлов башни магии, о которой мод уже спросил. Заполняется сама.");
            TowerFiles.Settle();
        }

        internal static bool SceneLoaded() => SceneManager.GetSceneByName(SceneName).isLoaded;

        internal static void Unload()
        {
            if (_bundle != null) { _bundle.Unload(true); _bundle = null; }
            if (_fx != null) { _fx.Unload(true); _fx = null; }
            _bloom = null;
        }

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
                if (Enabled == null || !Enabled.Value || _busy) return;
                var old = FindOld();
                if (old == null) return;
                TowerFiles.Settle();
                if (!File.Exists(Path.Combine(Folder, "tower_magic")))
                {
                    Plugin.Trace("[tower] tower file missing: " + Folder);
                    return;
                }
                Plugin.Instance.StartCoroutine(Run(old));
            }
            catch (Exception e) { Plugin.Fault("[tower] start: " + e); }
        }

        private static IEnumerator Run(GameObject old)
        {
            _busy = true;
            try
            {
                if (_bundle == null)
                {
                    var req = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "tower_magic"));
                    yield return req;
                    _bundle = req.assetBundle;
                }
                if (_fx == null && File.Exists(Path.Combine(Folder, "tower_magic_fx")))
                {
                    var req = AssetBundle.LoadFromFileAsync(Path.Combine(Folder, "tower_magic_fx"));
                    yield return req;
                    _fx = req.assetBundle;
                    if (_fx != null) _bloom = _fx.LoadAllAssets<Material>().FirstOrDefault(m => m.shader != null && m.shader.name == "Hidden/TowerBloom");
                }
                if (_bundle == null) { Plugin.Warn("[tower] tower bundle failed to load"); yield break; }
                if (old == null) yield break;
                if (!SceneLoaded())
                {
                    var op = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
                    if (op == null) { Plugin.Warn("[tower] scene failed to load"); yield break; }
                    yield return op;
                }
                var scene = SceneManager.GetSceneByName(SceneName);
                if (!scene.IsValid() || old == null) yield break;
                Apply(old, scene);
            }
            finally { _busy = false; }
        }

        private static void Apply(GameObject old, Scene scene)
        {
            var data = ReadData();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var c in root.GetComponentsInChildren<Camera>(true))
                    UnityEngine.Object.Destroy(c.gameObject);
            LightProbes.TetrahedralizeAsync();

            var fon = old.transform.Find("fonSprite");
            if (fon != null) fon.gameObject.SetActive(false);
            foreach (Transform ch in old.transform) if (ch.name.StartsWith("Particle", StringComparison.Ordinal)) ch.gameObject.SetActive(false);

            var cam = old.GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            _saved = new Saved
            {
                Mode = RenderSettings.ambientMode, Light = RenderSettings.ambientLight, Sky = RenderSettings.ambientSkyColor,
                Equator = RenderSettings.ambientEquatorColor, Ground = RenderSettings.ambientGroundColor, Fog = RenderSettings.fog, Cam = cam,
            };
            if (cam != null)
            {
                _saved.Ortho = cam.orthographic; _saved.Size = cam.orthographicSize; _saved.Fov = cam.fieldOfView;
                _saved.Near = cam.nearClipPlane; _saved.Far = cam.farClipPlane; _saved.Pos = cam.transform.position; _saved.Rot = cam.transform.rotation;
                _saved.Clear = cam.clearFlags; _saved.Back = cam.backgroundColor; _saved.Mask = cam.cullingMask; _saved.Hdr = cam.allowHDR;
                cam.orthographic = false;
                cam.fieldOfView = data.Fov;
                cam.nearClipPlane = 0.2f;
                cam.farClipPlane = 60f;
                cam.transform.position = data.CamPos;
                cam.transform.rotation = Quaternion.Euler(data.CamRot);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = data.Back;
                cam.cullingMask |= 1;
                if (SystemInfo.graphicsMemorySize >= 3500 && _bloom != null)
                {
                    cam.allowHDR = true;
                    var glow = cam.GetComponent<TowerGlow>();
                    if (glow == null) glow = cam.gameObject.AddComponent<TowerGlow>();
                    glow.mat = _bloom;
                }
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = data.Sky;
            RenderSettings.ambientEquatorColor = data.Equator;
            RenderSettings.ambientGroundColor = data.Ground;
            RenderSettings.fog = false;

            var host = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "MagicTower3D");
            if (host != null && host.GetComponent<TowerMotion>() == null) host.AddComponent<TowerMotion>();
            Plugin.Trace("[tower] magic tower replaced, camera " + (cam != null ? cam.name : "none") + ", bloom " + (_bloom != null));
        }

        internal static void Left()
        {
            try
            {
                if (_saved == null) return;
                var s = _saved;
                _saved = null;
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
                if (SceneLoaded()) SceneManager.UnloadSceneAsync(SceneName);
                Plugin.Trace("[tower] left magic tower, settings restored");
            }
            catch (Exception e) { Plugin.Trace("[tower] leaving: " + e.Message); }
        }

        private class Data
        {
            public Vector3 CamPos = new Vector3(0, 1.75f, -4.4f);
            public Vector3 CamRot = Vector3.zero;
            public float Fov = 53.7f;
            public Color Sky = new Color(0.16f, 0.12f, 0.26f), Equator = new Color(0.1f, 0.08f, 0.15f), Ground = new Color(0.05f, 0.04f, 0.06f);
            public Color Back = new Color(0.02f, 0.015f, 0.03f);
        }

        private static Data ReadData()
        {
            var d = new Data();
            try
            {
                var path = Path.Combine(Folder, "tower_magic.txt");
                if (!File.Exists(path)) return d;
                foreach (var line in File.ReadAllLines(path))
                {
                    var p = line.Split(' ');
                    float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
                    switch (p[0])
                    {
                        case "camera": d.CamPos = new Vector3(F(1), F(2), F(3)); d.CamRot = new Vector3(F(4), F(5), F(6)); d.Fov = F(7); break;
                        case "ambient": d.Sky = new Color(F(1), F(2), F(3)); d.Equator = new Color(F(4), F(5), F(6)); d.Ground = new Color(F(7), F(8), F(9)); break;
                        case "background": d.Back = new Color(F(1), F(2), F(3)); break;
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[tower] reading tower data: " + e.Message); }
            return d;
        }
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "AssetDataLoaded")]
    internal static class MagicTowerLoadPatch
    {
        private static void Postfix() => MagicTower.Loaded();
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "OnDestroy")]
    internal static class MagicTowerLeavePatch
    {
        private static void Postfix() => MagicTower.Left();
    }

    [HarmonyPatch(typeof(ClearActionsOnLoadLevel), "OnLevelLoaded")]
    internal static class MagicTowerSceneGuard
    {
        private static bool Prefix(Scene scene, LoadSceneMode loadSceneMode) =>
            !(loadSceneMode == LoadSceneMode.Additive && scene.name == "Tower_magic");
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
            if (!SceneManager.GetSceneByName("Tower_magic").isLoaded) Destroy(this);
        }
    }
}
