using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class GothicHint
    {
        internal static void Skin(Component dialog)
        {
            if (!Gothic.On || dialog == null) return;
            var root = Inner(dialog.transform);
            if (root == null) return;
            if (root.GetComponent<GothicMark>() == null) root.gameObject.AddComponent<GothicMark>();

            var back = Gothic.Layer(root, "QoLBack", "card", Image.Type.Sliced, new Color(0.82f, 0.8f, 0.8f), 0);
            back.pixelsPerUnitMultiplier = 1.4f;
            var ble = back.GetComponent<LayoutElement>() ?? back.gameObject.AddComponent<LayoutElement>();
            ble.ignoreLayout = true;
            Gothic.Stretch(back.rectTransform, -22, -22, 18, -22);
            var own = root.GetComponent<Image>();
            if (own != null) own.color = new Color(own.color.r, own.color.g, own.color.b, 0f);

            var caption = root.Find("CaptionWrapper");
            if (caption != null)
            {
                var ci = caption.GetComponent<Image>();
                if (ci != null)
                {
                    ci.sprite = Gothic.Sprite("title_plaque");
                    ci.type = Image.Type.Sliced;
                    ci.pixelsPerUnitMultiplier = 3.2f;
                    ci.color = Color.white;
                }
                foreach (var t in caption.GetComponentsInChildren<Text>(true))
                {
                    Gothic.Font(t, true);
                    t.color = Light(t.color, Gothic.Paper);
                    int max = Mathf.Max(t.fontSize, t.resizeTextMaxSize);
                    var fit = t.GetComponent<GothicFitLine>() ?? t.gameObject.AddComponent<GothicFitLine>();
                    fit.max = Mathf.Max(max, 20);
                }
            }

            var container = root.Find("HintContainer") ?? root.Find("RowContainer");
            if (container != null)
            {
                var hc = container.GetComponent<Image>();
                if (hc != null) hc.color = new Color(0f, 0f, 0f, 0f);
                bool rows = container.name == "RowContainer";
                foreach (var img in container.GetComponentsInChildren<Image>(true))
                {
                    if (img.transform == container) continue;
                    if (rows && img.GetComponentInParent<MarketProposalListItemRowItemRenderer>() != null) continue;
                    if (Under(img.transform, "DurabilityIndicator")) continue;
                    if (img.GetComponentInParent<DialogPrice>() != null || img.GetComponent<Button>() != null) continue;
                    if (img.transform.parent != null && img.transform.parent.GetComponent<Button>() != null) continue;
                    var n = img.name;
                    if (n.EndsWith("Wrapper") || n.EndsWith("Fon") || n.EndsWith("Panel") || n.EndsWith("Block") || n == "HintContainer")
                        img.color = new Color(0f, 0f, 0f, n == "GradientFon" || n == "HintContainer" || n == "RowContainer" ? 0f : 0.22f);
                }
            }

            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (caption != null && t.transform.IsChildOf(caption)) continue;
                if (t.GetComponentInParent<MarketProposalListItemRowItemRenderer>() != null) continue;
                Gothic.Font(t, false);
                t.color = Light(t.color, Gothic.Paper);
                t.supportRichText = true;
                t.text = Gothic.Keywords(t.text);
                Shade(t);
                bool inPrice = t.GetComponentInParent<DialogPrice>() != null;
                if (t.GetComponentInParent<Button>() == null)
                {
                    int want = inPrice ? 18 : Mathf.Clamp(t.fontSize + 5, 19, 20);
                    t.resizeTextForBestFit = false;
                    if (t.fontSize < want) t.fontSize = want;
                    t.verticalOverflow = VerticalWrapMode.Overflow;
                    if (!inPrice && t.name != "Description" && t.rectTransform.rect.height < 24 && root.Find("RowContainer") == null) Grow(t.transform, 28);
                }
                if (t.name == "Description")
                {
                    t.supportRichText = true;
                    t.color = new Color(0.97f, 0.93f, 0.84f);
                    t.text = Gothic.Keywords(t.text);
                }
            }

            foreach (var name in new[] { "ImageWrapper", "ThingImageWrapper" })
            {
                var w = root.Find(name);
                if (w == null) continue;
                var wrt = (RectTransform)w;
                wrt.anchorMin = wrt.anchorMax = new Vector2(0, 1);
                wrt.anchoredPosition = new Vector2(46, -38);
                wrt.localScale = new Vector3(0.7f, 0.7f, 1f);
                var wi = w.GetComponent<Image>();
                if (wi != null) wi.color = new Color(0, 0, 0, 0);
                var arch = w.Find("QoLArch");
                if (arch == null)
                {
                    var ag = new GameObject("QoLArch", typeof(RectTransform), typeof(Image), typeof(Mask));
                    ag.transform.SetParent(w, false);
                    arch = ag.transform;
                    ag.GetComponent<Mask>().showMaskGraphic = true;
                }
                arch.SetSiblingIndex(0);
                var art = (RectTransform)arch;
                art.anchorMin = art.anchorMax = new Vector2(0.5f, 0.5f);
                art.sizeDelta = new Vector2(76, 98);
                art.anchoredPosition = new Vector2(0, -4);
                var ai = arch.GetComponent<Image>();
                ai.sprite = Gothic.Sprite("icon_backing");
                ai.raycastTarget = false;
                ai.color = Color.white;
                var icon = arch.Find("ThingImage") ?? w.Find("ThingImage");
                if (icon != null)
                {
                    if (icon.parent != arch) icon.SetParent(arch, false);
                    var irt = (RectTransform)icon;
                    irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                    irt.pivot = new Vector2(0.5f, 0.5f);
                    irt.sizeDelta = new Vector2(64, 64);
                    irt.anchoredPosition = new Vector2(0, -8);
                    var ii = icon.GetComponent<Image>();
                    if (ii != null) ii.preserveAspect = true;
                }
                var frame = w.Find("ActionImageFrame");
                var fi = frame != null ? frame.GetComponent<Image>() : null;
                if (fi != null)
                {
                    frame.SetAsLastSibling();
                    fi.sprite = Gothic.Sprite("icon_frame");
                    fi.type = Image.Type.Simple;
                    fi.preserveAspect = false;
                    fi.raycastTarget = false;
                    fi.color = Color.white;
                    var frt = (RectTransform)frame;
                    frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 0.5f);
                    frt.sizeDelta = new Vector2(80, 104);
                    frt.anchoredPosition = new Vector2(0, -4);
                }
            }

            var close = root.Find("CloseButton");
            if (close != null)
            {
                foreach (var an in close.GetComponentsInChildren<Animator>(true)) an.enabled = false;
                Gothic.Close(close.GetComponent<Button>());
            }

            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (close != null && b.transform == close) continue;
                var n = b.name;
                if (n == "DecrementButton" || n == "IncrementButton")
                {
                    var bi = b.GetComponent<Image>();
                    if (bi != null) { bi.sprite = Gothic.Sprite("price_badge"); bi.type = Image.Type.Sliced; bi.pixelsPerUnitMultiplier = 6f; bi.color = Color.white; }
                    b.transition = Selectable.Transition.ColorTint;
                    var bt = b.GetComponentInChildren<Text>(true);
                    Gothic.Font(bt, false, Gothic.Paper);
                    if (bt != null)
                    {
                        var r = bt.rectTransform;
                        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
                        r.offsetMin = new Vector2(0, -6); r.offsetMax = new Vector2(0, 0);
                        bt.alignment = TextAnchor.MiddleCenter;
                        bt.resizeTextForBestFit = false;
                        bt.fontSize = 24;
                        bt.verticalOverflow = VerticalWrapMode.Overflow;
                    }
                    continue;
                }
                if (n == "RepairButton") continue;
                Gothic.Button(b);
            }

            var input = root.GetComponentInChildren<InputField>(true);
            if (input != null)
            {
                var ii = input.GetComponent<Image>();
                if (ii != null) { ii.sprite = Gothic.Sprite("price_badge"); ii.type = Image.Type.Sliced; ii.pixelsPerUnitMultiplier = 6f; ii.color = Color.white; }
                if (input.textComponent != null) Gothic.Font(input.textComponent, false, Gothic.Paper);
            }

            var dur = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "DurabilityIndicator") as RectTransform;
            if (dur != null)
            {
                dur.sizeDelta = new Vector2(dur.sizeDelta.x, 30);
                dur.anchoredPosition = new Vector2(dur.anchoredPosition.x, -42);
                var dw = dur.parent as RectTransform;
                if (dw != null && dw.sizeDelta.y < 64) dw.sizeDelta = new Vector2(dw.sizeDelta.x, 64);
                if (dw != null) Grow(dw.parent, 74);
                var fill = dur.GetComponentsInChildren<Image>(true).FirstOrDefault(x => x.name == "IndicatorPanel");
                if (fill != null) fill.color = new Color(0.62f, 0.12f, 0.16f, 1f);
                var dv = dur.GetComponentsInChildren<Text>(true).FirstOrDefault();
                if (dv != null) { dv.fontSize = 18; dv.alignment = TextAnchor.MiddleCenter; }
            }

            var qc = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "QuantityAndCostWrapper");
            if (qc != null)
            {
                foreach (var g in qc.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
                {
                    var a = g.childAlignment;
                    g.childAlignment = a == TextAnchor.LowerLeft || a == TextAnchor.LowerRight || a == TextAnchor.LowerCenter ? TextAnchor.LowerCenter
                        : a == TextAnchor.MiddleLeft || a == TextAnchor.MiddleRight || a == TextAnchor.MiddleCenter ? TextAnchor.MiddleCenter : TextAnchor.UpperCenter;
                }
                var top = qc.GetComponent<HorizontalLayoutGroup>();
                if (top != null)
                {
                    top.childForceExpandWidth = true;
                    top.childControlWidth = true;
                    top.padding.left = top.padding.right = Mathf.Max(top.padding.left, top.padding.right);
                    foreach (Transform ch in qc)
                    {
                        if (!ch.gameObject.activeSelf) continue;
                        var le = ch.GetComponent<LayoutElement>() ?? ch.gameObject.AddComponent<LayoutElement>();
                        le.flexibleWidth = 1;
                        le.preferredWidth = -1;
                    }
                }
                foreach (var t in qc.GetComponentsInChildren<Text>(true))
                    if (t.GetComponentInParent<Button>() == null && t.GetComponentInParent<InputField>() == null && t.GetComponentInParent<DialogPrice>() == null)
                        t.alignment = TextAnchor.MiddleCenter;
            }

            Prices(root);
        }

        private static bool Under(Transform t, string name)
        {
            for (var c = t; c != null; c = c.parent) if (c.name == name) return true;
            return false;
        }

        private static Transform Inner(Transform t)
        {
            if (t.Find("CaptionWrapper") != null) return t;
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.Find("CaptionWrapper") != null && (c.Find("HintContainer") != null || c.Find("RowContainer") != null)) return c;
            return null;
        }

        internal static void Prices(Transform root)
        {
            foreach (var p in root.GetComponentsInChildren<DialogPrice>(true))
            {
                var le = p.GetComponent<LayoutElement>() ?? p.gameObject.AddComponent<LayoutElement>();
                le.minWidth = Mathf.Max(le.minWidth, 96);
                le.preferredWidth = Mathf.Max(le.preferredWidth, 96);
                le.minHeight = Mathf.Max(le.minHeight, 30);
                var prt = (RectTransform)p.transform;
                if (prt.sizeDelta.x < 96) prt.sizeDelta = new Vector2(96, Mathf.Max(prt.sizeDelta.y, 30));
                GothicShop.Badge(p.transform, 0);
                var row = p.transform.parent != null ? p.transform.parent.GetComponent<HorizontalOrVerticalLayoutGroup>() : null;
                if (row != null && row.spacing < 18) row.spacing = 18;
                var icon = p.transform.Find("Icon");
                var value = p.transform.Find("Value");
                if (icon != null) { var r = (RectTransform)icon; r.anchorMin = r.anchorMax = new Vector2(0, 0.5f); r.pivot = new Vector2(0, 0.5f); r.anchoredPosition = new Vector2(8, 0); r.sizeDelta = new Vector2(24, 24); }
                if (value != null)
                {
                    var r = (RectTransform)value;
                    r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 0.5f);
                    r.offsetMin = new Vector2(36, 0); r.offsetMax = new Vector2(-6, 0);
                    var vt = value.GetComponent<Text>();
                    if (vt != null) { vt.alignment = TextAnchor.MiddleLeft; vt.horizontalOverflow = HorizontalWrapMode.Overflow; vt.resizeTextForBestFit = false; vt.fontSize = 18; vt.color = Gothic.Paper; }
                }
                foreach (var t in p.GetComponentsInChildren<Text>(true)) Gothic.Font(t, false, Gothic.Paper);
            }
        }

        private static void Grow(Transform t, float height)
        {
            var cur = t;
            for (int i = 0; i < 4 && cur != null && cur.parent != null; i++)
            {
                var group = cur.parent.GetComponent<VerticalLayoutGroup>();
                if (group != null)
                {
                    var le = cur.GetComponent<LayoutElement>() ?? cur.gameObject.AddComponent<LayoutElement>();
                    if (le.minHeight < height) le.minHeight = height;
                    if (le.preferredHeight < height) le.preferredHeight = height;
                    var rt = (RectTransform)cur;
                    if (rt.sizeDelta.y < height) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
                    return;
                }
                cur = cur.parent;
            }
        }

        private static void Shade(Text t)
        {
            var sh = t.GetComponent<Shadow>();
            if (sh == null) sh = t.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.95f);
            sh.effectDistance = new Vector2(1.4f, -1.4f);
        }

        private static Color Light(Color c, Color fallback)
        {
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum >= 0.5f) return c;
            float sat = Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b);
            return sat > 0.25f ? Color.Lerp(c, Color.white, 0.55f) : fallback;
        }
    }

    [HarmonyPatch(typeof(ContextQuickButtonHintDialog), "InitializeDialog")]
    internal static class GothicSpellHintPatch
    {
        private static void Postfix(ContextQuickButtonHintDialog __instance)
        {
            try { GothicHint.Skin(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] spell hint: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ThingHintDialog), "InitializeDialog")]
    internal static class GothicThingHintPatch
    {
        private static void Postfix(ThingHintDialog __instance)
        {
            try { GothicHint.Skin(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] thing hint: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PrescriptionHintDialog), "InitializeDialog")]
    internal static class GothicRecipeHintPatch
    {
        private static void Postfix(PrescriptionHintDialog __instance)
        {
            try { GothicHint.Skin(__instance); }
            catch (Exception e) { Plugin.Trace("[gothic] recipe hint: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PricePanel), "UpdatePrice")]
    internal static class GothicPricePanelPatch
    {
        private static void Postfix(PricePanel __instance)
        {
            try
            {
                if (!Gothic.On) return;
                var mark = __instance.GetComponentInParent<GothicMark>();
                if (mark == null) return;
                GothicHint.Prices(__instance.transform);
            }
            catch (Exception e) { Plugin.Trace("[gothic] price panel: " + e.Message); }
        }
    }
}
