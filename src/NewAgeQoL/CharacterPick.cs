using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.Authentication;

namespace NewAgeQoL
{
    internal static class CharacterPick
    {
        internal sealed class Hero
        {
            internal int UserId;
            internal string Login;
            internal int Level;
            internal int ClassId;
            internal bool Banned;
        }

        internal static readonly List<Hero> Known = new List<Hero>();
        internal static int Target;

        internal static void Remember(int userId)
        {
            if (userId <= 0 || Plugin.CfgLastCharacter == null) return;
            Plugin.CfgLastCharacter.Value = userId;
            Plugin.Trace("[персонаж] запомнен id " + userId);
            Chars.Use(userId);
        }

        internal static void Preselect(CharacterSelectorComponent selector, AvailableCharactersResponseMessage message)
        {
            try
            {
                Learn(message);
                if (selector == null || message?.Items == null) return;
                if (Target > 0) { Enter(selector, message); return; }
                int want = Plugin.CfgLastCharacter?.Value ?? 0;
                if (want <= 0) return;
                int index = Find(message, want);
                if (index <= 0) return;
                Pick(selector, index);
                Plugin.Trace("[персонаж] на экране выбора показан " + message.Items[index].Login);
            }
            catch (Exception e) { Plugin.Trace("[персонаж] выбор: " + e.Message); }
        }

        private static void Learn(AvailableCharactersResponseMessage message)
        {
            if (message?.Items == null) return;
            Known.Clear();
            foreach (var item in message.Items)
            {
                if (item == null || item.UserId <= 0) continue;
                Known.Add(new Hero
                {
                    UserId = item.UserId,
                    Login = item.Login ?? "",
                    Level = item.Level,
                    ClassId = item.ClassId.GetValueOrDefault(),
                    Banned = item.Banned == true,
                });
            }
            Known.Sort((a, b) => a.UserId.CompareTo(b.UserId));
            var line = new System.Text.StringBuilder();
            foreach (var one in Known) line.Append(line.Length > 0 ? ", " : "").Append(one.Login).Append(" класс ").Append(one.ClassId);
            Plugin.Trace("[персонаж] персонажей на аккаунте: " + Known.Count + " (" + line + ")");
        }

        private static int Find(AvailableCharactersResponseMessage message, int userId)
        {
            for (int i = 0; i < message.Items.Count; i++)
                if (message.Items[i] != null && message.Items[i].UserId == userId) return i;
            return -1;
        }

        private static void Pick(CharacterSelectorComponent selector, int index)
        {
            AccessTools.Field(typeof(CharacterSelectorComponent), "_selectedIndex")?.SetValue(selector, index);
            AccessTools.Method(typeof(CharacterSelectorComponent), "UpdateCurrentUserWidget")?.Invoke(selector, null);
        }

        private static void Enter(CharacterSelectorComponent selector, AvailableCharactersResponseMessage message)
        {
            int want = Target;
            Target = 0;
            int index = Find(message, want);
            if (index < 0 || message.Items[index].Banned == true)
            {
                Plugin.Trace("[персонаж] персонажа " + want + " нет в списке или он заблокирован, остаюсь на экране выбора");
                return;
            }
            Pick(selector, index);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Press(selector, want));
        }

        private static IEnumerator Press(CharacterSelectorComponent selector, int userId)
        {
            yield return null;
            if (selector == null) yield break;
            try
            {
                AccessTools.Method(typeof(CharacterSelectorComponent), "FireCharacterSelected")?.Invoke(selector, new object[] { userId });
                Plugin.Trace("[персонаж] вход персонажем " + userId + " сразу, без выбора");
            }
            catch (Exception e) { Plugin.Warn("[персонаж] вход: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CharacterSelectorComponent), "UpdateCharacters")]
    public static class CharacterPickListPatch
    {
        private static void Postfix(CharacterSelectorComponent __instance, AvailableCharactersResponseMessage message) =>
            CharacterPick.Preselect(__instance, message);
    }

    [HarmonyPatch(typeof(CharacterSelectorComponent), "FireCharacterSelected")]
    public static class CharacterPickEnterPatch
    {
        private static void Prefix(int __0) => CharacterPick.Remember(__0);
    }
}
