using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Cooldown
    {
        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { 26, "калеки" }, { 27, "сокра" }, { 28, "спринта" }, { 30, "штурма" },
            { 68, "поглота" }, { 69, "трюма" }, { 70, "самопожертвования" }, { 112, "избавления" },
            { 119, "сафари" }, { 95, "фехты" }, { 96, "преданности" }, { 155, "вуды" }, { 156, "эмпатии" },

            { 61, "мастерского броска" }, { 62, "отража" }, { 63, "мести" }, { 65, "куража" },
            { 88, "смеха" }, { 90, "рунки" }, { 121, "угрозы" }, { 122, "покрова" },
            { 97, "подава" }, { 98, "неисты" }, { 153, "рунной брони" }, { 154, "альтруизма" },

            { 54, "рассека" }, { 55, "обезоруживания" }, { 56, "разрывания" }, { 58, "вихря" },
            { 84, "стойки" }, { 86, "броска" }, { 115, "гарпа" }, { 116, "энергетического вампиризма" },
            { 101, "резни" }, { 102, "дд" }, { 158, "канала" }, { 160, "инверса" },

            { 47, "яда" }, { 49, "нейротоксина" }, { 51, "вг" }, { 52, "тройки" },
            { 80, "рикошета" }, { 82, "шокирующих стрел" }, { 117, "парализующего яда" }, { 118, "грации" },
            { 99, "гема" }, { 100, "песни" }, { 159, "мантры" }, { 162, "навеса" },

            { 40, "проника" }, { 41, "усиленного заклинания" }, { 42, "медитации" }, { 44, "связи маны" },
            { 76, "призыва тени" }, { 78, "призыва духа" }, { 125, "озарения" }, { 126, "предсказа" },
            { 91, "хаоса" }, { 92, "откровения" }, { 151, "эманации" }, { 152, "магнетизма" },

            { 33, "чумы" }, { 34, "новы" }, { 35, "гипноза" }, { 37, "тюри" },
            { 72, "изгиба реальности" }, { 74, "антимагии" }, { 106, "коллапса" }, { 124, "безмолвного стража" },
            { 93, "призыва ворона" }, { 94, "призыва голубя" }, { 157, "симулякра" }, { 161, "ментализма" },
        };

        internal const string Tip = "\n<color=#ffd76a>Ctrl+клик: сказать в командный чат, сколько раундов кд осталось. Умение при этом не применяется.</color>";

        private static float _saidAt;
        private static int _saidId;

        private static bool Held()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        internal static bool Catch(IQuickButton button)
        {
            try
            {
                if (button == null || button.QuickButtonType != EQuickButtonType.SKILL) return false;
                if (!Held() || !SideButtons.InCombat()) return false;
                if (_saidId == button.Id && Time.unscaledTime - _saidAt < 0.5f) return true;
                _saidId = button.Id;
                _saidAt = Time.unscaledTime;
                Tell(button);
                return true;
            }
            catch (Exception e) { Plugin.Trace("[кд] " + e.Message); return false; }
        }

        internal static IQuickButton Skill(int id)
        {
            try
            {
                var cd = FighterHint.Cd();
                var manager = cd != null ? cd.ButtonManager : null;
                return manager != null ? manager.Skills.GetButton(id) : null;
            }
            catch { return null; }
        }

        private static void Tell(IQuickButton button)
        {
            int left = Left(button);
            if (left <= 0)
            {
                Plugin.Trace("[кд] умение " + button.Id + " готово, в чат ничего не пишу");
                return;
            }
            string name;
            if (!Names.TryGetValue(button.Id, out name)) name = button.Name ?? "";
            string text = "кд " + name + " " + left + " " + Rounds(left);
            Post(text);
            Plugin.Trace("[кд] умение " + button.Id + ": перезарядка " + button.Recharge + ", прошло " + button.Turn
                         + ", в чат: " + text);
        }

        private static int Left(IQuickButton button)
        {
            if (button.CanActivate || button.Recharge <= 0) return 0;
            return Mathf.Max(0, button.Recharge - button.Turn);
        }

        private static string Rounds(int count)
        {
            int tens = count % 100;
            if (tens >= 11 && tens <= 14) return "раундов";
            int ones = count % 10;
            if (ones == 1) return "раунд";
            if (ones >= 2 && ones <= 4) return "раунда";
            return "раундов";
        }

        private static void Post(string text)
        {
            try { NetworkConnection.Instance.SendRequest(new ChatRequest(null, EChatMessageType.MSG_TEAM, text)); }
            catch (Exception e) { Plugin.Warn("[кд] отправка в командный чат: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "ActivateAbilityButtonHandler")]
    internal static class CooldownAbilityPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(int buttonId)
        {
            try { return !Cooldown.Catch(Cooldown.Skill(buttonId)); }
            catch (Exception e) { Plugin.Trace("[кд] кнопка умения: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(SkillList), "Fill")]
    internal static class CooldownTipPatch
    {
        private static void Postfix(SkillList __instance, int id)
        {
            try
            {
                if (!ReferenceEquals(__instance, SkillList.Abilities)) return;
                var skill = Cooldown.Skill(id);
                if (skill == null || skill.Recharge <= 0) return;
                var tip = Traverse.Create(__instance).Field("_tipText").GetValue<Text>();
                if (tip == null || string.IsNullOrEmpty(tip.text) || tip.text.Contains(Cooldown.Tip)) return;
                string text = tip.text;
                int cut = text.IndexOf('\n');
                tip.text = cut < 0 ? text + Cooldown.Tip : text.Substring(0, cut) + Cooldown.Tip + text.Substring(cut);
            }
            catch (Exception e) { Plugin.Trace("[кд] подсказка: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SkillList), "Pick")]
    internal static class CooldownPickPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(IQuickButton skill)
        {
            try { return !Cooldown.Catch(skill); }
            catch (Exception e) { Plugin.Trace("[кд] выбор умения: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(CombatButtonsController), "OnActionConfirmed")]
    internal static class CooldownSendPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(IQuickButton button)
        {
            try { return !Cooldown.Catch(button); }
            catch (Exception e) { Plugin.Trace("[кд] отправка умения: " + e.Message); return true; }
        }
    }
}
