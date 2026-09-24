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
                    Plugin.Trace("[клон] снят перехват клавиши действия " + action + (original != null ? ", вернул исходной кнопке" : ""));
                }
            }
            catch (Exception e) { Plugin.Trace("[клон] горячие клавиши: " + e.Message); }
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
