using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class SpeedBar
    {
        private const float Wide = 200f;
        private const float HeadHigh = 24f;
        private const float RowHigh = 22f;
        private const float Pad = 4f;
        private const float Lift = 4f;
        private const float Edge = 9f;
        private const float Snug = 8f;
        private const float Wing = 20f;
        private const int Tall = 13;
        private const int Small = 9;

        private sealed class Row
        {
            internal int Mode;
            internal Button Press;
            internal Text Title;
        }

        private static readonly List<Row> Rows = new List<Row>();

        private static GameObject _barGo;
        private static RectTransform _bar;
        private static Button _head;
        private static Text _headText;
        private static Text _arrow;
        private static RectTransform _list;
        private static bool _open;
        private static float _leftAt;
        private static float _next;

        internal static bool Open => _open && _list != null && _list.gameObject.activeSelf;

        internal static float Height => _barGo != null && _barGo.activeSelf ? HeadHigh + Lift : 0f;

        private static bool Live => ChatDock.Active && SideButtons.InWorld()
                                    && !SideButtons.InCombat() && MoveMode.Ready;

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            try
            {
                Hover();
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.1f;

                if (!Live) { Drop(); return; }
                var area = ChatDock.Area;
                if (area == null) { Drop(); return; }
                if (_bar == null || _bar.parent != area) Build(area);
                if (_bar == null) return;

                bool locked = MoveMode.Cooling || MoveMode.Sending;
                if (locked && _open) Shut();

                string caption = Caption(locked);
                if (_headText.text != caption) _headText.text = caption;
                var paint = locked ? WardrobeLook.Accent : WardrobeLook.Bright;
                if (_headText.color != paint) _headText.color = paint;
                if (_head.interactable == locked) _head.interactable = !locked;
                if (_arrow.enabled == locked) _arrow.enabled = !locked;
                Room(locked ? Snug : Wing);

                foreach (var row in Rows)
                {
                    bool now = row.Mode == MoveMode.Current;
                    string title = (now ? "• " : "   ") + MoveMode.Name(row.Mode);
                    if (row.Title.text != title) row.Title.text = title;
                    var tint = now ? WardrobeLook.Good : WardrobeLook.Bright;
                    if (row.Title.color != tint) row.Title.color = tint;
                    if (row.Press.interactable == now) row.Press.interactable = !now;
                }

                Place();
                if (!_barGo.activeSelf) _barGo.SetActive(true);
                bool show = _open && !locked;
                if (_list.gameObject.activeSelf != show) _list.gameObject.SetActive(show);
            }
            catch (Exception e) { Plugin.Trace("[скорость] полоса: " + e.Message); }
        }

        private static string Caption(bool locked)
        {
            if (MoveMode.Sending) return "Скорость: " + MoveMode.Name(MoveMode.Wanted) + " — перехожу…";
            if (locked) return "Скорость: " + MoveMode.Name(MoveMode.Current) + " — " + MoveMode.Clock(MoveMode.Left);
            string said = MoveMode.Status;
            if (said.Length > 0 && Time.unscaledTime - MoveMode.StatusAt < 3f) return said;
            return "Скорость: " + MoveMode.Name(MoveMode.Current);
        }

        private static void Toggle()
        {
            if (MoveMode.Cooling || MoveMode.Sending) return;
            _open = !_open;
            _leftAt = 0f;
            if (!_open) Shut();
        }

        private static void Shut()
        {
            _open = false;
            _leftAt = 0f;
            if (_list != null && _list.gameObject.activeSelf) _list.gameObject.SetActive(false);
        }

        private static void Drop()
        {
            Shut();
            if (_barGo != null && _barGo.activeSelf) _barGo.SetActive(false);
        }

        private static void Hover()
        {
            if (!Open) return;
            var eye = Eye();
            if (Inside(_list, eye) || Inside(_bar, eye)) { _leftAt = 0f; return; }
            if (_leftAt <= 0f) { _leftAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _leftAt > 0.5f) Shut();
        }

        private static bool Inside(RectTransform rect, Camera eye)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eye);
        }

        private static Camera Eye()
        {
            var canvas = _bar != null ? _bar.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private static void Place()
        {
            _bar.anchorMin = _bar.anchorMax = new Vector2(0.5f, 0f);
            _bar.pivot = new Vector2(1f, 0f);
            var size = new Vector2(Wide, HeadHigh);
            if (_bar.sizeDelta != size) _bar.sizeDelta = size;
            var spot = new Vector2(ChatDock.RightEdge, ChatDock.PanelHeight + Lift);
            if (_bar.anchoredPosition != spot) _bar.anchoredPosition = spot;
            _barGo.transform.SetAsLastSibling();
            Ruler();
        }

        private static void Build(RectTransform area)
        {
            if (_barGo != null) UnityEngine.Object.Destroy(_barGo);
            Rows.Clear();

            _barGo = new GameObject("QoLSpeedBar", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            Curtain.Stage(_barGo);
            _barGo.transform.SetParent(area, false);
            _bar = (RectTransform)_barGo.transform;
            _bar.localScale = Vector3.one;

            var back = _barGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;

            var edge = _barGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            _head = _barGo.GetComponent<Button>();
            _head.targetGraphic = back;
            var colors = _head.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
            colors.pressedColor = new Color(1.6f, 1.45f, 1.2f, 1f);
            colors.disabledColor = new Color(0.75f, 0.72f, 0.68f, 1f);
            colors.fadeDuration = 0.05f;
            _head.colors = colors;
            _head.onClick.AddListener(Toggle);

            var font = SideButtons.GameFont();

            _headText = Label(_bar, font, Tall, TextAnchor.MiddleLeft);
            _headText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _headText.verticalOverflow = VerticalWrapMode.Truncate;
            _headText.resizeTextForBestFit = true;
            _headText.resizeTextMinSize = Small;
            _headText.resizeTextMaxSize = Tall;
            Stretch(_headText.rectTransform, Edge, Wing);

            _arrow = Label(_bar, font, 11, TextAnchor.MiddleRight);
            Stretch(_arrow.rectTransform, 9f, 7f);
            _arrow.text = "▲";
            _arrow.color = WardrobeLook.Label;

            var listGo = new GameObject("list", typeof(RectTransform), typeof(Image), typeof(Outline));
            listGo.transform.SetParent(_bar, false);
            _list = (RectTransform)listGo.transform;
            _list.anchorMin = _list.anchorMax = new Vector2(0f, 0f);
            _list.pivot = new Vector2(0f, 0f);
            _list.localScale = Vector3.one;
            _list.sizeDelta = new Vector2(Wide, MoveMode.All.Length * RowHigh + Pad * 2f);
            _list.anchoredPosition = new Vector2(0f, HeadHigh + 3f);

            var sheet = listGo.GetComponent<Image>();
            sheet.color = WardrobeLook.Popup;
            sheet.sprite = OnlineWindow.Rounded(8);
            sheet.type = Image.Type.Sliced;
            sheet.raycastTarget = true;

            var lip = listGo.GetComponent<Outline>();
            lip.effectColor = WardrobeLook.Edge;
            lip.effectDistance = new Vector2(1f, -1f);

            for (int i = 0; i < MoveMode.All.Length; i++)
            {
                int mode = MoveMode.All[MoveMode.All.Length - 1 - i];
                var rowGo = new GameObject("mode" + mode, typeof(RectTransform), typeof(Image), typeof(Button));
                rowGo.transform.SetParent(_list, false);
                var rt = (RectTransform)rowGo.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(Pad, 0f);
                rt.offsetMax = new Vector2(-Pad, 0f);
                rt.sizeDelta = new Vector2(-Pad * 2f, RowHigh);
                rt.anchoredPosition = new Vector2(0f, -Pad - i * RowHigh);

                var fill = rowGo.GetComponent<Image>();
                fill.color = Color.white;
                fill.raycastTarget = true;

                var press = rowGo.GetComponent<Button>();
                press.targetGraphic = fill;
                var tint = press.colors;
                tint.normalColor = new Color(1f, 1f, 1f, 0.04f);
                tint.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                tint.pressedColor = new Color(1f, 1f, 1f, 0.14f);
                tint.selectedColor = new Color(1f, 1f, 1f, 0.04f);
                tint.disabledColor = new Color(1f, 1f, 1f, 0.02f);
                tint.fadeDuration = 0.05f;
                press.colors = tint;
                int picked = mode;
                press.onClick.AddListener(() =>
                {
                    MoveMode.Send(picked);
                    Shut();
                });

                var title = Label(rt, font, 13, TextAnchor.MiddleLeft);
                Stretch(title.rectTransform, 8f, 8f);
                title.text = MoveMode.Name(mode);

                Rows.Add(new Row { Mode = mode, Press = press, Title = title });
            }

            listGo.SetActive(false);
            Plugin.Trace("[скорость] полоса над чатом собрана");
        }

        private static bool _ruled;

        private static void Ruler()
        {
            if (_ruled || Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            var home = ChatDock.Root;
            if (home == null || _bar == null) return;
            _ruled = true;
            var mine = new Vector3[4];
            var his = new Vector3[4];
            _bar.GetWorldCorners(mine);
            home.GetWorldCorners(his);
            Plugin.Trace("[скорость] полоса x " + mine[0].x.ToString("0.#") + "…" + mine[2].x.ToString("0.#")
                + " | чат x " + his[0].x.ToString("0.#") + "…" + his[2].x.ToString("0.#")
                + " | справа расходятся на " + (mine[2].x - his[2].x).ToString("0.##"));
        }

        private static void Room(float right)
        {
            if (_headText == null) return;
            var rt = _headText.rectTransform;
            if (Mathf.Abs(rt.offsetMax.x + right) < 0.5f) return;
            rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
        }

        private static void Stretch(RectTransform rt, float left, float right)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, 0f);
            rt.offsetMax = new Vector2(-right, 0f);
        }

        private static Text Label(RectTransform host, Font font, int size, TextAnchor how)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(host, false);
            var label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.alignment = how;
            label.color = WardrobeLook.Bright;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.95f);
            edge.effectDistance = new Vector2(1.3f, -1.3f);
            return label;
        }
    }
}
