using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(ButtonHintProvider), "PrepareString")]
    internal static class LinkHints
    {
        private const int ReturnHint = 19;

        private static void Postfix(GameObject gameObject, ref string __result)
        {
            try
            {
                if (__result != null && __result.EndsWith("[None]")) __result = __result.Substring(0, __result.Length - 6);
                if (gameObject == null) return;
                var holder = gameObject.GetComponent<HintHolder>();
                if (holder == null || holder.Id != ReturnHint) return;
                var btn = gameObject.GetComponent<BottomPanelButton>();
                if (btn == null || btn.ObjectType != EObjectType.Link || btn.text == null) return;
                string label = btn.text.text;
                if (string.IsNullOrEmpty(label)) return;
                __result = Lang.F("   Перейти: {0}", label.Trim());
            }
            catch { }
        }

        internal static void Drop()
        {
            try
            {
                foreach (var h in Object.FindObjectsByType<HintHolder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (h != null) h.OnPointerExit(null);
            }
            catch (System.Exception e) { Plugin.Caught("[hints] drop: " + e.Message); }
        }
    }
}
