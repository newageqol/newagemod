using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class QuestWindow
    {
        private const float PanelW = 500f;
        private const float PanelH = 700f;
        private const float MinW = 420f;
        private const float MinH = 340f;
        private const float TopH = 52f;
        private const float BarH = 36f;
        private const float Gap = 10f;
        private const float GripH = 14f;
        private const float RowH = 34f;
        private const float Split = 0.38f;
        private const float StarSide = 20f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Transform _rows;
        private static ScrollRect _scroll;
        private static ScrollRect _deskScroll;
        private static Text _title;
        private static Text _desk;
        private static Text _status;
        private static Button _talk;
        private static Button _track;
        private static readonly List<KeyValuePair<int, Image>> Stars = new List<KeyValuePair<int, Image>>();
        private static int _picked;
        private static string _query = "";
        private static InputField _find;
        private static int _seen = -1;
        private static bool _want;
        private static float _reopenAt;
        private static float _pollAt;
        private static float _filledAt = -10f;
        private static int _starsSeen = -1;
        private static readonly List<KeyValuePair<int, Button>> Lines = new List<KeyValuePair<int, Button>>();

        internal static bool Open => _canvasGo != null;

        internal static int Picked => _picked;

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Show();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        private static void Show()
        {
            try
            {
                _query = "";
                Build();
                _want = true;
                _seen = -1;
                _picked = 0;
                QuestBoard.ReaskIfStale();
                Fill();
            }
            catch (Exception e) { Plugin.Fault("[задания] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            _want = false;
            Teardown();
        }

        private static void Teardown()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch (Exception e)
            {
                Plugin.Trace("[задания] закрытие: " + e.Message);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
            _canvasGo = null;
            _panelGo = null;
            _rows = null;
            _scroll = null;
            _deskScroll = null;
            _title = null;
            _desk = null;
            _status = null;
            _talk = null;
            _track = null;
            Stars.Clear();
            _find = null;
            Lines.Clear();
        }

        internal static void Tick()
        {
            try
            {
                if (_want && _panelGo == null)
                {
                    if (Time.unscaledTime < _reopenAt) return;
                    _reopenAt = Time.unscaledTime + 0.5f;
                    Teardown();
                    if (SideButtons.InWorld() && !SideButtons.InCombat()) Reopen();
                    return;
                }
                if (_canvasGo == null) return;
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + 0.2f;
                int stars = QuestTrack.Stamp;
                if (stars != _starsSeen) { _starsSeen = stars; Paint(); }
                if (QuestBoard.Version == _seen) return;
                if (_seen >= 0 && QuestBoard.Busy && Time.unscaledTime < _filledAt + 1f) return;
                _seen = QuestBoard.Version;
                _filledAt = Time.unscaledTime;
                Fill();
            }
            catch (Exception e) { Plugin.Trace("[задания] окно: " + e.Message); }
        }

        private static void Reopen()
        {
            try
            {
                Build();
                _seen = -1;
                QuestBoard.ReaskIfStale();
                Fill();
            }
            catch (Exception e) { Plugin.Trace("[задания] пересоздание: " + e.Message); Teardown(); }
        }

        private static void Build()
        {
            Teardown();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLQuestWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 1f);
            Vector2 pos; float h, w;
            LoadRect(out pos, out h, out w);
            prt.sizeDelta = new Vector2(w, h);
            prt.anchoredPosition = pos;
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = WardrobeLook.Window;
            pimg.sprite = OnlineWindow.Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopH), new Vector2(0f, 0f));
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;
            mover.OnDone = SaveRect;

            var gripGo = new GameObject("grip", typeof(RectTransform), typeof(Image), typeof(DragResize));
            gripGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)gripGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, GripH));
            gripGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            var sizer = gripGo.GetComponent<DragResize>();
            sizer.Target = prt;
            sizer.Canvas = canvas;
            sizer.Min = MinH;
            sizer.OnDone = SaveRect;
            var gripMark = OnlineWindow.Label(gripGo.transform, "• • •", 12, FontStyle.Bold, WardrobeLook.Faint);
            OnlineWindow.Place(gripMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            gripMark.verticalOverflow = VerticalWrapMode.Overflow;
            gripMark.horizontalOverflow = HorizontalWrapMode.Overflow;
            gripMark.raycastTarget = false;

            var sideGo = new GameObject("gripSide", typeof(RectTransform), typeof(Image), typeof(DragResize));
            sideGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)sideGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-GripH, GripH), new Vector2(0f, -TopH));
            sideGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            var wider = sideGo.GetComponent<DragResize>();
            wider.Target = prt;
            wider.Canvas = canvas;
            wider.Min = 0f;
            wider.MinWide = MinW;
            wider.OnDone = SaveRect;
            var sideMark = OnlineWindow.Label(sideGo.transform, "•\n•\n•", 12, FontStyle.Bold, WardrobeLook.Faint);
            OnlineWindow.Place(sideMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            _title = OnlineWindow.Label(_panelGo.transform, "Задания", 20, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(18f, -42f), new Vector2(-56f, -10f));
            _title.alignment = TextAnchor.MiddleLeft;
            _title.raycastTarget = false;

            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -TopH - Gap - BarH), new Vector2(-16f, -TopH - Gap));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _find = OnlineWindow.MakeInput(barGo.transform, 150f, "Поиск");
            var fle = _find.GetComponent<LayoutElement>();
            if (fle != null) { fle.preferredHeight = BarH; fle.minHeight = BarH; }
            _find.text = _query;
            _find.onValueChanged.AddListener(v => { _query = Norm(v); Fill(); });
            OnlineWindow.MakeGameButton(barGo.transform, "Обновить", 96f, BarH, () => { QuestBoard.Reask(); Fill(); });
            _talk = OnlineWindow.MakeGameButton(barGo.transform, "Открыть", 96f, BarH, () => { if (_picked > 0) QuestBoard.Talk(_picked); });
            _track = OnlineWindow.MakeGameButton(barGo.transform, "Сбросить", 110f, BarH, () => { QuestTrack.Clear(); Paint(); });
            _status = OnlineWindow.Label(barGo.transform, "", 13, FontStyle.Normal, WardrobeLook.Label);
            _status.alignment = TextAnchor.MiddleLeft;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f;
            sle.preferredHeight = BarH;
            sle.minWidth = 60f;

            float head = TopH + Gap + BarH + 6f;
            var listGo = new GameObject("list", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            listGo.transform.SetParent(_panelGo.transform, false);
            var lrt = (RectTransform)listGo.transform;
            lrt.anchorMin = new Vector2(0f, Split);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(14f, 4f);
            lrt.offsetMax = new Vector2(-30f, -head);
            var limg = listGo.GetComponent<Image>();
            limg.color = new Color(0f, 0f, 0f, 0.18f);
            limg.sprite = OnlineWindow.Rounded(8);
            limg.type = Image.Type.Sliced;
            _scroll = listGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 30f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _rows = Content(listGo.transform, _scroll, lrt, 3f);
            Bar(_panelGo.transform, _scroll, new Vector2(1f, Split), new Vector2(1f, 1f), new Vector2(-26f, 4f), new Vector2(-14f, -head));

            var deskTitle = OnlineWindow.Label(_panelGo.transform, "Описание", 15, FontStyle.Bold, WardrobeLook.Accent);
            deskTitle.alignment = TextAnchor.MiddleLeft;
            deskTitle.raycastTarget = false;
            var drt = deskTitle.rectTransform;
            drt.anchorMin = new Vector2(0f, Split);
            drt.anchorMax = new Vector2(1f, Split);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.offsetMin = new Vector2(16f, -22f);
            drt.offsetMax = new Vector2(-16f, 0f);

            var deskGo = new GameObject("desk", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            deskGo.transform.SetParent(_panelGo.transform, false);
            var brt = (RectTransform)deskGo.transform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, Split);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.offsetMin = new Vector2(14f, GripH + 6f);
            brt.offsetMax = new Vector2(-30f, -24f);
            var bimg = deskGo.GetComponent<Image>();
            bimg.color = new Color(0f, 0f, 0f, 0.18f);
            bimg.sprite = OnlineWindow.Rounded(8);
            bimg.type = Image.Type.Sliced;
            _deskScroll = deskGo.GetComponent<ScrollRect>();
            _deskScroll.horizontal = false;
            _deskScroll.vertical = true;
            _deskScroll.scrollSensitivity = 30f;
            _deskScroll.movementType = ScrollRect.MovementType.Clamped;
            var deskRows = Content(deskGo.transform, _deskScroll, brt, 0f);

            _desk = OnlineWindow.Label(deskRows, "", 15, FontStyle.Normal, WardrobeLook.Body);
            _desk.alignment = TextAnchor.UpperLeft;
            _desk.supportRichText = true;
            _desk.horizontalOverflow = HorizontalWrapMode.Wrap;
            _desk.verticalOverflow = VerticalWrapMode.Overflow;
            _desk.raycastTarget = false;
            var dle = _desk.gameObject.AddComponent<LayoutElement>();
            dle.flexibleWidth = 1f;
            Bar(_panelGo.transform, _deskScroll, new Vector2(1f, 0f), new Vector2(1f, Split), new Vector2(-26f, GripH + 6f), new Vector2(-14f, -24f));
        }

        private static Transform Content(Transform host, ScrollRect scroll, RectTransform viewport, float spacing)
        {
            var go = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(6, 6, 5, 5);
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = go.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rt;
            scroll.viewport = viewport;
            return go.transform;
        }

        private static void Bar(Transform host, ScrollRect scroll, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            go.GetComponent<Image>().color = WardrobeLook.Field;
            var sb = go.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            var hrt = (RectTransform)handle.transform;
            OnlineWindow.Place(hrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handle.GetComponent<Image>().color = WardrobeLook.FieldEdge;
            sb.handleRect = hrt;
            sb.targetGraphic = handle.GetComponent<Image>();
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static void Fill()
        {
            if (_rows == null) return;
            var all = QuestBoard.All;
            var cards = new List<QuestBoard.Card>();
            foreach (var card in all) if (Fits(card)) cards.Add(card);
            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            Lines.Clear();
            Stars.Clear();

            bool alive = false;
            foreach (var card in cards)
            {
                if (card.Id == _picked) alive = true;
                Line(card);
            }
            if (!alive) _picked = cards.Count > 0 ? cards[0].Id : 0;

            if (_title != null) _title.text = "Задания" + (all.Count > 0 ? ": " + all.Count : "");
            if (_status != null)
            {
                string tail = "";
                int fresh = QuestBoard.NewOnes();
                int done = QuestBoard.Ready();
                if (done > 0) tail = "готовы сдать: " + done;
                if (fresh > 0) tail = (tail.Length > 0 ? tail + " · " : "") + "новых: " + fresh;
                if (QuestBoard.Busy) tail = (tail.Length > 0 ? tail + " · " : "") + "читаю…";
                if (_query.Length > 0) tail = "найдено: " + cards.Count + (tail.Length > 0 ? " · " + tail : "");
                if (all.Count == 0) tail = "заданий нет";
                _status.text = tail;
            }
            Paint();
            Desk();
        }

        private static bool Fits(QuestBoard.Card card)
        {
            if (_query.Length == 0) return true;
            return Norm(Title(card)).IndexOf(_query, StringComparison.Ordinal) >= 0
                   || Norm(card.Goal).IndexOf(_query, StringComparison.Ordinal) >= 0;
        }

        private static string Norm(string text)
        {
            return (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        }

        private static void Line(QuestBoard.Card card)
        {
            var go = new GameObject("QoLQuest", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_rows, false);
            var img = go.GetComponent<Image>();
            img.sprite = OnlineWindow.Rounded(8);
            img.type = Image.Type.Sliced;
            img.color = WardrobeLook.Tab;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = RowH;
            le.minHeight = RowH;
            le.flexibleWidth = 1f;

            float x = 8f;
            var mark = QuestBoard.Mark(card.Status);
            if (mark != null)
            {
                var markGo = new GameObject("mark", typeof(RectTransform), typeof(Image));
                markGo.transform.SetParent(go.transform, false);
                var mrt = (RectTransform)markGo.transform;
                mrt.anchorMin = new Vector2(0f, 0.5f);
                mrt.anchorMax = new Vector2(0f, 0.5f);
                mrt.pivot = new Vector2(0f, 0.5f);
                mrt.sizeDelta = new Vector2(20f, 20f);
                mrt.anchoredPosition = new Vector2(x, 0f);
                var mimg = markGo.GetComponent<Image>();
                mimg.sprite = mark;
                mimg.preserveAspect = true;
                mimg.raycastTarget = false;
            }
            x += 24f;

            var name = OnlineWindow.Label(go.transform, Title(card), 15, FontStyle.Normal, Tint(card.Status));
            name.alignment = TextAnchor.MiddleLeft;
            name.raycastTarget = false;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            OnlineWindow.Place(name.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(-(StarSide + 12f), 0f));

            int id = card.Id;
            var starGo = new GameObject("star", typeof(RectTransform), typeof(Image), typeof(Button));
            starGo.transform.SetParent(go.transform, false);
            var srt = (RectTransform)starGo.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.sizeDelta = new Vector2(StarSide, StarSide);
            srt.anchoredPosition = new Vector2(-8f, 0f);
            var simg = starGo.GetComponent<Image>();
            simg.preserveAspect = true;
            var sbutton = starGo.GetComponent<Button>();
            sbutton.targetGraphic = simg;
            sbutton.transition = Selectable.Transition.None;
            sbutton.onClick.AddListener(() => { QuestTrack.Toggle(id); Paint(); });
            Stars.Add(new KeyValuePair<int, Image>(id, simg));
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => Pick(id));
            go.name = "QoLQuest" + id;
            Lines.Add(new KeyValuePair<int, Button>(id, button));
        }

        private static string Title(QuestBoard.Card card)
        {
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            if (!string.IsNullOrEmpty(card.Npc)) return card.Npc;
            return card.Known ? "Задание " + card.Id : "…";
        }

        private static Color Tint(int status)
        {
            switch ((EQuestStatus)status)
            {
                case EQuestStatus.QUEST_NOT_FOUND: return WardrobeLook.Accent;
                case EQuestStatus.QUEST_SUCCESS: return WardrobeLook.Good;
                case EQuestStatus.QUEST_FAILED: return WardrobeLook.Bad;
            }
            return WardrobeLook.Bright;
        }

        private static void Pick(int id)
        {
            if (_picked == id) { QuestBoard.Talk(id); return; }
            _picked = id;
            Paint();
            Desk();
        }

        private static void Paint()
        {
            foreach (var line in Lines)
            {
                var button = line.Value;
                if (button == null) continue;
                var img = button.targetGraphic as Image;
                if (img == null) continue;
                bool on = line.Key == _picked;
                img.color = on ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.28f) : WardrobeLook.Tab;
            }
            if (_talk != null) _talk.interactable = _picked > 0;
            if (_track != null) _track.interactable = QuestTrack.Any;
            foreach (var star in Stars)
            {
                if (star.Value == null) continue;
                bool on = QuestTrack.Tracked(star.Key);
                star.Value.sprite = Icons.Star(on);
                star.Value.color = on ? WardrobeLook.Accent : WardrobeLook.Faint;
            }
        }

        private static void Desk()
        {
            if (_desk == null) return;
            QuestBoard.Card card = null;
            foreach (var one in QuestBoard.All) if (one.Id == _picked) { card = one; break; }
            if (card == null)
            {
                _desk.text = QuestBoard.All.Count == 0
                    ? "Заданий нет."
                    : "Выбери задание в списке.";
                return;
            }

            var text = new System.Text.StringBuilder();
            text.Append("<b>").Append(Title(card)).Append("</b>");
            if (!string.IsNullOrEmpty(card.Desc)) text.Append("\n\n").Append(card.Desc.Trim());
            if (!string.IsNullOrEmpty(card.Goal)) text.Append("\n\n<color=#ffd67a>Цель:</color> ").Append(card.Goal.Trim());
            if (card.Counters != null)
                foreach (var line in card.Counters) text.Append("\n<color=#c8b48c>").Append(line).Append("</color>");
            if (!card.Known) text.Append("\n\n<color=#c8b48c>Читаю задание…</color>");
            _desk.text = text.ToString();
            if (_deskScroll != null) _deskScroll.verticalNormalizedPosition = 1f;
        }

        private static void LoadRect(out Vector2 pos, out float h, out float w)
        {
            OnlineWindow.LoadRect(Plugin.CfgQuestWindow, PanelW, PanelH, MinW, MinH, out pos, out h, out w);
        }

        private static void SaveRect()
        {
            OnlineWindow.SaveRect(Plugin.CfgQuestWindow, _panelGo);
        }
    }
}
