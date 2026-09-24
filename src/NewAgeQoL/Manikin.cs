using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Manikin
    {
        private const float PanelW = 440f;
        private const float PanelH = 600f;
        private const float Cell = 58f;
        private const float Gap = 6f;
        private const int Cols = 6;
        private const int Rows = 7;



        private static readonly int[] LeftOrder = { 1, 3, 6, 25, 26, 7, 8 };
        private static readonly int[] RightOrder = { 2, 5, 4, 13, 14, 9, 10 };
        private static readonly int[] WeaponOrder = { 20, 19, 12, 11 };
        private static readonly int[] RelicOrder = { 15, 16, 17, 18, 21, 22, 23, 24 };
        private static readonly int[][] Groups =
        {
            new[] { 7, 8, 9, 10 },
            new[] { 25, 26 },
            new[] { 15, 16, 17, 18 },
            new[] { 21, 22, 23, 24 }
        };

        private static readonly Dictionary<int, int> Wear = new Dictionary<int, int>();
        private static readonly Dictionary<int, InteractiveIcon> Icons = new Dictionary<int, InteractiveIcon>();

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static GameObject _slotsGo;
        private static Text _status;
        private static GameObject _gridGo;
        private static bool _hooked;
        private static float _repaint;
        private static float _hookAt;
        private static int _shownSeen = -1;
        private static float _nextLook;
        private static int _builtFrame;

        internal static string SlotName(int slot)
        {
            switch (slot)
            {
                case 1: return "шлем";
                case 2: return "амулет";
                case 3: return "доспех";
                case 4: return "перчатки";
                case 5: return "наручи";
                case 6: return "пояс";
                case 7: case 8: case 9: case 10: return "кольцо";
                case 11: return "левая рука";
                case 12: return "правая рука";
                case 13: return "поножи";
                case 14: return "сапоги";
                case 15: case 16: case 17: case 18: return "реликвия";
                case 19: return "запас: левая рука";
                case 20: return "запас: правая рука";
                case 21: case 22: case 23: case 24: return "запас: реликвия";
                case 25: case 26: return "серьга";
                default: return "слот " + slot;
            }
        }

        internal static void Forget()
        {
            Close();
            Wear.Clear();
        }

        internal static Dictionary<int, int> Saved()
        {
            var map = new Dictionary<int, int>();
            string raw = Plugin.CfgManikin != null ? Plugin.CfgManikin.Value ?? "" : "";
            foreach (var part in raw.Split(','))
            {
                var pair = part.Split(':');
                if (pair.Length != 2) continue;
                int slot, thing;
                if (!int.TryParse(pair[0].Trim(), out slot) || !int.TryParse(pair[1].Trim(), out thing)) continue;
                if (slot <= 0 || thing <= 0) continue;
                map[slot] = thing;
            }
            Pack(map);
            return map;
        }

        private static void Pack(Dictionary<int, int> map)
        {
            foreach (var group in Groups)
            {
                var things = new List<int>();
                foreach (int slot in group)
                {
                    int thing;
                    if (map.TryGetValue(slot, out thing) && thing > 0) things.Add(thing);
                    map.Remove(slot);
                }
                for (int i = 0; i < things.Count; i++) map[group[i]] = things[i];
            }
        }

        internal static int Used(int thingId, int exceptSlot)
        {
            if (thingId <= 0) return 0;
            int count = 0;
            foreach (var pair in Wear)
                if (pair.Key != exceptSlot && pair.Value == thingId) count++;
            return count;
        }

        internal static bool Owns(Transform node)
        {
            if (node == null) return false;
            if (_slotsGo != null && node.IsChildOf(_slotsGo.transform)) return true;
            return _gridGo != null && node.IsChildOf(_gridGo.transform);
        }

        private static void Keep()
        {
            if (Plugin.CfgManikin == null) return;
            var text = new System.Text.StringBuilder();
            foreach (var pair in Wear)
            {
                if (pair.Value <= 0) continue;
                if (text.Length > 0) text.Append(',');
                text.Append(pair.Key).Append(':').Append(pair.Value);
            }
            Plugin.CfgManikin.Value = text.ToString();
        }

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Open();
        }

        private static void Open()
        {
            try
            {
                Wear.Clear();
                foreach (var pair in Saved()) Wear[pair.Key] = pair.Value;
                Keep();
                Build();
                Flasks.RequestScan();
                Artifacts.RequestWear();
                Artifacts.RequestStorage();
                Plugin.Trace("[набор] открыт, в хранилище: " + (Artifacts.NearStorage() ? "да" : "нет")
                                    + ", в памяти хранилища вещей " + Storage.Things().Count);
            }
            catch (Exception e) { Plugin.Fault("[манекен] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panelGo = null;
            _slotsGo = null;
            _gridGo = null;
            _status = null;
            _hooked = false;
            _shownSeen = -1;
            _nextLook = 0f;
            _noteStore = -1;
            _noteAt = 0f;
            Icons.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        internal static void Tick()
        {
            ManikinPicker.Tick();
            if (_canvasGo == null) return;
            if (!_hooked) Hook();
            else
            {
                if (_repaint > 0f && Time.unscaledTime > _repaint) { _repaint = 0f; Paint(); }
                if (Time.unscaledTime >= _nextLook)
                {
                    _nextLook = Time.unscaledTime + 0.3f;
                    int shown = Drawable();
                    if (shown != _shownSeen) { _shownSeen = shown; Paint(); }
                }
            }
            if (Time.unscaledTime >= _noteAt)
            {
                _noteAt = Time.unscaledTime + 0.3f;
                int store = Artifacts.NearStorage() ? 1 : 0;
                if (store != _noteStore)
                {
                    _noteStore = store;
                    if (_status != null) _status.text = Note(store == 1);
                }
            }
        }

        private static int _noteStore = -1;
        private static float _noteAt;

        private static string Note() => Note(Artifacts.NearStorage());

        private static string Note(bool near)
        {
            string source = near
                ? "Нажми на слот и выбери вещь: сумка, хранилище, надетое."
                : "Нажми на слот и выбери вещь: сумка и надетое. За вещами из хранилища зайди в него.";
            return source + System.Environment.NewLine + "Наденется само после сдачи вещей, снимется при возврате.";
        }

        private static void Hook()
        {
            if (_slotsGo == null) return;
            if (Time.frameCount < _builtFrame + 3) return;
            if (Time.unscaledTime < _hookAt) return;
            _hookAt = Time.unscaledTime + 0.5f;
            var content = _slotsGo.GetComponent<UserMenuCharacterSlotsPanelContent>();
            if (content == null) content = _slotsGo.GetComponentInChildren<UserMenuCharacterSlotsPanelContent>(true);
            var all = _slotsGo.GetComponentsInChildren<InteractiveIcon>(true);
            Plugin.Trace("[набор] панель: " + (content != null ? content.GetType().Name : "нет компонента")
                         + ", ячеек в префабе " + (all != null ? all.Length : 0));
            if (all == null || all.Length == 0) return;

            Icons.Clear();
            if (content != null)
                try
                {
                    var known = AccessTools.Field(typeof(UserMenuCharacterSlotsPanelContent), "_slotItemRenderers")
                                          ?.GetValue(content) as IDictionary<ESlots.SlotType, InteractiveIcon>;
                    if (known != null)
                        foreach (var pair in known)
                            if (pair.Value != null) Icons[(int)pair.Key] = pair.Value;
                }
                catch (Exception e) { Plugin.Trace("[набор] разметка слотов игры: " + e.Message); }

            if (Icons.Count == 0) ByShape();

            if (Icons.Count == 0) { Plugin.Warn("[набор] ячейки не разложились по слотам"); return; }

            foreach (var pair in Icons)
            {
                int slot = pair.Key;
                var icon = pair.Value;
                if (icon == null) continue;
                icon.gameObject.SetActive(true);
                icon.SetInteractable(true);
                var button = icon.GetButton();
                if (button != null)
                {
                    button.enabled = true;
                    button.interactable = true;
                    button.onClick.AddListener(() => Choose(slot));
                }
                else icon.ButtonClickEvent += ignored => Choose(slot);
            }
            _hooked = true;
            Plugin.Trace("[набор] слотов подключено: " + Icons.Count);
            Arrange(content);
            Paint();
        }

        private static void ByShape()
        {
            var groups = new Dictionary<Transform, List<InteractiveIcon>>();
            foreach (var icon in _slotsGo.GetComponentsInChildren<InteractiveIcon>(true))
            {
                if (icon == null || icon.transform.parent == null) continue;
                List<InteractiveIcon> list;
                if (!groups.TryGetValue(icon.transform.parent, out list))
                {
                    list = new List<InteractiveIcon>();
                    groups[icon.transform.parent] = list;
                }
                list.Add(icon);
            }

            var sevens = new List<KeyValuePair<Transform, List<InteractiveIcon>>>();
            foreach (var pair in groups)
            {
                if (pair.Value.Count == 7) sevens.Add(pair);
                else if (pair.Value.Count == 4) Bind(pair.Value, WeaponOrder);
                else if (pair.Value.Count == 8) Bind(pair.Value, RelicOrder);
            }
            if (sevens.Count == 2)
            {
                sevens.Sort((a, b) => a.Key.position.x.CompareTo(b.Key.position.x));
                Bind(sevens[0].Value, LeftOrder);
                Bind(sevens[1].Value, RightOrder);
            }
            else foreach (var pair in sevens) Bind(pair.Value, LeftOrder);
        }

        private static void Bind(List<InteractiveIcon> icons, int[] order)
        {
            icons.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            for (int i = 0; i < icons.Count && i < order.Length; i++)
                if (icons[i] != null) Icons[order[i]] = icons[i];
        }

        private static int Drawable()
        {
            int count = 0;
            foreach (var pair in Wear)
                if (pair.Value > 0 && !string.IsNullOrEmpty(Picture(pair.Value))) count++;
            return count;
        }

        private static string Picture(int thing)
        {
            string image = Flasks.ImageOf(thing);
            if (!string.IsNullOrEmpty(image)) return image;
            foreach (var pair in Storage.WornSlots())
            {
                var item = pair.Value;
                if (item == null || item.ThingId != thing || string.IsNullOrEmpty(item.Image)) continue;
                Flasks.Note(thing, item.Image, item.Subtype, item.Rarity, item.Level);
                return item.Image;
            }
            return null;
        }

        private static void Paint()
        {
            foreach (var pair in Icons)
            {
                var icon = pair.Value;
                if (icon == null) continue;
                int thing;
                if (!Wear.TryGetValue(pair.Key, out thing) || thing <= 0)
                {
                    icon.Clear();
                    icon.SetIcon(SlotSprite(pair.Key));
                    icon.SetInteractable(true);
                    continue;
                }
                string image = Picture(thing);
                if (string.IsNullOrEmpty(image))
                {
                    Flasks.AskName(thing);
                    icon.Clear();
                    icon.SetIcon(SlotSprite(pair.Key));
                    icon.SetInteractable(true);
                    continue;
                }
                icon.Id = thing;
                icon.DisplayThing(image, (EThingSubType)Flasks.SubTypeOf(thing), (EThingRarity)Flasks.RarityOf(thing), Flasks.LevelOf(thing), 0);
                icon.SetInteractable(true);
            }
        }

        private static void Arrange(UserMenuCharacterSlotsPanelContent content)
        {
            var left = Column(content, "LeftColumnGroup");
            var right = Column(content, "RightColumnGroup");
            var relic = Column(content, "RelicGroup");
            var arms = Column(content, "WeaponGroup");

            int rows = Rows;
            if (left.Count > rows) rows = left.Count;
            if (right.Count > rows) rows = right.Count;

            float wide = Cols * Cell + (Cols - 1) * Gap;
            float high = rows * Cell + (rows - 1) * Gap;

            _gridGo = new GameObject("slots", typeof(RectTransform));
            _gridGo.transform.SetParent(_panelGo.transform, false);
            var grt = (RectTransform)_gridGo.transform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 1f);
            grt.pivot = new Vector2(0.5f, 1f);
            grt.sizeDelta = new Vector2(wide, high);
            grt.anchoredPosition = new Vector2(0f, -58f);

            if (left.Count > 0 || right.Count > 0)
            {
                for (int i = 0; i < left.Count; i++) Put(grt, left[i], 0, i);
                for (int i = 0; i < right.Count; i++) Put(grt, right[i], Cols - 1, i);
                for (int i = 0; i < relic.Count; i++) Put(grt, relic[i], 1 + i % 4, i / 4);
                for (int i = 0; i < arms.Count; i++) Put(grt, arms[i], 2 + i % 2, rows - 2 + i / 2);
            }
            else
            {
                int cell = 0;
                foreach (var pair in Icons)
                {
                    Put(grt, pair.Value, cell % Cols, cell / Cols);
                    cell++;
                }
            }

            if (_slotsGo != null) _slotsGo.SetActive(false);
        }

        private static List<InteractiveIcon> Column(UserMenuCharacterSlotsPanelContent content, string field)
        {
            var list = new List<InteractiveIcon>();
            if (content == null) return list;
            try
            {
                var group = AccessTools.Field(typeof(UserMenuCharacterSlotsPanelContent), field)?.GetValue(content) as GridLayoutGroup;
                if (group == null) return list;
                foreach (Transform child in group.transform)
                {
                    var icon = child.GetComponent<InteractiveIcon>();
                    if (icon == null) icon = child.GetComponentInChildren<InteractiveIcon>(true);
                    if (icon != null) list.Add(icon);
                }
            }
            catch (Exception e) { Plugin.Trace("[набор] группа " + field + ": " + e.Message); }
            return list;
        }

        private static void Put(RectTransform grid, InteractiveIcon icon, int col, int row)
        {
            if (icon == null) return;
            var rt = icon.transform as RectTransform;
            if (rt == null) return;
            float side = rt.rect.width > 1f ? rt.rect.width : Cell;
            rt.SetParent(grid, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one * (Cell / side);
            rt.anchoredPosition = new Vector2(Cell * 0.5f + col * (Cell + Gap),
                                              -Cell * 0.5f - row * (Cell + Gap));
        }

        private static Sprite SlotSprite(int slot)
        {
            try { return AtlasUtils.getSlotIconSprite((ESlots.SlotType)slot); }
            catch { return null; }
        }

        private static void Choose(int slot)
        {
            ManikinPicker.Open(slot, thing =>
            {
                if (thing > 0) Wear[slot] = thing;
                else Wear.Remove(slot);
                Pack(Wear);
                Keep();
                _shownSeen = -1;
                Paint();
                _repaint = Time.unscaledTime + 1.5f;
            });
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLManikin", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 810;
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

            _panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;

            var back = _panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var title = OnlineWindow.Label(_panelGo.transform, "Запасной набор", 20, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(18f, -46f), new Vector2(-70f, -10f));
            title.alignment = TextAnchor.MiddleLeft;

            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var prefab = VisualPrefabsHolder.Instance != null ? VisualPrefabsHolder.Instance.UserMenuCharacterSlotsPanelContentPrefab : null;
            if (prefab == null) throw new Exception("не нашёл окно экипировки игры");
            _slotsGo = UnityEngine.Object.Instantiate(prefab, _panelGo.transform, false);
            _slotsGo.name = "QoLSlots";
            _builtFrame = Time.frameCount;
            var hide = _slotsGo.GetComponent<CanvasGroup>();
            if (hide == null) hide = _slotsGo.AddComponent<CanvasGroup>();
            hide.alpha = 0f;
            var srt = _slotsGo.transform as RectTransform;
            if (srt != null)
            {
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = Vector2.one;
                srt.offsetMin = Vector2.zero;
                srt.offsetMax = Vector2.zero;
                srt.localScale = Vector3.one;
            }

            _status = OnlineWindow.Label(_panelGo.transform, Note(), 11, FontStyle.Normal, WardrobeLook.Label);
            OnlineWindow.Place(_status.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(14f, 8f), new Vector2(-14f, 86f));
            _status.alignment = TextAnchor.UpperLeft;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
