using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Dialogs
    {
        private const string Wrap = "QoLDialogScale";

        internal static float Scale => Plugin.CfgDialogScale == null ? 0.7f : Mathf.Clamp(Plugin.CfgDialogScale.Value, 0.4f, 1f);

        private static bool Small(Transform window)
        {
            return window != null && window is RectTransform;
        }

        internal static void Shrink(Component changer)
        {
            try
            {
                var window = changer != null ? changer.transform as RectTransform : null;
                var root = window != null ? window.parent as RectTransform : null;
                if (root == null || root.name == Wrap || !Small(window)) return;
                float k = Scale;
                if (k > 0.995f) return;

                var go = new GameObject(Wrap, typeof(RectTransform));
                var wrap = (RectTransform)go.transform;
                wrap.SetParent(root, false);
                wrap.anchorMin = Vector2.zero;
                wrap.anchorMax = Vector2.one;
                wrap.pivot = new Vector2(0.5f, 0.5f);
                wrap.offsetMin = Vector2.zero;
                wrap.offsetMax = Vector2.zero;
                wrap.SetSiblingIndex(window.GetSiblingIndex());
                window.SetParent(wrap, false);
                wrap.localScale = new Vector3(k, k, 1f);
                Plugin.Trace("[окна] " + root.name + " уменьшено до " + k);
            }
            catch (Exception e) { Plugin.Trace("[окна] уменьшение: " + e.Message); }
        }

        private const int High = 990;

        private static ConfirmActionDialog _action;
        private static bool _told;

        private static ConfirmActionDialog Search()
        {
            foreach (var one in Resources.FindObjectsOfTypeAll<ConfirmActionDialog>())
                if (one != null && one.gameObject.scene.IsValid()) return one;
            return null;
        }

        private static System.Reflection.FieldInfo _girl;

        internal static void Bare(HelperBase helper)
        {
            try
            {
                if (helper == null) return;
                if (_girl == null) _girl = AccessTools.Field(typeof(HelperBase), "WhoreWithASword");
                var girl = _girl != null ? _girl.GetValue(helper) as Image : null;
                if (girl == null) { Plugin.Trace("[окна] подсказка игры: картинки не видно"); return; }
                if (!girl.enabled) return;
                girl.enabled = false;
                Plugin.Trace("[окна] подсказка игры: картинка убрана, остался папирус");
            }
            catch (Exception e) { Plugin.Trace("[окна] подсказка игры: " + e.Message); }
        }

        internal static void Raise(ConfirmActionDialog dialog)
        {
            if (dialog != null) _action = dialog;
            if (_action == null) _action = Search();
            _told = false;
            Tick();
        }

        private static float _menuAt;
        private static readonly List<RectTransform> Seen = new List<RectTransform>();

        internal static void Wee(Component menu)
        {
            try
            {
                var rt = menu != null ? menu.transform as RectTransform : null;
                if (rt == null) return;
                bool known = false;
                for (int i = 0; i < Seen.Count; i++) if (ReferenceEquals(Seen[i], rt)) { known = true; break; }
                if (!known) Seen.Add(rt);
                float k = Scale;
                if (k > 0.995f) return;
                if (Mathf.Abs(rt.localScale.x - k) > 0.001f) rt.localScale = new Vector3(k, k, 1f);
            }
            catch (Exception e) { Plugin.Trace("[окна] меню: " + e.Message); }
        }

        private static void Menus()
        {
            if (Seen.Count == 0) return;
            if (Time.unscaledTime < _menuAt) return;
            _menuAt = Time.unscaledTime + 0.15f;
            float k = Scale;
            for (int i = Seen.Count - 1; i >= 0; i--)
            {
                var rt = Seen[i];
                if (rt == null) { Seen.RemoveAt(i); continue; }
                if (k > 0.995f) continue;
                if (Mathf.Abs(rt.localScale.x - k) < 0.001f) continue;
                rt.localScale = new Vector3(k, k, 1f);
            }
        }

        internal static void Tick()
        {
            try
            {
                Menus();
                var found = _action;
                if (found == null) return;
                var rt = found.transform as RectTransform;
                if (rt == null || !found.gameObject.activeInHierarchy) return;

                var lift = found.gameObject.GetComponent<Canvas>();
                if (lift == null)
                {
                    lift = found.gameObject.AddComponent<Canvas>();
                    found.gameObject.AddComponent<GraphicRaycaster>();
                }
                if (!lift.overrideSorting || lift.sortingOrder != High)
                {
                    lift.overrideSorting = true;
                    lift.sortingOrder = High;
                }

                float k = Scale;
                if (Mathf.Abs(rt.localScale.x - k) > 0.001f) rt.localScale = new Vector3(k, k, 1f);

                var middle = new Vector2(0.5f, 0.5f);
                if (rt.anchorMin != middle || rt.anchorMax != middle) { rt.anchorMin = rt.anchorMax = middle; }
                if (rt.pivot != middle) rt.pivot = middle;

                var canvas = rt.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var now = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
                var want = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                var gap = want - now;
                var host = rt.parent as RectTransform;
                float unit = host != null ? host.lossyScale.x : 1f;
                if (unit < 0.0001f) unit = 1f;
                if (gap.sqrMagnitude > 0.25f) rt.anchoredPosition += gap / unit;
                if (_told) return;
                _told = true;
                Plugin.Trace("[окна] окно действия по центру, размер " + k + ", слой " + High);
            }
            catch (Exception e) { Plugin.Trace("[окна] окно действия: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SummonPlayerDialog), "FillContent")]
    internal static class DialogsSummonPatch
    {
        private static void Postfix(SummonPlayerDialog __instance)
        {
            try
            {
                if (__instance == null) return;
                var scroll = __instance.GetComponentInChildren<ScrollRect>(true);
                if (scroll == null) { Plugin.Trace("[окна] призыв: списка нет"); return; }
                var view = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
                if (view != null && view.GetComponent<RectMask2D>() == null && view.GetComponent<Mask>() == null)
                {
                    view.gameObject.AddComponent<RectMask2D>();
                    Plugin.Trace("[окна] призыв: список подрезан по окну");
                }
                if (scroll.viewport == null) scroll.viewport = view;
                scroll.vertical = true;
                scroll.horizontal = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                if (scroll.scrollSensitivity < 20f) scroll.scrollSensitivity = 30f;
                var content = scroll.content;
                if (content != null)
                {
                    var fit = content.GetComponent<ContentSizeFitter>();
                    if (fit == null) fit = content.gameObject.AddComponent<ContentSizeFitter>();
                    fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    content.anchorMin = new Vector2(content.anchorMin.x, 1f);
                    content.anchorMax = new Vector2(content.anchorMax.x, 1f);
                    content.pivot = new Vector2(content.pivot.x, 1f);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                    scroll.verticalNormalizedPosition = 1f;
                    Plugin.Trace("[окна] призыв: строк " + content.childCount + ", высота " + content.rect.height.ToString("0"));
                }
            }
            catch (Exception e) { Plugin.Trace("[окна] призыв: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ContextMenu), "Awake")]
    internal static class DialogsMenuBornPatch
    {
        private static void Postfix(ContextMenu __instance) => Dialogs.Wee(__instance);
    }

    [HarmonyPatch(typeof(HelperBig), "InitializeDialog")]
    internal static class DialogsHelperBigPatch
    {
        private static void Postfix(HelperBig __instance) => Dialogs.Bare(__instance);
    }

    [HarmonyPatch(typeof(HelperSmall), "InitializeDialog")]
    internal static class DialogsHelperSmallPatch
    {
        private static void Postfix(HelperSmall __instance) => Dialogs.Bare(__instance);
    }

    [HarmonyPatch(typeof(ConfirmActionDialogController), "OpenViewSetup")]
    internal static class DialogsActionPatch
    {
        private static void Postfix() => Dialogs.Raise(null);
    }

    [HarmonyPatch(typeof(ConfirmActionDialog), "OpenDialog")]
    internal static class DialogsActionOpenPatch
    {
        private static void Postfix(ConfirmActionDialog __instance) => Dialogs.Raise(__instance);
    }

    [HarmonyPatch(typeof(ConfirmActionDialog), "OpenAttackDialog")]
    internal static class DialogsAttackOpenPatch
    {
        private static void Postfix(ConfirmActionDialog __instance) => Dialogs.Raise(__instance);
    }

    [HarmonyPatch(typeof(CanvasGroupVisibilityChanger), "Awake")]
    internal static class DialogsShrinkPatch
    {
        private static void Postfix(CanvasGroupVisibilityChanger __instance) => Dialogs.Shrink(__instance);
    }
}
