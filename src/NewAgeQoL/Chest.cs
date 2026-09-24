using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Chest
    {
        private const float Gap = 8f;
        private const float Every = 0.25f;
        private const string HolderName = "QoLChestHolder";

        private static OpenDailyChestButton _chest;
        private static RectTransform _holder;
        private static float _placeAt;
        private static bool _shown;
        private static bool _told;

        internal static void Found(OpenDailyChestButton chest)
        {
            if (chest == null) return;
            _chest = chest;
            _holder = null;
            _placeAt = 0f;
            _shown = false;
            _told = false;
            Plugin.Trace("[сундук] игра создала сундук");
        }

        internal static void Tick()
        {
            if (_chest == null)
            {
                if (!ReferenceEquals(_chest, null)) Forget();
                return;
            }
            try
            {
                bool shown = _chest.gameObject.activeInHierarchy;
                if (shown != _shown) { _shown = shown; _placeAt = 0f; }
                if (!shown || Time.unscaledTime < _placeAt) return;
                _placeAt = Time.unscaledTime + Every;
                var holder = Holder();
                if (holder == null) return;
                if (!ChatDock.Active)
                {
                    if (holder.anchoredPosition != Vector2.zero) holder.anchoredPosition = Vector2.zero;
                    return;
                }
                Place(holder);
            }
            catch (Exception e) { Plugin.Trace("[сундук] " + e.Message); }
        }

        private static void Forget()
        {
            _chest = null;
            _holder = null;
            Plugin.Trace("[сундук] игра убрала сундук, больше не слежу");
        }

        private static RectTransform Holder()
        {
            var root = _chest.transform as RectTransform;
            if (root == null) return null;
            if (_holder != null && _holder.parent == root) return _holder;
            var found = root.Find(HolderName) as RectTransform;
            if (found != null) { _holder = found; return found; }
            if (root.childCount == 0) return null;

            var kids = new List<Transform>();
            for (int i = 0; i < root.childCount; i++) kids.Add(root.GetChild(i));

            var go = new GameObject(HolderName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            foreach (var kid in kids) kid.SetParent(rt, false);
            _holder = rt;
            Plugin.Trace("[сундук] содержимое взято в держатель: " + kids.Count + ", кнопка " + kids[0].name);
            return rt;
        }

        private static void Place(RectTransform holder)
        {
            var button = holder.childCount > 0 ? holder.GetChild(0) as RectTransform : null;
            if (button == null) return;
            var canvas = _chest.GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

            var box = button.rect;
            var local = (Vector2)button.localPosition;
            var low = RectTransformUtility.WorldToScreenPoint(cam, holder.TransformPoint(local + box.min));
            var high = RectTransformUtility.WorldToScreenPoint(cam, holder.TransformPoint(local + box.max));

            float edge = Gap * scale;
            float wide = high.x - low.x;
            float x = Mathf.Min(Screen.width - ChatDock.LeftPixels + edge, Screen.width - edge - wide);
            float dx = x - low.x;
            float dy = edge - low.y;
            if (Mathf.Abs(dx) < 1f && Mathf.Abs(dy) < 1f) return;
            holder.anchoredPosition += new Vector2(dx, dy) / scale;
            if (_told) return;
            _told = true;
            Plugin.Trace("[сундук] стоит справа от панели чата");
        }
    }

    [HarmonyPatch(typeof(OpenDailyChestButton), "Awake")]
    internal static class ChestFoundPatch
    {
        private static void Postfix(OpenDailyChestButton __instance)
        {
            try { Chest.Found(__instance); }
            catch (Exception e) { Plugin.Trace("[сундук] появление: " + e.Message); }
        }
    }
}
