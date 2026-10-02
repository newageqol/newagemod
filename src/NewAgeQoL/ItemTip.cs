using System;
using System.Collections.Generic;
using Transport.Messages.Responses.Things.Thinginfo.Contextinfo;
using UnityDI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ItemTip
    {
        private const float Delay = 0.3f;
        private const float Fresh = 30f;
        private const float Retry = 5f;
        private const float Wide = 330f;
        private const float Pic = 54f;

        private sealed class Known
        {
            internal ContextInventoryThingHintResponseMessage Reply;
            internal float At;
        }

        private static readonly Dictionary<int, Known> Wear = new Dictionary<int, Known>();
        private static readonly Dictionary<int, float> Asked = new Dictionary<int, float>();
        private static readonly Dictionary<int, ResponseCallbackContext> Pending = new Dictionary<int, ResponseCallbackContext>();
        private static readonly List<ResponseCallbackContext> Spent = new List<ResponseCallbackContext>();
        private static readonly List<int> Stale = new List<int>();

        private static GameObject _canvasGo;
        private static GameObject _tip;
        private static Image _frame;
        private static Image _icon;
        private static Text _name;
        private static Text _kind;
        private static RectTransform _body;
        private static ItemTipHover _hover;
        private static float _hoverAt;
        private static int _shownThing;
        private static int _shownInv;
        private static IGeneralThingInfoDescription _about;
        private static bool _dirty;
        private static bool _muted;

        internal static void Attach(GameObject go, InventoryThingItemRenderer cell, InteractiveIcon icon, ESlots.SlotType slot)
        {
            if (go == null) return;
            var hover = go.GetComponent<ItemTipHover>() ?? go.AddComponent<ItemTipHover>();
            hover.Cell = cell;
            hover.Icon = icon;
            hover.Slot = slot;
        }

        internal static void Enter(ItemTipHover hover)
        {
            _hover = hover;
            _hoverAt = Time.unscaledTime + Delay;
            _muted = false;
            Forget();
        }

        internal static void Leave(ItemTipHover hover)
        {
            if (!ReferenceEquals(_hover, hover)) return;
            _hover = null;
            Hide();
        }

        internal static void Tick()
        {
            Sweep();
            if (_hover == null) { Hide(); return; }
            if (_muted || DressDrag.Dragging || !_hover.isActiveAndEnabled || !Allowed(_hover.transform)) { Hide(); return; }
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) { _muted = true; Hide(); return; }
            if (Time.unscaledTime < _hoverAt) return;

            int thing, inv, canUse;
            if (!Resolve(_hover, out thing, out inv, out canUse)) { Hide(); return; }
            if (_tip == null) Build();
            if (_tip == null) return;

            if (thing != _shownThing || inv != _shownInv)
            {
                _shownThing = thing;
                _shownInv = inv;
                _about = null;
                Head(null, Picture(_hover));
                Clear();
                Line(_body, "загружаю…", 13, FontStyle.Italic, WardrobeLook.Faint);
                Describe(thing);
                AskWear(inv);
                _dirty = true;
            }
            if (_dirty && _about != null)
            {
                _dirty = false;
                Head(_about, Picture(_hover));
                Fill(_about, inv, canUse);
            }
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
            if (!_tip.activeSelf) _tip.SetActive(true);
            Follow();
        }

        private static bool Allowed(Transform t)
        {
            try
            {
                if (t == null || Manikin.Owns(t)) return false;
                var menu = Controllers.Get<UserMenuController>();
                return menu != null && menu.IsWindowOpened && menu.Window != null && t.IsChildOf(menu.Window.transform);
            }
            catch { return false; }
        }

        private static bool Resolve(ItemTipHover hover, out int thing, out int inv, out int canUse)
        {
            thing = 0;
            inv = 0;
            canUse = 0;
            if (hover.Cell != null)
            {
                var data = hover.Cell.Data;
                if (data == null || data.ThingId <= 0) return false;
                thing = data.ThingId;
                inv = data.InventoryId;
                canUse = data.CanUse;
                return true;
            }
            if (hover.Icon == null || hover.Icon.Id <= 0) return false;
            inv = hover.Icon.Id;
            thing = DressDrag.ThingOf(hover.Slot, inv);
            return thing > 0;
        }

        private static Sprite Picture(ItemTipHover hover)
        {
            try
            {
                if (hover == null) return null;
                if (hover.Cell != null) return DressDrag.PictureOf(hover.Cell);
                if (hover.Icon != null) return DressDrag.PictureOf(hover.Icon);
            }
            catch { }
            return null;
        }

        private static void Describe(int thing)
        {
            try
            {
                var cache = DependencyContainer.GetContainer()?.Resolve<GeneralThingInfoDescriptionManager>();
                if (cache == null) return;
                cache.Get(thing, about =>
                {
                    if (about == null || about.ThingId != _shownThing) return;
                    _about = about;
                    _dirty = true;
                });
            }
            catch (Exception e) { Plugin.Trace("[itemtip] description " + thing + ": " + e.Message); }
        }

        private static void AskWear(int inv)
        {
            if (inv <= 0) return;
            if (Wear.TryGetValue(inv, out var known) && Time.unscaledTime - known.At < Fresh) return;
            if (Asked.TryGetValue(inv, out var at) && Time.unscaledTime - at < Retry) return;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                if (Pending.TryGetValue(inv, out var stale)) { Spent.Add(stale); Pending.Remove(inv); }
                var context = new ResponseCallbackContext();
                Asked[inv] = Time.unscaledTime;
                Pending[inv] = context;
                nc.SendRequest(context, new ContextInventoryThingHintRequest(inv, EThingContextWindow.WINDOW_INVENTORY), 387, -1, msg =>
                {
                    Asked.Remove(inv);
                    if (Pending.TryGetValue(inv, out var mine) && ReferenceEquals(mine, context)) Pending.Remove(inv);
                    Spent.Add(context);
                    var reply = msg as ContextInventoryThingHintResponseMessage;
                    if (reply == null) return;
                    Wear[inv] = new Known { Reply = reply, At = Time.unscaledTime };
                    if (inv == _shownInv) _dirty = true;
                });
            }
            catch (Exception e) { Plugin.Trace("[itemtip] wear " + inv + ": " + e.Message); }
        }

        private static void Sweep()
        {
            if (Pending.Count > 0)
            {
                Stale.Clear();
                foreach (var pair in Pending)
                    if (!Asked.TryGetValue(pair.Key, out var at) || Time.unscaledTime - at >= Retry) Stale.Add(pair.Key);
                foreach (var inv in Stale)
                {
                    Spent.Add(Pending[inv]);
                    Pending.Remove(inv);
                    Asked.Remove(inv);
                }
            }
            if (Spent.Count == 0) return;
            foreach (var context in Spent)
            {
                try { context.Destroy(); }
                catch (Exception e) { Plugin.Trace("[itemtip] reply context: " + e.Message); }
            }
            Spent.Clear();
        }

        private static void Head(IGeneralThingInfoDescription about, Sprite sprite)
        {
            int rarity = about != null ? (int)about.Rarity : 0;
            Color tint = WardrobeData.RarityColor(rarity);
            _frame.color = rarity > 1 ? tint : WardrobeLook.Edge;
            _icon.sprite = sprite;
            _icon.enabled = sprite != null;
            if (about == null)
            {
                _name.text = "";
                _kind.text = "";
                return;
            }
            _name.text = string.IsNullOrEmpty(about.Name) ? "#" + about.ThingId : about.Name;
            _name.color = tint;
            var head = new List<string>();
            string kind = Lang("thingsubtypes.thingsubtype" + (int)about.ThingSubType + ".name");
            if (kind != null) head.Add(kind);
            string rare = WardrobeData.RarityName(rarity);
            if (!string.IsNullOrEmpty(rare)) head.Add("<color=#" + ColorUtility.ToHtmlStringRGB(tint) + ">" + rare + "</color>");
            _kind.text = string.Join("  ·  ", head.ToArray());
        }

        private static void Fill(IGeneralThingInfoDescription about, int inv, int canUse)
        {
            int? wear = null;
            if (inv > 0 && Wear.TryGetValue(inv, out var known) && known.Reply != null)
            {
                wear = known.Reply.Wear;
                canUse = known.Reply.CanUseMask;
            }
            Clear();
            bool any = false;

            int start = _body.childCount;
            if (about.Level.HasValue)
                Row(Lang("things.level") ?? "Уровень", about.Level.Value.ToString(), ThingsHelper.IsLevelRequired(canUse) ? WardrobeLook.Bad : WardrobeLook.Bright);
            var classes = ERPGClassExtension.DecodeClassMask(about.PreferableClassMask);
            if (classes != null && classes.Count > 0)
            {
                var names = new List<string>();
                foreach (var one in classes) names.Add(Lang("character.params.class." + (int)one) ?? one.ToString());
                Row(Lang("character.params.class_label") ?? "Класс", string.Join(", ", names.ToArray()), ThingsHelper.IsClassRequired(canUse) ? WardrobeLook.Bad : WardrobeLook.Bright);
            }
            if (about.MaxDurability.HasValue)
            {
                int max = about.MaxDurability.Value;
                string value = wear.HasValue ? wear.Value + " / " + max : max.ToString();
                Color color = WardrobeLook.Bright;
                if (wear.HasValue && wear.Value <= 0) color = WardrobeLook.Bad;
                else if (wear.HasValue && max > 0 && wear.Value * 4 < max) color = WardrobeLook.Accent;
                Row(Lang("things.durability") ?? "Прочность", value, color);
            }
            any |= _body.childCount > start;

            var p = about.AddedParams;
            if (p != null)
            {
                start = _body.childCount;
                if (any) Rule();
                if (p.MinDamage.HasValue && p.MaxDamage.HasValue)
                    Row(Lang("things.damage") ?? "Урон", p.MinDamage.Value + " – " + p.MaxDamage.Value, WardrobeLook.Accent);
                if (p.Range.HasValue) Row(Lang("things.range") ?? "Дальность", p.Range.Value.ToString(), WardrobeLook.Accent);
                Stat("character.params.name.strength", p.Strength);
                Stat("character.params.name.reaction", p.Reaction);
                Stat("character.params.name.constitution", p.Constitution);
                Stat("character.params.name.dexterity", p.Dexterity);
                Stat("character.params.name.intelligence", p.Intelligence);
                Stat("character.params.name.wisdom", p.Wisdom);
                Stat("character.params.name.luck", p.Luck);
                Stat("character.params.name.stamina", p.AddStamina);
                Stat("things.armor.armor", p.Armor);
                Stat("character.params.name.protection.astral", p.AstralMagicProtection);
                Stat("character.params.name.protection.black", p.BlackMagicProtection);
                Stat("character.params.name.protection.white", p.WhiteMagicProtection);
                if (_body.childCount == start + (any ? 1 : 0)) Drop(start);
                else any = true;
            }

            string desc = about.Description;
            if (!string.IsNullOrEmpty(desc))
            {
                desc = desc.Trim();
                if (desc.Length > 400) desc = desc.Substring(0, 399).TrimEnd() + "…";
                if (any) Rule();
                var text = Line(_body, desc, 13, FontStyle.Italic, WardrobeLook.Label);
                text.lineSpacing = 1.05f;
            }
        }

        private static void Stat(string key, int? value)
        {
            if (!value.HasValue || value.Value == 0) return;
            int v = value.Value;
            Row(Lang(key) ?? key, (v > 0 ? "+" : "") + v, v > 0 ? WardrobeLook.Good : WardrobeLook.Bad);
        }

        private static void Row(string label, string value, Color color)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_body, false);
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            var left = Line(go.transform, label, 14, FontStyle.Normal, WardrobeLook.Label);
            left.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var right = Line(go.transform, value, 14, FontStyle.Bold, color);
            right.alignment = TextAnchor.UpperRight;
            right.horizontalOverflow = HorizontalWrapMode.Wrap;
            right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1.4f;
        }

        private static void Rule()
        {
            var go = new GameObject("rule", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            go.transform.SetParent(_body, false);
            var image = go.GetComponent<Image>();
            image.color = WardrobeLook.Edge;
            image.raycastTarget = false;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 1f;
            le.minHeight = 1f;
        }

        private static Text Line(Transform host, string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(host, text, size, style, color);
            label.alignment = TextAnchor.UpperLeft;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static void Clear() => Drop(0);

        private static void Drop(int from)
        {
            for (int i = _body.childCount - 1; i >= from; i--)
            {
                var old = _body.GetChild(i).gameObject;
                old.SetActive(false);
                old.transform.SetParent(null, false);
                UnityEngine.Object.Destroy(old);
            }
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

        private static void Forget()
        {
            _shownThing = 0;
            _shownInv = 0;
            _about = null;
        }

        private static void Hide()
        {
            Forget();
            if (_tip != null && _tip.activeSelf) _tip.SetActive(false);
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLItemTip", typeof(Canvas), typeof(CanvasScaler));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29000;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);

            _tip = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _tip.transform.SetParent(_canvasGo.transform, false);
            var rt = (RectTransform)_tip.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(Wide, 80f);
            var back = _tip.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Popup;
            back.raycastTarget = false;
            var edge = _tip.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var vlg = _tip.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 12);
            vlg.spacing = 0f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = _tip.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = new GameObject("head", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            head.transform.SetParent(_tip.transform, false);
            var shade = head.GetComponent<Image>();
            shade.sprite = OnlineWindow.Rounded(10);
            shade.type = Image.Type.Sliced;
            shade.color = WardrobeLook.Card;
            shade.raycastTarget = false;
            var hlg = head.GetComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 14, 12, 12);
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            var frameGo = new GameObject("frame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frameGo.transform.SetParent(head.transform, false);
            _frame = frameGo.GetComponent<Image>();
            _frame.sprite = OnlineWindow.Rounded(8);
            _frame.type = Image.Type.Sliced;
            _frame.raycastTarget = false;
            var fle = frameGo.GetComponent<LayoutElement>();
            fle.minWidth = fle.preferredWidth = Pic;
            fle.minHeight = fle.preferredHeight = Pic;
            var wellGo = new GameObject("well", typeof(RectTransform), typeof(Image));
            wellGo.transform.SetParent(frameGo.transform, false);
            var wrt = (RectTransform)wellGo.transform;
            wrt.anchorMin = Vector2.zero;
            wrt.anchorMax = Vector2.one;
            wrt.offsetMin = new Vector2(2f, 2f);
            wrt.offsetMax = new Vector2(-2f, -2f);
            var well = wellGo.GetComponent<Image>();
            well.sprite = OnlineWindow.Rounded(7);
            well.type = Image.Type.Sliced;
            well.color = WardrobeLook.Field;
            well.raycastTarget = false;
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(wellGo.transform, false);
            var irt = (RectTransform)iconGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = new Vector2(3f, 3f);
            irt.offsetMax = new Vector2(-3f, -3f);
            _icon = iconGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            var titles = new GameObject("titles", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            titles.transform.SetParent(head.transform, false);
            titles.GetComponent<LayoutElement>().flexibleWidth = 1f;
            var tvlg = titles.GetComponent<VerticalLayoutGroup>();
            tvlg.spacing = 3f;
            tvlg.childAlignment = TextAnchor.MiddleLeft;
            tvlg.childControlWidth = true;
            tvlg.childControlHeight = true;
            tvlg.childForceExpandWidth = true;
            tvlg.childForceExpandHeight = false;
            _name = Line(titles.transform, "", 17, FontStyle.Bold, WardrobeLook.Bright);
            _kind = Line(titles.transform, "", 13, FontStyle.Normal, WardrobeLook.Label);

            var bodyGo = new GameObject("body", typeof(RectTransform), typeof(VerticalLayoutGroup));
            bodyGo.transform.SetParent(_tip.transform, false);
            _body = (RectTransform)bodyGo.transform;
            var bvlg = bodyGo.GetComponent<VerticalLayoutGroup>();
            bvlg.padding = new RectOffset(16, 16, 10, 0);
            bvlg.spacing = 6f;
            bvlg.childControlWidth = true;
            bvlg.childControlHeight = true;
            bvlg.childForceExpandWidth = true;
            bvlg.childForceExpandHeight = false;

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

        internal static void Shutdown()
        {
            _hover = null;
            Sweep();
            foreach (var context in Pending.Values)
            {
                try { context.Destroy(); }
                catch { }
            }
            Pending.Clear();
            Asked.Clear();
            Wear.Clear();
            Forget();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _tip = null;
            _frame = null;
            _icon = null;
            _name = null;
            _kind = null;
            _body = null;
        }
    }

    internal sealed class ItemTipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal InventoryThingItemRenderer Cell;
        internal InteractiveIcon Icon;
        internal ESlots.SlotType Slot;

        public void OnPointerEnter(PointerEventData data) => ItemTip.Enter(this);

        public void OnPointerExit(PointerEventData data) => ItemTip.Leave(this);

        private void OnDisable() => ItemTip.Leave(this);
    }
}
