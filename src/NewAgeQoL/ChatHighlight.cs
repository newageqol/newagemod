using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Chat;

namespace NewAgeQoL
{
    internal static class ChatHighlight
    {
        private const int SystemMessage = 3;
        private const string Open = "<color=#4CFF4C>";
        private const string Close = "</color>";

        private static readonly Regex Colors = new Regex("</?color[^>]*>", RegexOptions.Compiled);


        internal static void Apply(ChatResponseMessage message)
        {
            if (message == null || message.Type != SystemMessage) return;
            if (string.IsNullOrEmpty(message.Text)) return;
            if (message.Text.StartsWith(Open, StringComparison.Ordinal)) return;

            string me = Login();
            if (string.IsNullOrEmpty(me) || me.Length < 2) return;
            if (message.Text.IndexOf(me, StringComparison.OrdinalIgnoreCase) < 0) return;

            message.Text = Open + Colors.Replace(message.Text, "") + Close;
        }

        internal static string Login()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null && ud.UserInfo != null ? ud.UserInfo.Login : null;
            }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(ChatController), "ChatMessageReceived")]
    public static class ChatHighlightPatch
    {
        private static void Prefix(ChatResponseMessage message)
        {
            try { ChatHighlight.Apply(message); }
            catch (Exception e) { Plugin.Fault("[chat] подсветка строки: " + e.Message); }
        }
    }
}
