using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D;

internal sealed class ArenaNight : MonoBehaviour
{
    private static readonly Regex LampName = new("lamp|lantern|torch|candle", RegexOptions.IgnoreCase);
    private static readonly Regex FloodName = new("field|overhead|beam|fill", RegexOptions.IgnoreCase);
    private static readonly Regex GlowName = new("glow|flame|fire|light", RegexOptions.IgnoreCase);
    private static readonly Color Warm = new(1f, 0.78f, 0.5f);

    private readonly List<(Renderer View, LightProbeUsage Was)> _mobs = new();
    private readonly HashSet<Renderer> _known = new();
    private readonly List<Light> _lamps = new();
    private MaterialPropertyBlock _block;
    private Mesh _quad;
    private Material _glow;
    private SphericalHarmonicsL2[] _sh;
    private Transform _root;
    private Camera _eye;
    private ArenaCfg _cfg;
    private float _night, _scan;

    internal void Init(Transform root, Camera eye, ArenaCfg cfg, float night, bool dark, Vector3 mid)
    {
        _root = root;
        _eye = eye;
        _cfg = cfg;
        _night = night;
        _block = new MaterialPropertyBlock();
        _sh = new SphericalHarmonicsL2[1];
        int made = Lamps(mid, dark);
        Plugin.Log.LogInfo($"[arena3d] lamps: map lights {_lamps.Count - made}, added {made}");
    }

    internal static bool Flood(Light light) => FloodName.IsMatch(light.name);

