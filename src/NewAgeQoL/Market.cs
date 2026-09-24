using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Transport.Messages.Common.User;
using Transport.Messages.Requests.Things.Actions;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Shop;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Market
    {
        private const int PutOnMarket = 0x1000;
        private const int RemoveFromSale = 0x40;
        private const int WinPutOnMarket = -103;
        private const int WinSellerOffers = -107;
        private const int MaxLots = 999;


        private static object _on;
        private static int _lots = 1;
        private static int _remaining;
        private static bool _ours;
        private static bool _done = true;
        private static int _id, _tab, _quantity;
        private static float _talls, _gold;
        private static bool _single;

        private static readonly Dictionary<int, int> _offerThing = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<int>> _thingOffers = new Dictionary<int, List<int>>();
        private static int _removeLots = 1;
        private static int _removeCap = 1;
        private static bool _rDone = true;
        private static int _rTab;
        private static readonly List<int> _rQueue = new List<int>();

        internal static void Tick()
        {
            try
            {
                Listen();
                ShopCards.Tick();
                if (MarketAirCenterPatch.Pending && Time.unscaledTime - MarketAirCenterPatch.PendingAt > 1f)
                    MarketAirCenterPatch.Pending = false;
            }
            catch (Exception e) { Plugin.Fault("[рынок] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(416, OnResult);
            nc.RemoveMessageListener(102, OnOffers);
            nc.AddMessageListener(416, OnResult);
            nc.AddMessageListener(102, OnOffers);
            _on = nc;
        }

        private static void OnOffers(object m)
        {
            var msg = m as ShopTabContentResponseMessage;
            if (msg == null || msg.WindowId != WinSellerOffers || msg.Items == null) return;
            _offerThing.Clear();
            _thingOffers.Clear();
            foreach (var it in msg.Items)
            {
                if (it == null || it.ThingInfo == null) continue;
                int oid = it.ShopItemId.GetValueOrDefault();
                if (oid == 0) continue;
                int tid = it.ThingInfo.ThingId;
                _offerThing[oid] = tid;
                List<int> list;
                if (!_thingOffers.TryGetValue(tid, out list)) { list = new List<int>(); _thingOffers[tid] = list; }
                list.Add(oid);
            }
        }

        internal static void FixDecimal(PutOnMarketConfirmDialog dialog)
        {
            if (dialog == null) return;
            try
            {
                foreach (var fname in new[] { "TallPriceInputField", "GoldPriceInputField" })
                {
                    var cur = AccessTools.Field(typeof(PutOnMarketConfirmDialog), fname)?.GetValue(dialog) as PutOnMarketCurrencyInputField;
                    if (cur == null) continue;
                    var input = AccessTools.Field(typeof(PutOnMarketCurrencyInputField), "InputField")?.GetValue(cur) as InputField;
                    if (input == null) continue;
                    input.onValidateInput = (text, index, ch) =>
                    {
                        if (ch == ',') ch = '.';
                        return char.IsDigit(ch) || ch == '.' ? ch : '\0';
                    };
                    var field = input;
                    field.onValueChanged.AddListener(v =>
                    {
                        if (v != null && v.IndexOf(',') >= 0) field.SetTextWithoutNotify(v.Replace(',', '.'));
                    });
                }
            }
            catch (Exception e) { Plugin.Trace("[рынок] запятая в цене: " + e.Message); }
        }

        internal static void Setup(PutOnMarketConfirmDialog dialog)
        {
            _lots = 1;
            _remaining = 0;
            _done = true;
            if (dialog == null) return;
            try
            {
                var qty = AccessTools.Field(typeof(PutOnMarketConfirmDialog), "QuantityInputField")?.GetValue(dialog) as IntegerInputField;
                if (qty == null) return;

                var row = qty.transform.parent;
                var rowGo = UnityEngine.Object.Instantiate(row.gameObject, row.parent);
                Clones.StripHotkeys(rowGo, row.gameObject);
                rowGo.name = "QoLLotsRow";
                rowGo.transform.SetSiblingIndex(row.GetSiblingIndex() + 1);
                rowGo.SetActive(true);

                var field = rowGo.GetComponentInChildren<IntegerInputField>(true);
                if (field == null) { UnityEngine.Object.Destroy(rowGo); return; }

                foreach (Transform child in rowGo.transform)
                {
                    if (child == field.transform || field.transform.IsChildOf(child)) continue;
                    foreach (var g in child.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
                    foreach (var s in child.GetComponentsInChildren<Selectable>(true)) s.interactable = false;
                }

                _field = field;
                _qty = qty;
                _stock = Stock(qty);
                _field.OnValueChanged = new IntegerInputField.OnValueChangedEvent();
                _field.OnValueChanged.AddListener(OnLotsChanged);
                _field.Initialize("Лотов", 1, MaxByQuantity(), 1);

                var origLabel = AccessTools.Field(typeof(IntegerInputField), "LabelText")?.GetValue(qty) as Text;
                var newLabel = AccessTools.Field(typeof(IntegerInputField), "LabelText")?.GetValue(_field) as Text;
                if (origLabel != null && newLabel != null)
                {
                    float w = origLabel.preferredWidth;
                    if (w > 1f)
                    {
                        var le = newLabel.GetComponent<LayoutElement>();
                        if (le == null) le = newLabel.gameObject.AddComponent<LayoutElement>();
                        le.minWidth = w;
                        le.preferredWidth = w;
                        newLabel.alignment = origLabel.alignment;
                    }
                    var ort = (RectTransform)origLabel.transform;
                    var nrt = (RectTransform)newLabel.transform;
                    nrt.anchorMin = ort.anchorMin;
                    nrt.anchorMax = ort.anchorMax;
                    nrt.pivot = ort.pivot;
                    nrt.sizeDelta = ort.sizeDelta;
                    LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_field.transform);
                }

                qty.OnValueChanged.AddListener(OnQuantityChanged);
            }
            catch (Exception e) { Plugin.Warn("[рынок] поле лотов не добавлено: " + e.Message); }
        }

        private static IntegerInputField _field, _qty;
        private static int _stock;

        private static int Stock(IntegerInputField qty)
        {
            try
            {
                int max = (int)(AccessTools.Field(typeof(IntegerInputField), "_maxValue")?.GetValue(qty) ?? 0);
                return max > 0 ? max : 1;
            }
            catch { return 1; }
        }

        private static int CurrentQuantity()
        {
            try { return _qty != null ? (_qty.GetValue() ?? 1) : 1; }
            catch { return 1; }
        }

        private static int MaxByQuantity()
        {
            int q = CurrentQuantity();
            int max = q > 0 ? _stock / q : _stock;
            return max < 1 ? 1 : (max > MaxLots ? MaxLots : max);
        }

        private static void OnLotsChanged(int v)
        {
            int cap = MaxByQuantity();
            _lots = v < 1 ? 1 : (v > cap ? cap : v);
        }

        private static void OnQuantityChanged(int q)
        {
            if (_field == null) return;
            int cap = MaxByQuantity();
            if (_lots > cap) _lots = cap;
            try { _field.Initialize("Лотов", 1, cap, _lots < 1 ? 1 : _lots); }
            catch { }
        }

        internal static void Outgoing(BaseRequest request)
        {
            if (request == null) return;
            if (_ours) { _ours = false; return; }
            try
            {
                var msg = request.GenerateMessage() as ThingContextActionRequestMessage;
                if (msg == null) return;

                if (_done && msg.ButtonId == PutOnMarket && msg.WindowId == WinPutOnMarket && _lots > 1)
                {
                    _id = msg.Id;
                    _tab = msg.TabId;
                    _quantity = msg.Quantity.GetValueOrDefault(1);
                    _talls = msg.Cash != null ? msg.Cash.Talls.GetValueOrDefault() : 0f;
                    _gold = msg.Cash != null ? msg.Cash.Gold.GetValueOrDefault() : 0f;
                    _single = msg.SingleLot.GetValueOrDefault();
                    _remaining = _lots - 1;
                    _done = false;
                    Air("Рынок: выставляю лотов: " + _lots);
                    Plugin.Trace("[рынок] лот 1/" + _lots + " ушёл, в очереди ещё " + _remaining);
                    return;
                }

                if (_rDone && msg.ButtonId == RemoveFromSale && msg.WindowId == WinSellerOffers && _removeLots > 1)
                {
                    _rTab = msg.TabId;
                    int tid;
                    if (!_offerThing.TryGetValue(msg.Id, out tid)) return;
                    List<int> all;
                    if (!_thingOffers.TryGetValue(tid, out all)) return;
                    _rQueue.Clear();
                    foreach (int oid in all)
                    {
                        if (oid == msg.Id) continue;
                        _rQueue.Add(oid);
                        if (_rQueue.Count >= _removeLots - 1) break;
                    }
                    _rDone = false;
                    Air("Рынок: снимаю лотов: " + _removeLots);
                    Plugin.Trace("[рынок] снятие 1/" + _removeLots + ", в очереди ещё " + _rQueue.Count);
                }
            }
            catch (Exception e) { Plugin.Trace("[рынок] исходящий: " + e.Message); }
        }

        private static void OnResult(object m)
        {
            var resp = m as ThingContextActionResponseMessage;
            if (resp == null) return;

            if (resp.WindowId == WinSellerOffers && resp.ButtonId == RemoveFromSale && resp.Success)
                DropOffer(resp.Id);

            if (!_done && resp.WindowId == WinPutOnMarket)
            {
                if (!resp.Success)
                {
                    Plugin.Trace("[рынок] сервер отказал, остановка: " + resp.ErrorMessage);
                    _done = true;
                    _remaining = 0;
                    Refresh(WinPutOnMarket, _tab);
                    return;
                }
                if (_remaining > 0)
                {
                    int stock = -1;
                    if (resp.ChangesInTab != null)
                        foreach (var ch in resp.ChangesInTab)
                            if (ch != null && ch.Id == _id) { stock = ch.Quantity; break; }
                    if (stock == 0)
                    {
                        int placed = _lots - _remaining;
                        _done = true;
                        _remaining = 0;
                        Air("Рынок: вещи кончились, выставлено лотов: " + placed);
                        Plugin.Trace("[рынок] запись " + _id + " опустела, остальные лоты не шлю");
                        Refresh(WinPutOnMarket, _tab);
                        return;
                    }
                    _remaining--;
                    try
                    {
                        _ours = true;
                        var req = new ContextActionRequest(_id, PutOnMarket, WinPutOnMarket, _tab, _quantity, _talls, _gold, _single, 0);
                        NetworkConnection.Instance.SendRequest(req);
                        Plugin.Trace("[рынок] лот " + (_lots - _remaining) + "/" + _lots + " ушёл");
                    }
                    catch (Exception e) { _ours = false; _done = true; _remaining = 0; Plugin.Fault("[рынок] отправка лота: " + e.Message); Refresh(WinPutOnMarket, _tab); }
                    return;
                }
                _done = true;
                Air("Рынок: выставлено лотов: " + _lots);
                Plugin.Trace("[рынок] все " + _lots + " лотов выставлены");
                Refresh(WinPutOnMarket, _tab);
                return;
            }

            if (!_rDone && resp.WindowId == WinSellerOffers)
            {
                if (!resp.Success)
                {
                    Plugin.Trace("[рынок] снятие: сервер отказал, остановка: " + resp.ErrorMessage);
                    _rDone = true;
                    _rQueue.Clear();
                    Refresh(WinSellerOffers, _rTab);
                    return;
                }
                if (_rQueue.Count > 0)
                {
                    int nextId = _rQueue[0];
                    _rQueue.RemoveAt(0);
                    try
                    {
                        _ours = true;
                        NetworkConnection.Instance.SendRequest(new ContextActionRequest(nextId, RemoveFromSale, WinSellerOffers, _rTab, null));
                        Plugin.Trace("[рынок] снят лот, в очереди ещё " + _rQueue.Count);
                    }
                    catch (Exception e) { _ours = false; _rDone = true; _rQueue.Clear(); Plugin.Fault("[рынок] снятие лота: " + e.Message); Refresh(WinSellerOffers, _rTab); }
                    return;
                }
                _rDone = true;
                Air("Рынок: снято лотов: " + _removeLots);
                Plugin.Trace("[рынок] снятие завершено");
                Refresh(WinSellerOffers, _rTab);
            }
        }

        private static void Air(string text)
        {
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<AirMessageScript>();
                MarketAirCenterPatch.Pending = existing == null;
                MarketAirCenterPatch.PendingAt = Time.unscaledTime;
                AirMessageScript.ShowInformationNotification(text);
                if (existing != null) Center(existing);
            }
            catch (Exception e) { Plugin.Trace("[рынок] сообщение: " + e.Message); }
        }

        internal static void Center(AirMessageScript air)
        {
            if (air == null) return;
            var rt = air.transform as RectTransform;
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void DropOffer(int offerId)
        {
            int tid;
            if (!_offerThing.TryGetValue(offerId, out tid)) return;
            _offerThing.Remove(offerId);
            List<int> list;
            if (_thingOffers.TryGetValue(tid, out list))
            {
                list.Remove(offerId);
                if (list.Count == 0) _thingOffers.Remove(tid);
            }
        }

        private static void Refresh(int window, int tab)
        {
            try { NetworkConnection.Instance.SendRequest(new GetTabContentRequest(tab, window)); }
            catch (Exception e) { Plugin.Trace("[рынок] обновление вкладки: " + e.Message); }
        }

        internal static void SetupRemove(ThingHintDialog dialog)
        {
            _removeLots = 1;
            _removeCap = 1;
            _rDone = true;
            if (dialog == null) return;
            try
            {
                if (dialog.ContextWindow != (EThingContextWindow)WinSellerOffers) return;

                int offerId = (int)(AccessTools.Field(typeof(ThingHintDialog), "Id")?.GetValue(dialog) ?? 0);
                int tid;
                if (offerId == 0 || !_offerThing.TryGetValue(offerId, out tid)) return;
                List<int> all;
                int cap = _thingOffers.TryGetValue(tid, out all) ? all.Count : 1;
                if (cap <= 1) return;
                _removeCap = cap;

                var qty = AccessTools.Field(typeof(ThingHintDialog), "quantityInputField")?.GetValue(dialog) as IntegerInputField;
                if (qty == null) return;

                var go = UnityEngine.Object.Instantiate(qty.gameObject, qty.transform.parent);
                Clones.StripHotkeys(go, qty.gameObject);
                go.name = "QoLRemoveLotsField";
                go.transform.SetSiblingIndex(qty.transform.GetSiblingIndex() + 1);
                go.SetActive(true);
                var wrap = qty.transform.parent as RectTransform;
                if (wrap != null && !wrap.gameObject.activeSelf) wrap.gameObject.SetActive(true);

                var field = go.GetComponent<IntegerInputField>();
                field.OnValueChanged = new IntegerInputField.OnValueChangedEvent();
                field.OnValueChanged.AddListener(v => _removeLots = v < 1 ? 1 : (v > _removeCap ? _removeCap : v));
                field.Initialize("Снять лотов", 1, cap, 1);
                LayoutRebuilder.MarkLayoutForRebuild(qty.transform.parent as RectTransform);
            }
            catch (Exception e) { Plugin.Warn("[рынок] поле снятия не добавлено: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PutOnMarketConfirmDialog), "InitializeDialog")]
    public static class MarketDialogPatch
    {
        private static void Postfix(PutOnMarketConfirmDialog __instance)
        {
            Market.FixDecimal(__instance);
            Market.Setup(__instance);
        }
    }

    [HarmonyPatch(typeof(MarketProposalListItemRowItemRenderer), "UpdateView")]
    public static class MarketUnitPricePatch
    {
        private const string PacksName = "QoLPacks";
        private static readonly int[] Packs = { 10, 50, 100 };
        private static readonly System.Reflection.FieldInfo SellerField = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "SelllerLoginText");
        private static readonly System.Reflection.FieldInfo IconField = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "Icon");
        private static readonly System.Reflection.FieldInfo BuyField = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "BuyButton");
        private static readonly System.Reflection.FieldInfo GoldPriceField = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "GoldPrice");
        private static readonly System.Reflection.FieldInfo TallPriceField = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "TallPrice");
        private static readonly System.Text.StringBuilder Packed = new System.Text.StringBuilder();

        private static void Postfix(MarketProposalListItemRowItemRenderer __instance)
        {
            try
            {
                var seller = SellerField != null ? SellerField.GetValue(__instance) as Text : null;
                var icon = IconField != null ? IconField.GetValue(__instance) as Component : null;
                var gold = Price(__instance, GoldPriceField);
                var talls = Price(__instance, TallPriceField);
                var prices = gold != null ? gold.transform.parent : talls != null ? talls.transform.parent : null;
                if (seller == null || icon == null || prices == null) return;
                var fit = Fit(__instance, seller, icon, prices);
                if (fit == null) return;
                var data = __instance.Data;
                string text = null;
                if (data != null && data.Price != null && data.Quantity > 0 && !Durable(data))
                {
                    int lot = data.SingleSlot ? data.Quantity : 1;
                    text = Line(Shown(gold) ? data.Price.Gold : null, lot, "") ?? Line(Shown(talls) ? data.Price.Talls : null, lot, " талл.");
                }
                fit.Show(text);
            }
            catch (Exception e) { Plugin.Trace("[рынок] цена за 10, 50 и 100: " + e.Message); }
        }

        private static DialogPrice Price(MarketProposalListItemRowItemRenderer r, System.Reflection.FieldInfo field)
        {
            return field != null ? field.GetValue(r) as DialogPrice : null;
        }

        private static bool Shown(DialogPrice price)
        {
            return price != null && price.gameObject.activeSelf;
        }

        private static bool Durable(MarketProposalListItemDTO data)
        {
            return data.Wear.HasValue && data.ThingDescription != null && data.ThingDescription.MaxDurability.HasValue;
        }

        private static string Line(float? price, int lot, string unit)
        {
            if (!price.HasValue || price.Value <= 0f || lot <= 0) return null;
            float each = price.Value / lot;
            Packed.Length = 0;
            for (int i = 0; i < Packs.Length; i++)
            {
                if (i > 0) Packed.Append('\n');
                Packed.Append(Packs[i]).Append(" шт: ").Append(Amount(each * Packs[i])).Append(unit);
            }
            return Packed.ToString();
        }

        private static string Amount(float value)
        {
            return value >= 1f ? ResourceStrings.FloatToString(value) : value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static Text Words(Transform host, string name, Text like, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var text = go.GetComponent<Text>();
            text.font = like.font;
            text.fontStyle = style;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1f;
            text.raycastTarget = false;
            return text;
        }

        private static MarketPacksFit Fit(MarketProposalListItemRowItemRenderer row, Text seller, Component icon, Transform prices)
        {
            var found = row.transform.Find(PacksName);
            if (found != null) return found.GetComponent<MarketPacksFit>();
            var go = new GameObject(PacksName, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(row.transform, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var fit = go.AddComponent<MarketPacksFit>();
            fit.Name = Words(go.transform, "name", seller, FontStyle.Bold, TextAnchor.UpperCenter);
            fit.Packs = Words(go.transform, "packs", seller, FontStyle.Normal, TextAnchor.LowerCenter);
            var line = new GameObject("line", typeof(RectTransform), typeof(Image));
            line.transform.SetParent(go.transform, false);
            fit.Line = line.GetComponent<Image>();
            fit.Line.raycastTarget = false;
            fit.SellerText = seller;
            fit.SellerColor = seller.color;
            fit.Row = (RectTransform)row.transform;
            fit.Icon = icon.transform as RectTransform;
            fit.Prices = prices as RectTransform;
            fit.Biggest = Mathf.Clamp(seller.fontSize, 18, 24);

            var buy = BuyField != null ? BuyField.GetValue(row) as Component : null;
            var size = buy != null ? buy.GetComponent<LayoutElement>() : null;
            if (size != null)
            {
                size.flexibleHeight = 0f;
                size.preferredHeight = 90f;
            }
            go.SetActive(false);
            return fit;
        }
    }

    [HarmonyPatch(typeof(MarketProposalListDialog), "InitializeDialog")]
    public static class MarketTallRowsPatch
    {
        internal const float Extra = 60f;
        private const float Usual = 110f;
        private const int Seen = 4;
        private static readonly System.Reflection.FieldInfo GridField = AccessTools.Field(typeof(MarketProposalListDialog), "Grid");
        private static readonly System.Reflection.FieldInfo LayoutField = AccessTools.Field(typeof(Grid<MarketProposalListItemDTO>), "GridLayout");
        private static readonly System.Reflection.FieldInfo HeightField = AccessTools.Field(typeof(MarketProposalListDialog), "GridHeightModificator");

        private static bool Wanted(MarketProposalListDialogParams dialogParams)
        {
            var thing = dialogParams != null ? dialogParams.GeneralThingInfoDescription : null;
            return thing != null && !thing.MaxDurability.HasValue;
        }

        private static void Prefix(MarketProposalListDialog __instance, MarketProposalListDialogParams dialogParams)
        {
            try
            {
                if (!Wanted(dialogParams)) return;
                var grid = GridField != null ? GridField.GetValue(__instance) as Grid<MarketProposalListItemDTO> : null;
                if (grid == null) return;
                grid.ItemSize = new Vector2(grid.ItemSize.x, Usual + Extra);
                var layout = LayoutField != null ? LayoutField.GetValue(grid) as GridLayoutGroup : null;
                if (layout != null) layout.cellSize = grid.ItemSize;
            }
            catch (Exception e) { Plugin.Trace("[рынок] высота строк лотов: " + e.Message); }
        }

        private static void Postfix(MarketProposalListDialog __instance, MarketProposalListDialogParams dialogParams)
        {
            try
            {
                if (!Wanted(dialogParams) || dialogParams.Proposals == null) return;
                var height = HeightField != null ? HeightField.GetValue(__instance) as LayoutElement : null;
                if (height == null) return;
                int count = dialogParams.Proposals.Count;
                height.preferredHeight = Mathf.Min(count, Seen) * (Usual + Extra);
                Plugin.Trace("[рынок] строки лотов выше на " + Extra + " пикс. под цены за 10, 50 и 100 штук, лотов " + count);
            }
            catch (Exception e) { Plugin.Trace("[рынок] высота окна лотов: " + e.Message); }
        }
    }

    internal sealed class MarketPacksFit : MonoBehaviour
    {
        private const int Smallest = 11;
        private const float Gap = 10f;
        private const float Edge = 12f;
        private static readonly Vector3[] Corners = new Vector3[4];
        private static bool _told;
        internal Text Name;
        internal Text Packs;
        internal Image Line;
        internal Text SellerText;
        internal Color SellerColor;
        internal RectTransform Row;
        internal RectTransform Icon;
        internal RectTransform Prices;
        internal int Biggest = 20;
        private bool _keySet;
        private int _keyRoom;
        private int _keyTall;
        private string _keyName;
        private string _keyPacks;

        internal void Show(string text)
        {
            if (string.IsNullOrEmpty(text) || SellerText == null)
            {
                if (SellerText != null && SellerText.color != SellerColor) SellerText.color = SellerColor;
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            Name.text = SellerText.text;
            Name.color = SellerColor;
            Packs.text = text;
            Packs.color = SellerColor;
            Line.color = new Color(SellerColor.r, SellerColor.g, SellerColor.b, 0.45f);
            var hidden = new Color(SellerColor.r, SellerColor.g, SellerColor.b, 0f);
            if (SellerText.color != hidden) SellerText.color = hidden;
            _keySet = false;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        private static RectTransform Place(Graphic graphic, Vector2 anchor, Vector2 pivot, Vector2 at, Vector2 size)
        {
            var rt = graphic.rectTransform;
            if (rt.anchorMin != anchor) rt.anchorMin = anchor;
            if (rt.anchorMax != anchor) rt.anchorMax = anchor;
            if (rt.pivot != pivot) rt.pivot = pivot;
            if (rt.anchoredPosition != at) rt.anchoredPosition = at;
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            return rt;
        }

        private void LateUpdate()
        {
            if (Name == null || Packs == null || Line == null || Row == null || Icon == null || Prices == null) return;
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            float scale = Mathf.Abs(parent.lossyScale.x);
            if (scale < 0.00001f) return;
            Icon.GetWorldCorners(Corners);
            float left = Corners[2].x;
            Prices.GetWorldCorners(Corners);
            float right = Corners[0].x;
            Row.GetWorldCorners(Corners);
            float bottom = Corners[0].y;
            float top = Corners[1].y;
            if (right <= left || top <= bottom) return;
            int room = Mathf.FloorToInt((right - left) / scale) - 16;
            int tall = Mathf.FloorToInt((top - bottom) / scale - 2f * Edge);
            if (room < 40 || tall < 40) return;
            var rt = (RectTransform)transform;
            var at = new Vector3((left + right) * 0.5f, (top + bottom) * 0.5f, rt.position.z);
            if ((rt.position - at).sqrMagnitude > 0.0001f) rt.position = at;

            string named = Name.text;
            string packed = Packs.text;
            if (_keySet && room == _keyRoom && tall == _keyTall && named == _keyName && packed == _keyPacks) return;
            _keySet = true;
            _keyRoom = room;
            _keyTall = tall;
            _keyName = named;
            _keyPacks = packed;
            int size = Biggest;
            float nameH = 0f, packsH = 0f, wide = 0f;
            for (; size >= Smallest; size--)
            {
                Name.fontSize = size + 3;
                Packs.fontSize = size;
                nameH = Name.preferredHeight;
                packsH = Packs.preferredHeight;
                wide = Mathf.Max(Name.preferredWidth, Packs.preferredWidth);
                if (wide <= room && nameH + Gap + packsH <= tall) break;
            }
            float block = nameH + Gap + packsH;
            rt.sizeDelta = new Vector2(room, block);
            Place(Name, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(room, nameH));
            Place(Line, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -(nameH + Gap * 0.5f)), new Vector2(Mathf.Min(room, wide + 24f), 2f));
            Place(Packs, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(room, packsH));
            if (_told) return;
            _told = true;
            Plugin.Trace("[рынок] лот: ник и цены за 10, 50 и 100 штук блоком по центру, места " + room + "×" + tall + " пикс., шрифт " + size + " из " + Biggest);
        }
    }

    [HarmonyPatch(typeof(ThingHintDialog), "FillInventoryThingFields")]
    public static class MarketRemoveDialogPatch
    {
        private static void Postfix(ThingHintDialog __instance) => Market.SetupRemove(__instance);
    }

    [HarmonyPatch(typeof(AirMessageScript), "Start")]
    public static class MarketAirCenterPatch
    {
        internal static bool Pending;
        internal static float PendingAt;

        private static void Postfix(AirMessageScript __instance)
        {
            if (!Pending) return;
            Pending = false;
            Market.Center(__instance);
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[] { typeof(BaseRequest) })]
    public static class MarketOutPatch
    {
        private static void Prefix(BaseRequest request) => Market.Outgoing(request);
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[]
    {
        typeof(ResponseCallbackContext), typeof(BaseRequest), typeof(short), typeof(int), typeof(MessageHandler)
    })]
    public static class MarketOutCallbackPatch
    {
        private static void Prefix(BaseRequest request) => Market.Outgoing(request);
    }
}
