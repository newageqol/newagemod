using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ActionLook
    {
        private const float PressHigh = 50f;
        private const float PressEdge = 28f;
        private const float PressGap = 6f;
        private const int PressSize = 20;
        private const float Footer = 76f;
        private const int CaptionSize = 21;
        private const int HeadSize = 30;
        private const int SectionSize = 17;

        private static PropertyInfo _viewProp;
        private static FieldInfo _bodyField, _okOnField, _okOffField;
        private static FieldInfo _blocksField, _kicksField, _blockCaptionField, _kickCaptionField, _captionField;
        private static bool _wired;

        private static ConfirmActionDialog _dialog;
        private static AttackDescription _body;
        private static RectTransform _wrap;
        private static float _wrapLow;
        private static readonly HashSet<int> Done = new HashSet<int>();
        private static Sprite _back, _block, _strike, _rule;
        private static bool _told;
        private static float _scanAt;

        internal static void Tick()
        {
            try
            {
                var ctrl = Controllers.Get<ConfirmActionDialogController>();
                if (ctrl == null || !ctrl.Opened) return;
                Wire();
                var view = _viewProp?.GetValue(ctrl) as ConfirmActionDialog;
                if (view == null || !view.gameObject.activeInHierarchy) return;
                if (!view.IsAttackDialog)
                {
                    if (ReferenceEquals(view, _dialog)) Room(view, false);
                    return;
                }
                if (!ReferenceEquals(view, _dialog))
                {
                    _dialog = view;
                    _body = null;
                    _wrap = null;
                    Done.Clear();
                    _scanAt = 0f;
                    Frame(view);
                }
                Room(view, true);
                Body(view);
                if (Time.unscaledTime >= _scanAt)
                {
                    _scanAt = Time.unscaledTime + 0.25f;
                    Texts(view);
                }
            }
            catch (Exception e) { Plugin.Caught("[strike look] " + e.Message); }
        }

        private static void Wire()
        {
            if (_wired) return;
            _wired = true;
            _viewProp = AccessTools.Property(typeof(ConfirmActionDialogController), "ConfirmActionDialog");
            _bodyField = AccessTools.Field(typeof(ConfirmActionDialog), "AttackDescription");
            _okOnField = AccessTools.Field(typeof(ConfirmActionDialog), "EnableConfirmButtonSprite");
            _okOffField = AccessTools.Field(typeof(ConfirmActionDialog), "DisableConfirmButtonSprite");
            _blocksField = AccessTools.Field(typeof(AttackDescription), "Blocks");
            _kicksField = AccessTools.Field(typeof(AttackDescription), "Kicks");
            _blockCaptionField = AccessTools.Field(typeof(AttackDescription), "BlockCaption");
            _kickCaptionField = AccessTools.Field(typeof(AttackDescription), "KickCaption");
            _captionField = AccessTools.Field(typeof(AttackDialogRow), "CaptionText");
        }

        private static Sprite Art(ref Sprite kept, string name, float perUnit, float border)
        {
            if (kept != null && kept.texture != null) return kept;
            var raw = Icons.Flash(name);
            if (raw == null || raw.texture == null) return null;
            var tex = raw.texture;
            kept = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), perUnit, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            return kept;
        }

        private static void Frame(ConfirmActionDialog view)
        {
            var panel = view.transform.Find("ConfirmActionPanel") as RectTransform;
            if (panel == null) { Plugin.Trace("[strike look] window has no panel, left as is"); return; }

            var backGo = new GameObject("QoLStrikeBack", typeof(RectTransform), typeof(Image));
            backGo.transform.SetParent(panel, false);
            backGo.transform.SetAsFirstSibling();
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(-6f, -6f);
            brt.offsetMax = new Vector2(6f, 14f);
            var back = backGo.GetComponent<Image>();
            back.sprite = Art(ref _back, "act_back", 150f, 40f);
            back.type = Image.Type.Sliced;
            back.color = back.sprite != null ? Color.white : WardrobeLook.Window;
            back.raycastTarget = true;

            _wrap = panel.Find("DescriptionWrapper") as RectTransform;
            if (_wrap != null) _wrapLow = _wrap.offsetMin.y;
            Mute(_wrap);
            var header = panel.Find("HeaderWrapper");
            Mute(header);
            Mute(header != null ? header.Find("ActionImage") : null);
            Mute(header != null ? header.Find("ActionImage/ActionImageFrame") : null);

            Rule(panel, "QoLStrikeRule", 1f, -70f);
            Rule(panel, "QoLStrikeFoot", 0f, Footer);

            var face = OnlineWindow.Rounded(10);
            Press(view.transform.Find("CancelButton"), face, false, Lang.T("Отмена"), WardrobeLook.Button, WardrobeLook.Bright);
            _okOnField?.SetValue(view, face);
            _okOffField?.SetValue(view, face);
            Press(view.transform.Find("ConfirmButton"), face, true, Lang.T("Подтвердить"), WardrobeLook.Accent, WardrobeLook.OnAccent);

            if (_told) return;
            _told = true;
            Plugin.Trace("[strike look] strike window restyled");
        }

        private static void Rule(RectTransform panel, string name, float edge, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.08f, edge);
            rt.anchorMax = new Vector2(0.92f, edge);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(0f, 2f);
            rt.anchoredPosition = new Vector2(0f, y);
            var image = go.GetComponent<Image>();
            image.sprite = Art(ref _rule, "act_rule", 100f, 0f);
            image.color = image.sprite != null ? Color.white : WardrobeLook.Edge;
            image.raycastTarget = false;
        }

        private static void Room(ConfirmActionDialog view, bool attack)
        {
            var rt = view.transform as RectTransform;
            if (rt == null) return;
            float tall = 400f + Footer;
            if (attack && rt.sizeDelta.y < tall - 0.5f) rt.sizeDelta = new Vector2(rt.sizeDelta.x, tall);
            if (_wrap == null) return;
            float low = attack ? _wrapLow + Footer : _wrapLow;
            if (Mathf.Abs(_wrap.offsetMin.y - low) > 0.1f) _wrap.offsetMin = new Vector2(_wrap.offsetMin.x, low);
        }

        private static void Mute(Transform node)
        {
            var image = node != null ? node.GetComponent<Image>() : null;
            if (image != null) image.enabled = false;
        }

        private static void Press(Transform node, Sprite face, bool right, string caption, Color tint, Color ink)
        {
            if (node == null) return;
            var rt = node as RectTransform;
            if (rt != null)
            {
                rt.anchorMin = new Vector2(right ? 0.5f : 0.12f, 0f);
                rt.anchorMax = new Vector2(right ? 0.88f : 0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                float low = (Footer - PressHigh) * 0.5f;
                rt.offsetMin = new Vector2(right ? PressGap : PressEdge, low);
                rt.offsetMax = new Vector2(right ? -PressEdge : -PressGap, low + PressHigh);
            }
            var image = node.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = face;
                image.type = Image.Type.Sliced;
                image.preserveAspect = false;
                image.color = tint;
            }
            var labelGo = new GameObject("QoLLabel", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(node, false);
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var label = labelGo.GetComponent<Text>();
            label.font = FlaskPicker.Font();
            label.fontSize = PressSize;
            label.fontStyle = FontStyle.Bold;
            label.color = ink;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = caption;
            Done.Add(label.GetInstanceID());
            var button = node.GetComponent<Button>();
            if (button == null) return;
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.17f, 1.1f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        private static void Body(ConfirmActionDialog view)
        {
            var body = _bodyField?.GetValue(view) as AttackDescription;
            if (body == null || ReferenceEquals(body, _body)) return;
            _body = body;
            Rows(_blocksField?.GetValue(body) as AttackDialogRow[], Art(ref _block, "act_block", 100f, 0f));
            Rows(_kicksField?.GetValue(body) as AttackDialogRow[], Art(ref _strike, "act_strike", 100f, 0f));
            Section(_blockCaptionField?.GetValue(body) as Text);
            Section(_kickCaptionField?.GetValue(body) as Text);
        }

        private static void Rows(AttackDialogRow[] rows, Sprite mark)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                if (row == null) continue;
                Box(row.Left, mark);
                Box(row.Right, mark);
            }
        }

        private static void Box(Toggle toggle, Sprite mark)
        {
            if (toggle == null) return;
            var back = toggle.targetGraphic as Image;
            if (back != null)
            {
                Fill(back.rectTransform, 0f);
                back.sprite = OnlineWindow.RoundedExact(10);
                back.type = Image.Type.Sliced;
                back.color = WardrobeLook.Field;
                WardrobeLook.Frame(back.gameObject, WardrobeLook.FieldEdge);
            }
            var tick = toggle.graphic as Image;
            if (tick != null && mark != null)
            {
                Fill(tick.rectTransform, 1f);
                tick.sprite = mark;
                tick.type = Image.Type.Simple;
                tick.preserveAspect = true;
                tick.color = Color.white;
            }
            toggle.transition = Selectable.Transition.ColorTint;
            var colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.9f, 1.8f, 1.55f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(1.5f, 1.45f, 1.3f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.3f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            toggle.colors = colors;
        }

        private static void Fill(RectTransform rt, float grow)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-grow, -grow);
            rt.offsetMax = new Vector2(grow, grow);
            rt.anchoredPosition = Vector2.zero;
        }

        private static void Section(Text text)
        {
            if (text == null) return;
            Plain(text);
            text.fontSize = SectionSize;
            text.fontStyle = FontStyle.Bold;
            text.color = WardrobeLook.Label;
            Done.Add(text.GetInstanceID());
        }

        private static void Plain(Text text)
        {
            text.font = FlaskPicker.Font();
            foreach (var effect in text.GetComponents<UnityEngine.UI.BaseMeshEffect>())
                if (effect != null) effect.enabled = false;
        }

        private static void Texts(ConfirmActionDialog view)
        {
            var captions = Captions();
            foreach (var text in view.GetComponentsInChildren<Text>(true))
            {
                if (text == null || !Done.Add(text.GetInstanceID())) continue;
                Plain(text);
                string name = text.gameObject.name;
                if (name == "ActionName")
                {
                    text.supportRichText = true;
                    text.fontSize = HeadSize;
                    text.fontStyle = FontStyle.Bold;
                    text.color = WardrobeLook.Bright;
                    continue;
                }
                if (name == "QoLArmorNum")
                {
                    text.fontSize = CaptionSize;
                    text.fontStyle = FontStyle.Bold;
                    continue;
                }
                if (captions.Contains(text))
                {
                    text.fontSize = CaptionSize;
                    text.fontStyle = FontStyle.Normal;
                    text.color = WardrobeLook.Body;
                    continue;
                }
                text.color = WardrobeLook.Body;
            }
        }

        private static readonly List<Text> CaptionList = new List<Text>();

        private static List<Text> Captions()
        {
            CaptionList.Clear();
            if (_body == null) return CaptionList;
            Add(_blocksField?.GetValue(_body) as AttackDialogRow[]);
            Add(_kicksField?.GetValue(_body) as AttackDialogRow[]);
            return CaptionList;
        }

        private static void Add(AttackDialogRow[] rows)
        {
            if (rows == null) return;
            foreach (var row in rows)
            {
                var caption = row != null ? _captionField?.GetValue(row) as Text : null;
                if (caption != null) CaptionList.Add(caption);
            }
        }
    }
}
