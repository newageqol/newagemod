using System;
using HarmonyLib;
using Transport.Messages.Responses.Combat;

namespace NewAgeQoL
{
    internal static class PhaseWatch
    {
        private const float Wait = 3f;

        private static readonly object Gate = new object();
        private static int _round = -1;
        private static float _at;
        private static object _fight;
        private static int _asked = -1;
        private static int _played = -1;

        internal static void Played()
        {
            var cd = FighterHint.Cd();
            if (cd == null || cd.RoundType != RoundType.COMBAT_ROUND) return;
            lock (Gate) _played = cd.RoundNum;
        }

        internal static void Sent(BaseRequest request)
        {
            if (!(request is EndCombatAnimationRequest)) return;
            int round = Traverse.Create(request).Field("_round").GetValue<int>();
            lock (Gate)
            {
                if (round != _played) return;
                _round = round;
                _at = RealTime.Now;
            }
        }

        internal static void Tick()
        {
            if (!SideButtons.InCombat() || Spectate.Peeking) { Reset(); return; }
            var cd = FighterHint.Cd();
            if (cd == null) { Reset(); return; }
            if (!ReferenceEquals(cd, _fight))
            {
                Reset();
                _fight = cd;
                return;
            }
            int round;
            float at;
            lock (Gate) { round = _round; at = _at; }
            if (round < 0 || round == _asked) return;
            if (cd.RoundNum != round || cd.RoundType != RoundType.COMBAT_ROUND) return;
            if (RealTime.Now - at < Wait) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            _asked = round;
            nc.SendRequest(new MapLoadedRequest());
            Plugin.Log?.LogInfo("[phase watch] phase " + round + " did not end " + Wait + " s after the animation answer, fight state requested again");
        }

        private static void Reset()
        {
            lock (Gate) { _round = -1; _played = -1; }
            _fight = null;
            _asked = -1;
        }
    }

    [HarmonyPatch(typeof(CombatController), "OnAnimationResponse")]
    internal static class PhaseWatchPlayedPatch
    {
        private static void Postfix(object response)
        {
            try
            {
                var msg = response as AnimationResponseMessage;
                if (msg != null && msg.EndBattlePhase) PhaseWatch.Played();
            }
            catch (Exception e) { Plugin.Trace("[phase watch] " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "SendRequest", new[] { typeof(BaseRequest) })]
    internal static class PhaseWatchSentPatch
    {
        private static void Prefix(BaseRequest request)
        {
            try { PhaseWatch.Sent(request); }
            catch (Exception e) { Plugin.Trace("[phase watch] " + e.Message); }
        }
    }
}
