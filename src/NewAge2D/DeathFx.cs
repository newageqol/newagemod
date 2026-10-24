using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class DeathFx
{
    private static bool _tried;
    private static Material _sprite;
    private static Material _particle;
    private static Texture2D _noise;
    private static Texture2D _dot;

    internal static Material SpriteBase => _sprite;
    internal static Material ParticleBase => _particle;
    internal static Texture2D Noise => _noise;
    internal static Texture2D Dot => _dot;

    internal static bool On => Plugin.FlashFight && Ready();

    private static bool Ready()
    {
        if (_tried) return _sprite != null && _particle != null;
        _tried = true;
        try
        {
            using var stream = typeof(DeathFx).Assembly.GetManifestResourceStream("NewAge2D.death.bundle");
            if (stream == null)
            {
                Plugin.Log.LogWarning("[death fx] effect shaders are not inside the plugin");
                return false;
            }
            var bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int got = stream.Read(bytes, read, bytes.Length - read);
                if (got <= 0) break;
                read += got;
            }
            var bundle = AssetBundle.LoadFromMemory(bytes);
            if (bundle == null)
            {
                Plugin.Log.LogWarning("[death fx] effect shaders did not load");
                return false;
            }
            _sprite = bundle.LoadAsset<Material>("death_sprite");
            _particle = bundle.LoadAsset<Material>("death_particle");
            if (_sprite == null || _particle == null || !_sprite.shader.isSupported || !_particle.shader.isSupported)
            {
                Plugin.Log.LogWarning("[death fx] effect shaders are not supported by this video card");
                _sprite = null;
                _particle = null;
                return false;
            }
            _noise = Cells(128, 8);
            _dot = Soft(64);
            if (Trace.On) Trace.Write("death fx: shaders loaded");
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[death fx] load: " + e.Message);
            _sprite = null;
            _particle = null;
            return false;
        }
    }

    private static Texture2D Cells(int side, int grid)
    {
        var dice = new System.Random(7);
        var points = new Vector2[grid, grid];
        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
                points[x, y] = new Vector2((float)dice.NextDouble(), (float)dice.NextDouble());
        var tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "NewAge2D.DeathNoise" };
        var pixels = new Color32[side * side];
        float cell = (float)side / grid;
        for (int py = 0; py < side; py++)
            for (int px = 0; px < side; px++)
            {
                float gx = px / cell, gy = py / cell;
                int cx = (int)gx, cy = (int)gy;
                float near = 9f, next = 9f;
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int nx = cx + ox, ny = cy + oy;
                        var p = points[(nx % grid + grid) % grid, (ny % grid + grid) % grid];
                        float dx = nx + p.x - gx, dy = ny + p.y - gy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < near) { next = near; near = d; }
                        else if (d < next) next = d;
                    }
                float fine = Mathf.PerlinNoise(px * 0.11f, py * 0.11f);
                float v = Mathf.Clamp01(near * 0.75f + (next - near) * 0.35f + (fine - 0.5f) * 0.25f);
                byte b = (byte)(v * 255f);
                pixels[py * side + px] = new Color32(b, b, b, 255);
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    private static Texture2D Soft(int side)
    {
        var tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "NewAge2D.DeathDot" };
        var pixels = new Color32[side * side];
        float m = (side - 1) * 0.5f;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float r = Mathf.Sqrt((x - m) * (x - m) + (y - m) * (y - m)) / m;
                float a = Mathf.Clamp01(1f - r);
                pixels[y * side + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    internal static float Ease(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    internal static void Box(Material mat, Sprite sprite)
    {
        var b = sprite.bounds;
        mat.SetVector("_Box", new Vector4(b.min.x, b.min.y, b.size.x, b.size.y));
    }

    internal static void Drained(Material mat, float drain, float edge) => Drained(mat, drain, new Color(0.5f, 1.3f, 2.4f, 1f) * edge);

    internal static void Drained(Material mat, float drain, Color edge)
    {
        mat.SetFloat("_Drain", drain);
        mat.SetFloat("_DrainDesat", 0.85f);
        mat.SetColor("_DrainTint", new Color(0.62f, 0.66f, 0.78f, 1f));
        mat.SetColor("_DrainEdge", edge);
        mat.SetFloat("_DrainEdgeW", 0.08f);
    }

    internal static ParticleSystem Particles(string name, int layer, bool additive, Color a, Color b, Vector2 life, Vector2 size, Vector2 speed, Vector3 box, float gravity, int most)
    {
        var go = new GameObject(name);
        go.layer = layer;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.gravityModifier = gravity;
        main.maxParticles = most;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = box;
        sh.randomDirectionAmount = 0.6f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.4f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new UnityEngine.Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        var mat = new Material(_particle) { mainTexture = _dot };
        mat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_Dst", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        r.sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    internal static void Wind(ParticleSystem ps, Vector3 wind)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(wind.x * 0.6f, wind.x * 1.4f);
        vel.y = new ParticleSystem.MinMaxCurve(wind.y * 0.6f, wind.y * 1.4f);
        vel.z = new ParticleSystem.MinMaxCurve(wind.z * 0.6f, wind.z * 1.4f);
    }

    internal static void Rate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = rate;
    }

    internal static void Drop(ParticleSystem ps)
    {
        if (ps == null) return;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        if (r != null && r.sharedMaterial != null) UnityEngine.Object.Destroy(r.sharedMaterial);
        UnityEngine.Object.Destroy(ps.gameObject);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(CombatData), "RemoveCharacter")]
    private static void BeforeRemove(CombatData __instance, int id)
    {
        try
        {
            if (__instance == null || __instance.Characters == null) return;
            if (!__instance.Characters.TryGetValue(id, out var character) || character == null || !character.Dead) return;
            var doll = Fighters.DollOf(character);
            if (doll == null || !doll.HasPicture || !On) return;
            Ashes.Spawn(doll);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[death fx] decay: " + e.Message); }
    }
}

