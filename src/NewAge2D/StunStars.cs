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
    }

    private static readonly Dictionary<int, int> Masks = new();
    private static readonly Dictionary<int, Star> Live = new();
    private static readonly Dictionary<string, GameObject> Prefabs = new();
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
            if (effectMask == 0) Masks.Remove(userId);
            else Masks[userId] = effectMask;
            if (Trace.On) Trace.Write($"fighter {userId}: stun stars mask {effectMask}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[stun stars] " + ex.Message); }
    }

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
                string name = Masks[id] > 1 ? Dizzy : Stun;
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
        var collider = character.CharacterCollider;
        Vector3 spot;
        int order;
        if (doll != null && doll.Placed)
        {
            float lift = doll.FitHeight > 0f ? doll.FitHeight : collider != null ? collider.height : 0f;
            spot = doll.Feet + doll.HeadShift(eye) + new Vector3(0f, lift, 0f);
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
        if (star?.Root != null) UnityEngine.Object.Destroy(star.Root);
    }

    private static void Clear()
    {
        foreach (var star in Live.Values) Drop(star);
        Live.Clear();
        Masks.Clear();
        _combat = null;
    }
}
