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
                if (gameObject == null) return;
                var holder = gameObject.GetComponent<HintHolder>();
                if (holder == null || holder.Id != ReturnHint) return;
                var btn = gameObject.GetComponent<BottomPanelButton>();
                if (btn == null || btn.ObjectType != EObjectType.Link || btn.text == null) return;
                string label = btn.text.text;
                if (string.IsNullOrEmpty(label)) return;
                __result = "   Перейти: " + label.Trim();
            }
            catch { }
        }
    }
}
