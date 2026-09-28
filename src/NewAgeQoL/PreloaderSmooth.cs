using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(Preloader), "InternalUpdateMainIndicator")]
    internal static class PreloaderSmoothPatch
    {
        private static readonly FieldInfo GemsField = AccessTools.Field(typeof(Preloader), "Gems");

        private static bool Prefix(Preloader __instance, float progress)
        {
            try
            {
                var gems = GemsField?.GetValue(__instance) as Image;
                if (gems == null) return true;
                var smooth = __instance.GetComponent<PreloaderSmooth>();
                if (smooth == null)
                {
                    smooth = __instance.gameObject.AddComponent<PreloaderSmooth>();
                    smooth.Gems = gems;
                }
                smooth.Aim(progress);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[loading] smooth ring: " + e.Message);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Preloader), "Show")]
    internal static class PreloaderPicturePatch
    {
        private static void Prefix(ref bool isNewScene) => isNewScene = true;
    }

    internal sealed class PreloaderSmooth : MonoBehaviour
    {
        internal Image Gems;

        private float _target;

        internal void Aim(float progress)
        {
            float next = Mathf.Clamp01(progress);
            if (Gems != null && (next < Gems.fillAmount || next >= 1f)) Gems.fillAmount = next;
            _target = next;
        }

        private void Update()
        {
            if (Gems == null) return;
            float now = Gems.fillAmount;
            if (now >= _target) return;
            float speed = Mathf.Max(0.6f, (_target - now) * 6f);
            Gems.fillAmount = Mathf.MoveTowards(now, _target, speed * Time.unscaledDeltaTime);
        }
    }
}
