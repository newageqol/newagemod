using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal class GothicMark : MonoBehaviour
    {
        public int side = -1;
    }

    internal class GothicCorner : MonoBehaviour
    {
        public RectTransform frame;
        private readonly Vector3[] _c = new Vector3[4];

        private void LateUpdate()
        {
            if (frame == null) return;
            frame.GetWorldCorners(_c);
            var rt = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            float k = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            rt.position = _c[2] + new Vector3(-34f, -34f, 0) * k;
        }
    }

    internal class GothicLift : MonoBehaviour
    {
        internal static int Open;

        private void OnEnable() { Open++; GothicShop.Lift(this); }
        private void OnDisable() { Open = Mathf.Max(0, Open - 1); }
        private void LateUpdate() => GothicShop.Lift(this);
    }

    internal class GothicTop : MonoBehaviour
    {
        private Canvas _own;

        private void LateUpdate()
        {
            var root = transform.parent != null ? transform.parent.GetComponentInParent<Canvas>() : null;
            if (root == null) return;
            if (_own == null)
            {
                _own = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
                if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            }
            _own.overrideSorting = true;
            _own.sortingLayerID = root.rootCanvas.sortingLayerID;
            if (_own.sortingOrder != root.rootCanvas.sortingOrder + 1) _own.sortingOrder = root.rootCanvas.sortingOrder + 1;
        }
    }

    internal class GothicFollow : MonoBehaviour
    {
        public RectTransform target;
        public Vector4 pad;
        private readonly Vector3[] _c = new Vector3[4];

        private void LateUpdate()
        {
            if (target == null) return;
            var rt = (RectTransform)transform;
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            target.GetWorldCorners(_c);
            var a = parent.InverseTransformPoint(_c[0]);
            var b = parent.InverseTransformPoint(_c[2]);
            rt.anchorMin = rt.anchorMax = parent.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var min = new Vector2(a.x - pad.x, a.y - pad.w);
            var max = new Vector2(b.x + pad.y, b.y + pad.z);
            rt.sizeDelta = max - min;
            rt.localPosition = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0);
        }
    }

    internal static class GothicShop
    {
        private static float _next;

        private static readonly FieldInfo FName = AccessTools.Field(typeof(MagicShopItemRenderer), "NameLabel");
        private static readonly FieldInfo FManaLabel = AccessTools.Field(typeof(MagicShopItemRenderer), "ManaCostLabel");
        private static readonly FieldInfo FManaValue = AccessTools.Field(typeof(MagicShopItemRenderer), "ManaCostValue");
        private static readonly FieldInfo FDesc1 = AccessTools.Field(typeof(MagicShopItemRenderer), "DescriptionText1");
        private static readonly FieldInfo FDesc2 = AccessTools.Field(typeof(MagicShopItemRenderer), "DescriptionText2");
        private static readonly FieldInfo FDesc3 = AccessTools.Field(typeof(MagicShopItemRenderer), "DescriptionText3");
        private static readonly FieldInfo FString = AccessTools.Field(typeof(MagicShopItemRenderer), "StringDescriptionText");
        private static readonly FieldInfo FCaption = AccessTools.Field(typeof(MagicShopItemRenderer), "_priceCaption");
        private static readonly FieldInfo[] FPrices =
        {
            AccessTools.Field(typeof(MagicShopItemRenderer), "Price1"),
            AccessTools.Field(typeof(MagicShopItemRenderer), "Price2"),
            AccessTools.Field(typeof(MagicShopItemRenderer), "Price3"),
        };
        private static readonly FieldInfo FTabImage = AccessTools.Field(typeof(TabButton), "_image");
        private static readonly FieldInfo FTabIcon = AccessTools.Field(typeof(TabButton), "IconImage");

        private static T Get<T>(FieldInfo f, object o) where T : class => f != null ? f.GetValue(o) as T : null;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            try
            {
                AboveChat();
                if (!Gothic.On) return;
                foreach (var shop in UnityEngine.Object.FindObjectsOfType<ShopPanelContent>())
                {
                    if (!shop.isActiveAndEnabled) continue;
                    var w = shop.GetComponentInParent<TradePanelContentWindow>();
                    if (w == null) continue;
                    var m = Prepare(w, shop);
                    Center(shop);
                    if (m.side != 0) { m.side = 0; Accent(w.transform, Gothic.Gold); }
                }
                foreach (var panel in UnityEngine.Object.FindObjectsOfType<MagicShopPanelContent>())
                {
                    if (!panel.isActiveAndEnabled) continue;
                    var window = panel.GetComponentInParent<TradePanelContentWindow>();
                    if (window == null) continue;
                    var mark = Prepare(window, panel);
                    Center(panel);
                    int side = -1;
                    foreach (var r in panel.GetComponentsInChildren<MagicShopItemRenderer>(false))
                        if (r.Data != null) { side = r.Data.Side; break; }
                    if (side != mark.side)
                    {
                        mark.side = side;
                        Accent(window.transform, Gothic.AccentOf(side));
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[gothic] magic shop window: " + e.Message); }
        }

        internal static GothicMark Prepare(TradePanelContentWindow window, Component panel)
        {
            var mark = window.GetComponent<GothicMark>();
            if (mark != null) return mark;
            mark = window.gameObject.AddComponent<GothicMark>();
            Window(window.transform, panel.transform);
            return mark;
        }

        private static void Center(Component panel)
        {
            foreach (var gl in panel.GetComponentsInChildren<GridLayoutGroup>(true))
                if (gl.childAlignment != TextAnchor.UpperCenter) gl.childAlignment = TextAnchor.UpperCenter;
        }

        private static bool _hadWindow;

        internal static void Lift(Component w)
        {
            var content = w.transform.Find("ContentWrapper") as RectTransform;
            var canvas = w.GetComponentInParent<Canvas>();
            float scale = canvas != null ? canvas.rootCanvas.scaleFactor : 0f;
            if (content == null) return;
            if (scale <= 0) scale = Mathf.Max(0.01f, Screen.height / 1080f);
            float lift = ChatDock.CoverPixels / scale;
            if (lift > 0) lift += 6;
            if (Mathf.Abs(content.offsetMin.y - lift) > 0.5f) content.offsetMin = new Vector2(content.offsetMin.x, lift);
        }

        private static void AboveChat()
        {
            bool has = GothicLift.Open > 0;
            if (has != _hadWindow)
            {
                _hadWindow = has;
                foreach (var h in UnityEngine.Object.FindObjectsOfType<HintHolder>())
                    try { h.OnPointerExit(null); } catch { }
            }
            foreach (var w in UnityEngine.Object.FindObjectsOfType<TradePanelContentWindow>())
            {
                if (!w.isActiveAndEnabled) continue;
                if (w.GetComponent<GothicLift>() == null) w.gameObject.AddComponent<GothicLift>();
            }
        }

        private static void Window(Transform window, Transform panel)
        {
            var std = window.GetComponentInChildren<StandardContentWindowPanel>(true);
            var wrap = std != null ? std.transform.Find("PanelWrapper") : null;
            if (wrap != null)
            {
                var inner = wrap.Find("PanelContentWrapper");
                if (inner != null)
                {
                    if (inner.GetComponent<RectMask2D>() == null) inner.gameObject.AddComponent<RectMask2D>();
                    var stone = Gothic.Layer(inner, "QoLStone", "stone_tile", Image.Type.Tiled, new Color(0.72f, 0.69f, 0.76f), 0);
                    stone.pixelsPerUnitMultiplier = 1.4f;
                    Gothic.Stretch(stone.rectTransform);
                    var vig = Gothic.Layer(inner, "QoLVignette", "vignette", Image.Type.Sliced, new Color(0, 0, 0, 0.8f), 1);
                    Gothic.Stretch(vig.rectTransform);
                    var innerImg = inner.GetComponent<Image>();
                    if (innerImg != null) innerImg.color = new Color(innerImg.color.r, innerImg.color.g, innerImg.color.b, 0f);
                    var frame = Gothic.Layer(wrap, "QoLFrame", "window_frame", Image.Type.Sliced, Color.white, -1);
                    frame.pixelsPerUnitMultiplier = 3f;
                    var fle = frame.GetComponent<LayoutElement>() ?? frame.gameObject.AddComponent<LayoutElement>();
                    fle.ignoreLayout = true;
                    var follow = frame.GetComponent<GothicFollow>() ?? frame.gameObject.AddComponent<GothicFollow>();
                    follow.target = (RectTransform)inner;
                    follow.pad = new Vector4(40, 40, 40, 40);
                    var under = Gothic.Layer(wrap, "QoLUnder", "stone_tile", Image.Type.Tiled, new Color(0.62f, 0.6f, 0.66f), 0);
                    under.pixelsPerUnitMultiplier = 1.4f;
                    var ule = under.GetComponent<LayoutElement>() ?? under.gameObject.AddComponent<LayoutElement>();
                    ule.ignoreLayout = true;
                    var uf = under.GetComponent<GothicFollow>() ?? under.gameObject.AddComponent<GothicFollow>();
                    uf.target = (RectTransform)inner;
                    uf.pad = new Vector4(34, 34, 34, 34);
                }
                var header = wrap.Find("PanelHeader");
                if (header != null)
                {
                    if (header.GetComponent<GothicTop>() == null) header.gameObject.AddComponent<GothicTop>();
                    var plate = header.GetComponent<Image>();
                    if (plate != null) plate.color = new Color(plate.color.r, plate.color.g, plate.color.b, 0f);
                    var ribbon = Gothic.Layer(header, "QoLRibbon", "title_ribbon", Image.Type.Simple, Color.white, 0);
                    ribbon.preserveAspect = true;
                    var rr = ribbon.rectTransform;
                    rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
                    rr.pivot = new Vector2(0.5f, 1f);
                    rr.anchoredPosition = new Vector2(0, -2);
                    rr.sizeDelta = new Vector2(760, 150);
                    ribbon.gameObject.SetActive(false);
                    var plaque = Gothic.Layer(header, "QoLPlaque", "title_plaque", Image.Type.Sliced, Color.white, 1);
                    plaque.pixelsPerUnitMultiplier = 2.2f;
                    var pr = plaque.rectTransform;
                    pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
                    pr.pivot = new Vector2(0.5f, 0.5f);
                    pr.anchoredPosition = Vector2.zero;
                    pr.sizeDelta = new Vector2(900, 110);
                    Gothic.Font(header.GetComponentInChildren<Text>(true), true, Gothic.Paper);
                }
            }
            var close = window.Find("ContentWrapper/CloseButtonWrapper/CloseButtonWrapper/BtnClose");
            if (close != null)
            {
                foreach (var an in close.GetComponentsInParent<Animator>(true)) an.enabled = false;
                foreach (var an in close.GetComponentsInChildren<Animator>(true)) an.enabled = false;
                Gothic.Close(close.GetComponent<Button>());
                var std2 = window.GetComponentInChildren<StandardContentWindowPanel>(true);
                var fr = std2 != null ? std2.transform.Find("PanelWrapper/QoLFrame") : null;
                if (fr != null)
                {
                    var pin = close.GetComponent<GothicCorner>() ?? close.gameObject.AddComponent<GothicCorner>();
                    pin.frame = (RectTransform)fr;
                    if (close.GetComponent<GothicTop>() == null) close.gameObject.AddComponent<GothicTop>();
                }
            }
            var border = window.Find("ContentWrapper/CloseButtonWrapper/CloseButtonWrapper/CloseButtonBorder");
            if (border != null) border.gameObject.SetActive(false);
            foreach (var s in panel.GetComponentsInChildren<Scrollbar>(true)) Gothic.Scrollbar(s);
            GothicStore.Filters(panel);
            foreach (var t in window.GetComponentsInChildren<TabButton>(true)) Tab(t, false);
            Plugin.Trace("[gothic] magic shop window styled");
        }

        private static void Accent(Transform window, Color accent)
        {
            var std = window.GetComponentInChildren<StandardContentWindowPanel>(true);
            var header = std != null ? std.transform.Find("PanelWrapper/PanelHeader") : null;
            var title = header != null ? header.GetComponentInChildren<Text>(true) : null;
            if (title != null) title.color = Color.Lerp(Gothic.Paper, accent, 0.55f);
            var frame = std != null ? std.transform.Find("PanelWrapper/QoLFrame") : null;
            var fi = frame != null ? frame.GetComponent<Image>() : null;
            if (fi != null) fi.color = Color.Lerp(Color.white, accent, 0.18f);
            foreach (var t in window.GetComponentsInChildren<TabButton>(true))
            {
                var glow = t.transform.Find("QoLGlow");
                if (glow != null) glow.GetComponent<Image>().color = new Color(accent.r, accent.g, accent.b, 0.85f);
            }
        }

        internal static bool InStyled(Component c)
        {
            var w = c.GetComponentInParent<TradePanelContentWindow>();
            return w != null && w.GetComponent<GothicMark>() != null;
        }

        internal static void Tab(TabButton tab, bool selected)
        {
            var img = Get<Image>(FTabImage, tab) ?? tab.GetComponent<Image>();
            if (img == null) return;
            img.sprite = Gothic.Sprite(selected ? "tab_frame_active" : "tab_frame");
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            var ic = FTabIcon != null ? FTabIcon.GetValue(tab) as Image : null;
            if (ic != null)
            {
                var irt = ic.rectTransform;
                var tr = (RectTransform)tab.transform;
                float side = Mathf.Min(tr.rect.width, tr.rect.height);
                if (side <= 1) side = 90;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = new Vector2(side * 0.64f, side * 0.64f);
                ic.preserveAspect = true;
            }
            int count = tab.transform.parent != null ? tab.transform.parent.GetComponentsInChildren<TabButton>(false).Length : 1;
            float scale = count > 8 ? 0.72f : (count > 5 ? 0.85f : 1f);
            if (Mathf.Abs(tab.transform.localScale.x - scale) > 0.01f) tab.transform.localScale = new Vector3(scale, scale, 1f);
            img.color = Color.white;
            var mark = tab.GetComponentInParent<TradePanelContentWindow>()?.GetComponent<GothicMark>();
            var accent = Gothic.AccentOf(mark != null ? mark.side : -1);
            var glow = Gothic.Layer(tab.transform, "QoLGlow", "tab_glow", Image.Type.Simple, new Color(accent.r, accent.g, accent.b, 0.85f), 0);
            Gothic.Stretch(glow.rectTransform, -14, -14, -14, -14);
            glow.preserveAspect = true;
            glow.gameObject.SetActive(false);
            var backing = Gothic.Layer(tab.transform, "QoLBacking", "tab_disc", Image.Type.Simple, Color.white, 0);
            backing.preserveAspect = true;
            var brt = backing.rectTransform;
            Gothic.Stretch(brt);
            brt.pivot = ((RectTransform)tab.transform).pivot;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            if (backing.GetComponent<Mask>() == null) backing.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var tr2 = (RectTransform)tab.transform;
            float s2 = Mathf.Min(tr2.rect.width, tr2.rect.height);
            if (s2 <= 1) s2 = 90;
            var icon2 = FTabIcon != null ? FTabIcon.GetValue(tab) as Image : null;
            if (icon2 != null)
            {
                if (icon2.transform.parent != backing.transform) icon2.transform.SetParent(backing.transform, false);
                var r2 = icon2.rectTransform;
                r2.anchorMin = r2.anchorMax = GothicTabImagePatch.Center(tr2);
                r2.pivot = new Vector2(0.5f, 0.5f);
                r2.anchoredPosition = Vector2.zero;
                r2.sizeDelta = new Vector2(s2 * 0.58f, s2 * 0.58f);
            }
        }

        private static RectTransform Pin(Transform t, Transform root, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)t;
            if (rt.parent != root) rt.SetParent(root, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            if (size.x > 0) rt.sizeDelta = size;
            return rt;
        }

        internal static void Card(MagicShopItemRenderer card, ShopMagicContentResponseItemDTO data)
        {
            if (!Gothic.On || data == null) return;
            var root = card.transform;
            var accent = Gothic.AccentOf(data.Side);
            var TL = new Vector2(0, 1);
            var TR = new Vector2(1, 1);

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
                var nc = name.color;
                float lum = 0.3f * nc.r + 0.59f * nc.g + 0.11f * nc.b;
                name.color = lum < 0.55f ? Color.Lerp(Gothic.Paper, nc, 0.25f) : Color.Lerp(nc, Gothic.Paper, 0.2f);
                var nr = (RectTransform)name.transform;
                if (nr.parent != root) nr.SetParent(root, false);
                nr.anchorMin = new Vector2(0, 1);
                nr.anchorMax = new Vector2(1, 1);
                nr.pivot = new Vector2(0.5f, 1);
                nr.offsetMin = new Vector2(70, -58);
                nr.offsetMax = new Vector2(-70, -24);
                name.alignment = TextAnchor.MiddleCenter;
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 12;
                name.resizeTextMaxSize = 21;
            }

            var thing = root.Find("DescriptionContainer/Wrapper/ShortDescriptionWrapper/ThingImage") ?? root.Find("ThingImage");
            if (thing != null)
            {
                Pin(thing, root, TL, new Vector2(0.5f, 1), new Vector2(92, -62), new Vector2(82, 106));
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
                var ai = arch.GetComponent<Image>();
                ai.sprite = Gothic.Sprite("icon_backing");
                ai.type = Image.Type.Simple;
                ai.raycastTarget = false;
                ai.color = Color.Lerp(Color.white, accent, 0.15f);
                ai.color = new Color(ai.color.r, ai.color.g, ai.color.b, 1f);
                Gothic.Stretch((RectTransform)arch);
                var frame = thing.Find("Frame");
                var icon = arch.Find("Icon") ?? (frame != null ? frame.Find("Icon") : null);
                if (icon != null)
                {
                    if (icon.parent != arch) icon.SetParent(arch, false);
                    var irt = (RectTransform)icon;
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                    irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.sizeDelta = new Vector2(74, 74);
                    irt.anchoredPosition = new Vector2(0, -10);
                }
                var fimg = frame != null ? frame.GetComponent<Image>() : null;
                if (fimg != null)
                {
                    frame.SetAsLastSibling();
                    fimg.sprite = Gothic.Sprite("icon_frame");
                    fimg.type = Image.Type.Simple;
                    fimg.preserveAspect = false;
                    fimg.raycastTarget = false;
                    fimg.color = Color.Lerp(Color.white, accent, 0.35f);
                    var frt = (RectTransform)frame;
                    frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
                    frt.sizeDelta = new Vector2(82, 106);
                    frt.anchoredPosition = Vector2.zero;
                }
            }

            var manaLabel = Get<Text>(FManaLabel, card);
            if (manaLabel != null && manaLabel.gameObject.activeSelf) manaLabel.gameObject.SetActive(false);
            float x = 160;
            var mana = Get<Component>(FManaValue, card);
            if (mana != null)
            {
                Pin(mana.transform, root, TL, new Vector2(0, 0.5f), new Vector2(x + 12, -86), Vector2.zero);
                Badge(mana.transform);
                foreach (var t in mana.GetComponentsInChildren<Text>(true)) Gothic.Font(t, false, Gothic.Paper);
                x += 128;
            }

            var cap = Get<Text>(FCaption, card);
            if (cap != null && cap.gameObject.activeSelf) cap.gameObject.SetActive(false);
            foreach (var f in FPrices)
            {
                var p = Get<Component>(f, card);
                if (p == null) continue;
                Pin(p.transform, root, TL, new Vector2(0, 0.5f), new Vector2(x + 12, -86), Vector2.zero);
                Badge(p.transform);
                foreach (var t in p.GetComponentsInChildren<Text>(true)) Gothic.Font(t, false, Gothic.Paper);
                if (p.gameObject.activeSelf) x += 128;
            }

            var d1 = Get<Text>(FDesc1, card);
            string phase = Phase(data.Phase);
            if (d1 != null && !string.IsNullOrEmpty(phase) && d1.text == phase) d1.gameObject.SetActive(false);
            int reqs = 0;
            foreach (var d in new[] { d1, Get<Text>(FDesc2, card), Get<Text>(FDesc3, card) })
            {
                if (d == null) continue;
                Gothic.Font(d, false, null, 16);
                if (!d.gameObject.activeSelf) continue;
                Pin(d.transform, root, TL, new Vector2(0, 1), new Vector2(160, -112 - 21 * reqs), new Vector2(400, 21));
                d.resizeTextForBestFit = false;
                d.fontSize = 16;
                d.alignment = TextAnchor.MiddleLeft;
                d.horizontalOverflow = HorizontalWrapMode.Overflow;
                d.verticalOverflow = VerticalWrapMode.Overflow;
                if (d.color.r > 0.6f && d.color.g < 0.4f) d.color = Gothic.KeyRed;
                Shade(d);
                reqs++;
            }
            PhaseIcon(root, data.Phase, phase);

            var desc = Get<Text>(FString, card);
            if (desc != null)
            {
                Gothic.Font(desc, false, new Color(0.97f, 0.93f, 0.84f), 16);
                Shade(desc);
                desc.supportRichText = true;
                desc.text = Gothic.Keywords(desc.text).TrimStart();
                var dr = (RectTransform)desc.transform;
                if (dr.parent != root) dr.SetParent(root, false);
                dr.anchorMin = new Vector2(0, 0);
                dr.anchorMax = new Vector2(1, 1);
                dr.pivot = new Vector2(0, 1);
                dr.offsetMin = new Vector2(160, 22);
                dr.offsetMax = new Vector2(-44, -(116 + 21 * reqs));
                desc.alignment = TextAnchor.UpperLeft;
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                desc.verticalOverflow = VerticalWrapMode.Truncate;
                desc.resizeTextForBestFit = true;
                desc.resizeTextMinSize = 11;
                desc.resizeTextMaxSize = 16;
            }

            var buy = root.Find("QoLBuy");
            if (buy != null) UnityEngine.Object.Destroy(buy.gameObject);
        }

        private static void Shade(Text t)
        {
            var sh = t.GetComponent<Shadow>();
            if (sh == null || sh is Outline) sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.9f);
            sh.effectDistance = new Vector2(1.2f, -1.2f);
        }

        internal static void Badge(Transform price) => Badge(price, 10);

        internal static void Badge(Transform price, float left) => Badge(price, left, -5);

        internal static void Badge(Transform price, float left, float vpad)
        {
            var b = Gothic.Layer(price, "QoLBadge", "price_badge", Image.Type.Sliced, Color.white, 0);
            b.pixelsPerUnitMultiplier = 5f;
            Gothic.Stretch(b.rectTransform, -left, -12, vpad, vpad);
            var le = b.GetComponent<LayoutElement>() ?? b.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private static string Phase(int phase)
        {
            switch (phase)
            {
                case 1: return ResourceStrings.GetString("magic.required_phase.move_phase");
                case 2: return ResourceStrings.GetString("magic.required_phase.combat_phase");
                case 3: return ResourceStrings.GetString("magic.required_phase.any_phase");
            }
            return null;
        }

        private static void PhaseIcon(Transform root, int phase, string tip)
        {
            var holder = root.Find("QoLPhase");
            if (holder == null)
            {
                var go = new GameObject("QoLPhase", typeof(RectTransform), typeof(Image), typeof(GothicTip));
                go.transform.SetParent(root, false);
                holder = go.transform;
                var rt = (RectTransform)holder;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-40, -26);
                rt.sizeDelta = new Vector2(28, 28);
                var img = go.GetComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = true;
                var second = new GameObject("second", typeof(RectTransform), typeof(Image));
                second.transform.SetParent(holder, false);
                var srt = (RectTransform)second.transform;
                srt.anchorMin = srt.anchorMax = new Vector2(0, 0.5f);
                srt.pivot = new Vector2(1, 0.5f);
                srt.anchoredPosition = new Vector2(-4, 0);
                srt.sizeDelta = new Vector2(40, 40);
                var si = second.GetComponent<Image>();
                si.preserveAspect = true;
                si.raycastTarget = false;
            }
            var main = holder.GetComponent<Image>();
            var sec = holder.Find("second").GetComponent<Image>();
            main.sprite = Gothic.Sprite(phase == 1 ? "phase_move" : "phase_combat");
            sec.sprite = Gothic.Sprite("phase_move");
            sec.gameObject.SetActive(phase == 3);
            holder.gameObject.SetActive(phase >= 1 && phase <= 3);
            holder.GetComponent<GothicTip>().text = tip;
        }
    }

    internal class GothicEarly : MonoBehaviour
    {
        private int _frames;
        private bool _styled;
        private float _start = -1, _settled = -1;
        private CanvasGroup _veil;

        private void LateUpdate()
        {
            try
            {
                if (!Gothic.On || ++_frames > 900) { Finish(); return; }
                var window = GetComponent<TradePanelContentWindow>();
                Component panel = GetComponentInChildren<MagicShopPanelContent>(true);
                if (panel == null) panel = GetComponentInChildren<ShopPanelContent>(true);
                if (window == null || panel == null) return;
                if (!_styled)
                {
                    _styled = true;
                    _veil = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
                    _veil.alpha = 0f;
                    _start = Time.unscaledTime;
                    GothicShop.Prepare(window, panel);
                }
                Component grid = panel.GetComponentInChildren<MagicShopGrid>(true);
                if (grid == null) grid = panel.GetComponentInChildren<ShopThingGrid>(true);
                if (grid != null) ShopCards.FitNow(grid);
                if (_settled < 0)
                {
                    bool ready = false;
                    foreach (var r in panel.GetComponentsInChildren<CardSize>(false))
                        if (r.gameObject.activeInHierarchy) { ready = true; break; }
                    if (ready || Time.unscaledTime - _start > 0.25f) _settled = Time.unscaledTime;
                    return;
                }
                float k = (Time.unscaledTime - _settled) / 0.06f;
                if (k < 0) return;
                _veil.alpha = Mathf.Clamp01(k);
                if (k >= 1) Finish();
            }
            catch (Exception e) { Plugin.Trace("[gothic] early style: " + e.Message); Finish(); }
        }

        private void Finish()
        {
            if (_veil != null) _veil.alpha = 1f;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_veil != null) _veil.alpha = 1f;
        }
    }

    [HarmonyPatch(typeof(MagicShopController), "BuildWindow")]
    internal static class GothicMagicWindowPatch
    {
        private static void Postfix(BasePanelContentWindow __result)
        {
            try
            {
                if (__result == null) return;
                __result.gameObject.AddComponent<GothicLift>();
                if (Gothic.On) __result.gameObject.AddComponent<GothicEarly>();
            }
            catch (Exception e) { Plugin.Trace("[gothic] magic window: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ShopController), "BuildWindow")]
    internal static class GothicShopWindowPatch
    {
        private static void Postfix(BasePanelContentWindow __result)
        {
            try
            {
                if (__result == null) return;
                __result.gameObject.AddComponent<GothicLift>();
                if (Gothic.On) __result.gameObject.AddComponent<GothicEarly>();
            }
            catch (Exception e) { Plugin.Trace("[gothic] shop window: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MarketBuyThingController), "BuildWindow")]
    internal static class GothicMarketWindowPatch
    {
        private static void Postfix(BasePanelContentWindow __result)
        {
            try
            {
                if (__result == null) return;
                __result.gameObject.AddComponent<GothicLift>();
                if (Gothic.On) __result.gameObject.AddComponent<GothicEarly>();
            }
            catch (Exception e) { Plugin.Trace("[gothic] market window: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(HintHolder), "Update")]
    internal static class GothicHintGuard
    {
        private static readonly System.Reflection.FieldInfo Enter = AccessTools.Field(typeof(HintHolder), "_mouseEnterTime");

        private static bool Prefix(HintHolder __instance)
        {
            if (GothicLift.Open <= 0 || Enter == null) return true;
            if ((float)Enter.GetValue(__instance) <= 0f) return true;
            if (__instance.GetComponentInParent<TradePanelContentWindow>() != null) return true;
            Enter.SetValue(__instance, 0f);
            return false;
        }
    }

    [HarmonyPatch(typeof(MagicShopItemRenderer), "UpdateView")]
    internal static class GothicMagicCardPatch
    {
        private static void Postfix(MagicShopItemRenderer __instance, ShopMagicContentResponseItemDTO data)
        {
            try { GothicShop.Card(__instance, data); }
            catch (Exception e) { Plugin.Trace("[gothic] magic card: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(TabButton), "SetImage")]
    internal static class GothicTabImagePatch
    {
        internal static Vector2 Center(RectTransform t)
        {
            float w = Mathf.Max(1f, t.rect.width), h = Mathf.Max(1f, t.rect.height);
            float side = Mathf.Min(w, h);
            return new Vector2((t.pivot.x * (w - side) + side * 0.5f) / w, (t.pivot.y * (h - side) + side * 0.5f) / h);
        }

        private static void Postfix(TabButton __instance)
        {
            try
            {
                if (!Gothic.On) return;
                var backing = __instance.transform.Find("QoLBacking") as RectTransform;
                if (backing == null) return;
                float side = Mathf.Min(backing.rect.width, backing.rect.height);
                if (side <= 1) side = 90;
                foreach (Transform ch in backing)
                {
                    var r = ch as RectTransform;
                    if (r == null) continue;
                    r.anchorMin = r.anchorMax = GothicTabImagePatch.Center((RectTransform)__instance.transform);
                    r.pivot = new Vector2(0.5f, 0.5f);
                    r.anchoredPosition = Vector2.zero;
                    r.sizeDelta = new Vector2(side * 0.58f, side * 0.58f);
                }
            }
            catch (Exception e) { Plugin.Trace("[gothic] tab icon: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(TabButton), "SetSelection")]
    internal static class GothicTabPatch
    {
        private static void Postfix(TabButton __instance, bool selection)
        {
            try
            {
                if (!Gothic.On || !GothicShop.InStyled(__instance)) return;
                GothicShop.Tab(__instance, selection);
            }
            catch (Exception e) { Plugin.Trace("[gothic] tab: " + e.Message); }
        }
    }
}
