using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatData), nameof(CombatData.HangEffect))]
    internal static class StunTwice
    {
        private static readonly AccessTools.FieldRef<CombatData, Dictionary<int, int>> Hung =
            AccessTools.FieldRefAccess<CombatData, Dictionary<int, int>>("_characterStateEffects");

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CombatData __instance, int userId, int effectMask)
        {
            if (effectMask == 0) return;
            try
            {
                var hung = Hung(__instance);
                if (hung != null && hung.TryGetValue(userId, out int was) && hung.Remove(userId))
                    Plugin.Trace("[stun] fighter " + userId + " got effect mask " + effectMask + " over " + was + ", old one taken off first");
            }
            catch (Exception e) { Plugin.Trace("[stun] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(WaitCombatController), "AddParticipant")]
    internal static class LobbySeats
    {
        private static readonly AccessTools.FieldRef<WaitCombatController, List<int>> Free =
            AccessTools.FieldRefAccess<WaitCombatController, List<int>>("_freePositions");
        private static readonly MethodInfo Seats = AccessTools.PropertyGetter(typeof(WaitCombatController), "Positions");
        private static FieldInfo _team;

        private static bool Prefix(WaitCombatController __instance, int userId, string login, int team)
        {
            try
            {
                var free = Free(__instance);
                var seats = Seats?.Invoke(__instance, null) as Array;
                if (free == null || seats == null) return true;
                for (int k = 0; k < free.Count; k++)
                {
                    int i = free[k];
                    if (i < 0 || i >= seats.Length) continue;
                    var seat = seats.GetValue(i);
                    if (seat == null) continue;
                    if (_team == null) _team = AccessTools.Field(seat.GetType(), "Team");
                    if (_team == null) return true;
                    if (Convert.ToInt32(_team.GetValue(seat)) != team) continue;
                    if (k > 0)
                    {
                        free.RemoveAt(k);
                        free.Insert(0, i);
                    }
                    return true;
                }
                Plugin.Trace("[lobby] no free place for '" + login + "' (" + userId + "), team " + team + ": free " + free.Count + " of " + seats.Length + ", not shown");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[lobby] " + e.Message);
                return true;
            }
        }
    }
}
