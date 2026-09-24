using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WardrobeCompare
    {
        private static readonly Color Gold = WardrobeLook.Accent;
        private static readonly Color Ink = WardrobeLook.Bright;
        private static readonly Color Soft = WardrobeLook.Label;
        private static readonly Color Dim = WardrobeLook.Faint;
        private static readonly Color Up = WardrobeLook.Good;
        private static readonly Color Down = WardrobeLook.Bad;

        private static GameObject _go;
        private static string _otherKey;
        private static float _w, _h;

        internal static bool IsOpen => _go != null;

        internal static void Open(RectTransform panel, float x, float y, float w, float h)
        {
            Close();
            if (panel == null) return;
            _w = w;
            _h = h;
            _otherKey = null;
            _go = new GameObject("QoLWardrobeCompare", typeof(RectTransform), typeof(Image), typeof(Outline));
            _go.transform.SetParent(panel, false);
            Wardrobe.At((RectTransform)_go.transform, x, y, w, h);
            var back = _go.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(10);
            back.type = Image.Type.Sliced;
            var edge = _go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            Paint();
        }

        internal static void Refresh()
        {
            if (_go != null) Paint();
        }

        internal static void Close()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
        }

        internal static bool EscapeClose()
        {
            if (_go == null) return false;
            Close();
            return true;
        }

        private static void Paint()
        {
            var rt = (RectTransform)_go.transform;
            for (int i = rt.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(rt.GetChild(i).gameObject);
            var mine = WardrobeStore.Active;
            WardrobeManikin other = null;
            foreach (var m in WardrobeStore.All)
                if (m.Key == _otherKey && (mine == null || m.Key != mine.Key)) other = m;

            var title = Write(rt, "Сравнение манекенов", 18, FontStyle.Bold, Gold);
            Wardrobe.At(title.rectTransform, 16f, 10f, _w - 260f, 30f);
            var close = Wardrobe.Arrow(rt, "×", Close);
            Wardrobe.At((RectTransform)close.transform, _w - 44f, 10f, 34f, 30f);
            if (other == null) { Choose(rt, mine); return; }
            var again = Wardrobe.Arrow(rt, "Другой манекен", () => { _otherKey = null; Paint(); });
            Wardrobe.At((RectTransform)again.transform, _w - 44f - 8f - 170f, 10f, 170f, 30f);

            var b = new WardrobeState();
            b.Unpack(other.Body);
            string nameA = mine != null ? mine.Title : "открытый";
            string nameB = other.Title;
            Numbers(rt, Wardrobe.S, b, nameA, nameB, 16f, 52f, _w - 32f, _h - 62f);
        }

        private static void Choose(RectTransform rt, WardrobeManikin mine)
        {
            var ask = Write(rt, "С каким манекеном сравнить «" + (mine != null ? mine.Title : "открытый") + "»?", 15, FontStyle.Normal, Ink);
            Wardrobe.At(ask.rectTransform, 16f, 52f, _w - 32f, 28f);
            var list = new List<WardrobeManikin>();
            foreach (var m in WardrobeStore.All) if (!m.Received && (mine == null || m.Key != mine.Key)) list.Add(m);
            foreach (var m in WardrobeStore.All) if (m.Received && (mine == null || m.Key != mine.Key)) list.Add(m);
            if (list.Count == 0)
            {
                var none = Write(rt, "Других манекенов нет — создай ещё один кнопкой «+ Новый».", 14, FontStyle.Italic, Dim);
                Wardrobe.At(none.rectTransform, 16f, 90f, _w - 32f, 28f);
                return;
            }
            const int PerRow = 3;
            float bw = (_w - 32f - (PerRow - 1) * 10f) / PerRow;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                string key = m.Key;
                string label = m.Title + (m.Received && !string.IsNullOrEmpty(m.From) ? " (от " + m.From + ")" : "");
                var button = Wardrobe.Arrow(rt, label, () => { _otherKey = key; Paint(); });
                Wardrobe.At((RectTransform)button.transform, 16f + (i % PerRow) * (bw + 10f), 90f + (i / PerRow) * 48f, bw, 40f);
                if (m.Received) button.GetComponent<Image>().color = WardrobeLook.TabReceived;
                var text = button.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.fontSize = 15;
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = 10;
                    text.resizeTextMaxSize = 15;
                }
            }
        }

        private static void Numbers(RectTransform rt, WardrobeState a, WardrobeState b, string nameA, string nameB, float x, float y, float w, float h)
        {
            const int Lines = 27;
            float r = Mathf.Min(26f, Mathf.Floor(h / Lines));
            int size = r >= 24f ? 14 : 13;
            float nameW = w * 0.34f;
            float colW = w * 0.24f;
            float deltaX = x + nameW + colW * 2f;
            float deltaW = w - nameW - colW * 2f;
            Column(rt, nameA, x + nameW, y, colW, r, size);
            Column(rt, nameB, x + nameW + colW, y, colW, r, size);
            var diff = Write(rt, "разница", size - 1, FontStyle.Italic, Dim);
            Wardrobe.At(diff.rectTransform, deltaX, y, deltaW, r);
            diff.alignment = TextAnchor.MiddleRight;
            float at = y + r;
            int n = 0;

            Line(rt, "Раса", RaceOf(a), RaceOf(b), x, ref at, w, nameW, colW, r, size, ref n);
            Line(rt, "Уровень", a.Level.ToString(), b.Level.ToString(), x, ref at, w, nameW, colW, r, size, ref n);
            Line(rt, "Класс", ClassOf(a), ClassOf(b), x, ref at, w, nameW, colW, r, size, ref n);
            Line(rt, "Крепость", a.RankInfo != null ? a.RankInfo.Name : "нет", b.RankInfo != null ? b.RankInfo.Name : "нет", x, ref at, w, nameW, colW, r, size, ref n);

            Section(rt, "Итог", x, ref at, w, r, size);
            Value(rt, "Рейтинг", a.Rating, b.Rating, x, ref at, w, nameW, colW, r, size, ref n);
            Value(rt, "Жизнь", a.Life, b.Life, x, ref at, w, nameW, colW, r, size, ref n);
            Value(rt, "Мана", a.Mana, b.Mana, x, ref at, w, nameW, colW, r, size, ref n);
            Value(rt, "Энергия", a.Energy, b.Energy, x, ref at, w, nameW, colW, r, size, ref n);

            Section(rt, "Характеристики", x, ref at, w, r, size);
            foreach (int i in WardrobeData.StatOrder)
                Value(rt, WardrobeData.StatNames[i], a.Total(i), b.Total(i), x, ref at, w, nameW, colW, r, size, ref n);

            Section(rt, "Броня", x, ref at, w, r, size);
            for (int p = 0; p < 5; p++)
                Value(rt, WardrobeData.ArmorNames[p], a.Armor(p), b.Armor(p), x, ref at, w, nameW, colW, r, size, ref n);

            Section(rt, "Защита от магии", x, ref at, w, r, size);
            for (int m = 0; m < 3; m++)
                Value(rt, WardrobeData.MagicNames[m], a.Magic(m), b.Magic(m), x, ref at, w, nameW, colW, r, size, ref n);
        }

        private static string RaceOf(WardrobeState s)
        {
            var race = s.Race;
            return race != null ? race.Name : "?";
        }

        private static string ClassOf(WardrobeState s)
        {
            var klass = s.Klass;
            var sub = s.Sub;
            return (klass != null ? klass.Name : "?") + (sub != null ? " / " + sub.Name : "");
        }

        private static void Column(RectTransform rt, string name, float x, float y, float w, float r, int size)
        {
            var text = Write(rt, "«" + name + "»", size, FontStyle.Bold, Gold);
            Wardrobe.At(text.rectTransform, x, y, w - 6f, r);
            Fit(text, size);
        }

        private static void Section(RectTransform rt, string name, float x, ref float at, float w, float r, int size)
        {
            var text = Write(rt, name, size, FontStyle.Bold, Gold);
            Wardrobe.At(text.rectTransform, x, at, w, r);
            at += r;
        }

        private static void Line(RectTransform rt, string name, string a, string b, float x, ref float at, float w, float nameW, float colW, float r, int size, ref int n)
        {
            Band(rt, x, at, w, r, n++);
            var label = Write(rt, name, size, FontStyle.Normal, Soft);
            Wardrobe.At(label.rectTransform, x + 8f, at, nameW - 8f, r);
            var one = Write(rt, a, size, FontStyle.Normal, Ink);
            Wardrobe.At(one.rectTransform, x + nameW, at, colW - 6f, r);
            Fit(one, size);
            var two = Write(rt, b, size, a == b ? FontStyle.Normal : FontStyle.Bold, a == b ? Ink : Gold);
            Wardrobe.At(two.rectTransform, x + nameW + colW, at, colW - 6f, r);
            Fit(two, size);
            at += r;
        }

        private static void Value(RectTransform rt, string name, int a, int b, float x, ref float at, float w, float nameW, float colW, float r, int size, ref int n)
        {
            Band(rt, x, at, w, r, n++);
            var label = Write(rt, name, size, FontStyle.Normal, Soft);
            Wardrobe.At(label.rectTransform, x + 8f, at, nameW - 8f, r);
            var one = Write(rt, a.ToString(), size, FontStyle.Normal, Ink);
            Wardrobe.At(one.rectTransform, x + nameW, at, colW - 6f, r);
            var two = Write(rt, b.ToString(), size, FontStyle.Bold, Ink);
            Wardrobe.At(two.rectTransform, x + nameW + colW, at, colW - 6f, r);
            int delta = b - a;
            float deltaX = x + nameW + colW * 2f;
            var diff = Write(rt, delta == 0 ? "=" : (delta > 0 ? "+" + delta : delta.ToString()), size, FontStyle.Bold, delta > 0 ? Up : delta < 0 ? Down : Dim);
            Wardrobe.At(diff.rectTransform, deltaX, at, x + w - deltaX - 8f, r);
            diff.alignment = TextAnchor.MiddleRight;
            at += r;
        }

        private static void Band(RectTransform rt, float x, float y, float w, float r, int n)
        {
            var band = Wardrobe.Box(rt, "band", x, y + 1f, w, r - 2f, WardrobeLook.Stripe(n), 6);
            band.GetComponent<Image>().raycastTarget = false;
        }

        private static Text Write(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(parent, text, size, style, color);
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        private static void Fit(Text text, int size)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 9;
            text.resizeTextMaxSize = size;
        }
    }
}
