using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ModSwitch
    {
        private const string Caption = "dialogs.artworkshop.confirm.caption";

        internal static ConfigEntry<bool> Enabled;

        internal static bool On => Enabled == null || Enabled.Value;

        internal static void Bind(ConfigFile home)
        {
            Enabled = home.Bind("Mod", "Enabled", true,
                "Мод включён. Выключенный мод со следующего запуска игры ничего не меняет в клиенте, остаётся только кнопка «Включить мод» в настройках игры. Выключается в окне «Настройки мода».");
        }

        internal static void AskOff()
        {
            try
            {
                DialogFactory.ShowConfirmMessageBox(Caption, result =>
                {
                    if (result == EMessageBoxResult.MB_OK) TurnOff();
                }, Lang.T("Выключить мод? Это действует только после перезапуска игры: клиент сейчас закроется, запусти его снова — он будет обычным, без изменений мода и вида как во Flash. Включить обратно: настройки игры, кнопка «Включить мод»."));
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[mod] disable dialog: " + e.Message); }
        }

        internal static void AskOn()
        {
            try
            {
                DialogFactory.ShowConfirmMessageBox(Caption, result =>
                {
                    if (result == EMessageBoxResult.MB_OK) TurnOn();
                }, Lang.T("Включить мод? Он заработает только после перезапуска игры: клиент сейчас закроется, запусти его снова."));
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[mod] enable dialog: " + e.Message); }
        }

        private static void TurnOff()
        {
            if (Enabled == null) return;
            Enabled.Value = false;
            Plugin.Log?.LogInfo("[mod] disabled from the next game start, Flash look too, closing the client");
            FlashLook.Quit();
        }

        private static void TurnOn()
        {
            if (Enabled == null) return;
            Enabled.Value = true;
            Plugin.Log?.LogInfo("[mod] enabled from the next game start, closing the client");
            FlashLook.Quit();
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "Start")]
    public static class ModSwitchButtonPatch
    {
        private static void Postfix(SetupDialog __instance)
        {
            if (ModSwitch.On || !SideButtons.InWorld()) return;
            try
            {
                var reset = AccessTools.Field(typeof(SetupDialog), "ResetChatButton")?.GetValue(__instance) as Button;
                if (reset == null) { Plugin.Log?.LogWarning("[mod] 'Reset chat position' button not found, 'Enable mod' not added"); return; }

                var src = (RectTransform)reset.transform;
                var go = UnityEngine.Object.Instantiate(reset.gameObject, src.parent);
                Clones.StripHotkeys(go, reset.gameObject);
                go.name = "QoLModOnButton";
                var rt = (RectTransform)go.transform;
                rt.anchorMin = src.anchorMin;
                rt.anchorMax = src.anchorMax;
                rt.pivot = src.pivot;
                rt.sizeDelta = src.sizeDelta;
                rt.localScale = src.localScale;
                rt.anchoredPosition = src.anchoredPosition - new Vector2(0f, src.rect.height + 10f);

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = Lang.T("Включить мод");

                var btn = go.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ModSwitch.AskOn);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[mod] 'Enable mod' button not added: " + e.Message); }
        }
    }
}
