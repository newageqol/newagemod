using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class SearchList : FilteredListWrapper<InventoryThingTabContentDto>
    {
        internal SearchList(List<InventoryThingTabContentDto> all) : base(all ?? new List<InventoryThingTabContentDto>())
        {
            Filter = TabSearch.Rule;
            IsFiltered = TabSearch.Active;
        }
    }

    internal sealed class SearchRule : IItemFilter<InventoryThingTabContentDto>
    {
        public bool IsItemValid(InventoryThingTabContentDto item) => TabSearch.Fits(item);
    }

    internal static class TabSearch
    {
        private const float High = 40f;
        private const float Side = 16f;
        private const float Reserve = 170f;
        private const float Least = 160f;
        private const float WipeSide = 26f;

        private static readonly Color Back = new Color32(22, 40, 68, 245);
        private static readonly Color Rim = new Color32(196, 156, 78, 255);
        private static readonly Color Ink = new Color32(240, 230, 204, 255);
        private static readonly Color Hint = new Color32(150, 160, 176, 255);

        internal static readonly SearchRule Rule = new SearchRule();
        private static string _query = "";
        private static string _typed = "";
        private static float _applyAt = -1f;
        private static readonly Dictionary<string, string> Folded = new Dictionary<string, string>();

        private static StandardContentWindowPanel _panel;
        private static GameObject _go;
        private static RectTransform _rt;
        private static InputField _field;
        private static GameObject _wipe;

        internal static bool Active => _query.Length > 0;

        internal static bool Fits(InventoryThingTabContentDto item)
        {
            if (!Active) return true;
            if (item == null) return false;
            string name = Known(item);
            return name != null && Fold(name).Contains(_query);
        }

        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>();
        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static readonly Queue<int> Wanted = new Queue<int>();
        private static float _refilterAt = -1f;
        private const int AskPerTick = 15;

        private static string Known(InventoryThingTabContentDto item)
        {
            int id = item.ThingId;
            string name = item.Name;
            if (!string.IsNullOrEmpty(name)) { Names[id] = name; return name; }
            if (Names.TryGetValue(id, out name)) return name;
            try
            {
                var cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
                var about = cache != null ? cache.GetFromCache(id) : null;
                if (about != null && !string.IsNullOrEmpty(about.Name)) { Names[id] = about.Name; return about.Name; }
            }
            catch { }
            var known = RecipeData.Thing(id);
            if (known != null && !string.IsNullOrEmpty(known.Name)) { Names[id] = known.Name; return known.Name; }
            if (id > 0 && Asked.Add(id)) Wanted.Enqueue(id);
            return null;
        }

        private static void Ask()
        {
            if (Wanted.Count == 0) return;
            GeneralThingInfoDescriptionManager cache;
            try { cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>(); }
            catch { cache = null; }
            if (cache == null) return;
            for (int i = 0; i < AskPerTick && Wanted.Count > 0; i++)
            {
                int id = Wanted.Dequeue();
                try
                {
                    cache.Get(id, about =>
                    {
                        if (about == null || string.IsNullOrEmpty(about.Name)) return;
                        Names[about.ThingId] = about.Name;
                        if (_refilterAt < 0f) _refilterAt = Time.unscaledTime + 0.3f;
                    });
                }
                catch (Exception e) { Plugin.Trace("[tabsearch] name " + id + ": " + e.Message); }
            }
        }

        private static void Refilter()
        {
            if (_refilterAt < 0f || Time.unscaledTime < _refilterAt) return;
            _refilterAt = -1f;
            if (!Active || _panel == null) return;
            var grid = _panel.GetComponentInChildren<InventoryPanelContent>(true);
            var list = grid != null ? grid.TabContent as SearchList : null;
            if (list == null) return;
            list.IsFiltered = true;
            list.Refresh();
        }

        internal static void Bind(StandardContentWindowPanel panel)
        {
            if (panel == null || ReferenceEquals(panel, _panel)) return;
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_wipe != null) UnityEngine.Object.Destroy(_wipe);
            _go = null;
            _wipe = null;
            _field = null;
            _panel = panel;
            _query = "";
            _typed = "";
            _applyAt = -1f;
            _refilterAt = -1f;
        }

        private static string Fold(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string got;
            if (Folded.TryGetValue(name, out got)) return got;
            string own = name.ToLowerInvariant().Replace('ё', 'е');
            string shown = Lang.Game(name);
            if (!string.IsNullOrEmpty(shown) && shown != name) own += "\n" + shown.ToLowerInvariant().Replace('ё', 'е');
            if (Folded.Count > 4000) Folded.Clear();
            Folded[name] = own;
            return own;
        }

        internal static bool Ours(BaseGridContentPanel<InventoryThingTabContentDto> grid)
        {
            return grid is InventoryPanelContent && _panel != null && grid.transform.IsChildOf(_panel.transform);
        }

        internal static void Tick(StandardContentWindowPanel panel, RectTransform row)
        {
            if (panel == null || row == null) return;
            Bind(panel);
            if (_go == null) Build();
            if (_go == null) return;
            Place(row);
            bool typed = _field.text.Length > 0;
            if (_wipe != null && _wipe.activeSelf != typed) _wipe.SetActive(typed);
            if (_applyAt >= 0f && Time.unscaledTime >= _applyAt)
            {
                _applyAt = -1f;
                Apply(_typed);
            }
            Ask();
            Refilter();
        }

        private static void Build()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_wipe != null) UnityEngine.Object.Destroy(_wipe);
            _field = OnlineWindow.MakeInput(_panel.transform, 300f, Lang.T("Поиск по названию"));
            _go = _field.gameObject;
            _go.name = "QoLTabSearch";
            _go.GetComponent<LayoutElement>().ignoreLayout = true;
            _rt = (RectTransform)_go.transform;
            _rt.anchorMin = _rt.anchorMax = new Vector2(0f, 1f);
            _rt.pivot = new Vector2(0f, 0.5f);

            var back = _go.GetComponent<Image>();
            back.color = Back;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var rim = _go.GetComponent<Outline>();
            rim.effectColor = Rim;
            rim.effectDistance = new Vector2(1.5f, -1.5f);

            var font = SideButtons.GameFont();
            var ink = _field.textComponent;
            ink.fontSize = 18;
            ink.color = Ink;
            OnlineWindow.Place(ink.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(42f, 4f), new Vector2(-40f, -4f));
            var hint = _field.placeholder as Text;
            if (hint != null)
            {
                if (font != null) hint.font = font;
                hint.fontSize = 17;
                hint.color = Hint;
                hint.fontStyle = FontStyle.Normal;
                OnlineWindow.Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(42f, 4f), new Vector2(-40f, -4f));
            }
            _field.caretColor = Ink;
            _field.selectionColor = new Color(Rim.r, Rim.g, Rim.b, 0.35f);
            _field.characterLimit = 40;

            Loupe(_go.transform);
            _wipe = Wipe(_panel.transform);

            _field.text = _query;
            _typed = _query;
            _field.onValueChanged.AddListener(text =>
            {
                _typed = text ?? "";
                _applyAt = Time.unscaledTime + 0.2f;
            });
            Plugin.Trace("[tabsearch] field built");
        }

        private static void Loupe(Transform host)
        {
            var go = new GameObject("loupe", typeof(RectTransform));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.anchoredPosition = new Vector2(22f, 0f);

            var ring = new GameObject("ring", typeof(RectTransform), typeof(Image));
            ring.transform.SetParent(go.transform, false);
            var pic = ring.GetComponent<Image>();
            pic.sprite = OnlineWindow.Ring();
            pic.color = Rim;
            pic.raycastTarget = false;
            var rrt = (RectTransform)ring.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
            rrt.sizeDelta = new Vector2(15f, 15f);
            rrt.anchoredPosition = new Vector2(-2f, 2f);

            var stick = new GameObject("stick", typeof(RectTransform), typeof(Image));
            stick.transform.SetParent(go.transform, false);
            var bar = stick.GetComponent<Image>();
            bar.color = Rim;
            bar.raycastTarget = false;
            var srt = (RectTransform)stick.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(8f, 3f);
            srt.anchoredPosition = new Vector2(6.5f, -6.5f);
            srt.localRotation = Quaternion.Euler(0f, 0f, -45f);
        }

        private static GameObject Wipe(Transform host)
        {
            var go = new GameObject("wipe", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(WipeSide, WipeSide);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Disc();
            back.color = new Color32(70, 88, 116, 255);

            Bar(go.transform, 45f);
            Bar(go.transform, -45f);

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() =>
            {
                if (_field != null)
                {
                    _field.DeactivateInputField();
                    _field.text = "";
                }
                _typed = "";
                _applyAt = -1f;
                Apply("");
            });
            go.SetActive(_query.Length > 0);
            return go;
        }

        private static void Bar(Transform host, float turn)
        {
            var go = new GameObject("bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var pic = go.GetComponent<Image>();
            pic.color = Ink;
            pic.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(14f, 2.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, turn);
        }

        private static void Place(RectTransform row)
        {
            var panel = (RectTransform)_panel.transform;
            var ends = new Vector3[4];
            row.GetWorldCorners(ends);
            float top = panel.InverseTransformPoint(ends[1]).y - panel.rect.yMax;
            float bottom = panel.InverseTransformPoint(ends[0]).y - panel.rect.yMax;
            float wide = Mathf.Max(Least, panel.rect.width - Side - Reserve);
            var size = new Vector2(wide, High);
            if (_rt.sizeDelta != size) _rt.sizeDelta = size;
            var at = new Vector2(Side, (top + bottom) * 0.5f);
            if (_rt.anchoredPosition != at) _rt.anchoredPosition = at;
            if (_wipe == null) return;
            var wrt = (RectTransform)_wipe.transform;
            var spot = new Vector2(Side + wide - 8f - WipeSide * 0.5f, at.y);
            if (wrt.anchoredPosition != spot) wrt.anchoredPosition = spot;
        }

        private static void Apply(string text)
        {
            string want = (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
            if (want == _query) return;
            _query = want;
            Plugin.Trace("[tabsearch] query " + (want.Length == 0 ? "cleared" : "\"" + want + "\""));
            if (_panel == null) return;
            var grid = _panel.GetComponentInChildren<InventoryPanelContent>(true);
            var list = grid != null ? grid.TabContent as SearchList : null;
            if (list == null) return;
            list.IsFiltered = Active;
            grid.TabContent = list;
        }
    }

    [HarmonyPatch(typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>), "SetTabContent")]
    public static class TabSearchListPatch
    {
        private static readonly AccessTools.FieldRef<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, BaseGridContentPanel<InventoryThingTabContentDto>> GridRef =
            AccessTools.FieldRefAccess<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, BaseGridContentPanel<InventoryThingTabContentDto>>("_panelContent");

        private static void Postfix(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance)
        {
            try
            {
                var grid = GridRef(__instance);
                if (grid == null || !TabSearch.Ours(grid)) return;
                var now = grid.TabContent;
                if (now == null || now is FilteredListWrapper<InventoryThingTabContentDto>) return;
                grid.TabContent = new SearchList(now.Content);
            }
            catch (Exception e) { Plugin.Fault("[tabsearch] list: " + e.Message); }
        }
    }

    [HarmonyPatch]
    public static class TabSearchUpdatePatch
    {
        private static readonly AccessTools.FieldRef<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, BaseGridContentPanel<InventoryThingTabContentDto>> GridRef =
            AccessTools.FieldRefAccess<BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>, BaseGridContentPanel<InventoryThingTabContentDto>>("_panelContent");

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var type = typeof(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs>);
            yield return AccessTools.Method(type, "ChangeTabContentCountableItem");
            yield return AccessTools.Method(type, "RemoveItem");
        }

        private static SearchList List(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> resolver)
        {
            var grid = GridRef(resolver);
            return grid != null ? grid.TabContent as SearchList : null;
        }

        private static void Prefix(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance, out bool __state)
        {
            __state = false;
            try
            {
                var list = List(__instance);
                if (list == null || !list.IsFiltered) return;
                list.IsFiltered = false;
                __state = true;
            }
            catch (Exception e) { Plugin.Fault("[tabsearch] update: " + e.Message); }
        }

        private static void Finalizer(BaseThingTabPanelResolver<InventoryThingTabContentDto, UserMenuController.ETabs> __instance, bool __state)
        {
            if (!__state) return;
            try
            {
                var list = List(__instance);
                if (list == null) return;
                list.IsFiltered = TabSearch.Active;
                list.Refresh();
            }
            catch (Exception e) { Plugin.Fault("[tabsearch] update: " + e.Message); }
        }
    }
}
