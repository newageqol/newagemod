using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class TravelMenu
    {
        private const float RowHeight = 30f;
        private const float Width = 270f;
        private const float Mini = 24f;
        private const float Pad = 6f;

        private static RectTransform _root;
        private static readonly List<Text> _labels = new List<Text>();
        private static readonly List<Spot> _shown = new List<Spot>();
        private static bool _open;
        private static float _leftAt;
        private static Camera _eye;
        private static bool _eyeKnown;
        private static readonly List<RectTransform> _rows = new List<RectTransform>();
        private static int _dragFrom = -1;
        private static int _dragTo = -1;
        private static RectTransform _marker;

        internal static bool Dragging => _dragFrom >= 0;

        internal static bool Open => _open && _root != null && _root.gameObject.activeSelf;

        internal static void Toggle()
        {
            _open = !_open;
            _leftAt = 0f;
            if (!_open) Hide();
        }

        internal static void Hide()
        {
            DragStop();
            _open = false;
            _leftAt = 0f;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        internal static void Hover()
        {
            if (!_open || _root == null || !_root.gameObject.activeSelf) return;

            var cam = Eye();
            if (Dragging || Inside(_root, cam) || Inside(SideButtons.ButtonRect("QoLTravelButton"), cam))
            {
                _leftAt = 0f;
                return;
            }
            if (_leftAt <= 0f) { _leftAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - _leftAt > 0.4f) Hide();
        }

        private static bool Inside(RectTransform rect, Camera eye)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, eye);
        }

        private static Camera Eye()
        {
            if (_eyeKnown) return _eye;
            _eyeKnown = true;
            _eye = null;
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            if (canvas == null) return null;
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            _eye = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            return _eye;
        }

        internal static void Tick(bool ready)
        {
            try
            {
                Travel.Watch();
                if (!ready || !_open || !Travel.Enabled || SideButtons.ClaimLocked()) { Hide(); return; }

                var spots = Travel.Spots();
                if (!Same(spots)) Build(spots);
                if (_root == null) { _open = false; return; }

                for (int i = 0; i < _shown.Count && i < _labels.Count; i++)
                {
                    string text = Travel.IsRunning(_shown[i]) ? "▶ " + _shown[i].Name : _shown[i].Name;
                    if (_labels[i].text != text) _labels[i].text = text;
                }

                Place();
                if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            }
            catch (System.Exception e)
            {
                Plugin.Fault("[travel] список точек: " + e.Message);
                _open = false;
            }
        }

        private static bool Same(List<Spot> spots)
        {
            if (_root == null || _shown.Count != spots.Count) return false;
            for (int i = 0; i < spots.Count; i++)
                if (!ReferenceEquals(_shown[i], spots[i])) return false;
            return true;
        }

        private static void Place()
        {
            var panel = SideButtons.PanelRect;
            if (panel == null || _root == null) return;

            bool left = SideButtons.AtRightSide;
            _root.pivot = new Vector2(left ? 1f : 0f, 0.5f);
            float half = panel.sizeDelta.x * 0.5f;

            float wantY = SideButtons.RowOf("QoLTravelButton");
            var canvas = panel.parent as RectTransform;
            if (canvas != null && canvas.rect.height > 1f)
            {
                Vector2 center = canvas.InverseTransformPoint(panel.TransformPoint(panel.rect.center));
                float limit = Mathf.Max(0f, canvas.rect.height * 0.5f - _root.sizeDelta.y * 0.5f);
                wantY = Mathf.Clamp(center.y + wantY, -limit, limit) - center.y;
            }
            _root.anchoredPosition = new Vector2(left ? -(half + 8f) : half + 8f, wantY);
        }

        private static void Build(List<Spot> spots)
        {
            var panel = SideButtons.PanelRect;
            if (panel == null) return;
            if (_root != null)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }
            _eye = null;
            _eyeKnown = false;
            _labels.Clear();
            _rows.Clear();
            _marker = null;
            _dragFrom = -1;
            _shown.Clear();
            _shown.AddRange(spots);

            var go = new GameObject("QoLTravelMenu", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.localScale = Vector3.one;
            _root.sizeDelta = new Vector2(Width, (spots.Count + 1) * RowHeight + Pad * 2f);

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;
            WardrobeLook.Frame(go, WardrobeLook.Edge);

            var font = SideButtons.GameFont();
            for (int i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                var row = new GameObject("row" + i, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TravelRowDrag));
                row.GetComponent<TravelRowDrag>().Index = i;
                row.transform.SetParent(_root, false);
                var rt = (RectTransform)row.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(Pad, 0f);
                rt.offsetMax = new Vector2(-Pad, 0f);
                rt.sizeDelta = new Vector2(-Pad * 2f, RowHeight);
                rt.anchoredPosition = new Vector2(0f, -Pad - i * RowHeight);
                _rows.Add(rt);

                var fill = row.GetComponent<Image>();
                fill.color = Color.white;
                fill.raycastTarget = true;

                var button = row.GetComponent<Button>();
                button.targetGraphic = fill;
                var colors = button.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.18f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.05f);
                colors.fadeDuration = 0.05f;
                button.colors = colors;
                button.onClick.AddListener(() =>
                {
                    if (Dragging) return;
                    Travel.Click(spot);
                    Hide();
                });

                Small(rt, "✎", -(Mini * 2f + 6f), false, () => { Hide(); Travel.Rename(spot); });
                Small(rt, "×", -2f, true, () => Travel.Remove(spot));

                var textGo = new GameObject("text", typeof(RectTransform), typeof(Text));
                textGo.transform.SetParent(rt, false);
                var trt = (RectTransform)textGo.transform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(8f, 0f);
                trt.offsetMax = new Vector2(-(Mini * 2f + 12f), 0f);

                var label = textGo.GetComponent<Text>();
                label.font = font;
                label.fontSize = 19;
                label.alignment = TextAnchor.MiddleLeft;
                label.color = WardrobeLook.Bright;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 12;
                label.resizeTextMaxSize = 19;
                label.text = spot.Name;

                _labels.Add(label);
            }
            AddRow(spots.Count, font);
        }

        internal static void DragStart(int index)
        {
            if (_root == null || index < 0 || index >= _rows.Count || _rows[index] == null) return;
            _dragFrom = index;
            _dragTo = index;
            _rows[index].SetAsLastSibling();
            var go = new GameObject("marker", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, false);
            _marker = (RectTransform)go.transform;
            _marker.anchorMin = new Vector2(0f, 1f);
            _marker.anchorMax = new Vector2(1f, 1f);
            _marker.pivot = new Vector2(0.5f, 0.5f);
            _marker.sizeDelta = new Vector2(-Pad * 2f, 3f);
            var line = go.GetComponent<Image>();
            line.color = WardrobeLook.Accent;
            line.raycastTarget = false;
            Mark();
        }

        internal static void DragMove(PointerEventData data)
        {
            if (!Dragging || _root == null || _dragFrom >= _rows.Count || _rows[_dragFrom] == null) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, data.position, data.pressEventCamera, out local)) return;
            float fromTop = _root.rect.yMax - local.y - Pad;
            int count = _rows.Count;
            _dragTo = Mathf.Clamp(Mathf.FloorToInt(fromTop / RowHeight), 0, count - 1);
            float y = Mathf.Clamp(-(fromTop - RowHeight * 0.5f), -Pad - (count - 1) * RowHeight, -Pad);
            _rows[_dragFrom].anchoredPosition = new Vector2(0f, y);
            Mark();
        }

        internal static void DragEnd()
        {
            if (!Dragging) return;
            int from = _dragFrom;
            int to = _dragTo;
            var spot = from >= 0 && from < _shown.Count ? _shown[from] : null;
            DragStop();
            if (spot != null && to != from) Travel.Move(spot, to);
            else if (_root != null && from >= 0 && from < _rows.Count && _rows[from] != null)
                _rows[from].anchoredPosition = new Vector2(0f, -Pad - from * RowHeight);
        }

        private static void DragStop()
        {
            _dragFrom = -1;
            _dragTo = -1;
            if (_marker != null) Object.Destroy(_marker.gameObject);
            _marker = null;
        }

        private static void Mark()
        {
            if (_marker == null) return;
            int slot = _dragTo > _dragFrom ? _dragTo + 1 : _dragTo;
            _marker.anchoredPosition = new Vector2(0f, -Pad - slot * RowHeight);
            _marker.gameObject.SetActive(_dragTo != _dragFrom);
            _marker.SetAsLastSibling();
        }

        private static void AddRow(int index, Font font)
        {
            var row = new GameObject("add", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(_root, false);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-Pad * 2f, RowHeight);
            rt.anchoredPosition = new Vector2(0f, -Pad - index * RowHeight);
            var fill = row.GetComponent<Image>();
            fill.color = Color.white;
            var button = row.GetComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.02f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.18f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.02f);
            colors.fadeDuration = 0.05f;
            button.colors = colors;
            button.onClick.AddListener(() => { Hide(); Travel.AddHere(); });

            var label = OnlineWindow.Label(rt, "+ Добавить точку, где стою", 16, FontStyle.Normal, WardrobeLook.Accent);
            if (font != null) label.font = font;
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 0f), new Vector2(-8f, 0f));
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 11;
            label.resizeTextMaxSize = 16;
        }

        private static void Small(RectTransform row, string mark, float right, bool red, System.Action click)
        {
            var go = new GameObject(red ? "remove" : "rename", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(Mini, Mini);
            rt.anchoredPosition = new Vector2(right, 0f);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            back.color = red ? WardrobeLook.Danger : WardrobeLook.Button;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                try { click(); }
                catch (System.Exception e) { Plugin.Warn("[travel] кнопка точки: " + e.Message); }
            });
            var label = OnlineWindow.Label(rt, mark, 15, FontStyle.Bold, red ? WardrobeLook.DangerText : WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
        }
    }

    internal sealed class TravelRowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal int Index;

        public void OnBeginDrag(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            TravelMenu.DragStart(Index);
        }

        public void OnDrag(PointerEventData data) => TravelMenu.DragMove(data);

        public void OnEndDrag(PointerEventData data) => TravelMenu.DragEnd();

        private void OnDisable() => TravelMenu.DragEnd();
    }
}
