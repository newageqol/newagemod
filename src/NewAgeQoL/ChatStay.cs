using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ChatStay
    {
        private const float Edge = 0.0005f;

        private static FieldInfo _scroll;
        private static FieldInfo _holder;
        private static FieldInfo _body;
        private static bool _wasBottom;
        private static float _wasTop;
        private static int _turn;
        private static bool _touched;
        private static ScrollRect _watched;
        private static readonly Dictionary<ChatContentHolder, List<ITextScrollerContent>> Seen = new Dictionary<ChatContentHolder, List<ITextScrollerContent>>();
        private static readonly StringBuilder Pen = new StringBuilder();

        internal static bool On
        {
            get { return true; }
        }

        internal static ScrollRect Of(ChatPanelContent panel)
        {
            if (panel == null) return null;
            if (_scroll == null) _scroll = AccessTools.Field(typeof(ChatPanelContent), "ScrollRect");
            return _scroll == null ? null : _scroll.GetValue(panel) as ScrollRect;
        }

        internal static ChatContentHolder Holder(ChatPanelContent panel)
        {
            if (panel == null) return null;
            if (_holder == null) _holder = AccessTools.Field(typeof(ChatPanelContent), "_chatContentHolder");
            return _holder == null ? null : _holder.GetValue(panel) as ChatContentHolder;
        }

        private static TMP_Text Body(ChatPanelContent panel)
        {
            if (panel == null) return null;
            if (_body == null) _body = AccessTools.Field(typeof(ChatPanelContent), "Text");
            return _body == null ? null : _body.GetValue(panel) as TMP_Text;
        }

        internal static void Remember(ChatPanelContent panel)
        {
            var scroll = Of(panel);
            Remember(scroll);
            if (scroll == null || _wasBottom) return;
            float cut = Cut(panel, scroll);
            if (cut == 0f) return;
            _wasTop = Mathf.Max(0f, _wasTop - cut);
        }

        private static void Remember(ScrollRect scroll)
        {
            _turn++;
            _touched = false;
            _wasBottom = true;
            _wasTop = 0f;
            if (scroll == null) return;
            Watch(scroll);
            _wasBottom = AtBottom(scroll);
            _wasTop = FromTop(scroll);
        }

        internal static void Note(ChatPanelContent panel)
        {
            var holder = Holder(panel);
            if (holder == null) return;
            List<ITextScrollerContent> lines;
            if (!Seen.TryGetValue(holder, out lines))
            {
                lines = new List<ITextScrollerContent>();
                Seen[holder] = lines;
            }
            lines.Clear();
            lines.AddRange(ChatDock.Shown(holder));
        }

        private static float Cut(ChatPanelContent panel, ScrollRect scroll)
        {
            try
            {
                var holder = Holder(panel);
                List<ITextScrollerContent> before;
                if (holder == null || !Seen.TryGetValue(holder, out before) || before.Count == 0) return 0f;
                var now = ChatDock.Shown(holder);
                if (now.Count == 0) return 0f;
                int gone = before.IndexOf(now[0]);
                if (gone > 0)
                {
                    int kept = before.Count - gone;
                    if (kept > now.Count || !ReferenceEquals(now[kept - 1], before[before.Count - 1])) return 0f;
                    return Height(panel, scroll, before, gone);
                }
                if (gone == 0) return 0f;
                int added = now.IndexOf(before[0]);
                if (added <= 0) return 0f;
                int last = added + before.Count - 1;
                if (last >= now.Count || !ReferenceEquals(now[last], before[before.Count - 1])) return 0f;
                return -Height(panel, scroll, now, added);
            }
            catch (Exception e) { Plugin.Trace("[чат] срезанные сверху строки: " + e.Message); return 0f; }
        }

        private static float Height(ChatPanelContent panel, ScrollRect scroll, IList<ITextScrollerContent> lines, int count)
        {
            var body = Body(panel);
            if (body == null) return 0f;
            float wide = body.rectTransform.rect.width;
            if (wide <= 1f) return 0f;
            string probe = Join(lines, count, count + 1);
            string lost = Join(lines, 0, count);
            float cut = body.GetPreferredValues(lost + probe, wide, 0f).y - body.GetPreferredValues(probe, wide, 0f).y;
            if (cut <= 0f) return 0f;
            var content = scroll.content;
            float mine = body.rectTransform.lossyScale.y;
            float theirs = content != null ? content.lossyScale.y : 0f;
            return theirs > 0.0001f && mine > 0.0001f ? cut * mine / theirs : cut;
        }

        private static string Join(IList<ITextScrollerContent> lines, int from, int to)
        {
            Pen.Length = 0;
            for (int i = from; i < to && i < lines.Count; i++)
            {
                if (lines[i] == null) continue;
                Pen.Append(lines[i].GetText());
                Pen.Append(Environment.NewLine);
            }
            return Pen.ToString();
        }

        private static void Watch(ScrollRect scroll)
        {
            if (scroll != null && ReferenceEquals(_watched, scroll)) return;
            _watched = scroll;
            if (scroll == null) return;
            Hook(scroll.gameObject);
            if (scroll.verticalScrollbar != null) Hook(scroll.verticalScrollbar.gameObject);
        }

        private static void Hook(GameObject where)
        {
            if (where == null || where.GetComponent<ChatHand>() != null) return;
            where.AddComponent<ChatHand>();
        }

        internal static void Hand()
        {
            _touched = true;
        }

        internal static bool Held
        {
            get { return On && !_wasBottom; }
        }

        internal static bool Touched
        {
            get { return _touched; }
        }

        internal static void Settle()
        {
            _touched = false;
            _wasBottom = true;
            _wasTop = 0f;
        }

        internal static void ToBottom(ScrollRect scroll)
        {
            if (scroll == null || scroll.verticalNormalizedPosition <= 0.0001f) return;
            scroll.verticalNormalizedPosition = 0f;
        }

        internal static float Mark
        {
            get { return _wasTop; }
        }

        internal static bool AtBottom(ScrollRect scroll)
        {
            if (scroll.verticalScrollbar != null && scroll.verticalScrollbar.size >= 0.9999f) return true;
            return scroll.verticalNormalizedPosition <= Edge;
        }

        internal static bool AtTop(ScrollRect scroll)
        {
            return Span(scroll) > 1f && scroll.verticalNormalizedPosition >= 1f - Edge;
        }

        private static float Span(ScrollRect scroll)
        {
            if (scroll == null || scroll.content == null) return 0f;
            var window = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            if (window == null) return 0f;
            return scroll.content.rect.height - window.rect.height;
        }

        private static float FromTop(ScrollRect scroll)
        {
            float span = Span(scroll);
            return span <= 1f ? 0f : (1f - Mathf.Clamp01(scroll.verticalNormalizedPosition)) * span;
        }

        private static void Put(ScrollRect scroll, float fromTop)
        {
            if (scroll == null) return;
            float span = Span(scroll);
            if (span <= 1f) return;
            float want = Mathf.Clamp01(1f - fromTop / span);
            if (Mathf.Abs(scroll.verticalNormalizedPosition - want) < 1E-05f) return;
            scroll.verticalNormalizedPosition = want;
        }

        internal static void Keep(ScrollRect scroll, float fromTop)
        {
            if (scroll == null) return;
            try
            {
                Canvas.ForceUpdateCanvases();
                if (scroll.content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                Put(scroll, fromTop);
                if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Later(scroll, fromTop, _turn));
            }
            catch (Exception e) { Plugin.Trace("[чат] прокрутка: " + e.Message); }
        }

        private static IEnumerator Later(ScrollRect scroll, float fromTop, int turn)
        {
            yield return null;
            if (!Mine(turn)) yield break;
            Put(scroll, fromTop);
            yield return new WaitForSeconds(0.15f);
            if (!Mine(turn)) yield break;
            Put(scroll, fromTop);
        }

        private static bool Mine(int turn)
        {
            return turn == _turn && !_touched;
        }
    }

    internal sealed class ChatHand : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler, IPointerDownHandler
    {
        public void OnBeginDrag(PointerEventData eventData) { ChatStay.Hand(); }

        public void OnDrag(PointerEventData eventData) { ChatStay.Hand(); }

        public void OnScroll(PointerEventData eventData) { ChatStay.Hand(); }

        public void OnPointerDown(PointerEventData eventData) { ChatStay.Hand(); }
    }

    [HarmonyPatch(typeof(ChatPanelContent), "OnChatChanged")]
    internal static class ChatStayPatch
    {
        private static void Prefix(ChatPanelContent __instance)
        {
            if (!ChatStay.On) return;
            try { ChatStay.Remember(__instance); }
            catch (Exception e) { Plugin.Trace("[чат] запомнить прокрутку: " + e.Message); }
        }

        private static void Postfix(ChatPanelContent __instance)
        {
            if (!ChatStay.On) return;
            try { ChatStay.Note(__instance); }
            catch (Exception e) { Plugin.Trace("[чат] список строк: " + e.Message); }
            if (!ChatStay.Held) return;
            ChatStay.Keep(ChatStay.Of(__instance), ChatStay.Mark);
        }
    }

    [HarmonyPatch(typeof(ChatPanelContent), "SetChatContent")]
    internal static class ChatStayContentPatch
    {
        private static void Postfix(ChatPanelContent __instance)
        {
            try { ChatStay.Note(__instance); }
            catch (Exception e) { Plugin.Trace("[чат] смена вкладки: " + e.Message); }
        }
    }
}
