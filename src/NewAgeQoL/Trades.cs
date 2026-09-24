using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Transport.Messages.Responses.User.Professions;

namespace NewAgeQoL
{
    internal static class Trades
    {
        private static readonly Dictionary<int, double> Whole = new Dictionary<int, double>();
        private static readonly Dictionary<int, bool> Round = new Dictionary<int, bool>();

        internal static bool On
        {
            get { return Plugin.CfgTradeTotal == null || Plugin.CfgTradeTotal.Value; }
        }

        internal static void Keep(UserProfessionListResponseMessageItem item, bool whole)
        {
            if (item == null) return;
            Whole[item.ProfessionId] = item.CurrentValue.GetValueOrDefault();
            Round[item.ProfessionId] = whole;
        }

        internal static string Total(int professionId)
        {
            double value;
            if (!Whole.TryGetValue(professionId, out value) || value <= 0d) return "";
            bool whole;
            if (!Round.TryGetValue(professionId, out whole)) whole = true;
            return value.ToString(whole ? "0" : "0.00", CultureInfo.CurrentCulture);
        }
    }

    [HarmonyPatch(typeof(ProfessionDescriptionData), "Refresh")]
    internal static class TradeValuePatch
    {
        private static void Postfix(ProfessionDescriptionData __instance, UserProfessionListResponseMessageItem item)
        {
            try { Trades.Keep(item, __instance.IsIntValue); }
            catch (Exception e) { Plugin.Trace("[профессии] значение: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(InventoryProfessionsIconWithDescriptionAndProgress), "Initialize", new[] { typeof(IconWithDescriptionData) })]
    internal static class TradeRowPatch
    {
        private static void Postfix(InventoryProfessionsIconWithDescriptionAndProgress __instance, IconWithDescriptionData data)
        {
            try
            {
                if (!Trades.On || __instance == null) return;
                var mine = data as ProfessionDescriptionData;
                if (mine == null || !mine.ProfessionPresent) return;
                var label = __instance.LabelText;
                if (label == null) return;
                string total = Trades.Total(data.Id);
                if (total.Length == 0) return;
                label.supportRichText = true;
                label.text = data.Label + "  <color=#8a5a1e>" + total + "</color>";
            }
            catch (Exception e) { Plugin.Trace("[профессии] строка: " + e.Message); }
        }
    }
}
