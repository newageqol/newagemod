using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using Transport.Messages.Responses.Combat;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Prediction
    {
        private const string AbilityKey = "abilities.ability126.name";
        private const float Window = 3f;

        private static readonly Regex Ability = new Regex(@"\[\s*<span class='ca'>([^<]*)</span>\s*\]", RegexOptions.Compiled);
        private static readonly Regex Owner = new Regex(@"<span class='cu' data-id='(-?\d+)'>", RegexOptions.Compiled);
        private static readonly Regex Effect = new Regex(@"(\d+)", RegexOptions.Compiled);
        private static readonly Regex Tag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private sealed class Jump
        {
            internal int Delta;
            internal float At;
        }

        private static readonly Dictionary<int, Jump> Jumps = new Dictionary<int, Jump>();
        private static string _name;

        internal static void Assigned(CharacterIndicators indicators, CharacterIndicatorsMessage message, int before)
        {
            if (indicators == null || message == null || message.UserId == 0) return;
            int delta = indicators.CurrentLife - before;
            if (delta <= 0) return;
            Jumps[message.UserId] = new Jump { Delta = delta, At = Time.unscaledTime };
        }

        internal static void Heard(ChatResponseMessage message)
        {
            if (message == null || message.Type != 3 || string.IsNullOrEmpty(message.Text)) return;
            string text = message.Text;
            var ability = Ability.Match(text);
            if (!ability.Success || !Same(ability.Groups[1].Value)) return;
            if (!SideButtons.InCombat()) return;

            int userId = 0;
            foreach (Match m in Owner.Matches(text.Substring(0, ability.Index)))
                int.TryParse(m.Groups[1].Value, out userId);
            if (userId == 0) return;

            int bonus = Bonus(text.Substring(ability.Index + ability.Length));
            if (bonus <= 0) return;

            var cd = FighterHint.Cd();
            AbstractCharacter mage = null;
            if (cd == null || cd.Characters == null || !cd.Characters.TryGetValue(userId, out mage) || mage == null) return;
            var indicators = mage.Indicators;
            if (indicators == null || indicators.IsDead) return;

            Jump jump;
            if (Jumps.TryGetValue(userId, out jump) && Time.unscaledTime - jump.At <= Window
                && Math.Abs(jump.Delta - bonus) <= Math.Max(2, bonus / 50))
            {
                Plugin.Trace("[prediction] " + (mage.Login ?? userId.ToString()) + ": +" + bonus + " already in the server numbers, life " + indicators.CurrentLife);
                return;
            }

            int was = indicators.CurrentLife;
            indicators.CurrentLife = was + bonus;
            Plugin.Trace("[prediction] " + (mage.Login ?? userId.ToString()) + ": life " + was + " + " + bonus + " = " + indicators.CurrentLife + " of " + indicators.MaxLife + ", at once from the combat log");
        }

        private static int Bonus(string tail)
        {
            string plain = Tag.Replace(tail, " ");
            int best = 0;
            int at = 0;
            while (true)
            {
                int i = plain.IndexOf("эффект:", at, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                var m = Effect.Match(plain, i + 7);
                if (m.Success && m.Index - (i + 7) <= 3)
                {
                    int v;
                    if (int.TryParse(m.Value, out v) && v > best) best = v;
                }
                at = i + 7;
            }
            return best;
        }

        private static bool Same(string name)
        {
            if (_name == null)
            {
                string found = null;
                try { found = ResourceStrings.GetString(AbilityKey); }
                catch (Exception e) { Plugin.Trace("[prediction] ability name: " + e.Message); }
                if (string.IsNullOrEmpty(found) || found == AbilityKey) return false;
                _name = Norm(found);
            }
            return Norm(name) == _name;
        }

        private static string Norm(string s)
        {
            return string.Join(" ", (s ?? "").Replace(' ', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant().Replace('Ё', 'Е');
        }
    }

    [HarmonyPatch(typeof(ChatController), "ChatMessageReceived")]
    public static class PredictionChatPatch
    {
        private static void Postfix(ChatResponseMessage message)
        {
            try { Prediction.Heard(message); }
            catch (Exception e) { Plugin.Trace("[prediction] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CharacterIndicators), "Assign")]
    public static class PredictionAssignPatch
    {
        private static void Prefix(CharacterIndicators __instance, out int __state)
        {
            __state = __instance != null ? __instance.CurrentLife : 0;
        }

        private static void Postfix(CharacterIndicators __instance, CharacterIndicatorsMessage message, int __state)
        {
            try { Prediction.Assigned(__instance, message, __state); }
            catch (Exception e) { Plugin.Trace("[prediction] " + e.Message); }
        }
    }
}
