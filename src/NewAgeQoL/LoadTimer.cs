using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class LoadTimer
    {
        private const long FileLimit = 1000000;
        private const long StuckMs = 120000;

        private static readonly Stopwatch Clock = new Stopwatch();
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, long[]> Sums = new Dictionary<string, long[]>();

        private static bool _on;
        private static bool _stuck;
        private static bool _headed;
        private static int _count;
        private static int _type = -1;
        private static int _map = -1;
        private static int _from = -1;
        private static long _scene = -1;
        private static long _assets = -1;
        private static long _after = -1;
        private static int _frames;
        private static float _worst;
        private static int _gc;
        private static long _heap;
        private static bool _night;
        private static bool _town;

        internal static string File => Path.Combine(DiskJournal.Folder, "loads.log");
        private static string Old => Path.Combine(DiskJournal.Folder, "loads.old.log");

        internal static void Map(LocationMap map)
        {
            if (map == null) return;
            if (_on && _type >= 0)
            {
                Write("[load] #" + _count + " " + Where() + ": no loading screen close before the next map, " + Clock.ElapsedMilliseconds + " ms dropped");
                _on = false;
            }
            if (!_on) Begin();
            _type = map.MapType;
            _map = map.MapId;
        }

        internal static void Shown()
        {
            if (!_on) Begin();
        }

        internal static void SceneReady()
        {
            if (_on && _scene < 0) _scene = Clock.ElapsedMilliseconds;
        }

        internal static void AssetsReady()
        {
            if (_on && _assets < 0) _assets = Clock.ElapsedMilliseconds;
        }

        internal static void AssetsApplied()
        {
            if (_on && _after < 0) _after = Clock.ElapsedMilliseconds;
        }

        internal static void Tick()
        {
            if (!_on) return;
            _frames++;
            float dt = Time.unscaledDeltaTime;
            if (dt > _worst) _worst = dt;
            if (!_stuck && Clock.ElapsedMilliseconds > StuckMs)
            {
                _stuck = true;
                Write("[load] #" + _count + " " + Where() + ": loading screen still up after " + Clock.ElapsedMilliseconds / 1000 + " s" + Stages());
            }
        }

        internal static void Closed()
        {
            if (!_on) return;
            _on = false;
            long ms = Clock.ElapsedMilliseconds;
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                var sb = new StringBuilder();
                sb.Append("[load] #").Append(_count).Append(' ').Append(Where());
                if (_from >= 0) sb.Append(" (from location ").Append(_from).Append(')');
                sb.Append(", scene ").Append(scene).Append(": ").Append(ms).Append(" ms");
                sb.Append(Stages());
                sb.Append("; frames ").Append(_frames);
                if (_frames > 0) sb.Append(", avg ").Append(Num(ms / (float)_frames)).Append(" ms, worst ").Append(Mathf.RoundToInt(_worst * 1000f)).Append(" ms");
                sb.Append(", gc ").Append(GC.CollectionCount(0) - _gc);
                long heap = GC.GetTotalMemory(false);
                sb.Append(", mono heap ").Append(Mb(heap)).Append(" MB (").Append(heap >= _heap ? "+" : "-").Append(Mb(Math.Abs(heap - _heap))).Append(')');
                long unity = Profiler.GetTotalAllocatedMemoryLong();
                if (unity > 0) sb.Append(", unity ").Append(Mb(unity)).Append(" MB");
                sb.Append("; night theme ").Append(_night ? "on" : "off");
                sb.Append(", night town in memory ").Append(_town ? "yes" : "no");
                string key = Where() + ", night theme " + (_night ? "on" : "off");
                long[] sum;
                if (!Sums.TryGetValue(key, out sum)) { sum = new long[3]; Sums[key] = sum; }
                sum[0]++;
                sum[1] += ms;
                sum[2] = Math.Max(sum[2], ms);
                sb.Append("; this session ").Append(key).Append(": ").Append(sum[0]).Append(" loads, avg ").Append(sum[1] / sum[0]).Append(" ms, longest ").Append(sum[2]).Append(" ms");
                Write(sb.ToString());
            }
            catch (Exception e) { Plugin.Trace("[load] report: " + e.Message); }
        }

        private static void Begin()
        {
            Head();
            _on = true;
            _stuck = false;
            _count++;
            _type = -1;
            _map = -1;
            _scene = _assets = _after = -1;
            _frames = 0;
            _worst = 0f;
            _gc = GC.CollectionCount(0);
            _heap = GC.GetTotalMemory(false);
            _night = NightTheme.On;
            _town = NightTown.Held;
            try { var ud = Controllers.User; _from = ud != null ? ud.CurrentLocationId : -1; }
            catch { _from = -1; }
            Clock.Restart();
        }

        private static string Where()
        {
            if (_type < 0) return "without a new map";
            string kind = _type == 0 ? "location" : _type == 1 ? "combat" : _type == 2 ? "world map" : "map type " + _type;
            return kind + " " + _map;
        }

        private static string Stages()
        {
            var sb = new StringBuilder(" (");
            sb.Append("scene ").Append(_scene >= 0 ? _scene + " ms" : "-");
            sb.Append(", assets ").Append(_assets >= 0 ? _assets + " ms" : "-");
            sb.Append(", location set up ").Append(_after >= 0 ? _after + " ms" : "-");
            if (_after >= 0 && !_stuck) sb.Append(", screen after that ").Append(Clock.ElapsedMilliseconds - _after).Append(" ms");
            return sb.Append(')').ToString();
        }

        private static void Head()
        {
            if (_headed) return;
            _headed = true;
            try
            {
                Write("[load] session: RAM " + SystemInfo.systemMemorySize + " MB, VRAM " + SystemInfo.graphicsMemorySize + " MB, GPU " + SystemInfo.graphicsDeviceName
                    + ", CPU " + SystemInfo.processorType + " x" + SystemInfo.processorCount + ", screen " + Screen.width + "x" + Screen.height
                    + ", fps cap " + Application.targetFrameRate + ", mod " + Plugin.Version);
            }
            catch (Exception e) { Plugin.Trace("[load] session: " + e.Message); }
        }

        private static void Write(string line)
        {
            Plugin.Log?.LogInfo(line);
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(DiskJournal.Folder);
                    var info = new FileInfo(File);
                    if (info.Exists && info.Length > FileLimit)
                    {
                        if (System.IO.File.Exists(Old)) System.IO.File.Delete(Old);
                        System.IO.File.Move(File, Old);
                    }
                    System.IO.File.AppendAllText(File, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine, Encoding.UTF8);
                }
                catch { }
            }
        }

        private static string Mb(long bytes) => (bytes / (1024L * 1024L)).ToString(CultureInfo.InvariantCulture);

        private static string Num(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    }

    [HarmonyPatch(typeof(SceneLoader), "LoadMap")]
    internal static class LoadTimerMapPatch
    {
        private static void Prefix(LocationMap map)
        {
            try { LoadTimer.Map(map); }
            catch (Exception e) { Plugin.Trace("[load] map: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "SceneLoaded")]
    internal static class LoadTimerScenePatch
    {
        private static void Prefix() => LoadTimer.SceneReady();
    }

    [HarmonyPatch(typeof(SceneLoader), "OnAssetsLoaded")]
    internal static class LoadTimerAssetsPatch
    {
        private static void Prefix() => LoadTimer.AssetsReady();

        private static void Postfix() => LoadTimer.AssetsApplied();
    }

    [HarmonyPatch(typeof(Preloader), "UpdateMainIndicator")]
    internal static class LoadTimerShowPatch
    {
        private static readonly FieldInfo Current = AccessTools.Field(typeof(Preloader), "_preloader");

        private static void Prefix()
        {
            if (Current?.GetValue(null) == null) LoadTimer.Shown();
        }
    }

    [HarmonyPatch(typeof(Preloader), "Close")]
    internal static class LoadTimerClosePatch
    {
        private static readonly FieldInfo Current = AccessTools.Field(typeof(Preloader), "_preloader");

        private static void Postfix()
        {
            if (Current?.GetValue(null) == null) LoadTimer.Closed();
        }
    }
}
