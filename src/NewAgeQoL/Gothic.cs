using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Gothic
    {
        internal static ConfigEntry<bool> Enabled;

        internal static readonly Color Stone = Hex("1C1A1F");
        internal static readonly Color Iron = Hex("141214");
        internal static readonly Color Leather = Hex("2A1D1C");
        internal static readonly Color Gold = Hex("B08D57");
        internal static readonly Color Crimson = Hex("7A1420");
        internal static readonly Color Paper = Hex("E8DCC0");
        internal static readonly Color Paper2 = Hex("C9BCA4");
        internal static readonly Color KeyBlue = Hex("8EC5FF");
        internal static readonly Color KeyGreen = Hex("9BE38A");
        internal static readonly Color KeyRed = Hex("FF8A7A");

        internal sealed class School
        {
            internal string Key;
            internal Color Accent;
            internal string Glass;
        }

        internal static readonly Dictionary<int, School> Schools = new Dictionary<int, School>
        {
            { 1, new School { Key = "magic.name.white", Accent = Hex("E0B050"), Glass = "tab_glass_dawn" } },
            { 2, new School { Key = "magic.name.black", Accent = Hex("9B5CFF"), Glass = "tab_glass_night" } },
            { 3, new School { Key = "magic.name.astral", Accent = Hex("5FB4FF"), Glass = "tab_glass_fire" } },
        };

        internal static Color AccentOf(int side) => Schools.TryGetValue(side, out var s) ? s.Accent : Gold;

        private static bool _tried;
        private static AssetBundle _bundle;
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Font _title, _body;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("Look", "GothicShops", true,
                "Окна покупок в мрачном готическом стиле: кованые рамки, кожаные карточки, жетоны цен (со следующего открытия окна).");
        }

        internal static bool On => Enabled != null && Enabled.Value && Ready();

        internal static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }

        internal static bool Ready()
        {
            if (_bundle != null) return true;
            if (_tried) return false;
            _tried = true;
            try
            {
                var asm = typeof(Gothic).Assembly;
                var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("gothic_ui.bundle", StringComparison.OrdinalIgnoreCase));
                if (res == null) { Plugin.Warn("[gothic] embedded bundle not found"); return false; }
                byte[] data;
                using (var stream = asm.GetManifestResourceStream(res))
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    data = ms.ToArray();
                }
                _bundle = AssetBundle.LoadFromMemory(data);
                if (_bundle == null) { Plugin.Warn("[gothic] bundle failed to load"); return false; }
                foreach (var s in _bundle.LoadAllAssets<Sprite>()) Sprites[s.name] = s;
                foreach (var f in _bundle.LoadAllAssets<Font>())
                {
                    if (f.name.StartsWith("Ruslan", StringComparison.OrdinalIgnoreCase)) _title = f;
                    else _body = f;
                }
                Plugin.Trace("[gothic] ui kit loaded: sprites " + Sprites.Count + ", title font " + (_title != null) + ", body font " + (_body != null));
                return true;
            }
            catch (Exception e) { Plugin.Warn("[gothic] loading ui kit: " + e.Message); return false; }
        }

        internal static Sprite Sprite(string name) => Ready() && Sprites.TryGetValue(name, out var s) ? s : null;
        internal static Font TitleFont => Ready() ? _title : null;
        internal static Font BodyFont => Ready() ? _body : null;

        internal static RectTransform Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        internal static Image Layer(Transform parent, string name, string sprite, Image.Type type, Color color, int sibling)
        {
            var old = parent.Find(name);
            GameObject go;
            if (old != null) go = old.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
            }
            var img = go.GetComponent<Image>();
            img.sprite = Sprite(sprite);
            img.type = type;
            img.color = color;
            img.raycastTarget = false;
            if (sibling >= 0) go.transform.SetSiblingIndex(Mathf.Min(sibling, parent.childCount - 1));
            else go.transform.SetAsLastSibling();
            return img;
        }

        internal static void Font(Text t, bool title, Color? color = null, int size = 0)
        {
            if (t == null) return;
            var f = title ? TitleFont : BodyFont;
            if (f != null && t.font != f) t.font = f;
            if (color.HasValue) t.color = color.Value;
            if (size > 0) t.fontSize = size;
            foreach (var o in t.GetComponents<Outline>()) o.effectColor = new Color(0, 0, 0, 0.6f);
        }

        internal static void Button(Button b, Text label = null)
        {
            if (b == null) return;
            var img = b.targetGraphic as Image ?? b.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = Sprite("button_normal");
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.pixelsPerUnitMultiplier = 1.8f;
                b.targetGraphic = img;
            }
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState
            {
                highlightedSprite = Sprite("button_hover"),
                pressedSprite = Sprite("button_pressed"),
                selectedSprite = Sprite("button_normal"),
                disabledSprite = Sprite("button_disabled"),
            };
            if (label == null) label = b.GetComponentInChildren<Text>(true);
            Font(label, true, Paper);
            if (label != null)
            {
                var lr = label.rectTransform;
                if (lr.parent != b.transform) lr.SetParent(b.transform, false);
                lr.anchorMin = Vector2.zero;
                lr.anchorMax = Vector2.one;
                lr.pivot = new Vector2(0.5f, 0.5f);
                lr.offsetMin = new Vector2(12, -9);
                lr.offsetMax = new Vector2(-12, -1);
                label.alignment = TextAnchor.MiddleCenter;
                var fit = label.GetComponent<GothicFitLine>() ?? label.gameObject.AddComponent<GothicFitLine>();
                fit.max = Mathf.Max(label.fontSize, 22);
                lr.offsetMin = new Vector2(34, -9);
                lr.offsetMax = new Vector2(-34, -1);
            }
        }

        internal static void Close(Button b)
        {
            if (b == null) return;
            var img = b.targetGraphic as Image ?? b.GetComponent<Image>();
            if (img == null) return;
            img.sprite = Sprite("close_normal");
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.color = Color.white;
            b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 0.82f, 0.82f);
            cb.pressedColor = new Color(0.8f, 0.6f, 0.6f);
            cb.selectedColor = Color.white;
            b.colors = cb;
            foreach (var g in b.GetComponentsInChildren<Graphic>(true))
                if (g != img && g.gameObject != b.gameObject) g.enabled = false;
        }

        internal static void Scrollbar(Scrollbar s)
        {
            if (s == null) return;
            var track = s.GetComponent<Image>();
            if (track != null) { track.sprite = Sprite("scroll_track"); track.type = Image.Type.Sliced; track.color = Color.white; }
            var h = s.handleRect != null ? s.handleRect.GetComponent<Image>() : null;
            if (h != null) { h.sprite = Sprite("scroll_thumb"); h.type = Image.Type.Sliced; h.color = Color.white; }
            s.transition = Selectable.Transition.ColorTint;
            var cb = s.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1f, 0.92f, 0.8f);
            cb.pressedColor = new Color(0.85f, 0.75f, 0.6f);
            s.colors = cb;
        }

        private static readonly Regex ColorTag = new Regex("<color=#?([0-9A-Fa-f]{6})([0-9A-Fa-f]{2})?>", RegexOptions.Compiled);

        internal static string Keywords(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return ColorTag.Replace(text, m =>
            {
                ColorUtility.TryParseHtmlString("#" + m.Groups[1].Value, out var c);
                Color.RGBToHSV(c, out var hue, out var sat, out var val);
                Color to;
                if (sat < 0.2f) to = val < 0.45f ? Paper2 : Paper;
                else if (hue < 0.05f || hue > 0.92f) to = KeyRed;
                else if (hue < 0.12f) to = Hex("FFB070");
                else if (hue < 0.2f) to = Hex("F2C46B");
                else if (hue < 0.45f) to = KeyGreen;
                else if (hue < 0.75f) to = KeyBlue;
                else to = Hex("C9A2FF");
                return "<color=#" + ColorUtility.ToHtmlStringRGB(to) + ">";
            });
        }
    }

    internal class GothicFitLine : MonoBehaviour
    {
        public int max = 22;
        public int min = 10;
        private string _text;
        private float _width = -1;

        private void LateUpdate()
        {
            var t = GetComponent<Text>();
            if (t == null) return;
            float w = t.rectTransform.rect.width;
            if (t.text == _text && Mathf.Abs(w - _width) < 0.5f) return;
            _text = t.text;
            _width = w;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            int size = max;
            t.fontSize = size;
            while (size > min && t.preferredWidth > w) { size--; t.fontSize = size; }
        }
    }

    internal class GothicTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string text;
        private static GameObject _canvasGo;
        private static RectTransform _panel;
        private static Text _label;

        public void OnPointerEnter(PointerEventData e) => Show(text, e.position);
        public void OnPointerExit(PointerEventData e) => Hide();
        private void OnDisable() => Hide();

        internal static void Show(string text, Vector2 screen)
        {
            if (string.IsNullOrEmpty(text) || !Gothic.Ready()) return;
            if (_canvasGo == null)
            {
                _canvasGo = new GameObject("QoLGothicTip", typeof(Canvas), typeof(CanvasScaler));
                DontDestroyOnLoad(_canvasGo);
                var c = _canvasGo.GetComponent<Canvas>();
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = 32000;
                var sc = _canvasGo.GetComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(1920, 1080);
                sc.matchWidthOrHeight = 1f;
                var p = new GameObject("tip", typeof(RectTransform), typeof(Image));
                p.transform.SetParent(_canvasGo.transform, false);
                _panel = (RectTransform)p.transform;
                _panel.anchorMin = _panel.anchorMax = Vector2.zero;
                var img = p.GetComponent<Image>();
                img.sprite = Gothic.Sprite("tooltip");
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 5f;
                img.raycastTarget = false;
                var t = new GameObject("text", typeof(RectTransform), typeof(Text));
                t.transform.SetParent(p.transform, false);
                _label = t.GetComponent<Text>();
                _label.raycastTarget = false;
                _label.fontSize = 17;
                _label.font = Gothic.BodyFont;
                _label.color = Gothic.Paper;
                _label.alignment = TextAnchor.MiddleCenter;
                _label.horizontalOverflow = HorizontalWrapMode.Overflow;
                _label.verticalOverflow = VerticalWrapMode.Overflow;
                var lr = _label.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                lr.anchoredPosition = Vector2.zero;
            }
            _label.text = text;
            float w = Mathf.Ceil(_label.preferredWidth) + 36;
            float h = Mathf.Ceil(_label.preferredHeight) + 22;
            _label.rectTransform.sizeDelta = new Vector2(w - 36, h - 22);
            _panel.sizeDelta = new Vector2(w, h);
            float k = 1080f / Mathf.Max(1, Screen.height);
            float cw = Screen.width * k;
            var at = screen * k;
            float x = at.x + 16, y = at.y + 16;
            if (x + w > cw - 8) x = at.x - 16 - w;
            if (y + h > 1080 - 8) y = at.y - 16 - h;
            _panel.pivot = Vector2.zero;
            _panel.anchoredPosition = new Vector2(Mathf.Max(8, x), Mathf.Max(8, y));
            _canvasGo.SetActive(true);
        }

        internal static void Hide()
        {
            if (_canvasGo != null) _canvasGo.SetActive(false);
        }
    }
}
