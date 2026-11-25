using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SingleImageLoader), nameof(SingleImageLoader.LoadThingImage))]
    internal static class ThingIconDisk
    {
        private static readonly MethodInfo SetImage = AccessTools.Method(typeof(SingleImageLoader), "SetImage");
        private static readonly AccessTools.FieldRef<SingleImageLoader, bool> Canceled =
            AccessTools.FieldRefAccess<SingleImageLoader, bool>("_loadCanceled");

        private static bool Prefix(SingleImageLoader __instance, string spriteName, Action afterSuccessLoaded)
        {
            try
            {
                if (SetImage == null || string.IsNullOrEmpty(spriteName)) return true;
                if (AtlasUtils.GetThingSprite(spriteName) != null) return true;
                var sprite = WardrobeIcons.Saved(spriteName);
                if (sprite == null) return true;
                SetImage.Invoke(__instance, new object[] { sprite, false, afterSuccessLoaded });
                Canceled(__instance) = true;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Caught("[icons] thing picture from disk '" + spriteName + "': " + e.Message);
                return true;
            }
        }
    }
}
