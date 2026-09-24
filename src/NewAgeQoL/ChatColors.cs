using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ChatColors
    {
        internal sealed class Kind
        {
            internal string Key;
            internal string Title;
            internal string Blank = "";
            internal ConfigEntry<string> Cfg;
        }

        internal static readonly Kind[] All =
        {
            new Kind { Key = "Common", Title = "Общий чат", Blank = "#FFFFFF" },
            new Kind { Key = "Private", Title = "Личные сообщения", Blank = "#4FD5FF" },
            new Kind { Key = "Clan", Title = "Клан", Blank = "#3BE06B" },
            new Kind { Key = "Alliance", Title = "Альянс", Blank = "#E070E0" },
            new Kind { Key = "Team", Title = "Команда в бою", Blank = "#45E0D0" },
            new Kind { Key = "Troop", Title = "Отряд", Blank = "#FFC65A" },
            new Kind { Key = "Group", Title = "Поиск группы", Blank = "#C6C6FF" },
            new Kind { Key = "System", Title = "Системные и прочие", Blank = "#FFFFFF" },
        };

        private static readonly Color GameText = new Color32(253, 224, 127, 255);

        private static readonly string[] Swatches =
        {
            "#FFFFFF", "#FFD6D1", "#FFE2C7", "#FFF3B0", "#D9F7C8", "#C8F5EC", "#CDEFFF", "#D6E2FF", "#E4D9FF", "#FFD6EC",
            "#E3E6EA", "#FFB0A6", "#FFC99A", "#FFE58A", "#B6ECA0", "#9DEBDC", "#9EDFFF", "#AFC6FF", "#C6C6FF", "#FFB3D9",
            "#C4CAD2", "#FF8A7A", "#FFAD66", "#FFD560", "#8FE07A", "#6FE0CB", "#6FCFFF", "#8AAAFF", "#B394FF", "#FF8CC6",
            "#ACB3BD", "#FF6B5B", "#FF9A45", "#FFC65A", "#3BE06B", "#45E0D0", "#4FD5FF", "#6E93FF", "#A07CFF", "#E070E0",
            "#8E96A3", "#E8483A", "#F07F1F", "#E2B85C", "#2FBF55", "#1FBFAE", "#22AEE6", "#4F72F0", "#8A5CF5", "#D14FB8",
        };

        internal static void Bind()
        {
            foreach (var kind in All)
            {
                var one = kind;
                Chars.Own("Chat", "Tint" + one.Key, "",
                    "Цвет строк «" + one.Title + "» в чате мода, в виде #RRGGBB. Пусто — цвет по умолчанию" + (one.Blank.Length > 0 ? " " + one.Blank : ", как в игре") + ". Меняется в настройках мода.",
                    e =>
                    {
                        one.Cfg = e;
                        e.SettingChanged += (s, a) => ChatDock.Recolor();
                        ChatDock.Recolor();
                    });
            }
        }

        internal static string Hex(Kind kind)
        {
            string raw = kind.Cfg != null ? (kind.Cfg.Value ?? "").Trim() : "";
            if (raw.Length == 0) return "";
            if (raw[0] != '#') raw = "#" + raw;
            Color parsed;
            return raw.Length == 7 && ColorUtility.TryParseHtmlString(raw, out parsed) ? raw.ToUpperInvariant() : "";
        }

        private static string Now(Kind kind)
        {
            string hex = Hex(kind);
            return hex.Length > 0 ? hex : kind.Blank;
        }

        internal static Color Of(Kind kind)
        {
            string hex = Now(kind);
            return hex.Length > 0 ? Paint(hex) : GameText;
        }

        internal static void Show(Text text, Kind kind)
        {
            if (text == null || kind == null) return;
            text.text = Now(kind).Length > 0 ? "Пример" : "Как в игре";
            text.color = Of(kind);
        }

        private static string Sample(Kind kind)
        {
            string hex = Now(kind);
            if (kind == All[All.Length - 1]) return "[13:47] Создан хаотический бой с 17 уровня";
            if (hex.Length > 0) return "[13:48] Ник: так выглядит строка";
            return "<color=#FFFFFF>[13:48]</color> <color=#88B8FF>Ник</color>: так выглядит строка";
        }

        private static Color Paint(string hex)
        {
            Color parsed;
            return ColorUtility.TryParseHtmlString(hex, out parsed) ? parsed : Color.white;
        }

        internal static Color Base => Of(All[All.Length - 1]);

        private static Kind For(EChatMessageType type)
        {
            switch (type)
            {
                case EChatMessageType.MSG_COMMON: return All[0];
                case EChatMessageType.MSG_PRIVATE:
                case EChatMessageType.MSG_OFFLINE: return All[1];
                case EChatMessageType.MSG_CLAN: return All[2];
                case EChatMessageType.MSG_ALLIANCE: return All[3];
                case EChatMessageType.MSG_TEAM: return All[4];
                case EChatMessageType.MSG_TROOP: return All[5];
                case EChatMessageType.MSG_FIND_GROUP: return All[6];
                default: return null;
            }
        }

        internal static string Tint(EChatMessageType type)
        {
            var kind = For(type);
            if (kind == null) return null;
            string hex = Now(kind);
            return hex.Length > 0 ? hex : null;
        }

        private const float Pad = 20f;
        private const float Cell = 26f;
        private const float Gap = 6f;
        private const int Columns = 10;
        private const float Wide = 376f;
        private const float High = 310f;
        private const float Grid = 88f;
        private const float FieldW = 104f;
        private const float ResetW = 118f;
        private const float ReadyW = 94f;
        private const float Space = 10f;

        private static GameObject _pick;
        private static readonly List<KeyValuePair<string, Outline>> Rings = new List<KeyValuePair<string, Outline>>();

        internal static bool EscapeClose()
        {
            if (_pick == null) return false;
            Close();
            return true;
        }

        internal static void Close()
        {
            if (_pick != null) UnityEngine.Object.Destroy(_pick);
            _pick = null;
            Rings.Clear();
        }

        internal static void Pick(Kind kind, RectTransform host, Action done)
        {
            Close();
            if (kind == null || kind.Cfg == null || host == null) return;
            try
            {
                _pick = new GameObject("QoLChatColor", typeof(RectTransform), typeof(LayoutElement));
                _pick.GetComponent<LayoutElement>().ignoreLayout = true;
                _pick.transform.SetParent(host, false);
                OnlineWindow.Place((RectTransform)_pick.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                _pick.transform.SetAsLastSibling();

                var dim = new GameObject("dim", typeof(RectTransform), typeof(Image), typeof(Button));
                dim.transform.SetParent(_pick.transform, false);
                OnlineWindow.Place((RectTransform)dim.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
                var away = dim.GetComponent<Button>();
                away.transition = Selectable.Transition.None;
                away.onClick.AddListener(Close);

                var box = new GameObject("box", typeof(RectTransform), typeof(Image), typeof(Outline));
                box.transform.SetParent(_pick.transform, false);
                var brt = (RectTransform)box.transform;
                brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
                brt.pivot = new Vector2(0.5f, 0.5f);
                brt.sizeDelta = new Vector2(Wide, High);
                var back = box.GetComponent<Image>();
                back.color = WardrobeLook.Popup;
                back.sprite = OnlineWindow.Rounded(16);
                back.type = Image.Type.Sliced;
                WardrobeLook.Frame(box, WardrobeLook.Edge);

                var title = OnlineWindow.Label(box.transform, "Цвет: " + kind.Title, 16, FontStyle.Bold, WardrobeLook.Bright);
                title.alignment = TextAnchor.MiddleLeft;
                title.raycastTarget = false;
                Top(title.rectTransform, Pad, 16f, Wide - Pad * 2f, 22f);

                var strip = new GameObject("sample", typeof(RectTransform), typeof(Image));
                strip.transform.SetParent(box.transform, false);
                Top((RectTransform)strip.transform, Pad, 46f, Wide - Pad * 2f, 30f);
                var paper = strip.GetComponent<Image>();
                paper.color = WardrobeLook.Field;
                paper.sprite = OnlineWindow.Rounded(8);
                paper.type = Image.Type.Sliced;
                paper.raycastTarget = false;
                var sample = OnlineWindow.Label(strip.transform, Sample(kind), 14, FontStyle.Normal, Of(kind));
                sample.supportRichText = true;
                sample.alignment = TextAnchor.MiddleLeft;
                sample.raycastTarget = false;
                OnlineWindow.Place(sample.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 0f), new Vector2(-10f, 0f));

                InputField field = null;
                Action<string> choose = hex =>
                {
                    kind.Cfg.Value = hex;
                    string now = Now(kind);
                    sample.text = Sample(kind);
                    sample.color = Of(kind);
                    if (field != null) field.SetTextWithoutNotify(now);
                    Mark(now);
                    if (done != null) done();
                };

                float grid = Columns * Cell + (Columns - 1) * Gap;
                float left = (Wide - grid) * 0.5f;
                for (int i = 0; i < Swatches.Length; i++)
                {
                    string hex = Swatches[i];
                    var cell = new GameObject("swatch", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
                    cell.transform.SetParent(box.transform, false);
                    Top((RectTransform)cell.transform, left + (i % Columns) * (Cell + Gap), Grid + (i / Columns) * (Cell + Gap), Cell, Cell);
                    var face = cell.GetComponent<Image>();
                    face.color = Paint(hex);
                    face.sprite = OnlineWindow.Rounded(8);
                    face.type = Image.Type.Sliced;
                    var ring = cell.GetComponent<Outline>();
                    ring.effectDistance = new Vector2(2f, -2f);
                    Rings.Add(new KeyValuePair<string, Outline>(hex, ring));
                    var press = cell.GetComponent<Button>();
                    press.targetGraphic = face;
                    var tints = press.colors;
                    tints.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
                    tints.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                    tints.selectedColor = Color.white;
                    press.colors = tints;
                    press.onClick.AddListener(() => choose(hex));
                }
                Mark(Now(kind));

                int rows = (Swatches.Length + Columns - 1) / Columns;
                float row = Grid + rows * Cell + (rows - 1) * Gap + 16f;
                field = OnlineWindow.MakeInput(box.transform, FieldW, "#RRGGBB");
                Top((RectTransform)field.transform, Pad, row, FieldW, 34f);
                field.characterLimit = 7;
                field.SetTextWithoutNotify(Now(kind));
                field.onEndEdit.AddListener(text =>
                {
                    string hex = (text ?? "").Trim();
                    if (hex.Length == 0) { choose(""); return; }
                    if (hex[0] != '#') hex = "#" + hex;
                    Color parsed;
                    if (hex.Length == 7 && ColorUtility.TryParseHtmlString(hex, out parsed)) choose(hex.ToUpperInvariant());
                    else field.SetTextWithoutNotify(Now(kind));
                });

                var reset = OnlineWindow.MakeGameButton(box.transform, "По умолчанию", ResetW, 34f, () => choose(""));
                Top((RectTransform)reset.transform, Pad + FieldW + Space, row, ResetW, 34f);

                var ready = OnlineWindow.MakeGameButton(box.transform, "Готово", ReadyW, 34f, Close);
                Top((RectTransform)ready.transform, Wide - Pad - ReadyW, row, ReadyW, 34f);
                var readyFace = ready.GetComponent<Image>();
                if (readyFace != null) readyFace.color = WardrobeLook.Accent;
                var readyText = ready.GetComponentInChildren<Text>(true);
                if (readyText != null) readyText.color = WardrobeLook.OnAccent;
            }
            catch (Exception e)
            {
                Plugin.Fault("[цвета чата] окно выбора: " + e.Message);
                Close();
            }
        }

        private static void Mark(string hex)
        {
            foreach (var pair in Rings)
                if (pair.Value != null)
                    pair.Value.effectColor = string.Equals(pair.Key, hex, StringComparison.OrdinalIgnoreCase)
                        ? WardrobeLook.Accent
                        : new Color(0f, 0f, 0f, 0f);
        }

        private static void Top(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, -y);
        }
    }
}
