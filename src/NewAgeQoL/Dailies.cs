using System;
using HarmonyLib;
using UnityEngine;
using Transport.Messages.Responses.Gui.Dailytasks;

namespace NewAgeQoL
{
    internal static class Dailies
    {
        internal static bool Quiet => Plugin.CfgDailyToast == null || !Plugin.CfgDailyToast.Value;

        internal const float Scale = 0.7f;

        internal static void Shrink(Component window)
        {
            try
            {
                if (window == null) return;
                float k = Scale;
                if (k > 0.995f) return;
                var eye = window.GetComponentInChildren<Camera>(true);
                if (eye == null) { Plugin.Trace("[quests] window has no own camera, size left as is"); return; }
                if (eye.orthographic) eye.orthographicSize /= k;
                else
                {
                    float half = Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad) / k;
                    eye.fieldOfView = Mathf.Clamp(Mathf.Atan(half) * 2f * Mathf.Rad2Deg, 1f, 170f);
                }
                Plugin.Trace("[quests] scrolls shrunk to " + k + " of game size");
            }
            catch (Exception e) { Plugin.Trace("[quests] window size: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(DailyTasksWindow), "Awake")]
    internal static class DailyScalePatch
    {
        private static void Postfix(DailyTasksWindow __instance) => Dailies.Shrink(__instance);
    }

    [HarmonyPatch(typeof(DailyTasksWindowController), "OnDailyTasksProgressResponse")]
    internal static class DailyToastPatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                if (!Dailies.Quiet) return;
                var got = msg as DailyTasksProgressResponseMessage;
                if (got == null || got.ProgressTasks == null || got.ProgressTasks.Count == 0) return;
                int was = got.ProgressTasks.Count;
                got.ProgressTasks.RemoveAll(one => one == null || one.TaskId <= 0 || one.Counter != one.MaxCounter);
                int gone = was - got.ProgressTasks.Count;
                if (gone > 0) Plugin.Trace("[quests] progress cards removed: " + gone);
            }
            catch (Exception e) { Plugin.Trace("[quests] progress card: " + e.Message); }
        }
    }
}
