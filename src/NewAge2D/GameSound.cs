using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAge2D;

[HarmonyPatch]
internal static class GameSound
{
    private static readonly System.Reflection.FieldInfo SliderField = AccessTools.Field(typeof(SetupDialog), "SoundSlider");
    private static readonly System.Reflection.FieldInfo CaptionField = AccessTools.Field(typeof(SetupDialog), "SoundCaptionText");
    private static readonly System.Reflection.FieldInfo MusicField = AccessTools.Field(typeof(SetupDialog), "MusicSlider");

    internal static void Tick()
    {
        try
        {
            var settings = GameSettings.Instance;
            var kept = Plugin.CfgSoundBefore;
            if (settings == null || kept == null) return;
            if (Plugin.FlashLook)
            {
                if (kept.Value < 0f)
                {
                    kept.Value = Mathf.Clamp01(settings.SoundVolume);
                    Plugin.Log.LogInfo($"[звук] Flash-вид: звук игры {kept.Value:0.00} → 0, ползунок «Звук» в настройках игры спрятан");
                }
                if (settings.SoundVolume != 0f) settings.SoundVolume = 0f;
                return;
            }
            if (kept.Value < 0f) return;
            float back = Mathf.Clamp01(kept.Value);
            kept.Value = -1f;
            settings.SoundVolume = back;
            Plugin.Log.LogInfo($"[звук] Flash-вид выключен: звук игры возвращён на {back:0.00}, ползунок «Звук» снова на месте");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[звук] " + ex.Message); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(SetupDialog), "InitializeDialog")]
    private static void AfterDialog(SetupDialog __instance)
    {
        try
        {
            bool hide = Plugin.FlashLook;
            var music = MusicField?.GetValue(__instance) as Slider;
            if (SliderField?.GetValue(__instance) is Slider slider) Show(slider.gameObject, music, hide);
            if (CaptionField?.GetValue(__instance) is Text caption) Show(caption.gameObject, music, hide);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[звук] ползунок «Звук» в настройках игры: " + ex.Message); }
    }

    private static void Show(GameObject part, Slider music, bool hide)
    {
        if (part == null) return;
        if (hide && music != null && music.transform.IsChildOf(part.transform))
        {
            Plugin.Log.LogWarning($"[звук] «{part.name}» не спрятан: внутри него ползунок «Музыка»");
            return;
        }
        part.SetActive(!hide);
    }
}
