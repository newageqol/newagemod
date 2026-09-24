using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class Effects
{
    private sealed class PrefabInfo
    {
        public readonly List<string> Renderers = new();
        public readonly List<string> Lights = new();
    }

    private static readonly Dictionary<string, PrefabInfo> Known = new();
    private static readonly Dictionary<int, List<Renderer>> StrippedRenderers = new();
    private static readonly Dictionary<int, List<Light>> StrippedLights = new();
    private static readonly HashSet<string> Seen = new();

    [HarmonyPostfix, HarmonyPatch(typeof(BaseVisualEffectHandler), "GetEffectPrefabFromCache")]
    private static void AfterPrefab(string name, ref GameObject __result)
    {
        if (__result == null) return;
        try
        {
            if (Plugin.CfgVerbose.Value && Asked.Add(name ?? "")) Plugin.Log.LogInfo($"[эффекты] игра запросила префаб «{name}»");
            Remember(__result);
            if (Plugin.HideMagic) Strip(__result, name);
        }
        catch (Exception ex) { Plugin.Log.LogError("[эффекты] " + ex); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(BaseVisualEffectHandler), "AttachToBottom")]
    private static void AfterBottom(ref GameObject __result)
    {
        if (Plugin.HideMagic && __result != null) StripInstance(__result, "снизу");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(BaseVisualEffectHandler), "AttachToTop")]
    private static bool NoTop() => !Plugin.HideMagic;

    [HarmonyPrefix, HarmonyPatch(typeof(BaseVisualEffectHandler), "AttachToHand")]
    private static void BeforeHand(GameObject instance)
    {
        if (Plugin.HideMagic && instance != null) StripInstance(instance, "в руке");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(MagicAttacher), "MakeAttachments")]
    private static bool NoParticles() => !Plugin.HideMagic;

    [HarmonyPostfix, HarmonyPatch(typeof(TargetMagicSelector), "FindPrefab")]
    private static void NoTargetMarker(ref GameObject __result)
    {
        if (Plugin.HideMagic) __result = null;
    }

    private static readonly HashSet<string> Asked = new();

    private static void StripInstance(GameObject instance, string where)
    {
        int count = 0;
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            renderer.enabled = false;
            count++;
        }
        foreach (var light in instance.GetComponentsInChildren<Light>(true))
            light.enabled = false;
        if (Plugin.CfgVerbose.Value && Seen.Add("inst:" + instance.name))
            Plugin.Log.LogInfo($"[эффекты] экземпляр «{instance.name}» {where} скрыт: рендереров {count}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "arcTrailRendererStart")]
    private static bool NoTrailStart(AnimationEventHandler __instance)
    {
        if (!Plugin.FlashFight) return true;
        try
        {
            if (__instance.sourceWeapon != null && __instance.currentItem?.Source != null)
                __instance.currentItem.Source.PlayCombatSound("weapon_threaten_common");
        }
        catch { }
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(AnimationEventHandler), "arcTrailRendererStop")]
    private static bool NoTrailStop(AnimationEventHandler __instance)
    {
        try
        {
            var weapon = __instance.sourceWeapon?.DefaultGameObject;
            return weapon != null && weapon.GetComponent<TrailArcRender>() != null;
        }
        catch { return false; }
    }

    private static string PathOf(Component component, Transform root)
    {
        var parts = new List<string>();
        for (var node = component.transform; node != null && node != root; node = node.parent) parts.Add(node.name);
        parts.Reverse();
        int index = Array.IndexOf(component.GetComponents(component.GetType()), component);
        return string.Join("/", parts) + "#" + index;
    }

    private static T Resolve<T>(Transform root, string path) where T : Component
    {
        int hash = path.LastIndexOf('#');
        if (hash < 0) return null;
        string where = path.Substring(0, hash);
        if (!int.TryParse(path.Substring(hash + 1), out int index)) return null;
        var node = where.Length == 0 ? root : root.Find(where);
        if (node == null) return null;
        var all = node.GetComponents<T>();
        return index >= 0 && index < all.Length ? all[index] : null;
    }

    private static void Remember(GameObject prefab)
    {
        if (Known.ContainsKey(prefab.name)) return;
        var info = new PrefabInfo();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled) info.Renderers.Add(PathOf(renderer, prefab.transform));
        foreach (var light in prefab.GetComponentsInChildren<Light>(true))
            if (light.enabled) info.Lights.Add(PathOf(light, prefab.transform));
        Known[prefab.name] = info;
    }

    private static void Strip(GameObject prefab, string name)
    {
        int id = prefab.GetInstanceID();
        if (StrippedRenderers.ContainsKey(id)) return;
        var renderers = new List<Renderer>();
        var lights = new List<Light>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            renderer.enabled = false;
            renderers.Add(renderer);
        }
        foreach (var light in prefab.GetComponentsInChildren<Light>(true))
        {
            if (!light.enabled) continue;
            light.enabled = false;
            lights.Add(light);
        }
        StrippedRenderers[id] = renderers;
        StrippedLights[id] = lights;
        if (Plugin.CfgVerbose.Value && Seen.Add(name ?? ""))
            Plugin.Log.LogInfo($"[эффекты] 3D-эффект «{name}» скрыт: рендереров {renderers.Count}, источников света {lights.Count}");
    }

    internal static void Set(bool on)
    {
        SwitchLive(!on);
        if (on) return;
        foreach (var list in StrippedRenderers.Values)
            foreach (var renderer in list)
                if (renderer != null) renderer.enabled = true;
        foreach (var list in StrippedLights.Values)
            foreach (var light in list)
                if (light != null) light.enabled = true;
        StrippedRenderers.Clear();
        StrippedLights.Clear();
    }

    private static Transform RootOf(Transform transform, out PrefabInfo info)
    {
        for (var node = transform; node != null; node = node.parent)
        {
            string name = node.name;
            if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - 7);
            if (Known.TryGetValue(name, out info)) return node;
        }
        info = null;
        return null;
    }

    private static void SwitchLive(bool visible)
    {
        if (Known.Count == 0) return;
        try
        {
            var roots = new Dictionary<Transform, PrefabInfo>();
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>(true))
            {
                if (!renderer.gameObject.scene.IsValid()) continue;
                var root = RootOf(renderer.transform, out var info);
                if (root != null) roots[root] = info;
            }
            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if (!light.gameObject.scene.IsValid()) continue;
                var root = RootOf(light.transform, out var info);
                if (root != null) roots[root] = info;
            }
            foreach (var pair in roots)
            {
                if (visible) ShowInstance(pair.Key, pair.Value);
                else HideInstance(pair.Key);
            }
        }
        catch (Exception ex) { Plugin.Log.LogError("[эффекты] " + ex); }
    }

    private static void HideInstance(Transform root)
    {
        var mark = root.GetComponent<EffectMark>() ?? root.gameObject.AddComponent<EffectMark>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            renderer.enabled = false;
            mark.Renderers.Add(renderer);
        }
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (!light.enabled) continue;
            light.enabled = false;
            mark.Lights.Add(light);
        }
    }

    private static void ShowInstance(Transform root, PrefabInfo info)
    {
        var mark = root.GetComponent<EffectMark>();
        if (mark != null)
        {
            foreach (var renderer in mark.Renderers) if (renderer != null) renderer.enabled = true;
            foreach (var light in mark.Lights) if (light != null) light.enabled = true;
            UnityEngine.Object.Destroy(mark);
            return;
        }
        foreach (string path in info.Renderers)
        {
            var renderer = Resolve<Renderer>(root, path);
            if (renderer != null) renderer.enabled = true;
        }
        foreach (string path in info.Lights)
        {
            var light = Resolve<Light>(root, path);
            if (light != null) light.enabled = true;
        }
    }
}

internal sealed class EffectMark : MonoBehaviour
{
    public readonly List<Renderer> Renderers = new();
    public readonly List<Light> Lights = new();
}
