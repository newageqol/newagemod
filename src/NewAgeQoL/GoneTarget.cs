using System;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class GoneTarget
    {
        internal static bool Gone(IQuickButton button)
        {
            if (button == null) return false;
            if (button.QuickButtonType == EQuickButtonType.DODGE && button.Id >= 99998 && button.Id <= 99999) return false;
            var cd = FighterHint.Cd();
            if (cd == null || cd.MyCharacter == null || cd.Characters == null) return false;
            int target = TargetTypeExtension.GetTarget(button, cd);
            if (target == 0 || target == cd.MyCharacter.UserId) return false;
            if (cd.Characters.ContainsKey(target)) return false;
            Plugin.Trace("[target] " + button.Id + " not sent: target " + target + " is no longer on the field");
            Notice.Show(Lang.T("Цели уже нет на поле боя"), 3f);
            return true;
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnActionConfirmed")]
    internal static class GoneTargetPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(IQuickButton button)
        {
            try { return !GoneTarget.Gone(button); }
            catch (Exception e) { Plugin.Caught("[target] gone check: " + e.Message); return true; }
        }
    }
}
