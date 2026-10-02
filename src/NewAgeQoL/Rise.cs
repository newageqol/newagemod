using System;
using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatController), "OnMyIndicatorsChanged")]
    internal static class Rise
    {
        private static AbstractCharacter _me;
        private static bool _wasDead, _risen;
        private static int _lastAction;

        private static void Postfix()
        {
            try
            {
                var cd = FighterHint.Cd();
                var me = cd != null ? cd.MyCharacter : null;
                if (me == null) return;
                bool dead = me.Dead;
                if (!ReferenceEquals(me, _me))
                {
                    _me = me;
                    _wasDead = dead;
                    _risen = false;
                    _lastAction = 0;
                    return;
                }
                bool rose = _wasDead && !dead;
                _wasDead = dead;
                if (!rose) return;
                _risen = true;
                Plugin.Trace("[rise] own fighter came back to life in round " + cd.RoundNum + " (" + cd.RoundType + "), action points " + cd.TimeUnitsManager.ActionTimeUnits);
                Unlock(cd);
            }
            catch (Exception e) { Plugin.Trace("[rise] " + e.Message); }
        }

        internal static void NewRound(ICombatData cd)
        {
            try
            {
                if (cd == null || cd.MyCharacter == null || !ReferenceEquals(cd.MyCharacter, _me)) return;
                var tu = cd.TimeUnitsManager;
                if (cd.RoundType == RoundType.COMBAT_ROUND && tu.ActionTimeUnits > 0 && !cd.MyCharacter.Dead)
                {
                    _lastAction = tu.ActionTimeUnits;
                    _risen = false;
                    return;
                }
                Unlock(cd);
            }
            catch (Exception e) { Plugin.Trace("[rise] new round: " + e.Message); }
        }

        private static void Unlock(ICombatData cd)
        {
            if (!_risen || cd.MyCharacter == null || cd.MyCharacter.Dead || cd.RoundType != RoundType.COMBAT_ROUND) return;
            var tu = cd.TimeUnitsManager;
            if (tu.ActionTimeUnits > 0) { _risen = false; return; }
            int action = _lastAction > 0 ? _lastAction : tu.MaxTimeUnits;
            if (action <= 0) return;
            _risen = false;
            tu.RefreshTimeUnits(tu.MovementTimeUnits, action, tu.MaxTimeUnits);
            cd.MakeCombatSelection();
            Plugin.Trace("[rise] alive with no action points in round " + cd.RoundNum + ": attack unlocked with " + action + " points" + (_lastAction > 0 ? " (as in the last combat phase)" : " (maximum)"));
        }
    }

    [HarmonyPatch(typeof(CombatData), "StartNewRound")]
    internal static class RiseRoundPatch
    {
        private static void Postfix(CombatData __instance) => Rise.NewRound(__instance);
    }
}