internal sealed class SoulLeaves
{
    private const float Length = 2.6f;
    private readonly FighterDoll _doll;
    private readonly Material _mat;
    private readonly float _start;
    private SpriteRenderer _ghost;
    private Material _ghostMat;
    private ParticleSystem _motes;
    private bool _done;

    internal Material Look => _mat;

    internal SoulLeaves(FighterDoll doll, bool instant)
    {
        _doll = doll;
        _mat = new Material(DeathFx.SpriteBase) { name = "NewAge2D.DeathSprite" };
        _mat.SetTexture("_Noise", DeathFx.Noise);
        _start = Time.time - (instant ? Length : 0f);
        if (instant)
        {
            DeathFx.Drained(_mat, 1.25f, 0f);
            _done = true;
            return;
        }
        var view = doll.View;
        var go = new GameObject("NewAge2D.Soul");
        go.layer = doll.gameObject.layer;
        go.transform.SetParent(doll.transform, false);
        _ghost = go.AddComponent<SpriteRenderer>();
        _ghostMat = new Material(DeathFx.SpriteBase) { name = "NewAge2D.SoulSprite" };
        _ghostMat.SetTexture("_Noise", DeathFx.Noise);
        _ghostMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.One);
        _ghostMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
        _ghostMat.SetFloat("_Premul", 1f);
        _ghostMat.SetColor("_Color", new Color(0.55f, 0.8f, 1.2f, 1f));
        _ghostMat.SetFloat("_Glow", 0.5f);
        _ghostMat.SetColor("_GlowColor", new Color(0.3f, 0.6f, 1f, 1f));
        _ghostMat.SetFloat("_Rise", 0.8f);
        _ghostMat.SetFloat("_Edge", 0.15f);
        _ghostMat.SetColor("_EdgeColor", new Color(0.8f, 1.4f, 2.4f, 1f));
        _ghost.sharedMaterial = _ghostMat;
        _ghost.enabled = false;
        float height = view != null ? Mathf.Max(0.2f, view.bounds.size.y) : 2f;
        float k = height / 2.2f;
        _motes = DeathFx.Particles("NewAge2D.SoulMotes", go.layer, true, new Color(0.6f, 0.85f, 1f), new Color(1f, 0.95f, 0.75f),
            new Vector2(0.8f, 1.6f), new Vector2(0.04f * k, 0.1f * k), new Vector2(0.1f * k, 0.5f * k), new Vector3(1.4f * k, 0.6f * k, 0.3f * k), -0.04f, 300);
        DeathFx.Wind(_motes, (doll.transform.up * 1.1f) * k);
    }

    internal void Sync()
    {
        var view = _doll.View;
        if (view == null) return;
        if (view.sharedMaterial != _mat) view.sharedMaterial = _mat;
        var sprite = view.sprite;
        if (sprite == null) return;
        DeathFx.Box(_mat, sprite);
        if (_done) return;
        float t = Time.time - _start;
        float k = Mathf.Clamp01((t - 0.05f) / 1.1f);
        DeathFx.Drained(_mat, k > 0f ? DeathFx.Ease(k) * 1.25f : 0f, 1f - Mathf.Clamp01((t - 1.0f) / 0.3f));
        float s = (t - 0.1f) / 2.2f;
        bool on = s > 0f && s < 1f;
        if (_ghost != null)
        {
            if (_ghost.enabled != on) _ghost.enabled = on;
            if (on)
            {
                if (_ghost.sprite != sprite) _ghost.sprite = sprite;
                _ghost.flipX = view.flipX;
                _ghost.sortingLayerID = view.sortingLayerID;
                _ghost.sortingOrder = view.sortingOrder + 1;
                _ghost.color = new Color(1f, 1f, 1f, view.color.a);
                float e = DeathFx.Ease(s);
                float height = sprite.bounds.size.y;
                _ghost.transform.localPosition = new Vector3(0f, e * height * 0.55f, -0.001f);
                _ghost.transform.localScale = new Vector3(1f - s * 0.15f, 1f + e * 0.9f, 1f);
                DeathFx.Box(_ghostMat, sprite);
                _ghostMat.SetFloat("_Alpha", Mathf.Clamp01(s * 6f) * (1f - e) * 0.95f);
                _ghostMat.SetFloat("_WaveAmp", 0.035f * Mathf.Clamp01(s * 3f));
                _ghostMat.SetFloat("_WaveT", t * 9f);
                _ghostMat.SetFloat("_Cut", Mathf.Max(0f, (s - 0.45f) * 1.9f));
            }
        }
        if (_motes != null)
        {
            DeathFx.Rate(_motes, on && s < 0.75f ? 50f : 0f);
            if (_ghost != null)
                _motes.transform.position = _ghost.transform.position + _doll.transform.up * (sprite.bounds.size.y * _doll.transform.lossyScale.y * 0.6f);
            var r = _motes.GetComponent<ParticleSystemRenderer>();
            r.sortingLayerID = view.sortingLayerID;
            r.sortingOrder = view.sortingOrder + 2;
        }
        if (t >= Length)
        {
            _done = true;
            DeathFx.Drained(_mat, 1.25f, 0f);
            Clear();
        }
    }

    private void Clear()
    {
        if (_ghost != null) UnityEngine.Object.Destroy(_ghost.gameObject);
        _ghost = null;
        if (_ghostMat != null) UnityEngine.Object.Destroy(_ghostMat);
        _ghostMat = null;
        if (_motes != null)
        {
            DeathFx.Rate(_motes, 0f);
            var motes = _motes;
            _motes = null;
            var r = motes.GetComponent<ParticleSystemRenderer>();
            var mat = r != null ? r.sharedMaterial : null;
            motes.transform.SetParent(null, true);
            UnityEngine.Object.Destroy(motes.gameObject, 2f);
            if (mat != null) UnityEngine.Object.Destroy(mat, 2f);
        }
    }

    internal void Stop(Material skin)
    {
        Clear();
        var view = _doll.View;
        if (view != null && view.sharedMaterial == _mat) view.sharedMaterial = skin;
        UnityEngine.Object.Destroy(_mat);
    }
}

