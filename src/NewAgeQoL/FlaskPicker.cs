using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FlaskPicker
    {
        private const float PanelW = 580f;
        private const float PanelH = 640f;
        private const float Cell = 168f;
        private const float Icon = 64f;
        private const float Caption = 52f;
        private const int Columns = 3;
        private const int CombatUsed = 54;
        private const float NearW = 320f;
        private const float NearRow = 44f;
        private const float NearHead = 52f;
        private const int NearRows = 7;
        private const float Gap = 8f;

        private sealed class Slot
        {
            internal GameObject Go;
            internal Image Icon;
            internal Text Caption;
            internal int ThingId;
        }

        private static Canvas _canvas;
        private static Transform _grid;
        private static ConfigEntry<string> _byName;
        private static ConfigEntry<int> _byId;
        private static Action _onPicked;
        private static int _row = -1;
        private static readonly List<Slot> _cells = new List<Slot>();
        private static readonly List<int> _shown = new List<int>();
        private static readonly List<int> _pool = new List<int>();
        private static readonly List<int> _kin = new List<int>();
        private static readonly List<int> _all = new List<int>();
        private static readonly Dictionary<int, int> _subs = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _qty = new Dictionary<int, int>();
        private static int _waiting;
        private static float _refreshAt;
        private static float _retryAt;
        private static Text _note;
        private static Text _empty;
        private static bool _near;
        private static RectTransform _anchor;
        private static RectTransform _panel;
        private static int _rowsShown = -1;
        private static int _bornFrame = -1;

        internal static void Open(ConfigEntry<string> byName, ConfigEntry<int> byId, string title, Action onPicked)
        {
            _near = false;
            _anchor = null;
            Show(byName, byId, title, onPicked);
        }

        internal static void Near(int row, RectTransform anchor)
        {
            var byId = Flasks.IdEntry(row);
            if (byId == null || anchor == null) return;
            if (_canvas != null && _near && _anchor == anchor) { Close(); return; }
            _near = true;
            _anchor = anchor;
            _bornFrame = Time.frameCount;
            string title = Flasks.Title(row);
            Show(Flasks.NameEntry(row), byId, title, () => Plugin.Trace("[банки] слот «" + title + "» сменён из списка у кнопки"));
        }

        private static void Show(ConfigEntry<string> byName, ConfigEntry<int> byId, string title, Action onPicked)
        {
            _byName = byName;
            _byId = byId;
            _onPicked = onPicked;
            _row = Flasks.RowOf(byId);
            Plugin.Trace("[банки] окно выбора открыто: «" + title + "», бой " + SideButtons.InCombat()
                         + ", в мире " + SideButtons.InWorld() + ", " + Flasks.State());
            try
            {
                Flasks.RequestScanNow();
                Flasks.RequestNames();
                _retryAt = RealTime.Now + 5f;
                Look();
                Build(title);
                Flasks.Picking = true;
                Apply(Filtered());
            }
            catch (Exception e) { Plugin.Fault("[банки] выбор: " + e.Message); Close(); }
        }

        internal static void Close()
        {
            Flasks.Picking = false;
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _grid = null;
            _note = null;
            _empty = null;
            _panel = null;
            _rowsShown = -1;
            _cells.Clear();
            _shown.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvas == null) return false;
            Close();
            return true;
        }

        internal static void Tick()
        {
            if (_canvas == null)
            {
                if (Flasks.Picking) { Flasks.Picking = false; Plugin.Trace("[банки] окно выбора исчезло вместе со сценой, снимаю признак выбора"); }
                return;
            }
            if (_near && (_anchor == null || !_anchor.gameObject.activeInHierarchy)) { Close(); return; }
            if (_near && _panel != null && Time.frameCount > _bornFrame && Missed()) { Close(); return; }
            if (Time.unscaledTime < _refreshAt) return;
            _refreshAt = Time.unscaledTime + 0.3f;
            if (_near) Place();

            Look();
            Note();
            if (!Flasks.Scanning && _waiting > 0 && RealTime.Now >= _retryAt)
            {
                _retryAt = RealTime.Now + 5f;
                Flasks.RetryNames(12);
            }
            if (Flasks.Scanning) return;
            var things = Filtered();
            if (!Same(things, _shown)) { Apply(things); return; }
            for (int i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];
                if (cell.Icon == null || !cell.Go.activeSelf) continue;
                var sprite = Flasks.IconFor(cell.ThingId);
                if (cell.Icon.sprite != sprite) cell.Icon.sprite = sprite;
            }
        }

        private static void Look()
        {
            _waiting = Flasks.Consumables(_pool, _subs, _qty);
        }

        private static bool Same(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static List<int> Filtered()
        {
            _kin.Clear();
            _all.Clear();
            for (int i = 0; i < _pool.Count; i++)
            {
                int tid = _pool[i];
                int sub;
                if (_subs.TryGetValue(tid, out sub) && sub == CombatUsed) continue;
                if (Flasks.DisplayName(tid) == null) continue;
                _all.Add(tid);
                if (_row >= 0 && Flasks.RowFor(tid) == _row) _kin.Add(tid);
            }
            bool loading = Flasks.Scanning || _waiting > 0;
            var list = _kin.Count > 0 || (_row >= 0 && loading) ? _kin : _all;
            list.Sort();
            return list;
        }

        private static void Build(string title)
        {
            Close();
            var go = new GameObject("QoLFlaskPicker", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 800;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, _near ? 0f : 0.6f);
            backGo.GetComponent<Image>().raycastTarget = !_near;
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = _near ? new Vector2(NearW, NearHead + NearRow + 24f) : new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            _panel = prt;
            var panel = panelGo.GetComponent<Image>();
            panel.color = WardrobeLook.Window;
            panel.sprite = OnlineWindow.Rounded(16);
            panel.type = Image.Type.Sliced;
            var outline = panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            float head = _near ? NearHead : 76f;
            var titleT = Line(panelGo.transform, _near ? (title ?? "") : (title ?? "") + " — выбери банку", _near ? 17 : 22, FontStyle.Bold, WardrobeLook.Bright);
            var trt = (RectTransform)titleT.transform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
            trt.offsetMin = new Vector2(12f, _near ? -32f : -46f); trt.offsetMax = new Vector2(_near ? -40f : -12f, _near ? -6f : -8f);
            titleT.alignment = TextAnchor.MiddleCenter;

            _note = Line(panelGo.transform, "", _near ? 12 : 15, FontStyle.Normal, WardrobeLook.Label);
            var nrt = (RectTransform)_note.transform;
            nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f); nrt.pivot = new Vector2(0.5f, 1f);
            nrt.offsetMin = new Vector2(12f, _near ? -50f : -74f); nrt.offsetMax = new Vector2(-12f, _near ? -32f : -48f);
            _note.alignment = TextAnchor.MiddleCenter;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(panelGo.transform, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = _near ? new Vector2(26f, 26f) : new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-6f, -6f);
            var closeImg = closeGo.GetComponent<Image>();
            closeImg.color = WardrobeLook.Danger;
            closeImg.sprite = OnlineWindow.Rounded(8);
            closeImg.type = Image.Type.Sliced;
            closeGo.GetComponent<Button>().onClick.AddListener(Close);
            var xT = Line(closeGo.transform, "X", _near ? 15 : 20, FontStyle.Bold, WardrobeLook.DangerText);
            var xrt = (RectTransform)xT.transform; xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one; xrt.offsetMin = Vector2.zero; xrt.offsetMax = Vector2.zero;
            xT.alignment = TextAnchor.MiddleCenter;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(12f, 12f); srt.offsetMax = new Vector2(-12f, -head);
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(8);
            simg.type = Image.Type.Sliced;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = new Vector2(0f, 0f); cont.offsetMax = new Vector2(0f, 0f);
            var glg = contentGo.GetComponent<GridLayoutGroup>();
            glg.cellSize = _near ? new Vector2(NearW - 36f, NearRow) : new Vector2(Cell, Icon + Caption + 12f);
            glg.spacing = _near ? new Vector2(4f, 4f) : new Vector2(8f, 8f);
            glg.padding = new RectOffset(6, 6, 6, 6);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = _near ? 1 : Columns;
            glg.childAlignment = TextAnchor.UpperLeft;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = cont;
            scroll.viewport = srt;
            _grid = contentGo.transform;
            Note();
            if (_near) Place();
        }

        private static bool Missed()
        {
            bool left = Input.GetMouseButtonDown(0);
            bool right = Input.GetMouseButtonDown(1);
            if (!left && !right) return false;
            if (RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null)) return false;
            return !(right && Over(_anchor));
        }

        private static bool Over(RectTransform rt)
        {
            if (rt == null) return false;
            var host = rt.GetComponentInParent<Canvas>();
            var cam = host != null && host.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? host.rootCanvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam);
        }

        private static void Fit(int count)
        {
            if (!_near || _panel == null) return;
            int rows = Mathf.Clamp(count, 1, NearRows);
            if (rows == _rowsShown) return;
            _rowsShown = rows;
            _panel.sizeDelta = new Vector2(NearW, NearHead + 24f + rows * NearRow + (rows - 1) * 4f);
            Place();
        }

        private static void Place()
        {
            if (_panel == null || _anchor == null || _canvas == null) return;
            var root = (RectTransform)_canvas.transform;
            var host = _anchor.GetComponentInParent<Canvas>();
            var cam = host != null && host.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? host.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            _anchor.GetWorldCorners(corners);
            Vector2 low, high;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(cam, corners[0]), null, out low);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, RectTransformUtility.WorldToScreenPoint(cam, corners[2]), null, out high);

            bool left = (low.x + high.x) * 0.5f > 0f;
            _panel.pivot = new Vector2(left ? 1f : 0f, 1f);
            float x = left ? low.x - Gap : high.x + Gap;
            float y = high.y;
            var size = _panel.sizeDelta;
            var half = root.rect.size * 0.5f;
            if (half.y > 1f)
            {
                y = Mathf.Min(y, half.y - Gap);
                y = Mathf.Max(y, -half.y + Gap + size.y);
            }
            if (half.x > 1f) x = left ? Mathf.Max(x, -half.x + Gap + size.x) : Mathf.Min(x, half.x - Gap - size.x);
            var at = new Vector2(x, y);
            if ((_panel.anchoredPosition - at).sqrMagnitude > 0.25f) _panel.anchoredPosition = at;
        }

        private static void Note()
        {
            if (_note == null) return;
            int left = _waiting;
            bool scan = Flasks.Scanning;
            if (!scan && left <= 0)
            {
                if (_note.text.Length > 0) _note.text = "";
                Blank();
                return;
            }
            string dots = new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3);
            string text = scan
                ? "Смотрю сумку, банки сейчас появятся" + dots
                : "Подгружаю ещё " + left + ", подожди" + dots;
            if (_note.text != text) _note.text = text;
            Blank();
        }

        private static void Blank()
        {
            if (_empty == null || !_empty.gameObject.activeSelf) return;
            string text = Nothing();
            if (_empty.text != text) _empty.text = text;
        }

        private static string Nothing()
        {
            if (Flasks.Scanning || _waiting > 0) return "Список ещё грузится — банки появятся тут";
            if (SideButtons.InCombat()) return "В бою сумку не смотрю — выбери банку вне боя";
            return "Банок в сумке не нашлось";
        }

        private static void Apply(List<int> things)
        {
            if (_grid == null) return;
            _shown.Clear();
            bool loud = Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value;

            for (int i = 0; i < things.Count; i++)
            {
                int thingId = things[i];
                _shown.Add(thingId);
                var cell = i < _cells.Count ? _cells[i] : Make();
                if (cell == null) continue;
                cell.ThingId = thingId;
                if (!cell.Go.activeSelf) cell.Go.SetActive(true);

                var sprite = Flasks.IconFor(thingId);
                if (cell.Icon.sprite != sprite) cell.Icon.sprite = sprite;

                string name = Flasks.DisplayName(thingId) ?? ("id " + thingId);
                int qty;
                _qty.TryGetValue(thingId, out qty);
                string caption = qty > 0 ? name + "  x" + qty : name;
                if (cell.Caption.text != caption) cell.Caption.text = caption;

                if (!loud) continue;
                int sub;
                _subs.TryGetValue(thingId, out sub);
                Plugin.Trace("[банки] выбор: " + thingId + " «" + name + "» подтип " + sub);
            }

            for (int i = things.Count; i < _cells.Count; i++)
                if (_cells[i].Go != null && _cells[i].Go.activeSelf) _cells[i].Go.SetActive(false);

            Empty(things.Count == 0);
            Note();
            Fit(things.Count);
        }

        private static Slot Make()
        {
            var cell = new Slot();
            var cellGo = _near
                ? new GameObject("cell", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup))
                : new GameObject("cell", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup));
            cellGo.transform.SetParent(_grid, false);
            var back = cellGo.GetComponent<Image>();
            back.color = WardrobeLook.Tab;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var button = cellGo.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => Pick(cell.ThingId));
            if (_near)
            {
                var hlg = cellGo.GetComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(4, 6, 2, 2);
                hlg.spacing = 8f;
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;
            }
            else
            {
                var clg = cellGo.GetComponent<VerticalLayoutGroup>();
                clg.padding = new RectOffset(4, 4, 4, 2);
                clg.spacing = 1f;
                clg.childAlignment = TextAnchor.UpperCenter;
                clg.childForceExpandWidth = true;
                clg.childForceExpandHeight = false;
            }

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(cellGo.transform, false);
            var iconImg = iconGo.GetComponent<Image>();
            iconImg.preserveAspect = true;
            var isz = iconGo.GetComponent<LayoutElement>();
            if (_near) { isz.minWidth = NearRow - 4f; isz.preferredWidth = NearRow - 4f; }
            else { isz.minHeight = Icon; isz.preferredHeight = Icon; }

            var cap = Line(cellGo.transform, "", 13, FontStyle.Normal, WardrobeLook.Bright);
            cap.alignment = TextAnchor.UpperCenter;
            cap.horizontalOverflow = HorizontalWrapMode.Wrap;
            cap.verticalOverflow = VerticalWrapMode.Overflow;
            cap.resizeTextForBestFit = true;
            cap.resizeTextMinSize = 9;
            cap.resizeTextMaxSize = 13;
            var csz = cap.gameObject.GetComponent<LayoutElement>();
            if (csz == null) csz = cap.gameObject.AddComponent<LayoutElement>();
            if (_near)
            {
                cap.alignment = TextAnchor.MiddleLeft;
                cap.resizeTextMaxSize = 14;
                csz.flexibleWidth = 1f;
            }
            else { csz.minHeight = Caption; csz.preferredHeight = Caption; }

            cell.Go = cellGo;
            cell.Icon = iconImg;
            cell.Caption = cap;
            _cells.Add(cell);
            return cell;
        }

        private static void Empty(bool show)
        {
            if (_empty == null)
            {
                if (!show) return;
                var t = Line(_grid, Nothing(), 13, FontStyle.Normal, WardrobeLook.Label);
                var le = t.gameObject.GetComponent<LayoutElement>();
                if (le == null) le = t.gameObject.AddComponent<LayoutElement>();
                le.minWidth = (_near ? NearW : PanelW) - 40f; le.minHeight = 40f;
                _empty = t;
            }
            if (_empty.gameObject.activeSelf != show) _empty.gameObject.SetActive(show);
        }

        private static void Pick(int thingId)
        {
            try
            {
                if (_byId != null) _byId.Value = thingId;
                if (_byName != null) _byName.Value = "";
                Settings.Keep(_byId);
                Settings.Keep(_byName);
            }
            catch (Exception e) { Plugin.Fault("[банки] выбор: " + e.Message); }
            var cb = _onPicked;
            Close();
            try { cb?.Invoke(); } catch { }
        }

        private static Text Line(Transform host, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = text;
            return t;
        }

        internal static Font Font()
        {
            try
            {
                var holder = VisualPrefabsHolder.Instance;
                if (holder != null && holder.StandardFont != null) return holder.StandardFont;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
