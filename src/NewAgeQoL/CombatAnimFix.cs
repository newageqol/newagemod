using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(AnimationItem), "IsIdle")]
    public static class StuckAnimationPatch
    {
        private const float Limit = 2.5f;
        private static readonly Dictionary<AbstractCharacter, float> Since = new Dictionary<AbstractCharacter, float>();
        private static readonly HashSet<AbstractCharacter> Reported = new HashSet<AbstractCharacter>();

        private static void Postfix(AbstractCharacter character, ref bool __result)
        {
            try
            {
                if (character == null) return;
                if (__result) { Since.Remove(character); return; }
                float now = Time.unscaledTime;
                float since;
                if (!Since.TryGetValue(character, out since))
                {
                    if (Since.Count > 200) Since.Clear();
                    Since[character] = now;
                    return;
                }
                if (now - since < Limit) return;
                __result = true;
                if (Reported.Add(character))
                {
                    if (Reported.Count > 200) Reported.Clear();
                    string state = "-";
                    try
                    {
                        var an = character.CharacterAnimator;
                        if (an != null) state = an.GetCurrentAnimatorStateInfo(0).shortNameHash + (an.IsInTransition(0) ? " (переход)" : "");
                    }
                    catch { }
                    Plugin.Warn("[бой] " + character.GetType().Name + " id " + character.UserId + " «" + character.Login
                                            + "» не возвращается в idle дольше " + Limit + " с (состояние " + state + ") — дальше не ждём");
                }
            }
            catch { }
        }
    }
}