internal sealed class Rebirth
{
    private const float Length = 1.9f;
    private static readonly Color Gold = new(2.4f, 1.8f, 0.6f, 1f);
    private readonly FighterDoll _doll;
    private readonly Material _mat;
    private readonly float _start;
    private SpriteRenderer _ghost;
    private Material _ghostMat;
    private ParticleSystem _motes;

    internal bool Done { get; private set; }

    internal Rebirth(FighterDoll doll)
    {
        _doll = doll;
        _start = Time.time;
        _mat = new Material(DeathFx.SpriteBase) { name = "NewAge2D.RebirthSprite" };
        _mat.SetTexture("_Noise", DeathFx.Noise);
        DeathFx.Drained(_mat, 1.25f, Color.clear);
        var view = doll.View;
        var go = new GameObject("NewAge2D.Return");
        go.layer = doll.gameObject.layer;
        go.transform.SetParent(doll.transform, false);
        _ghost = go.AddComponent<SpriteRenderer>();
        _ghostMat = new Material(DeathFx.SpriteBase) { name = "NewAge2D.ReturnSprite" };
        _ghostMat.SetTexture("_Noise", DeathFx.Noise);
        _ghostMat.SetFloat("_Src", (float)UnityEngine.Rendering.BlendMode.One);
        _ghostMat.SetFloat("_Dst", (float)UnityEngine.Rendering.BlendMode.One);
        _ghostMat.SetFloat("_Premul", 1f);
        _ghostMat.SetColor("_Color", new Color(1.2f, 0.95f, 0.55f, 1f));
        _ghostMat.SetFloat("_Glow", 0.5f);
        _ghostMat.SetColor("_GlowColor", new Color(1f, 0.75f, 0.3f, 1f));
        _ghost.sharedMaterial = _ghostMat;
        _ghost.enabled = false;
        float height = view != null ? Mathf.Max(0.2f, view.bounds.size.y) : 2f;
        float k = height / 2.2f;
        _motes = DeathFx.Particles("NewAge2D.ReturnMotes", go.layer, true, new Color(1f, 0.8f, 0.35f), new Color(1f, 0.95f, 0.7f),
            new Vector2(0.7f, 1.3f), new Vector2(0.04f * k, 0.1f * k), new Vector2(0.2f * k, 0.7f * k), new Vector3(1.0f * k, 1.4f * k, 0.3f * k), -0.06f, 300);
        DeathFx.Wind(_motes, doll.transform.up * 1.3f * k);
    }

