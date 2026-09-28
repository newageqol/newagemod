using System;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class KeepTarget
    {
        private static CombatData _fight;
        private static bool _players;

        internal static void Fresh(CombatData combat)
        {
            if (combat == null || combat.RoundNum > 1) return;
            _fight = combat;
            _players = false;
        }

        internal static bool Hold(CombatData combat)
        {
            try
            {
                if (combat == null) return false;
                if (!ReferenceEquals(combat, _fight))
                {
                    _fight = combat;
                    _players = false;
                }
                var chosen = combat.SelectedCharacter;
                var me = combat.MyCharacter;
                if (chosen == null || me == null) return false;
                if (combat.Characters == null || !combat.Characters.ContainsKey(chosen.UserId))
                {
                    Plugin.Trace("[target] selected " + chosen.UserId + " left the field, the game picks the target");
                    return false;
                }
                if (!_players) _players = Rivals(combat, me);
                if (!_players) return false;
                Plugin.Trace("[target] fight against players: selection stays on " + chosen.UserId);
                return true;
            }
            catch (Exception e) { Plugin.Trace("[target] " + e.Message); }
            return false;
        }

        private static bool Rivals(CombatData combat, AbstractCharacter me)
        {
            if (combat.Characters == null) return false;
            foreach (var pair in combat.Characters)
            {
                var one = pair.Value as PlayerCharacter;
                if (one == null || one.UserId == me.UserId || one.Team == me.Team) continue;
                Plugin.Trace("[target] enemy player " + one.UserId + " is in the fight, target is manual from now on");
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatData), "SelectNearestEnemy")]
    internal static class KeepTargetPatch
    {
        private static bool Prefix(CombatData __instance) => !KeepTarget.Hold(__instance);
    }

    [HarmonyPatch(typeof(CombatData), "StartNewRound")]
    internal static class KeepTargetRoundPatch
    {
        private static void Postfix(CombatData __instance) => KeepTarget.Fresh(__instance);
    }
}
