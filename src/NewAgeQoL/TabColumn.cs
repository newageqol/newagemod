using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class TabColumn
    {
        private const float Wide = 176f;
        private const float Least = 120f;
        private const float RowHigh = 42f;
        private const float Gap = 4f;
        private const float Pad = 5f;
        private const float Away = 6f;
        private const float Top = 64f;
        private const float Lift = 2f;
        private const float Margin = 6f;
        private const float IconSide = 32f;

        private static readonly Color Back = new Color32(18, 26, 40, 225);
        private static readonly Color Rim = new Color32(96, 120, 150, 255);
        private static readonly Color RowBack = new Color32(32, 58, 94, 255);
        private static readonly Color RowRim = new Color32(78, 108, 148, 255);
        private static readonly Color RowText = new Color32(232, 222, 196, 255);
        private static readonly Color PickBack = new Color32(170, 122, 44, 255);
        private static readonly Color PickRim = new Color32(255, 214, 120, 255);
        private static readonly Color PickText = new Color32(255, 250, 236, 255);

        private sealed class Row
        {
            internal int Index;
            internal Image Back;
            internal Outline Rim;
            internal Text Name;
        }

        private static readonly List<Row> Rows = new List<Row>();
        private static StandardContentWindowPanel _panel;
        private static TabPanel _tabs;
        private static GameObject _go;
        private static RectTransform _rt;
        private static IList<TabView> _items;
        private static int _count = -1;
        private static int _shown = -2;
        private static bool _inside;
        private static FieldInfo _tabField;

        internal static void Take(StandardContentWindowPanel panel)
        {
            if (panel == null) return;
            if (_tabField == null) _tabField = AccessTools.Field(typeof(StandardContentWindowPanel), "TabPanel");
            var tabs = _tabField != null ? _tabField.GetValue(panel) as TabPanel : null;
            if (tabs == null) return;
            if (!ReferenceEquals(_panel, panel)) Drop();
            _panel = panel;
            _tabs = tabs;
            _items = null;
            Hide();
            TabSearch.Bind(panel);
        }

        private static void Drop()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _rt = null;
            Rows.Clear();
            _items = null;
            _count = -1;
            _shown = -2;
        }

        internal static void Tick()
        {
            if (ReferenceEquals(_panel, null)) return;
            try
            {
                if (_panel == null || _tabs == null)
                {
                    Drop();
                    _panel = null;
                    _tabs = null;
                    return;
                }
                if (!_panel.gameObject.activeInHierarchy) return;

                var items = _tabs.Items;
                if (items == null || items.Count == 0) return;

                Hide();
                if (_go == null || !ReferenceEquals(items, _items) || items.Count != _count) Build(items);
                if (_go == null) return;
                Roll();
                Paint();
                Place();
                TabSearch.Tick(_panel, _tabs.transform as RectTransform);
            }
            catch (Exception e) { Plugin.Caught("[tabcolumn] " + e.Message); }
        }

        private static void Hide()
        {
            var row = _tabs.transform;
            for (int i = 0; i < row.childCount; i++)
            {
                var kid = row.GetChild(i);
                if (kid.GetComponent<TabButton>() == null && kid.GetComponent<TabScrollButton>() == null) continue;
                var fade = kid.GetComponent<CanvasGroup>();
                if (fade == null) fade = kid.gameObject.AddComponent<CanvasGroup>();
                if (fade.alpha != 0f) fade.alpha = 0f;
                if (fade.blocksRaycasts) fade.blocksRaycasts = false;
                if (fade.interactable) fade.interactable = false;
            }
        }

        private static void Build(IList<TabView> items)
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            Rows.Clear();
            _items = items;
            _count = items.Count;
            _shown = -2;

            _go = new GameObject("QoLTabColumn", typeof(RectTransform), typeof(Image), typeof(Outline),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            _go.transform.SetParent(_panel.transform, false);
            _go.GetComponent<LayoutElement>().ignoreLayout = true;
            _rt = (RectTransform)_go.transform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(1f, 1f);
            _rt.pivot = new Vector2(0f, 1f);
            _rt.localScale = Vector3.one;
            _rt.sizeDelta = new Vector2(Wide, 0f);

            var back = _go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = Back;
            var rim = _go.GetComponent<Outline>();
            rim.effectColor = Rim;
            rim.effectDistance = new Vector2(1f, -1f);

            var list = _go.GetComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
            list.spacing = Gap;
            list.childAlignment = TextAnchor.UpperCenter;
            list.childControlWidth = true;
            list.childControlHeight = true;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;

            var fit = _go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var font = SideButtons.GameFont();
            for (int i = 0; i < items.Count; i++) Rows.Add(Line(items[i], i, font));
            Plugin.Trace("[tabcolumn] built: " + items.Count + " tabs");
        }

        private static Row Line(TabView tab, int index, Font font)
        {
            var go = new GameObject("tab" + tab.Id, typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_go.transform, false);
            var size = go.GetComponent<LayoutElement>();
            size.preferredHeight = RowHigh;
            size.minHeight = RowHigh;

            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            back.color = RowBack;
            var rim = go.GetComponent<Outline>();
            rim.effectColor = RowRim;
            rim.effectDistance = new Vector2(1f, -1f);

            if (tab.Image != null)
            {
                var icon = new GameObject("icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                var pic = icon.GetComponent<Image>();
                pic.sprite = tab.Image;
                pic.preserveAspect = true;
                pic.raycastTarget = false;
                OnlineWindow.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(5f, -IconSide / 2f), new Vector2(5f + IconSide, IconSide / 2f));
            }

            var label = new GameObject("name", typeof(RectTransform), typeof(Text), typeof(Shadow));
            label.transform.SetParent(go.transform, false);
            var text = label.GetComponent<Text>();
            text.font = font;
            text.text = tab.Name ?? "";
            text.fontSize = 15;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 15;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.color = RowText;
            text.raycastTarget = false;
            label.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.7f);
            OnlineWindow.Place((RectTransform)label.transform, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f),
                new Vector2(IconSide + 11f, 2f), new Vector2(-5f, -2f));

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            press.colors = colors;
            int at = index;
            press.onClick.AddListener(() => Pick(at));

            return new Row { Index = index, Back = back, Rim = rim, Name = text };
        }

        private static void Pick(int index)
        {
            try
            {
                if (_tabs == null || _tabs.Items == null || index < 0 || index >= _tabs.Items.Count) return;
                _tabs.SelectedItemIndex = index;
                Paint();
            }
            catch (Exception e) { Plugin.Fault("[tabcolumn] pick: " + e.Message); }
        }

        private static void Roll()
        {
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 0.01f || _rt == null) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_rt, Input.mousePosition, Cam())) return;
            int now = _tabs.SelectedItemIndex;
            int next = Mathf.Clamp(now + (wheel > 0f ? -1 : 1), 0, _tabs.Items.Count - 1);
            if (next != now) Pick(next);
        }

        private static Camera Cam()
        {
            var canvas = _panel.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas : null;
            return root != null && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
        }

        private static float Drop(RectTransform panel)
        {
            var row = _tabs != null ? _tabs.transform as RectTransform : null;
            if (row == null || !row.gameObject.activeInHierarchy) return Top;
            var ends = new Vector3[4];
            row.GetWorldCorners(ends);
            float bottom = panel.InverseTransformPoint(ends[0]).y;
            float drop = panel.rect.yMax - bottom - Lift;
            return drop > 0f && drop < panel.rect.height * 0.5f ? drop : Top;
        }

        private static void Paint()
        {
            int now = _tabs.SelectedItemIndex;
            if (now == _shown) return;
            _shown = now;
            foreach (var row in Rows)
            {
                bool on = row.Index == now;
                if (row.Back != null) row.Back.color = on ? PickBack : RowBack;
                if (row.Rim != null) row.Rim.effectColor = on ? PickRim : RowRim;
                if (row.Name != null) row.Name.color = on ? PickText : RowText;
            }
        }

        private static void Place()
        {
            var panel = (RectTransform)_panel.transform;
            float span = panel.rect.width;
            if (span < 40f) return;

            var cam = Cam();
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float left = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).x;
            float right = RectTransformUtility.WorldToScreenPoint(cam, corners[2]).x;
            float perUnit = (right - left) / span;
            if (perUnit <= 0.01f) return;

            float room = (Screen.width - right) / perUnit - Away - Margin;
            bool inside = room < Least;
            float wide = inside ? Least : Mathf.Min(Wide, room);
            if (inside != _inside)
            {
                _inside = inside;
                Plugin.Trace("[tabcolumn] " + (inside ? "no room on the right, column over the panel" : "column beside the panel"));
            }

            float drop = Drop(panel);
            float top = RectTransformUtility.WorldToScreenPoint(cam, corners[1]).y;
            float tall = _rt.rect.height;
            float below = top / perUnit - drop - Margin;
            float shrink = tall > 1f && below < tall ? Mathf.Max(0.4f, below / tall) : 1f;
            if (Mathf.Abs(_rt.localScale.x - shrink) > 0.004f) _rt.localScale = new Vector3(shrink, shrink, 1f);

            var want = new Vector2(inside ? -(wide * shrink + Margin) : Away, -drop);
            if (_rt.anchoredPosition != want) _rt.anchoredPosition = want;
            if (Mathf.Abs(_rt.sizeDelta.x - wide) > 0.5f) _rt.sizeDelta = new Vector2(wide, _rt.sizeDelta.y);
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnThingTypeTabsResponse")]
    public static class TabColumnPatch
    {
        private static void Postfix(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance)
        {
            try
            {
                if (!(__instance is PanelInventoryGridContentResolver)) return;
                TabColumn.Take(__instance.CurrentPanel);
            }
            catch (Exception e) { Plugin.Fault("[tabcolumn] tabs: " + e.Message); }
        }
    }
}
