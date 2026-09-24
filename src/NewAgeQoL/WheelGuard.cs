using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class WheelGuard
    {
        internal static bool Over()
        {
            try { return HelpColumn.Under() || SkillList.Things.Under() || Roster.Under(); }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(CameraControl), "LateUpdate")]
    internal static class WheelGuardPatch
    {
        private static bool Prefix()
        {
            try
            {
                if (Mathf.Approximately(Input.GetAxis("Mouse ScrollWheel"), 0f)) return true;
                return !WheelGuard.Over();
            }
            catch { return true; }
        }
    }
}
