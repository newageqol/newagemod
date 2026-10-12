using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Common.List;
using Transport.Messages.Responses.Combat.States;

namespace NewAgeQoL
{
    internal static class ThreatMark
    {
        private const int Type = 3;
        private const int Id = 121;
        private const int Negative = 2;
        private const int DizzyBit = 2;
        private const short StarsCode = 434;

        private static readonly int[][] OtherStuns =
        {
            new[] { 1, 111 }, new[] { 1, 96 }, new[] { 3, 102 }, new[] { 3, 117 }, new[] { 3, 27 }, new[] { 3, 39 }, new[] { 3, 173 },
        };

        private static readonly object Gate = new object();
        private static readonly Dictionary<int, int> Masks = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> Shown = new Dictionary<int, int>();

        internal static void Hang(int userId, int mask)
        {
            int had;
            if (userId == 0 || (Shown.TryGetValue(userId, out had) && had == mask)) return;
            Shown[userId] = mask;
            Plugin.Trace("[threat] fighter " + userId + " stun stars mask " + mask + ", refreshing states");
            FighterHint.Ask(userId);
        }

        internal static void Wire(short code, object message)
        {
            var stars = message as TwoIntMessageList;
            if (stars != null)
            {
                if (code != StarsCode) return;
                Note(stars);
                return;
            }
            var groups = message as StateGroupsOnUserResponseMessage;
            var items = message as UserEnchantmentsResponseMessage;
            if (groups == null && items == null) return;
            int user = groups != null ? groups.UserId : items.UserId;
            if (!Wanted(user)) return;
            if (groups != null)
            {
                if (groups.Groups == null) groups.Groups = new List<StateGroup>();
                var have = new List<int[]>();
                foreach (var g in groups.Groups) if (g != null) have.Add(new[] { g.StateType, g.StateId });
                if (Decide(have)) groups.Groups.Add(new StateGroup(Id, Type, Negative));
            }
            else
            {
                if (items.Items == null) items.Items = new List<UserEnchantmentsResponseItem>();
                var have = new List<int[]>();
                foreach (var it in items.Items) if (it != null) have.Add(new[] { it.StateType, it.StateId });
                if (Decide(have)) items.Items.Add(new UserEnchantmentsResponseItem(Id, Type, Negative, 0));
            }
        }

        internal static void Reset()
        {
            lock (Gate) Masks.Clear();
            Shown.Clear();
        }

        private static void Note(TwoIntMessageList stars)
        {
            if (stars.Item == null) return;
            lock (Gate)
                foreach (var one in stars.Item)
                    if (one != null) Masks[one.Value1] = one.Value2;
        }

        private static bool Wanted(int user)
        {
            lock (Gate)
            {
                int mask;
                return user != 0 && Masks.TryGetValue(user, out mask) && (mask & DizzyBit) != 0;
            }
        }

        private static bool Decide(List<int[]> have)
        {
            foreach (var st in have)
            {
                if (st[0] == Type && st[1] == Id) return false;
                foreach (var other in OtherStuns)
                    if (st[0] == other[0] && st[1] == other[1]) return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(CombatData), nameof(CombatData.HangEffect))]
    internal static class ThreatMarkHangPatch
    {
        private static void Prefix(int userId, int effectMask)
        {
            try { ThreatMark.Hang(userId, effectMask); }
            catch (Exception e) { Plugin.Trace("[threat] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(UserDataStateImpl), nameof(UserDataStateImpl.CleanupCombat))]
    internal static class ThreatMarkCleanupPatch
    {
        private static void Prefix()
        {
            try { ThreatMark.Reset(); }
            catch (Exception e) { Plugin.Trace("[threat] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "AsyncProcessMessagesHandle")]
    internal static class ThreatMarkWirePatch
    {
        private static void Prefix(short responseId, object message)
        {
            try { ThreatMark.Wire(responseId, message); }
            catch (Exception e) { Plugin.Trace("[threat] " + e.Message); }
        }
    }
}
