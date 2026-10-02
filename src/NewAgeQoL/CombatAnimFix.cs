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
        private const float Gap = 1f;

        private sealed class Watch
        {
            public float Since;
            public float Last;
            public bool Told;
        }

        private static readonly Dictionary<int, Watch> Waiting = new Dictionary<int, Watch>();

        private static void Postfix(AbstractCharacter character, ref bool __result)
        {
            try
            {
                if (character == null) return;
                int id = character.UserId;
                if (__result) { Waiting.Remove(id); return; }
                float now = Time.unscaledTime;
                Watch watch;
                if (!Waiting.TryGetValue(id, out watch) || now - watch.Last > Gap)
                {
                    if (watch == null)
                    {
                        if (Waiting.Count > 200) Waiting.Clear();
                        watch = new Watch();
                        Waiting[id] = watch;
                    }
                    watch.Since = now;
                    watch.Last = now;
                    watch.Told = false;
                    return;
                }
                watch.Last = now;
                if (now - watch.Since < Limit) return;
                __result = true;
                if (!watch.Told)
                {
                    watch.Told = true;
                    string state = "-";
                    try
                    {
                        var an = character.CharacterAnimator;
                        if (an != null) state = an.GetCurrentAnimatorStateInfo(0).shortNameHash + (an.IsInTransition(0) ? " (transition)" : "");
                    }
                    catch { }
                    Plugin.Warn("[combat] " + character.GetType().Name + " id " + character.UserId + " '" + character.Login
                                            + "' has not returned to idle for over " + Limit + " s (state " + state + ") - not waiting any longer");
                }
            }
            catch { }
        }
    }
}
