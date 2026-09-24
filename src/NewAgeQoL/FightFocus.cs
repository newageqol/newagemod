using System;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FightFocus
    {
        internal static void Free(CombatData combat)
        {
            try
            {
                if (combat == null || combat.RoundNum > 1) return;
                var system = EventSystem.current;
                var picked = system != null ? system.currentSelectedGameObject : null;
                if (picked == null) return;
                var line = picked.GetComponent<InputField>();
                if (line != null) line.DeactivateInputField();
                var rich = picked.GetComponent<TMPro.TMP_InputField>();
                if (rich != null) rich.DeactivateInputField();
                system.SetSelectedGameObject(null);
                Plugin.Trace("[бой] начало боя: строка чата отпущена, клавиши работают сразу");
            }
            catch (Exception e) { Plugin.Trace("[бой] строка чата: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatData), "StartNewRound")]
    internal static class FightFocusRoundPatch
    {
        private static void Postfix(CombatData __instance) => FightFocus.Free(__instance);
    }
}
