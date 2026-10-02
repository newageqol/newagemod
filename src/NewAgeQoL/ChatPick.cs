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
        internal static ScrollRect Stuck;

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
        private string _picked = "";
        private string _seen;
        private bool _check;
        private GameObject _menu;
        private bool _fight;

        internal static void Attach(Component view)
        {
            try
            {
                if (view == null) return;
                var body = AccessTools.Field(view.GetType(), "Text")?.GetValue(view) as Component;
                var text = body as TMP_Text;
                if (text == null) { Plugin.Trace("[chat] selection: text field not found"); return; }
                var handler = AccessTools.Field(view.GetType(), "ClickHandler")?.GetValue(view) as Component;
                var host = text.raycastTarget || handler == null ? text.gameObject : handler.gameObject;
                var pick = host.GetComponent<ChatPick>();
                if (pick == null) pick = host.AddComponent<ChatPick>();
                pick._text = text;
                pick._rt = text.rectTransform;
                pick._seen = text.text;
                pick._fight = SideButtons.InCombat();
                Stuck = AccessTools.Field(view.GetType(), "ScrollRect")?.GetValue(view) as ScrollRect;
                if (Stuck == null) Plugin.Trace("[chat] scroll rect not found, drag scrolling stays on");
                _live = pick;
                Plugin.Trace("[chat] selection enabled on " + host.name);
            }
            catch (Exception e) { Plugin.Trace("[chat] selection: " + e.Message); }
        }

        internal static void Forget()
        {
            var pick = _live;
            _live = null;
            Stuck = null;
            if (pick == null) return;
            try { pick.Clear(); pick.Shut(); }
            catch (Exception e) { Plugin.Trace("[chat] selection forget: " + e.Message); }
        }

        internal static void Drop()
        {
            if (_live != null) _live.Clear();
        }

        internal static void Tick()
        {
            var pick = _live;
            if (pick == null || pick._text == null) return;
            try
            {
                pick.Follow();
                if (pick._menu != null && Input.GetKeyDown(KeyCode.Escape)) pick.Shut();
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (!ctrl) return;
                if (Input.GetKeyDown(KeyCode.C)) pick.Copy();
            }
            catch (Exception e) { Plugin.Trace("[chat] copy: " + e.Message); }
        }

        private void OnDisable()
        {
            Shut();
        }

        private void OnDestroy()
        {
            Shut();
            if (_live == this) _live = null;
        }

        private void Follow()
        {
            bool fight = SideButtons.InCombat();
            if (fight != _fight)
            {
                _fight = fight;
                Shut();
                if (_from >= 0) { Clear(); Plugin.Trace("[chat] selection dropped: combat " + (fight ? "started" : "ended")); }
            }
            string now = _text.text;
            if (!ReferenceEquals(now, _seen))
            {
                _seen = now;
                _check = true;
                return;
            }
            if (!_check) return;
            _check = false;
            if (_held || _from < 0 || _from == _to) return;
            if (Grab() == _picked) { Paint(); return; }
            int moved = Find(Mathf.Min(_from, _to));
            if (moved >= 0)
            {
                int span = Mathf.Abs(_to - _from);
                _from = moved;
                _to = moved + span;
                if (Grab() == _picked) { Paint(); return; }
            }
            Clear();
            Plugin.Trace("[chat] selection dropped: text changed under it");
        }

        private int Find(int near)
        {
            var info = _text.textInfo;
            if (info == null || _picked.Length == 0) return -1;
            var want = _picked.Replace("\n", "");
            if (want.Length == 0) return -1;
            int best = -1;
            for (int s = 0; s < info.characterCount; s++)
            {
                if (info.characterInfo[s].character != want[0]) continue;
                int i = s, k = 0;
                while (i < info.characterCount && k < want.Length)
                {
                    char c = info.characterInfo[i].character;
                    if (Breaks(c)) { i++; continue; }
                    if (c != want[k]) break;
                    i++; k++;
                }
                if (k < want.Length) continue;
                if (best < 0 || Mathf.Abs(s - near) < Mathf.Abs(best - near)) best = s;
            }
            return best;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            Shut();
            _from = Spot(e.position, e.pressEventCamera);
            _to = _from;
            _picked = "";
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
            _to = Spot(e.position, e.pressEventCamera);
            Paint();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e == null || e.button != PointerEventData.InputButton.Left) return;
            _held = false;
            _picked = Grab();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e == null) return;
            try
            {
                if (e.button == PointerEventData.InputButton.Right) { Menu(e); return; }
                if (e.button != PointerEventData.InputButton.Left) return;
                if (e.clickCount < 2 || _moved) return;
                Whole(Spot(e.position, e.pressEventCamera));
            }
            catch (Exception err) { Plugin.Trace("[chat] click: " + err.Message); }
        }

        private static bool Breaks(char one)
        {
            return one == (char)10 || one == (char)13;
        }

        private bool Line(int at, out int a, out int b)
        {
            a = b = -1;
            var info = _text != null ? _text.textInfo : null;
            if (info == null || info.characterCount <= 0 || at < 0) return false;
            int last = info.characterCount - 1;
            at = Mathf.Clamp(at, 0, last);
            while (at > 0 && Breaks(info.characterInfo[at].character)) at--;
            if (Breaks(info.characterInfo[at].character)) return false;
            a = at;
            b = at;
            while (a > 0 && !Breaks(info.characterInfo[a - 1].character)) a--;
            while (b < last && !Breaks(info.characterInfo[b + 1].character)) b++;
            return true;
        }

        private void Whole(int at)
        {
            if (!Line(at, out int a, out int b)) return;
            _from = a;
            _to = b + 1;
            _picked = Grab();
            Paint();
            Plugin.Trace("[chat] line selected by double click, chars " + (b - a + 1));
        }

        private int Spot(Vector2 screen, Camera cam)
        {
            try
            {
                var info = _text.textInfo;
                if (info == null || info.characterCount <= 0 || info.lineCount <= 0) return -1;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, screen, cam, out var local)) return -1;

                int lines = Mathf.Min(info.lineCount, info.lineInfo.Length);
                int row = lines - 1;
                for (int l = 0; l < lines - 1; l++)
                {
                    float edge = (info.lineInfo[l].descender + info.lineInfo[l + 1].ascender) * 0.5f;
                    if (local.y >= edge) { row = l; break; }
                }

                var li = info.lineInfo[row];
                int first = Mathf.Clamp(li.firstCharacterIndex, 0, info.characterCount - 1);
                int end = Mathf.Clamp(li.lastCharacterIndex, first, info.characterCount - 1);
                while (end > first && Breaks(info.characterInfo[end].character)) end--;
                if (Breaks(info.characterInfo[end].character)) return end;

                for (int i = first; i <= end; i++)
                {
                    var ch = info.characterInfo[i];
                    float mid = (ch.origin + ch.xAdvance) * 0.5f;
                    if (local.x < mid) return i;
                }
                return end + 1;
            }
            catch { return -1; }
        }

        private void Clear()
        {
            _from = -1;
            _to = -1;
            _picked = "";
            _held = false;
            Wipe();
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

        private bool Range(out int a, out int b)
        {
            a = Mathf.Min(_from, _to);
            b = Mathf.Max(_from, _to) - 1;
            var info = _text != null ? _text.textInfo : null;
            if (info == null || info.characterCount <= 0 || a < 0 || b < a) return false;
            if (a >= info.characterCount) return false;
            b = Mathf.Min(b, info.characterCount - 1);
            return true;
        }

        private void Paint()
        {
            Wipe();
            if (!Range(out int a, out int b)) return;
            var info = _text.textInfo;

            var box = _rt.rect;
            int used = 0;
            int line = -1;
            float left = 0f, right = 0f, low = 0f, high = 0f;
            for (int i = a; i <= b; i++)
            {
                var ch = info.characterInfo[i];
                if (Breaks(ch.character)) continue;
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

        private string Grab()
        {
            if (!Range(out int a, out int b)) return "";
            return Grab(a, b);
        }

        private string Grab(int a, int b)
        {
            var info = _text.textInfo;
            var made = new StringBuilder();
            int line = -1;
            for (int i = a; i <= b; i++)
            {
                var ch = info.characterInfo[i];
                if (Breaks(ch.character)) continue;
                if (line >= 0 && ch.lineNumber != line) made.Append('\n');
                line = ch.lineNumber;
                made.Append(ch.character);
            }
            return made.ToString();
        }

        private void Copy()
        {
            string all = Grab();
            if (all.Length == 0) return;
            Put(all);
            Clear();
        }

        private static void Put(string all)
        {
            if (string.IsNullOrEmpty(all)) return;
            GUIUtility.systemCopyBuffer = all;
            Plugin.Trace("[chat] chars copied: " + all.Length);
        }

        private void Menu(PointerEventData e)
        {
            Shut();
            var cam = e.pressEventCamera;
            if (TMP_TextUtilities.FindIntersectingLink(_text, e.position, cam) >= 0) return;

            string what = Grab();
            bool picked = what.Length > 0;
            if (what.Length == 0)
            {
                int at = Spot(e.position, cam);
                if (at >= _text.textInfo.characterCount) at = _text.textInfo.characterCount - 1;
                if (!Line(at, out int a, out int b)) return;
                what = Grab(a, b).Replace("\n", "");
            }
            if (what.Length == 0) return;

            var canvas = _text.canvas != null ? _text.canvas.rootCanvas : null;
            if (canvas == null) return;
            var top = (RectTransform)canvas.transform;

            _menu = new GameObject("chatcopy", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            _menu.transform.SetParent(top, false);
            var own = _menu.GetComponent<Canvas>();
            own.overrideSorting = true;
            own.sortingOrder = 32000;
            var cover = (RectTransform)_menu.transform;
            cover.anchorMin = Vector2.zero;
            cover.anchorMax = Vector2.one;
            cover.offsetMin = cover.offsetMax = Vector2.zero;
            var shade = _menu.GetComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0f);
            var away = _menu.AddComponent<Button>();
            away.transition = Selectable.Transition.None;
            away.onClick.AddListener(Shut);

            var view = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(cover, e.position, view, out var spot);
            Sheet(cover, spot, what, picked);
        }

        private void Sheet(RectTransform cover, Vector2 spot, string what, bool picked)
        {
            const float Wide = 196f, Row = 30f, Pad = 4f;

            var pad = new GameObject("sheet", typeof(RectTransform), typeof(Image));
            pad.transform.SetParent(cover, false);
            var prt = (RectTransform)pad.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0f, 1f);
            prt.sizeDelta = new Vector2(Wide, Row + Pad * 2f);
            var room = cover.rect;
            spot += new Vector2(2f, -2f);
            spot.x = Mathf.Min(spot.x, room.xMax - prt.sizeDelta.x - 4f);
            spot.y = Mathf.Max(spot.y, room.yMin + prt.sizeDelta.y + 4f);
            prt.anchoredPosition = spot;
            pad.GetComponent<Image>().color = WardrobeLook.Popup;
            WardrobeLook.Frame(pad, WardrobeLook.Edge);

            var row = new GameObject("copy", typeof(RectTransform), typeof(Image), typeof(Button));
            row.transform.SetParent(pad.transform, false);
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = Vector2.zero;
            rrt.anchorMax = Vector2.one;
            rrt.offsetMin = new Vector2(Pad, Pad);
            rrt.offsetMax = new Vector2(-Pad, -Pad);
            var face = row.GetComponent<Image>();
            face.color = Color.white;
            var press = row.GetComponent<Button>();
            var tint = press.colors;
            tint.normalColor = WardrobeLook.Popup;
            tint.highlightedColor = WardrobeLook.Mix(WardrobeLook.Popup, WardrobeLook.Accent, 0.2f);
            tint.selectedColor = tint.highlightedColor;
            tint.pressedColor = WardrobeLook.Mix(WardrobeLook.Popup, WardrobeLook.Accent, 0.34f);
            tint.colorMultiplier = 1f;
            tint.fadeDuration = 0f;
            press.colors = tint;
            press.targetGraphic = face;
            face.canvasRenderer.SetColor(tint.normalColor);
            string keep = what;
            press.onClick.AddListener(() => { Put(keep); Clear(); Shut(); });

            var strip = new GameObject("mark", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(row.transform, false);
            var srt = (RectTransform)strip.transform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(2f, Row - 12f);
            srt.anchoredPosition = new Vector2(8f, 0f);
            var mark = strip.GetComponent<Image>();
            mark.color = WardrobeLook.Accent;
            mark.raycastTarget = false;

            Word(row.transform, picked ? "Копировать" : "Копировать сообщение", WardrobeLook.Bright, TextAlignmentOptions.MidlineLeft, new Vector2(18f, 0f), new Vector2(-10f, 0f), 14f);
            if (picked) Word(row.transform, "Ctrl+C", WardrobeLook.Faint, TextAlignmentOptions.MidlineRight, new Vector2(18f, 0f), new Vector2(-10f, 0f), 12f);
        }

        private void Word(Transform host, string what, Color color, TextAlignmentOptions align, Vector2 from, Vector2 to, float size)
        {
            var word = new GameObject("label", typeof(RectTransform), typeof(TextMeshProUGUI));
            word.transform.SetParent(host, false);
            var wrt = (RectTransform)word.transform;
            wrt.anchorMin = Vector2.zero;
            wrt.anchorMax = Vector2.one;
            wrt.offsetMin = from;
            wrt.offsetMax = to;
            var label = word.GetComponent<TextMeshProUGUI>();
            label.font = _text.font;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.text = what;
        }

        private void Shut()
        {
            if (_menu == null) return;
            var gone = _menu;
            _menu = null;
            UnityEngine.Object.Destroy(gone);
        }
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnBeginDrag))]
    internal static class ChatNoDragBegin
    {
        private static bool Prefix(ScrollRect __instance) => __instance == null || __instance != ChatPick.Stuck;
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnDrag))]
    internal static class ChatNoDragMove
    {
        private static bool Prefix(ScrollRect __instance) => __instance == null || __instance != ChatPick.Stuck;
    }

    [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnEndDrag))]
    internal static class ChatNoDragEnd
    {
        private static bool Prefix(ScrollRect __instance) => __instance == null || __instance != ChatPick.Stuck;
    }
}
