using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(MarketProposalListItemRowItemRenderer), "UpdateView")]
    internal static class MarketSellerPatch
    {
        private static void Postfix(MarketProposalListItemRowItemRenderer __instance)
        {
            try { MarketSeller.Hook(__instance); }
            catch (Exception e) { Plugin.Caught("[market seller] " + e.Message); }
        }
    }

    internal sealed class MarketSeller : MonoBehaviour, IPointerClickHandler
    {
        private MarketProposalListItemRowItemRenderer _row;

        internal static void Hook(MarketProposalListItemRowItemRenderer row)
        {
            if (row == null) return;
            var go = row.gameObject;
            var click = go.GetComponent<MarketSeller>() ?? go.AddComponent<MarketSeller>();
            click._row = row;
            if (go.GetComponent<Graphic>() == null)
            {
                var hit = go.AddComponent<Image>();
                hit.color = new Color(0f, 0f, 0f, 0f);
                hit.raycastTarget = true;
            }
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            try
            {
                var data = _row != null ? _row.Data : null;
                if (data == null || string.IsNullOrEmpty(data.SellerLogin)) return;
                Plugin.Trace("[market seller] writing to " + data.SellerLogin + " (" + data.SellerId + ")");
                ChatDock.WriteTo(data.SellerId, data.SellerLogin);
            }
            catch (Exception ex) { Plugin.Caught("[market seller] click: " + ex.Message); }
        }
    }
}
