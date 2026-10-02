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

        private static readonly Regex Used = new Regex(@"^\s*(.+?)\s+использовал\S*\s+\S+\s*\[\s*([^\]]+?)\s*\](.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex Owner = new Regex(@"data-id='(-?\d+)'", RegexOptions.Compiled);
        private static readonly Regex Effect = new Regex(@"(\d+)", RegexOptions.Compiled);
        private static readonly Regex Tag = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        private sealed class Jump
        {
            internal int Delta;
            internal float At;
        }

        private static readonly Dictionary<int, Jump> Jumps = new Dictionary<int, Jump>();
        private static readonly Dictionary<int, Jump> Items = new Dictionary<int, Jump>();
        private static readonly Dictionary<int, Jump> Added = new Dictionary<int, Jump>();
        private static AccessTools.FieldRef<ChangeLifeAnimationItem, float?> _startTime;
        private static bool _looked;
        private static string _name;

        internal static void Item(ChangeLifeAnimationItem item)
        {
            if (item == null || item.life <= 0 || item.AnimationName != "change_life" || item.Target == null) return;
            if (item.Source != null && item.Source != item.Target) return;
            if (item.Group != null && item.Group.actionType == ActionType.THINGEFFECT) return;
            int userId = item.Target.UserId;
            Jump added;
            if (Added.TryGetValue(userId, out added) && Time.unscaledTime - added.At <= Window && added.Delta == item.life)
            {
                Added.Remove(userId);
                if (Applied(item))
                {
                    Plugin.Trace("[prediction] " + (item.Target.Login ?? userId.ToString()) + ": server animation +" + item.life + " came after, marked as already added");
                    return;
                }
            }
            Items[userId] = new Jump { Delta = item.life, At = Time.unscaledTime };
        }

        private static bool Applied(ChangeLifeAnimationItem item)
        {
            if (!_looked)
            {
                _looked = true;
                try { _startTime = AccessTools.FieldRefAccess<ChangeLifeAnimationItem, float?>("_startTime"); }
                catch (Exception e) { Plugin.Trace("[prediction] field _startTime not found: " + e.Message); }
            }
            if (_startTime == null || _startTime(item).HasValue) return false;
            _startTime(item) = 0f;
            return true;
        }

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
            string raw = message.Text;
            var used = Used.Match(Tag.Replace(raw, ""));
            if (!used.Success || !Same(used.Groups[2].Value)) return;
            if (!SideButtons.InCombat()) return;
            Plugin.Trace("[prediction] combat log: " + (raw.Length > 300 ? raw.Substring(0, 300) : raw));

            int bonus = Bonus(used.Groups[3].Value);
            if (bonus <= 0) { Plugin.Trace("[prediction] no effect number in the line"); return; }

            var cd = FighterHint.Cd();
            if (cd == null || cd.Characters == null) return;
            var mage = Find(cd, raw, used.Groups[1].Value.Trim());
            if (mage == null) { Plugin.Trace("[prediction] fighter '" + used.Groups[1].Value.Trim() + "' not found on the field"); return; }
            int userId = mage.UserId;
            var indicators = mage.Indicators;
            if (indicators == null || indicators.IsDead) return;

            Jump jump;
            if (Jumps.TryGetValue(userId, out jump) && Time.unscaledTime - jump.At <= Window
                && Math.Abs(jump.Delta - bonus) <= Math.Max(2, bonus / 50))
            {
                Plugin.Trace("[prediction] " + (mage.Login ?? userId.ToString()) + ": +" + bonus + " already in the server numbers, life " + indicators.CurrentLife);
                return;
            }
            if (Items.TryGetValue(userId, out jump) && Time.unscaledTime - jump.At <= Window && jump.Delta == bonus)
            {
                Items.Remove(userId);
                Plugin.Trace("[prediction] " + (mage.Login ?? userId.ToString()) + ": server sent +" + bonus + " as an animation, it adds the life itself");
                return;
            }

            int was = indicators.CurrentLife;
            indicators.CurrentLife = was + bonus;
            Added[userId] = new Jump { Delta = bonus, At = Time.unscaledTime };
            Plugin.Trace("[prediction] " + (mage.Login ?? userId.ToString()) + ": life " + was + " + " + bonus + " = " + indicators.CurrentLife + " of " + indicators.MaxLife + ", at once from the combat log");
        }

        private static AbstractCharacter Find(ICombatData cd, string raw, string login)
        {
            var id = Owner.Match(raw);
            AbstractCharacter ch;
            int userId;
            if (id.Success && int.TryParse(id.Groups[1].Value, out userId) && cd.Characters.TryGetValue(userId, out ch) && ch != null) return ch;
            AbstractCharacter found = null;
            foreach (var pair in cd.Characters)
            {
                var one = pair.Value;
                if (one == null || !string.Equals(one.Login, login, StringComparison.Ordinal)) continue;
                if (found != null) return null;
                found = one;
            }
            return found;
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

    [HarmonyPatch(typeof(ChangeLifeAnimationItem), MethodType.Constructor, new[]
    {
        typeof(AnimationGroup), typeof(AbstractCharacter), typeof(AbstractCharacter),
        typeof(string), typeof(int), typeof(AnimationItemType),
    })]
    public static class PredictionItemPatch
    {
        private static void Postfix(ChangeLifeAnimationItem __instance)
        {
            try { Prediction.Item(__instance); }
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
