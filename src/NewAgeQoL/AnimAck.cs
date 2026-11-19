using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Transport.Messages.Responses.Combat;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(CombatController), "OnAnimationResponse")]
    internal static class AnimAckHeardPatch
    {
        private static void Postfix(object response)
        {
            try { AnimAck.Heard(response); }
            catch (Exception e) { Plugin.Caught("[anim ack] heard: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[] { typeof(BaseRequest) })]
    internal static class AnimAckSendPatch
    {
        private static void Prefix(BaseRequest request)
        {
            try { AnimAck.Sent(request); }
            catch (Exception e) { Plugin.Caught("[anim ack] sent: " + e.Message); }
        }
    }

    internal static class AnimAck
    {
        private const float FirstWait = 1.5f;
        private const float NextWait = 4f;
        private const int MostTries = 2;
        private const float QuickWait = 1f;
        private const float QuietWait = 0.7f;
        private const int MostQuick = 3;

        private static object _fight;
        private static bool _rivals;
        private static int _round = -1;
        private static RoundType _type;
        private static int _heard;
        private static int _answered;
        private static bool _over;
        private static float _idleAt = -1f;
        private static float _triedAt = -1f;
        private static int _tries;
        private static bool _sending;
        private static float _overAt = -1f;
        private static float _heardAt = -1f;
        private static int _quick;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Waits() => false;

        private static ICombatData Mine()
        {
            if (!SideButtons.InCombat() || Spectate.Peeking) return null;
            var cd = FighterHint.Cd();
            if (cd == null || cd.MyCharacter == null || cd.RoundNum <= 0) return null;
            if (!ReferenceEquals(cd, _fight))
            {
                _fight = cd;
                _rivals = false;
                _round = -1;
            }
            if (cd.RoundNum != _round || cd.RoundType != _type)
            {
                bool same = cd.RoundNum == _round;
                _round = cd.RoundNum;
                _type = cd.RoundType;
                _idleAt = -1f;
                _triedAt = -1f;
                _tries = 0;
                if (!same)
                {
                    _heard = 0;
                    _answered = 0;
                    _over = false;
                    _overAt = -1f;
                    _heardAt = -1f;
                    _quick = 0;
                }
            }
            return cd;
        }

        internal static void Heard(object response)
        {
            var cd = Mine();
            if (cd == null) return;
            var msg = response as AnimationResponseMessage;
            if (msg == null) return;
            if (msg.EndBattlePhase) _over = true;
            _idleAt = -1f;
            if (msg.Groups == null || msg.Groups.Count <= 0) return;
            _heard++;
            _heardAt = RealTime.Now;
        }

        internal static void Sent(BaseRequest request)
        {
            if (_sending || !(request is EndCombatAnimationRequest)) return;
            if (Mine() == null) return;
            _answered++;
        }

        private static bool Rivals(ICombatData cd)
        {
            if (_rivals) return true;
            var me = cd.MyCharacter;
            if (me == null || cd.Characters == null) return false;
            foreach (var pair in cd.Characters)
            {
                var one = pair.Value as PlayerCharacter;
                if (one == null || one.UserId == me.UserId || one.Team == me.Team) continue;
                _rivals = true;
                Plugin.Trace("[anim ack] enemy player " + one.UserId + " in the fight, watching for a stuck round end");
                return true;
            }
            return false;
        }

        internal static void Tick()
        {
            var cd = Mine();
            if (cd == null) return;
            bool calc = cd.RoundType == RoundType.CALCULATING_COMBAT;
            if (cd.RoundType != RoundType.COMBAT_ROUND && !calc) return;
            if (!calc && !_over) return;
            float now = RealTime.Now;
            if (_overAt < 0f) _overAt = now;
            if (Quick(cd, now)) return;
            var ap = cd.AnimationProcessor;
            if (ap == null || ap.Active) { _idleAt = -1f; return; }
            if (_idleAt < 0f) { _idleAt = now; return; }
            if (_tries >= MostTries) return;
            float due = _tries == 0 ? _idleAt + FirstWait : _triedAt + NextWait;
            if (now < due) return;
            if (!Rivals(cd)) return;
            if (Waits()) return;
            var conn = NetworkConnection.Instance;
            if (conn == null || !conn.IsConnected()) return;
            int times = Math.Max(1, _heard - _answered);
            _tries++;
            _triedAt = now;
            _sending = true;
            try
            {
                for (int i = 0; i < times; i++) conn.SendRequest(new EndCombatAnimationRequest(cd.RoundNum));
            }
            finally { _sending = false; }
            _answered += times;
            Plugin.Warn("[anim ack] round " + cd.RoundNum + " (" + cd.RoundType + ") stuck after the animations, answered " + times
                        + " (animations " + _heard + "), try " + _tries + " of " + MostTries);
        }

        private static bool Quick(ICombatData cd, float now)
        {
            if (_quick >= MostQuick || _heard <= _answered) return false;
            if (now < _overAt + QuickWait || now < _heardAt + QuietWait) return false;
            if (!Rivals(cd) || Waits()) return false;
            var conn = NetworkConnection.Instance;
            if (conn == null || !conn.IsConnected()) return false;
            int times = _heard - _answered;
            _quick++;
            _sending = true;
            try
            {
                for (int i = 0; i < times; i++) conn.SendRequest(new EndCombatAnimationRequest(cd.RoundNum));
            }
            finally { _sending = false; }
            _answered += times;
            Plugin.Trace("[anim ack] round " + cd.RoundNum + " (" + cd.RoundType + ") over, answered " + times
                         + " animations without waiting for them to play (animations " + _heard + ")");
            return true;
        }
    }
}
