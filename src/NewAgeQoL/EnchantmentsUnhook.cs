using System;
using System.Reflection;
using HarmonyLib;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(EnchantmentPanelsController), "OnDestroy")]
    internal static class EnchantmentsUnhook
    {
        private const short UserEnchantments = 109;
        private static readonly MethodInfo Handler = AccessTools.Method(typeof(EnchantmentPanelsController), "OnUserEnchantments");

        private static void Postfix(EnchantmentPanelsController __instance)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || Handler == null || __instance == null) return;
                var handler = (MessageHandler)Delegate.CreateDelegate(typeof(MessageHandler), __instance, Handler);
                nc.RemoveMessageListener(UserEnchantments, handler);
                nc.RemoveMessageListener(UserEnchantments, handler);
            }
            catch (Exception e) { Plugin.Trace("[enchantments] unhook: " + e.Message); }
        }
    }
}
