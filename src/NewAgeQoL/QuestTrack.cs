using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class QuestTrack
    {
        private const float Wide = 300f;
        private const float Least = 170f;
        private const float Gap = 8f;
        private const float Rise = 6f;
        private const int WindowLayer = 100;
        private const int TitleFont = 13;
        private const int LineFont = 12;

        private static readonly List<int> Ids = new List<int>();
        private static readonly Dictionary<int, float> Missing = new Dictionary<int, float>();
        private static string _read;
        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static RectTransform _clip;
        private static RectTransform _list;
        private static RectTransform _strip;
        private static Text _stripMark;
        private static bool _folded;
        private static float _slide;
        private static float _left;
        private static float _low;
        private static float _wide = Wide;
        private static float _dockHigh = 160f;
        private const float StripWide = 12f;
        private const float StripGap = 4f;
        private const float Shortest = 24f;
        private const float StripHigh = 72f;
        private const float SlideTime = 0.2f;
        private const float PadTop = 6f;
        private const float PadBottom = 7f;
        private static int _seen = -1;
        private static bool _redo;
        private static float _placeAt;
        private static bool _fits;
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static bool Tracked(int id)
        {
            Load();
            return Ids.Contains(id);
        }

        private static int _stamp;

        internal static int Stamp
        {
            get
            {
                Load();
                return _stamp;
            }
        }

        internal static bool Any
        {
            get
            {
                Load();
                return Ids.Count > 0;
            }
        }

        internal static void Clear()
        {
            Load();
            if (Ids.Count == 0) return;
            Ids.Clear();
            Missing.Clear();
            Save();
        }

        internal static void Toggle(int id)
        {
            if (id <= 0) return;
            Load();
            if (Ids.Contains(id)) Ids.Remove(id); else Ids.Add(id);
            Save();
        }

        private static void Drop(int id)
        {
            Load();
            if (!Ids.Remove(id)) return;
            Save();
        }

        private static void Load()
        {
            var entry = Plugin.CfgQuestTracked;
            string now = entry != null ? entry.Value ?? "" : "";
            if (now == _read) return;
            _read = now;
            Ids.Clear();
            Missing.Clear();
            foreach (var part in now.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int id;
                if (int.TryParse(part, out id) && id > 0 && !Ids.Contains(id)) Ids.Add(id);
            }
            _redo = true;
            _stamp++;
        }

        private static void Save()
        {
            string now = string.Join(";", Ids);
            _read = now;
            if (Plugin.CfgQuestTracked != null) Plugin.CfgQuestTracked.Value = now;
            _redo = true;
            _stamp++;
        }

        internal static void Tick()
        {
            try
            {
                Load();
                bool want = Ids.Count > 0 && SideButtons.InWorld() && !SideButtons.InCombat() && ChatDock.Active;
                if (!want) { Hide(); return; }
                Prune();
                if (!Present()) { Hide(); return; }
                if (_canvasGo == null) Build();
                if (_canvasGo == null) return;
                if (Time.unscaledTime >= _placeAt)
                {
                    _placeAt = Time.unscaledTime + 0.5f;
                    _fits = Place();
                }
                if (!_fits) { Hide(); return; }
                if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
                if (_redo || QuestBoard.Version != _seen)
                {
                    _redo = false;
                    _seen = QuestBoard.Version;
                    Fill();
                }
                Lay();
            }
            catch (Exception e) { Plugin.Trace("[слежка] " + e.Message); Kill(); }
        }

        private static void Prune()
        {
            var all = QuestBoard.All;
            if (all.Count == 0) { Missing.Clear(); return; }
            for (int i = Ids.Count - 1; i >= 0; i--)
            {
                int id = Ids[i];
                bool here = false;
                foreach (var card in all) if (card.Id == id) { here = true; break; }
                if (here) { Missing.Remove(id); continue; }
                float since;
                if (!Missing.TryGetValue(id, out since)) { Missing[id] = Time.unscaledTime; continue; }
                if (Time.unscaledTime - since < 10f) continue;
                Missing.Remove(id);
                Plugin.Trace("[слежка] задания " + id + " больше нет в списке — перестаю следить");
                Drop(id);
            }
        }

        private static bool Present()
        {
            var all = QuestBoard.All;
            foreach (int id in Ids)
                foreach (var card in all) if (card.Id == id) return true;
            return false;
        }

        private static void Hide()
        {
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static void Kill()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            _root = null;
            _clip = null;
            _list = null;
            _strip = null;
            _stripMark = null;
            _seen = -1;
            _fits = false;
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLQuestTrack", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            _canvas = _canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 240;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var clip = new GameObject("clip", typeof(RectTransform), typeof(RectMask2D));
            clip.transform.SetParent(_canvasGo.transform, false);
            _clip = (RectTransform)clip.transform;
            _clip.anchorMin = _clip.anchorMax = Vector2.zero;
            _clip.pivot = Vector2.zero;
            _clip.sizeDelta = new Vector2(Wide + 2f, 40f);

            var panel = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            panel.transform.SetParent(_clip, false);
            _root = (RectTransform)panel.transform;
            _root.anchorMin = _root.anchorMax = Vector2.zero;
            _root.pivot = Vector2.zero;
            _root.sizeDelta = new Vector2(Wide, 40f);
            var back = panel.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var scroll = panel.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var list = new GameObject("list", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            list.transform.SetParent(_root, false);
            _list = (RectTransform)list.transform;
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(0.5f, 1f);
            _list.offsetMin = Vector2.zero;
            _list.offsetMax = Vector2.zero;
            scroll.content = _list;
            scroll.viewport = _root;
            var group = list.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(8, 6, (int)PadTop, (int)PadBottom);
            group.spacing = 6f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = list.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var strip = new GameObject("fold", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            strip.transform.SetParent(_canvasGo.transform, false);
            _strip = (RectTransform)strip.transform;
            _strip.anchorMin = _strip.anchorMax = Vector2.zero;
            _strip.pivot = Vector2.zero;
            _strip.sizeDelta = new Vector2(StripWide, 40f);
            var stripBack = strip.GetComponent<Image>();
            stripBack.color = WardrobeLook.Window;
            stripBack.sprite = OnlineWindow.Rounded(8);
            stripBack.type = Image.Type.Sliced;
            var stripEdge = strip.GetComponent<Outline>();
            stripEdge.effectColor = WardrobeLook.Edge;
            stripEdge.effectDistance = new Vector2(1f, -1f);
            var fold = strip.GetComponent<Button>();
            fold.targetGraphic = stripBack;
            var colors = fold.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            fold.colors = colors;
            fold.onClick.AddListener(Fold);
            _stripMark = OnlineWindow.Label(strip.transform, "‹", 14, FontStyle.Bold, WardrobeLook.Label);
            _stripMark.raycastTarget = false;
            _stripMark.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stripMark.verticalOverflow = VerticalWrapMode.Overflow;
            OnlineWindow.Place(_stripMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _slide = _folded ? 1f : 0f;
            Folded();
            _seen = -1;
            _redo = true;
            _placeAt = 0f;
        }

        private static void Fill()
        {
            if (_list == null) return;
            for (int i = _list.childCount - 1; i >= 0; i--)
            {
                var old = _list.GetChild(i).gameObject;
                old.SetActive(false);
                old.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(old);
            }
            var all = QuestBoard.All;
            foreach (int id in Ids)
            {
                QuestBoard.Card card = null;
                foreach (var one in all) if (one.Id == id) { card = one; break; }
                if (card != null) Entry(id, card);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(_list);
        }

        private static void Entry(int id, QuestBoard.Card card)
        {
            var item = new GameObject("quest" + id, typeof(RectTransform), typeof(VerticalLayoutGroup));
            item.transform.SetParent(_list, false);
            var group = item.GetComponent<VerticalLayoutGroup>();
            group.spacing = 1f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            var head = new GameObject("head", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            head.transform.SetParent(item.transform, false);
            var row = head.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 4f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            var title = Line(head.transform, Title(card, id), TitleFont, FontStyle.Bold, Tint(card));
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;
            title.raycastTarget = true;
            var open = title.gameObject.AddComponent<Button>();
            open.transition = Selectable.Transition.None;
            open.onClick.AddListener(() => QuestBoard.Talk(id));

            Cross(head.transform, () => { Drop(id); });

            foreach (string text in Lines(card))
                Line(item.transform, text, LineFont, FontStyle.Normal, WardrobeLook.Label);
        }

        private static Text Line(Transform host, string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(host, text, size, style, color);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.gameObject.AddComponent<LayoutElement>();
            return label;
        }

        private static void Cross(Transform host, Action click)
        {
            var go = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var size = go.GetComponent<LayoutElement>();
            size.preferredWidth = size.minWidth = 16f;
            size.preferredHeight = size.minHeight = 16f;
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Tab;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var mark = OnlineWindow.Label(go.transform, "×", 14, FontStyle.Bold, WardrobeLook.Label);
            mark.raycastTarget = false;
            mark.horizontalOverflow = HorizontalWrapMode.Overflow;
            mark.verticalOverflow = VerticalWrapMode.Overflow;
            OnlineWindow.Place(mark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
        }

        private static string Title(QuestBoard.Card card, int id)
        {
            if (card == null) return "Задание " + id;
            if (!string.IsNullOrEmpty(card.Name)) return card.Name;
            if (!string.IsNullOrEmpty(card.Npc)) return card.Npc;
            return "Задание " + id;
        }

        private static Color Tint(QuestBoard.Card card)
        {
            if (card == null) return WardrobeLook.Faint;
            switch ((EQuestStatus)card.Status)
            {
                case EQuestStatus.QUEST_SUCCESS: return WardrobeLook.Good;
                case EQuestStatus.QUEST_FAILED: return WardrobeLook.Bad;
                case EQuestStatus.QUEST_NOT_FOUND: return WardrobeLook.Accent;
            }
            return WardrobeLook.Bright;
        }

        private static List<string> Lines(QuestBoard.Card card)
        {
            var lines = new List<string>();
            if (card == null) return lines;
            switch ((EQuestStatus)card.Status)
            {
                case EQuestStatus.QUEST_SUCCESS: lines.Add("выполнено — можно сдавать"); break;
                case EQuestStatus.QUEST_FAILED: lines.Add("провалено"); break;
                case EQuestStatus.QUEST_NOT_FOUND: lines.Add("ещё не взято"); break;
            }
            if (card.Counters != null && card.Counters.Count > 0)
            {
                foreach (string one in card.Counters) lines.Add(one);
            }
            else if (card.Status == (int)EQuestStatus.QUEST_ACTIVE)
            {
                if (!string.IsNullOrEmpty(card.Goal)) lines.Add(Clip(card.Goal.Trim(), 140));
                else if (!card.Known) lines.Add("читаю…");
            }
            return lines;
        }

        private static string Clip(string text, int most)
        {
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= most ? text : text.Substring(0, most - 1).TrimEnd() + "…";
        }

        private static void Fold()
        {
            _folded = !_folded;
            Folded();
            _placeAt = 0f;
        }

        private static void Folded()
        {
            if (_stripMark != null) _stripMark.text = _folded ? "›" : "‹";
        }

        private static void Lay()
        {
            if (_root == null || _clip == null || _strip == null || _list == null) return;
            float target = _folded ? 1f : 0f;
            if (_slide != target) _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime / SlideTime);
            bool shown = _slide < 0.999f;
            if (_clip.gameObject.activeSelf != shown) _clip.gameObject.SetActive(shown);

            float tallest = _canvas != null && _canvas.scaleFactor > 0f ? Screen.height / _canvas.scaleFactor * 0.6f : 600f;
            float high = Mathf.Clamp(_list.rect.height, Shortest, Mathf.Max(Shortest, tallest));
            var stripSize = new Vector2(StripWide, Mathf.Min(StripHigh, _dockHigh));
            if ((_strip.sizeDelta - stripSize).sqrMagnitude > 0.01f) _strip.sizeDelta = stripSize;
            var stripSpot = new Vector2(_left, _low);
            if ((_strip.anchoredPosition - stripSpot).sqrMagnitude > 0.01f) _strip.anchoredPosition = stripSpot;

            var clipSize = new Vector2(_wide + 2f, high + 2f);
            if ((_clip.sizeDelta - clipSize).sqrMagnitude > 0.01f) _clip.sizeDelta = clipSize;
            var clipSpot = new Vector2(_left + StripWide + StripGap, _low - 1f);
            if ((_clip.anchoredPosition - clipSpot).sqrMagnitude > 0.01f) _clip.anchoredPosition = clipSpot;

            var size = new Vector2(_wide, high);
            if ((_root.sizeDelta - size).sqrMagnitude > 0.01f) _root.sizeDelta = size;
            float eased = _slide * _slide * (3f - 2f * _slide);
            var spot = new Vector2(-eased * (_wide + StripGap + 2f), 1f);
            if ((_root.anchoredPosition - spot).sqrMagnitude > 0.01f) _root.anchoredPosition = spot;
        }

        private static bool Place()
        {
            var dock = ChatDock.Root;
            if (_root == null || _canvas == null || dock == null) return false;
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            dock.GetWorldCorners(Corners);
            var dockCanvas = dock.GetComponentInParent<Canvas>();
            var cam = dockCanvas != null && dockCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? dockCanvas.rootCanvas.worldCamera : null;
            float dockRight = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]).x;
            float dockBottom = Mathf.Max(0f, RectTransformUtility.WorldToScreenPoint(cam, Corners[0]).y);
            float dockTop = RectTransformUtility.WorldToScreenPoint(cam, Corners[1]).y;

            float left = dockRight + StripGap * scale;
            float right = Screen.width;
            var column = HelpColumn.Shown;
            if (column != null)
            {
                var columnCanvas = column.GetComponentInParent<Canvas>();
                var columnCam = columnCanvas != null && columnCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? columnCanvas.rootCanvas.worldCamera : null;
                column.GetWorldCorners(Corners);
                float edge = RectTransformUtility.WorldToScreenPoint(columnCam, Corners[0]).x;
                if (edge > left && edge < right) right = edge;
            }
            float room = (right - Gap * scale - left) / scale - StripWide - StripGap;
            if (room < Least) return false;
            float wide = Mathf.Min(Wide, room);
            float span = _folded ? StripWide : StripWide + StripGap + wide;

            float low = Floor(left, left + span * scale, dockBottom + Gap * scale);
            _left = left / scale;
            _low = low / scale;
            _wide = wide;
            _dockHigh = Mathf.Max(Shortest, (dockTop - dockBottom) / scale - Gap);
            return true;
        }

        private static float Floor(float from, float to, float low)
        {
            float best = low;
            float tallest = Screen.height * 0.4f;
            float widest = Screen.width * 0.5f;
            foreach (var one in Selectable.allSelectablesArray)
            {
                if (one == null || !one.isActiveAndEnabled || !one.interactable) continue;
                var rt = one.transform as RectTransform;
                if (rt == null || (_canvasGo != null && rt.IsChildOf(_canvasGo.transform))) continue;
                var canvas = one.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.enabled) continue;
                var root = canvas.rootCanvas;
                if (root.sortingOrder >= WindowLayer) continue;
                var cam = root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
                rt.GetWorldCorners(Corners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
                float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
                float yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
                if (yMax - yMin > tallest || xMax - xMin > widest) continue;
                if (xMax <= from || xMin >= to) continue;
                if (yMin > Screen.height * 0.5f) continue;
                if (yMax + Rise > best) best = yMax + Rise;
            }
            return best;
        }
    }
}
