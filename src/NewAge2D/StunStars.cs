using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class StunStars
{
    private const string Stun = "stun_target";
    private const string Dizzy = "dizziness_target";

    private sealed class Star
    {
        public string Name;
        public GameObject Root;
        public Renderer[] Parts;
        public StatusLook Look;
    }

    private static readonly Dictionary<int, int> Masks = new();
    private static readonly Dictionary<int, Star> Live = new();
    private static readonly Dictionary<string, GameObject> Prefabs = new();
    private static readonly Dictionary<int, float> Since = new();
    private const float PrisonWait = 0.35f;
    private static ICombatData _combat;

    [HarmonyPrefix, HarmonyPatch(typeof(CombatData), nameof(CombatData.HangEffect))]
    private static void BeforeHang(CombatData __instance, int userId, int effectMask)
    {
        try
        {
            if (!ReferenceEquals(_combat, __instance))
            {
                Clear();
                _combat = __instance;
            }
            if (effectMask == 0)
            {
                Masks.Remove(userId);
                Since.Remove(userId);
                StatusFx.Release(userId);
            }
            else
            {
                if (!Masks.ContainsKey(userId)) Since[userId] = Time.time;
                Masks[userId] = effectMask;
                if (StatusFx.On) StatusFx.Ask(userId);
            }
            if (Trace.On) Trace.Write($"fighter {userId}: stun stars mask {effectMask}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[stun stars] " + ex.Message); }
    }

    internal static bool Stunned(int userId) => Masks.ContainsKey(userId);

    internal static void Remember(string name, GameObject prefab)
    {
        if (prefab != null && (name == Stun || name == Dizzy)) Prefabs[name] = prefab;
    }

    internal static void Tick()
    {
        if (Masks.Count == 0 && Live.Count == 0) return;
        try
        {
            var combat = Fighters.Combat();
            if (combat == null || !ReferenceEquals(combat, _combat))
            {
                Clear();
                return;
            }
            var eye = CombatView.Get()?.CombatCamera;
            bool show = eye != null && Plugin.HideMagic && Plugin.CfgStunStars != null && Plugin.CfgStunStars.Value;
            foreach (int id in Masks.Keys.ToList())
            {
                combat.Characters.TryGetValue(id, out var character);
                if (character == null || character.Dead)
                {
                    Masks.Remove(id);
                    continue;
                }
                if (!show || !character.Initialized) continue;
                bool fx = StatusFx.On;
                string name = fx ? StatusFx.InPrison(id) ? StatusFx.Prison : StatusFx.Stun : Masks[id] > 1 ? Dizzy : Stun;
                if (fx && !Live.ContainsKey(id) && Since.TryGetValue(id, out float since) && Time.time - since < PrisonWait) continue;
                if (Live.TryGetValue(id, out var star) && (star.Root == null || star.Name != name))
                {
                    Drop(star);
                    Live.Remove(id);
                    star = null;
                }
                if (star == null)
                {
                    star = Build(name);
                    if (star == null) continue;
                    Live[id] = star;
                }
                Place(star, character, eye);
            }
            foreach (int id in Live.Keys.ToList())
            {
                if (show && Masks.ContainsKey(id)) continue;
                Drop(Live[id]);
                Live.Remove(id);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[stun stars] " + ex.Message);
            Clear();
        }
    }

    private static Star Build(string name)
    {
        if (name == StatusFx.Stun || name == StatusFx.Prison)
        {
            var look = StatusFx.Build(name);
            return look == null ? null : new Star { Name = name, Root = look.gameObject, Parts = new Renderer[0], Look = look };
        }
        if (!Prefabs.TryGetValue(name, out var prefab) || prefab == null) return null;
        var root = new GameObject("NewAge2D.StunStars");
        var effect = UnityEngine.Object.Instantiate(prefab);
        effect.name = "NewAge2D.StunStars." + name;
        effect.transform.SetParent(root.transform, false);
        Effects.Revive(effect, prefab);
        effect.SetActive(true);
        if (Trace.On) Trace.Write($"stun stars '{name}' built");
        return new Star { Name = name, Root = root, Parts = effect.GetComponentsInChildren<Renderer>(true) };
    }

    private static void Place(Star star, AbstractCharacter character, Camera eye)
    {
        var doll = Fighters.DollOf(character);
        if (star.Look != null)
        {
            if (doll != null && doll.Placed) StatusFx.Place(star.Look, doll, doll.SortingOrder + 1);
            else if (doll == null) StatusFx.PlaceAt(star.Look, character, eye);
            return;
        }
        var collider = character.CharacterCollider;
        Vector3 spot;
        int order;
        if (doll != null && doll.Placed)
        {
            float lift = doll.FitHeight > 0f ? doll.FitHeight : collider != null ? collider.height : 0f;
            spot = eye == null ? doll.Feet + new Vector3(0f, lift, 0f) : eye.ScreenToWorldPoint(Captions.Over(eye, doll.Feet + doll.HeadShift(eye), lift));
            order = doll.SortingOrder + 1;
        }
        else
        {
            spot = character.position + new Vector3(0f, collider != null ? collider.height : 0f, 0f);
            order = Fighters.Layer(eye, spot, character.HexGridPosition) + 1;
        }
        star.Root.transform.position = spot;
        foreach (var part in star.Parts)
            if (part != null && part.sortingOrder != order) part.sortingOrder = order;
    }

    private static void Drop(Star star)
    {
        if (star?.Look != null)
        {
            star.Look.Finish();
            return;
        }
        if (star?.Root != null) UnityEngine.Object.Destroy(star.Root);
    }

    private static void Clear()
    {
        foreach (var star in Live.Values)
            if (star?.Root != null) UnityEngine.Object.Destroy(star.Root);
        Live.Clear();
        Masks.Clear();
        Since.Clear();
        StatusFx.Clear();
        _combat = null;
    }
}
