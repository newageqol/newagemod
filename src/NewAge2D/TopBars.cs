using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

[HarmonyPatch]
internal static class TopBars
{
    private static readonly System.Reflection.FieldInfo LifeBar = AccessTools.Field(typeof(TopPanelView), "Life");
    private static readonly System.Reflection.FieldInfo ManaBar = AccessTools.Field(typeof(TopPanelView), "Mana");
    private static readonly System.Reflection.FieldInfo StaminaBar = AccessTools.Field(typeof(TopPanelView), "Stamina");
    private static readonly System.Reflection.FieldInfo LifeLine = AccessTools.Field(typeof(TopPanelView), "LifeText");
    private static readonly System.Reflection.FieldInfo ManaLine = AccessTools.Field(typeof(TopPanelView), "ManaText");
    private static readonly System.Reflection.FieldInfo StaminaLine = AccessTools.Field(typeof(TopPanelView), "StaminaText");
    private static readonly System.Reflection.FieldInfo ExpowerFill = AccessTools.Field(typeof(TopPanelView), "ExpowerImage");
    private static readonly System.Reflection.FieldInfo ChargesLine = AccessTools.Field(typeof(TopPanelView), "ChargesText");
    private static readonly System.Reflection.FieldInfo ChargesBack = AccessTools.Field(typeof(TopPanelView), "ChargesTextBackground");
    private static readonly System.Reflection.FieldInfo Positions = AccessTools.Field(typeof(TopPanelView), "ChargesIndicatorPositions");
    private static readonly System.Reflection.MethodInfo Redraw = AccessTools.Method(typeof(TopPanelView), "OnIndicatorsChanged");

    private static BaseIndicators _watched;
    private static TopPanelView[] _panels;
    private static float _panelsAt;

    internal static void Tick()
    {
        var me = Plugin.FlashFight ? Fighters.Combat()?.MyCharacter : null;
        BaseIndicators indicators = me != null && me.Initialized ? me.Indicators : null;
        if (ReferenceEquals(indicators, _watched)) return;
        if (_watched != null) _watched.IndicatorsChangedEvent -= Poke;
        _watched = indicators;
        if (_watched != null) _watched.IndicatorsChangedEvent += Poke;
        Poke();
    }

    private static void Poke()
    {
        if (Redraw == null) return;
        try
        {
            if (_panels == null || Time.unscaledTime - _panelsAt > 5f)
            {
                _panels = UnityEngine.Object.FindObjectsOfType<TopPanelView>();
                _panelsAt = Time.unscaledTime;
            }
            foreach (var panel in _panels)
                if (panel != null) Redraw.Invoke(panel, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[панель] " + ex.Message);
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TopPanelView), "OnIndicatorsChanged")]
    private static bool FromCombat(TopPanelView __instance)
    {
        if (!Plugin.FlashFight || __instance == null) return true;
        var me = Fighters.Combat()?.MyCharacter;
        if (me == null || !me.Initialized || me.Indicators == null) return true;
        try
        {
            var indicators = me.Indicators;
            (LifeBar?.GetValue(__instance) as BarIndicator)?.SetProgress(indicators.MaxLife, indicators.CurrentLife);
            (ManaBar?.GetValue(__instance) as BarIndicator)?.SetProgress(indicators.MaxMana, indicators.CurrentMana);
            (StaminaBar?.GetValue(__instance) as BarIndicator)?.SetProgress(indicators.MaxStamina, indicators.CurrentStamina);
            if (LifeLine?.GetValue(__instance) is Text life) life.text = indicators.CurrentLife + " / " + indicators.MaxLife;
            if (ManaLine?.GetValue(__instance) is Text mana) mana.text = indicators.CurrentMana + " / " + indicators.MaxMana;
            if (StaminaLine?.GetValue(__instance) is Text stamina) stamina.text = indicators.CurrentStamina + " / " + indicators.MaxStamina;
            if (Positions?.GetValue(null) is float[] positions && positions.Length > 0)
            {
                int bounded = Mathf.Clamp(indicators.CurrentExpower, 0, positions.Length - 1);
                if (ExpowerFill?.GetValue(__instance) is Image fill) fill.fillAmount = positions[bounded];
                var back = ChargesBack?.GetValue(__instance) as GameObject;
                if (back != null) back.SetActive(bounded > 0);
                if (bounded > 0 && ChargesLine?.GetValue(__instance) is Text charges) charges.text = indicators.CurrentExpower.ToString();
            }
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[панель] значения боя: " + ex.Message);
            return true;
        }
    }
}
