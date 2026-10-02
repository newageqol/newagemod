using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class OffGrid
    {
        private static readonly MethodInfo Nearest = AccessTools.Method(typeof(CombatData), "SelectNearestEnemy");
        private static readonly MethodInfo Fitment = AccessTools.Method(typeof(CombatData), "RemoveFitmentSelection");
        private static readonly FieldInfo Removed = AccessTools.Field(typeof(CombatData), "CharacterRemovedEvent");

        internal static bool Blocked(CombatData data, AbstractCharacter character)
        {
            var hex = character != null ? character.HexGridPosition : null;
            var cells = data != null ? data.Cells : null;
            if (hex == null || cells == null) return false;
            int x = hex.clientX, y = hex.clientY;
            if (x < 0 || y < 0 || x >= cells.GetLength(0) || y >= cells.GetLength(1)) return false;
            return cells[x, y] == null;
        }

        private static string Who(AbstractCharacter character) =>
            (character.Login ?? "?") + " (" + character.UserId + ") on blocked cell " + character.HexGridPosition.clientX + ";" + character.HexGridPosition.clientY;

        internal static void Place(CombatData data, AbstractCharacter character)
        {
            try
            {
                if (!Blocked(data, character) || !character.Initialized || !data.Characters.ContainsKey(character.UserId)) return;
                var grid = UnityEngine.Object.FindObjectOfType<HexGrid>();
                if (grid == null) return;
                character.position = HexUtils.offsetToPixelInWordSpace(grid.transform, character.HexGridPosition);
                character.SetParent(grid.transform, true);
                character.SetDefaultCharacterRotation();
                Plugin.Trace("[combat] " + Who(character) + " - put on its cell");
            }
            catch (Exception e) { Plugin.Trace("[combat] blocked cell placing: " + e); }
        }

        internal static bool Remove(CombatData data, int id)
        {
            AbstractCharacter value;
            if (data == null || data.Characters == null || !data.Characters.TryGetValue(id, out value) || !Blocked(data, value)) return true;
            try
            {
                Plugin.Trace("[combat] " + Who(value) + " - removing without the cell");
                data.Characters.Remove(id);
                if (data.SelectedCharacter != null && id == data.SelectedCharacter.UserId) Nearest?.Invoke(data, null);
                (Removed?.GetValue(data) as Delegate)?.DynamicInvoke(value);
                if (value.IsBot && ((BotCharacter)value).FitmentAura != null) Fitment?.Invoke(data, new object[] { value });
                if (data.RoundType == RoundType.WALK_ROUND)
                {
                    data.ClearActionSelection();
                    data.MakeWalkSelection();
                }
                value.Destroy();
            }
            catch (Exception e) { Plugin.Trace("[combat] blocked cell removing: " + e); }
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatData), "HandleOnCharacterLoaded")]
    internal static class OffGridPlacePatch
    {
        private static void Postfix(CombatData __instance, AbstractCharacter character) => OffGrid.Place(__instance, character);
    }

    [HarmonyPatch(typeof(CombatData), nameof(CombatData.RemoveCharacter))]
    internal static class OffGridRemovePatch
    {
        private static bool Prefix(CombatData __instance, int id) => OffGrid.Remove(__instance, id);
    }
}
