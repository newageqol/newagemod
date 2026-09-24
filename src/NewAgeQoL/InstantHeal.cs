using HarmonyLib;

namespace NewAgeQoL
{
    internal static class InstantHeal
    {
        private static AccessTools.FieldRef<ChangeLifeAnimationItem, float?> _startTime;
        private static bool _looked;

        private static bool Mark(ChangeLifeAnimationItem item)
        {
            if (!_looked)
            {
                _looked = true;
                try { _startTime = AccessTools.FieldRefAccess<ChangeLifeAnimationItem, float?>("_startTime"); }
                catch (System.Exception e) { Plugin.Fault("[combat] поле _startTime не найдено: " + e.Message); }
            }
            if (_startTime == null) return false;
            _startTime(item) = 0f;
            return true;
        }

        internal static void Apply(ChangeLifeAnimationItem item)
        {
            try
            {
                if (item == null || item.life <= 0) return;

                var target = item.Target;
                if (target == null || target != Me()) return;

                var indicators = target.Indicators;
                if (indicators == null) return;

                string animation = item.AnimationName;
                var group = item.Group;
                if (animation == "change_life" && (group == null || group.actionType != ActionType.THINGEFFECT))
                {
                    Plugin.Trace("[combat] жизнь +" + item.life + " не от расходника (действие "
                                 + (group == null ? "?" : group.actionType.ToString()) + ") — идёт по анимации, как в игре");
                    return;
                }
                Plugin.Trace("[combat] " + animation + " +" + item.life + ", действие "
                             + (group == null ? "?" : group.actionType.ToString()));
                int was, max;
                switch (animation)
                {
                    case "change_life": was = indicators.CurrentLife; max = indicators.MaxLife; break;
                    case "change_mana": was = indicators.CurrentMana; max = indicators.MaxMana; break;
                    case "change_expower": was = indicators.CurrentExpower; max = indicators.MaxExpower; break;
                    default: was = indicators.CurrentStamina; max = indicators.MaxStamina; break;
                }

                if (max <= 0 || was >= max) return;
                int now = was + item.life;
                if (now > max) now = max;

                if (!Mark(item)) return;

                switch (animation)
                {
                    case "change_life": indicators.CurrentLife = now; break;
                    case "change_mana": indicators.CurrentMana = now; break;
                    case "change_expower": indicators.CurrentExpower = now; break;
                    default: indicators.CurrentStamina = now; break;
                }

                Plugin.Trace("[combat] " + animation + " +" + item.life + " сразу, без анимации: было "
                                    + was + ", стало " + now + ", предел " + max
                                    + ", раунд " + Round() + ", очередь анимаций " + Queue());
            }
            catch (System.Exception e) { Plugin.Fault("[combat] " + e.Message); }
        }

        private static AbstractCharacter Me()
        {
            try { return Cd()?.MyCharacter; }
            catch { return null; }
        }

        private static ICombatData Cd()
        {
            try { return Controllers.User?.CombatData; }
            catch { return null; }
        }

        private static string Round()
        {
            try
            {
                var cd = Cd();
                return cd == null ? "?" : cd.RoundNum + "/" + cd.RoundType;
            }
            catch { return "?"; }
        }

        private static string Queue()
        {
            try
            {
                var ap = Cd()?.AnimationProcessor;
                return ap == null ? "?" : ap.GroupCount + (ap.Active ? " (идёт)" : " (стоит)");
            }
            catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(ChangeLifeAnimationItem), MethodType.Constructor, new[]
    {
        typeof(AnimationGroup), typeof(AbstractCharacter), typeof(AbstractCharacter),
        typeof(string), typeof(int), typeof(AnimationItemType),
    })]
    public static class InstantHealPatch
    {
        private static void Postfix(ChangeLifeAnimationItem __instance) => InstantHeal.Apply(__instance);
    }
}
