using System;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Clones
    {
        internal static void StripHotkeys(GameObject clone, GameObject source)
        {
            try
            {
                if (clone == null) return;
                var handlers = clone.GetComponentsInChildren<HotkeyHandler>(true);
                if (handlers == null || handlers.Length == 0) return;
                foreach (var handler in handlers)
                {
                    if (handler == null) continue;
                    var action = handler.GetAction();
                    UnityEngine.Object.DestroyImmediate(handler);
                    var dispatcher = HotkeyDispatcher.Instance;
                    if (dispatcher == null) continue;
                    var original = Live(action);
                    if (original == null) original = InSource(source, action);
                    if (original != null) dispatcher.RegisterHandler(action, original);
                    Plugin.Trace("[clone] action key hook removed " + action + (original != null ? ", returned to original button" : ""));
                }
            }
            catch (Exception e) { Plugin.Trace("[clone] hotkeys: " + e.Message); }
        }

        private static HotkeyHandler InSource(GameObject source, EHotkeyActions action)
        {
            if (source == null) return null;
            foreach (var candidate in source.GetComponentsInChildren<HotkeyHandler>(true))
                if (candidate != null && candidate.GetAction() == action) return candidate;
            return null;
        }

        private static HotkeyHandler Live(EHotkeyActions action)
        {
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<HotkeyHandler>())
                if (candidate != null && candidate.GetAction() == action) return candidate;
            return null;
        }
    }
}
