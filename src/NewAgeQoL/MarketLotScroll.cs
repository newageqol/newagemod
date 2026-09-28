using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(Grid<MarketProposalListItemDTO>), "OnScrollChanged")]
    internal static class MarketLotScrollPatch
    {
        private static bool Prefix(MonoBehaviour __instance, float value) => !(__instance is MarketProposalListGrid grid && MarketLotScroll.Place(grid, value));
    }

    internal static class MarketLotScroll
    {
        private static readonly Type G = typeof(Grid<MarketProposalListItemDTO>);
        private static readonly FieldInfo FPos = AccessTools.Field(G, "_scrollPos");
        private static readonly FieldInfo FMax = AccessTools.Field(G, "_maxScrollPosition");
        private static readonly FieldInfo FTotal = AccessTools.Field(G, "_totalRowCount");
        private static readonly FieldInfo FItems = AccessTools.Field(G, "_currentScrollItems");
        private static readonly FieldInfo FRenderers = AccessTools.Field(G, "_itemRenderers");
        private static readonly FieldInfo FPanel = AccessTools.Field(G, "_dataPanelTransform");
        private static readonly FieldInfo FLayout = AccessTools.Field(G, "GridLayout");
        private static readonly FieldInfo FUp = AccessTools.Field(G, "UpScrollButton");
        private static readonly FieldInfo FDown = AccessTools.Field(G, "DownScrollButton");
        private static readonly MethodInfo MData = AccessTools.PropertySetter(G, "DataScrollPosition");
        private static readonly Dictionary<int, float> Rest = new Dictionary<int, float>();
        private static bool _told;

        internal static bool Place(MarketProposalListGrid grid, float value)
        {
            try
            {
                if (grid == null || MData == null) return false;
                var panel = FPanel?.GetValue(grid) as Transform;
                var layout = FLayout?.GetValue(grid) as GridLayoutGroup;
                var renderers = FRenderers?.GetValue(grid) as IList;
                if (panel == null || layout == null || renderers == null || renderers.Count == 0) return false;

                int id = grid.GetInstanceID();
                if (!Rest.TryGetValue(id, out float rest))
                {
                    rest = panel.localPosition.y;
                    Rest[id] = rest;
                }

                float pos = Mathf.Clamp01(value);
                FPos.SetValue(grid, pos);
                float max = Mathf.Max(0f, (float)FMax.GetValue(grid));
                var up = FUp?.GetValue(grid) as Button;
                var down = FDown?.GetValue(grid) as Button;
                if (up != null && up.targetGraphic != null) up.targetGraphic.enabled = pos > 0.001f;
                if (down != null && down.targetGraphic != null) down.targetGraphic.enabled = pos < 0.999f && max > 0f;

                float cell = grid.ItemSize.y + layout.spacing.y;
                if (cell < 1f) return false;
                int columns = Mathf.Max(1, grid.Columns);
                int shown = renderers.Count / columns;
                int total = (int)FTotal.GetValue(grid);
                float num = pos * max;
                int first = Mathf.Clamp((int)(num / cell), 0, Mathf.Max(0, total - shown));
                FItems.SetValue(grid, first);
                MData.Invoke(grid, new object[] { first });

                var parent = panel.parent;
                float k = parent != null ? grid.transform.lossyScale.y / Mathf.Max(parent.lossyScale.y, 0.0001f) : 1f;
                var p = panel.localPosition;
                p.y = rest + (num - first * cell) * k;
                panel.localPosition = p;

                if (!_told && pos >= 0.999f)
                {
                    _told = true;
                    Plugin.Trace("[market] lot list scrolled to the end: rows " + total + ", shown " + shown + ", first " + first + ", offset " + (num - first * cell).ToString("0"));
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Trace("[market] lot scroll: " + e.Message);
                return false;
            }
        }
    }
}
