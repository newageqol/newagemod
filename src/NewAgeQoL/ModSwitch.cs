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
        private static ConfigEntry<bool> _flashWas;

        internal static bool On => Enabled == null || Enabled.Value;

        internal static void Bind(ConfigFile home)
        {
            Enabled = home.Bind("Mod", "Enabled", true,
                "Мод включён. Выключенный мод со следующего запуска игры ничего не меняет в клиенте, остаётся только кнопка «Включить мод» в настройках игры. Выключается в окне «Настройки мода».");
            _flashWas = home.Bind("Mod", "FlashLookWasOn", false,
                "Служебное: был ли включён вид как во Flash, когда мод выключали. При включении мода вид возвращается. Заполняется само.");
        }

        internal static void AskOff()
        {
            try
            {
                DialogFactory.ShowConfirmMessageBox(Caption, result =>
                {
                    if (result == EMessageBoxResult.MB_OK) TurnOff();
                }, "Выключить мод? После перезапуска игры клиент будет обычным, без изменений мода и вида как во Flash. Включить обратно: настройки игры, кнопка «Включить мод».");
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[мод] окно выключения: " + e.Message); }
        }

        internal static void AskOn()
        {
            try
            {
                DialogFactory.ShowConfirmMessageBox(Caption, result =>
                {
                    if (result == EMessageBoxResult.MB_OK) TurnOn();
                }, "Включить мод? Он заработает после перезапуска игры.");
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[мод] окно включения: " + e.Message); }
        }

        private static void TurnOff()
        {
            if (Enabled == null) return;
            var flash = FlashLook.Entry("General", "Enabled");
            bool look = flash != null && flash.Value;
            if (_flashWas != null) _flashWas.Value = look;
            if (look) flash.Value = false;
            Enabled.Value = false;
            Plugin.Log?.LogInfo("[мод] выключен со следующего запуска игры" + (look ? ", вид как во Flash тоже выключен" : ""));
            Tell("Мод выключен. Перезапусти игру, и клиент будет обычным.");
        }

        private static void TurnOn()
        {
            if (Enabled == null) return;
            Enabled.Value = true;
            var flash = FlashLook.Entry("General", "Enabled");
            bool look = _flashWas != null && _flashWas.Value;
            if (look && flash != null) flash.Value = true;
            if (_flashWas != null) _flashWas.Value = false;
            Plugin.Log?.LogInfo("[мод] включён со следующего запуска игры" + (look && flash != null ? ", вид как во Flash возвращён" : ""));
            Tell("Мод включён. Перезапусти игру, чтобы он заработал.");
        }

        private static void Tell(string text)
        {
            try { DialogFactory.ShowMessageBox(Caption, null, result => { }, text); }
            catch (Exception e) { Plugin.Log?.LogWarning("[мод] сообщение: " + e.Message); }
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
                if (reset == null) { Plugin.Log?.LogWarning("[мод] кнопку «Сбросить положение чата» не нашёл, «Включить мод» не добавлена"); return; }

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

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = "Включить мод";

                var btn = go.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(ModSwitch.AskOn);
            }
            catch (Exception e) { Plugin.Log?.LogWarning("[мод] кнопка «Включить мод» не добавлена: " + e.Message); }
        }
    }
}
