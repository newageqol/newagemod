using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class UiScale
    {
        private const float BaseWide = 1920f;
        private const float BaseHigh = 1080f;
        private const float LeastWide = 1440f;

        private static readonly List<CanvasScaler> Owned = new List<CanvasScaler>();
        private static int _w;
        private static int _h;
        private static float _k = -1f;
        private static float _at;
        private static float _said = -1f;

        internal static float Boost => Plugin.CfgSmallScreen == null ? 1.2f : Mathf.Clamp(Plugin.CfgSmallScreen.Value, 1f, 1.5f);

        internal static void Own(CanvasScaler scaler)
        {
            if (scaler == null) return;
            try
            {
                if (!Owned.Contains(scaler)) Owned.Add(scaler);
                Fit(scaler);
            }
            catch (Exception e) { Plugin.Trace("[масштаб] холст: " + e.Message); }
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _at) return;
            _at = Time.unscaledTime + 0.5f;
            try
            {
                float k = Boost;
                if (Screen.width == _w && Screen.height == _h && Mathf.Approximately(k, _k)) return;
                _w = Screen.width;
                _h = Screen.height;
                _k = k;
                Owned.RemoveAll(one => one == null);
                foreach (var one in Owned) Fit(one);

                float natural = Natural(1f);
                float target = Target(natural);
                if (natural > 0f && !Mathf.Approximately(target, _said))
                {
                    _said = target;
                    Plugin.Trace("[масштаб] экран " + _w + "×" + _h + ": интерфейс мода " + Mathf.RoundToInt(target / natural * 100f) + "% от игрового");
                }
            }
            catch (Exception e) { Plugin.Trace("[масштаб] " + e.Message); }
        }

        private static void Fit(CanvasScaler scaler)
        {
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight) return;
            float natural = Natural(scaler.matchWidthOrHeight);
            if (natural <= 0f) return;
            float share = natural / Target(natural);
            var want = new Vector2(BaseWide * share, BaseHigh * share);
            if ((scaler.referenceResolution - want).sqrMagnitude > 0.25f) scaler.referenceResolution = want;
        }

        private static float Natural(float match)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return 0f;
            return Mathf.Pow(Screen.width / BaseWide, 1f - match) * Mathf.Pow(Screen.height / BaseHigh, match);
        }

        private static float Target(float natural)
        {
            if (natural >= 1f) return natural;
            float want = Mathf.Min(1f, natural * Boost, Screen.width / LeastWide);
            return Mathf.Max(natural, want);
        }
    }
}
