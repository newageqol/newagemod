using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SetupDialog), "Start")]
    internal static class SetupFlasks
    {
        private const string RowName = "QoLSetupFlasks";
        private const float Shade = 0.027f;
        private static readonly string[] Short = { "Жизнь", "Мана", "Энергия", "Заряды" };

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                if (__instance == null || !SideButtons.InWorld()) return;
                __instance.StartCoroutine(Place(__instance));
            }
            catch (Exception e) { Plugin.Caught("[settings] flask row: " + e.Message); }
        }

        private static IEnumerator Place(SetupDialog dialog)
        {
            for (int step = 0; step < 30; step++)
            {
                yield return null;
                if (dialog == null) yield break;
                if (Put(dialog)) yield break;
            }
            Plugin.Warn("[settings] flask row: could not place under the language list");
        }

        private static bool Put(SetupDialog dialog)
        {
            try
            {
                var box = AccessTools.Field(typeof(SetupDialog), "LanguageDropdown")?.GetValue(dialog) as Dropdown;
                var sample = Unity3DHelper.FindInChild(dialog.gameObject, "MvlReportButton");
                if (box == null || sample == null) return false;
                var drop = (RectTransform)box.transform;
                var srt = (RectTransform)sample.transform;
                float high = srt.rect.height;
                float wide = srt.rect.width > 10f ? srt.rect.width - Inset(sample, srt) : drop.rect.width;
                if (high < 10f || wide < 10f) return false;
                var home = drop.parent as RectTransform;
                if (home == null || Unity3DHelper.FindInChild(home.gameObject, RowName) != null) return true;
                bool stacked = home.GetComponent<LayoutGroup>() != null;
                float side = Mathf.Max(high, 64f);
                var row = Build(home, side, wide);
                if (stacked)
                {
                    row.SetAsLastSibling();
                    var fit = row.gameObject.AddComponent<LayoutElement>();
                    fit.minHeight = side;
                    fit.preferredHeight = side;
                    fit.flexibleHeight = 0f;
                    fit.minWidth = wide;
                    fit.preferredWidth = wide;
                    fit.flexibleWidth = 0f;
                    LayoutRebuilder.MarkLayoutForRebuild(home);
                }
                else
                {
                    row.anchorMin = drop.anchorMin;
                    row.anchorMax = drop.anchorMax;
                    row.pivot = drop.pivot;
                    row.sizeDelta = new Vector2(wide, side);
                    float below = drop.rect.height * drop.pivot.y + side * (1f - row.pivot.y);
                    int extra = Unity3DHelper.FindInChild(home.gameObject, "MaxBattleButton") != null ? 2 : 0;
                    row.anchoredPosition = drop.anchoredPosition - new Vector2(0f, below + 10f + extra * (high + 10f));
                }
                Plugin.Trace("[settings] flask row placed under the language list" + (stacked ? " (layout " + home.name + ")" : ""));
                return true;
            }
            catch (Exception e) { Plugin.Warn("[settings] flask row: " + e.Message); return true; }
        }

        private static float Inset(GameObject sample, RectTransform srt)
        {
            try
            {
                var img = sample.GetComponent<Image>();
                if (img == null || img.sprite == null) img = sample.GetComponentInChildren<Image>(true);
                if (img == null || img.sprite == null) return srt.rect.width * Shade;
                var pad = UnityEngine.Sprites.DataUtility.GetPadding(img.sprite);
                float px = pad.x + pad.z;
                if (px <= 0f) return srt.rect.width * Shade;
                float unit;
                if (img.type == Image.Type.Sliced || img.type == Image.Type.Tiled)
                {
                    float per = img.pixelsPerUnit * img.pixelsPerUnitMultiplier;
                    unit = per > 0.001f ? 1f / per : 0f;
                }
                else unit = img.sprite.rect.width > 1f ? ((RectTransform)img.transform).rect.width / img.sprite.rect.width : 0f;
                float inset = Mathf.Clamp(px * unit, 0f, srt.rect.width * 0.2f);
                Plugin.Trace("[settings] flask row: button picture padding " + px.ToString("0") + " px = " + inset.ToString("0.0") + " of width " + srt.rect.width.ToString("0"));
                return inset;
            }
            catch { return 0f; }
        }

        private static RectTransform Build(RectTransform home, float side, float wide)
        {
            var go = new GameObject(RowName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(home, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(wide, side);
            var across = go.GetComponent<HorizontalLayoutGroup>();
            float cell = Mathf.Min(side * 1.2f, (wide - 3f * 6f) / Flasks.Rows);
            across.spacing = Mathf.Max(6f, (wide - cell * Flasks.Rows) / (Flasks.Rows - 1));
            across.childAlignment = TextAnchor.MiddleCenter;
            across.childControlWidth = false;
            across.childControlHeight = false;
            across.childForceExpandWidth = false;
            across.childForceExpandHeight = false;
            var view = go.AddComponent<SetupFlaskView>();
            for (int i = 0; i < Flasks.Rows; i++) view.Add(Cell(rt, i, side, cell));
            return rt;
        }

        private static SetupFlaskView.Slot Cell(RectTransform strip, int index, float side, float cell)
        {
            var go = new GameObject("QoLSetupFlask" + index, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(strip, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(cell, side);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Button;
            WardrobeLook.Frame(go, WardrobeLook.Edge);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(rt, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            float pad = side * 0.1f;
            irt.offsetMin = new Vector2(pad, pad);
            irt.offsetMax = new Vector2(-pad, -pad);
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var title = Label(rt, "title", Mathf.Max(10, Mathf.RoundToInt(side * 0.2f)), TextAnchor.UpperCenter, WardrobeLook.Bright);
            title.text = Lang.T(Short[index]);
            var count = Label(rt, "count", Mathf.Max(11, Mathf.RoundToInt(side * 0.27f)), TextAnchor.LowerRight, Color.white);
            count.fontStyle = FontStyle.Bold;
            count.horizontalOverflow = HorizontalWrapMode.Wrap;
            count.verticalOverflow = VerticalWrapMode.Truncate;
            count.resizeTextForBestFit = true;
            count.resizeTextMinSize = 8;
            count.resizeTextMaxSize = count.fontSize;
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 8;
            title.resizeTextMaxSize = title.fontSize;
            var empty = Label(rt, "empty", Mathf.RoundToInt(side * 0.45f), TextAnchor.MiddleCenter, WardrobeLook.Faint);
            empty.text = "+";

            int row = index;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            button.onClick.AddListener(() => Press(row, rt));
            var trigger = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(data =>
            {
                var click = data as PointerEventData;
                if (click != null && click.button == PointerEventData.InputButton.Right) FlaskPicker.Near(row, rt);
            });
            trigger.triggers.Add(entry);

            return new SetupFlaskView.Slot { Row = row, Back = back, Icon = icon, Count = count, Empty = empty };
        }

        private static Text Label(RectTransform host, string name, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(3f, 2f);
            rt.offsetMax = new Vector2(-3f, -2f);
            var t = go.GetComponent<Text>();
            t.font = SideButtons.GameFont();
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = go.GetComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.95f);
            o.effectDistance = new Vector2(1.2f, -1.2f);
            return t;
        }

        private static void Press(int row, RectTransform at)
        {
            if (!Flasks.Shown(row)) { FlaskPicker.Near(row, at); return; }
            if (SideButtons.InCombat()) { Notice.Show(Lang.T("В бою эти банки не пьются."), 4f); return; }
            if (Flasks.Busy(row)) return;
            Flasks.Use(row, (used, ranOut) =>
            {
                if (ranOut) Notice.Show(Lang.F("{0}: банки кончились.", Flasks.Title(row)), 5f);
                else if (Flasks.StatusRow == row && !string.IsNullOrEmpty(Flasks.Status)) Notice.Show(Flasks.Status, 4f);
            });
        }
    }

    internal sealed class SetupFlaskView : MonoBehaviour
    {
        internal sealed class Slot
        {
            internal int Row;
            internal Image Back, Icon;
            internal Text Count, Empty;
        }

        private readonly System.Collections.Generic.List<Slot> _slots = new System.Collections.Generic.List<Slot>();
        private float _next;

        internal void Add(Slot slot) { if (slot != null) _slots.Add(slot); }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            try
            {
                foreach (var s in _slots)
                {
                    bool shown = Flasks.Shown(s.Row);
                    var sprite = shown ? Flasks.Icon(s.Row) : null;
                    if (s.Icon.sprite != sprite) s.Icon.sprite = sprite;
                    bool drawn = sprite != null && sprite.texture != null;
                    if (s.Icon.enabled != drawn) s.Icon.enabled = drawn;
                    var tint = Flasks.Busy(s.Row) ? new Color(0.55f, 0.55f, 0.55f, 1f) : Color.white;
                    if (s.Icon.color != tint) s.Icon.color = tint;
                    if (s.Empty.gameObject.activeSelf == shown) s.Empty.gameObject.SetActive(!shown);
                    string badge = shown ? (Flasks.Busy(s.Row) ? "…" : Flasks.Badge(s.Row)) : "";
                    if (s.Count.text != badge) s.Count.text = badge;
                }
            }
            catch (Exception e) { Plugin.Caught("[settings] flask row: " + e.Message); }
        }
    }
}
