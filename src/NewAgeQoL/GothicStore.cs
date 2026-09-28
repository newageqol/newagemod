using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class GothicStore
    {
        private static readonly FieldInfo FName = AccessTools.Field(typeof(ShopThingItemRenderer), "NameLabel");
        private static readonly FieldInfo FCaption = AccessTools.Field(typeof(ShopThingItemRenderer), "_priceCaption");
        private static readonly FieldInfo[] FPrices =
        {
            AccessTools.Field(typeof(ShopThingItemRenderer), "Price1"),
            AccessTools.Field(typeof(ShopThingItemRenderer), "Price2"),
            AccessTools.Field(typeof(ShopThingItemRenderer), "Price3"),
        };
        private static readonly FieldInfo[] FDescs =
        {
            AccessTools.Field(typeof(ShopThingItemRenderer), "DescriptionText1"),
            AccessTools.Field(typeof(ShopThingItemRenderer), "DescriptionText2"),
            AccessTools.Field(typeof(ShopThingItemRenderer), "DescriptionText3"),
        };
        private static readonly FieldInfo FRowSeller = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "SelllerLoginText");
        private static readonly FieldInfo FRowBuy = AccessTools.Field(typeof(MarketProposalListItemRowItemRenderer), "BuyButton");

        private static T Get<T>(FieldInfo f, object o) where T : class => f != null ? f.GetValue(o) as T : null;

        private static RectTransform Pin(Transform t, Transform root, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)t;
            if (rt.parent != root) rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            if (size.x > 0) rt.sizeDelta = size;
            return rt;
        }

        internal static void Arch(Transform thing, Vector2 size)
        {
            var own = thing.GetComponent<Image>();
            if (own != null) own.color = new Color(own.color.r, own.color.g, own.color.b, 0f);
            var arch = thing.Find("QoLArch");
            if (arch == null)
            {
                var ag = new GameObject("QoLArch", typeof(RectTransform), typeof(Image), typeof(Mask));
                ag.transform.SetParent(thing, false);
                arch = ag.transform;
                ag.GetComponent<Mask>().showMaskGraphic = true;
            }
            arch.SetSiblingIndex(0);
            var art = (RectTransform)arch;
            art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
            art.pivot = new Vector2(0.5f, 0.5f);
            art.sizeDelta = size * 0.94f;
            art.anchoredPosition = Vector2.zero;
            var ai = arch.GetComponent<Image>();
            ai.sprite = Gothic.Sprite("icon_backing");
            ai.raycastTarget = false;
            ai.color = Color.white;
            var frame = thing.Find("Frame");
            var icon = arch.Find("Icon") ?? (frame != null ? frame.Find("Icon") : null);
            if (icon != null)
            {
                if (icon.parent != arch) icon.SetParent(arch, false);
                var irt = (RectTransform)icon;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(size.x * 0.78f, size.x * 0.78f);
                irt.anchoredPosition = new Vector2(0, -size.y * 0.08f);
                var ii = icon.GetComponent<Image>();
                if (ii != null) ii.preserveAspect = true;
            }
            var fimg = frame != null ? frame.GetComponent<Image>() : null;
            if (fimg != null)
            {
                frame.SetAsLastSibling();
                fimg.sprite = Gothic.Sprite("icon_frame");
                fimg.type = Image.Type.Simple;
                fimg.preserveAspect = false;
                fimg.raycastTarget = false;
                fimg.color = Color.white;
                var frt = (RectTransform)frame;
                frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
                frt.pivot = new Vector2(0.5f, 0.5f);
                frt.sizeDelta = size;
                frt.anchoredPosition = Vector2.zero;
            }
        }

        private static void Shade(Text t)
        {
            var sh = t.GetComponent<Shadow>();
            if (sh == null) sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.95f);
            sh.effectDistance = new Vector2(1.3f, -1.3f);
        }

        private static Color Bright(Color c)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum >= 0.6f) return c;
            float sat = Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b);
            return sat > 0.2f ? Color.Lerp(c, Color.white, 0.6f) : Gothic.Paper;
        }

        private static Color Light(Color c)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum >= 0.5f) return c;
            float sat = Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b);
            return sat > 0.25f ? Color.Lerp(c, Color.white, 0.55f) : Gothic.Paper;
        }

        internal static void Card(ShopThingItemRenderer card)
        {
            if (!Gothic.On || card == null) return;
            var root = card.transform;

            var back = card.GetComponent<Image>();
            if (back != null)
            {
                back.sprite = Gothic.Sprite("card");
                back.type = Image.Type.Sliced;
                back.pixelsPerUnitMultiplier = 1.6f;
                back.color = new Color(0.8f, 0.78f, 0.78f);
            }

            var name = Get<Text>(FName, card);
            if (name != null)
            {
                Gothic.Font(name, true, null, 21);
                name.color = Light(name.color);
                var nr = (RectTransform)name.transform;
                if (nr.parent != root) nr.SetParent(root, false);
                nr.anchorMin = new Vector2(0, 1);
                nr.anchorMax = new Vector2(1, 1);
                nr.pivot = new Vector2(0.5f, 1);
                nr.offsetMin = new Vector2(60, -58);
                nr.offsetMax = new Vector2(-60, -24);
                name.alignment = TextAnchor.MiddleCenter;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 12;
                name.resizeTextMaxSize = 21;
                Shade(name);
            }

            var thing = root.Find("DescriptionContainer/Wrapper/ShortDescriptionWrapper/ThingImage") ?? root.Find("ThingImage");
            if (thing != null)
            {
                Pin(thing, root, new Vector2(0.5f, 1), new Vector2(86, -56), new Vector2(98, 126));
                Arch(thing, new Vector2(98, 126));
            }

            var cap = Get<Text>(FCaption, card);
            if (cap != null && cap.gameObject.activeSelf) cap.gameObject.SetActive(false);
            float x = 160;
            foreach (var f in FPrices)
            {
                var p = Get<Component>(f, card);
                if (p == null) continue;
                Pin(p.transform, root, new Vector2(0, 0.5f), new Vector2(x + 12, -86), Vector2.zero);
                GothicShop.Badge(p.transform);
                foreach (var t in p.GetComponentsInChildren<Text>(true)) { Gothic.Font(t, false, Gothic.Paper); t.fontSize = Mathf.Max(t.fontSize, 17); }
                if (p.gameObject.activeSelf) x += 128;
            }

            float top = x > 160 ? 110f : 64f;
            int reqs = 0;
            foreach (var f in FDescs)
            {
                var d = Get<Text>(f, card);
                if (d == null) continue;
                Gothic.Font(d, false, null, 16);
                if (!d.gameObject.activeSelf) continue;
                Pin(d.transform, root, new Vector2(0, 1), new Vector2(160, -top - 21 * reqs), new Vector2(400, 21));
                d.resizeTextForBestFit = false;
                d.fontSize = 16;
                d.alignment = TextAnchor.MiddleLeft;
                d.horizontalOverflow = HorizontalWrapMode.Overflow;
                d.verticalOverflow = VerticalWrapMode.Overflow;
                d.color = Light(d.color);
                if (d.color.r > 0.6f && d.color.g < 0.45f) d.color = Gothic.KeyRed;
                Shade(d);
                reqs++;
            }

            var detail = root.Find("DetalDescriptionContainer");
            if (detail != null)
            {
                var dr = (RectTransform)detail;
                dr.anchorMin = dr.anchorMax = new Vector2(0, 1);
                dr.pivot = new Vector2(0, 1);
                dr.anchoredPosition = new Vector2(150, -top - 4 - 21 * reqs);
                var box = detail.GetComponent<GothicFitBox>() ?? detail.gameObject.AddComponent<GothicFitBox>();
                box.card = (RectTransform)root;
                foreach (var t in detail.GetComponentsInChildren<Text>(true))
                {
                    Gothic.Font(t, false);
                    t.color = Light(t.color);
                    t.supportRichText = true;
                    t.text = Gothic.Keywords(t.text);
                    t.resizeTextForBestFit = false;
                    t.fontSize = 15;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Shade(t);
                }
            }
        }

        internal static void Filters(Transform panel)
        {
            var filter = panel.Find("FilterPanel");
            if (filter == null) return;
            foreach (var img in filter.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "GradientFon" || img.name == "FilterPanel" || img.name == "DialogContentLightFon")
                    img.color = new Color(0f, 0f, 0f, img.name == "GradientFon" ? 0.35f : 0f);
            }
            foreach (var input in filter.GetComponentsInChildren<InputField>(true))
            {
                var ii = input.GetComponent<Image>();
                if (ii != null) { ii.sprite = Gothic.Sprite("price_badge"); ii.type = Image.Type.Sliced; ii.pixelsPerUnitMultiplier = 6f; ii.color = Color.white; }
                if (input.textComponent != null) Gothic.Font(input.textComponent, false, Gothic.Paper);
                if (input.placeholder is Text ph) Gothic.Font(ph, false, Gothic.Paper2);
            }
            foreach (var t in filter.GetComponentsInChildren<Text>(true))
            {
                if (t.GetComponentInParent<InputField>() != null) continue;
                Gothic.Font(t, false);
                t.color = Light(t.color);
                Shade(t);
            }
        }

        internal static void Row(MarketProposalListItemRowItemRenderer row)
        {
            if (!Gothic.On || row == null) return;
            var root = row.transform;
            var back = root.GetComponent<Image>();
            if (back != null)
            {
                back.sprite = Gothic.Sprite("card");
                back.type = Image.Type.Sliced;
                back.pixelsPerUnitMultiplier = 2.2f;
                back.color = new Color(0.8f, 0.78f, 0.78f);
            }
            var iconHolder = root.Find("InteractiveIcon78x78");
            if (iconHolder != null)
            {
                Arch(iconHolder, new Vector2(88, 114));
                var lot = iconHolder.Find("Frame/SingleLot");
                if (lot != null)
                {
                    var lr = (RectTransform)lot;
                    lr.anchorMin = lr.anchorMax = new Vector2(0, 0);
                    lr.pivot = new Vector2(0, 0);
                    lr.anchoredPosition = new Vector2(10, 9);
                    lr.sizeDelta = new Vector2(52, 52);
                    lot.SetAsLastSibling();
                }
            }
            var seller = Get<Text>(FRowSeller, row);
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t.GetComponentInParent<Button>() != null) continue;
                Gothic.Font(t, false);
                if (t.color.a > 0.05f) t.color = t == seller ? Bright(t.color) : Light(t.color);
                if (t == seller)
                {
                    var o = t.GetComponent<Outline>() ?? t.gameObject.AddComponent<Outline>();
                    o.effectColor = new Color(0, 0, 0, 0.9f);
                    o.effectDistance = new Vector2(1.2f, -1.2f);
                }
                Shade(t);
            }
            var fit = root.GetComponentInChildren<MarketPacksFit>(true);
            if (fit != null)
            {
                fit.SellerColor = Gothic.Paper;
                if (fit.Name != null) { Gothic.Font(fit.Name, false, Gothic.Paper); fit.Name.color = Gothic.Paper; }
                if (fit.Packs != null) { Gothic.Font(fit.Packs, false, Gothic.Paper); fit.Packs.color = Gothic.Paper; }
            }
            else if (seller != null && seller.color.a > 0.05f) seller.color = Gothic.Paper;
            var group = root.Find("Prices");
            var vg = group != null ? group.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
            if (vg != null && vg.spacing < 8) vg.spacing = 8;
            foreach (var p in root.GetComponentsInChildren<DialogPrice>(true))
            {
                GothicShop.Badge(p.transform, 0, 0);
                var ple = p.GetComponent<LayoutElement>() ?? p.gameObject.AddComponent<LayoutElement>();
                ple.minHeight = 38; ple.preferredHeight = 38;
                foreach (var t in p.GetComponentsInChildren<Text>(true)) Gothic.Font(t, false, Gothic.Paper);
            }
            var buy = Get<Button>(FRowBuy, row);
            if (buy != null)
            {
                Gothic.Button(buy);
                var bi = buy.GetComponent<Image>();
                if (bi != null) { bi.pixelsPerUnitMultiplier = 2.2f; bi.sprite = Gothic.Sprite("button_hover"); }
                var st = buy.spriteState;
                st.selectedSprite = Gothic.Sprite("button_hover");
                buy.spriteState = st;
                var le = buy.GetComponent<LayoutElement>();
                if (le != null) { le.preferredHeight = 84; le.minHeight = 84; le.flexibleHeight = 0; }
                var bl = buy.GetComponentInChildren<Text>(true);
                if (bl != null) { bl.resizeTextForBestFit = true; bl.resizeTextMinSize = 12; bl.resizeTextMaxSize = 22; }
            }
        }
    }

    internal class GothicFitBox : MonoBehaviour
    {
        public RectTransform card;

        private readonly System.Collections.Generic.Dictionary<Transform, int> _order = new System.Collections.Generic.Dictionary<Transform, int>();
        private readonly System.Collections.Generic.List<Transform> _cols = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Transform> _rows = new System.Collections.Generic.List<Transform>();
        private float _pitchX, _pitchY;
        private bool _flow, _told;

        private void Learn()
        {
            if (_cols.Count > 0) return;
            foreach (Transform col in transform) _cols.Add(col);
            for (int i = 0; i < _cols.Count; i++)
                for (int j = 0; j < _cols[i].childCount; j++)
                    _order[_cols[i].GetChild(j)] = i * 100 + j;
            _flow = _cols.Count > 1;
            foreach (var col in _cols)
            {
                var v = col.GetComponent<VerticalLayoutGroup>();
                if (v == null || v.childControlHeight) { _flow = false; break; }
            }
            if (_flow)
            {
                var c0 = (RectTransform)_cols[0];
                var c1 = (RectTransform)_cols[1];
                _pitchX = Mathf.Abs(c1.localPosition.x - c0.localPosition.x);
                var v = _cols[0].GetComponent<VerticalLayoutGroup>();
                float rh = 0f;
                foreach (Transform r in _cols[0]) { rh = ((RectTransform)r).rect.height; if (rh > 1f) break; }
                _pitchY = rh + v.spacing;
                if (_pitchX < 10f || _pitchY < 5f) _flow = false;
            }
            if (!_told)
            {
                _told = true;
                Plugin.Trace("[gothic] stat columns " + _cols.Count + ", rows " + _order.Count + ", reflow " + (_flow ? "on, column " + _pitchX.ToString("0") + ", row " + _pitchY.ToString("0") : "off"));
            }
        }

        private void Reflow(RectTransform rt, float availW, float availH)
        {
            _rows.Clear();
            foreach (var kv in _order) if (kv.Key != null && kv.Key.gameObject.activeSelf) _rows.Add(kv.Key);
            if (_rows.Count == 0) return;
            _rows.Sort((x, y) => _order[x].CompareTo(_order[y]));
            int n = _rows.Count, bestCols = 1;
            float bestK = -1f;
            for (int c = 1; c <= _cols.Count && c <= n; c++)
            {
                int per = (n + c - 1) / c;
                float k = Mathf.Min(availW / (c * _pitchX), availH / (per * _pitchY));
                if (k > bestK + 0.01f) { bestK = k; bestCols = c; }
            }
            int each = (n + bestCols - 1) / bestCols;
            bool moved = false;
            for (int i = 0; i < n; i++)
            {
                var want = _cols[i / each];
                var row = _rows[i];
                if (row.parent != want) { row.SetParent(want, false); moved = true; }
                int at = i % each;
                if (row.GetSiblingIndex() != at) { row.SetSiblingIndex(at); moved = true; }
            }
            if (!moved) return;
            foreach (var col in _cols)
            {
                var v = col.GetComponent<VerticalLayoutGroup>();
                if (v != null)
                {
                    int band = (int)v.childAlignment % 3;
                    v.childAlignment = (TextAnchor)band;
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)col);
            }
        }

        private void LateUpdate()
        {
            if (card == null) return;
            var rt = (RectTransform)transform;
            float availW = card.rect.width - rt.anchoredPosition.x - 20f;
            float availH = card.rect.height + rt.anchoredPosition.y - 18f;
            Learn();
            if (_flow) Reflow(rt, availW, availH);
            float bottom = 0f, right = 0f;
            var c4 = new Vector3[4];
            foreach (Transform col in transform)
            {
                if (!col.gameObject.activeSelf) continue;
                foreach (Transform row in col)
                {
                    if (!row.gameObject.activeSelf) continue;
                    ((RectTransform)row).GetWorldCorners(c4);
                    var lo = rt.InverseTransformPoint(c4[0]);
                    var hi = rt.InverseTransformPoint(c4[2]);
                    bottom = Mathf.Max(bottom, -lo.y);
                    right = Mathf.Max(right, hi.x);
                }
            }
            if (right <= 0 || bottom <= 0) return;
            float k = Mathf.Clamp(Mathf.Min(availW / right, availH / Mathf.Max(bottom, 1f)), 0.55f, 1.15f);
            if (Mathf.Abs(rt.localScale.x - k) > 0.01f) rt.localScale = new Vector3(k, k, 1f);
        }
    }

    [HarmonyPatch(typeof(ShopThingItemRenderer), "UpdateView")]
    internal static class GothicShopCardPatch
    {
        private static void Postfix(ShopThingItemRenderer __instance)
        {
            try { GothicStore.Card(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] shop card: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MarketProposalListItemRowItemRenderer), "UpdateView")]
    internal static class GothicMarketRowPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MarketProposalListItemRowItemRenderer __instance)
        {
            try { GothicStore.Row(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] market row: " + e.Message); }
        }
    }

    internal class GothicLotScroll : MonoBehaviour
    {
        private static readonly FieldInfo FMax = AccessTools.Field(typeof(Grid<MarketProposalListItemDTO>), "_maxScrollPosition");
        private static readonly FieldInfo FTotal = AccessTools.Field(typeof(Grid<MarketProposalListItemDTO>), "_totalRowCount");
        private static readonly FieldInfo FFilter = AccessTools.Field(typeof(Grid<MarketProposalListItemDTO>), "RectTransformDimensionsChangeFilter");
        private static readonly FieldInfo FLayout = AccessTools.Field(typeof(Grid<MarketProposalListItemDTO>), "GridLayout");
        private int _frames;
        private bool _told;

        private void LateUpdate()
        {
            if (++_frames > 600) { Destroy(this); return; }
            try
            {
                foreach (var g in GetComponentsInChildren<MarketProposalListGrid>(false))
                {
                    var filter = FFilter?.GetValue(g) as Component;
                    var layout = FLayout?.GetValue(g) as GridLayoutGroup;
                    if (filter == null || layout == null || FMax == null || FTotal == null) continue;
                    float view = ((RectTransform)g.transform).rect.height;
                    float inner = ((RectTransform)filter.transform).rect.height;
                    float visible = Mathf.Min(view, inner);
                    float cell = g.ItemSize.y + layout.spacing.y;
                    int total = (int)FTotal.GetValue(g);
                    float max = (float)FMax.GetValue(g);
                    float want = total * cell - visible + layout.padding.top + layout.padding.bottom;
                    if (!_told && total > 0 && visible > 1f)
                    {
                        _told = true;
                        Plugin.Trace("[gothic] lot list: rows " + total + ", cell " + cell + ", view " + view.ToString("0") + ", inner " + inner.ToString("0") + ", max " + max.ToString("0") + ", want " + want.ToString("0"));
                    }
                    if (total > 0 && visible > 1f && want > max + 1f) FMax.SetValue(g, want);
                }
            }
            catch (Exception e) { Plugin.Trace("[gothic] lot scroll: " + e.Message); Destroy(this); }
        }
    }

    [HarmonyPatch(typeof(MarketProposalListDialog), "InitializeDialog")]
    internal static class GothicMarketDialogPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MarketProposalListDialog __instance)
        {
            try { if (__instance.GetComponent<GothicLotScroll>() == null) __instance.gameObject.AddComponent<GothicLotScroll>(); }
            catch (Exception e) { Plugin.Trace("[gothic] lot scroll attach: " + e.Message); }
            try { GothicHint.Skin(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] market dialog: " + e.Message); }
        }
    }
}
