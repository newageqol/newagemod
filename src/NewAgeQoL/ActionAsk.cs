using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ActionAsk
    {
        private const float Width = 560f;
        private const float IconSide = 64f;
        private const int Order = 995;

        private static readonly FieldInfo DialogField = AccessTools.Field(typeof(ConfirmActionDialog), "DialogImage");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(ConfirmActionDialog), "ActionNameText");
        private static readonly FieldInfo TextField = AccessTools.Field(typeof(ConfirmActionDialog), "ActionDescriptionText");
        private static readonly FieldInfo ParamsField = AccessTools.Field(typeof(ConfirmActionDialog), "ActionParamsContainer");
        private static readonly FieldInfo WarnWrapField = AccessTools.Field(typeof(ConfirmActionDialog), "ValidatorMessageWrapper");
        private static readonly FieldInfo WarnField = AccessTools.Field(typeof(ConfirmActionDialog), "ValidatorMessageText");
        private static readonly FieldInfo OkField = AccessTools.Field(typeof(ConfirmActionDialog), "ConfirmButtonButton");
        private static readonly PropertyInfo ViewProp = AccessTools.Property(typeof(ConfirmActionDialogController), "ConfirmActionDialog");
        private static readonly PropertyInfo CancelProp = AccessTools.Property(typeof(ConfirmActionDialogController), "CancelButton");
        private static readonly PropertyInfo ConfirmProp = AccessTools.Property(typeof(ConfirmActionDialogController), "ConfirmButton");

        private static GameObject _root;
        private static RectTransform _panel;
        private static Image _icon;
        private static Text _title;
        private static RectTransform _chips;
        private static Text _body;
        private static Text _warn;
        private static Image _okBack;
        private static Text _okText;
        private static Button _ok;
        private static string _shown;
        private static CanvasGroup _veil;
        private static bool _told;
        private static bool _parked;
        private static bool _own;
        private static Action _yes;
        private static readonly StringBuilder Sign = new StringBuilder();
        private static readonly List<KeyValuePair<Sprite, string>> Params = new List<KeyValuePair<Sprite, string>>();

        internal static void Ask(Sprite icon, string title, string body, string chip, string warn, Action yes)
        {
            try
            {
                if (_root == null) Build();
                Unveil();
                _own = true;
                _yes = yes;
                if (!_root.activeSelf) _root.SetActive(true);
                _shown = null;

                _icon.sprite = icon;
                _icon.enabled = icon != null;
                _title.text = title ?? "";
                _body.text = body ?? "";
                _body.gameObject.SetActive(!string.IsNullOrEmpty(_body.text));
                _warn.text = warn ?? "";
                _warn.gameObject.SetActive(!string.IsNullOrEmpty(_warn.text));

                for (int i = _chips.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_chips.GetChild(i).gameObject);
                if (!string.IsNullOrEmpty(chip)) Chip(null, chip);
                _chips.gameObject.SetActive(!string.IsNullOrEmpty(chip));

                _ok.interactable = true;
                _okBack.color = WardrobeLook.Accent;
                _okText.color = WardrobeLook.OnAccent;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
                Plugin.Trace("[action window] asking before using '" + title + "'");
            }
            catch (Exception e) { Plugin.Trace("[action window] ask: " + e.Message); }
        }

        internal static bool Asking => _own && _root != null && _root.activeSelf;

        internal static void Drop()
        {
            if (!_own) return;
            _own = false;
            _yes = null;
            Hide();
        }

        private static void KeepOwn()
        {
            if (_root == null || !_root.activeSelf) { _own = false; _yes = null; return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && !Typing()) Confirm();
        }

        private static bool Typing()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var picked = es != null ? es.currentSelectedGameObject : null;
            return picked != null && (picked.GetComponent<InputField>() != null || picked.GetComponent("TMP_InputField") != null);
        }

        internal static void Keep()
        {
            if (_own) { KeepOwn(); return; }
            ConfirmActionDialogController ctrl = null;
            ConfirmActionDialog view = null;
            try
            {
                ctrl = Controllers.Get<ConfirmActionDialogController>();
                view = ctrl != null && ctrl.Opened ? ViewProp?.GetValue(ctrl) as ConfirmActionDialog : null;
            }
            catch (Exception e) { Plugin.Trace("[action window] game window: " + e.Message); }

            if (view == null || !view.gameObject.activeInHierarchy || view.IsAttackDialog || _parked)
            {
                if (view == null || !view.gameObject.activeInHierarchy) _parked = false;
                Hide();
                Unveil();
                return;
            }

            Veil(view);
            var warnWrap = WarnWrapField?.GetValue(view) as Transform;
            bool warned = warnWrap != null && warnWrap.gameObject.activeInHierarchy;
            var settings = GameSettings.Instance;
            if (settings != null && !settings.ShowConfirmation && !warned)
            {
                Hide();
                return;
            }

            if (_root == null) Build();
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                _shown = null;
            }
            Fill(view, warned);
        }

        private static void Veil(ConfirmActionDialog view)
        {
            if (_veil == null || _veil.gameObject != view.gameObject)
            {
                Unveil();
                _veil = view.GetComponent<CanvasGroup>() ?? view.gameObject.AddComponent<CanvasGroup>();
            }
            if (_veil.alpha != 0f) _veil.alpha = 0f;
            if (_veil.blocksRaycasts) _veil.blocksRaycasts = false;
        }

        private static void Unveil()
        {
            if (_veil == null) return;
            _veil.alpha = 1f;
            _veil.blocksRaycasts = true;
            _veil = null;
        }

        private static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
            _shown = null;
        }

        private static void Fill(ConfirmActionDialog view, bool warned)
        {
            var image = DialogField?.GetValue(view) as Image;
            var name = NameField?.GetValue(view) as Text;
            var body = TextField?.GetValue(view) as Text;
            var warn = WarnField?.GetValue(view) as Text;
            var ok = OkField?.GetValue(view) as Button;
            var holder = ParamsField?.GetValue(view) as Transform;

            Params.Clear();
            if (holder != null && holder.gameObject.activeInHierarchy)
                for (int i = 0; i < holder.childCount; i++)
                {
                    var one = holder.GetChild(i);
                    if (one == null || !one.gameObject.activeSelf || one.GetComponent<ConfirmActionDialogParam>() == null) continue;
                    var pic = one.Find("ActionParamIcon");
                    var label = one.Find("ActionParamText");
                    var sprite = pic != null ? pic.GetComponent<Image>()?.sprite : null;
                    var value = label != null ? label.GetComponent<Text>()?.text : null;
                    Params.Add(new KeyValuePair<Sprite, string>(sprite, value ?? ""));
                }

            bool can = ok == null || ok.interactable;
            Sign.Length = 0;
            Sign.Append(image != null && image.sprite != null ? image.sprite.GetInstanceID() : 0).Append('|')
                .Append(name != null ? name.text : "").Append('|')
                .Append(body != null ? body.text : "").Append('|')
                .Append(warned && warn != null ? warn.text : "").Append('|')
                .Append(can ? '1' : '0');
            foreach (var p in Params) Sign.Append('|').Append(p.Key != null ? p.Key.GetInstanceID() : 0).Append(':').Append(p.Value);
            string sign = Sign.ToString();
            if (sign == _shown) return;
            _shown = sign;

            _icon.sprite = image != null ? image.sprite : null;
            _icon.enabled = _icon.sprite != null;
            _title.text = name != null ? name.text : "";
            _body.text = body != null ? body.text : "";
            _body.gameObject.SetActive(!string.IsNullOrEmpty(_body.text));
            _warn.text = warned && warn != null ? warn.text : "";
            _warn.gameObject.SetActive(!string.IsNullOrEmpty(_warn.text));

            for (int i = _chips.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_chips.GetChild(i).gameObject);
            foreach (var p in Params) Chip(p.Key, p.Value);
            _chips.gameObject.SetActive(Params.Count > 0);

            _ok.interactable = can;
            _okBack.color = can ? WardrobeLook.Accent : WardrobeLook.Mix(WardrobeLook.Button, WardrobeLook.Accent, 0.15f);
            _okText.color = can ? WardrobeLook.OnAccent : WardrobeLook.Faint;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
            if (_told) return;
            _told = true;
            Plugin.Trace("[action window] game confirm window shown in mod style");
        }

        private static void Confirm()
        {
            if (_own)
            {
                var yes = _yes;
                Drop();
                try { yes?.Invoke(); }
                catch (Exception e) { Plugin.Trace("[action window] own confirm: " + e.Message); }
                return;
            }
            try
            {
                if (_ok != null && !_ok.interactable) return;
                var ctrl = Controllers.Get<ConfirmActionDialogController>();
                var press = ctrl != null ? ConfirmProp?.GetValue(ctrl) as ButtonClickCallback : null;
                if (press == null) { Plugin.Trace("[action window] game has no confirm button"); return; }
                press.OnClick();
                var view = ctrl.Opened ? ViewProp?.GetValue(ctrl) as ConfirmActionDialog : null;
                var warnWrap = view != null ? WarnWrapField?.GetValue(view) as Transform : null;
                if (view != null && view.gameObject.activeInHierarchy && (warnWrap == null || !warnWrap.gameObject.activeInHierarchy))
                {
                    _parked = true;
                    Plugin.Trace("[action window] game asked something more after confirm, its own window is back");
                }
            }
            catch (Exception e) { Plugin.Trace("[action window] confirm: " + e.Message); }
        }

        private static void Cancel()
        {
            if (_own) { Drop(); return; }
            try
            {
                var ctrl = Controllers.Get<ConfirmActionDialogController>();
                var press = ctrl != null ? CancelProp?.GetValue(ctrl) as ButtonClickCallback : null;
                if (press == null) { Plugin.Trace("[action window] game has no cancel button"); return; }
                press.OnClick();
            }
            catch (Exception e) { Plugin.Trace("[action window] cancel: " + e.Message); }
        }

        private static void Build()
        {
            _root = new GameObject("QoLActionAsk", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Order;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            UiScale.Own(scaler);

            var panelGo = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(_root.transform, false);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(Width, 200f);
            _panel.anchoredPosition = new Vector2(0f, 60f);
            var back = panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var edge = panelGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var column = panelGo.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(18, 18, 16, 16);
            column.spacing = 12f;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            var fit = panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = Box("head", panelGo.transform, true, 14f);
            head.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            var frameGo = new GameObject("icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            frameGo.transform.SetParent(head, false);
            var frame = frameGo.GetComponent<Image>();
            frame.color = WardrobeLook.Card;
            frame.sprite = OnlineWindow.Rounded(10);
            frame.type = Image.Type.Sliced;
            WardrobeLook.Frame(frameGo, WardrobeLook.Edge);
            var fl = frameGo.GetComponent<LayoutElement>();
            fl.minWidth = fl.preferredWidth = IconSide;
            fl.minHeight = fl.preferredHeight = IconSide;
            var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image));
            picGo.transform.SetParent(frameGo.transform, false);
            var prt = (RectTransform)picGo.transform;
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(5f, 5f); prt.offsetMax = new Vector2(-5f, -5f);
            _icon = picGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            var side = Box("side", head, false, 8f);
            side.GetComponent<LayoutElement>().flexibleWidth = 1f;
            side.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            _title = Say(side, 21, FontStyle.Bold, WardrobeLook.Accent, TextAnchor.MiddleLeft);
            _chips = Box("chips", panelGo.transform, true, 6f);
            var row = _chips.GetComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;

            var lineGo = new GameObject("line", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            lineGo.transform.SetParent(panelGo.transform, false);
            lineGo.GetComponent<Image>().color = WardrobeLook.Edge;
            lineGo.GetComponent<Image>().raycastTarget = false;
            lineGo.GetComponent<LayoutElement>().minHeight = lineGo.GetComponent<LayoutElement>().preferredHeight = 1f;

            _body = Say(panelGo.transform, 18, FontStyle.Normal, WardrobeLook.Body, TextAnchor.MiddleCenter);
            _body.lineSpacing = 1.1f;
            _warn = Say(panelGo.transform, 16, FontStyle.Bold, WardrobeLook.Bad, TextAnchor.MiddleCenter);

            var buttons = Box("buttons", panelGo.transform, true, 10f);
            buttons.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            Image cancelBack;
            Text cancelText;
            Press(buttons, Lang.T("Отмена"), WardrobeLook.Button, WardrobeLook.Bright, Cancel, out cancelBack, out cancelText);
            _ok = Press(buttons, Lang.T("Применить"), WardrobeLook.Accent, WardrobeLook.OnAccent, Confirm, out _okBack, out _okText);
        }

        private static RectTransform Box(string name, Transform host, bool across, float gap)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            HorizontalOrVerticalLayoutGroup group = across
                ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>()
                : go.AddComponent<VerticalLayoutGroup>();
            group.spacing = gap;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = !across;
            group.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        private static Text Say(Transform host, int size, FontStyle style, Color color, TextAnchor align)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Chip(Sprite sprite, string value)
        {
            var go = new GameObject("chip", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            go.transform.SetParent(_chips, false);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Field;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            WardrobeLook.Frame(go, WardrobeLook.FieldEdge);
            var row = go.GetComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(6, 10, 3, 3);
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            if (sprite != null)
            {
                var picGo = new GameObject("pic", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                picGo.transform.SetParent(go.transform, false);
                var pic = picGo.GetComponent<Image>();
                pic.sprite = sprite;
                pic.preserveAspect = true;
                pic.raycastTarget = false;
                var pl = picGo.GetComponent<LayoutElement>();
                pl.minWidth = pl.preferredWidth = 22f;
                pl.minHeight = pl.preferredHeight = 22f;
            }
            var t = Say(go.transform, 16, FontStyle.Bold, WardrobeLook.Bright, TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.text = value;
        }

        private static Button Press(Transform host, string caption, Color color, Color ink, Action act, out Image back, out Text label)
        {
            var go = new GameObject("button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            back = go.GetComponent<Image>();
            back.color = color;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 42f;
            le.flexibleWidth = 1f;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            var tint = button.colors;
            tint.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            tint.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            tint.disabledColor = Color.white;
            button.colors = tint;
            button.onClick.AddListener(() => act());
            label = Say(go.transform, 18, FontStyle.Bold, ink, TextAnchor.MiddleCenter);
            var lrt = label.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            label.text = caption;
            return button;
        }
    }
}
