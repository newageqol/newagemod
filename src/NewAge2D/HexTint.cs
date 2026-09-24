using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

[HarmonyPatch]
internal static class HexTint
{
    private static bool _logged;

    [HarmonyPrefix, HarmonyPatch(typeof(CombatData), "SetSelection", new[] { typeof(OffsetCoord), typeof(int), typeof(Color), typeof(bool), typeof(bool) })]
    private static void BeforeSelection(CombatData __instance, ref Color color)
    {
        if (!Plugin.FlashFight || !Plugin.CfgFlashHexes.Value) return;
        if (color != __instance.WalkSelectionColor) return;
        var wanted = Plugin.CfgHexColor.Value;
        if (Plugin.CfgVerbose.Value && !_logged)
        {
            _logged = true;
            var view = CombatView.Get();
            var grid = view != null ? view.HexGrid : null;
            var renderer = grid != null ? grid.GetComponent<MeshRenderer>() : null;
            string shader = renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null ? renderer.sharedMaterial.shader.name : "?";
            Plugin.Log.LogInfo($"[гексы] цвет хода карты {color} → {wanted}; шейдер сетки {shader}");
        }
        color = wanted;
    }
}
