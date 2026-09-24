using System;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class RealTime
    {
        private static readonly DateTime Start = DateTime.UtcNow;
        private static float _markReal = -1f;
        private static float _markGame;
        private static int _told = 10;

        internal static float Now => (float)(DateTime.UtcNow - Start).TotalSeconds;

        internal static void Tick()
        {
            float real = Now;
            if (_markReal < 0f) { _markReal = real; _markGame = Time.unscaledTime; return; }
            float span = real - _markReal;
            if (span < 30f) return;
            float rate = (Time.unscaledTime - _markGame) / span;
            _markReal = real;
            _markGame = Time.unscaledTime;
            int tenth = Mathf.RoundToInt(rate * 10f);
            if (Mathf.Abs(tenth - 10) <= 2) tenth = 10;
            if (tenth == _told) return;
            _told = tenth;
            Plugin.Trace(tenth == 10
                ? "[часы] время игры снова идёт с обычной скоростью"
                : "[часы] время игры идёт ×" + (tenth / 10f).ToString("0.0") + " от настоящего, ожидания сервера мод считает по настоящим часам");
        }
    }
}
