using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ChatFeed
    {
        private const float Band = 2f;
        private const float Edge = 0.5f;
        private const int Most = 14000;
        private const int Remembered = 4000;
        private const string Probe = "Ё";

        private static readonly Dictionary<string, int> Rows = new Dictionary<string, int>();
        private static readonly List<int> Sums = new List<int>();
        private static readonly StringBuilder Pen = new StringBuilder();

        private static ChatPanelContent _panel;
        private static TMP_Text _body;
        private static ScrollRect _scroll;
        private static ContentSizeFitter _fit;
        private static Vector4 _margin;
        private static TextAlignmentOptions _align;
        private static bool _own;

        private static float _wide = -1f, _size = -1f, _base, _step, _tail;
        private static int _from, _to, _count;
        private static ITextScrollerContent _last;
        private static float _top, _bottom;
        private static float _checkAt;
        private static float _stuckAt = -1f;

        internal static string Text(ChatPanelContent panel, IList<ITextScrollerContent> all)
        {
            if (!Bind(panel)) return null;
            _body.margin = _margin;
            float wide = _body.rectTransform.rect.width;
            if (wide <= 1f) return null;
            Measure(wide);

            int count = all.Count;
            Sums.Clear();
            Sums.Add(0);
            for (int i = 0; i < count; i++) Sums.Add(Sums[i] + Count(all[i]));
            int lines = Sums[count];
            float total = lines > 0 ? (lines - 1) * _step + _base + _tail : 0f;

            float view = Window();
            float end = Mathf.Max(0f, total - view);
            float at = ChatStay.Held ? Mathf.Clamp(ChatStay.Mark, 0f, end) : end;
            float low = at - view * Band, high = at + view * (1f + Band);
            int from = 0;
            while (from < count && Sums[from + 1] * _step <= low) from++;
            int to = from;
            while (to < count && Sums[to] * _step < high) to++;

            int letters = 0;
            for (int i = from; i < to; i++) letters += Size(all[i]);
            while (letters > Most && to - from > 1)
            {
                if (at - Sums[from] * _step > Sums[to] * _step - (at + view)) letters -= Size(all[from++]);
                else letters -= Size(all[--to]);
            }

            Pen.Length = 0;
            for (int i = from; i < to; i++)
            {
                if (all[i] == null) continue;
                Pen.Append(all[i].GetText());
                Pen.Append(Environment.NewLine);
            }

            _from = from;
            _to = to;
            _count = count;
            _last = count > 0 ? all[count - 1] : null;
            _top = Sums[from] * _step;
            _bottom = Sums[to] * _step;
            var margin = _margin;
            margin.y += _top;
            _body.margin = margin;
            _body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, total + _margin.y + _margin.w);
            return Pen.ToString();
        }

        internal static void Release(ChatPanelContent panel)
        {
            if (!_own || panel == null || !ReferenceEquals(panel, _panel)) return;
            _own = false;
            try
            {
                if (_body != null) { _body.margin = _margin; _body.alignment = _align; }
                if (_fit != null) _fit.enabled = true;
            }
            catch (Exception e) { Plugin.Trace("[chat] feed release: " + e.Message); }
        }

        internal static void Watch(ChatPanelContent view)
        {
            if (!_own || view == null || !ReferenceEquals(view, _panel) || _scroll == null) return;
            if (Time.unscaledTime < _checkAt) return;
            _checkAt = Time.unscaledTime + 0.05f;
            try
            {
                if (_body != null && Mathf.Abs(_body.rectTransform.rect.width - _wide) >= 0.5f) { _stuckAt = -1f; ChatDock.Redraw(); return; }
                float window = Window();
                float at = ChatStay.Top(_scroll);
                bool up = _from > 0 && at - window * Edge < _top;
                var holder = ChatStay.Holder(_panel);
                var all = holder != null ? holder.Content : null;
                bool stale = all != null && (all.Count != _count || (all.Count > 0 && !ReferenceEquals(all[all.Count - 1], _last)));
                bool down = (_to < _count || stale) && at + window * (1f + Edge) > _bottom;
                if (!up && !down) { _stuckAt = -1f; return; }
                if (_stuckAt >= 0f && Mathf.Abs(at - _stuckAt) < window * 0.25f) return;
                int from = _from, to = _to;
                ChatDock.Redraw();
                _stuckAt = from == _from && to == _to ? at : -1f;
            }
            catch (Exception e) { Plugin.Trace("[chat] feed scroll: " + e.Message); }
        }

        private static bool Bind(ChatPanelContent panel)
        {
            if (panel == null || !ReferenceEquals(panel, ChatDock.View)) return false;
            if (!ReferenceEquals(panel, _panel) || _body == null || _scroll == null)
            {
                _own = false;
                _panel = panel;
                _body = ChatStay.Body(panel);
                _scroll = ChatStay.Of(panel);
                if (_body == null || _scroll == null) return false;
                _fit = _body.GetComponent<ContentSizeFitter>();
                _margin = _body.margin;
                _align = _body.alignment;
                _wide = -1f;
            }
            if (!_own)
            {
                _own = true;
                if (_fit != null) _fit.enabled = false;
                _body.alignment = (TextAlignmentOptions)(((int)_align & 0xFF) | 0x100);
            }
            return true;
        }

        private static void Measure(float wide)
        {
            if (Mathf.Abs(wide - _wide) < 0.5f && Mathf.Abs(_body.fontSize - _size) < 0.01f) return;
            Rows.Clear();
            _wide = wide;
            _size = _body.fontSize;
            _base = Height(Probe);
            _step = Height(Probe + "\n" + Probe) - _base;
            if (_step <= 0.1f) _step = Mathf.Max(1f, _base);
            _tail = Mathf.Max(0f, Height(Probe + Environment.NewLine) - _base);
        }

        private static float Height(string text) => _body.GetPreferredValues(text, _wide, 0f).y;

        private static int Count(ITextScrollerContent item)
        {
            if (item == null) return 0;
            string text = item.GetText();
            if (string.IsNullOrEmpty(text)) return 1;
            if (Rows.TryGetValue(text, out int rows)) return rows;
            if (Rows.Count > Remembered) Rows.Clear();
            rows = Mathf.Max(1, Mathf.RoundToInt((Height(text) - _base) / _step) + 1);
            Rows[text] = rows;
            return rows;
        }

        private static int Size(ITextScrollerContent item)
        {
            string text = item != null ? item.GetText() : null;
            return text != null ? text.Length : 0;
        }

        private static float Window()
        {
            var window = _scroll.viewport != null ? _scroll.viewport : _scroll.transform as RectTransform;
            return window != null ? Mathf.Max(1f, window.rect.height) : 1f;
        }
    }
}
