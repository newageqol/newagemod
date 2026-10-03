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

        private static readonly List<(Text label, Text value, ActionRow row)> Lives = new List<(Text, Text, ActionRow)>();

        private sealed class PickRow : RowDef { internal ConfigEntry<string> ByName; internal ConfigEntry<int> ById; internal System.Func<string> Display; }

        private sealed class ColorRow : RowDef { internal ChatColors.Kind Kind; }

        private sealed class ValueRow : RowDef
        {
            internal Func<string> Get;
            internal Action<string> Set;
            internal Action Reset;
            internal Action Remember;
            internal bool Secret;
        }

        private static List<RowDef> Rows()
        {
            if (_keysMode) return KeyRows();

            var rows = new List<RowDef>
            {
                new Header { Title = "Мод" },
                new ActionRow { Title = UpdateTitle(), ButtonText = UpdateButton(), Do = Updater.Press, TitleNow = UpdateTitle, ButtonNow = UpdateButton },
                A("Выключить мод и вернуть обычный клиент (после перезапуска игры)", "Выключить", ModSwitch.AskOff),
                AssetSync.Pending
                    ? A("Новые версии старых локаций: " + AssetSync.PendingSize + " МБ", "Скачать", AssetSync.Retry)
                    : null,
                new Header { Title = "Кнопки" },
                B("Возврат в Иллениум ведёт на арену, к турнирам", Plugin.CfgTownTournament),
                B("Кнопка запасного набора", Plugin.CfgArtifactButtons),

                new Header { Title = "Переодевание" },
                A("Запасной набор", "Открыть", Manikin.Toggle),
                Artifacts.Pending > 0
                    ? A("Ждут возврата: " + Artifacts.Pending, "Забыть", Artifacts.ForgetStash)
                    : null,

                new Header { Title = "Банки" },
                B("Пить до полного", Plugin.CfgFlaskFillToMax),
                Pick("Жизнь", Plugin.CfgFlaskHpName, Plugin.CfgFlaskHpId),
                Pick("Мана", Plugin.CfgFlaskManaName, Plugin.CfgFlaskManaId),
                Pick("Энергия", Plugin.CfgFlaskEnergyName, Plugin.CfgFlaskEnergyId),
                Pick("Грибы", Plugin.CfgFlaskMushroomName, Plugin.CfgFlaskMushroomId),

                new Header { Title = "Бой" },
                B("Контрприём на себя в первой фазе", Plugin.CfgCounterAuto),

                new Header { Title = "Кто в игре" },
                S("Логин запасного аккаунта", Plugin.CfgOnlineLogin),
                S("Пароль запасного аккаунта", Plugin.CfgOnlinePassword, secret: true),


                new Header { Title = "Звуки" },
                Sl("Громкость звуков мода", Plugin.CfgSoundVolume, 0f, 1f, v => Sounds.Sample()),

                new Header { Title = "" },
            };
            var flash = FlashLook.Entry("General", "Enabled");
            if (flash != null)
            {
                rows.InsertRange(rows.Count - 1, new List<RowDef>
                {
                    new Header { Title = "Внешний вид" },
                    A(flash.Value ? "Бой и персонажи как во Flash" : "Бой и персонажи в 3D",
                        flash.Value ? "Вернуть 3D" : "Как во Flash", FlashLook.AskSwitch),
                    A("Кэш вещей Flash, как у нового игрока", "Сбросить", () => Notice.Show(FlashLook.ResetCache() ?? "Кэш вещей Flash сбросить не удалось", 6f)),
                });
            }
            int sounds = rows.FindIndex(r => r is Header && r.Title == "Звуки");
            rows.InsertRange(sounds >= 0 ? sounds : rows.Count - 1, Tints());
            rows.RemoveAll(r => r == null);
            return rows;
        }

        private static List<RowDef> Tints()
        {
            var rows = new List<RowDef> { new Header { Title = "Цвета чата" } };
            foreach (var kind in ChatColors.All)
                if (kind.Cfg != null) rows.Add(new ColorRow { Title = kind.Title, Kind = kind });
            return rows;
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
                    rows.Add(new Header { Title = group });
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
                    return "— выбрать —";
                },
            };
        }

        private static RowDef B(string title, ConfigEntry<bool> cfg) =>
            cfg == null ? null : new BoolRow { Title = title, Cfg = cfg };

        private static RowDef Sl(string title, ConfigEntry<float> cfg, float min, float max, Action<float> onChange = null) =>
            cfg == null ? null : new SliderRow { Title = title, Cfg = cfg, Min = min, Max = max, OnChange = onChange };

        private static RowDef S(string title, ConfigEntry<string> cfg, bool secret = false) =>
            cfg == null ? null : new ValueRow
            {
                Title = title,
                Get = () => cfg.Value ?? "",
                Set = v => cfg.Value = (v ?? "").Trim(),
                Remember = () => Remember(cfg),
                Reset = () => cfg.Value = (string)cfg.DefaultValue,
                Secret = secret,
            };

        private static string UpdateTitle()
        {
            if (Updater.State == Updater.Stage.Done) return Updater.Message;
            string head = "Версия мода " + Plugin.Version;
            return Updater.State == Updater.Stage.Idle ? head : head + " · " + Updater.Message;
        }

        private static string UpdateButton()
        {
            switch (Updater.State)
            {
                case Updater.Stage.Checking: return "Проверяю…";
                case Updater.Stage.Downloading: return "Качаю…";
                case Updater.Stage.Done: return "Готово";
                default: return "Обновить";
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
            if (revert) foreach (var u in _undo.Values) { try { u(); } catch (Exception e) { Plugin.Trace("[settings] revert: " + e.Message); } }
            if (_win != null) UnityEngine.Object.Destroy(_win);
            _win = null;
            _frame = null;
            _canvas = null;
            _placed = false;
            _undo.Clear();
            Lives.Clear();
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
            var prefab = VisualPrefabsHolder.Instance != null ? VisualPrefabsHolder.Instance.HotkeytsDialog : null;
            if (prefab == null) { Plugin.Warn("[settings] hotkeys window not found."); return; }

            _win = UnityEngine.Object.Instantiate(prefab);
            Clones.StripHotkeys(_win, prefab);
            var dlg = _win.GetComponent<HotkeysDialog>();
            if (dlg == null) { Plugin.Warn("[settings] no HotkeysDialog in the window."); Close(); return; }

            dlg.ShowDialog(null, ECanvasType.ModalWindow);

            var caption = Field<Text>(dlg, "CaptionText");
            var okButton = Field<Button>(dlg, "OkButton");
            var resetButton = Field<Button>(dlg, "ResetToDefaultButton");
            var resetText = Field<Text>(dlg, "ResetToDefaultButtonText");
            var manager = Field<MonoBehaviour>(dlg, "widgetManager");
            if (manager == null) { Plugin.Warn("[settings] window row list not found."); Close(); return; }

            var rowPrefab = Field<GameObject>(manager, "WidgetPrefab");
            var container = manager.transform;
            if (rowPrefab == null) { Plugin.Warn("[settings] row template not found."); Close(); return; }

            manager.enabled = false;
            for (int i = container.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);

            if (caption != null) caption.text = _keysMode ? "Горячие клавиши" : "Настройки мода";

            Widen(dlg, 380f);
            Sketch();
            Indent(container as RectTransform, 20f);

            if (okButton != null)
            {
                okButton.onClick.RemoveAllListeners();
                okButton.onClick.AddListener(() => Close(revert: false));
            }
            if (resetButton != null) resetButton.gameObject.SetActive(false);
            if (dlg.CloseButton != null)
            {
                dlg.CloseButton.onClick.RemoveAllListeners();
                dlg.CloseButton.onClick.AddListener(() => Close(revert: true));
            }

            _undo.Clear();
            Slots.Clear();
            Lives.Clear();
            foreach (var row in Rows())
            {
                try { AddRow(rowPrefab, container, row); }
                catch (Exception e) { Plugin.Fault("[settings] row '" + (row.Title ?? "?") + "': " + e); }
            }

            var scroll = Field<ScrollRect>(manager, "ParentScrollRect");
            ScrollTop(scroll);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(ScrollTopNextFrame(scroll));
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(EvenButtons());
        }

        private static readonly List<(RectTransform row, RectTransform slot, Text label)> Slots = new List<(RectTransform, RectTransform, Text)>();

        private static IEnumerator EvenButtons()
        {
            yield return null;
            yield return null;
            try
            {
                Canvas.ForceUpdateCanvases();
                float widest = 0f;
                foreach (var (row, slot, _) in Slots)
                    if (row != null && slot != null && slot.gameObject.activeInHierarchy) widest = Mathf.Max(widest, slot.rect.width);
                if (widest < 10f) yield break;
                int fixedCount = 0;
                foreach (var (row, slot, label) in Slots)
                {
                    if (row == null || slot == null || !slot.gameObject.activeInHierarchy) continue;
                    float add = widest - slot.rect.width;
                    if (add < 1f) continue;
                    var group = row.GetComponent<HorizontalLayoutGroup>();
                    if (group != null)
                    {
                        var le = slot.GetComponent<LayoutElement>() ?? slot.gameObject.AddComponent<LayoutElement>();
                        le.minWidth = widest;
                        le.preferredWidth = widest;
                        le.flexibleWidth = 0f;
                        if (label != null && label.transform.parent == row)
                        {
                            var ll = label.GetComponent<LayoutElement>() ?? label.gameObject.AddComponent<LayoutElement>();
                            ll.preferredWidth = 0f;
                            ll.minWidth = 0f;
                            ll.flexibleWidth = 1f;
                        }
                        LayoutRebuilder.ForceRebuildLayoutImmediate(row);
                    }
                    else if (Mathf.Abs(slot.anchorMax.x - slot.anchorMin.x) > 0.01f)
                    {
                        slot.offsetMin = new Vector2(slot.offsetMin.x - add, slot.offsetMin.y);
                        Squeeze(label, add);
                    }
                    else
                    {
                        slot.sizeDelta = new Vector2(slot.sizeDelta.x + add, slot.sizeDelta.y);
                        slot.anchoredPosition = new Vector2(slot.anchoredPosition.x - add * (1f - slot.pivot.x), slot.anchoredPosition.y);
                        Squeeze(label, add);
                    }
                    fixedCount++;
                }
                Plugin.Trace("[settings] buttons widened to " + widest.ToString("0") + ": " + fixedCount);
            }
            catch (Exception e) { Plugin.Fault("[settings] button width: " + e.Message); }
        }

        private static void Squeeze(Text label, float by)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            if (Mathf.Abs(rt.anchorMax.x - rt.anchorMin.x) > 0.01f)
                rt.offsetMax = new Vector2(rt.offsetMax.x - by, rt.offsetMax.y);
            else
            {
                rt.sizeDelta = new Vector2(Mathf.Max(20f, rt.sizeDelta.x - by), rt.sizeDelta.y);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x - by * rt.pivot.x, rt.anchoredPosition.y);
            }
        }

        private static RectTransform SlotOf(Transform row, Component part)
        {
            if (row == null || part == null) return null;
            var t = part.transform;
            while (t != null && t.parent != row) t = t.parent;
            return t as RectTransform;
        }

        private static void Indent(RectTransform list, float pad)
        {
            try
            {
                if (list == null) return;
                var layout = list.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.padding.left += Mathf.RoundToInt(pad);
                    Plugin.Trace("[settings] list left padding: " + layout.padding.left);
                    return;
                }
                list.offsetMin = new Vector2(list.offsetMin.x + pad, list.offsetMin.y);
                Plugin.Trace("[settings] list padding by offset: " + list.offsetMin.x);
            }
            catch (Exception e) { Plugin.Fault("[settings] list padding: " + e.Message); }
        }

        private static RectTransform _frame;
        private static float _extra, _wantWidth;
        private static Canvas _canvas;
        private static bool _placed;
        private static float _placedFrameW, _placedAreaW, _placedX;

        private static void Widen(HotkeysDialog dlg, float extra)
        {
            _frame = dlg.transform as RectTransform;
            _canvas = null;
            _placed = false;
            _extra = extra;
            _wantWidth = 0f;
            Stretch();
        }

        private static void Stretch()
        {
            try
            {
                if (_frame == null) return;
                float now = _frame.rect.width;
                if (now < 50f) return;
                if (_wantWidth <= 0f) _wantWidth = now + _extra;

                float need = _wantWidth - now;
                if (Mathf.Abs(need) < 1f) return;

                var fitter = _frame.GetComponent<ContentSizeFitter>();
                if (fitter != null) fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                var element = _frame.GetComponent<LayoutElement>();
                if (element != null) { element.preferredWidth = _wantWidth; element.minWidth = _wantWidth; }

                Grow(_frame, need, self: true);
                foreach (RectTransform child in _frame) Grow(child, need, self: false);
                Center();
                Plugin.Trace("[settings] window width " + now + " → " + _frame.rect.width
                             + " (want " + _wantWidth + ")");
            }
            catch (Exception e) { Plugin.Fault("[settings] window width: " + e.Message); }
        }

        private static void Center()
        {
            if (_frame == null) return;
            if (_canvas == null) _canvas = _frame.GetComponentInParent<Canvas>();
            var area = _canvas != null ? _canvas.transform as RectTransform : _frame.parent as RectTransform;
            if (area == null || area.rect.width < 50f) return;

            float frameW = _frame.rect.width;
            float areaW = area.rect.width;
            float atX = _frame.position.x;
            if (_placed && frameW == _placedFrameW && areaW == _placedAreaW && atX == _placedX) return;

            float left, right;
            if (!Span(area, out left, out right)) return;

            float shift = area.rect.center.x - (left + right) * 0.5f;
            if (Mathf.Abs(shift) < 0.5f) { Settled(frameW, areaW, atX); return; }
            _frame.position += area.TransformVector(new Vector3(shift, 0f, 0f));
            Settled(frameW, areaW, _frame.position.x);
            Plugin.Trace("[settings] shifting window horizontally by " + shift.ToString("0")
                                + " (window " + left.ToString("0") + ".." + right.ToString("0")
                                + ", canvas " + area.rect.xMin.ToString("0") + ".." + area.rect.xMax.ToString("0") + ")");
        }

        private static void Settled(float frameW, float areaW, float atX)
        {
            _placed = true;
            _placedFrameW = frameW;
            _placedAreaW = areaW;
            _placedX = atX;
        }

        private static readonly Vector3[] Corners = new Vector3[4];
        private static readonly List<Graphic> Arts = new List<Graphic>();

        private static bool Span(RectTransform area, out float left, out float right)
        {
            left = float.MaxValue;
            right = float.MinValue;
            var corners = Corners;
            Arts.Clear();
            _frame.GetComponentsInChildren(false, Arts);
            foreach (var art in Arts)
            {
                if (art == null || !art.isActiveAndEnabled) continue;
                var rt = art.rectTransform;
                if (rt.rect.width < 4f || rt.rect.height < 4f) continue;
                if (rt.rect.width >= area.rect.width - 1f) continue;
                rt.GetWorldCorners(corners);
                for (int i = 0; i < 4; i++)
                {
                    float x = area.InverseTransformPoint(corners[i]).x;
                    if (x < left) left = x;
                    if (x > right) right = x;
                }
            }
            return right > left;
        }

        private static void Sketch()
        {
            if (_frame == null || Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            try
            {
                var canvas = _frame.GetComponentInParent<Canvas>();
                var area = canvas != null ? canvas.transform as RectTransform : null;
                Plugin.Trace("[settings] canvas " + (area != null ? area.name + " " + area.rect.width.ToString("0") + "x" + area.rect.height.ToString("0") : "none")
                    + ", window " + _frame.name + " " + _frame.rect.width.ToString("0") + "x" + _frame.rect.height.ToString("0")
                    + " pivot " + _frame.pivot.x.ToString("0.00") + ", anchors " + _frame.anchorMin.x.ToString("0.00") + ".." + _frame.anchorMax.x.ToString("0.00"));
                foreach (RectTransform child in _frame)
                {
                    if (child == null) continue;
                    Plugin.Trace("[settings]   " + child.name + " " + child.rect.width.ToString("0") + "x" + child.rect.height.ToString("0")
                        + (child.gameObject.activeSelf ? "" : " (inactive)"));
                }
            }
            catch (Exception e) { Plugin.Trace("[settings] window dump: " + e.Message); }
        }

        private static void Grow(RectTransform rt, float extra, bool self)
        {
            bool stretched = Mathf.Abs(rt.anchorMax.x - rt.anchorMin.x) > 0.01f;
            if (stretched)
            {
                if (!self) return;
                rt.offsetMin = new Vector2(rt.offsetMin.x - extra * 0.5f, rt.offsetMin.y);
                rt.offsetMax = new Vector2(rt.offsetMax.x + extra * 0.5f, rt.offsetMax.y);
                return;
            }
            rt.sizeDelta = new Vector2(rt.sizeDelta.x + extra, rt.sizeDelta.y);
            if (!self) return;
            float slide = extra * (0.5f - rt.pivot.x);
            if (Mathf.Abs(slide) > 0.01f)
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x - slide, rt.anchoredPosition.y);
        }

        private static void AddRow(GameObject rowPrefab, Transform container, RowDef def)
        {
            var go = UnityEngine.Object.Instantiate(rowPrefab, container, worldPositionStays: false);
            go.name = "MvlRow";
            var widget = go.GetComponent<HotkeyWidget>();

            Text label = null, value = null;
            Image background = null;
            Button button = null;
            if (widget != null)
            {
                label = Field<Text>(widget, "LabelText");
                value = Field<Text>(widget, "KeyText");
                background = Field<Image>(widget, "KeyBackground");
                button = Field<Button>(widget, "Button");
                UnityEngine.Object.Destroy(widget);
            }
            if (label == null) label = go.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = def.Title;
                if (!(def is Header))
                {
                    label.resizeTextForBestFit = true;
                    label.resizeTextMaxSize = label.fontSize;
                    label.resizeTextMinSize = 8;

                    label.alignment = TextAnchor.MiddleLeft;
                    var lrt = label.rectTransform;
                    const float pad = 34f;
                    if (Mathf.Abs(lrt.anchorMax.x - lrt.anchorMin.x) > 0.01f)
                    {
                        lrt.offsetMin = new Vector2(lrt.offsetMin.x + pad, lrt.offsetMin.y);
                        lrt.offsetMax = new Vector2(lrt.offsetMax.x + pad * 0.25f, lrt.offsetMax.y);
                    }
                    else
                    {
                        lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x + pad, lrt.anchoredPosition.y);
                    }
                    if (Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                        Plugin.Trace("[settings] caption '" + def.Title + "' anchors "
                                     + lrt.anchorMin.x + ".." + lrt.anchorMax.x
                                     + " offsets " + lrt.offsetMin.x + ".." + lrt.offsetMax.x
                                     + " position " + lrt.anchoredPosition.x);
                }
            }

            if (!(def is Header))
            {
                var slot = SlotOf(go.transform, (Component)background ?? (Component)button ?? value);
                if (slot != null && (label == null || !label.transform.IsChildOf(slot)))
                {
                    Slots.Add((go.transform as RectTransform, slot, label));
                    if (Slots.Count == 1 && Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                        Plugin.Trace("[settings] row layout: group " + (go.GetComponent<HorizontalLayoutGroup>() != null) + ", slot " + slot.name
                                     + " anchors " + slot.anchorMin.x + ".." + slot.anchorMax.x + " pivot " + slot.pivot.x
                                     + ", label parent " + (label != null ? label.transform.parent.name : "-"));
                }
            }

            if (def is Header)
            {
                if (background != null) background.gameObject.SetActive(false);
                else if (value != null) value.gameObject.SetActive(false);
                if (label != null)
                {
                    label.text = def.Title.ToUpperInvariant();
                    label.fontStyle = FontStyle.Bold;
                    label.fontSize = Mathf.RoundToInt(label.fontSize * 1.25f);
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = new Color(0.45f, 0.12f, 0.06f);
                    var rt = label.rectTransform;
                    rt.anchorMin = new Vector2(0f, rt.anchorMin.y);
                    rt.anchorMax = new Vector2(1f, rt.anchorMax.y);
                    rt.offsetMin = new Vector2(6f, rt.offsetMin.y);
                    rt.offsetMax = new Vector2(-6f, rt.offsetMax.y);
                }
                return;
            }

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
            Func<string> shown = () => row.Secret ? new string('•', row.Get().Length) : row.Get();
            if (value != null) value.text = shown();

            if (background == null || value == null) return;
            if (button != null) UnityEngine.Object.DestroyImmediate(button);

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = value;
            input.lineType = InputField.LineType.SingleLine;
            if (row.Secret) input.contentType = InputField.ContentType.Password;
            value.text = shown();
            input.SetTextWithoutNotify(row.Get());
            input.onEndEdit.AddListener(v =>
            {
                row.Set(v);
                string now = row.Get();
                input.SetTextWithoutNotify(now);
                value.text = shown();
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
            catch (Exception e) { Plugin.Trace("[settings] game slider failed: " + e.Message); }

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
            return spec.Length > 0 ? spec : "нет";
        }

        private static void StartCapture(ConfigEntry<string> cfg, Image bg, Text text, Button btn)
        {
            if (Capturing) return;
            _capCfg = cfg; _capBg = bg; _capText = text; _capBtn = btn;
            _capTextColor = text.color; _capBgColor = bg.color;
            _capMsgUntil = 0f;
            bg.color = Color.black;
            text.color = new Color32(255, 216, 134, 255);
            text.text = "нажмите клавишу";
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
                        _capText.text = "занято: " + busy;
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
            Stretch();
            Center();
            Relive();
        }

        private static void ScrollTop(ScrollRect scroll)
        {
            if (scroll == null) return;
            try
            {
                Canvas.ForceUpdateCanvases();
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1f;
                Canvas.ForceUpdateCanvases();
            }
            catch { }
        }

        private static System.Collections.IEnumerator ScrollTopNextFrame(ScrollRect scroll)
        {
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                if (scroll == null) yield break;
                var content = scroll.content;
                if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                ScrollTop(scroll);
            }
        }

        private static void SetSwitch(Text value, bool on)
        {
            if (value == null) return;
            value.text = on ? "ВКЛ" : "ВЫКЛ";
            value.color = on ? new Color(0.05f, 0.42f, 0.08f) : new Color(0.62f, 0.08f, 0.06f);
        }

        private static T Field<T>(object obj, string name) where T : class
        {
            try { return AccessTools.Field(obj.GetType(), name)?.GetValue(obj) as T; }
            catch { return null; }
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
                    if (Wardrobe.EscapeClose() || KuCalc.EscapeClose() || CraftCalc.EscapeClose()) return false;
                    if (Smiles.EscapeClose()) return false;
                    if (FlaskPicker.EscapeClose()) return false;
                    if (TravelEdit.EscapeClose()) return false;
                    if (SkillList.EscapeClose()) return false;
                    if (ManikinPicker.EscapeClose()) return false;
                    if (Manikin.EscapeClose()) return false;
                    if (Changelog.EscapeClose()) return false;
                    if (Chaotic.EscapeClose()) return false;
                    if (Report.EscapeClose()) return false;
                    if (ModalDialogList.IsEmpty())
                    {
                        if (OnlineWindow.EscapeClose()) return false;
                        if (QuestWindow.EscapeClose()) return false;
                        if (EffectsWindow.EscapeClose()) return false;
                        if (WalkKeys.EscapeClose()) return false;
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[settings] escape: " + e.Message); }
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

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = "Настройки мода";

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

                foreach (var t in go.GetComponentsInChildren<Text>(true)) t.text = "Сообщить об ошибке мода";
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
            catch (Exception e) { Plugin.Trace("[settings] settings window did not stretch: " + e.Message); }
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
            catch (Exception e) { Plugin.Trace("[settings] chat reset button: " + e.Message); }
        }
    }
}
