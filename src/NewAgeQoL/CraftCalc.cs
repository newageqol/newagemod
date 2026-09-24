using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class CraftCalc
    {
        private const float PanelW = 1300f;
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

        private static GameObject _canvasGo;
        private static RectTransform _items;
        private static ScrollRect _itemsScroll;
        private static GameObject _toTop;
        private static RectTransform _head;
        private static RectTransform _tree;
        private static ScrollRect _treeScroll;
        private static RectTransform _cart;
        private static RectTransform _drops;
        private static Text _found;
        private static Text _craftText;
        private static InputField _search;
        private static InputField _qtyField;
        private static readonly List<Pic> Pics = new List<Pic>();
        private static readonly Dictionary<int, Image> ItemBacks = new Dictionary<int, Image>();
        private static readonly List<int> Things = new List<int>();
        private static readonly List<int> Listed = new List<int>();
        private static int _drawn;

        private static readonly List<Line> Cart = new List<Line>();
        private static readonly Dictionary<int, int> Variants = new Dictionary<int, int>();
        private static readonly HashSet<string> Open = new HashSet<string>();

        private static int _craft;
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

        internal static bool IsOpen => _canvasGo != null;

        internal static void Show()
        {
            try
            {
                if (!RecipeData.Ready)
                {
                    Notice.Show("Калькулятор крафта: в сборке нет рецептов", 6f);
                    return;
                }
                RecipeData.Fetch();
                if (_seenData != RecipeData.Version)
                {
                    Things.Clear();
                    Variants.Clear();
                    Cart.RemoveAll(line => RecipeData.Making(line.Id) == null);
                }
                _seenData = RecipeData.Version;
                Collect();
                _query = "";
                _typed = "";
                if (_picked == 0 || RecipeData.Making(_picked) == null) _picked = Things.Count > 0 ? Things[0] : 0;
                Build();
                Fill();
                Pick(_picked);
                Recount();
                Plugin.Trace("[крафт] окно открыто: вещей " + Things.Count + ", в корзине " + Cart.Count);
            }
            catch (Exception e) { Plugin.Fault("[крафт] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            _hoverId = 0;
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
            _drops = null;
            _found = null;
            _craftText = null;
            _search = null;
            _qtyField = null;
            Pics.Clear();
            ItemBacks.Clear();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
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
                if (query != _query)
                {
                    _query = query;
                    Fill();
                }
            }
            More();
            Tip();
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
            Cart.RemoveAll(line => RecipeData.Making(line.Id) == null);
            Variants.Clear();
            Open.Clear();
            Fill();
            Pick(_picked);
            Recount();
            Plugin.Trace("[крафт] рецепты обновились, окно перерисовано");
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

        private static bool Crafted(int thing) => RecipeData.Making(thing) != null;

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

            var title = Say(panel, "Калькулятор крафта", 22, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            Wardrobe.At(title.rectTransform, 20f, 12f, 400f, 36f);
            OnlineWindow.MakeCloseButton(panel, Close);

            BuildList(Wardrobe.Box(panel, "things", 16f, Top, ListW, BodyH, WardrobeLook.Card, 12));
            BuildMiddle(Wardrobe.Box(panel, "recipe", MidX, Top, MidW, BodyH, WardrobeLook.Card, 12));
            BuildCart(Wardrobe.Box(panel, "cart", CartX, Top, CartW, BodyH, WardrobeLook.Card, 12));
        }

        private static void BuildList(RectTransform box)
        {
            _search = OnlineWindow.MakeInput(box, ListW - 24f, "Поиск: вещь или компонент");
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

            _found = Say(box, "", 12, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(_found.rectTransform, 14f, 92f, ListW - 28f, 20f);
            Fit(_found, 9, 12);

            _itemsScroll = Scroller(box, 8f, 116f, ListW - 16f, BodyH - 124f, 2f, out _items);

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

            var need = Say(box, "Из чего крафтится", 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(need.rectTransform, 16f, 214f, MidW - 32f, 28f);
            _treeScroll = Scroller(box, 8f, 246f, MidW - 16f, BodyH - 254f, 2f, out _tree);
        }

        private static void BuildCart(RectTransform box)
        {
            var head = Say(box, "Корзина", 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(head.rectTransform, 16f, 10f, 200f, 30f);
            Wardrobe.At(Wardrobe.GameButton(box, "Очистить", ClearCart, true), CartW - 128f, 10f, 112f, 30f);
            Scroller(box, 8f, 46f, CartW - 16f, 230f, 2f, out _cart);

            var drop = Say(box, "Всего дропа", 16, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            Wardrobe.At(drop.rectTransform, 16f, 284f, CartW - 32f, 28f);
            Scroller(box, 8f, 316f, CartW - 16f, BodyH - 316f - 8f, 2f, out _drops);
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
            foreach (var recipe in list)
            {
                if (_craft != 0 && recipe.Profession != _craft) continue;
                if (_query.Length == 0 || recipe.Find.Contains(_query)) return true;
            }
            return false;
        }

        private static void Fill()
        {
            if (_items == null) return;
            if (_craftText != null) _craftText.text = _craft == 0 ? "Все профессии" : RecipeData.Profession(_craft);
            Clear(_items);
            ItemBacks.Clear();
            Prune();
            Listed.Clear();
            foreach (int thing in Things)
                if (Fits(thing)) Listed.Add(thing);
            _drawn = 0;
            int total = Listed.Count;
            if (total == 0) Note(_items, "Ничего не нашлось");
            if (_found != null)
            {
                string many = Wardrobe.Plural(total, "вещь", "вещи", "вещей");
                if (_query.Length > 0) _found.text = "Найдено: " + total + " " + many;
                else if (_craft != 0) _found.text = "В этой профессии " + total + " " + many;
                else _found.text = "Всего " + total + " " + many + ", многие крафтятся в нескольких профессиях";
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
            var name = Say(rt, RecipeData.ThingName(thing), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 54f, 4f, ListW - 16f - 62f, 22f);
            Fit(name, 10, 14);
            var all = RecipeData.Making(thing);
            string sub;
            if (_craft == 0 && all.Count > 1)
            {
                int low = int.MaxValue;
                foreach (var one in all) low = Math.Min(low, one.Skill);
                sub = all.Count + " " + Wardrobe.Plural(all.Count, "профессия", "профессии", "профессий") + ", нужно от " + low;
            }
            else sub = "нужно: " + RecipeData.Profession(recipe.Profession) + " " + recipe.Skill;
            int level = LevelOf(thing);
            if (level > 0) sub = "вещь ур. " + level + " · " + sub;
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
            var name = Say(_head, RecipeData.ThingName(_picked), 19, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 100f, 14f, MidW - 116f, 28f);
            Fit(name, 12, 19);
            var kind = new List<string>();
            if (recipe.ResultCount > 1) kind.Add("за один крафт " + recipe.ResultCount + " шт.");
            if (recipe.ResultLevel > 0) kind.Add("уровень " + recipe.ResultLevel);
            string rare = WardrobeData.RarityName(rarity);
            if (!string.IsNullOrEmpty(rare)) kind.Add(rare);
            var sub = Say(_head, string.Join(" · ", kind.ToArray()), 13, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(sub.rectTransform, 100f, 42f, MidW - 116f, 20f);

            var list = RecipeData.Making(_picked);
            string craft = "Нужно: " + RecipeData.Profession(recipe.Profession) + " не ниже " + recipe.Skill;
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

            var facts = Say(_head, "Шанс " + recipe.Chance + "% · цена крафта " + recipe.Price + " " + Talls(recipe.Price) + " · " + recipe.Name, 13, FontStyle.Normal, WardrobeLook.Faint, TextAnchor.MiddleLeft);
            Wardrobe.At(facts.rectTransform, 16f, 98f, MidW - 32f, 22f);
            Fit(facts, 9, 13);

            var label = Say(_head, "Крафтов", 15, FontStyle.Normal, WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(label.rectTransform, 16f, 130f, 110f, 34f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "−", () => Qty(_qty - 1)).transform, 126f, 131f, 32f, 32f);
            _qtyField = Number(_head, _qty, value => Qty(value));
            Wardrobe.At((RectTransform)_qtyField.transform, 162f, 131f, 70f, 32f);
            Wardrobe.At((RectTransform)Wardrobe.Arrow(_head, "+", () => Qty(_qty + 1)).transform, 236f, 131f, 32f, 32f);
            Wardrobe.At(Wardrobe.GameButton(_head, "В корзину", AddToCart, false), MidW - 16f - 170f, 128f, 170f, 38f);

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
            Variants[_picked] = ((index + by) % list.Count + list.Count) % list.Count;
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
            return "получится " + (long)runs * Math.Max(1, recipe.ResultCount) + " шт.";
        }

        private static void Hint()
        {
            if (_note == null) return;
            var recipe = Variant(_picked);
            if (recipe != null && recipe.ResultCount > 1)
            {
                _note.text = "За один крафт получается " + recipe.ResultCount + " шт. — за " + _qty + " " + Wardrobe.Plural(_qty, "крафт", "крафта", "крафтов") + " " + Output(_qty, recipe);
                _note.color = WardrobeLook.Accent;
            }
            else
            {
                _note.text = "Нажми на крафтовый компонент, чтобы раскрыть, из чего он делается";
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
            if (recipe.Parts.Count == 0) Note(_tree, "Сервер не назвал компонентов");
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
            var name = Say(rt, RecipeData.ThingName(id), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, x + 44f, 3f, nameW, 22f);
            Fit(name, 9, 14);
            string sub = craftable ? (open ? "крафтится — нажми, чтобы свернуть" : "крафтится — нажми, чтобы увидеть состав") : "дроп";
            if (info != null && info.Level > 0) sub = "ур. " + info.Level + " · " + sub;
            var line = Say(rt, sub, 12, FontStyle.Normal, craftable ? WardrobeLook.Good : WardrobeLook.Label, TextAnchor.MiddleLeft);
            Wardrobe.At(line.rectTransform, x + 44f, 24f, nameW, 18f);
            var count = Say(rt, "× " + need, 16, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleRight);
            Wardrobe.At(count.rectTransform, right - countW, 0f, countW, 46f);
            count.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(count, 10, 16);
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
            Cart.Clear();
            Recount();
        }

        private static void Recount()
        {
            if (_cart == null || _drops == null) return;
            Clear(_cart);
            Clear(_drops);
            Prune();
            foreach (var line in Cart) CartRow(line);
            if (Cart.Count == 0) Note(_cart, "Пусто — выбери вещь и нажми «В корзину»");

            var drops = new Dictionary<int, long>();
            Sum(drops, new Dictionary<int, long>());
            var order = new List<int>(drops.Keys);
            order.Sort((a, b) => string.Compare(RecipeData.ThingName(a), RecipeData.ThingName(b), StringComparison.CurrentCultureIgnoreCase));
            foreach (int id in order) DropRow(id, drops[id]);
            if (Cart.Count > 0 && drops.Count == 0) Note(_drops, "Дропа не нужно");
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
            if (!int.TryParse(amount, NumberStyles.Integer, CultureInfo.InvariantCulture, out whole)) return "таллов";
            return Wardrobe.Plural(whole, "талл", "талла", "таллов");
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
            var name = Say(rt, RecipeData.ThingName(thing), 14, FontStyle.Bold, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
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
            var rt = Plain(_drops, "drop" + id, 40f);
            Icon(rt, info != null ? info.Image : null, 6f, 4f, 32f, rarity, id);
            float w = CartW - 16f;
            var name = Say(rt, RecipeData.ThingName(id), 14, FontStyle.Normal, WardrobeData.RarityColor(rarity), TextAnchor.MiddleLeft);
            Wardrobe.At(name.rectTransform, 46f, 0f, w - 46f - 132f, 40f);
            Fit(name, 9, 14);
            var amount = Say(rt, "× " + count, 16, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleRight);
            Wardrobe.At(amount.rectTransform, w - 128f, 0f, 120f, 40f);
            amount.horizontalOverflow = HorizontalWrapMode.Wrap;
            Fit(amount, 10, 16);
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
                catch (Exception e) { Plugin.Warn("[крафт] нажатие: " + e.Message); }
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
            if (_tipFor != _hoverId)
            {
                int thing = _hoverId;
                _tipFor = thing;
                _tipText.text = TipTitle(thing) + "\n<color=#747b85>загружаю…</color>";
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
            return "<b><size=16><color=#" + ColorUtility.ToHtmlStringRGB(WardrobeData.RarityColor(rarity)) + ">" + RecipeData.ThingName(thing) + "</color></size></b>";
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
                    catch (Exception e) { Plugin.Trace("[крафт] облачко " + thing + ": " + e.Message); }
                });
            }
            catch (Exception e) { Plugin.Trace("[крафт] облачко " + thing + ": " + e.Message); }
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
            if (about.Level.HasValue) need.Add((Lang("things.level") ?? "Уровень") + ": " + about.Level.Value);
            var classes = ERPGClassExtension.DecodeClassMask(about.PreferableClassMask);
            if (classes != null && classes.Count > 0)
            {
                var names = new List<string>();
                foreach (var one in classes) names.Add(Lang("character.params.class." + (int)one) ?? one.ToString());
                need.Add((Lang("character.params.class_label") ?? "Класс") + ": " + string.Join(", ", names.ToArray()));
            }
            if (about.MaxDurability.HasValue) need.Add((Lang("things.durability") ?? "Прочность") + ": " + about.MaxDurability.Value);
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
                    stats.Add((Lang("things.damage") ?? "Урон") + ": <b>" + p.MinDamage.Value + "–" + p.MaxDamage.Value + "</b>");
                if (p.Range.HasValue) stats.Add((Lang("things.range") ?? "Дальность") + ": <b>" + p.Range.Value + "</b>");
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

    internal sealed class CraftCalcTicker : MonoBehaviour
    {
        private void Update()
        {
            try { CraftCalc.Tick(); }
            catch (Exception e) { Plugin.Warn("[крафт] такт: " + e.Message); }
        }
    }
}