    internal void Sync()
    {
        var view = _doll.View;
        if (view == null || Done) return;
        if (view.sharedMaterial != _mat) view.sharedMaterial = _mat;
        var sprite = view.sprite;
        if (sprite == null) return;
        DeathFx.Box(_mat, sprite);
        float t = Time.time - _start;
        float k = Mathf.Clamp01((t - 0.6f) / 0.9f);
        DeathFx.Drained(_mat, (1f - DeathFx.Ease(k)) * 1.25f, k > 0f && k < 1f ? Gold : Color.clear);
        _mat.SetFloat("_Glow", Mathf.Sin(Mathf.Clamp01((t - 1.25f) / 0.55f) * Mathf.PI) * 0.6f);
        _mat.SetColor("_GlowColor", new Color(1f, 0.8f, 0.4f, 1f));
        float s = t / 1.0f;
        bool on = s < 1f;
        if (_ghost != null)
        {
            if (_ghost.enabled != on) _ghost.enabled = on;
            if (on)
            {
                if (_ghost.sprite != sprite) _ghost.sprite = sprite;
                _ghost.flipX = view.flipX;
                _ghost.sortingLayerID = view.sortingLayerID;
                _ghost.sortingOrder = view.sortingOrder + 1;
                _ghost.color = new Color(1f, 1f, 1f, view.color.a);
                float e = DeathFx.Ease(s);
                float height = sprite.bounds.size.y;
                _ghost.transform.localPosition = new Vector3(0f, (1f - e) * height * 1.1f, -0.001f);
                _ghost.transform.localScale = new Vector3(0.85f + e * 0.15f, 1.9f - e * 0.9f, 1f);
                DeathFx.Box(_ghostMat, sprite);
                _ghostMat.SetFloat("_Alpha", Mathf.Clamp01(s * 4f) * (1f - Mathf.Clamp01((s - 0.75f) / 0.25f)) * 0.95f);
                _ghostMat.SetFloat("_WaveAmp", 0.03f * (1f - e));
                _ghostMat.SetFloat("_WaveT", t * 9f);
            }
        }
        if (_motes != null)
        {
            DeathFx.Rate(_motes, t > 1.15f && t < 1.6f ? 70f : 0f);
            _motes.transform.position = view.bounds.center;
            var r = _motes.GetComponent<ParticleSystemRenderer>();
            r.sortingLayerID = view.sortingLayerID;
            r.sortingOrder = view.sortingOrder + 2;
        }
        if (t >= Length) Done = true;
    }

    internal void Stop(Material skin)
    {
        if (_ghost != null) UnityEngine.Object.Destroy(_ghost.gameObject);
        _ghost = null;
        if (_ghostMat != null) UnityEngine.Object.Destroy(_ghostMat);
        _ghostMat = null;
        if (_motes != null)
        {
            DeathFx.Rate(_motes, 0f);
            var motes = _motes;
            _motes = null;
            var r = motes.GetComponent<ParticleSystemRenderer>();
            var mat = r != null ? r.sharedMaterial : null;
            UnityEngine.Object.Destroy(motes.gameObject, 2f);
            if (mat != null) UnityEngine.Object.Destroy(mat, 2f);
        }
        var view = _doll.View;
        if (view != null && view.sharedMaterial == _mat) view.sharedMaterial = skin;
        UnityEngine.Object.Destroy(_mat);
    }
}

