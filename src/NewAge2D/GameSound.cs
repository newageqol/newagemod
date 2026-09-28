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
                    Plugin.Log.LogInfo($"[sound] Flash view: game sound {kept.Value:0.00} → 0, 'Sound' slider in game settings hidden");
                }
                if (settings.SoundVolume != 0f) settings.SoundVolume = 0f;
                return;
            }
            if (kept.Value < 0f) return;
            float back = Mathf.Clamp01(kept.Value);
            kept.Value = -1f;
            settings.SoundVolume = back;
            Plugin.Log.LogInfo($"[sound] Flash view off: game sound restored to {back:0.00}, 'Sound' slider back in place");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[sound] " + ex.Message); }
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
        catch (Exception ex) { Plugin.Log.LogWarning("[sound] 'Sound' slider in game settings: " + ex.Message); }
    }

    private static void Show(GameObject part, Slider music, bool hide)
    {
        if (part == null) return;
        if (hide && music != null && music.transform.IsChildOf(part.transform))
        {
            Plugin.Log.LogWarning($"[sound] '{part.name}' not hidden: it contains the 'Music' slider");
            return;
        }
        part.SetActive(!hide);
    }
}
