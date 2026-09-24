using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Counter
    {
        private const float Period = 0.1f;
        private const int MaxTries = 3;

        private const int StateType = 5;

        private static object _battle;
        private static int _startExpower;
        private static int _lastUse;
        private static int _sentRound;
        private static int _tries;
        private static int _aimedAt;
        private static float _next;
        private static int _whyRound;
        private static string _whyText;
        private static int _seenPhase;
        private static int _gameRound;

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            try
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + Period;

                if (!SideButtons.InCombat()) { _battle = null; return; }

                var cd = Combat();
                if (cd == null) return;
                Sync(cd);

                var me = cd.MyCharacter;
                var ind = me != null ? me.Indicators : null;
                if (ind == null || !ind.Initialized) return;

                if (_startExpower < 0)
                {
                    _startExpower = ind.CurrentExpower;
                    Plugin.Trace("[контрприём] бой начался, зарядов " + _startExpower);
                }

                if (Plugin.CfgCounterAuto == null || !Plugin.CfgCounterAuto.Value) return;
                if (me.Dead) return;
                int round = Round(cd);
                if (_sentRound == round || _tries >= MaxTries) return;

                var btn = Button(cd);
                if (btn == null) return;

                if (_lastUse > 0) return;
                var st = me.CharacterStates;
                if (st != null && st.Actual && HasCounterState(me, btn.Id))
                {
                    _lastUse = round;
                    Plugin.Trace("[контрприём] уже висит на мне — ставить не нужно");
                    return;
                }

                if (_startExpower < btn.ExpowerCost) return;
                if (!EnemyPlayer(cd)) return;
                string why = "в бою есть игроки";

                string no = CantUse(cd, me, ind, btn);
                if (no != null)
                {
                    if (_whyRound != round || _whyText != no)
                    {
                        _whyRound = round;
                        _whyText = no;
                        Plugin.Trace("[контрприём] не сейчас (" + why + "): " + no);
                    }
                    return;
                }

                _aimedAt = me.UserId;
                NetworkConnection.Instance.SendRequest(new UseDodgeRequest(btn.Id, cd.RoundNum, me.UserId, null));
                _sentRound = round;
                _tries++;
                Plugin.Trace("[контрприём] «" + btn.Name + "» на себя, раунд " + round + " (фаза " + cd.RoundNum + ") — " + why);
            }
            catch (Exception e) { Plugin.Fault("[контрприём] " + e.Message); }
        }

        internal static void NoteAimed(IQuickButton btn)
        {
            try
            {
                if (btn == null || btn.QuickButtonType != EQuickButtonType.DODGE) return;
                var cd = Combat();
                if (cd == null) return;
                var counter = Button(cd);
                if (counter == null || counter.Id != btn.Id) return;
                _aimedAt = TargetTypeExtension.GetTarget(btn, cd);
            }
            catch { _aimedAt = 0; }
        }

        internal static void NoteUsed(QuickButton btn)
        {
            try
            {
                if (btn == null || btn.QuickButtonType != EQuickButtonType.DODGE) return;
                if (!SideButtons.InCombat()) return;

                var cd = Combat();
                if (cd == null) return;
                var counter = Button(cd);
                if (counter == null || counter.Id != btn.Id) return;

                Sync(cd);
                int round = Round(cd);
                int aimed = _aimedAt;
                _aimedAt = 0;
                var me = cd.MyCharacter;
                if (me == null) return;

                if (aimed == 0)
                {
                    Plugin.Trace("[контрприём] применён, но цель неизвестна — засчитываем на себя");
                    _lastUse = round;
                    _tries = 0;
                    return;
                }
                if (aimed != me.UserId)
                {
                    Plugin.Trace("[контрприём] ушёл на другого бойца (" + aimed + ") — себе не засчитываем");
                    return;
                }

                _lastUse = round;
                _tries = 0;
                Plugin.Trace("[контрприём] применён на себя в раунде " + round);
            }
            catch (Exception e) { Plugin.Fault("[контрприём] " + e.Message); }
        }

        private static void Sync(ICombatData cd)
        {
            if (ReferenceEquals(cd, _battle)) return;
            _battle = cd;
            _startExpower = -1;
            _lastUse = 0;
            _sentRound = 0;
            _tries = 0;
            _aimedAt = 0;
            _whyRound = 0;
            _whyText = null;
            _seenPhase = 0;
            _gameRound = 0;
        }

        private static bool HasCounterState(PlayerCharacter me, int id)
        {
            var st = me.CharacterStates;
            var list = st != null ? st.States : null;
            if (list == null) return false;
            foreach (var g in list)
                if (g != null && g.StateType == StateType && g.StateId == id) return true;
            return false;
        }

        private static int Round(ICombatData cd)
        {
            if (cd.RoundNum != _seenPhase)
            {
                _seenPhase = cd.RoundNum;
                if (_gameRound == 0 || cd.RoundType == RoundType.WALK_ROUND) _gameRound++;
            }
            return _gameRound;
        }

        private static QuickButton Button(ICombatData cd)
        {
            var bm = cd.ButtonManager;
            var dodges = bm != null ? bm.Dodges : null;
            if (dodges == null) return null;

            int id = Plugin.CfgCounterId != null && Plugin.CfgCounterId.Value > 0 ? Plugin.CfgCounterId.Value : 4;
            var btn = dodges.GetButton(id);
            if (btn != null) return btn;

            foreach (var b in dodges.GetButtonsByRoundType(cd.RoundType))
                if (b != null && !string.IsNullOrEmpty(b.Name)
                    && b.Id == DodgeConsts.DODGE_COUNTER_DODGE) return b;
            return null;
        }

        private static string CantUse(ICombatData cd, PlayerCharacter me, CharacterIndicators ind, QuickButton btn)
        {
            if (!btn.Enabled) return string.IsNullOrEmpty(btn.DisableCause) ? "приём выключен сервером" : btn.DisableCause;
            if (!QuickButtonHelper.CheckRoundType(btn, cd.RoundType)) return "не та фаза раунда";
            if (!btn.CanActivate) return "перезарядка";
            if (btn.StaminaCost > ind.CurrentStamina) return "не хватает энергии";
            if (btn.ExpowerCost > ind.CurrentExpower) return "не хватает зарядов";
            if (btn.Target == ETargetType.TARGET_ALLY_EXCEPT_SOURCE) return "этот приём на себя не наводится";
            if (TargetTypeExtension.IsActionHasCellTarget(btn)) return "приёму нужна клетка на поле";
            return null;
        }

        private static bool EnemyPlayer(ICombatData cd)
        {
            var me = cd.MyCharacter;
            var chars = cd.Characters;
            if (me == null || chars == null) return false;
            foreach (var kv in chars)
            {
                var ch = kv.Value;
                if (ch == null || ch.IsBot || ch.Dead) continue;
                if (ch.UserId == me.UserId || ch.Team == me.Team) continue;
                return true;
            }
            return false;
        }

        private static ICombatData Combat()
        {
            try { return Controllers.User?.CombatData; }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(QuickButton), nameof(QuickButton.Activate))]
    public static class CounterUsePatch
    {
        private static void Postfix(QuickButton __instance) => Counter.NoteUsed(__instance);
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnActionConfirmed")]
    public static class CounterAimPatch
    {
        private static void Prefix(IQuickButton button) => Counter.NoteAimed(button);
    }
}
