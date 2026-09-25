using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class TravelEnter
    {
        internal const string Question = "messages.confirms.globalmap.exit";

        private static ConfirmMessageBox _box;
        private static int _bornFrame = -1;
        private static int _enterFrame = -1;

        internal static bool Asking => _enterFrame == Time.frameCount || Open;

        private static bool Open => _box != null && _box.isActiveAndEnabled;

        internal static void Born(ConfirmMessageBox box)
        {
            if (box == null) return;
            _box = box;
            _bornFrame = Time.frameCount;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Plugin.Trace("[переход] окно перехода, Enter подтвердит");
        }

        internal static void Tick()
        {
            if (_box == null) return;
            try
            {
                if (!Open || Time.frameCount <= _bornFrame) return;
                if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
                var picked = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (picked != null && picked.GetComponent<InputField>() != null) return;
                var ok = AccessTools.Field(typeof(ConfirmMessageBox), "MbOkButton")?.GetValue(_box) as Button;
                if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
                _enterFrame = Time.frameCount;
                _box = null;
                Plugin.Trace("[переход] подтверждён по Enter");
                ok.onClick.Invoke();
            }
            catch (Exception e) { Plugin.Trace("[переход] Enter: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(DialogFactory), "ShowConfirmMessageBox", new[]
    {
        typeof(string), typeof(Sprite), typeof(Action<EMessageBoxResult>), typeof(string), typeof(object[])
    })]
    internal static class TravelEnterPatch
    {
        private static void Postfix(string message, ConfirmMessageBox __result)
        {
            try { if (message == TravelEnter.Question) TravelEnter.Born(__result); }
            catch (Exception e) { Plugin.Trace("[переход] окно: " + e.Message); }
        }
    }
}
