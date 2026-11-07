using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NewAgeQoL
{
    internal static class GridWheel
    {
        private const float RowsPerNotch = 2f;
        private const float Pace = 18f;

        private static readonly AccessTools.FieldRef<Grid<InventoryThingTabContentDto>, float> MaxRef =
            AccessTools.FieldRefAccess<Grid<InventoryThingTabContentDto>, float>("_maxScrollPosition");
        private static readonly AccessTools.FieldRef<Grid<InventoryThingTabContentDto>, float> SpeedRef =
            AccessTools.FieldRefAccess<Grid<InventoryThingTabContentDto>, float>("_scrollSpeed");
        private static readonly System.Reflection.MethodInfo CellGetter =
            AccessTools.PropertyGetter(typeof(Grid<InventoryThingTabContentDto>), "CellY");

        private static Grid<InventoryThingTabContentDto> _grid;
        private static float _target;
        private static float _last;

        internal static bool Push(Grid<InventoryThingTabContentDto> grid, float notches)
        {
            if (grid == null || CellGetter == null || Mathf.Abs(notches) < 0.001f) return false;
            float max = MaxRef(grid);
            if (max <= 0f) return true;
            float cell = (float)CellGetter.Invoke(grid, null);
            if (cell <= 0f) return false;
            SpeedRef(grid) = 0f;
            float step = RowsPerNotch * cell / max;
            bool going = ReferenceEquals(grid, _grid) && Mathf.Abs(grid.ScrollPos - _last) < 0.0001f;
            float from = going ? _target : grid.ScrollPos;
            _target = Mathf.Clamp01(from - notches * step);
            _grid = grid;
            _last = grid.ScrollPos;
            return true;
        }

        internal static void Tick()
        {
            var grid = _grid;
            if (ReferenceEquals(grid, null)) return;
            try
            {
                if (grid == null || !grid.isActiveAndEnabled || Mathf.Abs(grid.ScrollPos - _last) > 0.0001f)
                {
                    _grid = null;
                    return;
                }
                float now = grid.ScrollPos;
                float next = Mathf.Lerp(now, _target, 1f - Mathf.Exp(-Pace * Time.unscaledDeltaTime));
                if (Mathf.Abs(next - _target) < 0.0005f) next = _target;
                grid.ScrollPos = next;
                _last = grid.ScrollPos;
                if (next == _target) _grid = null;
            }
            catch (Exception e)
            {
                _grid = null;
                Plugin.Trace("[wheel] " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Grid<InventoryThingTabContentDto>), "OnScroll")]
    public static class GridWheelPatch
    {
        private static bool Prefix(object __instance, PointerEventData eventData)
        {
            try
            {
                var grid = __instance as Grid<InventoryThingTabContentDto>;
                if (grid == null || eventData == null) return true;
                return !GridWheel.Push(grid, eventData.scrollDelta.y);
            }
            catch (Exception e)
            {
                Plugin.Trace("[wheel] scroll: " + e.Message);
                return true;
            }
        }
    }
}
