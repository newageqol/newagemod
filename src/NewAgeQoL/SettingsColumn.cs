using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SetupDialog), "Start")]
    internal static class SettingsColumnPatch
    {
        private const string SpacerName = "QoLColumnSpacer";
        private static readonly Vector3[] Corners = new Vector3[4];

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SetupDialog __instance)
        {
            try { __instance.StartCoroutine(Lower(__instance)); }
            catch (Exception e) { Plugin.Caught("[settings] column: " + e.Message); }
        }

        private static IEnumerator Lower(SetupDialog dialog)
        {
            for (int step = 0; step < 3; step++) yield return null;
            try { Push(dialog); }
            catch (Exception e) { Plugin.Caught("[settings] column: " + e.Message); }
        }

        private static void Push(SetupDialog dialog)
        {
            var hotkeys = AccessTools.Field(typeof(SetupDialog), "HotkeysGroup")?.GetValue(dialog) as GameObject;
            var language = AccessTools.Field(typeof(SetupDialog), "LanguageDropdown")?.GetValue(dialog) as Component;
            if (language == null) return;
            var column = Common(hotkeys != null && hotkeys.activeInHierarchy ? hotkeys.transform : null, language.transform) as RectTransform;
            if (column == null) return;
            if (column.Find(SpacerName) != null) return;

            int first = int.MaxValue;
            foreach (var part in new[] { hotkeys != null && hotkeys.activeInHierarchy ? hotkeys.transform : null, language.transform })
            {
                var top = Under(part, column);
                if (top != null) first = Math.Min(first, top.GetSiblingIndex());
            }
            if (first == int.MaxValue) return;

            Canvas.ForceUpdateCanvases();
            RectTransform above = null;
            for (int i = first - 1; i >= 0; i--)
            {
                var child = column.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf) { above = child; break; }
            }
            if (above == null) return;
            float floor = Edge(above, true);
            float roof = float.MinValue;
            for (int i = first; i < column.childCount; i++)
            {
                var child = column.GetChild(i) as RectTransform;
                if (child != null && child.gameObject.activeSelf) roof = Mathf.Max(roof, Edge(child, false));
            }
            if (floor == float.MaxValue || roof == float.MinValue) return;
            var stack = column.GetComponent<VerticalLayoutGroup>();
            float scale = Mathf.Max(0.0001f, column.lossyScale.y);
            float gap = stack != null ? Mathf.Max(stack.spacing, 10f) : 10f;
            float lift = (floor - roof) / scale - gap;
            Plugin.Trace("[settings] column " + column.name + " (" + (stack != null ? "vertical layout" : "free layout") + "): " + Names(column) + "; lift under " + above.name + " " + lift.ToString("0.#"));
            if (lift < 4f) return;

            if (stack != null) stack.enabled = false;
            for (int i = first; i < column.childCount; i++)
            {
                var child = column.GetChild(i) as RectTransform;
                if (child != null) child.anchoredPosition += new Vector2(0f, lift);
            }
            new GameObject(SpacerName, typeof(RectTransform)).transform.SetParent(column, false);
            Plugin.Trace("[settings] hotkeys, report and language raised by " + lift.ToString("0.#"));
        }

        private static float Edge(RectTransform part, bool low)
        {
            float best = low ? float.MaxValue : float.MinValue;
            foreach (var g in part.GetComponentsInChildren<Graphic>(false))
            {
                if (g.transform == part || !g.enabled || !g.gameObject.activeInHierarchy || g.color.a <= 0.01f) continue;
                var text = g as Text;
                if (text != null && string.IsNullOrEmpty(text.text)) continue;
                g.rectTransform.GetWorldCorners(Corners);
                best = low ? Mathf.Min(best, Corners[0].y) : Mathf.Max(best, Corners[1].y);
            }
            return best;
        }

        private static Transform Common(Transform a, Transform b)
        {
            if (a == null) return b != null ? b.parent : null;
            var seen = new HashSet<Transform>();
            for (var t = a; t != null; t = t.parent) seen.Add(t);
            for (var t = b; t != null; t = t.parent) if (seen.Contains(t) && t != a && t != b) return t;
            return null;
        }

        private static Transform Under(Transform part, Transform column)
        {
            for (var t = part; t != null; t = t.parent)
                if (t.parent == column) return t;
            return null;
        }

        private static string Names(Transform column)
        {
            var parts = new List<string>();
            for (int i = 0; i < column.childCount; i++)
            {
                var c = column.GetChild(i);
                parts.Add(c.name + (c.gameObject.activeSelf ? "" : " (hidden)"));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
