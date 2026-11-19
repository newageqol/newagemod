using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Settings
    {
        private static GameObject _win;
        private static readonly Dictionary<object, Action> _undo = new Dictionary<object, Action>();

        private abstract class RowDef { internal string Title; }

        private sealed class Header : RowDef { }

        private sealed class BoolRow : RowDef { internal ConfigEntry<bool> Cfg; }

        private sealed class SliderRow : RowDef { internal ConfigEntry<float> Cfg; internal float Min; internal float Max; internal Action<float> OnChange; }

        private sealed class KeyRow : RowDef { internal ConfigEntry<string> Cfg; }

        private sealed class HotRow : RowDef { internal string Key; }

        private sealed class ActionRow : RowDef { internal string ButtonText; internal Action Do; internal Func<string> TitleNow; internal Func<string> ButtonNow; }

        private sealed class CycleRow : RowDef { internal ConfigEntry<int> Cfg; internal Func<int, string> Name; internal int Count; }

        private static readonly List<(Text label, Text value, ActionRow row)> Lives = new List<(Text, Text, ActionRow)>();

        private sealed class PickRow : RowDef { internal ConfigEntry<string> ByName; internal ConfigEntry<int> ById; internal System.Func<string> Display; }

        private sealed class ColorRow : RowDef { internal ChatColors.Kind Kind; }

        private sealed class ValueRow : RowDef
        {
            internal Func<string> Get;
            internal Action<string> Set;
            internal Action Reset;
            internal Action Remember;
        }

        private static List<RowDef> Rows()
        {
            if (_keysMode) return KeyRows();

            var rows = new List<RowDef>
            {
                new Header { Title = Lang.T("Мод") },
                new ActionRow { Title = UpdateTitle(), ButtonText = UpdateButton(), Do = Changelog.Toggle, TitleNow = UpdateTitle, ButtonNow = UpdateButton },
                A(Lang.T("Выключить мод и вернуть обычный клиент (после перезапуска игры)"), Lang.T("Выключить"), ModSwitch.AskOff),
                AssetSync.Pending
                    ? A(Lang.F("Не скачаны локации мода: {0} — {1} МБ", AssetSync.Places, AssetSync.PendingSize), Lang.T("Скачать"), AssetSync.Retry)
                    : null,
                B(Lang.T("Новые локации и экран загрузки мода"), Plugin.CfgNewPlaces),
                B(Lang.T("Новые 3D-арены в боях"), Plugin.CfgNewArenas),
                new Header { Title = Lang.T("Кнопки") },
                B(Lang.T("Возврат в Иллениум ведёт на арену, к турнирам"), Plugin.CfgTownTournament),
                B(Lang.T("Кнопка запасного набора"), Plugin.CfgArtifactButtons),
                Artifacts.Pending > 0
                    ? A(Lang.F("Ждут возврата: {0}", Artifacts.Pending), Lang.T("Забыть"), Artifacts.ForgetStash)
                    : null,

                new Header { Title = Lang.T("Банки") },
                B(Lang.T("После боя пополнять жизнь, ману и энергию"), Plugin.CfgFlaskAfterFight),
                B(Lang.T("После боя пополнять заряды"), Plugin.CfgChargesAfterFight),
                Sl(Lang.T("Размер панели банок в бою"), Plugin.CfgPotionScale, 0.7f, 1.6f),

                new Header { Title = Lang.T("Бой") },
                B(Lang.T("Контрприём на себя в первой фазе"), Plugin.CfgCounterAuto),


                new Header { Title = Lang.T("Звуки") },
                Sl(Lang.T("Громкость звуков боя"), Plugin.CfgSoundVolume, 0f, 1f, v => Sounds.Sample(false)),
                Sl(Lang.T("Громкость звука привата"), Plugin.CfgPmVolume, 0f, 1f, v => Sounds.Sample(true)),
                Sl(Lang.T("Громкость музыки боя"), Plugin.CfgMusicVolume, 0f, 1f),
                A(Lang.T("Авторы музыки боя"), Lang.T("Показать"), () => Notice.Show(Lang.T("Музыка боя: Cold Stone Reverb, Procession of Ashes, Heartbeat Groove, Pulse of the Void, Menacing Waltz, Stone Room Reverb, Dripping in the Dark, The Viol's Warning. Треки сделал автор мода."), 10f)),

                new Header { Title = "" },
            };
            if (FlashLook.Fight)
            {
                rows.InsertRange(rows.Count - 1, new List<RowDef>
                {
                    new Header { Title = Lang.T("Внешний вид") },
                    A(Lang.T("Кэш вещей Flash, как у нового игрока"), Lang.T("Сбросить"), () => Notice.Show(FlashLook.ResetCache() ?? Lang.T("Кэш вещей Flash сбросить не удалось"), 6f)),
                });
            }
            int sounds = rows.FindIndex(r => r is Header && r.Title == Lang.T("Звуки"));
            rows.InsertRange(sounds >= 0 ? sounds : rows.Count - 1, Tints());
            rows.RemoveAll(r => r == null);
            return rows;
        }

        private static List<RowDef> Tints()
        {
            var rows = new List<RowDef>
            {
                new Header { Title = Lang.T("Цвета чата") },
                new ActionRow
                {
                    Title = Lang.T("Цвета по видам сообщений"),
                    ButtonText = TintsButton(),
                    Do = ToggleTints,
                    TitleNow = () => Lang.T("Цвета по видам сообщений"),
                    ButtonNow = TintsButton,
                },
            };
            foreach (var kind in ChatColors.All)
                if (kind.Cfg != null) rows.Add(new ColorRow { Title = Lang.T(kind.Title), Kind = kind });
            return rows;
        }

        private static bool _tintsOpen;
        private static readonly List<GameObject> TintRows = new List<GameObject>();

        private static string TintsButton() => _tintsOpen ? Lang.T("Скрыть") : Lang.T("Показать");

        private static void ToggleTints()
        {
            _tintsOpen = !_tintsOpen;
            Transform list = null;
            foreach (var go in TintRows)
            {
                if (go == null) continue;
                go.SetActive(_tintsOpen);
                list = go.transform.parent;
            }
            if (list is RectTransform rt) LayoutRebuilder.MarkLayoutForRebuild(rt);
        }

        private static List<RowDef> KeyRows()
        {
            var rows = new List<RowDef>();
            string group = null;
            foreach (var act in Hotkeys.All())
            {
                if (act.Group != group)
                {
                    group = act.Group;
                    rows.Add(new Header { Title = Lang.T(group) });
                }
                rows.Add(new HotRow { Title = act.Title, Key = act.Key });
            }
            rows.Add(new Header { Title = "" });
            return rows;
        }

        private static RowDef Pick(string title, ConfigEntry<string> byName, ConfigEntry<int> byId)
        {
            if (byName == null || byId == null) return null;
            return new PickRow
            {
                Title = title,
                ByName = byName,
                ById = byId,
                Display = () =>
                {
                    if (!string.IsNullOrEmpty(byName.Value)) return byName.Value;
                    if (byId.Value > 0) { var n = Flasks.DisplayName(byId.Value); return n ?? byId.Value.ToString(); }
                    return Lang.T("— выбрать —");
                },
            };
        }

        private static RowDef B(string title, ConfigEntry<bool> cfg) =>
            cfg == null ? null : new BoolRow { Title = title, Cfg = cfg };

        private static RowDef Sl(string title, ConfigEntry<float> cfg, float min, float max, Action<float> onChange = null) =>
            cfg == null ? null : new SliderRow { Title = title, Cfg = cfg, Min = min, Max = max, OnChange = onChange };

        private static RowDef S(string title, ConfigEntry<string> cfg) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value ?? "",
                Set = v => cfg.Value = (v ?? "").Trim(),
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (string)cfg.DefaultValue,
            };

        private static string UpdateTitle()
        {
            if (Updater.State == Updater.Stage.Done) return Updater.Message;
            string head = Lang.F("Версия мода {0}", Plugin.Version);
            return Updater.State == Updater.Stage.Idle ? head : head + " · " + Updater.Message;
        }

        private static string UpdateButton()
        {
            switch (Updater.State)
            {
                case Updater.Stage.Checking: return Lang.T("Проверяю…");
                case Updater.Stage.Downloading: return Lang.T("Качаю…");
                case Updater.Stage.Done: return Lang.T("Готово");
                default: return Updater.CanUpdate ? Lang.T("Обновить") : Lang.T("Что нового");
            }
        }

        private static void Relive()
        {
            foreach (var (label, value, row) in Lives)
            {
                if (label != null) { string t = row.TitleNow(); if (label.text != t) label.text = t; }
                if (value != null) { string b = row.ButtonNow(); if (value.text != b) value.text = b; }
            }
        }

        private static RowDef A(string title, string buttonText, Action act) =>
            new ActionRow { Title = title, ButtonText = buttonText, Do = act };

        private static RowDef K(string title, ConfigEntry<string> cfg) =>
            cfg == null ? null : new KeyRow { Title = title, Cfg = cfg };

        private static RowDef I(string title, ConfigEntry<int> cfg, int min = int.MinValue, int max = int.MaxValue) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value.ToString(),
                Set = v => { if (int.TryParse(v.Trim(), out int n)) cfg.Value = Mathf.Clamp(n, min, max); },
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (int)cfg.DefaultValue,
            };

        private static RowDef F(string title, ConfigEntry<float> cfg) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Set = v =>
                {
                    if (float.TryParse(v.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out float n)) cfg.Value = n;
                },
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (float)cfg.DefaultValue,
            };


        private static bool _keysMode;

        internal static void Toggle()
        {
            if (_win != null) { Close(); return; }
            _keysMode = false;
            try { Open(); }
            catch (Exception e) { Plugin.Fault("[settings] window did not open: " + e); Close(); }
        }

        internal static void ToggleHotkeys()
        {
            if (_win != null) { Close(); return; }
            _keysMode = true;
            try { Open(); }
            catch (Exception e) { Plugin.Fault("[hotkeys] window did not open: " + e); Close(); }
        }

        internal static void Close() => Close(revert: false);

        internal static void Close(bool revert)
        {
            if (Capturing) { _capCfg = null; _capBg = null; _capText = null; _capBtn = null; _capMsgUntil = 0f; }
            if (Hotkeys.Capturing) Hotkeys.Stop();
            Changelog.Close();
            ChatColors.Close();
            if (revert) foreach (var u in _undo.Values) { try { u(); } catch (Exception e) { Plugin.Caught("[settings] revert: " + e.Message); } }
            if (_win != null) UnityEngine.Object.Destroy(_win);
            if (_ownCanvas != null)
            {
                try { CanvasFactory.ReleaseCanvas(ECanvasType.ModalWindow, _ownCanvas); }
                catch { UnityEngine.Object.Destroy(_ownCanvas); }
            }
            _ownCanvas = null;
            _win = null;
            _frame = null;
            _canvas = null;
            _undo.Clear();
            Lives.Clear();
            TintRows.Clear();
        }

        private static void Remember<T>(ConfigEntry<T> cfg)
        {
            if (cfg == null || _undo.ContainsKey(cfg)) return;
            T old = cfg.Value;
            _undo[cfg] = () => cfg.Value = old;
        }

        internal static void Keep(object cfg)
        {
            if (cfg != null) _undo.Remove(cfg);
        }

        private static void Open()
        {
            OpenOwn();
        }

        private static GameObject _ownCanvas;
        private static RectTransform _body;
        private static RectTransform _area;
        private static RectTransform[] _columns;
        private static Vector2 _fitSize = new Vector2(-1f, -1f);
        private static float _fitAt;

        private const float MostW = 1640f;
        private const float MostH = 980f;
        private const float Margin = 40f;
        private const float RowH = 30f;
        private const float HeadH = 26f;
        private const float CtrlW = 138f;
        private const float CtrlH = 24f;
        private const float Gutter = 14f;
        private const float BottomH = 58f;
        private const float TopH = 52f;

        internal static bool EscapeClose()
        {
            if (_win == null) return false;
            Close(revert: false);
            return true;
        }

        private static void OpenOwn()
        {
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.ModalWindow);
            _ownCanvas = canvas.gameObject;
            _canvas = canvas;
            _undo.Clear();
            Lives.Clear();
            TintRows.Clear();
            _tintsOpen = false;
            _fitSize = new Vector2(-1f, -1f);
            _fitAt = 0f;

            _win = new GameObject("QoLSettings", typeof(RectTransform));
            _win.transform.SetParent(canvas.transform, false);
            OnlineWindow.Place((RectTransform)_win.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var dim = new GameObject("dim", typeof(RectTransform), typeof(Image));
            dim.transform.SetParent(_win.transform, false);
            OnlineWindow.Place((RectTransform)dim.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panelGo.transform.SetParent(_win.transform, false);
            _frame = (RectTransform)panelGo.transform;
            _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
            _frame.pivot = new Vector2(0.5f, 0.5f);
            _frame.anchoredPosition = Vector2.zero;
            _frame.sizeDelta = new Vector2(1200f, 800f);
            var pimg = panelGo.GetComponent<Image>();
            pimg.color = WardrobeLook.Window;
            pimg.sprite = OnlineWindow.Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(_frame, _keysMode ? Lang.T("Горячие клавиши") : Lang.T("Настройки мода"), 22, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(20f, -TopH + 6f), new Vector2(-70f, -8f));
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;
            OnlineWindow.MakeCloseButton(_frame, () => Close(revert: false));

            var areaGo = new GameObject("area", typeof(RectTransform), typeof(RectMask2D));
            areaGo.transform.SetParent(_frame, false);
            _area = (RectTransform)areaGo.transform;
            OnlineWindow.Place(_area, Vector2.zero, Vector2.one, new Vector2(0.5f, 1f), new Vector2(16f, BottomH), new Vector2(-16f, -TopH));

            var bodyGo = new GameObject("body", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bodyGo.transform.SetParent(_area, false);
            _body = (RectTransform)bodyGo.transform;
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 1f);
            _body.pivot = new Vector2(0.5f, 1f);
            _body.anchoredPosition = Vector2.zero;
            var across = bodyGo.GetComponent<HorizontalLayoutGroup>();
            across.spacing = Gutter;
            across.childAlignment = TextAnchor.UpperLeft;
            across.childControlWidth = true;
            across.childControlHeight = true;
            across.childForceExpandWidth = true;
            across.childForceExpandHeight = false;

            var all = Rows();
            _columns = new RectTransform[Columns(all)];
            for (int i = 0; i < _columns.Length; i++)
            {
                var go = new GameObject("column" + i, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
                go.transform.SetParent(_body, false);
                var down = go.GetComponent<VerticalLayoutGroup>();
                down.spacing = 10f;
                down.childAlignment = TextAnchor.UpperLeft;
                down.childControlWidth = true;
                down.childControlHeight = true;
                down.childForceExpandWidth = true;
                down.childForceExpandHeight = false;
                var le = go.GetComponent<LayoutElement>();
                le.flexibleWidth = 1f;
                le.minWidth = 0f;
                _columns[i] = (RectTransform)go.transform;
            }

            var close = Pill(_frame, Lang.T("Закрыть"), WardrobeLook.Accent, WardrobeLook.OnAccent, () => Close(revert: false));
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(200f, 38f);
            crt.anchoredPosition = new Vector2(0f, 12f);

            var place = new List<Transform>();
            Spread(all, place);
            for (int i = 0; i < all.Count; i++)
            {
                var row = all[i];
                try { AddRow(null, place[i], row); }
                catch (Exception e) { Plugin.Fault("[settings] row '" + (row.Title ?? "?") + "': " + e); }
            }
            Fit();
        }

        private static int Columns(List<RowDef> all)
        {
            if (!_keysMode) return 3;
            int rows = 0;
            foreach (var row in all) rows += row is Header ? 2 : 1;
            return Mathf.Clamp(Mathf.CeilToInt(rows / 26f), 3, 5);
        }

        private static void Spread(List<RowDef> all, List<Transform> place)
        {
            var sections = new List<List<RowDef>>();
            foreach (var row in all)
            {
                if (row is Header || sections.Count == 0) sections.Add(new List<RowDef>());
                sections[sections.Count - 1].Add(row);
            }
            float total = 0f;
            foreach (var section in sections) total += Weight(section);
            int count = _columns.Length;
            float share = total / count;
            float filled = 0f;
            int column = 0;
            foreach (var section in sections)
            {
                float weight = Weight(section);
                if (column < count - 1 && filled > 0f && filled + weight * 0.5f > share * (column + 1)) column++;
                filled += weight;
                bool empty = section.Count == 1 && section[0] is Header && string.IsNullOrEmpty(section[0].Title);
                Transform host = empty ? (Transform)_columns[column] : Card(_columns[column]);
                foreach (var row in section) place.Add(host);
            }
        }

        private static float Weight(List<RowDef> section)
        {
            float n = 1.2f;
            foreach (var row in section) if (!(row is ColorRow)) n += 1f;
            return n;
        }

        private static Transform Card(RectTransform column)
        {
            var go = new GameObject("card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(column, false);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(12);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Card;
            back.raycastTarget = false;
            var edge = go.AddComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var down = go.GetComponent<VerticalLayoutGroup>();
            down.padding = new RectOffset(12, 10, 6, 8);
            down.spacing = 2f;
            down.childAlignment = TextAnchor.UpperLeft;
            down.childControlWidth = true;
            down.childControlHeight = true;
            down.childForceExpandWidth = true;
            down.childForceExpandHeight = false;
            return go.transform;
        }

        private static void OwnRow(Transform container, RowDef def)
        {
            var go = new GameObject("MvlRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(container, false);
            var le = go.GetComponent<LayoutElement>();
            if (def is Header)
            {
                bool blank = string.IsNullOrEmpty(def.Title);
                le.minHeight = le.preferredHeight = blank ? 0f : HeadH;
                if (blank) return;
                var head = OnlineWindow.Label(go.transform, def.Title.ToUpperInvariant(), 14, FontStyle.Bold, WardrobeLook.Accent);
                OnlineWindow.Place(head.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 0f), new Vector2(-2f, 0f));
                head.alignment = TextAnchor.MiddleLeft;
                head.raycastTarget = false;
                return;
            }
            le.minHeight = le.preferredHeight = RowH;

            var label = OnlineWindow.Label(go.transform, def.Title, 13, FontStyle.Normal, WardrobeLook.Body);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 0f), new Vector2(-(CtrlW + 10f), 0f));
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = 13;
            label.raycastTarget = false;

            var ctrlGo = new GameObject("KeyBackground", typeof(RectTransform), typeof(Image), typeof(Button));
            ctrlGo.transform.SetParent(go.transform, false);
            var crt = (RectTransform)ctrlGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(CtrlW, CtrlH);
            crt.anchoredPosition = Vector2.zero;
            var background = ctrlGo.GetComponent<Image>();
            background.sprite = OnlineWindow.Rounded(8);
            background.type = Image.Type.Sliced;
            background.color = WardrobeLook.Button;
            var button = ctrlGo.GetComponent<Button>();
            button.targetGraphic = background;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;

            var value = OnlineWindow.Label(ctrlGo.transform, "", 13, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            value.alignment = TextAnchor.MiddleCenter;
            value.raycastTarget = false;

            Fill(go, label, value, background, button, def);
        }

        private static Button Pill(Transform host, string text, Color back, Color ink, Action act)
        {
            var go = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.sprite = OnlineWindow.Rounded(10);
            img.type = Image.Type.Sliced;
            img.color = back;
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => act());
            var t = OnlineWindow.Label(go.transform, text, 16, FontStyle.Bold, ink);
            OnlineWindow.Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            return button;
        }

        private static void Fit()
        {
            if (_frame == null || _body == null || _area == null || _ownCanvas == null) return;
            var root = _ownCanvas.transform as RectTransform;
            if (root == null || root.rect.width < 50f) return;
            if (Time.unscaledTime < _fitAt) return;
            _fitAt = Time.unscaledTime + 0.2f;
            var size = new Vector2(Mathf.Min(MostW, root.rect.width - Margin * 2f), Mathf.Min(MostH, root.rect.height - Margin * 2f));
            if ((_frame.sizeDelta - size).sqrMagnitude > 0.25f) _frame.sizeDelta = size;
            float wide = _area.rect.width;
            float room = _area.rect.height;
            if (wide < 10f || room < 10f) return;
            float scale = _body.localScale.x > 0.01f ? _body.localScale.x : 1f;
            if (Mathf.Abs(_body.sizeDelta.x - wide / scale) > 0.5f)
                _body.sizeDelta = new Vector2(wide / scale, _body.sizeDelta.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
            float tall = LayoutUtility.GetPreferredHeight(_body);
            var key = new Vector2(wide * 10000f + room, tall);
            if ((key - _fitSize).sqrMagnitude < 0.25f) return;
            _fitSize = key;
            float want = tall > 1f ? Mathf.Min(1f, room / tall) : 1f;
            _body.localScale = new Vector3(want, want, 1f);
            _body.sizeDelta = new Vector2(wide / want, tall);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_body);
            if (want < 0.999f) Plugin.Trace("[settings] content scaled to " + want.ToString("0.00") + " to fit " + room.ToString("0") + " px");
        }

        private static void SpellIcon(Text label, string key)
        {
            if (label == null || key == null) return;
            int at = key.IndexOf(':');
            if (at <= 0) return;
            string kind = key.Substring(0, at);
            EQuickButtonType type;
            if (kind == "spell") type = EQuickButtonType.SPELL;
            else if (kind == "trick") type = EQuickButtonType.DODGE;
            else if (kind == "skill") type = EQuickButtonType.SKILL;
            else return;
            int id;
            if (!int.TryParse(key.Substring(at + 1), out id)) return;
            Sprite sprite = null;
            try { sprite = AtlasUtils.GetQuickButtonSprite(type, id, 0); }
            catch (System.Exception e) { Plugin.Caught("[settings] icon for " + key + ": " + e.Message); }
            if (sprite == null) return;
            const float size = 26f;
            var go = new GameObject("QoLSpellIcon", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            var row = label.transform.parent;
            if (row != null && row.GetComponent<HorizontalLayoutGroup>() != null)
            {
                const float gap = 8f;
                float tall = Mathf.Max(size, ((RectTransform)row).rect.height);
                var holder = new GameObject("QoLSpellIconSlot", typeof(RectTransform));
                holder.transform.SetParent(row, false);
                holder.transform.SetSiblingIndex(label.transform.GetSiblingIndex());
                ((RectTransform)holder.transform).sizeDelta = new Vector2(size + gap, tall);
                var le = holder.AddComponent<LayoutElement>();
                le.minWidth = le.preferredWidth = size + gap;
                le.minHeight = le.preferredHeight = tall;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 1f;
                go.transform.SetParent(holder.transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -4f);
            }
            else
            {
                go.transform.SetParent(label.transform, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-4f, 0f);
                var lrt = label.rectTransform;
                lrt.offsetMin = new Vector2(lrt.offsetMin.x + size + 6f, lrt.offsetMin.y);
            }
            rt.sizeDelta = new Vector2(size, size);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private static RectTransform _frame;
        private static Canvas _canvas;
        private static void AddRow(GameObject rowPrefab, Transform container, RowDef def)
        {
            OwnRow(container, def);
        }

        private static void Fill(GameObject go, Text label, Text value, Image background, Button button, RowDef def)
        {
            if (def is BoolRow b)
            {
                Remember(b.Cfg);
                SetSwitch(value, b.Cfg.Value);
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() =>
                    {
                        b.Cfg.Value = !b.Cfg.Value;
                        SetSwitch(value, b.Cfg.Value);
                    });
                }
                return;
            }

            if (def is SliderRow sl)
            {
                Remember(sl.Cfg);
                if (background == null || value == null) return;
                if (button != null) UnityEngine.Object.DestroyImmediate(button);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var slider = MakeSlider(background, sl.Min, sl.Max, sl.Cfg.Value);
                value.gameObject.SetActive(false);
                if (slider != null)
                {
                    slider.onValueChanged.AddListener(v =>
                    {
                        sl.Cfg.Value = v;
                        if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                            Plugin.Trace("[settings] " + def.Title + " = " + v.ToString("0.00", ci));
                        if (sl.OnChange != null) sl.OnChange(v);
                    });
                }
                return;
            }

            if (def is CycleRow cyc)
            {
                Remember(cyc.Cfg);
                if (value == null) return;
                value.text = cyc.Name(cyc.Cfg.Value);
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    var val = value;
                    button.onClick.AddListener(() =>
                    {
                        cyc.Cfg.Value = ((cyc.Cfg.Value % cyc.Count) + 1) % cyc.Count;
                        val.text = cyc.Name(cyc.Cfg.Value);
                    });
                }
                return;
            }

            if (def is ActionRow act)
            {
                if (value == null) return;
                value.text = act.ButtonText;
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => act.Do());
                }
                if (act.TitleNow != null && act.ButtonNow != null) Lives.Add((label, value, act));
                return;
            }

            if (def is KeyRow kr)
            {
                Remember(kr.Cfg);
                if (background == null || value == null) return;
                value.text = KeyName(kr.Cfg.Value);
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                value.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => StartCapture(kr.Cfg, background, value, button));
                }
                return;
            }



            if (def is HotRow hot)
            {
                SpellIcon(label, hot.Key);
                if (background == null || value == null) return;
                value.text = Hotkeys.Text(Hotkeys.Of(hot.Key));
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 8;
                value.resizeTextMaxSize = value.fontSize;
                value.horizontalOverflow = HorizontalWrapMode.Wrap;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(() => Hotkeys.Begin(hot.Key, value));
                }
                return;
            }

            if (def is ColorRow tone)
            {
                Remember(tone.Kind.Cfg);
                TintRows.Add(go);
                go.SetActive(_tintsOpen);
                if (value == null) return;
                ChatColors.Show(value, tone.Kind);
                if (background != null) background.color = WardrobeLook.Field;
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    var swatch = value;
                    button.onClick.AddListener(() => ChatColors.Pick(tone.Kind, _frame,
                        () => ChatColors.Show(swatch, tone.Kind)));
                }
                return;
            }

            if (def is PickRow pick)
            {
                Remember(pick.ByName);
                Remember(pick.ById);
                if (value != null) value.text = pick.Display();
                if (button != null)
                {
                    button.onClick.RemoveAllListeners();
                    var val = value;
                    button.onClick.AddListener(() => FlaskPicker.Open(pick.ByName, pick.ById, pick.Title,
                        () => { if (val != null) val.text = pick.Display(); }));
                }
                return;
            }

            var row = (ValueRow)def;
            if (row.Remember != null) row.Remember();
            if (value != null) value.text = row.Get();

            if (background == null || value == null) return;
            if (button != null) UnityEngine.Object.DestroyImmediate(button);

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = value;
            input.lineType = InputField.LineType.SingleLine;
            value.text = row.Get();
            input.SetTextWithoutNotify(row.Get());
            input.onEndEdit.AddListener(v =>
            {
                row.Set(v);
                string now = row.Get();
                input.SetTextWithoutNotify(now);
                value.text = now;
            });
        }

        private static Slider MakeSlider(Image background, float min, float max, float current)
        {
            Slider slider = null;
            try
            {
                Slider proto = null;
                foreach (var one in Resources.FindObjectsOfTypeAll<Slider>())
                {
                    if (one == null || !one.gameObject.scene.IsValid()) continue;
                    if (_win != null && one.transform.IsChildOf(_win.transform)) continue;
                    if (one.direction != Slider.Direction.LeftToRight) continue;
                    proto = one;
                    break;
                }
                if (proto != null)
                {
                    var go = UnityEngine.Object.Instantiate(proto.gameObject, background.transform, false);
                    Clones.StripHotkeys(go, proto.gameObject);
                    go.name = "MvlSlider";
                    foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (c == null || c is Slider) continue;
                        string ns = c.GetType().Namespace ?? "";
                        if (!ns.StartsWith("UnityEngine")) UnityEngine.Object.Destroy(c);
                    }
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.offsetMin = new Vector2(10f, 6f); rt.offsetMax = new Vector2(-10f, -6f);
                    rt.localScale = Vector3.one;
                    slider = go.GetComponent<Slider>();
                    bool hit = false;
                    foreach (var g in go.GetComponentsInChildren<Graphic>(true)) hit |= g.raycastTarget;
                    if (!hit) foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = true;
                    slider.onValueChanged = new Slider.SliderEvent();
                    slider.wholeNumbers = false;
                    slider.minValue = min; slider.maxValue = max;
                    slider.SetValueWithoutNotify(current);
                    slider.interactable = true;
                    go.SetActive(true);
                    return slider;
                }
            }
            catch (Exception e) { Plugin.Caught("[settings] game slider failed: " + e.Message); }

            var sgo = new GameObject("MvlSlider", typeof(RectTransform), typeof(Slider));
            sgo.transform.SetParent(background.transform, false);
            var srt = (RectTransform)sgo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(10f, 8f); srt.offsetMax = new Vector2(-10f, -8f);
            var track = new GameObject("track", typeof(RectTransform), typeof(Image));
            track.transform.SetParent(sgo.transform, false);
            var trt = (RectTransform)track.transform;
            trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(0.5f, 0.5f);
            trt.offsetMin = new Vector2(0f, -3f); trt.offsetMax = new Vector2(0f, 3f);
            track.GetComponent<Image>().color = new Color(0.25f, 0.17f, 0.08f, 0.9f);
            var fillArea = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fillArea.transform.SetParent(track.transform, false);
            var frt = (RectTransform)fillArea.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
            fillArea.GetComponent<Image>().color = new Color(0.85f, 0.62f, 0.2f, 1f);
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sgo.transform, false);
            var hrt = (RectTransform)handle.transform;
            hrt.sizeDelta = new Vector2(14f, 0f);
            hrt.anchorMin = new Vector2(0f, 0f); hrt.anchorMax = new Vector2(0f, 1f);
            handle.GetComponent<Image>().color = new Color(0.98f, 0.9f, 0.7f, 1f);
            slider = sgo.GetComponent<Slider>();
            slider.fillRect = frt;
            slider.handleRect = hrt;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min; slider.maxValue = max;
            slider.SetValueWithoutNotify(current);
            return slider;
        }

        private static ConfigEntry<string> _capCfg;
        private static Text _capText;
        private static Image _capBg;
        private static Button _capBtn;
        private static Color _capTextColor;
        private static Color _capBgColor;
        private static float _capMsgUntil;

        internal static bool Capturing => _capCfg != null;

        private static string KeyName(string spec)
        {
            spec = (spec ?? "").Trim();
            return spec.Length > 0 ? spec : Lang.T("нет");
        }

        private static void StartCapture(ConfigEntry<string> cfg, Image bg, Text text, Button btn)
        {
            if (Capturing) return;
            _capCfg = cfg; _capBg = bg; _capText = text; _capBtn = btn;
            _capTextColor = text.color; _capBgColor = bg.color;
            _capMsgUntil = 0f;
            bg.color = Color.black;
            text.color = new Color32(255, 216, 134, 255);
            text.text = Lang.T("нажмите клавишу");
            if (btn != null) btn.interactable = false;
        }

        private static void StopCapture()
        {
            try
            {
                if (_capBg != null) _capBg.color = _capBgColor;
                if (_capText != null)
                {
                    _capText.color = _capTextColor;
                    _capText.text = KeyName(_capCfg != null ? _capCfg.Value : "");
                }
                if (_capBtn != null) _capBtn.interactable = true;
            }
            catch { }
            _capCfg = null; _capBg = null; _capText = null; _capBtn = null; _capMsgUntil = 0f;
        }

        private static string TakenBy(KeyCode key)
        {
            return Hotkeys.Taken(key.ToString(), null);
        }

        private static void CaptureTick()
        {
            if (!Capturing) return;
            if (_capMsgUntil > 0f)
            {
                if (Time.unscaledTime < _capMsgUntil) return;
                StopCapture();
                return;
            }
            if (!Input.anyKeyDown) return;
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
                if (!Input.GetKeyDown(key)) continue;
                if (key == KeyCode.Escape) { StopCapture(); return; }
                if (key == KeyCode.Delete || key == KeyCode.Backspace)
                {
                    _capCfg.Value = "";
                    StopCapture();
                    return;
                }
                string busy = TakenBy(key);
                if (busy != null)
                {
                    if (_capText != null)
                    {
                        _capText.color = new Color32(255, 120, 100, 255);
                        _capText.text = Lang.F("занято: {0}", busy);
                    }
                    _capMsgUntil = Time.unscaledTime + 2f;
                    return;
                }
                _capCfg.Value = key.ToString();
                StopCapture();
                return;
            }
        }

        internal static void Tick()
        {
            CaptureTick();
            if (_win == null) return;
            Fit();
            Relive();
        }

        private static void SetSwitch(Text value, bool on)
        {
            if (value == null) return;
            value.text = on ? Lang.T("ВКЛ") : Lang.T("ВЫКЛ");
            value.color = on ? WardrobeLook.Good : WardrobeLook.Bad;
        }
    }

    [HarmonyPatch(typeof(HotkeyDispatcher), "Update")]
    public static class SettingsKeyCapturePatch
    {
        private static bool Prefix()
        {
            if (Settings.Capturing) return false;
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    if (ChatColors.EscapeClose()) return false;
                    if (CraftCalc.EscapeClose() || KuCalc.EscapeClose() || Wardrobe.EscapeClose()) return false;
                    if (Smiles.EscapeClose()) return false;
                    if (FlaskPicker.EscapeClose()) return false;
                    if (TravelEdit.EscapeClose()) return false;
                    if (SkillList.EscapeClose()) return false;
                    if (ManikinPicker.EscapeClose()) return false;
                    if (Manikin.EscapeClose()) return false;
                    if (Changelog.EscapeClose()) return false;
                    if (Settings.EscapeClose()) return false;
                    if (Chaotic.EscapeClose()) return false;
                    if (Mail.EscapeClose()) return false;
                    if (Report.EscapeClose()) return false;
                    int modals = ModalDialogList.GetCount();
                    if (modals <= QuestWindow.Under && QuestWindow.EscapeClose()) return false;
                    if (modals <= OnlineWindow.Under && OnlineWindow.EscapeClose()) return false;
                    if (modals <= EffectsWindow.Under && EffectsWindow.EscapeClose()) return false;
                    if (modals == 0 && WalkKeys.EscapeClose()) return false;
                }
            }
            catch (Exception e) { Plugin.Caught("[settings] escape: " + e.Message); }
            return true;
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "Start")]
    public static class ModSettingsButtonPatch
    {
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                if (!SideButtons.InWorld()) return;
                var reset = AccessTools.Field(typeof(SetupDialog), "ResetChatButton")?.GetValue(__instance) as Button;
                if (reset == null) { Plugin.Warn("[settings] 'Reset chat position' button not found."); return; }

                var src = (RectTransform)reset.transform;
                var go = UnityEngine.Object.Instantiate(reset.gameObject, src.parent);
                Clones.StripHotkeys(go, reset.gameObject);
                go.name = "MvlSettingsButton";
                var rt = (RectTransform)go.transform;
                rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
                rt.sizeDelta = src.sizeDelta;
                rt.localScale = src.localScale;
                rt.anchoredPosition = src.anchoredPosition;

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = Lang.T("Настройки мода");

                var btn = go.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Settings.Toggle());

                Bug(__instance, btn);
            }
            catch (Exception e) { Plugin.Fault("[settings] button not added: " + e.Message); }
        }

        private static void Bug(SetupDialog dialog, Button sample)
        {
            try
            {
                var caption = AccessTools.Field(typeof(SetupDialog), "LanguageCaptionText")?.GetValue(dialog) as Text;
                if (caption == null) { Plugin.Warn("[settings] 'Language' caption not found, not placing the report button."); return; }
                var slot = caption.rectTransform;
                if (Unity3DHelper.FindInChild(slot.gameObject, "MvlReportButton") != null) return;

                dialog.StartCoroutine(Fit(dialog, sample, slot));

                var go = UnityEngine.Object.Instantiate(sample.gameObject, slot);
                Clones.StripHotkeys(go, sample.gameObject);
                go.name = "MvlReportButton";
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = Lang.T("Сообщить об ошибке мода");
                caption.enabled = false;

                var press = go.GetComponent<Button>();
                press.onClick.RemoveAllListeners();
                press.onClick.AddListener(Report.Open);
                Plugin.Trace("[settings] report button placed where the 'Language' caption was");
            }
            catch (Exception e) { Plugin.Warn("[settings] report button: " + e.Message); }
        }

        private static IEnumerator Fit(SetupDialog dialog, Button sample, RectTransform slot)
        {
            for (int step = 0; step < 30; step++)
            {
                yield return null;
                if (Stretch(dialog, sample, slot)) yield break;
            }
            float want = sample != null ? ((RectTransform)sample.transform).rect.height : -1f;
            float was = slot != null ? slot.rect.height : -1f;
            Plugin.Warn("[settings] could not measure the report button height (sample " + want.ToString("0.#") + ", slot " + was.ToString("0.#") + "), the button stayed short.");
        }

        private static bool Stretch(SetupDialog dialog, Button sample, RectTransform slot)
        {
            try
            {
                if (dialog == null || sample == null || slot == null) return true;
                float want = ((RectTransform)sample.transform).rect.height;
                float was = slot.rect.height;
                if (want < 10f) return false;

                var home = slot.parent as RectTransform;
                var stack = home != null ? home.GetComponent<LayoutGroup>() : null;
                Plugin.Trace("[settings] 'Language' slot: parent " + (home != null ? home.name : "none")
                    + ", layout " + (stack != null ? stack.GetType().Name : "none")
                    + ", slot height " + was.ToString("0.#") + ", button " + want.ToString("0.#"));
                if (want <= was + 1f) return true;

                var fit = slot.GetComponent<LayoutElement>();
                if (fit == null) fit = slot.gameObject.AddComponent<LayoutElement>();
                fit.minHeight = want;
                fit.preferredHeight = want;
                fit.flexibleHeight = 0f;
                slot.sizeDelta = new Vector2(slot.sizeDelta.x, want);
                if (home != null) LayoutRebuilder.MarkLayoutForRebuild(home);
                Grow(dialog, want - Mathf.Max(was, 0f));
                return true;
            }
            catch (Exception e) { Plugin.Warn("[settings] report button height: " + e.Message); return true; }
        }

        private static void Grow(SetupDialog dialog, float by)
        {
            try
            {
                if (by <= 1f) return;
                var root = dialog.transform as RectTransform;
                if (root == null) return;
                var home = root.parent as RectTransform;
                if (home != null && home.GetComponent<LayoutGroup>() != null)
                {
                    Plugin.Trace("[settings] settings window is inside layout " + home.name + ", leaving height alone");
                    return;
                }
                root.sizeDelta = new Vector2(root.sizeDelta.x, root.sizeDelta.y + Mathf.Min(by, 60f));
                Plugin.Trace("[settings] game settings window grew by " + by + ", now " + root.sizeDelta.y);
            }
            catch (Exception e) { Plugin.Caught("[settings] settings window did not stretch: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "Start")]
    public static class ChatResetHidePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                if (!SideButtons.InWorld()) return;
                var reset = AccessTools.Field(typeof(SetupDialog), "ResetChatButton")?.GetValue(__instance) as Button;
                if (reset == null) return;
                reset.gameObject.SetActive(false);
                Plugin.Trace("[settings] 'Reset chat position' button hidden: the mod chat is in its own place");
            }
            catch (Exception e) { Plugin.Caught("[settings] chat reset button: " + e.Message); }
        }
    }
}
