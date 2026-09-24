using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(LifeManaEnergyDisplay), "ShowEffectText")]
    internal static class CaptionQueue
    {
        private const float Fallback = 1.5f;
        private const float Longest = 3f;

        private sealed class Line
        {
            internal readonly Queue<string> Texts = new Queue<string>();
            internal float FreeAt;
            internal bool Draining;
            internal int Generation;
        }

        private static readonly ConditionalWeakTable<LifeManaEnergyDisplay, Line> Lines = new ConditionalWeakTable<LifeManaEnergyDisplay, Line>();
        private static readonly Dictionary<RuntimeAnimatorController, float> Lengths = new Dictionary<RuntimeAnimatorController, float>();
        private static readonly FieldInfo AnimatorField = AccessTools.Field(typeof(LifeManaEnergyDisplay), "TextAnimator");
        private static bool _passing;

        private static bool Prefix(LifeManaEnergyDisplay __instance, string text)
        {
            if (_passing || __instance == null) return true;
            try
            {
                var line = Lines.GetValue(__instance, _ => new Line());
                float now = Time.unscaledTime;
                if (line.Draining && now > line.FreeAt + Longest)
                {
                    line.Texts.Clear();
                    line.Draining = false;
                    line.Generation++;
                }
                if (!line.Draining && now >= line.FreeAt)
                {
                    line.FreeAt = now + Length(__instance);
                    return true;
                }
                if (!__instance.isActiveAndEnabled) return true;
                line.Texts.Enqueue(text);
                if (!line.Draining)
                {
                    line.Draining = true;
                    __instance.StartCoroutine(Drain(__instance, line, line.Generation));
                }
                Plugin.Trace("[надписи] «" + text + "» ждёт, пока доиграет предыдущая надпись");
                return false;
            }
            catch (System.Exception e)
            {
                Plugin.Trace("[надписи] " + e.Message);
                return true;
            }
        }

        private static IEnumerator Drain(LifeManaEnergyDisplay display, Line line, int generation)
        {
            try
            {
                while (line.Generation == generation && line.Texts.Count > 0)
                {
                    while (Time.unscaledTime < line.FreeAt) yield return null;
                    if (display == null || line.Generation != generation) yield break;
                    if (line.Texts.Count == 0) break;
                    string text = line.Texts.Dequeue();
                    line.FreeAt = Time.unscaledTime + Length(display);
                    _passing = true;
                    try { display.ShowEffectText(text); }
                    finally { _passing = false; }
                }
            }
            finally
            {
                if (line.Generation == generation) line.Draining = false;
            }
        }

        private static float Length(LifeManaEnergyDisplay display)
        {
            var animator = AnimatorField != null ? AnimatorField.GetValue(display) as Animator : null;
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller == null) return Fallback;
            float length;
            if (Lengths.TryGetValue(controller, out length)) return length;
            length = 0f;
            foreach (var clip in controller.animationClips)
                if (clip != null && clip.length > length) length = clip.length;
            length = length > 0.2f ? Mathf.Min(length, Longest) : Fallback;
            Lengths[controller] = length;
            return length;
        }
    }
}
