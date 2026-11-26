using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class CraftCalc
    {
        private const float PanelW = 1360f;
        private const float PanelH = 820f;
        private const float Top = 60f;
        private const float BodyH = PanelH - Top - 16f;
        private const float ListW = 360f;
        private const float MidX = 16f + ListW + 12f;
        private const float MidW = 470f;
        private const float CartX = MidX + MidW + 12f;
        private const float CartW = PanelW - CartX - 16f;
        private const float RowH = 50f;
        private const float Indent = 22f;
        private const int Batch = 40;
        private const float Ahead = 400f;
        private const int Deepest = 12;

        private sealed class Pic
        {
            internal Image Image;
            internal string Name;
        }

        private sealed class Line
        {
            internal int Id;
            internal int Count;
        }

        private const int MostBins = 10;

        private sealed class Bin
        {
            internal readonly List<Line> Lines = new List<Line>();
        }

        private static GameObject _canvasGo;
        private static RectTransform _items;
        private static ScrollRect _itemsScroll;
        private static GameObject _toTop;
        private static RectTransform _head;
        private static RectTransform _tree;
        private static ScrollRect _treeScroll;
        private static RectTransform _cart;
        private static RectTransform _drops;
        private static RectTransform _cartView;
        private static RectTransform _dropHead;
        private static RectTransform _dropView;
        private static Text _found;
        private static Text _craftText;
        private static InputField _fromField;
        private static InputField _toField;
        private static InputField _skillFromField;
        private static InputField _skillToField;
        private static InputField _search;
        private static InputField _qtyField;
        private static readonly List<Pic> Pics = new List<Pic>();
        private static readonly Dictionary<int, Image> ItemBacks = new Dictionary<int, Image>();
        private static readonly List<int> Things = new List<int>();
        private static readonly List<int> Listed = new List<int>();
        private static int _drawn;

        private static readonly List<Bin> Bins = new List<Bin> { new Bin() };
        private static int _bin;
        private static List<Line> Cart => Bins[_bin].Lines;
        private static RectTransform _bins;
        private static Text _cartTitle;
        private static RectTransform _clear;
        private static readonly Dictionary<int, int> Variants = new Dictionary<int, int>();
        private static readonly HashSet<string> Open = new HashSet<string>();
        private static readonly Dictionary<int, int> Wanted = new Dictionary<int, int>();
        private static bool _loaded;
        private static string _saved;
        private static Text _dropTitle;

        private static int _craft;
        private static int _from;
        private static int _to;
        private static int _skillFrom;
        private static int _skillTo;
        private static int _picked;
        private static int _qty = 1;
        private static string _query = "";
        private static string _typed = "";
        private static float _filterAt;
        private static int _hoverId;
        private static float _hoverAt;
        private static int _tipFor;
        private static GameObject _tip;
        private static Text _tipText;
        private static Text _note;
        private static int _seenData;
        private static float _iconsAt;

        internal static void Show()
        {
            try
            {
                if (!RecipeData.Ready)
                {
                    Notice.Show(global::NewAgeQoL.Lang.T("Калькулятор крафта: в сборке нет рецептов"), 6f);
                    return;
                }
                RecipeData.Fetch();
                Load();
                if (_seenData != RecipeData.Version)
                {
                    Things.Clear();
                    Variants.Clear();
                    Drop();
                }
                _seenData = RecipeData.Version;
                Restore();
                Collect();
                _query = "";
                _typed = "";
                if (_picked == 0 || RecipeData.Making(_picked) == null) _picked = Things.Count > 0 ? Things[0] : 0;
                Build();
                Fill();
                Pick(_picked);
                Recount();
                Flasks.RequestScan();
                Artifacts.RequestStorage();
                Plugin.Trace("[craft] window opened: items " + Things.Count + ", in cart " + Cart.Count + ", at storage " + Artifacts.NearStorage());
            }
            catch (Exception e) { Plugin.Fault("[craft] window: " + e); Close(); }
        }

        internal static bool Has(int thing) => thing > 0 && RecipeData.Ready && RecipeData.Making(thing) != null;

        internal static int Crafted(int thing, string name)
        {
            if (Has(thing)) return thing;
            if (string.IsNullOrEmpty(name) || !RecipeData.Ready) return 0;
            foreach (var recipe in RecipeData.Recipes)
            {
                string made = RecipeData.ThingName(recipe.Result);
                if (string.Equals(made, name, StringComparison.CurrentCultureIgnoreCase)
                    || string.Equals(global::NewAgeQoL.Lang.Game(made), name, StringComparison.CurrentCultureIgnoreCase)) return recipe.Result;
            }
            return 0;
        }

        internal static void ShowRecipe(int thing)
        {
            if (!Has(thing)) return;
            _craft = 0;
            _from = 0;
            _to = 0;
            _skillFrom = 0;
            _skillTo = 0;
            _picked = thing;
            Show();
            if (_canvasGo == null) return;
            Pick(thing);
            string name = RecipeData.ThingName(thing);
            if (_search != null && !string.IsNullOrEmpty(name)) _search.text = global::NewAgeQoL.Lang.Game(name);
            Plugin.Trace("[craft] opened on recipe of " + name + " (" + thing + ")");
        }

        internal static bool IsOpen => _canvasGo != null;

        internal static void Under(bool low)
        {
            if (_canvasGo == null) return;
            var canvas = _canvasGo.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = low ? 440 : 831;
            _hoverId = 0;
            _hoverNote = null;
            if (_tip != null) _tip.SetActive(false);
        }

        internal static void Close()
        {
            MarketLook.Drop();
            _hoverId = 0;
            _hoverNote = null;
            _tipFor = 0;
            _tip = null;
            _tipText = null;
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _items = null;
            _itemsScroll = null;
            _toTop = null;
            _head = null;
            _tree = null;
            _treeScroll = null;
            _cart = null;
            _bins = null;
            _cartTitle = null;
            _clear = null;
            _drops = null;
            _cartView = null;
            _dropHead = null;
            _dropTitle = null;
            _dropView = null;
            _found = null;
            _craftText = null;
            _fromField = null;
            _toField = null;
            _skillFromField = null;
            _skillToField = null;
            _search = null;
            _qtyField = null;
            Pics.Clear();
            ItemBacks.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            if (MarketLook.Open) return false;
            Close();
            return true;
        }

        internal static void Tick()
        {
            if (_canvasGo == null) return;
            if (_seenData != RecipeData.Version) Reload();
            if (_filterAt > 0f && Time.unscaledTime >= _filterAt)
            {
                _filterAt = 0f;
                string query = (_typed ?? "").Trim().ToLowerInvariant();
                int from = Typed(_fromField);
                int to = Typed(_toField);
                int skillFrom = Typed(_skillFromField);
                int skillTo = Typed(_skillToField);
                if (query != _query || from != _from || to != _to || skillFrom != _skillFrom || skillTo != _skillTo)
                {
                    _query = query;
                    _from = from;
                    _to = to;
                    _skillFrom = skillFrom;
                    _skillTo = skillTo;
                    Refilter();
                }
            }
            More();
            Tip();
            Owned();
            if (_toTop != null && _items != null)
            {
                bool far = _items.anchoredPosition.y > RowH * 3f;
                if (_toTop.activeSelf != far) _toTop.SetActive(far);
            }
            if (Time.unscaledTime < _iconsAt) return;
            _iconsAt = Time.unscaledTime + 0.4f;
            Icons();
        }

        private static void Reload()
        {
            _seenData = RecipeData.Version;
            Things.Clear();
            Collect();
            if (Things.Count == 0) { Close(); return; }
            if (RecipeData.Making(_picked) == null) _picked = Things[0];
            Drop();
            Variants.Clear();
            Restore();
            Open.Clear();
            Fill();
            Pick(_picked);
            Recount();
            Plugin.Trace("[craft] recipes updated, window redrawn");
        }

        private static void Collect()
        {
            if (Things.Count > 0) return;
            var seen = new HashSet<int>();
            foreach (var recipe in RecipeData.Recipes)
                if (seen.Add(recipe.Result)) Things.Add(recipe.Result);
            Things.Sort((a, b) =>
            {
                int c = LevelOf(a).CompareTo(LevelOf(b));
                if (c != 0) return c;
                c = string.Compare(RecipeData.ThingName(a), RecipeData.ThingName(b), StringComparison.CurrentCultureIgnoreCase);
                return c != 0 ? c : a.CompareTo(b);
            });
        }

        private static int LevelOf(int thing)
        {
            var info = RecipeData.Thing(thing);
            if (info != null && info.Level > 0) return info.Level;
            var list = RecipeData.Making(thing);
            return list != null && list.Count > 0 ? list[0].ResultLevel : 0;
        }

        private static Recipe InCraft(int thing)
        {
            var list = RecipeData.Making(thing);
            if (list == null) return null;
            if (_craft != 0)
                foreach (var recipe in list)
                    if (recipe.Profession == _craft) return recipe;
            return Variant(thing);
        }

        private static Recipe Variant(int thing)
        {
            var list = RecipeData.Making(thing);
            if (list == null || list.Count == 0) return null;
            int index;
            if (!Variants.TryGetValue(thing, out index) || index < 0 || index >= list.Count) index = 0;
            return list[index];
        }

        private static bool Crafted(int thing) => RecipeData.Making(thing) != null && !RecipeData.Drops(thing);

        private static long Runs(long need, Recipe recipe)
        {
            long per = Math.Max(1, recipe.ResultCount);
            return (need + per - 1) / per;
        }

        private static float Price(Recipe recipe)
        {
            float price;
            return float.TryParse(recipe.Price, NumberStyles.Float, CultureInfo.InvariantCulture, out price) ? price : 0f;
        }

        private static void Build()
        {
            Close();
            var go = new GameObject("QoLCraftCalc", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 831;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);
            go.AddComponent<CraftCalcTicker>();

            var backGo = new GameObject("backdrop", typeof(RectTransform), typeof(Image), typeof(Button));
            backGo.transform.SetParent(go.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
            backGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            backGo.GetComponent<Button>().onClick.AddListener(Close);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panelGo.transform.SetParent(go.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            var back = panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            WardrobeLook.Frame(panelGo, WardrobeLook.Edge);
            var panel = panelGo.transform;

            var title = Say(panel, global::NewAgeQoL.Lang.T("Калькулятор крафта"), 22, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            Wardrobe.At(title.rectTransform, 20f, 12f, 400f, 36f);
            OnlineWindow.MakeCloseButton(panel, Close);

            BuildList(Wardrobe.Box(panel, "things", 16f, Top, ListW, BodyH, WardrobeLook.Card, 12));
            BuildMiddle(Wardrobe.Box(panel, "recipe", MidX, Top, MidW, BodyH, WardrobeLook.Card, 12));
            BuildCart(Wardrobe.Box(panel, "cart", CartX, Top, CartW, BodyH, WardrobeLook.Card, 12));
        }

        private static void BuildList(RectTransform box)
        {
            _search = OnlineWindow.MakeInput(box, ListW - 24f, global::NewAgeQoL.Lang.T("Поиск: вещь"));
            Wardrobe.At((RectTransform)_search.transform, 12f, 12f, ListW - 24f, 36f);
            WardrobeLook.Style(_search);
            _search.characterLimit = 40;
            _search.onValueChanged.AddListener(text =>
            {
                _typed = text;
                _filterAt = Time.unscaledTime + 0.25f;
            });

            Wardrobe.At((RectTransform)Wardrobe.Arrow(box, "‹", () => Step(-1)).transform, 12f, 56f, 34f, 32f);
            var field = Wardrobe.Box(box, "craft", 50f, 56f, ListW - 24f - 76f, 32f, WardrobeLook.Field, 8);
            _craftText = Say(field, "", 14, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleCenter);
            OnlineWindow.Place(_craftText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6f, 0f), new Vector2(-6f, 0f));
            _craftText.resizeTextForBestFit = true;
            _craftText.resizeTextMinSize = 10;
            _craftText.resizeTextMaxSize = 14;
            Wardrobe.At((RectTransform)Wardrobe.Arrow(box, "›", () => Step(1)).transform, ListW - 12f - 34f, 56f, 34f, 32f);

            var levelLabel = Say(box, global::NewAgeQoL.Lang.T("Вещь, ур. от"), 14, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(levelLabel.rectTransform, 14f, 94f, 90f, 32f);
            _fromField = LevelInput(box, _from);
            Wardrobe.At((RectTransform)_fromField.transform, 106f, 94f, 70f, 32f);
            var toLabel = Say(box, global::NewAgeQoL.Lang.T("до"), 14, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleCenter);
            Wardrobe.At(toLabel.rectTransform, 180f, 94f, 34f, 32f);
            _toField = LevelInput(box, _to);
            Wardrobe.At((RectTransform)_toField.transform, 218f, 94f, 70f, 32f);

            var skillLabel = Say(box, global::NewAgeQoL.Lang.T("Профессия от"), 14, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(skillLabel.rectTransform, 14f, 132f, 90f, 32f);
            Fit(skillLabel, 10, 14);
            _skillFromField = LevelInput(box, _skillFrom);
            Wardrobe.At((RectTransform)_skillFromField.transform, 106f, 132f, 70f, 32f);
            var skillToLabel = Say(box, global::NewAgeQoL.Lang.T("до"), 14, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleCenter);
            Wardrobe.At(skillToLabel.rectTransform, 180f, 132f, 34f, 32f);
            _skillToField = LevelInput(box, _skillTo);
            Wardrobe.At((RectTransform)_skillToField.transform, 218f, 132f, 70f, 32f);

            _found = Say(box, "", 12, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(_found.rectTransform, 14f, 168f, ListW - 28f, 20f);
            Fit(_found, 9, 12);

            _itemsScroll = Scroller(box, 8f, 192f, ListW - 16f, BodyH - 200f, 2f, out _items);

            var up = Wardrobe.GameButton(box, "↑", ToTop, false);
            Wardrobe.At(up, ListW - 20f - 44f, BodyH - 20f - 44f, 44f, 44f);
            var look = up.GetComponent<Image>();
            look.sprite = OnlineWindow.Disc();
            look.type = Image.Type.Simple;
            look.color = WardrobeLook.Accent;
            foreach (var label in up.GetComponentsInChildren<Text>(true))
            {
                label.color = WardrobeLook.OnAccent;
                label.resizeTextMaxSize = 22;
                label.fontSize = 22;
            }
            var edge = up.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.5f);
            edge.effectDistance = new Vector2(1f, -1f);
            _toTop = up.gameObject;
            _toTop.SetActive(false);
        }

        private static InputField LevelInput(RectTransform host, int value)
        {
            var field = OnlineWindow.MakeInput(host, 70f, "");
            WardrobeLook.Style(field);
            field.contentType = InputField.ContentType.Custom;
            field.characterValidation = InputField.CharacterValidation.None;
            field.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
            field.characterLimit = 3;
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            field.textComponent.fontSize = 15;
            field.SetTextWithoutNotify(value > 0 ? value.ToString() : "");
            field.onValueChanged.AddListener(text => _filterAt = Time.unscaledTime + 0.25f);
            return field;
        }

        private static int Typed(InputField field)
        {
            int value;
            if (field == null || !int.TryParse(field.text, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return 0;
            return value;
        }

        private static void ToTop()
        {
            if (_itemsScroll == null || _items == null) return;
            _itemsScroll.StopMovement();
            _items.anchoredPosition = new Vector2(_items.anchoredPosition.x, 0f);
        }

        private static void BuildMiddle(RectTransform box)
        {
            var headGo = new GameObject("head", typeof(RectTransform));
            headGo.transform.SetParent(box, false);
            _head = (RectTransform)headGo.transform;
            Wardrobe.At(_head, 0f, 0f, MidW, 214f);

            var need = Say(box, global::NewAgeQoL.Lang.T("Из чего крафтится"), 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(need.rectTransform, 16f, 214f, MidW - 32f, 28f);
            _treeScroll = Scroller(box, 8f, 246f, MidW - 16f, BodyH - 254f, 2f, out _tree);
        }

        private static void BuildCart(RectTransform box)
        {
            _cartTitle = Say(box, global::NewAgeQoL.Lang.T("Корзина"), 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(_cartTitle.rectTransform, 16f, 10f, CartW - 160f, 30f);
            Fit(_cartTitle, 11, 16);
            _clear = Wardrobe.GameButton(box, global::NewAgeQoL.Lang.T("Очистить"), ClearCart, true);
            Wardrobe.At(_clear, CartW - 128f, 10f, 112f, 30f);
            var binsGo = new GameObject("bins", typeof(RectTransform));
            binsGo.transform.SetParent(box, false);
            _bins = (RectTransform)binsGo.transform;
            Wardrobe.At(_bins, 8f, 46f, CartW - 16f, BinH);
            _cartView = (RectTransform)Scroller(box, 8f, CartY, CartW - 16f, CartMax, 2f, out _cart).transform;

            var drop = Say(box, global::NewAgeQoL.Lang.T("Всего дропа"), 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Fit(drop, 11, 16);
            _dropTitle = drop;
            _dropHead = drop.rectTransform;
            _dropView = (RectTransform)Scroller(box, 8f, 0f, CartW - 16f, 100f, 2f, out _drops).transform;
            FitCart();
        }

        private const float CartMax = 200f;
        private const float BinH = 36f;
        private const float CartY = 46f + BinH + 6f;

        private static void FitCart()
        {
            if (_cartView == null || _dropHead == null || _dropView == null) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_cart);
            float h = Mathf.Clamp(LayoutUtility.GetPreferredHeight(_cart), 50f, CartMax);
            Wardrobe.At(_cartView, 8f, CartY, CartW - 16f, h);
            float y = CartY + h + 8f;
            Wardrobe.At(_dropHead, 16f, y, CartW - 32f, 28f);
            Wardrobe.At(_dropView, 8f, y + 32f, CartW - 16f, BodyH - y - 32f - 8f);
        }

        private static ScrollRect Scroller(RectTransform host, float x, float y, float w, float h, float gap, out RectTransform content)
        {
            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(host, false);
            var catcher = scrollGo.GetComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            var srt = (RectTransform)scrollGo.transform;
            Wardrobe.At(srt, x, y, w, h);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = gap;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = srt;
            return scroll;
        }

        private static void Step(int by)
        {
            var crafts = RecipeData.Professions;
            int index = crafts.IndexOf(_craft) + 1;
            int count = crafts.Count + 1;
            index = ((index + by) % count + count) % count;
            _craft = index == 0 ? 0 : crafts[index - 1];
            Refilter();
        }

        private static void Refilter()
        {
            Fill();
            if (_picked == 0) return;
            if (Listed.Contains(_picked))
            {
                Pick(_picked);
                Reveal(Listed.IndexOf(_picked));
            }
        }

        private static void Reveal(int index)
        {
            if (index < 0 || _items == null || _itemsScroll == null) return;
            if (_drawn <= index) Draw(index + 1 - _drawn + Batch);
            Mark();
            Icons();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_items);
            var view = _itemsScroll.viewport;
            if (view == null) return;
            float row = RowH + 2f;
            float room = view.rect.height;
            float most = Mathf.Max(0f, _items.rect.height - room);
            float y = Mathf.Clamp(index * row - (room - RowH) * 0.5f, 0f, most);
            _itemsScroll.StopMovement();
            _items.anchoredPosition = new Vector2(_items.anchoredPosition.x, y);
        }

        private static bool Fits(int thing)
        {
            var list = RecipeData.Making(thing);
            if (list == null) return false;
            if (_from > 0 || _to > 0)
            {
                int level = LevelOf(thing);
                if (_from > 0 && level < _from) return false;
                if (_to > 0 && level > _to) return false;
            }
            foreach (var recipe in list)
            {
                if (_craft != 0 && recipe.Profession != _craft) continue;
                if (_skillFrom > 0 && recipe.Skill < _skillFrom) continue;
                if (_skillTo > 0 && recipe.Skill > _skillTo) continue;
                if (_query.Length == 0 || recipe.Find.Contains(_query)) return true;
            }
            return false;
        }

        private static void Fill()
        {
            if (_items == null) return;
            if (_craftText != null) _craftText.text = _craft == 0 ? global::NewAgeQoL.Lang.T("Все профессии") : RecipeData.Profession(_craft);
            Clear(_items);
            ItemBacks.Clear();
            Prune();
            Listed.Clear();
            foreach (int thing in Things)
                if (Fits(thing)) Listed.Add(thing);
            _drawn = 0;
            int total = Listed.Count;
            if (total == 0) Note(_items, global::NewAgeQoL.Lang.T("Ничего не нашлось"));
            if (_found != null)
            {
                string many = Wardrobe.Plural(total, global::NewAgeQoL.Lang.T("вещь"), global::NewAgeQoL.Lang.T("вещи"), global::NewAgeQoL.Lang.T("вещей"));
                if (_query.Length > 0) _found.text = global::NewAgeQoL.Lang.F("Найдено: {0} {1}", total, many);
                else if (_from > 0 || _to > 0 || _skillFrom > 0 || _skillTo > 0) _found.text = global::NewAgeQoL.Lang.F("Подходит {0} {1}", total, many);
                else if (_craft != 0) _found.text = global::NewAgeQoL.Lang.F("В этой профессии {0} {1}", total, many);
                else _found.text = global::NewAgeQoL.Lang.F("Всего {0} {1}, многие крафтятся в нескольких профессиях", total, many);
            }
            if (_itemsScroll != null) _itemsScroll.verticalNormalizedPosition = 1f;
            Draw(Batch);
            Mark();
            Icons();
        }

        private static void Draw(int count)
        {
            int until = Math.Min(Listed.Count, _drawn + count);
            for (; _drawn < until; _drawn++) ItemRow(Listed[_drawn]);
        }

        private static void More()
        {
            if (_items == null || _itemsScroll == null || _drawn >= Listed.Count) return;
            var view = _itemsScroll.viewport;
            if (view == null) return;
            float left = _items.rect.height - view.rect.height - _items.anchoredPosition.y;
            if (left > Ahead) return;
            Draw(Batch);
            Mark();
            Icons();
        }

        private static void ItemRow(int thing)
        {
            var recipe = InCraft(thing);
            var info = RecipeData.Thing(thing);
            int rarity = info != null ? info.Rarity : recipe.ResultRarity;
            var rt = Clickable(_items, "thing" + thing, RowH, () => Pick(thing));
            ItemBacks[thing] = rt.GetComponent<Image>();
            Icon(rt, info != null && !string.IsNullOrEmpty(info.Image) ? info.Image : recipe.ResultImage, 6f, 5f, 40f, rarity, thing);
            var name = Say(rt, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(thing)), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 54f, 4f, ListW - 16f - 62f, 22f);
            Fit(name, 10, 14);
            var all = RecipeData.Making(thing);
            string sub;
            if (_craft == 0 && all.Count > 1)
            {
                int low = int.MaxValue;
                foreach (var one in all) low = Math.Min(low, one.Skill);
                sub = global::NewAgeQoL.Lang.F("{0} {1}, нужно от {2}", all.Count, Wardrobe.Plural(all.Count, global::NewAgeQoL.Lang.T("профессия"), global::NewAgeQoL.Lang.T("профессии"), global::NewAgeQoL.Lang.T("профессий")), low);
            }
            else sub = global::NewAgeQoL.Lang.F("нужно: {0} {1}", RecipeData.Profession(recipe.Profession), recipe.Skill);
            int level = LevelOf(thing);
            if (level > 0) sub = global::NewAgeQoL.Lang.F("вещь ур. {0} · {1}", level, sub);
            var line = Say(rt, sub, 12, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(line.rectTransform, 54f, 26f, ListW - 16f - 62f, 20f);
            Fit(line, 9, 12);
        }

        private static void Mark()
        {
            foreach (var pair in ItemBacks)
                if (pair.Value != null) pair.Value.color = pair.Key == _picked ? WardrobeLook.Mix(WardrobeLook.Card, WardrobeLook.Accent, 0.22f) : WardrobeLook.Tab;
        }

        private static void Pick(int thing)
        {
            if (RecipeData.Making(thing) == null) return;
            if (thing != _picked)
            {
                _picked = thing;
                _qty = 1;
                Open.Clear();
            }
            var list = RecipeData.Making(thing);
            var now = Variant(thing);
            if (_craft != 0 && now != null && now.Profession != _craft)
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Profession == _craft)
                    {
                        Variants[thing] = i;
                        Wanted[thing] = list[i].Profession;
                        Recount();
                        break;
                    }
            Mark();
            Head();
            Tree();
            if (_treeScroll != null) _treeScroll.verticalNormalizedPosition = 1f;
        }

        private static void Head()
        {
            if (_head == null) return;
            Clear(_head);
            Prune();
            var recipe = Variant(_picked);
            if (recipe == null) return;
            var info = RecipeData.Thing(_picked);
            int rarity = info != null ? info.Rarity : recipe.ResultRarity;
            Icon(_head, recipe.ResultImage, 16f, 14f, 72f, rarity, _picked);
            var name = Say(_head, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(_picked)), 19, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 100f, 14f, MidW - 116f, 28f);
            Fit(name, 12, 19);
            var kind = new List<string>();
            if (recipe.ResultCount > 1) kind.Add(global::NewAgeQoL.Lang.F("за один крафт {0} шт.", recipe.ResultCount));
            if (recipe.ResultLevel > 0) kind.Add(global::NewAgeQoL.Lang.F("уровень {0}", recipe.ResultLevel));
            string rare = WardrobeData.RarityName(rarity);
            if (!string.IsNullOrEmpty(rare)) kind.Add(rare);
            var sub = Say(_head, string.Join(" · ", kind.ToArray()), 13, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(sub.rectTransform, 100f, 42f, MidW - 116f, 20f);

            var list = RecipeData.Making(_picked);
            string craft = global::NewAgeQoL.Lang.F("Нужно: {0} не ниже {1}", RecipeData.Profession(recipe.Profession), recipe.Skill);
            if (list.Count > 1)
            {
                Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "‹", () => Turn(-1)).transform, 100f, 66f, 30f, 26f);
                var field = Wardrobe.Box(_head, "variant", 134f, 66f, MidW - 116f - 68f, 26f, WardrobeLook.Field, 6);
                var text = Say(field, craft, 13, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleCenter);
                OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
                Fit(text, 9, 13);
                Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "›", () => Turn(1)).transform, MidW - 16f - 30f, 66f, 30f, 26f);
            }
            else
            {
                var text = Say(_head, craft, 13, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
                Wardrobe.At(text.rectTransform, 100f, 66f, MidW - 116f, 26f);
            }

            var facts = Say(_head, global::NewAgeQoL.Lang.F("Шанс {0}% · цена крафта {1} {2} · {3}", recipe.Chance, recipe.Price, Talls(recipe.Price), global::NewAgeQoL.Lang.Game(recipe.Name)), 13, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(facts.rectTransform, 16f, 98f, MidW - 32f, 22f);
            Fit(facts, 9, 13);

            var label = Say(_head, global::NewAgeQoL.Lang.T("Крафтов"), 15, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(label.rectTransform, 16f, 130f, 110f, 34f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "−", () => Qty(_qty - 1)).transform, 126f, 131f, 32f, 32f);
            _qtyField = Number(_head, _qty, value => Qty(value));
            Wardrobe.At((RectTransform)_qtyField.transform, 162f, 131f, 70f, 32f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "+", () => Qty(_qty + 1)).transform, 236f, 131f, 32f, 32f);
            Wardrobe.At(Wardrobe.GameButton(_head, global::NewAgeQoL.Lang.T("В корзину"), AddToCart, false), MidW - 16f - 170f, 128f, 170f, 38f);

            _note = Say(_head, "", 12, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(_note.rectTransform, 16f, 172f, MidW - 32f, 36f);
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(_note, 9, 13);
            Hint();
            Icons();
        }

        private static void Turn(int by)
        {
            var list = RecipeData.Making(_picked);
            if (list == null || list.Count < 2) return;
            int index;
            if (!Variants.TryGetValue(_picked, out index)) index = 0;
            index = ((index + by) % list.Count + list.Count) % list.Count;
            Variants[_picked] = index;
            Wanted[_picked] = list[index].Profession;
            Head();
            Tree();
            Recount();
        }

        private static void Qty(int value)
        {
            _qty = Mathf.Clamp(value, 1, 99999);
            if (_qtyField != null) _qtyField.SetTextWithoutNotify(_qty.ToString());
            Hint();
            Tree();
        }

        private static string Output(int runs, Recipe recipe)
        {
            return global::NewAgeQoL.Lang.F("получится {0} шт.", (long)runs * Math.Max(1, recipe.ResultCount));
        }

        private static void Hint()
        {
            if (_note == null) return;
            var recipe = Variant(_picked);
            if (recipe != null && recipe.ResultCount > 1)
            {
                _note.text = global::NewAgeQoL.Lang.F("За один крафт получается {0} шт. — за {1} {2} {3}", recipe.ResultCount, _qty, Wardrobe.Plural(_qty, global::NewAgeQoL.Lang.T("крафт"), global::NewAgeQoL.Lang.T("крафта"), global::NewAgeQoL.Lang.T("крафтов")), Output(_qty, recipe));
                _note.color = WardrobeLook.Accent;
            }
            else
            {
                _note.text = global::NewAgeQoL.Lang.T("Нажми на крафтовый компонент, чтобы раскрыть, из чего он делается");
                _note.color = WardrobeLook.Faint;
            }
        }

        private static void Tree()
        {
            if (_tree == null) return;
            float keep = _treeScroll != null ? _treeScroll.verticalNormalizedPosition : 1f;
            Clear(_tree);
            Prune();
            var recipe = Variant(_picked);
            if (recipe == null) return;
            var stack = new List<int> { _picked };
            Branch(recipe, _qty, 0, _picked.ToString(), stack);
            if (recipe.Parts.Count == 0) Note(_tree, global::NewAgeQoL.Lang.T("Сервер не назвал компонентов"));
            Icons();
            if (_treeScroll != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_tree);
                _treeScroll.verticalNormalizedPosition = keep;
            }
        }

        private static void Branch(Recipe recipe, long runs, int depth, string path, List<int> stack)
        {
            foreach (var part in recipe.Parts)
            {
                long need = part.Count * runs;
                string key = path + "/" + part.Id;
                bool craftable = RecipeData.Making(part.Id) != null && !stack.Contains(part.Id) && depth < Deepest;
                bool open = craftable && Open.Contains(key);
                PartRow(part.Id, need, depth, key, craftable, open);
                if (!open) continue;
                var inner = Variant(part.Id);
                stack.Add(part.Id);
                Branch(inner, Runs(need, inner), depth + 1, key, stack);
                stack.RemoveAt(stack.Count - 1);
            }
        }

        private static void PartRow(int id, long need, int depth, string key, bool craftable, bool open)
        {
            var info = RecipeData.Thing(id);
            int rarity = info != null ? info.Rarity : 0;
            RectTransform rt;
            if (craftable) rt = Clickable(_tree, "part" + id, 46f, () => Unfold(key));
            else rt = Plain(_tree, "part" + id, 46f);
            float x = 6f + depth * Indent;
            if (craftable)
            {
                var arrow = Say(rt, open ? "▾" : "▸", 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleCenter);
                Wardrobe.At(arrow.rectTransform, x, 0f, 16f, 46f);
            }
            x += 18f;
            Icon(rt, info != null ? info.Image : null, x, 5f, 36f, rarity, id);
            float right = MidW - 16f - 8f;
            float countW = 110f;
            float nameW = right - countW - 6f - (x + 44f);
            var name = Say(rt, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(id)), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, x + 44f, 3f, nameW, 22f);
            Fit(name, 9, 14);
            string sub = craftable ? (open ? global::NewAgeQoL.Lang.T("крафтится — нажми, чтобы свернуть") : global::NewAgeQoL.Lang.T("крафтится — нажми, чтобы увидеть состав")) : global::NewAgeQoL.Lang.T("дроп");
            if (craftable && RecipeData.Drops(id)) sub = open ? global::NewAgeQoL.Lang.T("дроп · можно и скрафтить — нажми, чтобы свернуть") : global::NewAgeQoL.Lang.F("дроп · можно и скрафтить: {0} — нажми, чтобы увидеть", Yield(id));
            if (info != null && info.Level > 0) sub = global::NewAgeQoL.Lang.F("ур. {0} · {1}", info.Level, sub);
            var line = Say(rt, sub, 12, FontStyle.Normal, craftable ? WardrobeLook.Good : WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(line.rectTransform, x + 44f, 24f, nameW, 18f);
            Fit(line, 9, 12);
            var count = Say(rt, "× " + need, 16, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleRight);
            Wardrobe.At(count.rectTransform, right - countW, 0f, countW, 46f);
            count.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(count, 10, 16);
        }

        private static string Yield(int id)
        {
            var recipe = Variant(id);
            if (recipe == null || recipe.Parts.Count == 0) return global::NewAgeQoL.Lang.T("есть рецепт");
            var main = recipe.Parts[0];
            foreach (var part in recipe.Parts)
                if (part.Count > main.Count) main = part;
            return global::NewAgeQoL.Lang.F("{0} × {1} → {2} шт.", main.Count, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(main.Id)), Math.Max(1, recipe.ResultCount));
        }

        private static void Unfold(string key)
        {
            if (!Open.Remove(key)) Open.Add(key);
            Tree();
        }

        private static void AddToCart()
        {
            if (_picked == 0) return;
            foreach (var line in Cart)
                if (line.Id == _picked) { line.Count = Math.Min(line.Count + _qty, 99999); Recount(); return; }
            Cart.Add(new Line { Id = _picked, Count = _qty });
            Recount();
        }

        private static void ClearCart()
        {
            if (Empty(Bins[_bin]) && Bins.Count > 1)
            {
                Bins.RemoveAt(_bin);
                if (_bin >= Bins.Count) _bin = Bins.Count - 1;
                Plugin.Trace("[craft] cart removed, left " + Bins.Count);
                Recount();
                return;
            }
            Cart.Clear();
            Recount();
        }

        private static void Drop()
        {
            foreach (var bin in Bins) bin.Lines.RemoveAll(line => RecipeData.Making(line.Id) == null);
        }

        private static bool Empty(Bin bin) => bin.Lines.Count == 0;

        private static void Switch(int index)
        {
            if (index < 0 || index >= Bins.Count || index == _bin) return;
            _bin = index;
            Recount();
        }

        private static void NewBin()
        {
            for (int i = 0; i < Bins.Count; i++)
                if (Empty(Bins[i])) { _bin = i; Recount(); return; }
            if (Bins.Count >= MostBins) return;
            Bins.Add(new Bin());
            _bin = Bins.Count - 1;
            Plugin.Trace("[craft] new cart " + Bins.Count);
            Recount();
        }

        private static void BinTabs()
        {
            if (_bins == null) return;
            Clear(_bins);
            const float gap = 4f;
            float wide = Mathf.Min(BinH, (CartW - 16f - gap * MostBins) / (MostBins + 1));
            for (int i = 0; i < Bins.Count; i++)
            {
                int index = i;
                var bin = Bins[i];
                bool on = i == _bin;
                bool full = bin.Lines.Count > 0;
                var tab = Wardrobe.Arrow(_bins, "", () => Switch(index));
                var rt = (RectTransform)tab.transform;
                Wardrobe.At(rt, i * (wide + gap), 0f, wide, BinH);
                tab.GetComponent<Image>().color = on ? WardrobeLook.Accent : WardrobeLook.Button;
                if (full)
                {
                    int first = bin.Lines[0].Id;
                    var info = RecipeData.Thing(first);
                    var recipe = Variant(first);
                    string image = info != null && !string.IsNullOrEmpty(info.Image) ? info.Image : recipe != null ? recipe.ResultImage : null;
                    int rarity = info != null ? info.Rarity : recipe != null ? recipe.ResultRarity : 0;
                    float side = Mathf.Min(wide, BinH) - 6f;
                    Icon(rt, image, (wide - side) / 2f, (BinH - side) / 2f, side, rarity, first);
                }
                var number = Say(rt, (i + 1).ToString(CultureInfo.InvariantCulture), full ? 11 : 15, FontStyle.Bold,
                    full ? (on ? WardrobeLook.Accent : WardrobeLook.Bright) : (on ? WardrobeLook.OnAccent : WardrobeLook.Bright),
                    full ? TextAnchor.LowerRight : TextAnchor.MiddleCenter);
                OnlineWindow.Place(number.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 1f), new Vector2(-3f, -1f));
                number.raycastTarget = false;
                if (full)
                {
                    var shade = number.gameObject.AddComponent<Outline>();
                    shade.effectColor = new Color(0f, 0f, 0f, 0.9f);
                    shade.effectDistance = new Vector2(1f, -1f);
                }
            }
            if (Bins.Count < MostBins)
            {
                var add = Wardrobe.Arrow(_bins, "+", NewBin);
                Wardrobe.At((RectTransform)add.transform, Bins.Count * (wide + gap), 0f, wide, BinH);
            }
            if (_cartTitle != null)
                _cartTitle.text = Bins.Count > 1
                    ? global::NewAgeQoL.Lang.F("Корзина {0}", _bin + 1)
                    : global::NewAgeQoL.Lang.T("Корзина");
            if (_clear != null)
            {
                var label = _clear.GetComponentInChildren<Text>(true);
                if (label != null)
                    label.text = Empty(Bins[_bin]) && Bins.Count > 1
                        ? global::NewAgeQoL.Lang.T("Убрать")
                        : global::NewAgeQoL.Lang.T("Очистить");
            }
        }

        private static bool _recounting;
        private static bool _again;

        private static void Recount()
        {
            if (_recounting) { _again = true; return; }
            _recounting = true;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    _again = false;
                    Redraw();
                    if (!_again) break;
                }
            }
            finally { _recounting = false; }
        }

        private static void Redraw()
        {
            Save();
            if (_cart == null || _drops == null) return;
            Clear(_cart);
            Clear(_drops);
            Prune();
            BinTabs();
            foreach (var line in Cart) CartRow(line);
            if (Cart.Count == 0) Note(_cart, global::NewAgeQoL.Lang.T("Пусто — выбери вещь и нажми «В корзину»"));
            FitCart();

            var drops = new Dictionary<int, long>();
            Sum(drops, new Dictionary<int, long>());
            var order = new List<int>(drops.Keys);
            order.Sort((a, b) => string.Compare(RecipeData.ThingName(a), RecipeData.ThingName(b), StringComparison.CurrentCultureIgnoreCase));
            int ready = 0;
            DropIds.Clear();
            foreach (int id in order)
            {
                DropIds.Add(id);
                if (Mine(id) >= drops[id]) ready++;
                DropRow(id, drops[id]);
            }
            if (Cart.Count > 0 && drops.Count == 0) Note(_drops, global::NewAgeQoL.Lang.T("Дропа не нужно"));
            if (order.Count > 0) Remark(_drops, StorageNote());
            _ownedSaid = OwnedKey();
            if (_dropTitle != null)
                _dropTitle.text = order.Count > 0
                    ? global::NewAgeQoL.Lang.F("Всего дропа · собрано {0} из {1}", ready, order.Count)
                    : global::NewAgeQoL.Lang.T("Всего дропа");
            Icons();
        }

        private static float Sum(Dictionary<int, long> drops, Dictionary<int, long> made)
        {
            var demand = new Dictionary<int, long>();
            foreach (var line in Cart)
            {
                var recipe = Variant(line.Id);
                Add(demand, line.Id, (long)line.Count * Math.Max(1, recipe != null ? recipe.ResultCount : 1));
            }
            var edges = new Dictionary<int, List<int>>();
            var feeds = new Dictionary<int, int>();
            var reach = new List<int>(demand.Keys);
            var seen = new HashSet<int>(reach);
            var roots = new HashSet<int>(reach);
            for (int i = 0; i < reach.Count; i++)
            {
                int thing = reach[i];
                if (!roots.Contains(thing) && !Crafted(thing)) continue;
                var recipe = Variant(thing);
                if (recipe == null) continue;
                var next = new List<int>();
                foreach (var part in recipe.Parts)
                {
                    if (part.Id == thing) continue;
                    next.Add(part.Id);
                    int n;
                    feeds[part.Id] = feeds.TryGetValue(part.Id, out n) ? n + 1 : 1;
                    if (seen.Add(part.Id)) reach.Add(part.Id);
                }
                edges[thing] = next;
            }

            var queue = new Queue<int>();
            foreach (int thing in reach)
                if (!feeds.ContainsKey(thing)) queue.Enqueue(thing);
            var done = new HashSet<int>();
            float price = 0f;
            while (true)
            {
                if (queue.Count == 0)
                {
                    foreach (int thing in reach)
                        if (!done.Contains(thing)) { queue.Enqueue(thing); break; }
                    if (queue.Count == 0) break;
                }
                int one = queue.Dequeue();
                if (!done.Add(one)) continue;
                long need;
                demand.TryGetValue(one, out need);
                List<int> next;
                if (!edges.TryGetValue(one, out next))
                {
                    if (need > 0) Add(drops, one, need);
                    continue;
                }
                var recipe = Variant(one);
                long runs = need <= 0 ? 0 : (need + Math.Max(1, recipe.ResultCount) - 1) / Math.Max(1, recipe.ResultCount);
                if (runs > 0)
                {
                    Add(made, one, runs);
                    price += runs * Price(recipe);
                    foreach (var part in recipe.Parts)
                    {
                        long more = part.Count * runs;
                        if (part.Id == one || done.Contains(part.Id)) Add(drops, part.Id, more);
                        else Add(demand, part.Id, more);
                    }
                }
                foreach (int part in next)
                {
                    if (--feeds[part] == 0 && !done.Contains(part)) queue.Enqueue(part);
                }
            }
            return price;
        }

        private static string Talls(string amount)
        {
            int whole;
            if (!int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out whole)) return global::NewAgeQoL.Lang.T("таллов");
            return Wardrobe.Plural(whole, global::NewAgeQoL.Lang.T("талл"), global::NewAgeQoL.Lang.T("талла"), global::NewAgeQoL.Lang.T("таллов"));
        }

        private static void Add(Dictionary<int, long> map, int id, long count)
        {
            long have;
            map[id] = map.TryGetValue(id, out have) ? have + count : count;
        }

        private static void CartRow(Line line)
        {
            var info = RecipeData.Thing(line.Id);
            var recipe = Variant(line.Id);
            int rarity = info != null ? info.Rarity : recipe != null ? recipe.ResultRarity : 0;
            int thing = line.Id;
            var rt = Clickable(_cart, "cart" + thing, 44f, () => Pick(thing));
            Icon(rt, info != null && !string.IsNullOrEmpty(info.Image) ? info.Image : recipe != null ? recipe.ResultImage : null, 6f, 4f, 36f, rarity, thing);
            float w = CartW - 16f;
            bool batch = recipe != null && recipe.ResultCount > 1;
            var name = Say(rt, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(thing)), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            float nameW = w - 50f - 170f;
            Wardrobe.At(name.rectTransform, 50f, batch ? 2f : 0f, nameW, batch ? 22f : 44f);
            Fit(name, 9, 14);
            if (batch)
            {
                var sub = Say(rt, Output(line.Count, recipe), 12, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
                Wardrobe.At(sub.rectTransform, 50f, 24f, nameW, 18f);
                Fit(sub, 9, 12);
            }
            Wardrobe.At((RectTransform)Wardrobe.Arrow(rt, "−", () => Change(line, -1)).transform, w - 164f, 8f, 28f, 28f);
            var field = Number(rt, line.Count, value =>
            {
                line.Count = value;
                Recount();
            });
            Wardrobe.At((RectTransform)field.transform, w - 132f, 7f, 62f, 30f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(rt, "+", () => Change(line, 1)).transform, w - 66f, 8f, 28f, 28f);
            var drop = Wardrobe.Arrow(rt, "×", () => Remove(line));
            Wardrobe.At((RectTransform)drop.transform, w - 34f, 8f, 28f, 28f);
            drop.GetComponent<Image>().color = WardrobeLook.Danger;
            foreach (var label in drop.GetComponentsInChildren<Text>(true)) label.color = WardrobeLook.DangerText;
        }

        private static InputField Number(RectTransform host, int value, Action<int> done)
        {
            var field = OnlineWindow.MakeInput(host, 60f, "");
            WardrobeLook.Style(field);
            field.contentType = InputField.ContentType.Custom;
            field.characterValidation = InputField.CharacterValidation.None;
            field.onValidateInput = (text, index, c) => c >= '0' && c <= '9' ? c : '\0';
            field.characterLimit = 5;
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            field.textComponent.fontSize = 15;
            field.SetTextWithoutNotify(value.ToString());
            field.onEndEdit.AddListener(text =>
            {
                int typed;
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out typed) || typed < 1) typed = value;
                typed = Mathf.Clamp(typed, 1, 99999);
                field.SetTextWithoutNotify(typed.ToString());
                done(typed);
            });
            return field;
        }

        private static void Change(Line line, int by)
        {
            line.Count = Mathf.Clamp(line.Count + by, 1, 99999);
            Recount();
        }

        private static void Remove(Line line)
        {
            Cart.Remove(line);
            Recount();
        }

        private static void DropRow(int id, long count)
        {
            var info = RecipeData.Thing(id);
            int rarity = info != null ? info.Rarity : 0;
            long mine = Mine(id);
            bool enough = mine >= count;
            var rt = Plain(_drops, "drop" + id, 44f);
            if (enough) rt.GetComponent<Image>().color = WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Good, 0.32f);
            float w = CartW - 16f;

            Icon(rt, info != null ? info.Image : null, 6f, 6f, 32f, rarity, id);
            var name = Say(rt, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(id)), 14, FontStyle.Normal, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 44f, 0f, w - 44f - 192f, 44f);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(name, 9, 14);

            var look = Wardrobe.Arrow(rt, "", () => MarketLook.Show(id, global::NewAgeQoL.Lang.Game(RecipeData.ThingName(id))));
            Wardrobe.At((RectTransform)look.transform, w - 186f, 7f, 30f, 30f);
            look.GetComponent<Image>().color = MarketLook.Here ? WardrobeLook.Field : WardrobeLook.Mix(WardrobeLook.Field, WardrobeLook.Tab, 0.5f);
            var lookEdge = look.gameObject.AddComponent<Outline>();
            lookEdge.effectColor = WardrobeLook.FieldEdge;
            lookEdge.effectDistance = new Vector2(1f, -1f);
            var bagGo = new GameObject("bag", typeof(RectTransform), typeof(Image));
            bagGo.transform.SetParent(look.transform, false);
            OnlineWindow.Place((RectTransform)bagGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6f, 6f), new Vector2(-6f, -6f));
            var bag = bagGo.GetComponent<Image>();
            bag.sprite = global::NewAgeQoL.Icons.Bag();
            bag.preserveAspect = true;
            bag.raycastTarget = false;
            bag.color = MarketLook.Here ? WardrobeLook.Accent : WardrobeLook.Faint;
            look.gameObject.AddComponent<CraftNote>().Text = () => MarketLook.Here
                ? global::NewAgeQoL.Lang.T("Предложения на рынке")
                : global::NewAgeQoL.Lang.T("Предложения на рынке: только когда стоите на рынке");

            var amount = Say(rt, mine.ToString(CultureInfo.InvariantCulture) + " / " + count.ToString(CultureInfo.InvariantCulture), 16, FontStyle.Bold,
                enough ? WardrobeLook.Good : WardrobeLook.Bright, TextAnchor.MiddleRight);
            Wardrobe.At(amount.rectTransform, w - 148f, 2f, 142f, 22f);
            Fit(amount, 10, 16);
            string left = enough ? global::NewAgeQoL.Lang.T("хватает")
                : global::NewAgeQoL.Lang.F("не хватает {0}", count - mine);
            var rest = Say(rt, left, 12, FontStyle.Bold, enough ? WardrobeLook.Good : WardrobeLook.Accent, TextAnchor.MiddleRight);
            Wardrobe.At(rest.rectTransform, w - 148f, 24f, 142f, 18f);
            Fit(rest, 9, 12);
        }

        private static readonly List<int> DropIds = new List<int>();
        private static string _ownedSaid;
        private static float _ownedAt;

        private static long Mine(int id) => (long)Flasks.QtyOf(id) + Storage.QtyOf(id);

        private static string OwnedKey()
        {
            var at = Storage.Updated;
            var key = new System.Text.StringBuilder(at.HasValue ? at.Value.Ticks.ToString(CultureInfo.InvariantCulture) : "-");
            foreach (int id in DropIds) key.Append(',').Append(Mine(id));
            return key.ToString();
        }

        private static string StorageNote()
        {
            var at = Storage.Updated;
            if (!at.HasValue)
                return global::NewAgeQoL.Lang.T("Хранилище ещё не просмотрено, посчитан только инвентарь. Зайдите в хранилище, и мод его запомнит.");
            return global::NewAgeQoL.Lang.F("Хранилище запомнено {0} (Киев). Положенное без мода учтётся при следующем заходе в хранилище.",
                Kyiv.From(at.Value).ToString("dd.MM HH:mm", CultureInfo.InvariantCulture));
        }

        private static void Owned()
        {
            if (Time.unscaledTime < _ownedAt) return;
            _ownedAt = Time.unscaledTime + 1f;
            if (DropIds.Count == 0 || OwnedKey() == _ownedSaid) return;
            Plugin.Trace("[craft] stock in bag or storage changed, recounting");
            Recount();
        }

        private static string SavePath
        {
            get { return Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "newage.qol"), "craftcart.txt"); }
        }

        private static void Load()
        {
            if (_loaded) return;
            try
            {
                string path = SavePath;
                Bins.Clear();
                Bins.Add(new Bin());
                _bin = 0;
                int seen = 0;
                int active = 0;
                if (File.Exists(path))
                    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        var parts = raw.Split('\t');
                        int id, value;
                        if (parts.Length < 3
                            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0
                            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) continue;
                        if (parts[0] == "bin")
                        {
                            if (seen > 0)
                            {
                                if (Bins.Count >= MostBins) break;
                                Bins.Add(new Bin());
                                _bin = Bins.Count - 1;
                            }
                            seen++;
                            if (value == 1) active = _bin;
                        }
                        else if (parts[0] == "cart")
                        {
                            if (value >= 1 && !Cart.Exists(line => line.Id == id)) Cart.Add(new Line { Id = id, Count = Mathf.Clamp(value, 1, 99999) });
                        }
                        else if (parts[0] == "variant") Wanted[id] = value;
                    }
                _bin = active;
                _loaded = true;
                _saved = Dump();
                Plugin.Trace("[craft] carts read: " + Bins.Count + ", open " + (_bin + 1) + ": " + Cart.Count + " items");
            }
            catch (Exception e) { Plugin.Warn("[craft] reading cart: " + e.Message); }
        }

        private static void Restore()
        {
            foreach (var pair in Wanted)
            {
                var list = RecipeData.Making(pair.Key);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Profession == pair.Value) { Variants[pair.Key] = i; break; }
            }
        }

        private static string Dump()
        {
            var text = new StringBuilder();
            foreach (var pair in Wanted)
                text.Append("variant\t").Append(pair.Key.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(pair.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < Bins.Count; i++)
            {
                var bin = Bins[i];
                text.Append("bin\t").Append((i + 1).ToString(CultureInfo.InvariantCulture)).Append('\t').Append(i == _bin ? "1" : "0").Append('\n');
                foreach (var line in bin.Lines)
                    text.Append("cart\t").Append(line.Id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(line.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            return text.ToString();
        }

        private static void Save()
        {
            if (!_loaded) return;
            string text = Dump();
            if (text == _saved) return;
            try
            {
                string path = SavePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (!File.Exists(path)) File.Move(temp, path);
                else
                {
                    try { File.Replace(temp, path, null); }
                    catch (Exception e)
                    {
                        Plugin.Caught("[craft] atomic replace failed, replacing in steps: " + e.Message);
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                _saved = text;
            }
            catch (Exception e) { Plugin.Warn("[craft] saving cart: " + e.Message); }
        }

        private static RectTransform Clickable(RectTransform host, string name, float high, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().preferredHeight = high;
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Tab;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                try { click(); }
                catch (Exception e) { Plugin.Warn("[craft] click: " + e.Message); }
            });
            return (RectTransform)go.transform;
        }

        private static RectTransform Plain(RectTransform host, string name, float high)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            go.GetComponent<LayoutElement>().preferredHeight = high;
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Tab;
            back.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        private static void Note(RectTransform host, string text)
        {
            var label = Say(host, text, 14, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
        }

        private static void Remark(RectTransform host, string text)
        {
            var go = new GameObject("remark", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(host, false);
            var vlg = go.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var label = Say(go.transform, text, 13, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.UpperLeft);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void Icon(RectTransform host, string image, float x, float y, float size, int rarity, int thing)
        {
            var frame = Wardrobe.Box(host, "icon", x, y, size, size, WardrobeLook.Field, 6);
            frame.GetComponent<Image>().raycastTarget = thing > 0;
            if (thing > 0) frame.gameObject.AddComponent<CraftHover>().Id = thing;
            var edge = frame.gameObject.AddComponent<Outline>();
            edge.effectColor = WardrobeData.RarityColor(rarity);
            edge.effectDistance = new Vector2(1f, -1f);
            var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image));
            picGo.transform.SetParent(frame, false);
            OnlineWindow.Place((RectTransform)picGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var pic = picGo.GetComponent<Image>();
            pic.preserveAspect = true;
            pic.raycastTarget = false;
            pic.enabled = false;
            if (!string.IsNullOrEmpty(image)) Pics.Add(new Pic { Image = pic, Name = image });
        }

        private static void Icons()
        {
            foreach (var one in Pics)
            {
                if (one.Image == null) continue;
                if (one.Image.enabled && !Quickslots.Faded(one.Image.sprite)) continue;
                var sprite = WardrobeIcons.Get(one.Name);
                one.Image.sprite = sprite;
                one.Image.enabled = sprite != null;
            }
        }

        private static Func<string> _hoverNote;

        internal static void EnterNote(Func<string> text)
        {
            _hoverId = -1;
            _hoverNote = text;
            _hoverAt = Time.unscaledTime + 0.3f;
        }

        internal static void LeaveNote(Func<string> text)
        {
            if (_hoverId != -1 || _hoverNote != text) return;
            _hoverId = 0;
            _hoverNote = null;
            _tipFor = 0;
            if (_tip != null) _tip.SetActive(false);
        }

        internal static void Enter(int thing)
        {
            _hoverId = thing;
            _hoverAt = Time.unscaledTime + 0.3f;
        }

        internal static void Leave(int thing)
        {
            if (_hoverId != thing) return;
            _hoverId = 0;
            _tipFor = 0;
            if (_tip != null) _tip.SetActive(false);
        }

        private static void Tip()
        {
            if (_canvasGo == null) return;
            if (_hoverId == 0)
            {
                if (_tip != null && _tip.activeSelf) _tip.SetActive(false);
                return;
            }
            if (Time.unscaledTime < _hoverAt) return;
            if (_tip == null) BuildTip();
            if (_hoverId < 0)
            {
                _tipFor = -1;
                string note = _hoverNote != null ? _hoverNote() : "";
                if (_tipText.text != note) _tipText.text = note;
            }
            else if (_tipFor != _hoverId)
            {
                int thing = _hoverId;
                _tipFor = thing;
                _tipText.text = TipTitle(thing) + "\n<color=#747b85>" + global::NewAgeQoL.Lang.T("загружаю…") + "</color>";
                Ask(thing);
            }
            if (!_tip.activeSelf) _tip.SetActive(true);
            Follow();
        }

        private static void BuildTip()
        {
            _tip = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _tip.transform.SetParent(_canvasGo.transform, false);
            var rt = (RectTransform)_tip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            var back = _tip.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Popup;
            back.raycastTarget = false;
            var edge = _tip.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var vlg = _tip.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(14, 14, 10, 12);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = _tip.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            rt.sizeDelta = new Vector2(340f, 60f);
            _tipText = Say(_tip.transform, "", 14, FontStyle.Normal, WardrobeLook.Body, TextAnchor.UpperLeft);
            _tipText.supportRichText = true;
            _tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
            _tip.SetActive(false);
        }

        private static void Follow()
        {
            var rt = (RectTransform)_tip.transform;
            rt.SetAsLastSibling();
            var host = (RectTransform)_canvasGo.transform;
            Vector2 point;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(host, Input.mousePosition, null, out point)) return;
            var size = rt.rect.size;
            var half = host.rect.size * 0.5f;
            float x = point.x + 18f;
            float y = point.y - 18f;
            if (x + size.x > half.x - 8f) x = point.x - 18f - size.x;
            if (y - size.y < -half.y + 8f) y = -half.y + 8f + size.y;
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static string TipTitle(int thing)
        {
            var info = RecipeData.Thing(thing);
            int rarity = info != null ? info.Rarity : 0;
            return "<b><size=16><color=#" + ColorUtility.ToHtmlStringRGB(WardrobeData.RarityColor(rarity)) + ">" + global::NewAgeQoL.Lang.Game(RecipeData.ThingName(thing)) + "</color></size></b>";
        }

        private static void Ask(int thing)
        {
            try
            {
                var cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
                if (cache == null) return;
                cache.Get(thing, about =>
                {
                    try
                    {
                        if (_tipText == null || _tipFor != thing || about == null) return;
                        _tipText.text = About(thing, about);
                    }
                    catch (Exception e) { Plugin.Caught("[craft] tooltip " + thing + ": " + e.Message); }
                });
            }
            catch (Exception e) { Plugin.Caught("[craft] tooltip " + thing + ": " + e.Message); }
        }

        private static string About(int thing, IGeneralThingInfoDescription about)
        {
            var text = new System.Text.StringBuilder(TipTitle(thing));
            var head = new List<string>();
            string kind = Lang("thingsubtypes.thingsubtype" + (int)about.ThingSubType + ".name");
            if (kind != null) head.Add(kind);
            string rare = WardrobeData.RarityName((int)about.Rarity);
            if (!string.IsNullOrEmpty(rare)) head.Add(rare);
            if (head.Count > 0) text.Append("\n<color=#acb3bd>").Append(string.Join(" · ", head.ToArray())).Append("</color>");

            var need = new List<string>();
            if (about.Level.HasValue) need.Add((Lang("things.level") ?? global::NewAgeQoL.Lang.T("Уровень")) + ": " + about.Level.Value);
            var classes = ERPGClassExtension.DecodeClassMask(about.PreferableClassMask);
            if (classes != null && classes.Count > 0)
            {
                var names = new List<string>();
                foreach (var one in classes) names.Add(Lang("character.params.class." + (int)one) ?? one.ToString());
                need.Add((Lang("character.params.class_label") ?? global::NewAgeQoL.Lang.T("Класс")) + ": " + string.Join(", ", names.ToArray()));
            }
            if (about.MaxDurability.HasValue) need.Add((Lang("things.durability") ?? global::NewAgeQoL.Lang.T("Прочность")) + ": " + about.MaxDurability.Value);
            if (need.Count > 0) text.Append("\n\n").Append(string.Join("\n", need.ToArray()));

            var p = about.AddedParams;
            if (p != null)
            {
                var stats = new List<string>();
                Stat(stats, "character.params.name.strength", p.Strength);
                Stat(stats, "character.params.name.reaction", p.Reaction);
                Stat(stats, "character.params.name.constitution", p.Constitution);
                Stat(stats, "character.params.name.dexterity", p.Dexterity);
                Stat(stats, "character.params.name.intelligence", p.Intelligence);
                Stat(stats, "character.params.name.wisdom", p.Wisdom);
                Stat(stats, "character.params.name.luck", p.Luck);
                Stat(stats, "character.params.name.stamina", p.AddStamina);
                Stat(stats, "things.armor.armor", p.Armor);
                Stat(stats, "character.params.name.protection.astral", p.AstralMagicProtection);
                Stat(stats, "character.params.name.protection.black", p.BlackMagicProtection);
                Stat(stats, "character.params.name.protection.white", p.WhiteMagicProtection);
                if (p.MinDamage.HasValue && p.MaxDamage.HasValue)
                    stats.Add((Lang("things.damage") ?? global::NewAgeQoL.Lang.T("Урон")) + ": <b>" + p.MinDamage.Value + "–" + p.MaxDamage.Value + "</b>");
                if (p.Range.HasValue) stats.Add((Lang("things.range") ?? global::NewAgeQoL.Lang.T("Дальность")) + ": <b>" + p.Range.Value + "</b>");
                if (stats.Count > 0) text.Append("\n\n").Append(string.Join("\n", stats.ToArray()));
            }

            string desc = about.Description;
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.Trim();
                if (desc.Length > 400) desc = desc.Substring(0, 399).TrimEnd() + "…";
                text.Append("\n\n<color=#acb3bd>").Append(desc).Append("</color>");
            }
            return text.ToString();
        }

        private static void Stat(List<string> list, string key, int? value)
        {
            if (!value.HasValue || value.Value == 0) return;
            int v = value.Value;
            string color = v > 0 ? "#7ed68a" : "#f07a6e";
            list.Add((Lang(key) ?? key) + ": <color=" + color + "><b>" + (v > 0 ? "+" : "") + v + "</b></color>");
        }

        private static string Lang(string key)
        {
            try
            {
                string text = ResourceStrings.GetString(key);
                return string.IsNullOrEmpty(text) || text == key ? null : text;
            }
            catch { return null; }
        }

        private static void Prune()
        {
            Pics.RemoveAll(p => p.Image == null);
        }

        private static void Clear(RectTransform host)
        {
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var old = host.GetChild(i).gameObject;
                old.SetActive(false);
                old.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(old);
            }
        }

        private static void Fit(Text text, int least, int most)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = least;
            text.resizeTextMaxSize = most;
        }

        private static Text Say(Transform host, string text, int size, FontStyle style, Color color, TextAnchor anchor)
        {
            var label = OnlineWindow.Label(host, text, size, style, color);
            label.alignment = anchor;
            label.raycastTarget = false;
            return label;
        }
    }

    internal sealed class CraftHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal int Id;

        public void OnPointerEnter(PointerEventData data) => CraftCalc.Enter(Id);

        public void OnPointerExit(PointerEventData data) => CraftCalc.Leave(Id);

        private void OnDisable() => CraftCalc.Leave(Id);
    }

    internal sealed class CraftNote : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Func<string> Text;

        public void OnPointerEnter(PointerEventData data) => CraftCalc.EnterNote(Text);

        public void OnPointerExit(PointerEventData data) => CraftCalc.LeaveNote(Text);

        private void OnDisable() => CraftCalc.LeaveNote(Text);
    }

    internal sealed class CraftCalcTicker : MonoBehaviour
    {
        private void Update()
        {
            try { CraftCalc.Tick(); }
            catch (Exception e) { Plugin.Warn("[craft] tick: " + e.Message); }
        }
    }
}
