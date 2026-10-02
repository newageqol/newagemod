using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal class ShopLift : MonoBehaviour
    {
        internal static int Open;

        private void OnEnable() { Open++; Lift(); }
        private void OnDisable() { Open = Mathf.Max(0, Open - 1); }
        private void LateUpdate() => Lift();

        private void Lift()
        {
            var content = transform.Find("ContentWrapper") as RectTransform;
            if (content == null) return;
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 0f;
            if (scale <= 0) scale = Mathf.Max(0.01f, Screen.height / 1080f);
            float lift = ChatDock.CoverPixels / scale;
            if (lift > 0) lift += 6;
            if (Mathf.Abs(content.offsetMin.y - lift) > 0.5f) content.offsetMin = new Vector2(content.offsetMin.x, lift);
        }

        private static float _next;
        private static bool _hadWindow;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            try
            {
                bool has = Open > 0;
                if (has != _hadWindow)
                {
                    _hadWindow = has;
                    foreach (var h in UnityEngine.Object.FindObjectsOfType<HintHolder>())
                        try { h.OnPointerExit(null); } catch { }
                }
                foreach (var w in UnityEngine.Object.FindObjectsOfType<TradePanelContentWindow>())
                {
                    if (!w.isActiveAndEnabled) continue;
                    if (w.GetComponent<ShopLift>() == null) w.gameObject.AddComponent<ShopLift>();
                }
            }
            catch (Exception e) { Plugin.Trace("[shop] above chat: " + e.Message); }
        }

        internal static void Attach(BasePanelContentWindow w)
        {
            try { if (w != null && w.GetComponent<ShopLift>() == null) w.gameObject.AddComponent<ShopLift>(); }
            catch (Exception e) { Plugin.Trace("[shop] lift: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MagicShopController), "BuildWindow")]
    internal static class ShopLiftMagicPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(ShopController), "BuildWindow")]
    internal static class ShopLiftShopPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(MarketBuyThingController), "BuildWindow")]
    internal static class ShopLiftMarketPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(ClanTreasuryListThingsController), "BuildWindow")]
    internal static class ShopLiftClanTreasuryListThingsPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(ClanTreasuryPutThingController), "BuildWindow")]
    internal static class ShopLiftClanTreasuryPutThingPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(ClanTreasuryPutCrystallsController), "BuildWindow")]
    internal static class ShopLiftClanTreasuryPutCrystallsPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(StorageGetThingController), "BuildWindow")]
    internal static class ShopLiftStorageGetThingPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(StoragePutThingController), "BuildWindow")]
    internal static class ShopLiftStoragePutThingPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => ShopLift.Attach(__result);
    }

    [HarmonyPatch(typeof(HintHolder), "Update")]
    internal static class ShopLiftHintGuard
    {
        private static readonly System.Reflection.FieldInfo Enter = AccessTools.Field(typeof(HintHolder), "_mouseEnterTime");

        private static bool Prefix(HintHolder __instance)
        {
            if (ShopLift.Open <= 0 || Enter == null) return true;
            if ((float)Enter.GetValue(__instance) <= 0f) return true;
            if (__instance.GetComponentInParent<TradePanelContentWindow>() != null) return true;
            Enter.SetValue(__instance, 0f);
            return false;
        }
    }
}
