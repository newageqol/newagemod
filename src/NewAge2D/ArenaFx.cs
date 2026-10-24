using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D;

internal sealed class ArenaFx : MonoBehaviour
{
    internal static ArenaFx Current;

    private sealed class Caster
    {
        public GameObject Go;
        public SpriteRenderer Renderer;
        public bool WasDead;
    }

    private readonly Dictionary<FighterDoll, Caster> _casters = new();
    private readonly List<MeshRenderer> _marks = new();
    private int _markNext;
    private Mesh _markMesh;
    private Material _shadow;
    private Material[] _blood, _soot;
    private ParticleSystem _puffs;
    private Transform _root;
    private Quaternion _sunFace;
    private System.Random _rnd = new();

    internal void Init(Transform root, AssetBundle fx, ArenaCfg cfg, Vector3 sunDir, bool night, Color sun)
    {
        Current = this;
        _root = root;
        var flat = new Vector3(sunDir.x, 0f, sunDir.z);
        _sunFace = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(-flat.normalized, Vector3.up) : Quaternion.identity;
        _shadow = fx.LoadAsset<Material>("shadow");
        _blood = new[] { fx.LoadAsset<Material>("mark_blood0"), fx.LoadAsset<Material>("mark_blood1"), fx.LoadAsset<Material>("mark_blood2") };
        _soot = new[] { fx.LoadAsset<Material>("mark_soot") };
        Crowd(root.Find("Crowd"));
        _motes = cfg.motes;
        Motes(fx.LoadAsset<Material>("mote"), night ? new Color(1f, 0.69f, 0.44f, 0.35f) : Color.Lerp(sun, new Color(1f, 0.95f, 0.85f), 0.5f) * new Color(1, 1, 1, 0.5f));
        Puffs(fx.LoadAsset<Material>("puff"), night ? new Color(0.23f, 0.19f, 0.16f) : Color.Lerp(new Color(0.54f, 0.43f, 0.31f), sun, 0.15f));
        Marks();
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        foreach (var c in _casters.Values)
            if (c.Go != null) Destroy(c.Go);
        _casters.Clear();
        if (_markMesh != null) Destroy(_markMesh);
    }

    private ArenaCrowd _crowd;
    private float[] _motes;

    private void Crowd(Transform crowd)
    {
        if (crowd == null) return;
        _crowd = gameObject.AddComponent<ArenaCrowd>();
        _crowd.Init(crowd);
        Plugin.Log.LogInfo($"[arena3d] crowd {_crowd.Fans.Count}, reactions {string.Join(",", _crowd.Acts)}");
    }

    internal void React(float strength)
    {
        if (_crowd == null || _crowd.Acts.Count == 0) return;
        float now = Time.time;
        foreach (var fan in _crowd.Fans)
        {
            if (fan.Live || fan.BusyUntil > now) continue;
            if (_rnd.NextDouble() > strength * (0.4f + fan.Excite * 0.6f)) continue;
            string act = _crowd.Acts[_rnd.Next(_crowd.Acts.Count)];
            float delay = (float)_rnd.NextDouble() * 0.6f;
            var st = fan.Anim != null ? fan.Anim[act] : null;
            if (st == null) continue;
            fan.BusyUntil = now + delay + st.length + 0.8f;
            _crowd.Wake(fan, act, delay);
        }
    }

