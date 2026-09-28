using System;
using HarmonyLib;
using Transport.Messages.Requests.Combat.Actions;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Strike
    {
        private const float Twice = 0.45f;
        private const float Hold = 0.8f;
        private static int _press;
        private static int _took = -1;
        private static int _pressFrame = -1;
        private static float _pressAt = -10f;
        private static int _lastId;
        private static float _lastAt = -10f;
        private static float _byMouse = -10f;
        private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> Hits =
            new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();

        internal static void Tick()
        {
            Fresh();
            Watch();
            Later();
            Sync();
            if (!Input.GetMouseButtonDown(0)) return;
            try
            {
                if (!SideButtons.InCombat() || OnUi()) return;
                var cd = FighterHint.Cd();
                if (cd == null) return;
                int id = FighterHint.Under(cd, false, 0.08f);
                if (id == 0) return;
                _byMouse = Time.unscaledTime;
                Poke(id);
            }
            catch (Exception e) { Plugin.Trace("[strike] mouse: " + e.Message); }
        }

        private static float _watchAt;
        private static bool _mine;
        private const float Wait = 0.3f;
        private static int _soonId;
        private static float _soonAt;

        private static void Later()
        {
            if (_soonId == 0) return;
            if (!SideButtons.InCombat()) { _soonId = 0; return; }
            if (Time.unscaledTime < _soonAt) return;
            int id = _soonId;
            _soonId = 0;
            Open(id);
        }

        private static void Watch()
        {
            if (Time.unscaledTime < _watchAt) return;
            _watchAt = Time.unscaledTime + 0.1f;
            if (!_mine) return;
            if (!SideButtons.InCombat()) { _mine = false; return; }
            if (Counted()) Shut("round is being calculated");
        }

        private static bool Counted()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null) return true;
                return cd.RoundType != RoundType.COMBAT_ROUND;
            }
            catch (Exception e) { Plugin.Trace("[strike] round type: " + e.Message); return false; }
        }

        private static int _chosenRound = -1;
        private static string _chosenWhat = "";
        private static ICombatData _fight;
        private static int _seenRound = -1;

        private static void Fresh()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null)
                {
                    if (_fight != null) Forget("no fight");
                    return;
                }
                if (ReferenceEquals(cd, _fight) && cd.RoundNum >= _seenRound)
                {
                    _seenRound = cd.RoundNum;
                    return;
                }
                if (_fight != null) Forget("new fight");
                _fight = cd;
                _seenRound = cd.RoundNum;
            }
            catch (Exception e) { Plugin.Trace("[strike] fight change: " + e.Message); }
        }

        private static void Forget(string why)
        {
            bool had = _calcRound >= 0 || _chosenRound >= 0;
            _fight = null;
            _seenRound = -1;
            _calcRound = -1;
            _handedRound = -1;
            _chosenRound = -1;
            _chosenWhat = "";
            _soonId = 0;
            _lastId = 0;
            _mine = false;
            if (had) Plugin.Trace("[strike] " + why + " - previous fight locks cleared");
        }

        internal static void Chosen(string what)
        {
            try
            {
                Fresh();
                var cd = FighterHint.Cd();
                if (cd == null || cd.RoundType != RoundType.COMBAT_ROUND) return;
                if (_chosenRound != cd.RoundNum) Plugin.Trace("[strike] round " + cd.RoundNum + ": " + what + " chosen, no strike until round end by button, key or double click");
                _chosenRound = cd.RoundNum;
                _chosenWhat = what;
            }
            catch (Exception e) { Plugin.Trace("[strike] strike choice: " + e.Message); }
        }

        internal static bool AnyBlock(BlocksMessage blocks)
        {
            return blocks != null && (blocks.Blockhead.GetValueOrDefault() != 0 || blocks.Blockbody.GetValueOrDefault() != 0
                || blocks.Blocklefthand.GetValueOrDefault() != 0 || blocks.Blockrighthand.GetValueOrDefault() != 0
                || blocks.Blocklegs.GetValueOrDefault() != 0);
        }

        internal static bool Handing;
        private static int _calcRound = -1;
        private static int _handedRound = -1;

        internal static bool Calculating
        {
            get
            {
                var cd = FighterHint.Cd();
                return cd != null && _calcRound == cd.RoundNum;
            }
        }

        internal static string Locked()
        {
            Fresh();
            var cd = FighterHint.Cd();
            if (cd == null) return null;
            if (_handedRound == cd.RoundNum) return "phase already handed in";
            if (_calcRound == cd.RoundNum) return "combat phase over, round is being calculated";
            if (_chosenRound == cd.RoundNum) return _chosenWhat + " already chosen this round";
            return null;
        }

        internal static void PhaseOver()
        {
            try
            {
                Fresh();
                var cd = FighterHint.Cd();
                if (cd != null) _calcRound = cd.RoundNum;
                if (Handing)
                {
                    if (cd != null) _handedRound = cd.RoundNum;
                    Plugin.Trace("[strike] phase handed in by player in round " + (cd != null ? cd.RoundNum : -1) + ", no more strikes: the server counts a strike as the end of the phase");
                    Shut("phase handed in, no strike chosen");
                    return;
                }
                Plugin.Trace("[strike] combat phase ended in round " + (cd != null ? cd.RoundNum : -1) + ", no more strikes");
                Shut("phase ended, no strike chosen");
            }
            catch (Exception e) { Plugin.Trace("[strike] phase end: " + e.Message); }
        }

        internal static void Release()
        {
            try
            {
                var ctrl = Controllers.Get<CombatButtonsController>();
                var button = ctrl != null ? AccessTools.Property(typeof(CombatButtonsController), "AttackButton")?.GetValue(ctrl) as AttackButton : null;
                if (button != null && button.Activated) button.CloseMenu();
            }
            catch (Exception e) { Plugin.Trace("[strike] strike button: " + e.Message); }
        }

        private static void Shut(string why)
        {
            _mine = false;
            try
            {
                var dialog = Controllers.Get<ConfirmActionDialogController>();
                if (dialog == null || !dialog.Opened) return;
                dialog.AttackConfirmedCallback = null;
                var close = AccessTools.Method(typeof(ConfirmActionDialogController), "Close");
                if (close == null) { Plugin.Trace("[strike] game has no window close"); return; }
                close.Invoke(dialog, null);
                Plugin.Trace("[strike] " + why + " - strike window closed");
            }
            catch (Exception e) { Plugin.Trace("[strike] window close: " + e.Message); }
        }

        private static bool OnUi()
        {
            var system = UnityEngine.EventSystems.EventSystem.current;
            if (system == null) return false;
            var pointer = new UnityEngine.EventSystems.PointerEventData(system) { position = Input.mousePosition };
            Hits.Clear();
            system.RaycastAll(pointer, Hits);
            foreach (var hit in Hits)
            {
                var go = hit.gameObject;
                if (go == null) continue;
                var canvas = go.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                if (root != null && root.sortingOrder >= 100) return true;
                if (go.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
            }
            return false;
        }

        internal static void Hex(HexClickInfo spot)
        {
            if (Time.unscaledTime - _byMouse < 0.35f) return;
            try
            {
                var cd = FighterHint.Cd();
                if (spot == null || cd == null || cd.Characters == null) return;
                foreach (var pair in cd.Characters)
                {
                    var one = pair.Value;
                    if (one == null || !one.HexGridPosition.Equals(spot.Coord)) continue;
                    Poke(one.UserId);
                    return;
                }
            }
            catch (Exception e) { Plugin.Trace("[strike] cell: " + e.Message); }
        }

        internal static void Body()
        {
            if (Time.unscaledTime - _byMouse < 0.35f) return;
            try
            {
                var cd = FighterHint.Cd();
                var picked = cd != null ? cd.SelectedCharacter : null;
                if (picked != null) Poke(picked.UserId);
            }
            catch (Exception e) { Plugin.Trace("[strike] fighter: " + e.Message); }
        }

        private static void Sync()
        {
            if (!Input.GetMouseButtonDown(0) || _pressFrame == Time.frameCount) return;
            _pressFrame = Time.frameCount;
            _press++;
            _pressAt = Time.unscaledTime;
        }

        private static void Poke(int id)
        {
            if (id == 0 || SkillList.Armed || Spectate.Peeking) return;
            Sync();
            float now = Time.unscaledTime;
            if (_press == _took || now - _pressAt > Hold)
            {
                Plugin.Trace("[strike] response without a fresh press - not counted as a click");
                return;
            }
            _took = _press;
            bool twice = id == _lastId && now - _lastAt <= Twice;
            _lastId = id;
            _lastAt = twice ? -10f : now;
            Plugin.Trace("[strike] click on " + id + (twice ? ", second" : ", first"));
            if (!twice) return;
            _soonId = id;
            _soonAt = now + Wait;
        }

        private static AbstractCharacter Find(ICombatData cd, int id)
        {
            var all = cd.Characters;
            if (all == null) return null;
            foreach (var pair in all)
                if (pair.Value != null && pair.Value.UserId == id) return pair.Value;
            return null;
        }

        private static bool Near(Weapon weapon, int distance)
        {
            return (weapon != null ? weapon.Range : 1) >= distance;
        }

        private static void Open(int id)
        {
            var cd = FighterHint.Cd();
            if (cd == null) return;
            var target = Find(cd, id);
            var me = cd.MyCharacter;
            if (target == null || me == null) return;
            if (cd.RoundType != RoundType.COMBAT_ROUND || target.Dead) return;
            string locked = Locked();
            if (locked != null) { Plugin.Trace("[strike] double click: " + locked + " - window not opened"); return; }

            var dialog = Controllers.Get<ConfirmActionDialogController>();
            if (dialog == null || dialog.Opened) return;
            var units = cd.TimeUnitsManager;
            if (units == null) return;

            bool foe = target.Team != me.Team;
            int distance = HexUtils.range(me.HexGridPosition, target.HexGridPosition);
            bool two = units.GetHandType(me.RightHandWeapon) == EHandType.TWO_HAND_WEAPON;
            bool left = foe && Near(me.LeftHandWeapon, distance) && !two;
            bool right = foe && Near(me.RightHandWeapon, distance);
            int kickCost = two ? 4 : 2;
            bool shieldLeft = units.IsShiledMaster(me.LeftHandWeapon);
            bool shieldRight = units.IsShiledMaster(me.RightHandWeapon);
            int blockCost = shieldLeft || shieldRight ? 1 : 2;
            int round = cd.RoundNum;
            int who = target.UserId;

            dialog.OpenAttackDialog(units.ActionTimeUnits, kickCost, blockCost, left, right, shieldLeft, shieldRight);
            AttackHead.Aim(who, target.Login, target.IsBot, target.Race, me.UserId, me.Login);
            _mine = true;
            dialog.AttackConfirmedCallback = (ok, blocks, leftKick, rightKick) =>
            {
                try
                {
                    _mine = false;
                    if (!ok) return;
                    var request = new CombatRequest(round);
                    request.SetBlocks(blocks);
                    if (leftKick != null) leftKick.Target = who;
                    if (rightKick != null) rightKick.Target = who;
                    request.SetKicks(leftKick, rightKick);
                    NetworkConnection.Instance.SendRequest(request);
                    Quiet();
                    Plugin.Trace("[strike] sent at " + who);
                }
                catch (Exception e) { Plugin.Warn("[strike] send: " + e.Message); }
            };
            Plugin.Trace("[strike] window at " + (foe ? "enemy " : "ally ") + who + ", distance " + distance);
        }

        internal static void Key()
        {
            string locked = Locked();
            if (locked != null) { Plugin.Trace("[strike] key: " + locked + " - not striking"); return; }
            if (Swing()) return;
            try
            {
                if (Counted()) { Plugin.Trace("[strike] key: not a combat phase now"); return; }
                var ctrl = Controllers.Get<CombatButtonsController>();
                var click = AccessTools.Method(typeof(CombatButtonsController), "OnAttackButtonClicked");
                if (ctrl == null || click == null) return;
                Plugin.Trace("[strike] key: strike not assembled, the game decides, as with a button click");
                click.Invoke(ctrl, null);
            }
            catch (Exception e) { Plugin.Trace("[strike] key: " + e.Message); }
        }

        internal static bool Swing()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null || cd.RoundType != RoundType.COMBAT_ROUND) return false;
                var me = cd.MyCharacter;
                var target = cd.SelectedCharacter;
                if (me == null || target == null) return false;
                if (target.UserId == me.UserId || target.Team == me.Team || target.Dead) return false;

                var ctrl = Controllers.Get<CombatButtonsController>();
                var call = AccessTools.Method(typeof(CombatButtonsController), "MakeAutomaticAttack");
                if (ctrl == null || call == null) { Plugin.Trace("[strike] game has no plain strike"); return false; }

                int distance = HexUtils.range(me.HexGridPosition, target.HexGridPosition);
                call.Invoke(ctrl, new object[] { cd.RoundNum, cd.TimeUnitsManager, me, target, distance });
                Plugin.Trace("[strike] plain strike at " + target.UserId + ", distance " + distance);
                return true;
            }
            catch (Exception e) { Plugin.Trace("[strike] plain strike: " + e.Message); return false; }
        }

        private static void Quiet()
        {
            try
            {
                var ctrl = Controllers.Get<CombatButtonsController>();
                var button = ctrl != null ? AccessTools.Property(typeof(CombatButtonsController), "AttackButton")?.GetValue(ctrl) as AttackButton : null;
                if (button != null) button.Disable();
            }
            catch (Exception e) { Plugin.Trace("[strike] strike button: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnAttackButtonClicked")]
    internal static class StrikePlainPatch
    {
        private static bool Prefix()
        {
            try
            {
                string locked = Strike.Locked();
                if (locked == null) return !Strike.Swing();
                Plugin.Trace("[strike] strike button: " + locked + " - not striking");
                Strike.Release();
                return false;
            }
            catch (Exception e) { Plugin.Trace("[strike] attack button: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(CombatRequest), "SetKicks")]
    internal static class StrikeKicksPatch
    {
        private static void Postfix(object[] __args)
        {
            if (__args != null && ((__args.Length > 0 && __args[0] != null) || (__args.Length > 1 && __args[1] != null))) Strike.Chosen("strike");
        }
    }

    [HarmonyPatch(typeof(CombatRequest), "AddKick")]
    internal static class StrikeKickPatch
    {
        private static void Postfix() => Strike.Chosen("strike");
    }

    [HarmonyPatch(typeof(CombatRequest), "SetBlocks")]
    internal static class StrikeBlocksPatch
    {
        private static void Postfix(object[] __args)
        {
            if (__args != null && __args.Length > 0 && Strike.AnyBlock(__args[0] as BlocksMessage)) Strike.Chosen("block");
        }
    }

    [HarmonyPatch(typeof(CombatRequest), "AddBlock")]
    internal static class StrikeBlockPatch
    {
        private static void Postfix() => Strike.Chosen("block");
    }

    [HarmonyPatch(typeof(PhaseTimerScript), "EndPhasePressed")]
    internal static class StrikeHandPatch
    {
        private static void Prefix() => Strike.Handing = true;
        private static void Finalizer() => Strike.Handing = false;
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnEndPhase")]
    internal static class StrikePhasePatch
    {
        private static void Postfix() => Strike.PhaseOver();
    }

    [HarmonyPatch(typeof(CombatController), "OnCharacterClick")]
    internal static class StrikeBodyPatch
    {
        private static void Postfix() => Strike.Body();
    }

    [HarmonyPatch(typeof(CombatController), "OnHexClick")]
    internal static class StrikeHexPatch
    {
        private static void Postfix(HexClickInfo target) => Strike.Hex(target);
    }
}
