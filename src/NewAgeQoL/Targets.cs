using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Targets
    {
        internal static void Next()
        {
            try
            {
                if (!SideButtons.InCombat()) return;
                var cd = FighterHint.Cd();
                var mine = cd != null ? cd.MyCharacter : null;
                if (cd == null || mine == null || cd.Characters == null) return;

                var foes = Foes(cd, mine);
                if (foes.Count == 0) { Plugin.Trace("[цели] врагов на поле нет"); return; }

                int at = -1;
                var now = cd.SelectedCharacter;
                if (now != null)
                    for (int i = 0; i < foes.Count; i++)
                        if (foes[i].UserId == now.UserId) { at = i; break; }

                var pick = foes[at < 0 ? 0 : (at + 1) % foes.Count];
                if (pick == now) return;
                cd.SelectedCharacter = pick;
                FighterHint.Ask(pick.UserId);
                Plugin.Trace("[цели] " + (pick.Login ?? "?") + " id " + pick.UserId + ", до него " + Far(mine, pick) + ", всего врагов " + foes.Count);
            }
            catch (Exception e) { Plugin.Warn("[цели] " + e); }
        }

        private static List<AbstractCharacter> Foes(ICombatData cd, AbstractCharacter mine)
        {
            var list = new List<AbstractCharacter>();
            foreach (var pair in cd.Characters)
            {
                var ch = pair.Value;
                if (ch == null || ch.UserId == 0) continue;
                if (ch.Team == mine.Team || ch.IsFitment() || Gone(ch)) continue;
                list.Add(ch);
            }
            list.Sort((a, b) =>
            {
                int byLife = (a.Dead ? 1 : 0).CompareTo(b.Dead ? 1 : 0);
                if (byLife != 0) return byLife;
                int byKind = (a.IsBot ? 1 : 0).CompareTo(b.IsBot ? 1 : 0);
                if (byKind != 0) return byKind;
                int byFar = Far(mine, a).CompareTo(Far(mine, b));
                return byFar != 0 ? byFar : a.UserId.CompareTo(b.UserId);
            });
            return list;
        }

        private static bool Gone(AbstractCharacter ch)
        {
            try
            {
                if (!ch.Initialized) return true;
                var live = ch.Indicators;
                return live != null && live.IsDecayed;
            }
            catch { return false; }
        }

        private static int Far(AbstractCharacter from, AbstractCharacter to)
        {
            try { return HexUtils.range(from.HexGridPosition, to.HexGridPosition); }
            catch { return int.MaxValue; }
        }
    }
}
