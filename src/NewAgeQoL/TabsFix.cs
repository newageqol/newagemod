using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(TabPanel), "SetTabs")]
    public static class TabPanelRebuildPatch
    {
        private static AccessTools.FieldRef<TabPanel, IList<TabButton>> _buttons;
        private static AccessTools.FieldRef<TabPanel, int> _scroll;
        private static MethodInfo _unhook;
        private static bool _looked;

        private static void Prefix(TabPanel __instance)
        {
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    _buttons = AccessTools.FieldRefAccess<TabPanel, IList<TabButton>>("_tabButtons");
                    _scroll = AccessTools.FieldRefAccess<TabPanel, int>("_scroll");
                    _unhook = AccessTools.Method(typeof(TabPanel), "RemoveEventHandlers");
                }
                if (_buttons == null) return;

                var old = _buttons(__instance);
                if (old == null || old.Count == 0) return;

                _unhook?.Invoke(__instance, null);
                foreach (var button in old)
                    if (button != null) Object.Destroy(button.gameObject);
                foreach (var arrow in __instance.GetComponentsInChildren<TabScrollButton>(true))
                    if (arrow != null) Object.Destroy(arrow.gameObject);
                old.Clear();
                if (_scroll != null) _scroll(__instance) = 0;
            }
            catch (System.Exception e) { Plugin.Fault("[tabs] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(TabPanel), "SetTabData")]
    public static class TabPanelFitPatch
    {
        private const float Edge = 14f;

        private sealed class Row
        {
            internal RectTransform Rt;
            internal UnityEngine.UI.HorizontalOrVerticalLayoutGroup Group;
            internal bool Looked;
            internal string Key;
            internal int Seen = -1;
            internal int Kids = -1;
            internal bool Told;
            internal readonly List<Transform> Faces = new List<Transform>();
        }

        private static readonly List<Row> Rows = new List<Row>();
        private static readonly Dictionary<string, float> Kept = new Dictionary<string, float>();
        private static FieldInfo _face;
        private static int _wide, _high;

        private static void Postfix(TabPanel __instance)
        {
            try
            {
                var rt = __instance != null ? __instance.transform as RectTransform : null;
                if (rt == null) return;
                foreach (var one in Rows) if (ReferenceEquals(one.Rt, rt)) return;
                Rows.Add(new Row { Rt = rt, Told = true });
            }
            catch (System.Exception e) { Plugin.Trace("[tabs] подгонка: " + e.Message); }
        }

        internal static void Tick()
        {
            Resized();
            for (int i = Rows.Count - 1; i >= 0; i--)
            {
                var row = Rows[i];
                if (row.Rt == null)
                {
                    Rows.RemoveAt(i);
                    continue;
                }
                if (!row.Rt.gameObject.activeInHierarchy) continue;
                try { Fit(row); }
                catch (System.Exception e) { Plugin.Trace("[tabs] ряд: " + e.Message); }
            }
        }

        private static void Resized()
        {
            if (Screen.width == _wide && Screen.height == _high) return;
            _wide = Screen.width;
            _high = Screen.height;
            if (Kept.Count > 0) Kept.Clear();
        }

        private static void Fit(Row row)
        {
            if (!row.Looked)
            {
                row.Looked = true;
                row.Group = row.Rt.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
            }
            var group = row.Group;
            if (group == null) return;

            var rt = row.Rt;
            float have = rt.rect.width;
            if (have < 40f) return;

            int kids = rt.childCount;
            int seen = 0;
            float wide = 0f;
            for (int i = 0; i < kids; i++)
            {
                var kid = rt.GetChild(i) as RectTransform;
                if (kid == null || !kid.gameObject.activeSelf) continue;
                wide += kid.rect.width;
                seen++;
            }
            if (seen < 2 || wide < 1f) return;

            float need = wide + group.spacing * (seen - 1) + group.padding.left + group.padding.right;
            float fit = need > have ? (have - Edge) / need : 1f;
            if (fit < 0.5f) fit = 0.5f;
            if (fit > 1f) fit = 1f;

            if (row.Key == null || row.Seen != seen)
            {
                row.Seen = seen;
                row.Key = rt.name + ":" + seen;
            }
            float kept;
            if (Kept.TryGetValue(row.Key, out kept) && kept < fit) fit = kept;
            Kept[row.Key] = fit;

            if (rt.pivot.x > 0.001f) rt.pivot = new Vector2(0f, rt.pivot.y);

            var now = rt.localScale;
            if (Mathf.Abs(now.x - fit) > 0.004f || Mathf.Abs(now.y - 1f) > 0.004f)
                rt.localScale = new Vector3(fit, 1f, now.z);

            Faces(row, kids, fit);
            Say(row, seen, wide, have, need, fit, 0f);
        }

        private static void Faces(Row row, int kids, float fit)
        {
            if (_face == null) _face = AccessTools.Field(typeof(TabButton), "IconImage");
            if (_face == null) return;
            if (row.Kids != kids || !Whole(row)) Collect(row, kids);
            for (int i = 0; i < row.Faces.Count; i++)
            {
                var spot = row.Faces[i];
                if (spot == null) continue;
                var was = spot.localScale;
                if (Mathf.Abs(was.y - fit) > 0.004f) spot.localScale = new Vector3(was.x, fit, was.z);
            }
        }

        private static bool Whole(Row row)
        {
            for (int i = 0; i < row.Faces.Count; i++) if (row.Faces[i] == null) return false;
            return true;
        }

        private static void Collect(Row row, int kids)
        {
            row.Kids = kids;
            row.Faces.Clear();
            var rt = row.Rt;
            for (int i = 0; i < kids; i++)
            {
                var kid = rt.GetChild(i) as RectTransform;
                if (kid == null) continue;
                var tab = kid.GetComponent<TabButton>();
                if (tab == null) continue;
                var icon = _face.GetValue(tab) as UnityEngine.UI.Image;
                if (icon == null) continue;
                row.Faces.Add(icon.transform);
            }
        }

        private static void Say(Row row, int seen, float wide, float have, float need, float fit, float slide)
        {
            if (!row.Told) return;
            row.Told = false;
            if (Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            Plugin.Trace("[tabs] ряд " + row.Rt.name + ": вкладок " + seen + ", их ширина " + Mathf.RoundToInt(wide)
                + ", нужно " + Mathf.RoundToInt(need) + ", место " + Mathf.RoundToInt(have)
                + ", сжатие " + fit.ToString("0.00") + ", сдвиг " + Mathf.RoundToInt(slide)
                + ", точка привязки " + row.Rt.pivot.x.ToString("0.00"));
        }
    }
}
