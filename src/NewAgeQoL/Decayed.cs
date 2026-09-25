using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Combat;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatData), "UpdateIndicators")]
    internal static class Decayed
    {
        [HarmonyPrefix]
        private static void Sweep(CombatData __instance, ref List<CharacterIndicatorsMessage> newIndicators)
        {
            try
            {
                if (__instance == null || newIndicators == null) return;
                List<CharacterIndicatorsMessage> kept = null;
                for (int i = 0; i < newIndicators.Count; i++)
                {
                    var one = newIndicators[i];
                    if (one == null || one.Decayed != true)
                    {
                        if (kept != null) kept.Add(one);
                        continue;
                    }
                    if (kept == null) kept = new List<CharacterIndicatorsMessage>(newIndicators.GetRange(0, i));
                    Plugin.Trace("[бой] тело " + one.UserId + " истлело — убираю с поля");
                    __instance.RemoveCharacter(one.UserId);
                }
                if (kept != null) newIndicators = kept;
            }
            catch (Exception e) { Plugin.Trace("[бой] истлевшие тела: " + e.Message); }
        }
    }
}
