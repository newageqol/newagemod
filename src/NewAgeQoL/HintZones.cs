using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class HintZones
    {
        private const float Line = 2f;
        private const int Layer = 660;

        private sealed class Mark
        {
            internal GameObject Go;
            internal RectTransform Rt;
            internal Image Soft;
            internal readonly Image[] Edges = new Image[4];
            internal Text Name;
        }

        private static readonly List<Mark> Marks = new List<Mark>();
        private static GameObject _canvasGo;
        private static RectTransform _root;

        internal static bool On;

        internal static void Toggle()
        {
            On = !On;
            if (!On) Drop();
            Plugin.Log?.LogInfo("[зоны] показ зон наведения " + (On ? "включён" : "выключен"));
        }

        internal static void Tick()
        {
            try
            {
                if (!On || !SideButtons.InCombat() || Spectate.Peeking) { Drop(); return; }
                var cd = FighterHint.Cd();
                var camera = FighterHint.Eye();
                var all = cd != null ? cd.Characters : null;
                if (camera == null || all == null) { Drop(); return; }

                if (_root == null) Build();
                if (_root == null) return;

                float limit = FighterHint.Limit(FighterHint.Slack);
                int picked = FighterHint.Under(cd, false, FighterHint.Slack);

                int used = 0;
                foreach (var pair in all)
                {
                    var ch = pair.Value;
                    if (ch == null || ch.UserId == 0) continue;
                    Rect zone;
                    bool capsule;
                    if (!FighterHint.Zone(camera, ch, out zone, out capsule)) continue;
                    Draw(Seat(used++), ch, zone, limit, ch.UserId == picked, capsule);
                }
                for (int i = used; i < Marks.Count; i++)
                    if (Marks[i].Go.activeSelf) Marks[i].Go.SetActive(false);

                if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[зоны] " + e.Message); }
        }

        private static readonly Color Hot = new Color(0.35f, 1f, 0.45f);
        private static readonly Color Cold = new Color(1f, 0.82f, 0.35f);
        private static readonly Color Skin = new Color(1f, 0.42f, 0.38f);

        private static void Draw(Mark mark, AbstractCharacter ch, Rect zone, float limit, bool picked, bool capsule)
        {
            var tint = !capsule ? Skin : picked ? Hot : Cold;

            mark.Rt.anchoredPosition = new Vector2(zone.xMin, zone.yMin);
            mark.Rt.sizeDelta = new Vector2(zone.width, zone.height);

            var soft = (RectTransform)mark.Soft.transform;
            soft.offsetMin = new Vector2(-limit, -limit);
            soft.offsetMax = new Vector2(limit, limit);
            mark.Soft.sprite = Round(Mathf.RoundToInt(limit));
            mark.Soft.color = new Color(tint.r, tint.g, tint.b, picked ? 0.16f : 0.07f);

            for (int i = 0; i < mark.Edges.Length; i++)
                mark.Edges[i].color = new Color(tint.r, tint.g, tint.b, picked ? 1f : 0.7f);

            if (mark.Name != null)
            {
                string text = ch.Login;
                if (string.IsNullOrEmpty(text)) text = "#" + ch.UserId;
                if (!capsule) text += " — по скину";
                if (mark.Name.text != text) mark.Name.text = text;
                mark.Name.color = tint;
            }

            if (!mark.Go.activeSelf) mark.Go.SetActive(true);
        }

        private static Sprite Round(int radius)
        {
            return OnlineWindow.RoundedExact(Mathf.Clamp(radius, 2, 96));
        }

        private static Mark Seat(int index)
        {
            while (Marks.Count <= index) Marks.Add(Make());
            return Marks[index];
        }

        private static Mark Make()
        {
            var mark = new Mark();
            mark.Go = new GameObject("zone", typeof(RectTransform));
            mark.Go.transform.SetParent(_root, false);
            mark.Rt = (RectTransform)mark.Go.transform;
            mark.Rt.anchorMin = mark.Rt.anchorMax = Vector2.zero;
            mark.Rt.pivot = Vector2.zero;

            var softGo = new GameObject("soft", typeof(RectTransform), typeof(Image));
            softGo.transform.SetParent(mark.Rt, false);
            var srt = (RectTransform)softGo.transform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.pivot = new Vector2(0.5f, 0.5f);
            mark.Soft = softGo.GetComponent<Image>();
            mark.Soft.type = Image.Type.Sliced;
            mark.Soft.raycastTarget = false;

            mark.Edges[0] = Edge(mark.Rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, Line));
            mark.Edges[1] = Edge(mark.Rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, Line));
            mark.Edges[2] = Edge(mark.Rt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(Line, 0f));
            mark.Edges[3] = Edge(mark.Rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(Line, 0f));

            var nameGo = new GameObject("name", typeof(RectTransform), typeof(Text), typeof(Outline));
            nameGo.transform.SetParent(mark.Rt, false);
            var nrt = (RectTransform)nameGo.transform;
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0.5f, 0f);
            nrt.offsetMin = new Vector2(0f, 2f);
            nrt.offsetMax = new Vector2(0f, 18f);
            mark.Name = nameGo.GetComponent<Text>();
            mark.Name.font = SideButtons.GameFont();
            mark.Name.fontSize = 12;
            mark.Name.alignment = TextAnchor.LowerCenter;
            mark.Name.raycastTarget = false;
            mark.Name.horizontalOverflow = HorizontalWrapMode.Overflow;
            mark.Name.verticalOverflow = VerticalWrapMode.Overflow;
            var edge = nameGo.GetComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.95f);
            edge.effectDistance = new Vector2(1.2f, -1.2f);

            mark.Go.SetActive(false);
            return mark;
        }

        private static Image Edge(RectTransform host, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject("edge", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void Build()
        {
            Drop();
            _canvasGo = new GameObject("QoLHintZones", typeof(Canvas));
            _canvasGo.transform.SetParent(null, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = Layer;
            _root = (RectTransform)_canvasGo.transform;
            Marks.Clear();
            Plugin.Trace("[зоны] холст зон собран");
        }

        private static void Drop()
        {
            Marks.Clear();
            _root = null;
            if (_canvasGo == null) return;
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
        }
    }
}
