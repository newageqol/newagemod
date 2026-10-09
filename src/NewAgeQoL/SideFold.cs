using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class SideFold
    {
        private const float StripWide = 18f;
        private const float StripGap = 4f;
        private const float SlideTime = 0.2f;
        private const float OpenLeast = 24f;

        private readonly ConfigEntry<bool> _cfg;
        private readonly RectTransform _clip;
        private readonly RectTransform _strip;
        private readonly Image _mark;
        private readonly Text _name;
        private float _slide;
        private float _least;
        private float _high;
        private Vector2 _size;

        internal readonly RectTransform Body;

        internal SideFold(Transform host, string key, string title, ConfigEntry<bool> folded)
        {
            _cfg = folded;
            _slide = Folded ? 1f : 0f;

            var clip = new GameObject(key + "Clip", typeof(RectTransform), typeof(RectMask2D));
            clip.transform.SetParent(host, false);
            _clip = (RectTransform)clip.transform;
            _clip.anchorMin = _clip.anchorMax = new Vector2(1f, 1f);
            _clip.pivot = new Vector2(1f, 1f);

            var body = new GameObject(key, typeof(RectTransform));
            body.transform.SetParent(_clip, false);
            Body = (RectTransform)body.transform;
            Body.anchorMin = Body.anchorMax = new Vector2(1f, 1f);
            Body.pivot = new Vector2(1f, 1f);

            var strip = new GameObject(key + "Fold", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            strip.transform.SetParent(host, false);
            _strip = (RectTransform)strip.transform;
            _strip.anchorMin = _strip.anchorMax = new Vector2(1f, 1f);
            _strip.pivot = new Vector2(1f, 1f);
            var back = strip.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            var edge = strip.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var press = strip.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            press.colors = colors;
            press.onClick.AddListener(Toggle);

            var markGo = new GameObject("mark", typeof(RectTransform), typeof(Image));
            markGo.transform.SetParent(strip.transform, false);
            _mark = markGo.GetComponent<Image>();
            _mark.sprite = Icons.Chevron();
            _mark.color = WardrobeLook.Label;
            _mark.preserveAspect = true;
            _mark.raycastTarget = false;
            var mrt = _mark.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0.5f, 1f);
            mrt.pivot = new Vector2(0.5f, 0.5f);
            mrt.sizeDelta = new Vector2(9f, 9f);

            _name = OnlineWindow.Label(strip.transform, "", 12, FontStyle.Bold, WardrobeLook.Label);
            _name.raycastTarget = false;
            _name.horizontalOverflow = HorizontalWrapMode.Overflow;
            _name.verticalOverflow = VerticalWrapMode.Overflow;
            var nrt = _name.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Title(title);
            Marked();
        }

        internal bool Folded => _cfg != null && _cfg.Value;

        internal float Height => _high;

        internal bool Open => _slide < 0.05f;

        internal void Title(string title)
        {
            if (_name.text == title) return;
            _name.text = title;
            float len = Mathf.Ceil(_name.preferredWidth) + 8f;
            _name.rectTransform.sizeDelta = new Vector2(len, StripWide);
            _name.rectTransform.anchoredPosition = new Vector2(0f, -(20f + len * 0.5f));
            _least = 24f + len;
        }

        internal void Lay(Vector2 rightTop, Vector2 size)
        {
            float target = Folded ? 1f : 0f;
            if (_slide != target) _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime / SlideTime);
            float e = _slide * _slide * (3f - 2f * _slide);
            bool shown = _slide < 0.999f;
            if (_clip.gameObject.activeSelf != shown) _clip.gameObject.SetActive(shown);

            _size = size;
            if ((Body.sizeDelta - size).sqrMagnitude > 0.01f) Body.sizeDelta = size;
            var clipSize = new Vector2(size.x + 2f, size.y + 2f);
            if ((_clip.sizeDelta - clipSize).sqrMagnitude > 0.01f) _clip.sizeDelta = clipSize;
            var clipSpot = new Vector2(rightTop.x - StripWide - StripGap, rightTop.y);
            if ((_clip.anchoredPosition - clipSpot).sqrMagnitude > 0.01f) _clip.anchoredPosition = clipSpot;
            var bodySpot = new Vector2(e * (size.x + 4f), -1f);
            if ((Body.anchoredPosition - bodySpot).sqrMagnitude > 0.01f) Body.anchoredPosition = bodySpot;

            float open = Mathf.Max(size.y, OpenLeast);
            _high = Mathf.Lerp(open, _least, e);
            var stripSize = new Vector2(StripWide, _high);
            if ((_strip.sizeDelta - stripSize).sqrMagnitude > 0.01f) _strip.sizeDelta = stripSize;
            if ((_strip.anchoredPosition - rightTop).sqrMagnitude > 0.01f) _strip.anchoredPosition = rightTop;
            var markSpot = new Vector2(0f, -Mathf.Lerp(_high * 0.5f, 12f, e));
            if ((_mark.rectTransform.anchoredPosition - markSpot).sqrMagnitude > 0.01f) _mark.rectTransform.anchoredPosition = markSpot;
            var tint = _name.color;
            if (!Mathf.Approximately(tint.a, e)) { tint.a = e; _name.color = tint; }
            bool named = e > 0.01f;
            if (_name.gameObject.activeSelf != named) _name.gameObject.SetActive(named);
        }

        internal bool Under()
        {
            if (RectTransformUtility.RectangleContainsScreenPoint(_strip, Input.mousePosition, null)) return true;
            return Open && RectTransformUtility.RectangleContainsScreenPoint(_clip, Input.mousePosition, null);
        }

        private void Toggle()
        {
            if (_cfg != null) _cfg.Value = !_cfg.Value;
            Marked();
            Plugin.Trace("[side] " + _name.text + (Folded ? " folded to the right" : " opened"));
        }

        private void Marked() => _mark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Folded ? 180f : 0f);
    }
}