internal sealed class Ashes : MonoBehaviour
{
    private const float Length = 3.4f;
    private SpriteRenderer _view;
    private Material _mat;
    private ParticleSystem _ash;
    private ParticleSystem _embers;
    private float _start;

    internal static void Spawn(FighterDoll doll)
    {
        var view = doll.View;
        if (view == null || view.sprite == null) return;
        var go = new GameObject("NewAge2D.Ashes");
        go.layer = doll.gameObject.layer;
        go.transform.SetPositionAndRotation(doll.transform.position, doll.transform.rotation);
        go.transform.localScale = doll.transform.lossyScale;
        var ashes = go.AddComponent<Ashes>();
        ashes.Init(doll, view);
        if (Trace.On) Trace.Write($"'{(doll.Owner != null ? doll.Owner.Login : "?")}' body decayed: crumbles to ash");
    }

    private void Init(FighterDoll doll, SpriteRenderer source)
    {
        _start = Time.time;
        _view = gameObject.AddComponent<SpriteRenderer>();
        _view.sprite = source.sprite;
        _view.flipX = source.flipX;
        _view.sortingLayerID = source.sortingLayerID;
        _view.sortingOrder = source.sortingOrder;
        _view.color = source.color;
        _mat = new Material(DeathFx.SpriteBase) { name = "NewAge2D.AshSprite" };
        _mat.SetTexture("_Noise", DeathFx.Noise);
        if (doll.Passing != null) DeathFx.Drained(_mat, 1.25f, 0f);
        _mat.SetFloat("_Edge", 0.1f);
        _mat.SetFloat("_Rise", -0.6f);
        _mat.SetColor("_EdgeColor", new Color(0.9f, 0.45f, 0.2f, 1f));
        DeathFx.Box(_mat, source.sprite);
        _view.sharedMaterial = _mat;
        var bounds = source.bounds;
        float k = Mathf.Max(0.2f, bounds.size.y) / 2.9f;
        var center = bounds.center;
        var right = transform.right;
        var up = Vector3.up;
        _ash = DeathFx.Particles("NewAge2D.Ash", gameObject.layer, false, new Color(0.16f, 0.14f, 0.13f, 0.9f), new Color(0.32f, 0.3f, 0.28f, 0.8f),
            new Vector2(1.5f, 2.6f), new Vector2(0.05f * k, 0.14f * k), new Vector2(0.1f * k, 0.4f * k), new Vector3(bounds.size.x * 0.55f, bounds.size.y * 0.15f, 0.3f * k), -0.02f, 1200);
        DeathFx.Wind(_ash, (right * 0.9f + up * 0.55f) * k);
        _embers = DeathFx.Particles("NewAge2D.Embers", gameObject.layer, true, new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.4f),
            new Vector2(0.6f, 1.4f), new Vector2(0.03f * k, 0.08f * k), new Vector2(0.2f * k, 0.9f * k), new Vector3(bounds.size.x * 0.5f, bounds.size.y * 0.15f, 0.2f * k), -0.05f, 300);
        DeathFx.Wind(_embers, (right * 0.25f + up * 0.9f) * k);
        foreach (var ps in new[] { _ash, _embers })
        {
            ps.transform.position = center;
            ps.transform.rotation = transform.rotation;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingLayerID = source.sortingLayerID;
            r.sortingOrder = source.sortingOrder + 1;
        }
    }

    private void Update()
    {
        float t = Time.time - _start;
        float k = Mathf.Clamp01((t - 0.5f) / 2.4f);
        _mat.SetFloat("_Char", DeathFx.Ease(Mathf.Clamp01((t - 0.3f) / 0.8f)) * 0.75f);
        _mat.SetFloat("_Cut", k > 0f ? 0.01f + DeathFx.Ease(k) : 0f);
        DeathFx.Rate(_ash, k > 0f && k < 0.95f ? 260f : 0f);
        DeathFx.Rate(_embers, k > 0f && k < 0.8f ? 25f : 0f);
        if (k >= 1f && _view.enabled) _view.enabled = false;
        if (t >= Length + 2.6f) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        DeathFx.Drop(_ash);
        DeathFx.Drop(_embers);
        if (_mat != null) Destroy(_mat);
    }
}