    private void Motes(Material mat, Color tint)
    {
        if (mat == null || _motes == null || _motes.Length < 6) return;
        var go = new GameObject("motes");
        go.transform.SetParent(_root, false);
        go.transform.localPosition = new Vector3(_motes[0], _motes[1], -_motes[2]);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.prewarm = true; main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
        main.startColor = tint;
        main.maxParticles = 420;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 32f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(_motes[3], _motes[4], _motes[5]);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.06f, 0.3f); vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.13f); vel.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.08f; noise.frequency = 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new UnityEngine.Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        ps.Play();
    }

    private void Puffs(Material mat, Color dust)
    {
        if (mat == null || _motes == null || _motes.Length < 6) return;
        var go = new GameObject("puffs");
        go.transform.SetParent(_root, false);
        _puffs = go.AddComponent<ParticleSystem>();
        _puffs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = _puffs.main;
        main.loop = false; main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;
        main.startColor = dust;
        var em = _puffs.emission; em.enabled = false;
        var sh = _puffs.shape; sh.enabled = false;
        var size = _puffs.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3.2f));
        var col = _puffs.colorOverLifetime; col.enabled = true;
        var g = new UnityEngine.Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.42f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var limit = _puffs.limitVelocityOverLifetime; limit.enabled = true; limit.dampen = 0.08f; limit.limit = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.material = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortingOrder = 0;
    }

    private void Puff(Vector3 world, int n, float power)
    {
        if (_puffs == null) return;
        for (int i = 0; i < n; i++)
        {
            float a = (float)_rnd.NextDouble() * Mathf.PI * 2f, rr = 0.2f + (float)_rnd.NextDouble() * 0.35f;
            var p = new ParticleSystem.EmitParams
            {
                position = world + new Vector3(Mathf.Cos(a) * rr * 0.5f, 0.1f + (float)_rnd.NextDouble() * 0.2f, Mathf.Sin(a) * rr * 0.5f),
                velocity = new Vector3(Mathf.Cos(a) * rr * 1.6f * power, 0.35f + (float)_rnd.NextDouble() * 0.5f * power, Mathf.Sin(a) * rr * 1.6f * power),
                startSize = 0.35f + (float)_rnd.NextDouble() * 0.35f,
                startLifetime = 0.7f + (float)_rnd.NextDouble() * 0.6f,
                applyShapeToPosition = false,
            };
            _puffs.Emit(p, 1);
        }
    }

    private void Marks()
    {
        var mesh = new Mesh();
        _markMesh = mesh;
        mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        var holder = new GameObject("marks").transform;
        holder.SetParent(_root, false);
        for (int i = 0; i < 70; i++)
        {
            var go = new GameObject("mark");
            go.transform.SetParent(holder, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = short.MinValue + 3;
            go.SetActive(false);
            _marks.Add(r);
        }
    }

    internal void Clear()
    {
        foreach (var m in _marks) if (m != null) m.gameObject.SetActive(false);
    }

    private void Mark(Vector3 world, string kind)
    {
        var list = kind == "soot" ? _soot : _blood;
        if (list == null || _marks.Count == 0) return;
        var r = _marks[_markNext++ % _marks.Count];
        r.sharedMaterial = list[_rnd.Next(list.Length)];
        float size = kind == "soot" ? 1.4f + (float)_rnd.NextDouble() * 0.4f : 0.5f + (float)_rnd.NextDouble() * 0.4f;
        float off = 0.35f + (float)_rnd.NextDouble() * 0.45f, a = (float)_rnd.NextDouble() * Mathf.PI * 2f;
        var local = _root.InverseTransformPoint(world);
        local += new Vector3(Mathf.Cos(a) * off, 0f, Mathf.Sin(a) * off * 0.7f);
        local.y = 0.02f + (_markNext % 7) * 0.001f;
        var t = r.transform;
        t.localPosition = local;
        t.localRotation = Quaternion.Euler(0f, (float)_rnd.NextDouble() * 360f, 0f);
        t.localScale = new Vector3(size, 1f, size * (0.7f + (float)_rnd.NextDouble() * 0.3f));
        r.gameObject.SetActive(true);
    }

    private static readonly string[] Lifeless = { "christmas_tree", "totem", "flag", "stolb", "post", "tree", "barrel", "dummy" };

    private static bool Bleeds(AbstractCharacter who)
    {
        if (who == null) return false;
        if (!(who is BotCharacter)) return true;
        string path = (who.AssetBundlePath ?? "").ToLowerInvariant();
        foreach (var word in Lifeless)
            if (path.Contains(word)) return false;
        return true;
    }

    internal void Hit(AbstractCharacter target, bool magic)
    {
        if (target == null) return;
        var at = target.position;
        if (magic || Bleeds(target)) Mark(at, magic ? "soot" : "blood");
        Puff(at, magic ? 10 : 6, magic ? 1.3f : 0.8f);
        React(0.35f);
    }

    private void LateUpdate()
    {
        if (_rnd.NextDouble() < 0.004) React(0.06f);
        var seen = new HashSet<FighterDoll>();
        foreach (var doll in Fighters.Dolls)
        {
            if (doll == null || doll.View == null) continue;
            seen.Add(doll);
            if (!_casters.TryGetValue(doll, out var c))
            {
                var go = new GameObject("NewAge2D.SunShadow");
                go.transform.SetParent(_root, true);
                var r = go.AddComponent<SpriteRenderer>();
                r.sharedMaterial = _shadow;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.receiveShadows = false;
                c = new Caster { Go = go, Renderer = r, WasDead = doll.Owner != null && doll.Owner.Dead };
                _casters[doll] = c;
            }
            var view = doll.View;
            bool show = view.enabled && view.gameObject.activeInHierarchy && view.sprite != null && _shadow != null;
            if (c.Renderer.enabled != show) c.Renderer.enabled = show;
            if (show)
            {
                c.Renderer.sprite = view.sprite;
                c.Renderer.flipX = view.flipX;
                c.Go.transform.SetPositionAndRotation(doll.Feet, _sunFace);
                c.Go.transform.localScale = view.transform.lossyScale;
            }
            bool dead = doll.Owner != null && doll.Owner.Dead;
            if (dead && !c.WasDead)
            {
                Puff(doll.Feet, 12, 1.4f);
                React(0.9f);
            }
            c.WasDead = dead;
        }
        if (seen.Count == _casters.Count) return;
        foreach (var gone in _casters.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (_casters[gone].Go != null) Destroy(_casters[gone].Go);
            _casters.Remove(gone);
        }
    }
}
