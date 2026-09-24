using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.Things.Actions;
using Transport.Messages.Responses.User.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class DressDrag
    {
        private const float Twice = 0.3f;
        private const float Patience = 4f;
        private const float RefreshDelay = 0.25f;

        private static readonly Color Fits = new Color(0.35f, 0.9f, 0.4f, 0.28f);
        private static readonly Color FitsHere = new Color(0.45f, 1f, 0.5f, 0.55f);
        private static readonly Color Wrong = new Color(1f, 0.3f, 0.25f, 0.45f);

        private static readonly FieldInfo HintsField = AccessTools.Field(typeof(UserMenuController), "_hintResolverContext");
        private static readonly MethodInfo SendAction = AccessTools.Method(typeof(HintResolverContext), "SendActionRequest");
        private static readonly MethodInfo CellClick = AccessTools.Method(typeof(ItemRenderer<InventoryThingTabContentDto>), "FireItemClick");
        private static readonly MethodInfo IconClick = AccessTools.Method(typeof(InteractiveIcon), "OnButtonClick");
        private static readonly FieldInfo CellButton = AccessTools.Field(typeof(InventoryThingItemRenderer), "Button");
        private static readonly FieldInfo CellImage = AccessTools.Field(typeof(InventoryThingItemRenderer), "ThingImage");
        private static readonly FieldInfo IconButton = AccessTools.Field(typeof(InteractiveIcon), "Button");
        private static readonly FieldInfo IconImage = AccessTools.Field(typeof(InteractiveIcon), "IconImage");

        private static readonly AccessTools.FieldRef<UserMenuCharacterSlotsPanelContent, IDictionary<ESlots.SlotType, InteractiveIcon>> SlotIcons =
            AccessTools.FieldRefAccess<UserMenuCharacterSlotsPanelContent, IDictionary<ESlots.SlotType, InteractiveIcon>>("_slotItemRenderers");

        private sealed class Worn
        {
            internal int Id;
            internal int ThingId;
            internal int Kind;
        }

        private static UserMenuCharacterSlotsPanelContent _panel;
        private static readonly Dictionary<ESlots.SlotType, Worn> Slots = new Dictionary<ESlots.SlotType, Worn>();
        private static readonly HashSet<InteractiveIcon> Hooked = new HashSet<InteractiveIcon>();
        private static readonly Dictionary<InteractiveIcon, Image> Glows = new Dictionary<InteractiveIcon, Image>();

        private static UnityEngine.Object _pending;
        private static float _pendingAt;
        private static Coroutine _pendingRun;
        private static bool _busy;
        private static ThingContextActionResponseMessage _reply;
        private static int _waitFor;
        private static float _refreshAt;
        private static bool _refreshing;

        private static GameObject _ghostCanvas;
        private static RectTransform _ghost;
        private static Image _ghostImage;
        private static IList<ESlots.SlotType> _allowed;
        private static InteractiveIcon _hover;

        private static UserMenuController Menu
        {
            get { try { return Controllers.Get<UserMenuController>(); } catch { return null; } }
        }

        private static HintResolverContext Hints
        {
            get
            {
                var menu = Menu;
                return menu != null && HintsField != null ? HintsField.GetValue(menu) as HintResolverContext : null;
            }
        }

        internal static bool InMenu(Transform t)
        {
            try
            {
                var menu = Menu;
                if (menu == null || !menu.IsWindowOpened || menu.Window == null || t == null) return false;
                if (Manikin.Owns(t)) return false;
                return t.IsChildOf(menu.Window.transform) && _panel != null && _panel.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        internal static void Sync(UserMenuCharacterSlotsPanelContent panel, IDictionary<ESlots.SlotType, InventoryWearResponseMessageItem> worn)
        {
            try
            {
                _panel = panel;
                Slots.Clear();
                if (worn != null)
                    foreach (var pair in worn)
                        if (pair.Value != null)
                            Slots[pair.Key] = new Worn { Id = pair.Value.InventoryId, ThingId = pair.Value.ThingId, Kind = pair.Value.Subtype };
                Hooked.RemoveWhere(one => one == null);
                var gone = new List<InteractiveIcon>();
                foreach (var pair in Glows) if (pair.Key == null || pair.Value == null) gone.Add(pair.Key);
                foreach (var one in gone) Glows.Remove(one);
                var icons = SlotIcons(panel);
                if (icons == null) return;
                foreach (var pair in icons)
                {
                    var icon = pair.Value;
                    if (icon == null || !Hooked.Add(icon)) continue;
                    var button = IconButton != null ? IconButton.GetValue(icon) as Button : null;
                    if (button != null && IconClick != null)
                    {
                        button.onClick.RemoveListener((UnityAction)Delegate.CreateDelegate(typeof(UnityAction), icon, IconClick));
                        var slot = pair.Key;
                        button.onClick.AddListener(() => SlotClicked(icon, slot));
                    }
                    var drag = icon.gameObject.GetComponent<DressDragHandle>() ?? icon.gameObject.AddComponent<DressDragHandle>();
                    drag.Icon = icon;
                    drag.Slot = pair.Key;
                }
            }
            catch (Exception e) { Plugin.Trace("[вещи] слоты: " + e.Message); }
        }

        internal static void HookCell(InventoryThingItemRenderer cell)
        {
            try
            {
                var button = CellButton != null ? CellButton.GetValue(cell) as Button : null;
                if (button == null || CellClick == null) return;
                button.onClick.RemoveListener((UnityAction)Delegate.CreateDelegate(typeof(UnityAction), cell, CellClick));
                button.onClick.AddListener(() => BagClicked(cell));
                var drag = cell.gameObject.GetComponent<DressDragHandle>() ?? cell.gameObject.AddComponent<DressDragHandle>();
                drag.Cell = cell;
            }
            catch (Exception e) { Plugin.Trace("[вещи] ячейка: " + e.Message); }
        }

        internal static bool Dragging;

        private static void BagClicked(InventoryThingItemRenderer cell)
        {
            if (cell == null || Dragging) return;
            var item = cell.Data;
            if (!InMenu(cell.transform) || item == null || !Wearable((int)item.SubType)) { Pass(cell); return; }
            if (Second(cell))
            {
                var data = cell.Data;
                if (data != null && data.InventoryId > 0) Run(Dress(data.InventoryId, (int)data.SubType, null));
                return;
            }
            Wait(cell, () => Pass(cell));
        }

        private static void SlotClicked(InteractiveIcon icon, ESlots.SlotType slot)
        {
            if (icon == null || Dragging) return;
            if (!InMenu(icon.transform)) { Pass(icon); return; }
            if (Second(icon))
            {
                if (icon.Id > 0) Run(Undress(icon.Id));
                return;
            }
            Wait(icon, () => Pass(icon));
        }

        private static bool Second(UnityEngine.Object what)
        {
            bool twice = ReferenceEquals(_pending, what) && Time.unscaledTime - _pendingAt <= Twice;
            if (!twice) return false;
            if (_pendingRun != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_pendingRun);
            _pendingRun = null;
            _pending = null;
            return true;
        }

        private static void Wait(UnityEngine.Object what, Action single)
        {
            if (_pendingRun != null && Plugin.Instance != null) Plugin.Instance.StopCoroutine(_pendingRun);
            _pending = what;
            _pendingAt = Time.unscaledTime;
            _pendingRun = Plugin.Instance != null ? Plugin.Instance.StartCoroutine(Later(single)) : null;
            if (_pendingRun == null) single();
        }

        private static IEnumerator Later(Action single)
        {
            float until = Time.unscaledTime + Twice;
            while (Time.unscaledTime < until) yield return null;
            _pending = null;
            _pendingRun = null;
            try { single(); }
            catch (Exception e) { Plugin.Trace("[вещи] одиночный клик: " + e.Message); }
        }

        private static void Pass(InventoryThingItemRenderer cell)
        {
            try { CellClick?.Invoke(cell, null); }
            catch (Exception e) { Plugin.Trace("[вещи] клик ячейки: " + e.Message); }
        }

        private static void Pass(InteractiveIcon icon)
        {
            try { IconClick?.Invoke(icon, null); }
            catch (Exception e) { Plugin.Trace("[вещи] клик слота: " + e.Message); }
        }

        private static void Run(IEnumerator job)
        {
            if (_busy || Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(Guard(job));
        }

        private static IEnumerator Guard(IEnumerator job)
        {
            _busy = true;
            try { while (job.MoveNext()) yield return job.Current; }
            finally { _busy = false; }
        }

        private static EThingActionButton Into(ESlots.SlotType slot) =>
            ESlots.IsAdditionalSlot(slot) ? EThingActionButton.DRESS_ADDITIONAL_SLOT : EThingActionButton.DRESS;

        internal static bool Wearable(int kind)
        {
            try { return kind > 0 && ESlots.GetSlotsByThingSubtype((EThingSubType)kind).Count > 0; }
            catch { return false; }
        }

        private static bool Accepts(int kind, ESlots.SlotType slot)
        {
            try { return ESlots.GetSlotsByThingSubtype((EThingSubType)kind).Contains(slot); }
            catch { return false; }
        }

        private static IEnumerator Dress(int id, int kind, ESlots.SlotType? into)
        {
            if (into.HasValue && Slots.TryGetValue(into.Value, out var there) && there != null && there.Id > 0 && there.Id != id && there.Kind == kind)
            {
                ThingContextActionResponseMessage freed = null;
                yield return Act(there.Id, EThingActionButton.TAKE_OFF, 0, r => freed = r);
                if (freed == null || !freed.Success) yield break;
            }
            var button = into.HasValue ? Into(into.Value) : EThingActionButton.DRESS;
            yield return Act(id, button, Tab(), null);
        }

        private static IEnumerator Undress(int id)
        {
            yield return Act(id, EThingActionButton.TAKE_OFF, 0, null);
        }

        private static IEnumerator Move(ESlots.SlotType from, ESlots.SlotType to)
        {
            if (!Slots.TryGetValue(from, out var moving) || moving == null || moving.Id <= 0) yield break;
            Slots.TryGetValue(to, out var staying);
            if (staying != null && (staying.Id <= 0 || !Accepts(staying.Kind, from))) staying = null;

            int movedId, movedTab;
            ThingContextActionResponseMessage reply = null;
            yield return Act(moving.Id, EThingActionButton.TAKE_OFF, 0, r => reply = r);
            if (!Landed(reply, moving.ThingId, out movedId, out movedTab)) yield break;

            int stayedId = 0, stayedTab = 0;
            if (staying != null)
            {
                reply = null;
                yield return Act(staying.Id, EThingActionButton.TAKE_OFF, 0, r => reply = r);
                if (!Landed(reply, staying.ThingId, out stayedId, out stayedTab)) staying = null;
            }

            yield return Act(movedId, Into(to), movedTab, null);
            if (staying != null) yield return Act(stayedId, Into(from), stayedTab, null);
        }

        private static bool Landed(ThingContextActionResponseMessage reply, int thingId, out int id, out int tab)
        {
            id = 0;
            tab = 0;
            if (reply == null || !reply.Success || reply.ChangesInTab == null) return false;
            foreach (var change in reply.ChangesInTab)
                if (change != null && change.ThingId == thingId) { id = change.Id; tab = reply.TabId; return id > 0; }
            return false;
        }

        private static int Tab()
        {
            try
            {
                var hints = Hints;
                int? tab = null;
                if (hints != null && hints.ResolveCurrentTabCallback != null) hints.ResolveCurrentTabCallback(out tab);
                return tab.GetValueOrDefault();
            }
            catch { return 0; }
        }

        private static IEnumerator Act(int id, EThingActionButton button, int tab, Action<ThingContextActionResponseMessage> done)
        {
            var hints = Hints;
            if (hints == null || SendAction == null) { done?.Invoke(null); yield break; }
            _reply = null;
            _waitFor = id;
            var context = new ResponseCallbackContext();
            try
            {
                SendAction.Invoke(hints, new object[] { id, button, EThingContextWindow.WINDOW_INVENTORY, (int?)tab, null, context, null });
                Plugin.Trace("[вещи] " + button + " вещь " + id + " вкладка " + tab);
            }
            catch (Exception e)
            {
                Plugin.Warn("[вещи] отправка " + button + ": " + e.Message);
                Forget(context);
                done?.Invoke(null);
                yield break;
            }
            float until = Time.unscaledTime + Patience;
            while (_reply == null && Time.unscaledTime < until) yield return null;
            Forget(context);
            var reply = _reply;
            if (reply == null) Plugin.Trace("[вещи] сервер не ответил на " + button + " вещи " + id);
            _reply = null;
            _waitFor = 0;
            done?.Invoke(reply);
        }

        private static void Forget(ResponseCallbackContext context)
        {
            try { context.Destroy(); }
            catch (Exception e) { Plugin.Trace("[вещи] контекст ответа: " + e.Message); }
        }

        internal static void Heard(HintResolverContext from, EThingActionButton button, object msg)
        {
            var reply = msg as ThingContextActionResponseMessage;
            if (reply == null) return;
            if (_waitFor != 0 && reply.Id == _waitFor) _reply = reply;
            if (!reply.Success || from == null || !ReferenceEquals(from, Hints)) return;
            if (button != EThingActionButton.DRESS && button != EThingActionButton.DRESS_ADDITIONAL_SLOT && button != EThingActionButton.TAKE_OFF) return;
            _refreshAt = Time.unscaledTime + RefreshDelay;
            if (!_refreshing && Plugin.Instance != null) Plugin.Instance.StartCoroutine(Refresh());
        }

        private static IEnumerator Refresh()
        {
            _refreshing = true;
            try
            {
                while (Time.unscaledTime < _refreshAt || _busy) yield return null;
                var menu = Menu;
                if (menu == null || !menu.IsWindowOpened) yield break;
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) yield break;
                int tab = Tab();
                nc.SendRequest(new GetTabContentRequest(tab, (int)EThingContextWindow.WINDOW_INVENTORY));
                Plugin.Trace("[вещи] перечитываю вкладку сумки " + tab);
            }
            finally { _refreshing = false; }
        }

        internal static void Lift(Sprite sprite, int kind)
        {
            if (sprite == null) return;
            if (_ghostCanvas == null)
            {
                _ghostCanvas = new GameObject("QoLDressGhost", typeof(Canvas));
                UnityEngine.Object.DontDestroyOnLoad(_ghostCanvas);
                var canvas = _ghostCanvas.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 30000;
                var go = new GameObject("ghost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
                go.transform.SetParent(_ghostCanvas.transform, false);
                _ghost = (RectTransform)go.transform;
                _ghost.anchorMin = _ghost.anchorMax = Vector2.zero;
                _ghost.pivot = new Vector2(0.5f, 0.5f);
                _ghostImage = go.GetComponent<Image>();
                _ghostImage.raycastTarget = false;
                _ghostImage.preserveAspect = true;
                var group = go.GetComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            float side = Mathf.Max(48f, Screen.height * 0.06f);
            _ghost.sizeDelta = new Vector2(side, side);
            _ghostImage.sprite = sprite;
            _ghostImage.color = new Color(1f, 1f, 1f, 0.8f);
            _ghostCanvas.SetActive(true);
            try { _allowed = ESlots.GetSlotsByThingSubtype((EThingSubType)kind); }
            catch { _allowed = null; }
            _hover = null;
            Move(Input.mousePosition, null);
        }

        internal static void Move(Vector2 screen, PointerEventData data)
        {
            if (_ghost != null) _ghost.anchoredPosition = screen;
            ESlots.SlotType slot;
            _hover = data != null ? Under(data, out slot) : null;
            Paint();
        }

        internal static void Drop()
        {
            if (_ghostCanvas != null) _ghostCanvas.SetActive(false);
            _allowed = null;
            _hover = null;
            Paint();
        }

        private static void Paint()
        {
            if (_panel == null) return;
            var icons = SlotIcons(_panel);
            if (icons == null) return;
            foreach (var pair in icons)
            {
                var icon = pair.Value;
                if (icon == null) continue;
                bool fits = _allowed != null && _allowed.Contains(pair.Key);
                bool here = icon == _hover;
                Color? tint = null;
                if (here) tint = fits ? FitsHere : Wrong;
                else if (fits) tint = Fits;
                Glow(icon, tint);
            }
        }

        private static void Glow(InteractiveIcon icon, Color? tint)
        {
            Glows.TryGetValue(icon, out var glow);
            if (tint == null)
            {
                if (glow != null && glow.gameObject.activeSelf) glow.gameObject.SetActive(false);
                return;
            }
            if (glow == null)
            {
                var go = new GameObject("QoLDropGlow", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(icon.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                glow = go.GetComponent<Image>();
                glow.raycastTarget = false;
                glow.sprite = OnlineWindow.Rounded(8);
                glow.type = Image.Type.Sliced;
                Glows[icon] = glow;
            }
            glow.transform.SetAsLastSibling();
            if (glow.color != tint.Value) glow.color = tint.Value;
            if (!glow.gameObject.activeSelf) glow.gameObject.SetActive(true);
        }

        private static InteractiveIcon Under(PointerEventData data, out ESlots.SlotType slot)
        {
            slot = default(ESlots.SlotType);
            if (_panel == null || EventSystem.current == null) return null;
            var icons = SlotIcons(_panel);
            if (icons == null) return null;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            foreach (var hit in hits)
            {
                var icon = hit.gameObject != null ? hit.gameObject.GetComponentInParent<InteractiveIcon>() : null;
                if (icon == null) continue;
                foreach (var pair in icons)
                    if (pair.Value == icon) { slot = pair.Key; return icon; }
            }
            return null;
        }

        internal static void Shutdown()
        {
            if (_ghostCanvas != null) UnityEngine.Object.Destroy(_ghostCanvas);
            _ghostCanvas = null;
            _ghost = null;
            _ghostImage = null;
            foreach (var glow in Glows.Values) if (glow != null) UnityEngine.Object.Destroy(glow.gameObject);
            Glows.Clear();
            Hooked.Clear();
            Slots.Clear();
            _panel = null;
        }

        internal static Sprite PictureOf(InventoryThingItemRenderer cell)
        {
            var image = CellImage != null ? CellImage.GetValue(cell) as Image : null;
            return image != null && image.enabled ? image.sprite : null;
        }

        internal static Sprite PictureOf(InteractiveIcon icon)
        {
            var image = IconImage != null ? IconImage.GetValue(icon) as Image : null;
            return image != null ? image.sprite : null;
        }

        internal static int KindOf(ESlots.SlotType slot) => Slots.TryGetValue(slot, out var worn) && worn != null ? worn.Kind : 0;

        internal static void Dropped(DressDragHandle from, PointerEventData data)
        {
            try
            {
                var target = Under(data, out var slot);
                if (from.Cell != null)
                {
                    var item = from.Cell.Data;
                    if (target == null || item == null || item.InventoryId <= 0) return;
                    if (!Accepts((int)item.SubType, slot)) { Notice.Show("Эту вещь сюда не надеть", 3f); return; }
                    Plugin.Trace("[вещи] перетащил вещь " + item.InventoryId + " на слот " + slot);
                    Run(Dress(item.InventoryId, (int)item.SubType, slot));
                    return;
                }
                if (from.Icon == null || from.Icon.Id <= 0) return;
                if (target == null)
                {
                    Plugin.Trace("[вещи] стащил вещь " + from.Icon.Id + " со слота " + from.Slot);
                    Run(Undress(from.Icon.Id));
                    return;
                }
                if (slot == from.Slot) return;
                if (!Accepts(KindOf(from.Slot), slot)) { Notice.Show("Эту вещь сюда не надеть", 3f); return; }
                if (ESlots.IsAdditionalSlot(slot) == ESlots.IsAdditionalSlot(from.Slot))
                {
                    Notice.Show("Переставить можно только между основными и запасными слотами", 4f);
                    return;
                }
                Plugin.Trace("[вещи] переношу вещь со слота " + from.Slot + " на слот " + slot);
                Run(Move(from.Slot, slot));
            }
            catch (Exception e) { Plugin.Trace("[вещи] бросок: " + e.Message); }
        }
    }

    internal sealed class DressDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal InventoryThingItemRenderer Cell;
        internal InteractiveIcon Icon;
        internal ESlots.SlotType Slot;
        private bool _lifted;
        private ScrollRect _scroll;
        private UserMenuCharacterSlotsPanelContent _doll;

        public void OnBeginDrag(PointerEventData data)
        {
            _lifted = false;
            DressDrag.Dragging = true;
            if (data.button != PointerEventData.InputButton.Left || !DressDrag.InMenu(transform)) { Forward(data, 0); return; }
            if (Cell != null)
            {
                var item = Cell.Data;
                if (item == null || item.InventoryId <= 0 || !DressDrag.Wearable((int)item.SubType)) { Forward(data, 0); return; }
                DressDrag.Lift(DressDrag.PictureOf(Cell), (int)item.SubType);
            }
            else if (Icon != null)
            {
                if (Icon.Id <= 0) { Forward(data, 0); return; }
                DressDrag.Lift(DressDrag.PictureOf(Icon), DressDrag.KindOf(Slot));
            }
            else return;
            _lifted = true;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!_lifted) { Forward(data, 1); return; }
            DressDrag.Move(data.position, data);
        }

        public void OnEndDrag(PointerEventData data)
        {
            DressDrag.Dragging = false;
            if (!_lifted) { Forward(data, 2); return; }
            _lifted = false;
            DressDrag.Drop();
            DressDrag.Dropped(this, data);
        }

        private void Forward(PointerEventData data, int stage)
        {
            if (Icon != null)
            {
                if (_doll == null) _doll = GetComponentInParent<UserMenuCharacterSlotsPanelContent>();
                if (_doll == null) return;
                if (stage == 0) _doll.OnBeginDrag(data);
                else if (stage == 1) _doll.OnDrag(data);
                return;
            }
            if (Cell == null) return;
            if (_scroll == null) _scroll = GetComponentInParent<ScrollRect>();
            if (_scroll == null) return;
            if (stage == 0) _scroll.OnBeginDrag(data);
            else if (stage == 1) _scroll.OnDrag(data);
            else _scroll.OnEndDrag(data);
        }

        private void OnDisable()
        {
            DressDrag.Dragging = false;
            if (!_lifted) return;
            _lifted = false;
            DressDrag.Drop();
        }
    }

    [HarmonyPatch(typeof(UserMenuCharacterSlotsPanelContent), "UpdateView")]
    public static class DressDragSlotsPatch
    {
        private static void Postfix(UserMenuCharacterSlotsPanelContent __instance,
                                    IDictionary<ESlots.SlotType, InventoryWearResponseMessageItem> wearedSlots)
        {
            if (__instance == null || Manikin.Owns(__instance.transform)) return;
            DressDrag.Sync(__instance, wearedSlots);
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "Awake")]
    public static class DressDragCellPatch
    {
        private static void Postfix(InventoryThingItemRenderer __instance) => DressDrag.HookCell(__instance);
    }

    [HarmonyPatch(typeof(HintResolverContext), "ParseThingContextActionResponse")]
    public static class DressDragReplyPatch
    {
        private static void Postfix(HintResolverContext __instance, EThingActionButton buttonId, object msg) => DressDrag.Heard(__instance, buttonId, msg);
    }
}
