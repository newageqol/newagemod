using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class ChatLinks
    {
        private const string Paint = "#6FC3FF";
        private const int Keep = 3000;

        private static readonly Regex Urls = new Regex(@"(?<![\w/@.])(?:https?://|www\.)[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Plain = new Regex(@"<link=(\d+)><u>([^<]*)</u></link>", RegexOptions.Compiled);
        private static readonly char[] Tail = { '.', ',', ';', ':', '!', '?', ')', ']', '}', '»' };
        private static readonly Dictionary<int, string> Known = new Dictionary<int, string>();
        private static readonly Queue<int> Order = new Queue<int>();
        private static readonly AccessTools.FieldRef<ChatContent, string> Body = AccessTools.FieldRefAccess<ChatContent, string>("_text");
        private static readonly FieldInfo Counter = AccessTools.Field(typeof(ChatContent), "linkCounter");

        internal sealed class Link : BaseChatLink
        {
            internal readonly string Url;

            internal Link(int id, string url) : base(id, null)
            {
                Url = url;
            }

            protected override IChatLinkData BuildClickObject() => this;

            public override void Execute() => Open(Url);
        }

        internal static bool Has(int id) => Known.ContainsKey(id);

        internal static bool Has(string id)
        {
            int got;
            return int.TryParse(id, out got) && Known.ContainsKey(got);
        }

        internal static void Mark(ChatContent content, ChatResponseMessage resp)
        {
            if (content == null || resp == null || Counter == null) return;
            if ((EChatMessageType)resp.Type == EChatMessageType.MSG_SYSTEM) return;
            string text = Body(content);
            if (string.IsNullOrEmpty(text)) return;
            if (text.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0) return;

            var box = new StringBuilder(text.Length + 64);
            int from = 0;
            int made = 0;
            foreach (Match m in Urls.Matches(text))
            {
                if (m.Index < from || Inside(text, m.Index)) continue;
                string url = m.Value.TrimEnd(Tail);
                if (url.Length < 8) continue;
                int id = (int)Counter.GetValue(null) + 1;
                Counter.SetValue(null, id);
                box.Append(text, from, m.Index - from);
                box.Append("<link=").Append(id).Append("><u><color=").Append(Paint).Append('>')
                    .Append(url).Append("</color></u></link>");
                from = m.Index + url.Length;
                content.GetLinks().Add(new Link(id, url));
                Remember(id, url);
                made++;
            }
            if (made == 0) return;
            box.Append(text, from, text.Length - from);
            Body(content) = box.ToString();
        }

        internal static string Dress(string line)
        {
            if (Known.Count == 0 || string.IsNullOrEmpty(line) || line.IndexOf("<link=", StringComparison.Ordinal) < 0) return line;
            return Plain.Replace(line, m => Has(m.Groups[1].Value)
                ? "<link=" + m.Groups[1].Value + "><u><color=" + Paint + ">" + m.Groups[2].Value + "</color></u></link>"
                : m.Value);
        }

        internal static void Open(string url)
        {
            try
            {
                string go = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + url : url;
                Application.OpenURL(go);
                Plugin.Trace("[чат] открыта ссылка из чата");
            }
            catch (Exception e) { Plugin.Warn("[чат] ссылка не открылась: " + e.Message); }
        }

        private static bool Inside(string text, int at)
        {
            if (at <= 0) return false;
            if (text.LastIndexOf('<', at) > text.LastIndexOf('>', at)) return true;
            return text.LastIndexOf("<link", at, StringComparison.Ordinal) > text.LastIndexOf("</link>", at, StringComparison.Ordinal);
        }

        private static void Remember(int id, string url)
        {
            Known[id] = url;
            Order.Enqueue(id);
            while (Order.Count > Keep) Known.Remove(Order.Dequeue());
        }
    }

    [HarmonyPatch(typeof(ChatContent), MethodType.Constructor, typeof(ChatResponseMessage), typeof(Action<IChatLinkData>))]
    internal static class ChatLinksMarkPatch
    {
        private static void Postfix(ChatContent __instance, ChatResponseMessage resp)
        {
            try { ChatLinks.Mark(__instance, resp); }
            catch (Exception e) { Plugin.Trace("[чат] ссылки в строке: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChatPanelContent), "FireContentLinkClick")]
    internal static class ChatLinksClickPatch
    {
        private static bool Prefix(IChatLinkData obj)
        {
            var link = obj as ChatLinks.Link;
            if (link == null) return true;
            ChatLinks.Open(link.Url);
            return false;
        }
    }
}
