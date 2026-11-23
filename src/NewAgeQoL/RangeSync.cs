using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using Transport.Messages.Responses.Combat;
using Transport.Messages.Responses.Combat.States;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class RangeSync
    {
        private const double Window = 4d;
        private const double Tail = 0.6d;
        private const float Gap = 2.5f;
        private const short FullMeters = 353;

        private static readonly object Gate = new object();
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly HashSet<long> States = new HashSet<long>();
        private static readonly HashSet<int> Roster = new HashSet<int>();
        private static readonly HashSet<string> SystemLines = new HashSet<string>();
        private static readonly Dictionary<int, string> LastWear = new Dictionary<int, string>();
        private static readonly HashSet<short> Snapshot = new HashSet<short> { 169, 231, 434 };
        private static readonly HashSet<long> RangeStates = new HashSet<long>
        {
            Key(1, 10018), Key(1, 10026), Key(1, 120), Key(2, 162), Key(2, 86), Key(2, 61)
        };

        private static long Key(int type, int id) => ((long)type << 32) | (uint)id;

        private static int _me;
        private static int _base;
        private static int _now;
        private static int _round;
        private static bool _statesKnown;
        private static bool _want;
        private static bool _fresh = true;
        private static double _until;
        private static double _closeAt;
        private static int _held;
        private static float _nextAt;
        private static string _note;

        private static double Now => Clock.Elapsed.TotalSeconds;

        internal static void Tick()
        {
            string note;
            lock (Gate) { note = _note; _note = null; }
            if (note != null) Plugin.Trace(note);
            int me = 0;
            try { me = Controllers.User.UserInfo.UserId; } catch { }
            lock (Gate) _me = me;
            if (!SideButtons.InCombat())
            {
                if (!_fresh) Reset();
                return;
            }
            _fresh = false;
            bool want;
            lock (Gate)
            {
                want = _want;
                if (_until > 0d && Now >= _until) Close("timeout");
            }
            if (!want || Time.unscaledTime < _nextAt) return;
            lock (Gate) _want = false;
            if (Spectate.Peeking) return;
            var cd = FighterHint.Cd();
            if (cd == null || cd.MyCharacter == null || cd.MyCharacter.Indicators == null || cd.MyCharacter.Indicators.IsDead) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            _nextAt = Time.unscaledTime + Gap;
            int had, full;
            lock (Gate)
            {
                had = _now;
                full = _base;
                _until = Now + Window;
                _closeAt = 0d;
                _held = 0;
            }
            try
            {
                nc.SendRequest(new MapLoadedRequest());
                Plugin.Trace("[range] effect left me while range is " + had + " of " + full + ", asking the server for the fight snapshot");
            }
            catch (Exception e)
            {
                lock (Gate) _until = 0d;
                Plugin.Warn("[range] snapshot request: " + e.Message);
            }
        }

        private static void Reset()
        {
            _fresh = true;
            lock (Gate)
            {
                States.Clear();
                Roster.Clear();
                SystemLines.Clear();
                LastWear.Clear();
                _base = 0;
                _now = 0;
                _round = 0;
                _statesKnown = false;
                _want = false;
                _until = 0d;
                _closeAt = 0d;
            }
        }

        private static void Close(string why)
        {
            if (_until <= 0d) return;
            _until = 0d;
            _closeAt = 0d;
            _note = "[range] snapshot window closed (" + why + "), range now " + _now + " of " + _base + ", repeated parts held back: " + _held;
        }

        private static string Look(WearResponseMessage wear)
        {
            var text = new System.Text.StringBuilder();
            text.Append(wear.ExchangeWeapon == true ? 'x' : '-');
            if (wear.Slots != null)
                foreach (var slot in wear.Slots)
                    if (slot != null) text.Append('|').Append(slot.SlotId).Append(':').Append(slot.ThingId).Append(':').Append(slot.ImageSkin).Append(':').Append(slot.InventoryId).Append(':').Append(slot.Range);
            return text.ToString();
        }

        internal static bool Incoming(short op, object message)
        {
            lock (Gate)
            {
                if (_me == 0) return true;
                double now = Now;
                bool open = _until > 0d && now < _until;
                if (open && _closeAt > 0d && now >= _closeAt)
                {
                    Close("settled");
                    open = false;
                }
                else if (!open && _until > 0d) Close("timeout");

                var wear = message as WearResponseMessage;
                if (wear != null)
                {
                    string look = Look(wear);
                    string was;
                    bool same = LastWear.TryGetValue(wear.UserId, out was) && was == look;
                    LastWear[wear.UserId] = look;
                    if (wear.UserId == _me && wear.Slots != null)
                    {
                        int top = 0;
                        foreach (var slot in wear.Slots)
                            if (slot != null && slot.Range.HasValue && slot.Range.Value > top) top = slot.Range.Value;
                        if (top > 0) _base = top;
                    }
                    if (open && same) { _held++; return false; }
                    return true;
                }

                var range = message as RefreshRangeResponseMessage;
                if (range != null)
                {
                    int top = 0;
                    if (range.Ranges != null)
                        foreach (var one in range.Ranges)
                            if (one != null && one.Range > top) top = one.Range;
                    if (top > 0) _now = top;
                    if (open && _closeAt <= 0d) _closeAt = now + Tail;
                    return true;
                }

                var states = message as StateGroupsOnUserResponseMessage;
                if (states != null)
                {
                    if (states.UserId != _me) return true;
                    var fresh = new HashSet<long>();
                    if (states.Groups != null)
                        foreach (var g in states.Groups)
                            if (g != null) fresh.Add(Key(g.StateType, g.StateId));
                    bool lost = false;
                    if (_statesKnown)
                        foreach (long id in States)
                            if (RangeStates.Contains(id) && !fresh.Contains(id)) { lost = true; break; }
                    States.Clear();
                    States.UnionWith(fresh);
                    _statesKnown = true;
                    if (lost && !open && _base > 0 && _now > 0 && _now != _base) _want = true;
                    return true;
                }

                var round = message as NewRoundResponseMessage;
                if (round != null)
                {
                    if (open && round.RoundNum == _round) { _held++; return false; }
                    if (open && round.RoundNum != _round) Close("new round");
                    _round = round.RoundNum;
                    return true;
                }

                var roster = message as PlayersDescResponseMessage;
                if (roster != null)
                {
                    bool known = roster.Players != null && roster.Players.Count > 0;
                    if (roster.Players != null)
                        foreach (var one in roster.Players)
                        {
                            if (one == null) continue;
                            if (!Roster.Contains(one.UserId)) known = false;
                            Roster.Add(one.UserId);
                        }
                    if (open && known) { _held++; return false; }
                    return true;
                }

                var chat = message as ChatResponseMessage;
                if (chat != null)
                {
                    if (chat.SenderId != 0 || chat.Type != 3 || string.IsNullOrEmpty(chat.Text)) return true;
                    bool seen = !SystemLines.Add(chat.Text);
                    if (open && seen) { _held++; return false; }
                    return true;
                }

                if (!open || _closeAt > 0d) return true;

                var meters = message as IndicatorsResponseMessage;
                if (meters != null)
                {
                    if (op == FullMeters) { _held++; return false; }
                    if (meters.Indicators != null) meters.Indicators.RemoveAll(one => one == null || one.UserId != _me);
                    if (meters.Indicators == null || meters.Indicators.Count == 0) { _held++; return false; }
                    return true;
                }

                if (Snapshot.Contains(op)) { _held++; return false; }
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "AsyncProcessMessagesHandle")]
    internal static class RangeSyncPatch
    {
        private static bool Prefix(short responseId, object message)
        {
            try { return RangeSync.Incoming(responseId, message); }
            catch { return true; }
        }
    }
}
