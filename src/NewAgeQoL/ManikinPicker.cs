using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ManikinPicker
    {
        private const float Cell = 132f;
        private const float IconSide = 72f;
        private const float CardH = IconSide + 58f;
        private const float Space = 10f;
        private const float Pad = 10f;
        private const float Inset = 16f;
        private const float Head = 72f;
        private const int Columns = 5;
        private const float MaxRows = 3.4f;
        private const float Patience = 1.5f;

        internal sealed class Choice
        {
            internal int ThingId;
            internal string Where;
            internal int Left;
            internal int Have;
        }

        private sealed class Card
        {
            internal int ThingId;
            internal Image Icon;
            internal Text Caption;
            internal Text Note;
        }

        private static GameObject _canvasGo;
        private static GameObject _scrollGo;
        private static Transform _grid;
        private static GridLayoutGroup _layout;
        private static RectTransform _panel;
        private static RectTransform _shadow;
        private static Text _sub;
        private static int _current;
        private static Text _wait;
        private static Action<int> _done;
        private static int _slot;
        private static bool _built;
        private static float _at, _since;
        private static string _sign = "";
        private static readonly List<Card> Cards = new List<Card>();

        internal static void Open(int slot, int current, Action<int> done)
        {
            Close();
            _slot = slot;
            _current = current;
            _done = done;
            _built = false;
            _sign = "";
            _since = Time.unscaledTime;
            _at = 0f;
            try
            {
                var list = Candidates(_slot);
                Build(list.Count);
                Ask(list);
                Flasks.RequestScan();
                if (Ready(list)) Fill(list);
            }
            catch (Exception e) { Plugin.Fault("[manikin] item pick: " + e); Close(); }
        }

        internal static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _scrollGo = null;
            _grid = null;
            _layout = null;
            _panel = null;
            _shadow = null;
            _sub = null;
            _wait = null;
            _done = null;
            _built = false;
            Cards.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        internal static void Tick()
        {
            if (_canvasGo == null || _grid == null) return;
            if (Time.unscaledTime < _at) return;
            _at = Time.unscaledTime + (_built ? 0.3f : 0.05f);

            var list = Candidates(_slot);
            if (!_built)
            {
                if (!Ready(list) && Time.unscaledTime - _since < Patience) return;
                Fill(list);
                return;
            }

            if (Sign(list) != _sign) { Fill(list); return; }
            Refresh(list);
        }

        private static void Ask(List<Choice> list)
        {
            foreach (var choice in list)
            {
                Flasks.AskName(choice.ThingId);
                Flasks.IconFor(choice.ThingId);
            }
        }

        private static bool Ready(List<Choice> list)
        {
            foreach (var choice in list)
            {
                if (Flasks.DisplayName(choice.ThingId) == null) return false;
                if (Flasks.ImageOf(choice.ThingId) == null) return false;
            }
            return true;
        }

        private static string Sign(List<Choice> list)
        {
            var text = new System.Text.StringBuilder();
            foreach (var choice in list) text.Append(choice.ThingId).Append('/').Append(choice.Left).Append('/').Append(choice.Have).Append(';');
            return text.ToString();
        }

        private static bool Fits(int slot, int subType)
        {
            try
            {
                var allowed = ESlots.GetSlotsByThingSubtype((EThingSubType)subType);
                if (allowed == null) return false;
                int wanted = Base(slot);
                bool hand = wanted == 11 || wanted == 12;
                foreach (var one in allowed)
                {
                    int id = (int)one;
                    if (id == wanted) return true;
                    if (hand && (id == 11 || id == 12)) return true;
                }
            }
            catch { }
            return false;
        }

        private static int Base(int slot)
        {
            switch (slot)
            {
                case 19: return 11;
                case 20: return 12;
                case 21: return 15;
                case 22: return 16;
                case 23: return 17;
                case 24: return 18;
                default: return slot;
            }
        }

        private static bool Suits(int slot, int thingId)
        {
            int sub = Flasks.SubTypeOf(thingId);
            if (sub == 0) return false;
            if (!Fits(slot, sub)) return false;
            return Flasks.CanWear(thingId);
        }

        private static int Have(int thingId)
        {
            int worn, bag, store;
            Count(thingId, out worn, out bag, out store);
            return worn + bag + store;
        }

        private static void Count(int thingId, out int worn, out int bag, out int store)
        {
            var seen = new HashSet<int>();
            worn = 0;
            foreach (var pair in Storage.WornSlots())
            {
                var item = pair.Value;
                if (item == null || item.ThingId != thingId) continue;
                if (item.InventoryId > 0 && !seen.Add(item.InventoryId)) continue;
                worn++;
            }
            bag = Flasks.QtyOf(thingId, seen);
            store = Artifacts.NearStorage() ? Storage.QtyOf(thingId) : 0;
        }

        internal static List<Choice> Candidates(int slot)
        {
            var list = new List<Choice>();
            var seen = new HashSet<int>();

            foreach (var pair in Storage.WornSlots())
            {
                var item = pair.Value;
                if (item == null || item.ThingId <= 0) continue;
                Flasks.Note(item.ThingId, item.Image, item.Subtype, item.Rarity, item.Level);
                Flasks.NoteUse(item.ThingId, 0);
                if (!Fits(slot, item.Subtype)) continue;
                if (!seen.Add(item.ThingId)) continue;
                Offer(list, slot, item.ThingId, Lang.T("надето"));
            }
            foreach (var thing in Flasks.BagThings())
            {
                if (!Suits(slot, thing)) continue;
                if (!seen.Add(thing)) continue;
                Offer(list, slot, thing, Lang.T("инвентарь"));
            }
            if (Artifacts.NearStorage())
                foreach (var thing in Storage.Things())
                {
                    if (!Flasks.Known(thing.Key)) { Flasks.AskName(thing.Key); continue; }
                    if (!Suits(slot, thing.Key)) continue;
                    if (!seen.Add(thing.Key)) continue;
                    Offer(list, slot, thing.Key, Lang.T("хранилище"));
                }
            return list;
        }

        private static void Offer(List<Choice> list, int slot, int thingId, string where)
        {
            int have = Have(thingId);
            int used = Manikin.Used(thingId, slot);
            int left = have - used;
            if (have > 0 && left <= 0) return;
            list.Add(new Choice { ThingId = thingId, Where = where, Left = left > 0 ? left : 1, Have = have });
        }

        private static void Fill(List<Choice> list)
        {
            if (_grid == null) return;
            for (int i = _grid.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_grid.GetChild(i).gameObject);
            Cards.Clear();

            if (_current > 0) Make(0, Lang.T("пусто"), Lang.T("убрать из слота"), 0, false);
            foreach (var choice in list)
                Make(choice.ThingId, Flasks.DisplayName(choice.ThingId) ?? Lang.F("вещь {0}", choice.ThingId), Where(choice),
                     choice.Have > 1 ? choice.Left : 0, choice.ThingId == _current);

            Fit(Cards.Count);
            foreach (var choice in list)
            {
                if (choice.Have <= 1) continue;
                int worn, bag, store;
                Count(choice.ThingId, out worn, out bag, out store);
                Plugin.Trace("[kit] " + Manikin.SlotName(_slot) + ": thing " + choice.ThingId + " worn " + worn + ", bag " + bag
                             + ", storage " + store + ", in kit elsewhere " + Manikin.Used(choice.ThingId, _slot) + ", left " + choice.Left);
            }
            if (_sub != null)
                _sub.text = list.Count > 0
                    ? Lang.F("Подходит вещей: {0}", list.Count)
                    : Lang.T("Подходящих вещей нет: положи вещь в сумку или зайди в хранилище.");

            _built = true;
            _sign = Sign(list);
            if (_wait != null) _wait.gameObject.SetActive(false);
            if (_scrollGo != null) _scrollGo.SetActive(true);
        }

        private static string Where(Choice choice)
        {
            return choice.ThingId == _current ? Lang.T("в этом слоте") : choice.Where;
        }

        private static void Fit(int cards)
        {
            if (_panel == null) return;
            int cols = Mathf.Clamp(cards, 1, Columns);
            int rows = Mathf.Max(1, (cards + Columns - 1) / Columns);
            float shown = Mathf.Min(rows, MaxRows);
            float wide = Mathf.Max(460f, cols * Cell + (cols - 1) * Space + Pad * 2f + Inset * 2f);
            float high = Head + shown * CardH + (Mathf.Ceil(shown) - 1f) * Space + Pad * 2f + Inset;
            if (rows > MaxRows) high = Head + MaxRows * (CardH + Space) + Pad + Inset;
            _panel.sizeDelta = new Vector2(wide, high);
            if (_shadow != null) _shadow.sizeDelta = new Vector2(wide + 28f, high + 28f);
            if (_layout != null) _layout.constraintCount = cols;
        }

        private static string Upper(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private static void Refresh(List<Choice> list)
        {
            var known = new Dictionary<int, Choice>();
            foreach (var choice in list) known[choice.ThingId] = choice;
            foreach (var card in Cards)
            {
                if (card.ThingId <= 0) continue;
                if (card.Icon != null)
                {
                    var sprite = Flasks.IconFor(card.ThingId);
                    if (sprite != null && card.Icon.sprite != sprite) { card.Icon.sprite = sprite; card.Icon.enabled = true; }
                }
                string name = Flasks.DisplayName(card.ThingId);
                if (card.Caption != null && name != null && card.Caption.text != name) card.Caption.text = name;
                Choice choice;
                if (card.Note != null && known.TryGetValue(card.ThingId, out choice))
                {
                    string note = Where(choice);
                    if (card.Note.text != note) card.Note.text = note;
                }
            }
        }

        private static void Make(int thingId, string name, string where, int left, bool mine)
        {
            var go = new GameObject("thing" + thingId, typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup), typeof(Outline));
            go.transform.SetParent(_grid, false);
            var back = go.GetComponent<Image>();
            back.color = mine ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.12f) : WardrobeLook.Tab;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = mine ? WardrobeLook.Accent : new Color(1f, 1f, 1f, 0.06f);
            edge.effectDistance = mine ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(go.transform, false);
            var icon = iconGo.GetComponent<Image>();
            var sprite = thingId > 0 ? Flasks.IconFor(thingId) : Manikin.SlotSprite(_slot);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.preserveAspect = true;
            if (thingId <= 0) icon.color = new Color(1f, 1f, 1f, 0.35f);
            icon.raycastTarget = false;
            var ile = iconGo.GetComponent<LayoutElement>();
            ile.minHeight = IconSide; ile.preferredHeight = IconSide;

            if (left > 0)
            {
                var pillGo = new GameObject("count", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                pillGo.transform.SetParent(go.transform, false);
                pillGo.GetComponent<LayoutElement>().ignoreLayout = true;
                var prt = (RectTransform)pillGo.transform;
                prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f);
                prt.pivot = new Vector2(1f, 1f);
                prt.sizeDelta = new Vector2(46f, 20f);
                prt.anchoredPosition = new Vector2(-6f, -6f);
                var pill = pillGo.GetComponent<Image>();
                pill.sprite = OnlineWindow.Rounded(8);
                pill.type = Image.Type.Sliced;
                pill.color = new Color(0f, 0f, 0f, 0.55f);
                pill.raycastTarget = false;
                var count = OnlineWindow.Label(pillGo.transform, Lang.F("{0} шт.", left), 12, FontStyle.Bold, WardrobeLook.Accent);
                OnlineWindow.Place(count.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                count.raycastTarget = false;
            }

            var caption = OnlineWindow.Label(go.transform, name, 12, FontStyle.Normal, WardrobeLook.Bright);
            caption.alignment = TextAnchor.UpperCenter;
            caption.horizontalOverflow = HorizontalWrapMode.Wrap;
            caption.verticalOverflow = VerticalWrapMode.Truncate;
            caption.resizeTextForBestFit = true;
            caption.resizeTextMinSize = 9;
            caption.resizeTextMaxSize = 12;
            caption.gameObject.AddComponent<LayoutElement>().minHeight = 30f;

            var note = OnlineWindow.Label(go.transform, where, 11, FontStyle.Normal, mine ? WardrobeLook.Accent : WardrobeLook.Faint);
            note.alignment = TextAnchor.UpperCenter;
            note.gameObject.AddComponent<LayoutElement>().minHeight = 16f;

            int id = thingId;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                var done = _done;
                Close();
                if (done != null) done(id);
            });

            Cards.Add(new Card { ThingId = thingId, Icon = icon, Caption = caption, Note = note });
        }

        private static void Build(int count)
        {
            var go = new GameObject("QoLManikinPicker", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 820;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            var shadowGo = new GameObject("shadow", typeof(RectTransform), typeof(Image));
            shadowGo.transform.SetParent(go.transform, false);
            _shadow = (RectTransform)shadowGo.transform;
            _shadow.anchorMin = _shadow.anchorMax = new Vector2(0.5f, 0.5f);
            _shadow.anchoredPosition = new Vector2(0f, -8f);
            var shade = shadowGo.GetComponent<Image>();
            shade.sprite = OnlineWindow.Rounded(16);
            shade.type = Image.Type.Sliced;
            shade.color = new Color(0f, 0f, 0f, 0.45f);
            shade.raycastTarget = false;

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            _panel = prt;
            var panel = panelGo.GetComponent<Image>();
            panel.color = WardrobeLook.Window;
            panel.sprite = OnlineWindow.Rounded(16);
            panel.type = Image.Type.Sliced;
            var outline = panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(panelGo.transform, Upper(Lang.F("{0}: выбери вещь", Manikin.SlotName(_slot))), 20, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(22f, -42f), new Vector2(-70f, -12f));
            title.alignment = TextAnchor.MiddleLeft;

            _sub = OnlineWindow.Label(panelGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Faint);
            OnlineWindow.Place(_sub.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(22f, -60f), new Vector2(-70f, -42f));
            _sub.alignment = TextAnchor.MiddleLeft;

            OnlineWindow.MakeCloseButton(panelGo.transform, Close);

            _wait = OnlineWindow.Label(panelGo.transform, Lang.T("Загружаю вещи…"), 16, FontStyle.Normal, WardrobeLook.Label);
            OnlineWindow.Place(_wait.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(Inset, Inset), new Vector2(-Inset, -Head));
            _wait.alignment = TextAnchor.MiddleCenter;

            _scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            _scrollGo.transform.SetParent(panelGo.transform, false);
            var srt = (RectTransform)_scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(Inset, Inset), new Vector2(-Inset, -Head));
            var simg = _scrollGo.GetComponent<Image>();
            simg.color = WardrobeLook.Field;
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            var scroll = _scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(_scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Cell, CardH);
            grid.spacing = new Vector2(Space, Space);
            grid.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            _layout = grid;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cont;
            scroll.viewport = srt;
            _grid = contentGo.transform;
            _scrollGo.SetActive(false);
            Fit(count + (_current > 0 ? 1 : 0));
        }
    }
}
