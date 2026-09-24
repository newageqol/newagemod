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

                if (_weapons == null)
                    _weapons = Build(WeaponToggle(panel), "QoLSwapWeapons", "dodge7",
                                     () => Swap("оружие", MainWeapons, SpareWeapons));
                if (_relics == null)
                    _relics = Build(RelicToggle(panel), "QoLSwapRelics", "dodge19",
                                    () => Swap("реликвии", MainRelics, SpareRelics));

                Place(_weapons, Slot(panel, ESlots.SlotType.RIGHTHANDSLOT), toTop: false);
                Place(_relics, Slot(panel, ESlots.SlotType.RELICSLOT1), toTop: true);

                Show(_weapons, Any(MainWeapons) || Any(SpareWeapons));
                Show(_relics, Any(MainRelics) || Any(SpareRelics));
            }
            catch (System.Exception e) { Plugin.Fault("[slots] окно снаряжения: " + e.Message); }
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
                Plugin.Trace("[slots] меняю " + what + ": надето " + dressed.Count + ", в запасе " + stash.Count);
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
                Plugin.Warn("[slots] сервер отказал: " + reply.ErrorMessage);
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
                    Plugin.Trace("[slots] слот " + slot + " ← вещь " + change.InventoryId);
                }
                else if (!change.IsDressed)
                {
                    Held.Remove(slot);
                    Plugin.Trace("[slots] слот " + slot + " опустел");
                }
            }

            foreach (var change in reply.ChangesInTab)
            {
                if (change == null) continue;
                if (piece != null && change.ThingId == piece.ThingId)
                {
                    piece.Id = change.Id;
                    piece.Kind = change.SubType;
                    Plugin.Trace("[slots] вещь " + change.ThingId + " в сумке под номером " + change.Id);
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
                if (!Send(piece.Id, action, tab)) yield break;

                float t = 0f;
                while (_reply == null && t < 0.8f) { yield return null; t += Time.unscaledDeltaTime; }
                if (_reply != null) { Apply(piece); yield break; }
                Plugin.Trace("[slots] молчание на " + action + " вещи " + piece.Id + " во вкладке " + tab);
            }
            Plugin.Warn("[slots] сервер не принял " + action + ": вещь " + piece.Id
                                   + " (предмет " + piece.ThingId + ", подтип " + piece.Kind + ")");
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
            catch (System.Exception e) { Plugin.Fault("[slots] слушатель ответов: " + e.Message); }
        }

        private static bool Send(int inventoryId, EThingActionButton action, int tab)
        {
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) { Note("нет соединения", 3f); return false; }
                conn.SendRequest(new ContextActionRequest(inventoryId, (int)action,
                                                          (int)EThingContextWindow.WINDOW_INVENTORY, tab, null));
                Plugin.Trace("[slots] " + action + " вещь " + inventoryId + " вкладка " + tab);
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Fault("[slots] отправка: " + e.Message);
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

        private static Button Build(Button sample, string name, string icon, UnityEngine.Events.UnityAction click)
        {
            if (sample == null) return null;
            var host = sample.transform.parent as RectTransform;
            var sampleRt = sample.transform as RectTransform;
            if (host == null || sampleRt == null) return null;

            float side = Mathf.Max(22f, sampleRt.rect.height * 0.75f);
            var go = SideButtons.BuildCell(host, name, side, out var art, out _, out var background);
            if (go == null) return null;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.SetAsLastSibling();

            if (art != null)
            {
                var sprite = AtlasUtils.GetQuickButtonSprite(icon);
                art.gameObject.SetActive(true);
                art.enabled = true;
                art.preserveAspect = true;
                art.color = Color.white;
                if (sprite != null && sprite.name != "unknown") art.sprite = sprite;
            }

            var button = go.GetComponent<Button>();
            if (button == null) button = go.AddComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.targetGraphic = background;
            button.interactable = true;
            button.onClick.AddListener(() =>
            {
                Plugin.Trace("[slots] нажата кнопка " + name);
                click();
            });
            return button;
        }

        private static RectTransform Slot(UserMenuCharacterSlotsPanelContent panel, ESlots.SlotType slot)
        {
            var icons = SlotIcons(panel);
            if (icons == null || !icons.TryGetValue(slot, out var icon) || icon == null) return null;
            return icon.transform as RectTransform;
        }

        private static void Place(Button button, RectTransform slot, bool toTop)
        {
            if (button == null || slot == null) return;
            var rt = (RectTransform)button.transform;
            var host = rt.parent as RectTransform;
            if (host == null || slot.rect.width < 1f) return;

            Vector3 world = slot.TransformPoint((Vector3)slot.rect.center);
            Vector2 local = host.InverseTransformPoint(world);
            float x = local.x - (slot.rect.width + rt.rect.width) * 0.5f - 6f;
            float y = local.y + slot.rect.height * 0.5f;
            if (toTop) y = Mathf.Max(y, host.rect.yMax - 6f);

            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
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
