using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class HexRange
    {
        private static readonly Color Tint = new Color32(150, 220, 255, 255);

        private static GameObject _root;
        private static RectTransform _tag;
        private static Text _text;

        internal static void Tick()
        {
            int far = -1;
            try { far = Far(); }
            catch (Exception e) { Plugin.Trace("[hexrange] " + e.Message); }
            if (far < 0) { Hide(); return; }
            Show(far);
        }

        private static int Far()
        {
            if (!SideButtons.InCombat() || BodyClick.OverUi()) return -1;
            var cd = FighterHint.Cd();
            var me = cd != null ? cd.MyCharacter : null;
            if (me == null || me.HexGridPosition == null) return -1;
            if (!SkillList.HexUnder(out var hex) || hex == null) return -1;
            var cells = cd.Cells;
            if (cells == null) return -1;
            int x = hex.clientX, y = hex.clientY;
            if (x < 0 || y < 0 || x >= cells.GetLength(0) || y >= cells.GetLength(1)) return -1;
            var cell = cells[x, y];
            if (cell == null || cell.occupiedBy != null) return -1;
            if (FighterHint.OnHex(cd, false) != null) return -1;
            return HexUtils.range(me.HexGridPosition, hex);
        }

        private static void Show(int far)
        {
            if (_root == null) Build();
            if (_root == null) return;
            string line = far.ToString();
            if (_text.text != line) _text.text = line;
            int size = Mathf.Max(11, Mathf.RoundToInt(Screen.height / 72f));
            if (_text.fontSize != size) _text.fontSize = size;
            var mouse = (Vector2)Input.mousePosition;
            _tag.anchoredPosition = new Vector2(mouse.x + size * 0.6f, mouse.y + 2f);
            if (!_root.activeSelf) _root.SetActive(true);
        }

        private static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private static void Build()
        {
            _root = new GameObject("QoLHexRange", typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 689;

            var go = new GameObject("value", typeof(RectTransform), typeof(Text), typeof(Outline));
            _tag = (RectTransform)go.transform;
            _tag.SetParent(_root.transform, false);
            _tag.anchorMin = _tag.anchorMax = Vector2.zero;
            _tag.pivot = new Vector2(0.5f, 0f);
            _tag.sizeDelta = new Vector2(80f, 30f);
            _text = go.GetComponent<Text>();
            _text.font = FlaskPicker.Font();
            _text.fontStyle = FontStyle.Bold;
            _text.color = Tint;
            _text.alignment = TextAnchor.LowerCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.supportRichText = false;
            _text.raycastTarget = false;
            var shade = go.GetComponent<Outline>();
            shade.effectColor = new Color(0f, 0f, 0f, 0.95f);
            shade.effectDistance = new Vector2(1.6f, -1.6f);
            _root.SetActive(false);
        }
    }
}
