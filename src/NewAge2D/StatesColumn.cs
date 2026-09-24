using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

internal static class StatesColumn
{
    private static readonly System.Reflection.PropertyInfo[] Panels =
    {
        AccessTools.Property(typeof(EnchantmentPanelsController), "SelectedCharacterPanel"),
    };

    private static readonly System.Reflection.FieldInfo Grid = AccessTools.Field(typeof(AbstractCharacterPanel), "enchantmentsPanel");
    private static readonly Dictionary<CanvasGroup, (float Alpha, bool Raycasts, bool Interactable)> Hidden = new();
    private static float _next;

    internal static void Tick()
    {
        bool hide = Plugin.FlashFight && CombatView.Get() != null && Fighters.Combat() != null;
        if (!hide)
        {
            if (Hidden.Count > 0) Clear();
            return;
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        try { Hide(); }
        catch (Exception ex) { Plugin.Log.LogWarning("[значки состояний] " + ex.Message); }
    }

    private static void Hide()
    {
        if (Grid == null) return;
        var controller = Controllers.Get<EnchantmentPanelsController>();
        if (controller == null) return;
        foreach (var property in Panels)
        {
            if (property == null || property.GetValue(controller) is not AbstractCharacterPanel panel || panel == null) continue;
            if (Grid.GetValue(panel) is not Component grid || grid == null) continue;
            var group = grid.GetComponent<CanvasGroup>();
            if (group == null) group = grid.gameObject.AddComponent<CanvasGroup>();
            if (!Hidden.ContainsKey(group))
            {
                Hidden[group] = (group.alpha, group.blocksRaycasts, group.interactable);
                if (Trace.On) Trace.Write($"спрятан столбец значков состояний {property.Name}");
            }
            if (group.alpha != 0f) group.alpha = 0f;
            if (group.blocksRaycasts) group.blocksRaycasts = false;
            if (group.interactable) group.interactable = false;
        }
    }

    internal static void Clear()
    {
        foreach (var pair in Hidden)
        {
            if (pair.Key == null) continue;
            pair.Key.alpha = pair.Value.Alpha;
            pair.Key.blocksRaycasts = pair.Value.Raycasts;
            pair.Key.interactable = pair.Value.Interactable;
        }
        Hidden.Clear();
        _next = 0f;
    }
}
