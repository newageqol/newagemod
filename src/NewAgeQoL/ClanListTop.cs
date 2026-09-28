using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(ClanListView), "InitializeDialog")]
    internal static class ClanListTopPatch
    {
        private static void Postfix(ClanListView __instance)
        {
            try
            {
                if (__instance == null) return;
                var scroll = __instance.GetComponentInChildren<ScrollRect>(true);
                if (scroll == null) { Plugin.Trace("[clan] list: no scroll view"); return; }
                var pin = scroll.GetComponent<ClanListTop>() ?? scroll.gameObject.AddComponent<ClanListTop>();
                pin.Scroll = scroll;
                pin.Window = __instance.transform as RectTransform;
                float was = scroll.scrollSensitivity;
                if (scroll.scrollSensitivity < 60f) scroll.scrollSensitivity = 60f;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                Plugin.Trace("[clan] list wheel step " + was.ToString("0.#") + " -> " + scroll.scrollSensitivity.ToString("0.#"));
                Plugin.Trace("[clan] list held at the top until scrolled by hand");
            }
            catch (Exception e) { Plugin.Trace("[clan] list top: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ClanMembersDialog), "InitializeDialog")]
    internal static class ClanMembersScrollPatch
    {
        private static void Postfix(ClanMembersDialog __instance)
        {
            if (__instance == null) return;
            ClanMembersScroll.Fix(__instance);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Later(__instance));
        }

        private static System.Collections.IEnumerator Later(ClanMembersDialog dlg)
        {
            yield return null;
            yield return null;
            if (dlg != null) ClanMembersScroll.Fix(dlg);
        }
    }

    internal static class ClanMembersScroll
    {
        internal static void Fix(ClanMembersDialog dlg)
        {
            try
            {
                var grid = Traverse.Create(dlg).Field("Grid").GetValue<ClanMembersGrid>();
                ScrollRect scroll = grid != null ? Traverse.Create(grid).Field("ParentScrollRect").GetValue<ScrollRect>() : null;
                if (scroll == null) scroll = dlg.GetComponentInChildren<ScrollRect>(true);
                if (scroll == null) { Plugin.Trace("[clan] members: no scroll view"); return; }
                var content = scroll.content != null ? scroll.content : grid != null ? grid.transform as RectTransform : null;
                var view = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
                var bar = scroll.verticalScrollbar;
                var barRt = bar != null ? bar.transform as RectTransform : null;
                if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                    Plugin.Trace("[clan] members before: h " + scroll.horizontal + " v " + scroll.vertical + " move " + scroll.movementType + " wheel " + scroll.scrollSensitivity.ToString("0.#")
                        + ", view " + (view != null ? view.name + " " + view.rect.width.ToString("0") + "x" + view.rect.height.ToString("0") : "-")
                        + ", content " + (content != null ? content.name + " " + content.rect.width.ToString("0") + "x" + content.rect.height.ToString("0") + " at " + content.anchoredPosition.ToString("0") + " rows " + content.childCount : "-")
                        + ", bar " + (barRt != null ? barRt.parent.name + " anchors " + barRt.anchorMin.x + ".." + barRt.anchorMax.x + " x " + barRt.anchoredPosition.x.ToString("0") : "-"));

                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                if (scroll.scrollSensitivity < 30f) scroll.scrollSensitivity = 30f;
                if (scroll.content == null && content != null) scroll.content = content;
                if (scroll.viewport == null && view != null) scroll.viewport = view;
                if (view != null && view.GetComponent<RectMask2D>() == null && view.GetComponent<Mask>() == null)
                    view.gameObject.AddComponent<RectMask2D>();

                if (content != null)
                {
                    float width = content.rect.width;
                    var pos = content.anchoredPosition;
                    content.anchorMin = new Vector2(content.anchorMin.x, 1f);
                    content.anchorMax = new Vector2(content.anchorMax.x, 1f);
                    content.pivot = new Vector2(content.pivot.x, 1f);
                    content.anchoredPosition = new Vector2(pos.x, 0f);
                    if (Mathf.Abs(content.anchorMax.x - content.anchorMin.x) < 0.01f && width > 10f)
                        content.sizeDelta = new Vector2(width, content.sizeDelta.y);
                    if (content.GetComponent<LayoutGroup>() != null)
                    {
                        var fit = content.GetComponent<ContentSizeFitter>() ?? content.gameObject.AddComponent<ContentSizeFitter>();
                        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    }
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                }

                string barHow = "none";
                if (barRt != null && content != null)
                {
                    var row = barRt.parent != null ? barRt.parent.GetComponent<HorizontalLayoutGroup>() : null;
                    if (row != null)
                    {
                        if (barRt.GetSiblingIndex() != barRt.parent.childCount - 1) barRt.SetAsLastSibling();
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)barRt.parent);
                        barHow = "last in row";
                    }
                    else if (barRt.parent != null && content.parent == barRt.parent)
                    {
                        var host = (RectTransform)barRt.parent;
                        var corners = new Vector3[4];
                        content.GetWorldCorners(corners);
                        float right = host.InverseTransformPoint(corners[2]).x - host.rect.xMin;
                        barRt.anchorMin = new Vector2(0f, barRt.anchorMin.y);
                        barRt.anchorMax = new Vector2(0f, barRt.anchorMax.y);
                        barRt.anchoredPosition = new Vector2(right + 4f + barRt.rect.width * barRt.pivot.x, barRt.anchoredPosition.y);
                        barHow = "placed at " + barRt.anchoredPosition.x.ToString("0");
                    }
                    else barHow = "left as is, parent " + (barRt.parent != null ? barRt.parent.name : "-");
                    var group = barRt.parent != null ? barRt.parent.GetComponent<LayoutGroup>() : null;
                    barHow += ", parent layout " + (group != null ? group.GetType().Name : "none") + ", index " + barRt.GetSiblingIndex() + "/" + (barRt.parent != null ? barRt.parent.childCount : 0);
                }
                scroll.verticalNormalizedPosition = 1f;
                Plugin.Trace("[clan] members list: vertical only, " + (content != null ? content.childCount + " rows, " + content.rect.width.ToString("0") + "x" + content.rect.height.ToString("0") + " anchors " + content.anchorMin.x + ".." + content.anchorMax.x : "no rows")
                    + ", scroll " + ((RectTransform)scroll.transform).rect.width.ToString("0") + " wide, view " + (view != null ? view.rect.width.ToString("0") + " anchors " + view.anchorMin.x + ".." + view.anchorMax.x + " offsets " + view.offsetMin.x.ToString("0") + ".." + view.offsetMax.x.ToString("0") : "-")
                    + ", bar " + (barRt != null ? barRt.parent.name + " x " + barRt.anchoredPosition.x.ToString("0") + " w " + barRt.rect.width.ToString("0") : "none") + " (" + barHow + ")");
            }
            catch (Exception e) { Plugin.Trace("[clan] members scroll: " + e.Message); }
        }
    }

    internal sealed class ClanListTop : MonoBehaviour
    {
        internal ScrollRect Scroll;
        internal RectTransform Window;

        private bool _touched;
        private bool _said;

        private void LateUpdate()
        {
            if (_touched || Scroll == null || !Scroll.vertical) return;
            if (Touched())
            {
                _touched = true;
                Plugin.Trace("[clan] list touched by hand, no longer held at the top");
                return;
            }
            if (Scroll.verticalNormalizedPosition >= 0.999f) return;
            if (!_said)
            {
                _said = true;
                Plugin.Trace("[clan] list drifted to " + Scroll.verticalNormalizedPosition.ToString("0.000") + ", returned to the top");
            }
            Scroll.StopMovement();
            Scroll.verticalNormalizedPosition = 1f;
        }

        private bool Touched()
        {
            if (Input.mouseScrollDelta.y == 0f && !Input.GetMouseButtonDown(0)) return false;
            var area = Window != null ? Window : Scroll.transform as RectTransform;
            if (area == null) return true;
            var canvas = area.GetComponentInParent<Canvas>();
            var eye = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(area, Input.mousePosition, eye);
        }
    }
}
