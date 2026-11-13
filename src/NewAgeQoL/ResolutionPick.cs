using System;
using HarmonyLib;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SetupDialog), "PrepareStandalone")]
    internal static class ResolutionPickPatch
    {
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                var drop = Traverse.Create(__instance).Field("ResolutionDropdown").GetValue<Dropdown>();
                var settings = GameSettings.Instance;
                if (drop == null || settings == null) return;
                string want = settings.ResolutionWidth + " × " + settings.ResolutionHeight;
                for (int i = 0; i < drop.options.Count; i++)
                {
                    if (drop.options[i].text != want) continue;
                    if (drop.value != i) drop.SetValueWithoutNotify(i);
                    return;
                }
            }
            catch (Exception e) { Plugin.Caught("[resolution] " + e.Message); }
        }
    }
}
