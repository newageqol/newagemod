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
        private static bool _auto;
        private static readonly System.Reflection.FieldInfo PositionField = AccessTools.Field(typeof(GlobalMapController), "_currentPosition");

        internal static bool Asking => _enterFrame == Time.frameCount || Open;

        private static bool Open => _box != null && _box.isActiveAndEnabled;

        internal static void Born(ConfirmMessageBox box)
        {
            if (box == null) return;
            _box = box;
            _bornFrame = Time.frameCount;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            _auto = City(out int location);
            if (_auto) { var veil = box.GetComponent<CanvasGroup>() ?? box.gameObject.AddComponent<CanvasGroup>(); veil.alpha = 0f; }
            Plugin.Trace(_auto ? "[gate] gate window of town " + location + ", confirming by itself" : "[gate] gate window" + (location > 0 ? " of location " + location : "") + ", Enter will confirm");
        }

        private static bool City(out int location)
        {
            location = 0;
            try
            {
                var map = Controllers.Get<GlobalMapController>();
                var all = map != null ? map.Vertices : null;
                if (all == null || PositionField == null) return false;
                int at = (int)PositionField.GetValue(map);
                if (at < 0 || at >= all.Length || all[at] == null) return false;
                var spot = all[at];
                location = spot.LocationId;
                return spot.VertexType == EGlobalMapVertexType.SavePoint && spot.IsRoot && spot.LocationId > 0 && spot.LocationId < 1000;
            }
            catch (Exception e) { Plugin.Trace("[gate] town check: " + e.Message); return false; }
        }

        internal static void Tick()
        {
            if (_box == null) return;
            try
            {
                if (!Open || Time.frameCount <= _bornFrame) return;
                if (!_auto)
                {
                    if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
                    var picked = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                    if (picked != null && picked.GetComponent<InputField>() != null) return;
                }
                var ok = AccessTools.Field(typeof(ConfirmMessageBox), "MbOkButton")?.GetValue(_box) as Button;
                if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
                _enterFrame = Time.frameCount;
                _box = null;
                Plugin.Trace(_auto ? "[gate] town entered without asking" : "[gate] confirmed by Enter");
                _auto = false;
                ok.onClick.Invoke();
            }
            catch (Exception e) { Plugin.Trace("[gate] Enter: " + e.Message); }
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
            catch (Exception e) { Plugin.Trace("[gate] window: " + e.Message); }
        }
    }
}
