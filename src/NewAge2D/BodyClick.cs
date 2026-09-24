using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAge2D;

[HarmonyPatch]
internal static class BodyClick
{
    private static readonly string[] Shape =
    {
        "      ##          ",
        "     #..#         ",
        "     #..#         ",
        "     #..#         ",
        "     #..#         ",
        "     #..###       ",
        "     #..#..###    ",
        "     #..#..#..##  ",
        " ### #..#..#..#.# ",
        " #..##........#.# ",
        " #...#..........# ",
        "  #.............# ",
        "  #.............# ",
        "   #............# ",
        "   #...........#  ",
        "    #..........#  ",
        "    #.........#   ",
        "     #........#   ",
        "     #........#   ",
        "     ##########   ",
    };

    private static readonly System.Reflection.MethodInfo Moved = AccessTools.Method(typeof(HexGridControl), "PointerMoved");
    private static readonly System.Reflection.MethodInfo Fire = AccessTools.Method(typeof(HexGridControl), "FireClickEvent");
    private static readonly List<RaycastResult> Hits = new();
    private static readonly Vector3[] Corners = new Vector3[8];
    private static readonly Texture2D[] Hands = new Texture2D[3];
    private static int _scale;
    private static bool _shown;
    private static int _alone = -1;
    private static PointerEventData _pointer;
    private static EventSystem _pointerSystem;

    private static bool Alone
    {
        get
        {
            if (_alone < 0) _alone = Chainloader.PluginInfos.ContainsKey("newage.qol") ? 0 : 1;
            return _alone == 1;
        }
    }

    internal static void Tick()
    {
        if (!Alone) return;
        bool over = false;
        try { over = Plugin.FlashFight && Fighters.Combat() != null && !OverUi() && Body() != null; }
        catch (Exception ex) { Plugin.Log.LogWarning("[курсор] " + ex.Message); }
        if (over == _shown) return;
        _shown = over;
        try
        {
            if (over) Cursor.SetCursor(Hand(), new Vector2(6f * _scale, 0f), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[курсор] " + ex.Message); }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(HexGridControl), "OnPointerClick")]
    private static bool ClickBody(HexGridControl __instance)
    {
        if (!Alone || !Plugin.FlashFight || Fire == null) return true;
        try
        {
            if (Moved != null && Moved.Invoke(__instance, null) is bool moved && moved) return true;
            var combat = Fighters.Combat();
            var body = Body(combat == null || combat.RoundType != RoundType.WALK_ROUND);
            var hex = body != null ? body.HexGridPosition : null;
            if (hex == null) return true;
            BaseStateButton.CloseDefaultButtonGroup();
            if (Trace.On) Trace.Write($"«{body.Login}» клик по телу, выбрана клетка {hex.clientX};{hex.clientY}");
            Fire.Invoke(__instance, new object[] { hex });
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[клик по телу] " + ex.Message);
            return true;
        }
    }

    internal static AbstractCharacter Body() => Body(true);

    internal static AbstractCharacter Body(bool dead)
    {
        var location = CombatView.Get();
        var eye = location != null ? location.CombatCamera : null;
        var combat = Fighters.Combat();
        if (eye == null || combat == null) return null;
        var mouse = (Vector2)Input.mousePosition;
        var cell = Cell(location, eye, mouse);
        AbstractCharacter best = null, standing = null, corpse = null, lying = null;
        float near = float.MaxValue;
        float low = float.MaxValue;
        foreach (var character in combat.Characters.Values)
        {
            if (character == null || character.UserId == 0 || !character.Initialized) continue;
            if (character.Dead && !dead) continue;
            bool shape = Covers(eye, character, mouse);
            bool here = !shape && cell != null && character.HexGridPosition != null && character.HexGridPosition.Equals(cell);
            if (!shape && !here) continue;
            if (here)
            {
                if (character.Dead) lying = character;
                else standing = character;
                continue;
            }
            float far = eye.WorldToScreenPoint(character.position).z;
            if (character.Dead)
            {
                if (far >= low) continue;
                low = far;
                corpse = character;
                continue;
            }
            if (far >= near) continue;
            near = far;
            best = character;
        }
        return best ?? standing ?? corpse ?? lying;
    }

    private static bool Covers(Camera eye, AbstractCharacter character, Vector2 mouse)
    {
        var doll = Fighters.DollOf(character);
        if (doll != null && doll.HasPicture) return doll.Covers(eye, mouse);
        var body = character.CharacterCollider;
        if (body == null || !body.gameObject.activeInHierarchy) return false;
        return body.Raycast(eye.ScreenPointToRay(mouse), out _, 1000f);
    }

    private static OffsetCoord Cell(CombatLocationView location, Camera eye, Vector2 mouse)
    {
        var grid = location.HexGrid != null ? location.HexGrid.transform : null;
        if (grid == null) return null;
        var ray = eye.ScreenPointToRay(mouse);
        var floor = new Plane(grid.up, grid.position);
        if (!floor.Raycast(ray, out float along)) return null;
        return HexUtils.pixelToOffset(grid.InverseTransformPoint(ray.GetPoint(along)));
    }

    internal static bool OverUi()
    {
        var system = EventSystem.current;
        if (system == null) return false;
        if (_pointer == null || _pointerSystem != system)
        {
            _pointer = new PointerEventData(system);
            _pointerSystem = system;
        }
        _pointer.position = Input.mousePosition;
        Hits.Clear();
        system.RaycastAll(_pointer, Hits);
        foreach (var hit in Hits)
            if (hit.module is GraphicRaycaster) return true;
        return false;
    }

    private static Texture2D Hand()
    {
        int scale = Screen.height > 1500 ? 2 : 1;
        _scale = scale;
        if (Hands[scale] != null) return Hands[scale];
        int size = 32 * scale;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var clear = new Color(0f, 0f, 0f, 0f);
        var edge = new Color(0f, 0f, 0f, 1f);
        var fill = new Color(1f, 1f, 1f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                texture.SetPixel(x, y, clear);
        for (int row = 0; row < Shape.Length; row++)
            for (int column = 0; column < Shape[row].Length; column++)
            {
                char mark = Shape[row][column];
                if (mark == ' ') continue;
                var color = mark == '#' ? edge : fill;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        texture.SetPixel(column * scale + dx, size - 1 - row * scale - dy, color);
            }
        texture.Apply();
        Hands[scale] = texture;
        return texture;
    }
}
