using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WardrobePicker
    {
        private const float ListTop = 244f;
        private const int Limit = 500;

        private sealed class Row
        {
            internal WardrobeThing Thing;
            internal Image Icon;
            internal bool Done;
        }

        private sealed class Chip
        {
            internal Image Back;
            internal Text Label;
            internal Func<bool> On;
            internal Color32 Color;
        }

        private static GameObject _go;
        private static RectTransform _content;
        private static ScrollRect _scroll;
        private static Text _title;
        private static Text _count;
        private static int _slot;
        private static bool _fitOnly;
        private static int _from;
        private static InputField _fromInput;
        private static Text _upTo;
        private static string _query = "";
        private static float _refillAt;
        private static float _iconsAt;
        private static readonly HashSet<int> Hidden = new HashSet<int>();
        private static readonly List<Row> Rows = new List<Row>();
        private static readonly List<Chip> Chips = new List<Chip>();

        internal static bool IsOpen => _go != null;

        internal static void Open(RectTransform area, int slot)
        {
            Close();
            if (area == null) return;
            _slot = slot;
            _fitOnly = false;
            _go = new GameObject("QoLWardrobePicker", typeof(RectTransform), typeof(Image));
            _go.transform.SetParent(area, false);
            var rt = (RectTransform)_go.transform;
            OnlineWindow.Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var back = _go.GetComponent<Image>();
            back.color = WardrobeLook.Card;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;

            float width = area.rect.width > 10f ? area.rect.width : 440f;
            bool arts = WardrobeData.ArtKinds(slot).Count > 0;
            float artX = width - 10f - 34f - 10f - 120f;

            _title = OnlineWindow.Label(rt, "", 18, FontStyle.Bold, WardrobeLook.Bright);
            Wardrobe.At(_title.rectTransform, 14f, 10f, (arts ? artX : width - 54f) - 22f, 34f);
            _title.alignment = TextAnchor.MiddleLeft;
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = 12;
            _title.resizeTextMaxSize = 18;
            var close = Wardrobe.Arrow(rt, "×", Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 32f);
            crt.anchoredPosition = new Vector2(-10f, -11f);
            if (arts)
            {
                var art = Wardrobe.Arrow(rt, "Артефакт", () => WardrobeArt.Open(rt, _slot));
                Wardrobe.At((RectTransform)art.transform, artX, 11f, 120f, 32f);
                art.GetComponent<Image>().color = WardrobeLook.Mix(WardrobeLook.Button, WardrobeData.RarityColor(WardrobeArt.Rarity), 0.45f);
                var artLabel = art.GetComponentInChildren<Text>();
                if (artLabel != null) artLabel.fontSize = 15;
            }

            var search = OnlineWindow.MakeInput(rt, width - 28f, "поиск по названию");
            WardrobeLook.Style(search);
            Wardrobe.At((RectTransform)search.transform, 14f, 56f, width - 28f, 32f);
            search.text = _query;
            search.onValueChanged.AddListener(value => { _query = value ?? ""; _refillAt = Time.unscaledTime + 0.3f; });

            var rarities = WardrobeData.RarityOrder;
            const int PerRow = 4;
            const float Gap = 8f;
            float chipW = (width - 28f - (PerRow - 1) * Gap) / PerRow;
            for (int i = 0; i < rarities.Length; i++)
            {
                int rarity = rarities[i];
                MakeChip(rt, WardrobeData.RarityGroup(rarity), 14f + (i % PerRow) * (chipW + Gap), 100f + (i / PerRow) * 36f, chipW, 30f,
                    () => !Hidden.Contains(rarity),
                    () => { if (!Hidden.Remove(rarity)) Hidden.Add(rarity); Fill(); },
                    WardrobeData.RarityColor(rarity));
            }

            const float Row = 180f;
            var fromLabel = OnlineWindow.Label(rt, "С уровня", 14, FontStyle.Normal, WardrobeLook.Label);
            Wardrobe.At(fromLabel.rectTransform, 14f, Row, 72f, 30f);
            fromLabel.alignment = TextAnchor.MiddleLeft;
            var down = Wardrobe.Arrow(rt, "‹", () => StepFrom(-1));
            Wardrobe.At((RectTransform)down.transform, 88f, Row + 1f, 28f, 28f);
            _fromInput = WardrobeArt.Number(rt, 48f, 2);
            Wardrobe.At((RectTransform)_fromInput.transform, 120f, Row + 1f, 48f, 28f);
            _fromInput.onEndEdit.AddListener(value =>
            {
                int number;
                if (!int.TryParse(value, out number)) number = 0;
                _from = Mathf.Clamp(number, 0, Wardrobe.S.Level);
                Fill();
            });
            var up = Wardrobe.Arrow(rt, "›", () => StepFrom(1));
            Wardrobe.At((RectTransform)up.transform, 172f, Row + 1f, 28f, 28f);
            _upTo = OnlineWindow.Label(rt, "", 14, FontStyle.Normal, WardrobeLook.Label);
            Wardrobe.At(_upTo.rectTransform, 206f, Row, width - 14f - 136f - 8f - 206f, 30f);
            _upTo.alignment = TextAnchor.MiddleLeft;
            _upTo.resizeTextForBestFit = true;
            _upTo.resizeTextMinSize = 10;
            _upTo.resizeTextMaxSize = 14;
            MakeChip(rt, "Можно надеть", width - 14f - 136f, Row, 136f, 30f, () => _fitOnly, () => { _fitOnly = !_fitOnly; Fill(); }, WardrobeLook.Accent);

            _count = OnlineWindow.Label(rt, "", 12, FontStyle.Normal, WardrobeLook.Faint);
            Wardrobe.At(_count.rectTransform, 14f, 218f, width - 28f, 20f);
            _count.alignment = TextAnchor.MiddleLeft;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(rt, false);
            var srt = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 8f), new Vector2(-8f, -ListTop));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
            _scroll.viewport = srt;

            Fill();
        }

        internal static void Close()
        {
            WardrobeArt.Close();
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            _content = null;
            _scroll = null;
            _title = null;
            _count = null;
            _fromInput = null;
            _upTo = null;
            _refillAt = 0f;
            Rows.Clear();
            Chips.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_go == null) return false;
            if (WardrobeArt.EscapeClose()) return true;
            Close();
            return true;
        }

        internal static void Refresh()
        {
            if (_go == null) return;
            Fill();
            WardrobeArt.Refresh();
        }

        private static void StepFrom(int step)
        {
            int top = Wardrobe.S.Level;
            _from = Mathf.Clamp(Math.Min(_from, top) + step, 0, top);
            Fill();
        }

        internal static void Tick()
        {
            if (_go == null) return;
            if (_refillAt > 0f && Time.unscaledTime >= _refillAt) { _refillAt = 0f; Fill(); }
            if (Time.unscaledTime < _iconsAt) return;
            _iconsAt = Time.unscaledTime + 0.15f;
            int budget = 24;
            foreach (var row in Rows)
            {
                if (row.Done || row.Icon == null) continue;
                var sprite = WardrobeIcons.Get(row.Thing.Image);
                if (sprite != null)
                {
                    row.Icon.sprite = sprite;
                    row.Icon.enabled = true;
                    row.Done = true;
                }
                if (--budget <= 0) break;
            }
        }

        private static void MakeChip(RectTransform parent, string text, float x, float y, float w, float h, Func<bool> on, Action toggle, Color32 color)
        {
            var button = Wardrobe.Arrow(parent, text, () => { toggle(); PaintChips(); });
            Wardrobe.At((RectTransform)button.transform, x, y, w, h);
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.fontSize = 14;
                label.fontStyle = FontStyle.Bold;
                label.resizeTextForBestFit = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
            Chips.Add(new Chip { Back = button.GetComponent<Image>(), Label = label, On = on, Color = color });
            PaintChips();
        }

        private static void PaintChips()
        {
            foreach (var chip in Chips)
            {
                bool on = chip.On();
                if (chip.Back != null)
                    chip.Back.color = on ? WardrobeLook.Mix(WardrobeLook.Tab, chip.Color, 0.42f) : WardrobeLook.Tab;
                if (chip.Label != null)
                    chip.Label.color = on ? WardrobeLook.Bright : WardrobeLook.Faint;
            }
        }

        private static bool Wanted(WardrobeThing thing, WardrobeState s)
        {
            int slot = _slot == 11 ? 12 : _slot;
            if (!WardrobeData.Fits(slot, thing.Sub) || thing.Level > s.Level || thing.Level < Math.Min(_from, s.Level)) return false;
            if (Hidden.Contains(thing.Rarity)) return false;
            if (_fitOnly && !s.Fits(thing)) return false;
            if (_query.Length > 0 && thing.Name.IndexOf(_query, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private static void Fill()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            Rows.Clear();
            var s = Wardrobe.S;
            if (_title != null) _title.text = WardrobeData.SlotName(_slot);
            string from = Math.Min(_from, s.Level).ToString();
            if (_fromInput != null && !_fromInput.isFocused && _fromInput.text != from) _fromInput.text = from;
            if (_upTo != null) _upTo.text = "по " + s.Level + " ур.";
            PaintChips();

            var list = new List<WardrobeThing>();
            foreach (var thing in WardrobeData.Things)
                if (Wanted(thing, s)) list.Add(thing);
            int bit = s.ClassId > 0 ? 1 << (s.ClassId - 1) : 0;
            list.Sort((a, b) =>
            {
                int c = ((b.Classes & bit) != 0).CompareTo((a.Classes & bit) != 0);
                if (c != 0) return c;
                c = b.Level.CompareTo(a.Level);
                if (c != 0) return c;
                c = b.Rarity.CompareTo(a.Rarity);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            int mine = 0;
            foreach (var thing in list) if ((thing.Classes & bit) != 0) mine++;

            WardrobeThing worn;
            s.Worn.TryGetValue(_slot, out worn);
            string raw;
            if (worn != null) Clear("Снять «" + worn.Name + "»");
            else if (s.Unknown.TryGetValue(_slot, out raw)) Clear("Убрать «" + WardrobeState.Title(raw) + "» — её нет в базе");

            int baseRating = s.Rating;
            int shown = 0;
            var klass = s.Klass;
            if (mine > 0) Section("Для класса «" + (klass != null ? klass.Name : "?") + "»: " + mine);
            for (int i = 0; i < list.Count && shown < Limit; i++)
            {
                if (i == mine) Section(mine > 0 ? "Остальные: " + (list.Count - mine) : "Для класса «" + (klass != null ? klass.Name : "?") + "» вещей нет");
                Make(list[i], s, baseRating, worn == list[i]);
                shown++;
            }
            if (_count != null)
                _count.text = list.Count == 0
                    ? "Подходящих вещей нет: включи другое качество или смени уровень."
                    : "Вещей: " + list.Count + (list.Count > Limit ? ", показаны первые " + Limit : "") + ". Красные сейчас не надеть.";
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static void Clear(string caption)
        {
            var go = new GameObject("clear", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;
            var img = go.GetComponent<Image>();
            img.color = WardrobeLook.Mix(WardrobeLook.Card, WardrobeLook.Danger, 0.55f);
            img.sprite = OnlineWindow.Rounded(8);
            img.type = Image.Type.Sliced;
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            int slot = _slot;
            button.onClick.AddListener(() => Wardrobe.Pick(slot, null, false));
            var label = OnlineWindow.Label(go.transform, caption, 15, FontStyle.Bold, WardrobeLook.DangerText);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(12f, 0f), new Vector2(-12f, 0f));
            label.alignment = TextAnchor.MiddleLeft;
        }

        private static void Make(WardrobeThing thing, WardrobeState s, int baseRating, bool isWorn)
        {
            int target = _slot == 11 && !WardrobeData.Fits(11, thing.Sub) ? 12 : _slot;
            var group = WardrobeData.Group(target);
            bool fits = s.Fits(thing);
            var probe = s.Clone();
            bool can = fits || probe.Grant(thing);
            probe.Wear(target, thing);
            int delta = can ? probe.Rating - baseRating : 0;

            var go = new GameObject("thing" + thing.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_content, false);
            var row = go.GetComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 8, 8);
            row.spacing = 10f;
            row.childAlignment = TextAnchor.UpperLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = isWorn ? new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.14f)
                : fits ? new Color(1f, 1f, 1f, 0.04f)
                : new Color(WardrobeLook.Bad.r, WardrobeLook.Bad.g, WardrobeLook.Bad.b, 0.13f);
            if (isWorn)
            {
                var ol = go.AddComponent<Outline>();
                ol.effectColor = WardrobeLook.Accent;
                ol.effectDistance = new Vector2(1.5f, -1.5f);
            }
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            button.colors = colors;
            int slot = _slot;
            button.onClick.AddListener(() => Wardrobe.Pick(slot, thing, false));

            var frameGo = new GameObject("frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frameGo.transform.SetParent(go.transform, false);
            var fle = frameGo.GetComponent<LayoutElement>();
            fle.minWidth = fle.preferredWidth = 60f;
            fle.minHeight = fle.preferredHeight = 60f;
            var frame = frameGo.GetComponent<Image>();
            frame.sprite = OnlineWindow.Rounded(8);
            frame.type = Image.Type.Sliced;
            frame.color = WardrobeData.RarityColor(thing.Rarity);
            frame.raycastTarget = false;
            var inner = Wardrobe.Box(frameGo.transform, "inner", 2f, 2f, 56f, 56f, WardrobeLook.Field, 6);
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(inner, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var icon = iconGo.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = false;
            Rows.Add(new Row { Thing = thing, Icon = icon });

            var column = new GameObject("text", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            column.transform.SetParent(go.transform, false);
            column.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var lines = column.GetComponent<VerticalLayoutGroup>();
            lines.spacing = 3f;
            lines.childAlignment = TextAnchor.UpperLeft;
            lines.childControlWidth = true;
            lines.childControlHeight = true;
            lines.childForceExpandWidth = true;
            lines.childForceExpandHeight = false;
            Line(column.transform, thing.Name, 15, FontStyle.Bold, WardrobeData.RarityColor(thing.Rarity));
            Line(column.transform, Needs(thing, s), 12, FontStyle.Normal, WardrobeLook.Faint);
            Line(column.transform, Gives(thing), 13, FontStyle.Normal, WardrobeLook.Label);

            var side = new GameObject("score", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            side.transform.SetParent(go.transform, false);
            var sle = side.GetComponent<LayoutElement>();
            sle.minWidth = sle.preferredWidth = 84f;
            var stack = side.GetComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.UpperRight;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            var score = Line(side.transform, can ? (delta > 0 ? "+" + delta : delta.ToString()) : "—", 17, FontStyle.Bold,
                !can ? WardrobeLook.Faint : delta > 0 ? WardrobeLook.Good : delta < 0 ? WardrobeLook.Bad : WardrobeLook.Label);
            score.alignment = TextAnchor.UpperRight;
            var caption = Line(side.transform, isWorn ? "надето" : "рейтинг", 11, FontStyle.Normal, WardrobeLook.Faint);
            caption.alignment = TextAnchor.UpperRight;
            if (group != null)
            {
                var all = Wardrobe.Arrow(side.transform, "Надеть " + group.Length, () => Wardrobe.Pick(slot, thing, true));
                var ale = all.gameObject.AddComponent<LayoutElement>();
                ale.minHeight = ale.preferredHeight = 26f;
                all.GetComponent<Image>().color = WardrobeLook.Button;
                var label = all.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.fontSize = 12;
                    label.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }
        }

        private static void Section(string text)
        {
            var label = OnlineWindow.Label(_content, text, 14, FontStyle.Bold, WardrobeLook.Accent);
            label.alignment = TextAnchor.MiddleLeft;
            var le = label.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 26f;
        }

        private static Text Line(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(parent, text, size, style, color);
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        private static string Needs(WardrobeThing thing, WardrobeState s)
        {
            var text = new StringBuilder();
            text.Append("ур. ").Append(thing.Level);
            string rarity = WardrobeData.RarityName(thing.Rarity);
            if (rarity.Length > 0) text.Append(", ").Append(rarity);
            bool any = false;
            foreach (int i in WardrobeData.StatOrder)
            {
                if (thing.Req[i] <= 0) continue;
                text.Append(any ? ", " : " · треб.: ");
                any = true;
                bool short1 = s.Base(i) < thing.Req[i];
                if (short1) text.Append("<color=#f07a6e>");
                text.Append(WardrobeData.StatNames[i]).Append(' ').Append(thing.Req[i]);
                if (short1) text.Append("</color>");
            }
            return text.ToString();
        }

        internal static string Gives(WardrobeThing thing)
        {
            var parts = new List<string>();
            foreach (int i in WardrobeData.StatOrder)
                if (thing.Bonus[i] != 0) parts.Add(WardrobeData.StatNames[i] + " " + Signed(thing.Bonus[i]));
            if (thing.Energy != 0) parts.Add("Энергия " + Signed(thing.Energy));
            var armor = new List<string>();
            for (int p = 0; p < 5; p++)
                if (thing.Armor[p] != 0) armor.Add(WardrobeData.ArmorNames[p].ToLowerInvariant() + " " + thing.Armor[p]);
            if (armor.Count > 0) parts.Add("броня: " + string.Join(", ", armor.ToArray()));
            if (thing.Magic[0] != 0 || thing.Magic[1] != 0 || thing.Magic[2] != 0)
                parts.Add("защита " + thing.Magic[0] + "/" + thing.Magic[1] + "/" + thing.Magic[2]);
            if (thing.DamageMax > 0) parts.Add("урон " + thing.DamageMin + "–" + thing.DamageMax + (thing.Range > 0 ? ", дальность " + thing.Range : ""));
            if (WardrobeData.IsTwoHand(thing.Sub)) parts.Add("двуручное");
            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "без прибавок";
        }

        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();
    }
}
