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
        private static readonly System.Reflection.FieldInfo PositionField = AccessTools.Field(typeof(GlobalMapController), "_currentPosition");

        internal static bool Asking => _enterFrame == Time.frameCount || Open;

        private static bool Open => _box != null && _box.isActiveAndEnabled;

        internal static void Born(ConfirmMessageBox box)
        {
            if (box == null) return;
            _box = box;
            _bornFrame = Time.frameCount;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            var veil = box.GetComponent<CanvasGroup>() ?? box.gameObject.AddComponent<CanvasGroup>();
            veil.alpha = 0f;
            int location = Location();
            Plugin.Trace("[gate] gate window" + (location > 0 ? " of location " + location : "") + ", confirming by itself");
        }

        private static int Location()
        {
            try
            {
                var map = Controllers.Get<GlobalMapController>();
                var all = map != null ? map.Vertices : null;
                if (all == null || PositionField == null) return 0;
                int at = (int)PositionField.GetValue(map);
                if (at < 0 || at >= all.Length || all[at] == null) return 0;
                return all[at].LocationId;
            }
            catch (Exception e) { Plugin.Caught("[gate] location: " + e.Message); return 0; }
        }

        internal static void Tick()
        {
            if (_box == null) return;
            try
            {
                if (!Open || Time.frameCount <= _bornFrame) return;
                var ok = AccessTools.Field(typeof(ConfirmMessageBox), "MbOkButton")?.GetValue(_box) as Button;
                if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
                _enterFrame = Time.frameCount;
                _box = null;
                Plugin.Trace("[gate] passed without asking");
                ok.onClick.Invoke();
            }
            catch (Exception e) { Plugin.Caught("[gate] confirm: " + e.Message); }
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
            catch (Exception e) { Plugin.Caught("[gate] window: " + e.Message); }
        }
    }
}