    private int Lamps(Vector3 mid, bool dark)
    {
        var own = new List<Vector3>();
        foreach (var light in _root.GetComponentsInChildren<Light>(true))
        {
            if (light.type != LightType.Point && light.type != LightType.Spot) continue;
            if (light.name.StartsWith("NewAge2D.")) continue;
            if (light.renderMode != LightRenderMode.ForceVertex) light.renderMode = LightRenderMode.ForcePixel;
            if (!Flood(light)) light.intensity *= Mathf.Lerp(1f, _cfg.lampBoost, _night);
            own.Add(light.transform.position);
            _lamps.Add(light);
        }
        var spots = new List<(Vector3 At, int Kind, float Far)>();
        foreach (var view in _root.GetComponentsInChildren<Renderer>(true))
        {
            if (!view.enabled || !view.gameObject.activeInHierarchy) continue;
            var m = view.sharedMaterial;
            string shader = m != null && m.shader != null ? m.shader.name : "";
            var parent = view.transform.parent;
            bool named = LampName.IsMatch(view.name) || (parent != null && LampName.IsMatch(parent.name));
            int kind = -1;
            Vector3 at;
            if (view is ParticleSystemRenderer)
            {
                if (!(shader.Contains("Flame") || shader.Contains("AddLum")) || !(named || GlowName.IsMatch(view.name))) continue;
                at = view.transform.position;
                kind = 1;
            }
            else
            {
                var box = view.bounds;
                at = box.center;
                if (shader.Contains("WindowGlow")) kind = 2;
                else if (shader.Contains("Halo") || shader.Contains("GlassGlow")) kind = 1;
                else if (m != null && m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.3f && named) kind = 1;
                else if (dark && named && box.size.y > 1.5f) { kind = 0; at = new Vector3(box.center.x, box.max.y - 0.35f, box.center.z); }
                else continue;
            }
            float far = new Vector2(at.x - mid.x, at.z - mid.z).magnitude;
            if (far <= _cfg.lampReach) spots.Add((at, kind, far));
        }
        var halo = Shader.Find("Arena/Halo");
        var placed = new List<Vector3>();
        float strength = dark ? 1f : 0.7f;
        int made = 0;
        foreach (var (spot, kind, _) in spots.OrderBy(x => x.Kind).ThenBy(x => x.Far))
        {
            if (made >= (int)_cfg.lampMax) break;
            var at = spot;
            bool taken = false;
            foreach (var p in placed)
                if ((p - at).sqrMagnitude < 1.2f * 1.2f) { taken = true; break; }
            if (taken) continue;
            bool lit = false;
            foreach (var p in own)
                if ((p - at).sqrMagnitude < 2.5f * 2.5f) { lit = true; break; }
            if (lit) continue;
            placed.Add(at);
            var go = new GameObject("NewAge2D.Lamp");
            go.transform.SetParent(_root, false);
            var light = go.AddComponent<Light>();
            light.color = Warm;
            if (kind == 0)
            {
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                light.type = LightType.Spot;
                float high = Mathf.Max(1f, at.y - _root.position.y);
                light.spotAngle = 115f;
                light.range = high * 2.5f;
                light.intensity = _cfg.lampGlow * strength;
            }
            else if (kind == 2)
            {
                var outward = new Vector3(mid.x - at.x, 0f, mid.z - at.z);
                if (outward.sqrMagnitude > 1e-4f) at += outward.normalized * 0.8f;
                light.type = LightType.Point;
                light.range = 6f;
                light.intensity = _cfg.lampGlow * 0.15f * strength;
            }
            else
            {
                light.type = LightType.Point;
                light.range = Mathf.Clamp((at.y - _root.position.y) * 2.5f, 3f, 7f);
                light.intensity = _cfg.lampGlow * 0.5f * strength;
            }
            go.transform.position = at;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            own.Add(at);
            _lamps.Add(light);
            made++;
            if (kind != 0 || halo == null) continue;
            if (_quad == null)
            {
                _quad = new Mesh { name = "NewAge2D.LampQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
                _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _quad.bounds = new Bounds(Vector3.zero, Vector3.one * 3f);
                _glow = new Material(halo) { name = "NewAge2D.LampHalo" };
                _glow.SetColor("_Color", Warm);
                _glow.SetFloat("_Gain", 0.9f);
                _glow.SetFloat("_Size", 1.3f);
            }
            var shine = new GameObject("NewAge2D.LampHalo");
            shine.transform.SetParent(go.transform, false);
            shine.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = shine.AddComponent<MeshRenderer>();
            r.sharedMaterial = _glow;
            r.sortingOrder = short.MinValue;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        return made;
    }

    private void Update()
    {
        if (_night < 0.01f || _cfg.mobGlow <= 0f) return;
        if (Time.unscaledTime >= _scan)
        {
            _scan = Time.unscaledTime + 1f;
            var combat = Fighters.Combat();
            if (combat?.Characters != null)
                foreach (var who in combat.Characters.Values)
                {
                    var model = who != null ? Fighters.Model(who) : null;
                    if (model == null) continue;
                    foreach (var view in model.GetComponentsInChildren<Renderer>(true))
                        if ((view is MeshRenderer || view is SkinnedMeshRenderer) && _known.Add(view))
                            _mobs.Add((view, view.lightProbeUsage));
                }
        }
        float gain = Mathf.Max(0.3f, ArenaPost.Gain);
        var sh = RenderSettings.ambientProbe;
        var toward = _eye != null ? -_eye.transform.forward : Vector3.back;
        toward.y = Mathf.Abs(toward.y) + 0.4f;
        sh.AddAmbientLight(Warm * (0.35f * _night / gain));
        sh.AddDirectionalLight(toward.normalized, Warm, _cfg.mobGlow * _night / gain);
        _sh[0] = sh;
        for (int i = _mobs.Count - 1; i >= 0; i--)
        {
            var view = _mobs[i].View;
            if (view == null) { _mobs.RemoveAt(i); continue; }
            view.lightProbeUsage = LightProbeUsage.CustomProvided;
            view.GetPropertyBlock(_block);
            _block.CopySHCoefficientArraysFrom(_sh);
            view.SetPropertyBlock(_block);
        }
    }

    private void OnDestroy()
    {
        foreach (var (view, was) in _mobs)
            if (view != null) view.lightProbeUsage = was;
        _mobs.Clear();
        _known.Clear();
        if (_quad != null) Destroy(_quad);
        if (_glow != null) Destroy(_glow);
    }
}
