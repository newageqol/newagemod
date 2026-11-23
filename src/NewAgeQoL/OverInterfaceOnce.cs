using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(Unity3DHelper), nameof(Unity3DHelper.IsOverInterface))]
    internal static class OverInterfaceOnce
    {
        private static int _frame = -1;
        private static Vector3 _at;
        private static bool _over;

        private static bool Prefix(ref bool __result)
        {
            if (Time.frameCount != _frame || Input.mousePosition != _at) return true;
            __result = _over;
            return false;
        }

        private static void Postfix(bool __result)
        {
            _frame = Time.frameCount;
            _at = Input.mousePosition;
            _over = __result;
        }
    }
}
