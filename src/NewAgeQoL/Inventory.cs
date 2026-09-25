using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class RecipeIcons
    {
        private const int RecipeAlchemy = 19, RecipeTanner = 20, RecipeBlacksmith = 21;

        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Queue<int> Waiting = new Queue<int>();
        private static readonly List<InventoryThingItemRenderer> Live = new List<InventoryThingItemRenderer>();

        private static bool _listener, _draining;


        internal static bool IsRecipe(int subType) =>
            subType == RecipeAlchemy || subType == RecipeTanner || subType == RecipeBlacksmith;

        internal static int CurrentTab = -1;

        private static int[] _tabs;
        private static string _tabsRaw;

        internal static bool SwapHere()
        {
            string raw = Plugin.CfgRecipeIconTabs != null ? Plugin.CfgRecipeIconTabs.Value : "5,15,17,22";
            if (_tabs == null || raw != _tabsRaw)
            {
                _tabsRaw = raw;
                _tabs = raw.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => int.TryParse(x.Trim(), out int v) ? v : -1).Where(v => v > 0).ToArray();
            }
            return Array.IndexOf(_tabs, CurrentTab) >= 0;
        }

        internal static string ResultImage(int recipeThingId)
        {
            var image = Store.Image(recipeThingId);
            if (image != null) return image;
            lock (Asked)
            {
                if (Asked.Contains(recipeThingId)) return null;
                Asked.Add(recipeThingId);
                Waiting.Enqueue(recipeThingId);
            }
            Drain();
            return null;
        }

        internal static void Sync(InventoryThingItemRenderer r)
        {
            if (r == null) return;
            var d = SwapHere() ? r.Data : null;
            bool on = d != null && IsRecipe((int)d.SubType);
            Mark(r, on, on ? d.Image : null);
            if (!on) { Shrink(r, false); return; }
            Redraw(r);
        }

        private static MethodInfo _updateImage;

        private static void Redraw(InventoryThingItemRenderer r)
        {
            try
            {
                if (_updateImage == null) _updateImage = AccessTools.Method(typeof(InventoryThingItemRenderer), "UpdateImage");
                _updateImage?.Invoke(r, null);
            }
            catch { }
        }

        private static readonly AccessTools.FieldRef<InventoryThingItemRenderer, Image> ThingImageRef =
            AccessTools.FieldRefAccess<InventoryThingItemRenderer, Image>("ThingImage");

        internal static void Shrink(InventoryThingItemRenderer r, bool on)
        {
            try
            {
                var img = ThingImageRef(r);
                if (img == null) return;
                float scale = on ? 0.78f : 1f;
                var t = img.transform;
                if (Mathf.Abs(t.localScale.x - scale) > 0.01f) t.localScale = new Vector3(scale, scale, 1f);
            }
            catch { }
        }

        internal static void Track(InventoryThingItemRenderer r)
        {
            if (r != null && !Live.Contains(r)) Live.Add(r);
        }

        internal static void Forget(InventoryThingItemRenderer r) => Live.Remove(r);

        private static void Drain()
        {
            if (_draining || Plugin.Instance == null) return;
            _draining = true;
            Plugin.Instance.StartCoroutine(DrainRoutine());
        }

        private static IEnumerator DrainRoutine()
        {
            try
            {
                while (true)
                {
                    var batch = new List<int>();
                    lock (Asked)
                    {
                        while (Waiting.Count > 0 && batch.Count < 8) batch.Add(Waiting.Dequeue());
                    }
                    if (batch.Count == 0) yield break;
                    if (!EnsureListener()) yield break;
                    foreach (int id in batch)
                    {
                        try { NetworkConnection.Instance.SendRequest(new GeneralThingHintRequest(id)); }
                        catch { yield break; }
                    }
                    float t = 0f;
                    while (t < 0.05f) { yield return null; t += Time.unscaledDeltaTime; }
                }
            }
            finally { _draining = false; }
        }

        private static bool EnsureListener()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return false;
                if (_listener) return true;
                nc.AddMessageListener(388, OnRecipeInfo);
                _listener = true;
                return true;
            }
            catch { return false; }
        }

        private static void OnRecipeInfo(object msg)
        {
            var rec = msg as GeneralPrescriptionInfoMessage;
            if (rec == null || rec.ThingId <= 0 || string.IsNullOrEmpty(rec.ResultThingImage)) return;
            if (Store.Image(rec.ThingId) != null) return;
            Store.SetImage(rec.ThingId, rec.ResultThingImage);
            RefreshCells(rec.ThingId);
        }

        private static void RefreshCells(int recipeThingId)
        {
            if (_updateImage == null) _updateImage = AccessTools.Method(typeof(InventoryThingItemRenderer), "UpdateImage");
            var update = _updateImage;
            if (update == null) return;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var r = Live[i];
                if (r == null) { Live.RemoveAt(i); continue; }
                try
                {
                    var d = r.Data;
                    if (d != null && d.ThingId == recipeThingId) update.Invoke(r, null);
                }
                catch { }
            }
        }

        private const string MarkName = "MvlRecipeMark";

        private static void Mark(InventoryThingItemRenderer r, bool on, string recipeImage)
        {
            try
            {
                var t = r.transform.Find(MarkName);
                if (t == null)
                {
                    if (!on) return;
                    t = BuildMark(r);
                    if (t == null) return;
                }

                if (on)
                {
                    var sprite = string.IsNullOrEmpty(recipeImage) ? null : AtlasUtils.GetThingSprite(recipeImage);
                    var img = t.GetComponent<Image>();
                    if (img != null) { img.sprite = sprite; img.enabled = sprite != null; }
                    var fallback = t.Find("txt");
                    if (fallback != null) fallback.gameObject.SetActive(sprite == null);
                }
                if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            }
            catch { }
        }

        private static Transform BuildMark(InventoryThingItemRenderer r)
        {
            var cell = r.transform as RectTransform;
            if (cell == null) return null;

            var go = new GameObject(MarkName, typeof(RectTransform));
            go.transform.SetParent(cell, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(7f, -7f);
            float side = Mathf.Max(10f, cell.rect.width * 0.26f);
            rt.sizeDelta = new Vector2(side, side);
            rt.localScale = Vector3.one;

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.enabled = false;

            var countText = AccessTools.Field(typeof(InventoryThingItemRenderer), "CountText")?.GetValue(r) as Text;
            var font = countText != null ? countText.font : null;
            if (font != null)
            {
                var sub = new GameObject("txt", typeof(RectTransform));
                sub.transform.SetParent(rt, worldPositionStays: false);
                var srt = (RectTransform)sub.transform;
                srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
                srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
                srt.localScale = Vector3.one;
                var txt = sub.AddComponent<Text>();
                txt.font = font;
                txt.fontSize = countText != null ? Mathf.Max(10, countText.fontSize - 2) : 12;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.UpperLeft;
                txt.color = new Color(1f, 0.87f, 0.25f);
                txt.raycastTarget = false;
                txt.text = "Р";
                var outline = sub.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                sub.SetActive(false);
            }

            if (countText != null && countText.transform.parent == cell)
                rt.SetSiblingIndex(countText.transform.GetSiblingIndex());

            return rt;
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "UpdateImage")]
    public static class RecipeIconPatch
    {
        private static readonly AccessTools.FieldRef<InventoryThingItemRenderer, SingleImageLoader> LoaderRef =
            AccessTools.FieldRefAccess<InventoryThingItemRenderer, SingleImageLoader>("_singleImageLoader");

        private static bool Prefix(InventoryThingItemRenderer __instance)
        {
            try
            {
                var data = __instance.Data;
                if (data == null || !RecipeIcons.SwapHere()) { RecipeIcons.Shrink(__instance, false); return true; }
                if (!RecipeIcons.IsRecipe((int)data.SubType)) { RecipeIcons.Shrink(__instance, false); return true; }

                string img = RecipeIcons.ResultImage(data.ThingId);
                if (string.IsNullOrEmpty(img)) return true;

                var loader = LoaderRef(__instance);
                if (loader == null) return true;
                RecipeIcons.Shrink(__instance, true);
                loader.LoadThingImage(img, null);
                return false;
            }
            catch (Exception e) { Plugin.Fault("[recipes] UpdateImage: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "Data", MethodType.Setter)]
    public static class CellDataSyncPatch
    {
        private static void Postfix(InventoryThingItemRenderer __instance)
        {
            RecipeIcons.Sync(__instance);
            ContractNumbers.Sync(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "Awake")]
    public static class CellAwakePatch
    {
        private static void Postfix(InventoryThingItemRenderer __instance)
        {
            RecipeIcons.Track(__instance);
            ContractNumbers.Track(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryThingItemRenderer), "OnDestroy")]
    public static class CellDestroyPatch
    {
        private static void Prefix(InventoryThingItemRenderer __instance)
        {
            RecipeIcons.Forget(__instance);
            ContractNumbers.Forget(__instance);
        }
    }

    internal static class Contracts
    {
        internal const int AllTab = 18;

        internal static int TabId => Plugin.CfgContractsTabId != null ? Plugin.CfgContractsTabId.Value : 40;
        internal static int IconTabId => Plugin.CfgContractsIcon != null ? Plugin.CfgContractsIcon.Value : 16;

        internal static bool IsContract(ThingItemMessage t) => t != null && IsContract(t.ThingId);

        internal static bool IsContract(int thingId) => thingId >= 4000 && thingId <= 4999;

        internal static void LearnIcon(ThingItemMessage t)
        {
            try
            {
                if (Plugin.CfgContractsIconImage == null || string.IsNullOrEmpty(t?.Image)) return;
                if (!string.IsNullOrEmpty(Plugin.CfgContractsIconImage.Value)) return;
                Plugin.CfgContractsIconImage.Value = t.Image;
                Plugin.Trace("[contracts] картинка вкладки: «" + t.Image + "» (по контракту " + t.ThingId + " «" + t.Name + "»)");
            }
            catch { }
        }

        private static Sprite _remote;
        private static string _remoteFor;
        private static Sprite _served;
        private static string _askedFor;
        private static int _seat = -1;

        internal static void Remember(Sprite icon)
        {
            _served = icon;
        }

        internal static void Seat(int index)
        {
            _seat = index;
        }

        internal static void Refresh()
        {
            if (_seat < 0) return;
            string img = Plugin.CfgContractsIconImage != null ? Plugin.CfgContractsIconImage.Value : null;
            bool named = img != _askedFor;
            if (!named && (_remote == null || _remote == _served)) return;
            _askedFor = img;
            var fresh = Icon();
            if (fresh == null || fresh == _served) return;
            _served = fresh;
            try
            {
                foreach (var tab in UnityEngine.Object.FindObjectsOfType<TabButton>())
                {
                    if (tab == null || tab.Index != _seat) continue;
                    tab.UpdateView(_seat, fresh);
                    Plugin.Trace("[contracts] картинка вкладки доехала, значок обновлён на месте");
                }
            }
            catch (Exception e) { Plugin.Trace("[contracts] обновить значок вкладки: " + e.Message); }
        }

        internal static void Wake()
        {
            _seat = -1;
            _served = null;
            _askedFor = null;
            Icon();
        }

        internal static Sprite Icon()
        {
            try
            {
                string img = Plugin.CfgContractsIconImage != null ? Plugin.CfgContractsIconImage.Value : null;
                if (string.IsNullOrEmpty(img)) return null;

                var s = AtlasUtils.GetThingSprite(img);
                if (s != null && s.name != "unknown") return s;
                if (_remote != null && _remoteFor == img) return _remote;

                if (_remoteFor != img)
                {
                    _remoteFor = img;
                    RemoteImageLoader.Instance.Load(
                        "https://files.nura.biz/site/images/things100x100/" + img + ".png",
                        sprite => _remote = sprite,
                        error => Plugin.Warn("[contracts] картинка вкладки не загрузилась: " + error));
                }
            }
            catch { }
            return null;
        }
    }

    internal static class ContractNumbers
    {
        private const string BadgeName = "QoLContractNumber";

        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>();

        private static bool _loaded, _dirty;
        private static float _saveAt = -1f;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string raw = Plugin.CfgContractCache != null ? Plugin.CfgContractCache.Value : "";
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var piece in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = piece.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(piece.Substring(0, colon).Trim(), out int thingId)) continue;
                string number = piece.Substring(colon + 1).Trim();
                if (number.Length > 0) lock (Known) Known[thingId] = number;
            }
        }

        private static void Save()
        {
            try
            {
                if (Plugin.CfgContractCache == null) return;
                var text = new System.Text.StringBuilder();
                lock (Known)
                    foreach (var pair in Known)
                    {
                        if (text.Length > 0) text.Append(',');
                        text.Append(pair.Key).Append(':').Append(pair.Value);
                    }
                Plugin.CfgContractCache.Value = text.ToString();
            }
            catch { }
        }
        private static readonly List<InventoryThingItemRenderer> Live = new List<InventoryThingItemRenderer>();


        internal static string Parse(int thingId, string name)
        {
            if (!Contracts.IsContract(thingId) || string.IsNullOrEmpty(name)) return null;
            int i = 0;
            while (i < name.Length && (name[i] < '0' || name[i] > '9')) i++;
            int from = i;
            while (i < name.Length && name[i] >= '0' && name[i] <= '9') i++;
            return i > from ? name.Substring(from, i - from) : null;
        }

        internal static void Ask(int thingId)
        {
            Load();
            lock (Known)
                if (Known.ContainsKey(thingId)) return;
            Store.AskNow(thingId);
        }

        internal static int Order(int thingId, string name)
        {
            Load();
            string text = string.IsNullOrEmpty(name) ? Store.Text(thingId) : name;
            string number = Parse(thingId, text);
            if (number == null)
                lock (Known) Known.TryGetValue(thingId, out number);
            return number != null && int.TryParse(number, out int value) ? value : int.MaxValue;
        }

        internal static void Learn(int thingId, string name)
        {
            if (thingId <= 0) return;
            Load();
            string number = Parse(thingId, name);
            if (number == null) return;
            bool fresh;
            lock (Known)
            {
                fresh = !Known.TryGetValue(thingId, out var was) || was != number;
                Known[thingId] = number;
            }
            if (!fresh) return;
            Plugin.Trace("[contracts] номер " + number + " у предмета " + thingId);
            Refresh(thingId);
            _restackAt = Time.unscaledTime + 0.4f;
            _dirty = true;
            _saveAt = Time.unscaledTime + 2f;
        }

        internal static void Track(InventoryThingItemRenderer r)
        {
            if (r != null && !Live.Contains(r)) Live.Add(r);
        }

        internal static void Forget(InventoryThingItemRenderer r) => Live.Remove(r);

        private static float _restackAt = -1f;
        private static float _iconAt;

        internal static void Tick()
        {
            if (Time.unscaledTime >= _iconAt)
            {
                _iconAt = Time.unscaledTime + 0.5f;
                Contracts.Refresh();
            }
            if (_dirty && _saveAt >= 0f && Time.unscaledTime >= _saveAt)
            {
                _dirty = false; _saveAt = -1f;
                Save();
            }
            if (_restackAt < 0f || Time.unscaledTime < _restackAt) return;
            _restackAt = -1f;
            if (RecipeIcons.CurrentTab != Contracts.TabId) return;
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) return;
                conn.SendRequest(new GetTabContentRequest(Contracts.AllTab, (int)EThingContextWindow.WINDOW_INVENTORY));
                Plugin.Trace("[contracts] перестраиваю вкладку — узнали новые номера");
            }
            catch { }
        }

        internal static void SyncAll()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i] == null) { Live.RemoveAt(i); continue; }
                Sync(Live[i]);
            }
        }

        internal static void Sync(InventoryThingItemRenderer r)
        {
            if (r == null) return;
            try { Draw(r, Number(r.Data)); }
            catch (Exception e) { Plugin.Fault("[contracts] номер на клетке: " + e.Message); }
        }

        private static string Number(InventoryThingTabContentDto d)
        {
            if (d == null) return null;
            string name = string.IsNullOrEmpty(d.Name) ? Store.Text(d.ThingId) : d.Name;

            string number = Parse(d.ThingId, name);
            if (number != null)
            {
                lock (Known) Known[d.ThingId] = number;
                return number;
            }
            lock (Known)
                if (Known.TryGetValue(d.ThingId, out var known)) return known;

            if (Contracts.IsContract(d.ThingId)) Store.AskNow(d.ThingId);
            return null;
        }

        private static void Refresh(int thingId)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var r = Live[i];
                if (r == null) { Live.RemoveAt(i); continue; }
                var d = r.Data;
                if (d != null && d.ThingId == thingId) Sync(r);
            }
        }

        private static void Draw(InventoryThingItemRenderer r, string number)
        {
            var badge = r.transform.Find(BadgeName);
            if (badge == null)
            {
                if (number == null) return;
                badge = Build(r);
                if (badge == null) return;
            }

            if (number != null)
            {
                var txt = badge.GetComponent<Text>();
                if (txt != null && txt.text != number) txt.text = number;

                if (badge.parent != null && badge.GetSiblingIndex() != badge.parent.childCount - 1)
                    badge.SetAsLastSibling();
            }
            if (badge.gameObject.activeSelf != (number != null)) badge.gameObject.SetActive(number != null);
        }

        private static Transform Build(InventoryThingItemRenderer r)
        {
            var cell = r.transform as RectTransform;
            if (cell == null) return null;

            var countText = AccessTools.Field(typeof(InventoryThingItemRenderer), "CountText")?.GetValue(r) as Text;
            var font = countText != null ? countText.font : null;
            if (font == null) return null;

            int size = cell.rect.height > 1f
                     ? Mathf.Clamp(Mathf.RoundToInt(cell.rect.height * 0.3f), 11, 22)
                     : Mathf.Max(11, countText.fontSize);

            var go = new GameObject(BadgeName, typeof(RectTransform));
            go.layer = cell.gameObject.layer;
            go.transform.SetParent(cell, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(3f, 3f);
            rt.offsetMax = new Vector2(-3f, -3f);
            rt.localScale = Vector3.one;

            var txt = go.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = size;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.color = new Color(1f, 0.93f, 0.55f);
            txt.raycastTarget = false;
            txt.text = "";

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            rt.SetAsLastSibling();
            return rt;
        }
    }

    [HarmonyPatch(typeof(InventoryThingTabContentDto), "CompareTo")]
    public static class ContractOrderPatch
    {
        private static bool Prefix(InventoryThingTabContentDto __instance, InventoryThingTabContentDto other, ref int __result)
        {
            try
            {
                if (other == null) return true;
                int mine = ContractNumbers.Order(__instance.ThingId, __instance.Name);
                int theirs = ContractNumbers.Order(other.ThingId, other.Name);
                if (mine == int.MaxValue || theirs == int.MaxValue) return true;

                __result = mine != theirs ? mine.CompareTo(theirs)
                                          : __instance.ThingId.CompareTo(other.ThingId);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnThingTypeTabsResponse")]
    public static class ContractsTabListPatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                var tabs = msg as ThingTypeTabsResponseMessage;
                if (tabs?.TabIds == null || tabs.TabIds.Count == 0) return;
                if (tabs.TabIds.Contains(Contracts.TabId)) { Contracts.Seat(tabs.TabIds.IndexOf(Contracts.TabId)); return; }
                tabs.TabIds.Insert(Math.Max(0, tabs.TabIds.Count - 1), Contracts.TabId);
                Contracts.Seat(tabs.TabIds.IndexOf(Contracts.TabId));
            }
            catch (Exception e) { Plugin.Fault("[contracts] список вкладок: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "GetTabCaption")]
    public static class ContractsTabCaptionPatch
    {
        private static bool Prefix(int tabId, ref string __result)
        {
            if (tabId != Contracts.TabId) return true;
            __result = "Контракты";
            return false;
        }
    }

    [HarmonyPatch(typeof(AtlasUtils), "GetThingTabImage")]
    public static class ContractsTabIconPatch
    {
        private static bool Prefix(EThingTabType tabType, ref Sprite __result)
        {
            if ((int)tabType != Contracts.TabId) return true;
            var own = Contracts.Icon();
            if (own != null) { __result = own; Contracts.Remember(own); return false; }
            if (Contracts.IconTabId == Contracts.TabId) return true;
            __result = AtlasUtils.GetThingTabImage((EThingTabType)Contracts.IconTabId);
            Contracts.Remember(__result);
            return false;
        }
    }


    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "OnTabSelected")]
    public static class ContractsTabSelectedPatch
    {
        private static readonly AccessTools.FieldRef<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, int> CurrentTabRef =
            AccessTools.FieldRefAccess<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, int>("_currentTab");

        private static bool Prefix(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance, TabView tab)
        {
            try
            {
                if (tab != null) RecipeIcons.CurrentTab = tab.Id;
                if (tab == null || tab.Id != Contracts.TabId) return true;
                CurrentTabRef(__instance) = tab.Id;
                NetworkConnection.Instance.SendRequest(
                    new GetTabContentRequest(Contracts.AllTab, (int)EThingContextWindow.WINDOW_INVENTORY));
                return false;
            }
            catch (Exception e) { Plugin.Fault("[contracts] выбор вкладки: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs>), "OnReceiveTabContent")]
    public static class ContractsTabContentPatch
    {
        private static void Prefix(BaseInventoryThingTabPanelResolver<UserMenuController.ETabs> __instance, object msg)
        {
            try
            {
                var content = msg as ThingTabInventoryResponseMessage;
                if (content?.Things == null) return;
                if (content.WindowId != (int)EThingContextWindow.WINDOW_INVENTORY) return;

                RecipeIcons.CurrentTab = __instance.CurrentTab;
                SlotSwap.LearnTabs(content);
                foreach (var t in content.Things)
                {
                    if (!Contracts.IsContract(t)) continue;
                    Contracts.LearnIcon(t);
                    ContractNumbers.Learn(t.ThingId, t.Name);
                    ContractNumbers.Ask(t.ThingId);
                }

                if (__instance.CurrentTab == Contracts.TabId && content.TabNumber == Contracts.AllTab)
                {
                    var mine = content.Things.Where(Contracts.IsContract).ToList();
                    content.Things = mine.OrderBy(t => ContractNumbers.Order(t.ThingId, t.Name))
                                         .ThenBy(t => t.ThingId)
                                         .ToList();
                    content.TabNumber = Contracts.TabId;
                    Plugin.Trace("[contracts] вкладка: " + mine.Count + " шт.");
                    return;
                }
                if (content.TabNumber == Contracts.AllTab || content.TabNumber == Contracts.TabId) return;
                content.Things = content.Things.Where(t => !Contracts.IsContract(t)).ToList();
                if (content.TabNumber == (int)EThingTabType.EQUIPMENT_TAB)
                    content.Things = content.Things.OrderByDescending(t => t.Level)
                                                     .ThenBy(t => t.SubType == (int)EThingSubType.RELIQUIAE || t.SubType == (int)EThingSubType.EARING ? 1 : 0)
                                                     .ToList();
            }
            catch (Exception e) { Plugin.Fault("[contracts] содержимое вкладки: " + e.Message); }
        }
    }
}
