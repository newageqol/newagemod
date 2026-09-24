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
                if (eye == null) { Plugin.Trace("[задания] своей камеры у окна нет, размер оставлен как есть"); return; }
                if (eye.orthographic) eye.orthographicSize /= k;
                else
                {
                    float half = Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad) / k;
                    eye.fieldOfView = Mathf.Clamp(Mathf.Atan(half) * 2f * Mathf.Rad2Deg, 1f, 170f);
                }
                Plugin.Trace("[задания] свитки уменьшены до " + k + " от игрового размера");
            }
            catch (Exception e) { Plugin.Trace("[задания] размер окна: " + e.Message); }
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
                if (gone > 0) Plugin.Trace("[задания] карточек прогресса убрано: " + gone);
            }
            catch (Exception e) { Plugin.Trace("[задания] карточка прогресса: " + e.Message); }
        }
    }
}
