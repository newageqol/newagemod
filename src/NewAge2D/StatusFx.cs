using System.Globalization;
using HarmonyLib;
using Transport.Messages.Common.List;
using Transport.Messages.Responses.Combat.States;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class StatusFx
{
    internal const string Stun = "fx_stun";
    internal const string Prison = "fx_prison";
    private const int PrisonType = 3;
    private const int PrisonId = 37;
    private const float BodyHeight = 1.9f;
    private const float HexShare = 0.9f;
    private const float DomeHeight = 1.1f;
    private const float ChainShare = 0.9f;

    private static bool _tried;
    private static readonly Dictionary<string, GameObject> Prefabs = new();
    private static readonly Dictionary<string, string[]> Anims = new();
    private static readonly HashSet<int> Jailed = new();
    private static readonly HashSet<int> Asked = new();

    internal static bool On => Plugin.CfgStunFx != null && Plugin.CfgStunFx.Value && Ready();

    internal static bool InPrison(int userId) => Jailed.Contains(userId);

    private static bool Ready()
    {
        if (_tried) return Prefabs.Count == 2;
        _tried = true;
        try
        {
            using var stream = typeof(StatusFx).Assembly.GetManifestResourceStream("NewAge2D.status.bundle");
            if (stream == null)
            {
                Plugin.Log.LogWarning("[status fx] stun effects are not inside the plugin, game stars are shown");
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
                Plugin.Log.LogWarning("[status fx] stun effects did not load");
                return false;
            }
            foreach (var name in new[] { Stun, Prison })
            {
                var prefab = bundle.LoadAsset<GameObject>(name);
                var anim = bundle.LoadAsset<TextAsset>(name + "_anim");
                if (prefab == null)
                {
                    Plugin.Log.LogWarning($"[status fx] effect '{name}' is missing in the bundle");
                    Prefabs.Clear();
                    return false;
                }
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && !m.shader.isSupported)
                        {
                            Plugin.Log.LogWarning($"[status fx] shader '{m.shader.name}' is not supported by this video card");
                            Prefabs.Clear();
                            return false;
                        }
                Prefabs[name] = prefab;
                Anims[name] = anim != null ? anim.text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
            }
            if (Trace.On) Trace.Write("status fx: stun and prison effects loaded");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[status fx] " + ex.Message);
            Prefabs.Clear();
            return false;
        }
    }

    internal static StatusLook Build(string name)
    {
        if (!Prefabs.TryGetValue(name, out var prefab) || prefab == null) return null;
        var root = new GameObject("NewAge2D.StatusFx." + name);
        root.SetActive(false);
        var effect = UnityEngine.Object.Instantiate(prefab, root.transform, false);
        effect.name = name;
        var look = root.AddComponent<StatusLook>();
        look.Init(effect.transform, Anims.TryGetValue(name, out var lines) ? lines : new string[0]);
        look.Dome = name == Prison;
        if (Trace.On) Trace.Write($"status fx '{name}' built");
        return look;
    }

    internal static void Place(StatusLook look, FighterDoll doll, int order)
    {
        var t = doll.transform;
        float tall = doll.Tall;
        if (tall <= 0.01f) return;
        float grid = doll.Owner?.parent != null ? Mathf.Abs(doll.Owner.parent.lossyScale.x) : 1f;
        float near = Field.DollSize > 0.0001f ? Mathf.Abs(t.localScale.x) / Field.DollSize : 1f;
        float room = MathConsts.Sqrt3 * 0.5f * MathConsts.HEX_SIZE * (grid > 0.0001f ? grid : 1f) * near * HexShare;
        Fit(look, t.position, t.forward, tall, room, doll.gameObject.layer, order);
    }

    internal static void PlaceAt(StatusLook look, AbstractCharacter character, Camera eye)
    {
        if (character == null || eye == null) return;
        float grid = character.parent != null ? Mathf.Abs(character.parent.lossyScale.x) : 1f;
        if (grid <= 0.0001f) grid = 1f;
        var collider = character.CharacterCollider;
        float tall = collider != null ? collider.bounds.size.y : BodyHeight * grid;
        if (tall <= 0.01f) tall = BodyHeight * grid;
        float room = MathConsts.Sqrt3 * 0.5f * MathConsts.HEX_SIZE * grid * HexShare;
        var spot = character.position;
        int order = Fighters.Layer(eye, spot, character.HexGridPosition) + 1;
        Fit(look, spot, eye.transform.forward, tall, room, collider != null ? collider.gameObject.layer : 0, order);
    }

    private static void Fit(StatusLook look, Vector3 spot, Vector3 forward, float tall, float room, int layer, int order)
    {
        var facing = Vector3.ProjectOnPlane(forward, Vector3.up);
        var upright = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity;
        look.transform.SetPositionAndRotation(spot, upright);
        if (look.Dome)
        {
            look.transform.localScale = new Vector3(room / look.FitRadius, tall * DomeHeight, room / look.FitRadius);
        }
        else
        {
            float s = Mathf.Min(tall / BodyHeight, room / look.FitRadius) * ChainShare;
            look.transform.localScale = Vector3.one * s;
        }
        look.Layer(layer);
        look.Order(order);
        if (!look.gameObject.activeSelf) look.gameObject.SetActive(true);
    }

    internal static void Ask(int userId)
    {
        if (userId == 0 || !Asked.Add(userId)) return;
        try
        {
            NetworkConnection.Instance?.SendRequest(new GetStateGroupsOnUserRequest(userId));
            if (Trace.On) Trace.Write($"fighter {userId}: states asked to tell prison from stun");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[status fx] " + ex.Message); }
    }

    internal static void Release(int userId)
    {
        Asked.Remove(userId);
        Jailed.Remove(userId);
    }

    internal static void Clear()
    {
        Jailed.Clear();
        Asked.Clear();
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EnchantmentPanelsController), "OnStateGroupsOnUserResponse")]
    private static void OnGroups(object msg)
    {
        try
        {
            if (msg is not StateGroupsOnUserResponseMessage groups) return;
            bool jailed = false;
            if (groups.Groups != null)
                foreach (var g in groups.Groups)
                    if (g != null && g.StateType == PrisonType && g.StateId == PrisonId) jailed = true;
            bool was = Jailed.Contains(groups.UserId);
            if (jailed) Jailed.Add(groups.UserId);
            else Jailed.Remove(groups.UserId);
            if (was != jailed && Trace.On) Trace.Write($"fighter {groups.UserId}: astral prison {(jailed ? "on" : "off")}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[status fx] " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(EnchantmentPanelsController), "OnCombatUsersWithChangingStatesResponse")]
    private static void OnChanging(object msg)
    {
        try
        {
            if (msg is not ListIntMessage list || list.Value == null || !StatusFx.On) return;
            foreach (int id in list.Value)
            {
                Asked.Remove(id);
                if (StunStars.Stunned(id)) Ask(id);
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[status fx] " + ex.Message); }
    }
}

internal sealed class StatusLook : MonoBehaviour
{
    private const float FadeTime = 0.8f;
    private const float LingerTime = 1.6f;

    private sealed class Track
    {
        public Renderer Rend;
        public int Prop;
        public bool IsColor;
        public float Length;
        public bool Loop;
        public float[] Values;
        public Color[] Colors;
        public Color Base;
    }

    private readonly List<Track> _tracks = new();
    private readonly List<Material> _local = new();
    private readonly List<Transform> _localAt = new();
    private Renderer[] _parts = new Renderer[0];
    private ParticleSystem[] _systems = new ParticleSystem[0];
    private MaterialPropertyBlock _block;
    private float _start;
    private float _fadeAt = -1f;
    private int _order = int.MinValue;
    private static readonly int InverseMatrix = Shader.PropertyToID("_InverseTransformMatrix");

    internal bool Fading => _fadeAt >= 0f;

    internal float FitRadius { get; private set; } = 1f;

    internal bool Dome { get; set; }

    internal void Init(Transform effect, string[] lines)
    {
        _block = new MaterialPropertyBlock();
        _start = Time.time;
        _parts = GetComponentsInChildren<Renderer>(true);
        _systems = GetComponentsInChildren<ParticleSystem>(true);
        var inv = CultureInfo.InvariantCulture;
        foreach (var raw in lines)
        {
            var f = raw.TrimEnd('\r').Split('\t');
            if (f[0] == "fit")
            {
                if (f.Length > 1 && float.TryParse(f[1], NumberStyles.Float, inv, out float r) && r > 0.01f) FitRadius = r;
                continue;
            }
            var node = f.Length > 1 ? effect.Find(f[1]) : null;
            var rend = node != null ? node.GetComponent<Renderer>() : null;
            if (rend == null) continue;
            if (f[0] == "local")
            {
                _local.Add(rend.material);
                _localAt.Add(node);
                continue;
            }
            if (f.Length < 6) continue;
            var track = new Track
            {
                Rend = rend,
                Prop = Shader.PropertyToID(f[2]),
                IsColor = f[0] == "color",
                Length = Mathf.Max(0.01f, float.Parse(f[3], inv)),
                Loop = f[4] == "1"
            };
            var samples = f[5].Split(' ');
            if (track.IsColor)
            {
                track.Colors = samples.Select(s =>
                {
                    var c = s.Split(',');
                    return new Color(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv), float.Parse(c[3], inv));
                }).ToArray();
                track.Base = rend.sharedMaterial != null && rend.sharedMaterial.HasProperty(track.Prop) ? rend.sharedMaterial.GetColor(track.Prop) : Color.white;
            }
            else track.Values = samples.Select(s => float.Parse(s, inv)).ToArray();
            _tracks.Add(track);
        }
        Apply(0f, false);
    }

    private int _layer = -1;

    internal void Layer(int layer)
    {
        if (layer == _layer) return;
        _layer = layer;
        foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    private void OnEnable()
    {
        if (!Fading) _start = Time.time;
    }

    internal void Order(int order)
    {
        if (order == _order) return;
        _order = order;
        foreach (var r in _parts)
            if (r != null) r.sortingOrder = order;
    }

    internal void Finish()
    {
        if (Fading) return;
        if (!gameObject.activeSelf)
        {
            Release();
            Destroy(gameObject);
            return;
        }
        _fadeAt = Time.time;
        foreach (var ps in _systems)
            if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    private void Update()
    {
        float t = Time.time - _start;
        if (Fading)
        {
            float k = (Time.time - _fadeAt) / FadeTime;
            Apply(Mathf.Clamp01(1f - k), true);
            if (Time.time - _fadeAt >= LingerTime) Destroy(gameObject);
            return;
        }
        Apply(t, false);
    }

    private void LateUpdate()
    {
        for (int i = 0; i < _local.Count; i++)
            if (_local[i] != null && _localAt[i] != null) _local[i].SetMatrix(InverseMatrix, _localAt[i].worldToLocalMatrix);
    }

    private void Apply(float time, bool backward)
    {
        foreach (var track in _tracks)
        {
            if (track.Rend == null) continue;
            float x = backward ? time : track.Loop ? Mathf.Repeat(time / track.Length, 1f) : Mathf.Clamp01(time / track.Length);
            track.Rend.GetPropertyBlock(_block);
            if (track.IsColor) _block.SetColor(track.Prop, Sample(track.Colors, x) * track.Base);
            else _block.SetFloat(track.Prop, Sample(track.Values, x));
            track.Rend.SetPropertyBlock(_block);
        }
    }

    private static float Sample(float[] v, float x)
    {
        if (v.Length == 0) return 0f;
        float p = x * (v.Length - 1);
        int i = Mathf.Clamp((int)p, 0, v.Length - 1);
        int j = Mathf.Min(i + 1, v.Length - 1);
        return Mathf.Lerp(v[i], v[j], p - i);
    }

    private static Color Sample(Color[] v, float x)
    {
        if (v.Length == 0) return Color.white;
        float p = x * (v.Length - 1);
        int i = Mathf.Clamp((int)p, 0, v.Length - 1);
        int j = Mathf.Min(i + 1, v.Length - 1);
        return Color.Lerp(v[i], v[j], p - i);
    }

    private void OnDestroy() => Release();

    private void Release()
    {
        foreach (var m in _local)
            if (m != null) Destroy(m);
        _local.Clear();
        _localAt.Clear();
    }
}
