using System;
using HarmonyLib;
using Transport.Messages.Responses.Combat.Buttons;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Focus
    {
        private const int SummonGarnet = 22;
        private const int WardGarnet = 23;
        private const int SummonMalachite = 24;
        private const int WardMalachite = 25;
        private const int SummonAmethyst = 26;
        private const int WardAmethyst = 27;

        private static readonly int[] Alone =
        {
            DodgesButtonStateHolder.HantingOnButtonId, DodgesButtonStateHolder.HantingOffButtonId,
            DodgeConsts.DODGE_COUNTER_DODGE, DodgeConsts.DODGE_CHANGE_WEAPON, DodgeConsts.DODGE_CHANGE_RELICS, DodgeConsts.DODGE_MUMMIFICATION,
            DodgeConsts.DODGE_SUMMON_1, DodgeConsts.DODGE_SUMMON_2, DodgeConsts.DODGE_SUMMON_3, DodgeConsts.DODGE_GENERAL_SUMMON,
            SummonGarnet, SummonMalachite, SummonAmethyst,
            DodgeConsts.DODGE_PREVENT_BY_RACE_1, DodgeConsts.DODGE_PREVENT_BY_RACE_2, DodgeConsts.DODGE_PREVENT_BY_RACE_3,
            WardGarnet, WardMalachite, WardAmethyst,
        };

        private static bool _on;
        private static ICombatData _fight;
        private static int _last = -1;
        private static bool _busy;
        private static int _walks;
        private static int _lastWalk = -1;
        private static ICombatData _walkFight;
        private static int _spentAt = -1;
        private static ICombatData _spentFight;
        private static Sprite _glow;

        internal static bool On => _on;

        internal static bool Is(IQuickButton skill)
        {
            return skill != null && skill.QuickButtonType == EQuickButtonType.DODGE
                && skill.Id == DodgeConsts.DODGE_FORCED_DODGE;
        }

        internal static bool Lit(IQuickButton skill)
        {
            return _on && Is(skill);
        }

        private static bool Shift()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        internal static bool Click(IQuickButton skill)
        {
            if (!Is(skill)) return false;
            if (!Shift())
            {
                Stop("сосредоточенность нажата без Shift");
                return false;
            }
            if (_on)
            {
                Stop("снята по Shift");
                return true;
            }
            var cd = FighterHint.Cd();
            if (cd == null) return true;
            if (!ReferenceEquals(cd, _fight)) { _fight = cd; _last = cd.RoundNum; }
            _on = true;
            Plugin.Trace("[связка] включена по Shift в раунде " + cd.RoundNum
                + (Spent(cd) ? ", в этом раунде уже нажата" : ""));
            return true;
        }

        internal static string Hint(string binding)
        {
            string key = string.IsNullOrEmpty(binding) ? "" : Hotkeys.Text(binding);
            if (key == "—") key = "";
            string how = key.Length > 0 ? "Shift+клик или Shift+" + key : "Shift+клик";
            return how + (_on ? " — снять связку" : " — включить связку");
        }

        internal static Sprite Glow()
        {
            if (_glow != null) return _glow;
            const int side = 96;
            var pic = new Texture2D(side, side, TextureFormat.RGBA32, false);
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    float fx = (x + 0.5f) / side * 2f - 1f;
                    float fy = (y + 0.5f) / side * 2f - 1f;
                    float far = Mathf.Sqrt(fx * fx + fy * fy);
                    float edge = Mathf.InverseLerp(1f, 0.9f, far);
                    float rim = Mathf.InverseLerp(0.3f, 0.92f, far);
                    pic.SetPixel(x, y, new Color(1f, 1f, 1f, far >= 1f ? 0f : edge * Mathf.Lerp(0.5f, 1f, rim * rim)));
                }
            pic.Apply();
            pic.filterMode = FilterMode.Bilinear;
            pic.wrapMode = TextureWrapMode.Clamp;
            _glow = Sprite.Create(pic, new Rect(0f, 0f, side, side), new Vector2(0.5f, 0.5f));
            return _glow;
        }

        internal static void Tick()
        {
            if (!SideButtons.InCombat())
            {
                if (_fight == null) return;
                Stop("бой закончился");
                _fight = null;
                _last = -1;
                return;
            }
            var cd = FighterHint.Cd();
            if (cd != null && !ReferenceEquals(cd, _fight)) Begin(cd);
        }

        internal static void Round(ICombatData cd)
        {
            if (cd == null) return;
            if (!ReferenceEquals(cd, _fight) || cd.RoundNum < _last) Begin(cd);
            _last = cd.RoundNum;
            if (cd.RoundType != RoundType.WALK_ROUND) return;
            if (ReferenceEquals(cd, _walkFight) && cd.RoundNum == _lastWalk) return;
            _walkFight = cd;
            _lastWalk = cd.RoundNum;
            _walks++;
        }

        private static void Begin(ICombatData cd)
        {
            _fight = cd;
            _last = cd.RoundNum;
            _on = true;
            Plugin.Trace("[связка] новый бой, связка включена сразу");
        }

        internal static void Before(CombatButtonsController ctrl, IQuickButton button)
        {
            if (_busy || ctrl == null || button == null || button.QuickButtonType != EQuickButtonType.DODGE) return;
            var cd = FighterHint.Cd();
            if (cd == null) return;
            if (Is(button)) { Mark(cd); return; }
            if (!_on || !ReferenceEquals(cd, _fight) || Array.IndexOf(Alone, button.Id) >= 0 || Spent(cd)) return;

            var manager = cd.ButtonManager;
            IQuickButton focus = manager != null ? manager.Dodges.GetButton(DodgeConsts.DODGE_FORCED_DODGE) : null;
            if (focus == null) return;
            string why = Why(cd, focus, button);
            if (why != null)
            {
                Plugin.Trace("[связка] приём " + button.Id + " уходит без сосредоточенности: " + why);
                return;
            }

            var send = AccessTools.Method(typeof(CombatButtonsController), "OnActionConfirmed");
            if (send == null) { Plugin.Trace("[связка] отправлять нечем"); return; }
            _busy = true;
            try { send.Invoke(ctrl, new object[] { focus, null }); }
            finally { _busy = false; }
            Mark(cd);
            Plugin.Trace("[связка] сосредоточенность ушла перед приёмом " + button.Id + " в раунде " + cd.RoundNum);
        }

        private static string Why(ICombatData cd, IQuickButton focus, IQuickButton button)
        {
            if (!QuickButtonHelper.CheckRoundType(focus, cd.RoundType)) return "не в этой фазе";
            if (!focus.Enabled) return string.IsNullOrEmpty(focus.DisableCause) ? "недоступна" : focus.DisableCause;
            if (!focus.CanActivate) return "уже нажата или перезаряжается";
            var me = cd.MyCharacter;
            var ind = me != null ? me.Indicators : null;
            if (ind == null) return null;
            if (focus.ManaCost + button.ManaCost > ind.CurrentMana) return "не хватает маны";
            if (focus.ExpowerCost + button.ExpowerCost > ind.CurrentExpower) return "не хватает зарядов";
            return null;
        }

        private static void Mark(ICombatData cd)
        {
            _spentFight = cd;
            _spentAt = _walks;
        }

        private static bool Spent(ICombatData cd)
        {
            return ReferenceEquals(_spentFight, cd) && _spentAt == _walks;
        }

        private static void Stop(string why)
        {
            if (!_on) return;
            _on = false;
            Plugin.Trace("[связка] выключена: " + why);
        }
    }

    internal sealed class FocusPulse : MonoBehaviour
    {
        private Image _pic;

        private void OnEnable() => Step();

        private void Update() => Step();

        private void Step()
        {
            if (_pic == null) _pic = GetComponent<Image>();
            if (_pic == null) return;
            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _pic.color = new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.3f + 0.6f * wave);
            float grow = 1f + 0.07f * wave;
            transform.localScale = new Vector3(grow, grow, 1f);
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnActionConfirmed")]
    internal static class FocusSendPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(CombatButtonsController __instance, IQuickButton button, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try { Focus.Before(__instance, button); }
            catch (Exception e) { Plugin.Trace("[связка] перед приёмом: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatData), "StartNewRound")]
    internal static class FocusRoundPatch
    {
        private static void Postfix(CombatData __instance)
        {
            try { Focus.Round(__instance); }
            catch (Exception e) { Plugin.Trace("[связка] новый раунд: " + e.Message); }
        }
    }
}
