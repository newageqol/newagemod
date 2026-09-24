using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatController), "OnChatResponse")]
    internal static class TeamBubbleOffPatch
    {
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(TeamChatPanel), "Init")]
    internal static class TeamBubbleWipePatch
    {
        private static bool Prefix(TeamChatPanel __instance)
        {
            if (__instance != null) Object.Destroy(__instance.gameObject);
            return false;
        }
    }
}
