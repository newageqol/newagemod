using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ScoutEnter
    {
        private static ScoutingDialog _box;
        private static int _bornFrame = -1;
        private static int _enterFrame = -1;

        internal static bool Asking => _enterFrame == Time.frameCount || Open;

        private static bool Open => _box != null && _box.isActiveAndEnabled;

        internal static void Born(ScoutingDialogParams args, ScoutingDialog box)
        {
            if (box == null || args == null) return;
            if (args.ShowType != ScoutingDialogParams.ShowDialogType.Attack || args.opponent != null)
            {
                _box = null;
                return;
            }
            _box = box;
            _bornFrame = Time.frameCount;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            Plugin.Trace("[scout] scouting window, Enter will attack");
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
                var ok = _box.GetAttackButton();
                if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
                _enterFrame = Time.frameCount;
                _box = null;
                Plugin.Trace("[scout] attack confirmed by Enter");
                ok.onClick.Invoke();
            }
            catch (Exception e) { Plugin.Trace("[scout] Enter: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(DialogFactory), "ShowScoutingDialog")]
    internal static class ScoutEnterPatch
    {
        private static void Postfix(ScoutingDialogParams dialogParams, ScoutingDialog __result)
        {
            try { ScoutEnter.Born(dialogParams, __result); }
            catch (Exception e) { Plugin.Trace("[scout] window: " + e.Message); }
        }
    }
}
