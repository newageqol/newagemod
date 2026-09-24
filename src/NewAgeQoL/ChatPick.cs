using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal sealed class ChatPick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IPointerClickHandler
    {
        private static ChatPick _live;

        private TMP_Text _text;
        private RectTransform _rt;
        private RectTransform _marks;
        private readonly List<Image> Bars = new List<Image>();
        private const float Slack = 16f;

        private int _from = -1;
        private int _to = -1;
        private bool _held;
        private bool _moved;
        private Vector2 _at;

        internal static void Attach(Component view)
        {
            try
            {
                if (view == null) return;
                var body = AccessTools.Field(view.GetType(), "Text")?.GetValue(view) as Component;
                var text = body as TMP_Text;
                if (text == null) { Plugin.Trace("[чат] выделение: поле текста не найдено"); return; }
                var handler = AccessTools.Field(view.GetType(), "ClickHandler")?.GetValue(view) as Component;
                var host = text.raycastTarget || handler == null ? text.gameObject : handler.gameObject;
                var pick = host.GetComponent<ChatPick>();
                if (pick == null) pick = host.AddComponent<ChatPick>();
                pick._text = text;
                pick._rt = text.rectTransform;
                _live = pick;
                Plugin.Trace("[чат] выделение включено на " + host.name);
            }
            catch (Exception e) { Plugin.Trace("[чат] выделение: " + e.Message); }
        }

        internal static void Forget()
        {
            _live = null;
        }

        internal static void Tick()
        {
            var pick = _live;
            if (pick == null || pick._text == null) return;
            try
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (!ctrl) return;
                if (Input.GetKeyDown(KeyCode.C)) pick.Copy();
            }
            catch (Exception e) { Plugin.Trace("[чат] копирование: " + e.Message); }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            _from = Spot(e.position);
            _to = _from;
            _held = true;
            _moved = false;
            _at = e.position;
            Wipe();
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_held || e == null) return;
            if (!_moved && (e.position - _at).sqrMagnitude < Slack) return;
            _moved = true;
            _to = Spot(e.position);
            Paint();
        }

        public void OnPointerUp(PointerEventData e)
        {
            _held = false;
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            if (e.clickCount < 2 || _moved) return;
            try { Whole(Spot(e.position)); }
            catch (Exception err) { Plugin.Trace("[чат] выделение строки: " + err.Message); }
        }

        private static bool Breaks(char one)
        {
            return one == (char)10 || one == (char)13;
        }

        private void Whole(int at)
        {
            var info = _text != null ? _text.textInfo : null;
            if (info == null || info.characterCount <= 0 || at < 0) return;
            int last = info.characterCount - 1;
            at = Mathf.Clamp(at, 0, last);
            while (at > 0 && Breaks(info.characterInfo[at].character)) at--;
            if (Breaks(info.characterInfo[at].character)) return;

            int a = at, b = at;
            while (a > 0 && !Breaks(info.characterInfo[a - 1].character)) a--;
            while (b < last && !Breaks(info.characterInfo[b + 1].character)) b++;
            if (a >= b) return;
            _from = a;
            _to = b;
            Paint();
            Plugin.Trace("[чат] строка выделена двойным кликом, знаков " + (b - a + 1));
        }

        private int Spot(Vector2 screen)
        {
            try { return TMP_TextUtilities.FindNearestCharacter(_text, screen, null, true); }
            catch { return -1; }
        }

        private void Wipe()
        {
            foreach (var bar in Bars) if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);
        }

        private Image Bar(int index)
        {
            while (Bars.Count <= index)
            {
                var go = new GameObject("pick", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(Host(), false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                var art = go.GetComponent<Image>();
                art.color = new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.3f);
                art.raycastTarget = false;
                Bars.Add(art);
            }
            return Bars[index];
        }

        private Transform Host()
        {
            if (_marks != null) return _marks;
            var go = new GameObject("picks", typeof(RectTransform));
            go.transform.SetParent(_rt, false);
            _marks = (RectTransform)go.transform;
            _marks.anchorMin = _marks.anchorMax = Vector2.zero;
            _marks.pivot = Vector2.zero;
            _marks.anchoredPosition = Vector2.zero;
            _marks.sizeDelta = Vector2.zero;
            return _marks;
        }

        private void Paint()
        {
            Wipe();
            var info = _text != null ? _text.textInfo : null;
            if (info == null || info.characterCount <= 0) return;
            int a = Mathf.Min(_from, _to), b = Mathf.Max(_from, _to);
            if (a < 0 || b < 0 || a == b) return;
            a = Mathf.Clamp(a, 0, info.characterCount - 1);
            b = Mathf.Clamp(b, 0, info.characterCount - 1);

            var box = _rt.rect;
            int used = 0;
            int line = -1;
            float left = 0f, right = 0f, low = 0f, high = 0f;
            for (int i = a; i <= b; i++)
            {
                var ch = info.characterInfo[i];
                if (ch.lineNumber != line)
                {
                    if (line >= 0) used = Draw(used, left, right, low, high, box);
                    line = ch.lineNumber;
                    left = ch.bottomLeft.x; right = ch.topRight.x;
                    low = ch.descender; high = ch.ascender;
                    continue;
                }
                if (ch.bottomLeft.x < left) left = ch.bottomLeft.x;
                if (ch.topRight.x > right) right = ch.topRight.x;
                if (ch.descender < low) low = ch.descender;
                if (ch.ascender > high) high = ch.ascender;
            }
            if (line >= 0) used = Draw(used, left, right, low, high, box);
        }

        private int Draw(int used, float left, float right, float low, float high, Rect box)
        {
            if (right - left < 0.5f) right = left + 4f;
            var bar = Bar(used);
            var rt = (RectTransform)bar.transform;
            rt.anchoredPosition = new Vector2(left - box.xMin, low - box.yMin);
            rt.sizeDelta = new Vector2(right - left, high - low);
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
            return used + 1;
        }

        private void Copy()
        {
            var info = _text != null ? _text.textInfo : null;
            if (info == null || info.characterCount <= 0) return;
            int a = Mathf.Min(_from, _to), b = Mathf.Max(_from, _to);
            if (a < 0 || b < 0 || a == b) return;
            a = Mathf.Clamp(a, 0, info.characterCount - 1);
            b = Mathf.Clamp(b, 0, info.characterCount - 1);

            var made = new StringBuilder();
            int line = -1;
            for (int i = a; i <= b; i++)
            {
                var ch = info.characterInfo[i];
                if (ch.character == '\n' || ch.character == '\r') continue;
                if (line >= 0 && ch.lineNumber != line) made.Append('\n');
                line = ch.lineNumber;
                made.Append(ch.character);
            }
            string all = made.ToString();
            if (all.Length == 0) return;
            GUIUtility.systemCopyBuffer = all;
            Plugin.Trace("[чат] скопировано знаков: " + all.Length);
        }
    }
}
