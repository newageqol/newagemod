using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch]
    internal static class BundleWatch
    {
        private sealed class Gone
        {
            public DateTime At;
            public string Name;
            public bool All;
            public string Caller;
        }

        private static readonly List<Gone> Recent = new List<Gone>();
        private static int _main;
        private static int _busy;

        internal static void Hook()
        {
            _main = Thread.CurrentThread.ManagedThreadId;
            Application.logMessageReceivedThreaded -= Heard;
            Application.logMessageReceivedThreaded += Heard;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.Unload))]
        private static void Before(AssetBundle __instance, bool unloadAllLoadedObjects)
        {
            try
            {
                var gone = new Gone
                {
                    At = DateTime.UtcNow,
                    Name = __instance != null ? __instance.name : "",
                    All = unloadAllLoadedObjects,
                    Caller = string.Join(" < ", new StackTrace(2, false).GetFrames()
                        .Select(f => f.GetMethod())
                        .Where(m => m != null && m.DeclaringType != null)
                        .Take(4)
                        .Select(m => m.DeclaringType.Name + "." + m.Name))
                };
                lock (Recent)
                {
                    Recent.Add(gone);
                    if (Recent.Count > 4) Recent.RemoveAt(0);
                }
            }
            catch { }
        }

        private static void Heard(string text, string stack, LogType type)
        {
            if (text == null || !text.StartsWith("AssetBundle '") || !text.Contains("already unloaded")) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var now = DateTime.UtcNow;
                string last;
                lock (Recent)
                    last = Recent.Count == 0 ? "none yet" : string.Join("; ", Recent.Select(g =>
                        "'" + g.Name + "' " + (g.All ? "with objects" : "bundle only") + ", " + (int)(now - g.At).TotalMilliseconds + " ms ago, from " + g.Caller));
                int thread = Thread.CurrentThread.ManagedThreadId;
                Plugin.Trace("[bundles] already unloaded bundle reported on " + (thread == _main ? "main thread" : "thread " + thread)
                    + (string.IsNullOrEmpty(stack) ? "" : ", stack " + stack.Replace('\n', '|'))
                    + "; last unloads: " + last);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }
    }
}
