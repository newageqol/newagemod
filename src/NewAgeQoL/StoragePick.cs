using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class StoragePick
    {
        private struct Pick
        {
            internal int Id, Tab, Qty;
        }

        private const float High = 38f;
        private const float Edge = 10f;
        private const int GiveUp = 3;

        private static readonly Dictionary<int, Pick> Picked = new Dictionary<int, Pick>();
        private static readonly FieldInfo ContextOf = AccessTools.Field(typeof(HintResolverContext), "_contextWindow");
        private static readonly FieldInfo TabsOf = AccessTools.Field(typeof(StandardContentWindowPanel), "TabPanel");
        private static readonly FieldInfo ContentOf = AccessTools.Field(typeof(StandardContentWindowPanel), "PanelContentParent");
        private static readonly Vector3[] Corners = new Vector3[4];

        private static BasePanelContentWindow _window;
        private static StandardContentWindowPanel _panel;
        private static RectTransform _content;
        private static Text _title;
        private static Button _flip;
        private static Button _go;
        private static Text _flipLabel;
        private static Text _goLabel;
        private static bool _toBox;
        private static bool _on;
        private static bool _running;
        private static bool _stop;
        private static int _done;
        private static int _total;
        private static float _next;

        internal static void Opened(BasePanelContentWindow window, bool toBox)
        {
            Reset();
            _window = window;
            _toBox = toBox;
            Plugin.Trace("[storage pick] window opened: " + (toBox ? "put" : "get"));
        }

        private static void Reset()
        {
            if (_running) _stop = true;
            _on = false;
            Picked.Clear();
            _window = null;
            _panel = null;
            _content = null;
            _title = null;
            if (_flip != null) UnityEngine.Object.Destroy(_flip.gameObject);
            if (_go != null) UnityEngine.Object.Destroy(_go.gameObject);
            _flip = null;
            _go = null;
            _flipLabel = null;
            _goLabel = null;
        }

        internal static bool Catch(HintResolverContext context, int id, int? quantity, int? tab)
        {
            try
            {
                if (!_on || _window == null || ContextOf == null || context == null) return false;
                var where = (EThingContextWindow)ContextOf.GetValue(context);
                var mine = _toBox ? EThingContextWindow.WINDOW_PUT_THINGS_IN_BOX : EThingContextWindow.DEFAULT_WINDOW_BY_LOCATION;
                if (where != mine) return false;
                if (_running) return true;
                if (Picked.Remove(id)) { _next = 0f; return true; }
                int at = tab ?? Tab();
                if (at <= 0) return false;
                Picked[id] = new Pick { Id = id, Tab = at, Qty = Math.Max(1, quantity ?? 1) };
                _next = 0f;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Caught("[storage pick] click: " + e.Message);
                return false;
            }
        }

        private static int Tab()
        {
            var tab = _panel != null ? _panel.SelectedTab : null;
            return tab != null ? tab.Id : 0;
        }

        internal static void Tick()
        {
            if (ReferenceEquals(_window, null)) return;
            if (_window == null) { Reset(); return; }
            try
            {
                if (_on && _flip != null) Marks();
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.15f;
                if (_flip == null && !Build()) return;
                Stand();
                Paint();
                Marks();
            }
            catch (Exception e) { Plugin.Caught("[storage pick] " + e.Message); }
        }

        private static bool Build()
        {
            _panel = _window.GetComponentInChildren<StandardContentWindowPanel>(true);
            if (_panel == null) return false;
            _content = (ContentOf != null ? ContentOf.GetValue(_panel) as Transform : null) as RectTransform;
            var tabs = TabsOf != null ? TabsOf.GetValue(_panel) as TabPanel : null;
            _title = tabs != null ? tabs.TabHeaderText : null;
            if (_content == null) return false;

            _flip = OnlineWindow.MakeGameButton(_panel.transform, Lang.T("Выбрать несколько"), 240f, High, Flip);
            _go = OnlineWindow.MakeGameButton(_panel.transform, "", 320f, High, Go);
            Loose(_flip, new Vector2(0f, 0.5f), 240f);
            Loose(_go, new Vector2(1f, 0.5f), 320f);
            _flipLabel = _flip.GetComponentInChildren<Text>();
            _goLabel = _go.GetComponentInChildren<Text>();
            Loud(_flip, _flipLabel, WardrobeLook.Accent, WardrobeLook.OnAccent);
            Loud(_go, _goLabel, WardrobeLook.Good, WardrobeLook.OnAccent);
            _go.gameObject.SetActive(false);
            Plugin.Trace("[storage pick] buttons built, header text " + (_title != null ? "found" : "not found"));
            return true;
        }

        private static void Loose(Button button, Vector2 pivot, float wide)
        {
            button.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)button.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(wide, High);
            rt.SetAsLastSibling();
        }

        private static void Loud(Button button, Text label, Color back, Color ink)
        {
            var image = button.GetComponent<Image>();
            if (image != null && image.color != back) image.color = back;
            if (label == null) return;
            if (label.color != ink) label.color = ink;
            label.resizeTextMaxSize = 19;
            label.fontSize = 19;
        }

        private static void Stand()
        {
            _content.GetWorldCorners(Corners);
            float scale = Mathf.Max(0.0001f, _content.lossyScale.x);
            float y = Corners[1].y + 27f * scale;
            if (_title != null)
            {
                var rt = _title.rectTransform;
                float mid = rt.TransformPoint(rt.rect.center).y;
                if (mid > Corners[1].y && mid - Corners[1].y < 90f * scale) y = mid;
            }
            Put((RectTransform)_flip.transform, new Vector3(Corners[1].x + Edge * scale, y, Corners[1].z));
            Put((RectTransform)_go.transform, new Vector3(Corners[2].x - Edge * scale, y, Corners[2].z));
        }

        private static void Put(RectTransform rt, Vector3 at)
        {
            if ((rt.position - at).sqrMagnitude > 0.0001f) rt.position = at;
        }

        private static void Paint()
        {
            Set(_flipLabel, _on ? Lang.T("Отменить выбор") : Lang.T("Выбрать несколько"));
            if (_on) Loud(_flip, _flipLabel, WardrobeLook.Danger, WardrobeLook.DangerText);
            else Loud(_flip, _flipLabel, WardrobeLook.Accent, WardrobeLook.OnAccent);
            if (_flip.interactable == _running) _flip.interactable = !_running;
            if (_go.gameObject.activeSelf != _on) _go.gameObject.SetActive(_on);
            if (!_on) return;
            string text = _running ? Lang.F("Стоп · {0} из {1}", _done, _total)
                : _toBox ? Lang.F("Положить выбранное: {0}", Picked.Count)
                : Lang.F("Забрать выбранное: {0}", Picked.Count);
            Set(_goLabel, text);
            bool can = _running || Picked.Count > 0;
            if (_go.interactable != can) _go.interactable = can;
        }

        private static void Set(Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        private static void Marks()
        {
            if (_toBox)
            {
                foreach (var cell in _window.GetComponentsInChildren<InventoryThingItemRenderer>(false))
                    Mark(cell.transform, _on && Has(cell));
                return;
            }
            foreach (var row in _window.GetComponentsInChildren<ShopThingItemRenderer>(false))
                Mark(row.transform, _on && Has(row));
        }

        private static bool Has(InventoryThingItemRenderer cell) => cell.Data != null && Picked.ContainsKey(cell.Data.InventoryId);

        private static bool Has(ShopThingItemRenderer row) => row.Data != null && Picked.ContainsKey(row.Data.Id);

        internal static bool Holds(Transform cell)
        {
            if (!_on || cell == null) return false;
            var one = cell.GetComponent<InventoryThingItemRenderer>();
            if (one != null) return Has(one);
            var row = cell.GetComponent<ShopThingItemRenderer>();
            return row != null && Has(row);
        }

        private static void Mark(Transform cell, bool on)
        {
            var had = cell.Find("QoLPick");
            if (!on)
            {
                if (had != null && had.gameObject.activeSelf) had.gameObject.SetActive(false);
                return;
            }
            if (had != null)
            {
                if (!had.gameObject.activeSelf) had.gameObject.SetActive(true);
                return;
            }
            var go = new GameObject("QoLPick", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(StoragePickMark));
            go.transform.SetParent(cell, false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rt = (RectTransform)go.transform;
            OnlineWindow.Place(rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.SetAsLastSibling();
            var fill = go.GetComponent<Image>();
            var tint = WardrobeLook.Good;
            tint.a = 0.3f;
            fill.color = tint;
            fill.raycastTarget = false;

            var badge = new GameObject("badge", typeof(RectTransform), typeof(Image));
            badge.transform.SetParent(rt, false);
            var brt = (RectTransform)badge.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(3f, -3f);
            brt.sizeDelta = new Vector2(22f, 22f);
            var plate = badge.GetComponent<Image>();
            plate.sprite = OnlineWindow.Rounded(8);
            plate.type = Image.Type.Sliced;
            plate.color = WardrobeLook.Good;
            plate.raycastTarget = false;
            var tick = OnlineWindow.Label(brt, "✓", 16, FontStyle.Bold, WardrobeLook.OnAccent);
            OnlineWindow.Place(tick.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            tick.alignment = TextAnchor.MiddleCenter;
            tick.raycastTarget = false;
        }

        private static void Flip()
        {
            if (_running) return;
            _on = !_on;
            if (!_on) Picked.Clear();
            _next = 0f;
            Plugin.Trace("[storage pick] picking " + (_on ? "on" : "off"));
        }

        private static void Go()
        {
            if (_running) { _stop = true; return; }
            if (Picked.Count == 0 || Plugin.Instance == null) return;
            if (Artifacts.Busy) { TravelLockPatch.Refuse(); return; }
            Plugin.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            bool toBox = _toBox;
            var list = new List<Pick>(Picked.Values);
            int moved = 0, failed = 0, row = 0;
            string why = "";
            _running = true;
            _stop = false;
            _done = 0;
            _total = list.Count;
            _next = 0f;
            Artifacts.Busy = true;
            Plugin.Trace("[storage pick] " + (toBox ? "putting " : "taking ") + list.Count + " stacks");
            try
            {
                foreach (var pick in list)
                {
                    if (_stop || row >= GiveUp) break;
                    yield return Plugin.Instance.StartCoroutine(Artifacts.Move(pick.Id, toBox, pick.Tab, pick.Qty));
                    _done++;
                    _next = 0f;
                    if (Artifacts.Moved)
                    {
                        moved++;
                        row = 0;
                        Picked.Remove(pick.Id);
                        continue;
                    }
                    failed++;
                    row++;
                    if (why.Length == 0) why = Artifacts.MoveError ?? "";
                }
            }
            finally
            {
                Artifacts.Busy = false;
                _running = false;
                if (Picked.Count == 0) _on = false;
                _next = 0f;
            }

            Plugin.Trace("[storage pick] done: moved " + moved + ", refused " + failed + ", left picked " + Picked.Count
                         + (why.Length > 0 ? ", first refusal: " + why : ""));
            string said = toBox ? Lang.F("Положил в хранилище: {0}", moved) : Lang.F("Забрал из хранилища: {0}", moved);
            if (failed > 0) said += Lang.F(", не вышло: {0}", failed) + (why.Length > 0 ? " — " + why : "");
            try
            {
                if (failed > 0) AirMessageScript.ShowInformationNotification(said);
                else AirMessageScript.ShowSuccessNotification(said);
            }
            catch { }

            if (_window != null)
            {
                int tab = Tab();
                if (tab > 0) Artifacts.Reload(tab, toBox);
            }
            Artifacts.RequestStorage();
        }
    }

    internal sealed class StoragePickMark : MonoBehaviour
    {
        private void LateUpdate() => Drop();

        private void OnEnable() => Canvas.willRenderCanvases += Drop;

        private void OnDisable() => Canvas.willRenderCanvases -= Drop;

        private void Drop()
        {
            if (this == null || !gameObject.activeSelf) return;
            if (!StoragePick.Holds(transform.parent)) gameObject.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(StorageGetThingController), "BuildWindow")]
    internal static class StoragePickGetPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => StoragePick.Opened(__result, false);
    }

    [HarmonyPatch(typeof(StoragePutThingController), "BuildWindow")]
    internal static class StoragePickPutPatch
    {
        private static void Postfix(BasePanelContentWindow __result) => StoragePick.Opened(__result, true);
    }

    [HarmonyPatch(typeof(HintResolverContext), nameof(HintResolverContext.ShowInventoryHint))]
    internal static class StoragePickClickPatch
    {
        private static bool Prefix(HintResolverContext __instance, int inventoryId, int? currentQuantity, int? tabId) =>
            !StoragePick.Catch(__instance, inventoryId, currentQuantity, tabId);
    }
}
