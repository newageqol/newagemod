using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.Things.Thingtabs;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class SlotSwap
    {
        private static readonly ESlots.SlotType[] MainWeapons =
        {
            ESlots.SlotType.RIGHTHANDSLOT,
            ESlots.SlotType.LEFTHANDSLOT,
        };

        private static readonly ESlots.SlotType[] SpareWeapons =
        {
            ESlots.SlotType.ADDITIONALRIGHTHANDSLOT,
            ESlots.SlotType.ADDITIONALLEFTHANDSLOT,
        };

        private static readonly ESlots.SlotType[] MainRelics =
        {
            ESlots.SlotType.RELICSLOT1,
            ESlots.SlotType.RELICSLOT2,
            ESlots.SlotType.RELICSLOT3,
            ESlots.SlotType.RELICSLOT4,
        };

        private static readonly ESlots.SlotType[] SpareRelics =
        {
            ESlots.SlotType.ADDITIONALRELICSLOT1,
            ESlots.SlotType.ADDITIONALRELICSLOT2,
            ESlots.SlotType.ADDITIONALRELICSLOT3,
            ESlots.SlotType.ADDITIONALRELICSLOT4,
        };

        private static readonly Dictionary<ESlots.SlotType, Piece> Held = new Dictionary<ESlots.SlotType, Piece>();
        private static readonly Dictionary<int, int> TabOfKind = new Dictionary<int, int>();

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, Button> WeaponToggle =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, Button>("BtnAdditionalWeaponSlots");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, Button> RelicToggle =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, Button>("BtnAdditionalReliquaieSlots");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent,
                                                     IDictionary<ESlots.SlotType, InteractiveIcon>> SlotIcons =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent,
                                       IDictionary<ESlots.SlotType, InteractiveIcon>>("_slotItemRenderers");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, GridLayoutGroup> LeftColumn =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, GridLayoutGroup>("LeftColumnGroup");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, GridLayoutGroup> RightColumn =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, GridLayoutGroup>("RightColumnGroup");

        private static readonly Dictionary<int, Vector2> Home = new Dictionary<int, Vector2>();
        private const float Gap = 8f;

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, GridLayoutGroup> RelicRow =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, GridLayoutGroup>("RelicGroup");

        private static readonly AccessTools.FieldRef<InteractiveIcon, Image> IconOf =
            AccessTools.FieldRefAccess<InteractiveIcon, Image>("IconImage");

        private static readonly AccessTools.FieldRef<InteractiveIcon, Image> FrameOf =
            AccessTools.FieldRefAccess<InteractiveIcon, Image>("FrameImage");

        private static readonly AccessTools.FieldRef<InteractiveIcon, Text> CounterOf =
            AccessTools.FieldRefAccess<InteractiveIcon, Text>("CounterText");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, GridLayoutGroup> WeaponRow =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, GridLayoutGroup>("WeaponGroup");

        private static UserMenuCharacterSlotsPanelContent _panel;
        private static string _told;
        private static Sprite _cycle;
        private static float _nextArrange;
        private static Button _weapons, _relics;
        private static Text _note;
        private static float _noteUntil;
        private static bool _busy;


        internal static void Sync(UserMenuCharacterSlotsPanelContent panel,
                                  IDictionary<ESlots.SlotType, InventoryWearResponseMessageItem> worn)
        {
            try
            {
                Held.Clear();
                if (worn != null)
                    foreach (var pair in worn)
                    {
                        if (pair.Value == null) continue;
                        Held[pair.Key] = new Piece
                        {
                            Id = pair.Value.InventoryId,
                            ThingId = pair.Value.ThingId,
                            Kind = pair.Value.Subtype,
                        };
                    }

                var face = Slot(panel, ESlots.SlotType.RELICSLOT1);
                if (_weapons == null)
                    _weapons = Build(WeaponToggle(panel), face, "QoLSwapWeapons",
                                     () => Swap("оружие", MainWeapons, SpareWeapons));
                if (_relics == null)
                    _relics = Build(RelicToggle(panel), face, "QoLSwapRelics",
                                    () => Swap("реликвии", MainRelics, SpareRelics));

                _panel = panel;
                Arrange(panel);

                Show(_weapons, Any(MainWeapons) || Any(SpareWeapons));
                Show(_relics, Any(MainRelics) || Any(SpareRelics));
            }
            catch (System.Exception e) { Plugin.Fault("[slots] gear window: " + e.Message); }
        }

        internal static void LearnTabs(ThingTabInventoryResponseMessage content)
        {
            try
            {
                if (content?.Things == null || content.TabNumber <= 0) return;
                if (content.TabNumber == 18) return;
                foreach (var thing in content.Things)
                    if (thing != null) TabOfKind[thing.SubType] = content.TabNumber;
            }
            catch { }
        }

        internal static void Tick()
        {
            if (_panel != null && _panel.gameObject.activeInHierarchy && Time.unscaledTime >= _nextArrange)
            {
                _nextArrange = Time.unscaledTime + 0.25f;
                try { Arrange(_panel); }
                catch (System.Exception e) { Plugin.Fault("[slots] layout: " + e.Message); _panel = null; }
            }
            if (_note == null || !_note.gameObject.activeSelf) return;
            if (Time.unscaledTime > _noteUntil) _note.gameObject.SetActive(false);
        }

        private static bool Any(ESlots.SlotType[] slots)
        {
            foreach (var slot in slots)
                if (Held.TryGetValue(slot, out var piece) && piece != null && piece.Id > 0) return true;
            return false;
        }

        private static void Show(Button button, bool on)
        {
            if (button != null && button.gameObject.activeSelf != on) button.gameObject.SetActive(on);
        }

        private sealed class Piece
        {
            internal int Id;
            internal int ThingId;
            internal int Kind;
        }

        private sealed class Job
        {
            internal string What;
            internal ESlots.SlotType[] Main;
            internal ESlots.SlotType[] Spare;
        }

        private static readonly List<Job> Waiting = new List<Job>();

        private static void Swap(string what, ESlots.SlotType[] main, ESlots.SlotType[] spare)
        {
            if (Plugin.Instance == null) return;
            if (!_busy)
            {
                Plugin.Instance.StartCoroutine(Run(what, main, spare));
                return;
            }
            foreach (var job in Waiting) if (job.What == what) return;
            if (Waiting.Count >= 2) return;
            Waiting.Add(new Job { What = what, Main = main, Spare = spare });
            Note(what + ": в очереди", 6f);
        }

        private static void Next()
        {
            if (Waiting.Count == 0 || Plugin.Instance == null) return;
            var job = Waiting[0];
            Waiting.RemoveAt(0);
            Plugin.Instance.StartCoroutine(Run(job.What, job.Main, job.Spare));
        }

        private static IEnumerator Run(string what, ESlots.SlotType[] main, ESlots.SlotType[] spare)
        {
            _busy = true;
            try
            {
                var dressed = Pieces(main);
                var stash = Pieces(spare);
                if (dressed.Count == 0 && stash.Count == 0)
                {
                    Note("менять нечего", 2f);
                    yield break;
                }

                Note("меняю " + what + "…", 6f);
                Plugin.Trace("[slots] swapping " + what + ": worn " + dressed.Count + ", spare " + stash.Count);
                _failed = null;

                foreach (var piece in dressed) { if (_failed != null) break; yield return Strip(piece); }
                foreach (var piece in stash) { if (_failed != null) break; yield return Strip(piece); }

                foreach (var piece in stash) { if (_failed != null) break; yield return Act(piece, EThingActionButton.DRESS); }
                foreach (var piece in dressed) { if (_failed != null) break; yield return Act(piece, EThingActionButton.DRESS_ADDITIONAL_SLOT); }

                try { NetworkConnection.Instance?.SendRequest(new InventoryWearRequest()); }
                catch { }
                if (_failed != null) Note(what + ": не удалось — " + _failed, 6f);
                else Note(what + ": готово", 3f);
            }
            finally { _busy = false; Next(); }
        }

        private static IEnumerator Strip(Piece piece)
        {
            yield return Act(piece, EThingActionButton.TAKE_OFF);
        }

        private static List<Piece> Pieces(ESlots.SlotType[] slots)
        {
            var list = new List<Piece>();
            foreach (var slot in slots)
                if (Held.TryGetValue(slot, out var piece) && piece != null && piece.Id > 0)
                    list.Add(new Piece { Id = piece.Id, ThingId = piece.ThingId, Kind = piece.Kind });
            return list;
        }

        private static void Apply(Piece piece)
        {
            var reply = _reply;
            _reply = null;
            if (reply == null) return;

            if (!reply.Success)
            {
                Plugin.Warn("[slots] server refused: " + reply.ErrorMessage);
                _failed = Said(reply.ErrorMessage);
                return;
            }

            foreach (var change in reply.ChangesInSlots)
            {
                if (change == null) continue;
                var slot = (ESlots.SlotType)change.SlotId;
                if (change.IsDressed && change.InventoryId.HasValue)
                {
                    Held[slot] = new Piece
                    {
                        Id = change.InventoryId.Value,
                        ThingId = change.ThingId ?? 0,
                        Kind = change.SubType ?? 0,
                    };
                    if (piece != null && change.ThingId == piece.ThingId) piece.Id = change.InventoryId.Value;
                    Plugin.Trace("[slots] slot " + slot + " ← item " + change.InventoryId);
                }
                else if (!change.IsDressed)
                {
                    Held.Remove(slot);
                    Plugin.Trace("[slots] slot " + slot + " emptied");
                }
            }

            foreach (var change in reply.ChangesInTab)
            {
                if (change == null) continue;
                if (piece != null && change.ThingId == piece.ThingId)
                {
                    piece.Id = change.Id;
                    piece.Kind = change.SubType;
                    Plugin.Trace("[slots] item " + change.ThingId + " in bag with id " + change.Id);
                }
            }
        }

        private static IEnumerator Act(Piece piece, EThingActionButton action)
        {
            Listen();
            foreach (int tab in Tabs(piece))
            {
                _reply = null;
                _waitFor = piece.Id;
                if (!Send(piece.Id, action, tab))
                {
                    if (_failed == null) _failed = "запрос не ушёл на сервер";
                    yield break;
                }

                float t = 0f;
                while (_reply == null && t < 0.8f) { yield return null; t += Time.unscaledDeltaTime; }
                if (_reply != null) { Apply(piece); yield break; }
                Plugin.Trace("[slots] no reply to " + action + " of item " + piece.Id + " in tab " + tab);
            }
            Plugin.Warn("[slots] server rejected " + action + ": item " + piece.Id
                                   + " (thing " + piece.ThingId + ", subtype " + piece.Kind + ")");
            if (_failed == null) _failed = "сервер не ответил";
        }

        private static IEnumerable<int> Tabs(Piece piece)
        {
            if (piece.Kind > 0 && TabOfKind.TryGetValue(piece.Kind, out int tab)) yield return tab;
            yield return 0;
            yield return 18;
        }

        private static ThingContextActionResponseMessage _reply;
        private static string _failed;

        private static string Said(string key)
        {
            if (string.IsNullOrEmpty(key)) return "сервер отказал";
            try
            {
                string text = ResourceStrings.GetString(key);
                if (!string.IsNullOrEmpty(text) && text != key) return text;
            }
            catch { }
            return key;
        }
        private static int _waitFor;
        private static bool _listening;

        private static void Listen()
        {
            if (_listening) return;
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) return;
                conn.AddMessageListener(416, msg =>
                {
                    var reply = msg as ThingContextActionResponseMessage;
                    if (reply != null && reply.Id == _waitFor) _reply = reply;
                });
                _listening = true;
            }
            catch (System.Exception e) { Plugin.Fault("[slots] reply listener: " + e.Message); }
        }

        private static bool Send(int inventoryId, EThingActionButton action, int tab)
        {
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) { Note("нет соединения", 3f); return false; }
                conn.SendRequest(new ContextActionRequest(inventoryId, (int)action,
                                                          (int)EThingContextWindow.WINDOW_INVENTORY, tab, null));
                Plugin.Trace("[slots] " + action + " item " + inventoryId + " tab " + tab);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Fault("[slots] send: " + e.Message);
                Note("не вышло: " + e.Message, 4f);
                return false;
            }
        }

        private static void Note(string text, float seconds)
        {
            try
            {
                var host = _weapons != null ? _weapons.transform.parent as RectTransform : null;
                if (host == null) return;

                if (_note == null)
                {
                    var go = new GameObject("QoLSwapNote", typeof(RectTransform), typeof(Text), typeof(Outline));
                    go.transform.SetParent(host, worldPositionStays: false);
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0f);
                    rt.sizeDelta = new Vector2(360f, 30f);

                    _note = go.GetComponent<Text>();
                    _note.font = SideButtons.GameFont();
                    _note.fontSize = 20;
                    _note.fontStyle = FontStyle.Bold;
                    _note.alignment = TextAnchor.MiddleCenter;
                    _note.color = new Color(1f, 0.93f, 0.6f);
                    _note.raycastTarget = false;
                    _note.horizontalOverflow = HorizontalWrapMode.Overflow;

                    var outline = go.GetComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                }

                var nrt = (RectTransform)_note.transform;
                nrt.anchoredPosition = new Vector2(0f, host.rect.height * 0.24f);
                nrt.SetAsLastSibling();
                _note.text = text;
                _note.gameObject.SetActive(true);
                _noteUntil = Time.unscaledTime + seconds;
            }
            catch { }
        }

        private static Button Build(Button sample, RectTransform face, string name, UnityEngine.Events.UnityAction click)
        {
            if (sample == null || face == null) return null;
            var host = sample.transform.parent as RectTransform;
            var source = face.GetComponent<InteractiveIcon>();
            if (host == null || source == null) return null;

            var hide = new List<string>
            {
                Path(face, IconOf(source) != null ? IconOf(source).transform : null),
                Path(face, FrameOf(source) != null ? FrameOf(source).transform : null),
                Path(face, CounterOf(source) != null ? CounterOf(source).transform : null),
            };

            var go = Object.Instantiate(face.gameObject, host, false);
            go.name = name;
            Clones.StripHotkeys(go, face.gameObject);
            foreach (var part in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (part == null || part is Graphic || part is Mask || part is RectMask2D || part is BaseMeshEffect) continue;
                try { Object.DestroyImmediate(part); }
                catch { }
            }
            foreach (var path in hide)
            {
                if (path == null) continue;
                var t = path.Length == 0 ? go.transform : go.transform.Find(path);
                if (t == null) continue;
                if (t == go.transform) { var im = t.GetComponent<Image>(); if (im != null) im.enabled = false; }
                else t.gameObject.SetActive(false);
            }

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.SetAsLastSibling();

            var root = go.GetComponent<Image>();
            if (root != null) { root.raycastTarget = true; root.color = Color.white; }

            var mark = new GameObject("arrows", typeof(RectTransform), typeof(Image));
            mark.transform.SetParent(go.transform, false);
            var box = (RectTransform)mark.transform;
            box.anchorMin = new Vector2(0.22f, 0.22f);
            box.anchorMax = new Vector2(0.78f, 0.78f);
            box.offsetMin = box.offsetMax = Vector2.zero;
            var art = mark.GetComponent<Image>();
            art.sprite = Cycle();
            art.color = WardrobeLook.Accent;
            art.preserveAspect = true;
            art.raycastTarget = false;

            var button = go.AddComponent<Button>();
            button.targetGraphic = root != null ? (Graphic)root : art;
            button.interactable = true;
            button.onClick.AddListener(() =>
            {
                Plugin.Trace("[slots] button pressed " + name);
                click();
            });
            go.SetActive(true);
            return button;
        }

        private static string Path(Transform root, Transform t)
        {
            if (t == null) return null;
            var parts = new List<string>();
            while (t != null && t != root) { parts.Insert(0, t.name); t = t.parent; }
            return t == root ? string.Join("/", parts.ToArray()) : null;
        }

        private static Sprite Cycle()
        {
            if (_cycle != null && _cycle.texture != null) return _cycle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontUnloadUnusedAsset,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (Arrows(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f)) hit++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _cycle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _cycle.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _cycle;
        }

        private static bool Arrows(float x, float y)
        {
            float dx = x - 64f, dy = y - 64f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float a = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            if (a < 0f) a += 360f;
            if (r >= 36f && r <= 48f && ((a >= 40f && a <= 170f) || (a >= 220f && a <= 350f))) return true;
            return Head(x, y, 40f) || Head(x, y, 220f);
        }

        private static bool Head(float x, float y, float end)
        {
            float e = end * Mathf.Deg2Rad, t = (end - 34f) * Mathf.Deg2Rad;
            var a = new Vector2(64f + 26f * Mathf.Cos(e), 64f + 26f * Mathf.Sin(e));
            var b = new Vector2(64f + 58f * Mathf.Cos(e), 64f + 58f * Mathf.Sin(e));
            var c = new Vector2(64f + 42f * Mathf.Cos(t), 64f + 42f * Mathf.Sin(t));
            var p = new Vector2(x, y);
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static float Cross(Vector2 p, Vector2 a, Vector2 b)
        {
            return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        }

        private static RectTransform Slot(UserMenuCharacterSlotsPanelContent panel, ESlots.SlotType slot)
        {
            var icons = SlotIcons(panel);
            if (icons == null || !icons.TryGetValue(slot, out var icon) || icon == null) return null;
            return icon.transform as RectTransform;
        }

        private struct Plan
        {
            internal float Side, Spread, Lift, Drop, Shove;
            internal Vector2 Relics, Weapons;
            internal bool Fits;
        }

        private static void Arrange(UserMenuCharacterSlotsPanelContent panel)
        {
            var relic = Slot(panel, ESlots.SlotType.RELICSLOT1);
            var hand = Slot(panel, ESlots.SlotType.RIGHTHANDSLOT);
            var top = Slot(panel, ESlots.SlotType.HELMETSLOT);
            var low = Slot(panel, ESlots.SlotType.RINGSLOT2);
            var far = Slot(panel, ESlots.SlotType.NECKLACESLOT);
            var left = Group(LeftColumn(panel));
            var right = Group(RightColumn(panel));
            var row = Group(RelicRow(panel));
            var arms = Group(WeaponRow(panel));
            var toggle = RelicToggle(panel) != null ? (RectTransform)RelicToggle(panel).transform : null;
            var lever = WeaponToggle(panel) != null ? (RectTransform)WeaponToggle(panel).transform : null;
            var host = left != null ? left.parent as RectTransform : null;
            if (host == null || relic == null || hand == null || top == null || low == null || far == null
                || right == null || row == null || arms == null) return;
            if (relic.rect.width < 1f || hand.rect.width < 1f || top.rect.width < 1f) return;

            var groups = new[] { left, right, row, arms };
            float even = hand.rect.height > 1f ? relic.rect.height / hand.rect.height : 1f;
            var plan = default(Plan);
            float k = 0.92f;
            for (; ; k -= 0.02f)
            {
                foreach (var g in groups)
                {
                    Restore(g);
                    float z = g == arms ? k * even : k;
                    g.localScale = new Vector3(z, z, 1f);
                }
                if (toggle != null) Restore(toggle);
                if (lever != null) Restore(lever);
                plan = Measure(panel, host, relic, hand, top, low, far);
                if (plan.Fits || k <= 0.7f) break;
            }

            Shift(host, left, new Vector2(-plan.Spread, -plan.Drop));
            Shift(host, right, new Vector2(plan.Spread, -plan.Drop));
            Shift(host, row, new Vector2(0f, plan.Lift));
            if (toggle != null) Shift(host, toggle, new Vector2(0f, plan.Lift));
            Shift(host, arms, new Vector2(plan.Shove, 0f));
            if (lever != null) Shift(host, lever, new Vector2(plan.Shove, 0f));

            Put(host, _relics, plan.Relics, plan.Side);
            Put(host, _weapons, plan.Weapons, plan.Side);

            string told = "scale " + k.ToString("0.00") + ", side " + plan.Side.ToString("0") + ", lift " + plan.Lift.ToString("0")
                        + ", drop " + plan.Drop.ToString("0") + ", spread " + plan.Spread.ToString("0") + (plan.Fits ? "" : ", does not fit");
            if (told != _told) { _told = told; Plugin.Trace("[slots] gear layout: " + told); }
        }

        private static Plan Measure(UserMenuCharacterSlotsPanelContent panel, RectTransform host, RectTransform relic,
                                    RectTransform hand, RectTransform top, RectTransform low, RectTransform far)
        {
            var relicBox = Box(host, relic);
            var other = Slot(panel, ESlots.SlotType.LEFTHANDSLOT);
            var handBox = other != null && other.gameObject.activeInHierarchy
                ? Union(Box(host, hand), Box(host, other)) : Box(host, hand);
            var cell = Box(host, top);
            var bottom = Box(host, low);
            var edge = Box(host, far);
            var area = host.rect;

            var plan = new Plan { Side = relicBox.height };
            plan.Shove = (plan.Side + Gap) * 0.5f;
            plan.Spread = Mathf.Max(0f, cell.xMax - (handBox.xMin + plan.Shove - Gap - plan.Side - Gap));
            plan.Spread = Mathf.Max(plan.Spread, handBox.xMax + plan.Shove + Gap - edge.xMin);
            float need = Mathf.Max(0f, cell.yMax - (relicBox.yMin - Gap * 0.5f));
            float below = Mathf.Max(0f, bottom.yMin - area.yMin);
            float above = Mathf.Max(0f, area.yMax - relicBox.yMax);
            plan.Lift = need <= below ? 0f : Mathf.Min(need - below, above);
            plan.Drop = need - plan.Lift;
            plan.Fits = plan.Drop <= below + 0.5f
                     && cell.xMin - plan.Spread >= area.xMin - 0.5f
                     && edge.xMax + plan.Spread <= area.xMax + 0.5f;
            plan.Relics = new Vector2(cell.center.x - plan.Spread, relicBox.center.y + plan.Lift);
            plan.Weapons = new Vector2(handBox.xMin + plan.Shove - Gap - plan.Side * 0.5f, handBox.center.y);
            return plan;
        }

        private static RectTransform Group(GridLayoutGroup group)
        {
            return group != null ? (RectTransform)group.transform : null;
        }

        private static void Restore(RectTransform rt)
        {
            int key = rt.GetInstanceID();
            if (Home.TryGetValue(key, out var at)) rt.anchoredPosition = at;
            else Home[key] = rt.anchoredPosition;
        }

        private static void Shift(RectTransform host, RectTransform rt, Vector2 by)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            Vector2 local = parent.InverseTransformVector(host.TransformVector(by));
            rt.anchoredPosition = Home[rt.GetInstanceID()] + local;
        }

        private static void Put(RectTransform host, Button button, Vector2 center, float side)
        {
            if (button == null) return;
            var rt = (RectTransform)button.transform;
            var parent = rt.parent as RectTransform;
            if (parent == null || rt.rect.width < 1f) return;
            float wide = parent.InverseTransformVector(host.TransformVector(new Vector3(side, 0f, 0f))).magnitude;
            float k = wide / rt.rect.width;
            rt.localScale = new Vector3(k, k, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            Vector3 at = parent.InverseTransformPoint(host.TransformPoint(center));
            rt.localPosition = new Vector3(at.x, at.y, rt.localPosition.z);
        }

        private static Rect Box(RectTransform host, RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 a = host.InverseTransformPoint(corners[0]);
            Vector2 b = host.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }
    }

    [HarmonyPatch(typeof(UserMenuCharacterSlotsPanelContent), "UpdateView")]
    public static class SlotSwapViewPatch
    {
        private static void Postfix(UserMenuCharacterSlotsPanelContent __instance,
                                    IDictionary<ESlots.SlotType, InventoryWearResponseMessageItem> wearedSlots)
        {
            if (__instance == null || Manikin.Owns(__instance.transform)) return;
            SlotSwap.Sync(__instance, wearedSlots);
        }
    }
}
