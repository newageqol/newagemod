using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace NewAgeQoL
{
    internal static class NightLoad
    {
        private const float Guess = 0.5f;

        private static readonly Stopwatch Clock = new Stopwatch();
        private static bool _expect;
        private static bool _hold;
        private static bool _driving;
        private static bool _releasing;
        private static bool _nightDone;
        private static float _share = Guess;
        private static float _game;
        private static float _night;
        private static long _gameMs = -1;
        private static long _nightMs = -1;

        internal static bool Holding => _hold && !_releasing;
        internal static bool Driving => _driving;

        internal static void Expect()
        {
            _expect = true;
            _nightDone = false;
            _game = 0f;
            _night = 0f;
            _share = Share();
            Clock.Restart();
        }

        internal static void Forget() => _expect = false;

        internal static float Scale(float progress)
        {
            if (!_expect) return progress;
            _game = Math.Max(_game, progress);
            return Mixed();
        }

        internal static void Night(float progress)
        {
            _night = Math.Max(_night, progress);
            if (_night >= 0.95f && !_nightDone)
            {
                _nightDone = true;
                _nightMs = Clock.ElapsedMilliseconds;
            }
            if (_expect || _hold) Push();
        }

        internal static void GameDone(bool running)
        {
            bool expected = _expect;
            _expect = false;
            if (!running) return;
            if (expected) _gameMs = Clock.ElapsedMilliseconds;
            _game = 1f;
            _hold = true;
            Push();
        }

        internal static void Release(bool ok)
        {
            if (!_hold) return;
            _hold = false;
            Plugin.Trace("[night] loading screen released" + (ok ? "" : " without the night town") + ": game " + _gameMs + " ms, night town " + _nightMs + " ms, whole " + Clock.ElapsedMilliseconds + " ms");
            _releasing = true;
            try { Preloader.Close(); }
            catch (Exception e) { Plugin.Trace("[night] loading screen close: " + e.Message); }
            finally { _releasing = false; }
        }

        private static float Mixed() => _share * _game + (1f - _share) * _night;

        private static void Push()
        {
            _driving = true;
            try { Preloader.UpdateMainIndicator(Mixed()); }
            catch (Exception e) { Plugin.Trace("[night] loading bar: " + e.Message); }
            finally { _driving = false; }
        }

        private static float Share()
        {
            if (_gameMs <= 0 || _nightMs <= 0) return Guess;
            float share = _gameMs / (float)(_gameMs + _nightMs);
            return Math.Max(0.2f, Math.Min(0.8f, share));
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "SceneLoaded")]
    internal static class NightLoadStartPatch
    {
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(SceneLoader), "_currentLocationMap");

        private static void Prefix(SceneLoader __instance)
        {
            try
            {
                if (!(MapField?.GetValue(__instance) is LocationMap map)) return;
                if (map.MapType == 0 && map.MapId == 2 && NightTown.WillRun())
                {
                    NightLoad.Expect();
                    NightTown.Prepare();
                }
                else NightLoad.Forget();
            }
            catch (Exception e) { Plugin.Trace("[night] loading start: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Preloader), "UpdateMainIndicator")]
    internal static class NightLoadBarPatch
    {
        private static void Prefix(ref float progress)
        {
            if (!NightLoad.Driving) progress = NightLoad.Scale(progress);
        }
    }

    [HarmonyPatch(typeof(Preloader), "Close")]
    internal static class NightLoadClosePatch
    {
        private static bool Prefix() => !NightLoad.Holding;
    }
}
