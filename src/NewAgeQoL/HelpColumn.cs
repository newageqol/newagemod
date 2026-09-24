using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class HelpColumn
    {
        private const float Side = 46f;
        private const float Gap = 4f;
        internal const float SideGap = 14f;
        private const float LeastTop = 84f;
        private const float TipWide = 300f;
        private const float ArrowH = 20f;
        private const int Cap = 17;
        private const float Handle = 16f;
        private const float TabWide = 22f;
        private const float SlideTime = 0.2f;

        private sealed class Cell
        {
            internal int Id;
            internal Image Icon;
            internal Image Cool;
            internal Text Count;
            internal Button Press;
        }

        private static readonly List<Cell> Cells = new List<Cell>();
        private static readonly Dictionary<int, float> Used = new Dictionary<int, float>();
        private static readonly List<int> Filled = new List<int>();

        private static GameObject _panelGo;
        private static RectTransform _panel;

        internal static RectTransform Shown => _panelGo != null && _panelGo.activeInHierarchy ? _panel : null;
        private static GameObject _tipGo;
        private static Text _tipText;
        private static GameObject _upGo, _downGo;
        private static Image _upPic, _downPic;
        private static bool _filled;
        private static int _filledPage = -1, _filledRows = -1;
        private static float _next;
        private static int _page;
        private static int _total;
        private static int _rows = 1;
        private static GameObject _foldGo;
        private static RectTransform _fold;
        private static RectTransform _foldMark;
        private static float _slideFrom;
        private static float _slideTo;
        private static float _slideAt = -1f;

        private static bool Folded => Plugin.CfgHelpFolded != null && Plugin.CfgHelpFolded.Value;

        private static float Slide
        {
            get
            {
                if (_slideAt < 0f) return _slideTo;
                float step = Mathf.Clamp01((Time.unscaledTime - _slideAt) / SlideTime);
                return Mathf.SmoothStep(_slideFrom, _slideTo, step);
            }
        }

        private static bool Sliding => _slideAt >= 0f && Time.unscaledTime - _slideAt < SlideTime;

        private static void Watch()
        {
            float want = Folded ? 1f : 0f;
            if (want == _slideTo) return;
            _slideFrom = Slide;
            _slideTo = want;
            _slideAt = Time.unscaledTime;
        }

        private static void Settle()
        {
            _slideTo = Folded ? 1f : 0f;
            _slideFrom = _slideTo;
            _slideAt = -1f;
        }

        internal static void Wake()
        {
            _next = 0f;
            Settle();
        }

        internal static void Tick()
        {
            try
            {
                if (!SideButtons.InWorld() || SideButtons.InCombat() || !Quickslots.OnMap) { Close(); return; }
                Roll();
                Watch();
                bool sliding = Sliding;
                if (!sliding && Time.unscaledTime < _next) return;
                if (!sliding) _next = Time.unscaledTime + 0.15f;

                var all = Quickslots.Help();
                if (all.Count == 0) { Close(); return; }

                var host = SideButtons.Deck();
                if (host == null) { Close(); return; }
                if (_panel == null || _panel.parent != host) Build(host);
                if (_panel == null) return;
                if (_foldGo == null) BuildFold(host);
                PlaceFold();
                if (sliding) HideTip();
                if (Folded && !sliding)
                {
                    HideTip();
                    if (_panelGo.activeSelf) _panelGo.SetActive(false);
                    if (_upGo != null && _upGo.activeSelf) _upGo.SetActive(false);
                    if (_downGo != null && _downGo.activeSelf) _downGo.SetActive(false);
                    return;
                }

                float top = Top();
                _total = all.Count;
                int fit = Fit(top, false);
                bool paged = _total > Mathf.Min(fit, Cap);
                _rows = Mathf.Max(1, Mathf.Min(Cap, paged ? Fit(top, true) : fit));
                _page = paged ? Mathf.Clamp(_page, 0, _total - _rows) : 0;
                int from = paged ? _page : 0;
                int count = paged ? _rows : all.Count;

                if (!Same(all, from, count))
                {
                    Filled.Clear();
                    for (int i = 0; i < count; i++) Filled.Add(all[from + i].Id);
                    _filledPage = _page;
                    _filledRows = _rows;
                    _filled = true;
                    Fill(all, from, count);
                }
                Paint(all, from, count);
                Place(top, paged);
                if (!_panelGo.activeSelf) _panelGo.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[помощь] " + e.Message); }
        }

        internal static bool Under()
        {
            if ((_panelGo == null || !_panelGo.activeInHierarchy) && (_foldGo == null || !_foldGo.activeInHierarchy)) return false;
            return Hit(_panel) || Hit(_upGo) || Hit(_downGo) || Hit(_foldGo);
        }

        private static bool Hit(GameObject go)
        {
            return go != null && go.activeInHierarchy && Hit((RectTransform)go.transform);
        }

        private static bool Hit(RectTransform rt)
        {
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
        }

        private static void Roll()
        {
            if (_panelGo == null || !_panelGo.activeSelf || _total <= _rows) return;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!Under()) return;
            Scroll(wheel > 0f ? -1 : 1);
        }

        private static void Scroll(int by)
        {
            int was = _page;
            _page = Mathf.Clamp(_page + by, 0, Mathf.Max(0, _total - _rows));
            if (_page == was) return;
            HideTip();
            _next = 0f;
            Plugin.Trace("[помощь] прокрутка: с " + (_page + 1) + " из " + _total);
        }

        private static bool Same(List<QuickButton> all, int from, int count)
        {
            if (!_filled || _filledPage != _page || _filledRows != _rows || Filled.Count != count) return false;
            for (int i = 0; i < count; i++)
                if (Filled[i] != all[from + i].Id) return false;
            return true;
        }

        private static void Close()
        {
            HideTip();
            Settle();
            if (_panelGo != null && _panelGo.activeSelf) _panelGo.SetActive(false);
            if (_upGo != null && _upGo.activeSelf) _upGo.SetActive(false);
            if (_downGo != null && _downGo.activeSelf) _downGo.SetActive(false);
            if (_foldGo != null && _foldGo.activeSelf) _foldGo.SetActive(false);
        }

        internal static float Head => Mathf.Max(LeastTop, LeftColumn.TopHeight() + 16f);

        internal static float Wide => _foldGo != null && _foldGo.activeInHierarchy ? SideGap + Mathf.Lerp(Side, TabWide, Slide) : 0f;

        private static float Top()
        {
            return Head + Handle + Gap;
        }

        private static void PlaceFold()
        {
            float slide = Slide;
            var size = new Vector2(Mathf.Lerp(Side, TabWide, slide), Mathf.Lerp(Handle, Side, slide));
            if (_fold.sizeDelta != size) _fold.sizeDelta = size;
            var at = new Vector2(-SideGap, -Head);
            if (_fold.anchoredPosition != at) _fold.anchoredPosition = at;
            var turn = Quaternion.Euler(0f, 0f, Mathf.Lerp(90f, -90f, slide));
            if (_foldMark != null && _foldMark.localRotation != turn) _foldMark.localRotation = turn;
            if (!_foldGo.activeSelf) _foldGo.SetActive(true);
        }

        private static void Flip()
        {
            if (Plugin.CfgHelpFolded == null) return;
            Plugin.CfgHelpFolded.Value = !Plugin.CfgHelpFolded.Value;
            HideTip();
            _next = 0f;
            Plugin.Trace("[помощь] столбец " + (Plugin.CfgHelpFolded.Value ? "свёрнут" : "развёрнут"));
        }

        private static void BuildFold(RectTransform host)
        {
            _foldGo = new GameObject("QoLHelpFold", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            _foldGo.transform.SetParent(host, false);
            _fold = (RectTransform)_foldGo.transform;
            _fold.anchorMin = _fold.anchorMax = new Vector2(1f, 1f);
            _fold.pivot = new Vector2(1f, 1f);
            _fold.sizeDelta = new Vector2(Side, Handle);

            var back = _foldGo.GetComponent<Image>();
            back.color = WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;

            var edge = _foldGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var mark = new GameObject("mark", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(_foldGo.transform, false);
            var pic = mark.GetComponent<Image>();
            pic.sprite = Icons.Downward();
            pic.color = WardrobeLook.Bright;
            pic.raycastTarget = false;
            pic.preserveAspect = true;
            _foldMark = (RectTransform)mark.transform;
            _foldMark.anchorMin = _foldMark.anchorMax = new Vector2(0.5f, 0.5f);
            _foldMark.pivot = new Vector2(0.5f, 0.5f);
            _foldMark.sizeDelta = new Vector2(12f, 12f);
            _foldMark.anchoredPosition = Vector2.zero;

            var press = _foldGo.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            press.colors = colors;
            press.onClick.AddListener(Flip);
        }

        private static float Tall()
        {
            var host = _panel != null ? _panel.parent as RectTransform : null;
            return host != null && host.rect.height > 100f ? host.rect.height : 1080f;
        }

        private static int Fit(float top, bool arrows)
        {
            float room = Tall() - top - Mathf.Max(0f, ChatDock.PanelHeight) - SideGap;
            if (arrows) room -= 2f * (ArrowH + Gap);
            if (room < Side) room = Side;
            return Mathf.Max(1, Mathf.FloorToInt((room + Gap) / (Side + Gap)));
        }

        private static void Place(float top, bool paged)
        {
            float shift = paged ? ArrowH + Gap : 0f;
            float away = Slide * (Side + SideGap);
            var want = new Vector2(-SideGap + away, -(top + shift));
            if (_panel.anchoredPosition != want) _panel.anchoredPosition = want;

            var grid = _panelGo.GetComponent<GridLayoutGroup>();
            if (grid != null && grid.constraintCount != _rows) grid.constraintCount = _rows;

            if (!paged)
            {
                if (_upGo != null && _upGo.activeSelf) _upGo.SetActive(false);
                if (_downGo != null && _downGo.activeSelf) _downGo.SetActive(false);
                return;
            }
            if (_upGo == null) _upGo = Arrow(true);
            if (_downGo == null) _downGo = Arrow(false);
            if (_upGo == null || _downGo == null) return;
            if (!_upGo.activeSelf) _upGo.SetActive(true);
            if (!_downGo.activeSelf) _downGo.SetActive(true);

            float high = _rows * Side + (_rows - 1) * Gap;
            var up = (RectTransform)_upGo.transform;
            var upAt = new Vector2(-SideGap + away, -top);
            if (up.anchoredPosition != upAt) up.anchoredPosition = upAt;
            var down = (RectTransform)_downGo.transform;
            var downAt = new Vector2(-SideGap + away, -(top + shift + high + Gap));
            if (down.anchoredPosition != downAt) down.anchoredPosition = downAt;

            var dim = new Color(WardrobeLook.Bright.r, WardrobeLook.Bright.g, WardrobeLook.Bright.b, 0.3f);
            if (_upPic != null) _upPic.color = _page > 0 ? WardrobeLook.Bright : dim;
            if (_downPic != null) _downPic.color = _page < _total - _rows ? WardrobeLook.Bright : dim;
        }

        private static GameObject Arrow(bool up)
        {
            var host = _panel != null ? _panel.parent : null;
            if (host == null) return null;
            var go = new GameObject(up ? "QoLHelpUp" : "QoLHelpDown", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(Side, ArrowH);

            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var tip = new GameObject("mark", typeof(RectTransform), typeof(Image));
            tip.transform.SetParent(go.transform, false);
            var pic = tip.GetComponent<Image>();
            pic.sprite = Icons.Downward();
            pic.color = WardrobeLook.Bright;
            pic.raycastTarget = false;
            pic.preserveAspect = true;
            var trt = (RectTransform)tip.transform;
            OnlineWindow.Place(trt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6f, 3f), new Vector2(-6f, -3f));
            if (up) trt.localRotation = Quaternion.Euler(0f, 0f, 180f);
            if (up) _upPic = pic; else _downPic = pic;

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() => Scroll(up ? -1 : 1));
            return go;
        }

        private static void Build(RectTransform host)
        {
            if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
            if (_upGo != null) UnityEngine.Object.Destroy(_upGo);
            if (_downGo != null) UnityEngine.Object.Destroy(_downGo);
            if (_foldGo != null) UnityEngine.Object.Destroy(_foldGo);
            _upGo = null;
            _downGo = null;
            _foldGo = null;
            _fold = null;
            _foldMark = null;
            _upPic = null;
            _downPic = null;
            Cells.Clear();
            _filled = false;
            _tipGo = null;
            _tipText = null;

            _panelGo = new GameObject("QoLHelpColumn", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            Curtain.Stage(_panelGo);
            _panelGo.transform.SetParent(host, false);
            _panel = (RectTransform)_panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.localScale = Vector3.one;

            var grid = _panelGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Side, Side);
            grid.spacing = new Vector2(Gap, Gap);
            grid.startCorner = GridLayoutGroup.Corner.UpperRight;
            grid.startAxis = GridLayoutGroup.Axis.Vertical;
            grid.childAlignment = TextAnchor.UpperRight;
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 1;

            var fit = _panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Plugin.Trace("[помощь] столбец построен");
        }

        private static void Fill(List<QuickButton> all, int from, int count)
        {
            HideTip();
            for (int i = _panel.childCount - 1; i >= 0; i--)
            {
                var kid = _panel.GetChild(i).gameObject;
                if (_tipGo != null && kid == _tipGo) continue;
                UnityEngine.Object.Destroy(kid);
            }
            Cells.Clear();
            for (int i = 0; i < count; i++) Cells.Add(Slot(all[from + i]));
        }

        private static void Paint(List<QuickButton> all, int from, int count)
        {
            for (int i = 0; i < Cells.Count && i < count; i++)
            {
                var cell = Cells[i];
                var button = all[from + i];
                if (cell == null || button == null || cell.Id != button.Id) continue;

                bool waiting = Waiting(button.Id);
                bool cooling = !button.CanActivate && button.Recharge > 0 && button.Turn < button.Recharge;
                bool live = button.Enabled && !cooling && !waiting;

                if (cell.Icon != null)
                {
                    var tint = live ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                    if (cell.Icon.color != tint) cell.Icon.color = tint;
                    if (Quickslots.Faded(cell.Icon.sprite))
                    {
                        var sprite = Quickslots.Icon(button);
                        if (sprite != null) cell.Icon.sprite = sprite;
                    }
                    bool drawn = !Quickslots.Faded(cell.Icon.sprite);
                    if (cell.Icon.enabled != drawn) cell.Icon.enabled = drawn;
                }
                if (cell.Cool != null)
                {
                    if (cell.Cool.enabled != cooling) cell.Cool.enabled = cooling;
                    if (cooling) cell.Cool.fillAmount = Mathf.Clamp01(1f - (float)button.Turn / button.Recharge);
                }
                if (cell.Count != null)
                {
                    bool show = button.Count > 0;
                    if (cell.Count.enabled != show) cell.Count.enabled = show;
                    if (show)
                    {
                        string text = button.Count.ToString();
                        if (cell.Count.text != text) cell.Count.text = text;
                    }
                }
                if (cell.Press != null && cell.Press.interactable != live) cell.Press.interactable = live;
            }
        }

        private static bool Waiting(int id)
        {
            float when;
            return Used.TryGetValue(id, out when) && Time.unscaledTime - when < 0.4f;
        }

        private static void Use(QuickButton button)
        {
            if (button == null) return;
            if (Waiting(button.Id)) return;
            Used[button.Id] = Time.unscaledTime;
            string trouble = Quickslots.Apply(button);
            if (!string.IsNullOrEmpty(trouble))
            {
                try { AirMessageScript.ShowErrorNotification(trouble); } catch { }
            }
        }

        private static Cell Slot(QuickButton button)
        {
            var go = new GameObject("help", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_panel, false);

            var frame = go.GetComponent<Image>();
            frame.color = WardrobeLook.Card;
            frame.sprite = OnlineWindow.Rounded(6);
            frame.type = Image.Type.Sliced;

            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.FieldEdge;
            edge.effectDistance = new Vector2(2f, -2f);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = Quickslots.Icon(button);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = !Quickslots.Faded(icon.sprite);

            var coolGo = new GameObject("cool", typeof(RectTransform), typeof(Image));
            coolGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)coolGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var cool = coolGo.GetComponent<Image>();
            cool.sprite = OnlineWindow.Rounded(6);
            cool.color = new Color(0f, 0f, 0f, 0.62f);
            cool.type = Image.Type.Filled;
            cool.fillMethod = Image.FillMethod.Radial360;
            cool.fillOrigin = (int)Image.Origin360.Top;
            cool.fillClockwise = false;
            cool.fillAmount = 0f;
            cool.raycastTarget = false;
            cool.enabled = false;

            var count = OnlineWindow.Label(go.transform, "", 13, FontStyle.Bold, WardrobeLook.Bright);
            count.alignment = TextAnchor.LowerRight;
            count.raycastTarget = false;
            var shadow = count.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(2f, 2f), new Vector2(-3f, -2f));
            count.enabled = false;

            var press = go.GetComponent<Button>();
            press.targetGraphic = frame;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(2.6f, 2.2f, 1.6f, 1f);
            colors.fadeDuration = 0f;
            press.colors = colors;
            var shot = button;
            press.onClick.AddListener(() => Use(shot));

            var watch = go.AddComponent<HoverWatch>();
            var where = (RectTransform)go.transform;
            watch.OnEnter = () => ShowTip(shot, where);
            watch.OnExit = HideTip;

            return new Cell { Id = button.Id, Icon = icon, Cool = cool, Count = count, Press = press };
        }

        private static void ShowTip(QuickButton button, RectTransform near)
        {
            try
            {
                if (button == null || _panel == null) return;
                if (_tipGo == null) BuildTip();
                if (_tipGo == null || _tipText == null) return;

                var text = new StringBuilder();
                text.Append("<b><color=#e2b85c>").Append(button.Name ?? "").Append("</color></b>");
                if (button.Count > 0) text.Append("  <color=#acb3bd>×").Append(button.Count).Append("</color>");
                if (!button.Enabled && !string.IsNullOrEmpty(button.DisableCause))
                    text.Append("\n<color=#f07a6e>").Append(button.DisableCause).Append("</color>");
                string about = Quickslots.About(button);
                if (about.Length > 0) text.Append("\n\n").Append(about);
                _tipText.text = text.ToString();

                var trt = (RectTransform)_tipGo.transform;
                trt.pivot = new Vector2(1f, 1f);
                float x = near.anchoredPosition.x - Side * 0.5f - Gap;
                float y = near.anchoredPosition.y + Side * 0.5f;
                trt.anchoredPosition = new Vector2(x, y);
                _tipGo.transform.SetAsLastSibling();
                if (!_tipGo.activeSelf) _tipGo.SetActive(true);

                LayoutRebuilder.ForceRebuildLayoutImmediate(trt);
                float floor = -Tall() + 8f;
                float bottom = _panel.anchoredPosition.y + y - trt.rect.height;
                if (bottom < floor) trt.anchoredPosition = new Vector2(x, y + (floor - bottom));
            }
            catch (Exception e) { Plugin.Trace("[помощь] подсказка: " + e.Message); }
        }

        private static void HideTip()
        {
            if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
        }

        private static void BuildTip()
        {
            if (_panel == null) return;
            _tipGo = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline),
                typeof(ContentSizeFitter), typeof(VerticalLayoutGroup), typeof(CanvasGroup), typeof(LayoutElement));
            _tipGo.transform.SetParent(_panel, false);
            var trt = (RectTransform)_tipGo.transform;
            trt.anchorMin = trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(1f, 1f);

            var ignore = _tipGo.GetComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            var back = _tipGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var veil = _tipGo.GetComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;

            var box = _tipGo.GetComponent<VerticalLayoutGroup>();
            box.padding = new RectOffset(10, 10, 8, 8);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = true;
            box.childForceExpandHeight = false;

            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            trt.sizeDelta = new Vector2(TipWide + 20f, 60f);

            _tipText = OnlineWindow.Label(_tipGo.transform, "", 14, FontStyle.Normal, WardrobeLook.Bright);
            _tipText.alignment = TextAnchor.UpperLeft;
            _tipText.supportRichText = true;
            _tipText.raycastTarget = false;
            _tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            var room = _tipText.gameObject.AddComponent<LayoutElement>();
            room.preferredWidth = TipWide;
            _tipGo.SetActive(false);
        }
    }
}
