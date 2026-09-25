using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class ChestWindow
    {
        private const float Most = 0.7f;
        private const float Least = 0.4f;
        private const float Lowest = 0.0675f;
        private const float Margin = 0.02f;

        internal static void Shrink(DailyBonusWindow window)
        {
            try
            {
                if (window == null) return;
                float k = Scale();
                if (k > 0.995f) return;

                var eye = window.transform.Find("chests/Chests Camera");
                var cam = eye != null ? eye.GetComponent<Camera>() : window.GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    if (cam.orthographic) cam.orthographicSize /= k;
                    else
                    {
                        float half = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / k;
                        cam.fieldOfView = Mathf.Clamp(Mathf.Atan(half) * 2f * Mathf.Rad2Deg, 1f, 170f);
                    }
                }
                else Plugin.Trace("[сундук] у окна нет камеры, сундуки оставлены как есть");

                var ui = window.transform.Find("DailyBonusUICanvas");
                int moved = 0;
                if (ui != null)
                {
                    for (int i = 0; i < ui.childCount; i++)
                    {
                        var rt = ui.GetChild(i) as RectTransform;
                        if (rt == null) continue;
                        Pull(rt, k);
                        moved++;
                    }
                }
                Plugin.Trace("[сундук] окно уменьшено до " + k.ToString("0.00") + ", частей интерфейса " + moved);
            }
            catch (Exception e) { Plugin.Trace("[сундук] размер окна: " + e.Message); }
        }

        private static float Scale()
        {
            float dock = 0f;
            if (ChatDock.Active && Screen.height > 0) dock = ChatDock.PanelPixels / Screen.height;
            float fit = (0.5f - dock - Margin) / (0.5f - Lowest);
            return Mathf.Clamp(Mathf.Min(Most, fit), Least, Most);
        }

        private static void Pull(RectTransform rt, float k)
        {
            var point = Vector2.Scale(rt.anchorMax - rt.anchorMin, rt.pivot) + rt.anchorMin;
            var shift = (new Vector2(0.5f, 0.5f) - point) * (1f - k);
            rt.anchorMin += shift;
            rt.anchorMax += shift;
            rt.anchoredPosition *= k;
            rt.localScale = Vector3.Scale(rt.localScale, new Vector3(k, k, 1f));
        }
    }

    [HarmonyPatch(typeof(DailyBonusWindow), "Awake")]
    internal static class ChestWindowPatch
    {
        private static void Postfix(DailyBonusWindow __instance) => ChestWindow.Shrink(__instance);
    }
}
