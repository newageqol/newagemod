using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
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

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();
        private static readonly Texture2D[] Hands = new Texture2D[3];
        private static PointerEventData _pointer;
        private static EventSystem _pointerSystem;
        private static int _scale;
        private static bool _shown;

        private static readonly Color Lit = new Color(0.45f, 0.95f, 0.45f, 1f);
        private static ICombatData _litData;
        private static int _litPick;
        private static int _litFor;
        private static OffsetCoord _litAt;
        private static Renderer _tinted;
        private static MaterialPropertyBlock _paint;

        internal static int LitId => _litPick;

        internal static void Tick()
        {
            bool over = false;
            ICombatData cd = null;
            AbstractCharacter body = null;
            try
            {
                cd = FighterHint.Cd();
                bool armed = SkillList.Armed;
                if (!armed) FlashLook.Hint(0);
                if (cd != null && SideButtons.InCombat() && !armed && !OverUi())
                {
                    body = Hovered(cd);
                    over = body != null || Walk(cd);
                }
            }
            catch (Exception e) { Plugin.Trace("[боец] курсор: " + e.Message); }
            Light(cd, body);
            if (over == _shown) return;
            _shown = over;
            try
            {
                if (over)
                {
                    var hand = Hand();
                    Cursor.SetCursor(hand, new Vector2(6f * _scale, 0f), CursorMode.Auto);
                }
                else if (!SkillList.Armed) Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
            catch (Exception e) { Plugin.Trace("[боец] курсор: " + e.Message); }
        }

        private static void Light(ICombatData cd, AbstractCharacter body)
        {
            var at = body != null ? body.HexGridPosition : null;
            if (body != null && at != null && ReferenceEquals(cd, _litData) && _litFor == body.UserId && _litPick != 0 && at.Equals(_litAt)) return;
            Unlight(cd);
            if (cd == null || body == null || at == null) return;
            try { _litPick = cd.SetSelection(at, 0, Lit, true, true); }
            catch (Exception e) { Plugin.Trace("[боец] подсветка клетки: " + e.Message); _litPick = 0; }
            _litData = cd;
            _litFor = body.UserId;
            _litAt = at;
            if (!FlashLook.Fight) Tint(body);
        }

        private static void Unlight(ICombatData cd)
        {
            if (_litPick != 0 && _litData != null && ReferenceEquals(_litData, cd))
            {
                try { _litData.ClearSelection(_litPick); }
                catch (Exception e) { Plugin.Trace("[боец] снять подсветку клетки: " + e.Message); }
            }
            _litPick = 0;
            _litFor = 0;
            _litAt = null;
            _litData = null;
            if (_tinted != null)
            {
                try { _tinted.SetPropertyBlock(null); }
                catch { }
                _tinted = null;
            }
        }

        private static void Tint(AbstractCharacter body)
        {
            try
            {
                var holder = body.CharacterMeshRendererHolder;
                var skin = holder != null ? holder.MainRenderer : null;
                if (skin == null) return;
                if (_paint == null) _paint = new MaterialPropertyBlock();
                skin.GetPropertyBlock(_paint);
                _paint.SetColor("_Color", Lit);
                _paint.SetColor("_BaseColor", Lit);
                _paint.SetColor("_TintColor", Lit);
                _paint.SetColor("_EmissionColor", new Color(0.04f, 0.22f, 0.06f, 1f));
                _paint.SetColor("_RimColor", Lit);
                _paint.SetColor("_OutlineColor", Lit);
                skin.SetPropertyBlock(_paint);
                _tinted = skin;
            }
            catch (Exception e) { Plugin.Trace("[боец] подсветка модели: " + e.Message); }
        }

        private static AbstractCharacter Hovered(ICombatData cd)
        {
            AbstractCharacter flash;
            return FlashLook.Under(out flash) ? flash : Body(cd);
        }

        private static bool Walk(ICombatData cd)
        {
            if (cd.RoundType != RoundType.WALK_ROUND || Spectate.Peeking) return false;
            return SkillList.HexUnder(out var hex) && WalkHex.Walkable(cd, hex);
        }

        internal static AbstractCharacter Body(ICombatData cd)
        {
            var camera = FighterHint.Eye();
            if (camera == null || cd == null || cd.Characters == null) return null;
            return FighterHint.Ray(cd, camera, true) ?? FighterHint.OnHex(cd, true);
        }

        private static bool OverUi()
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
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0f, 0f, 0f, 0f);
            var edge = new Color(0f, 0f, 0f, 1f);
            var fill = new Color(1f, 1f, 1f, 1f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, clear);
            for (int row = 0; row < Shape.Length; row++)
                for (int column = 0; column < Shape[row].Length; column++)
                {
                    char mark = Shape[row][column];
                    if (mark == ' ') continue;
                    var color = mark == '#' ? edge : fill;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                            tex.SetPixel(column * scale + dx, size - 1 - row * scale - dy, color);
                }
            tex.Apply();
            Hands[scale] = tex;
            return tex;
        }
    }

    [HarmonyPatch(typeof(HexGridControl), "OnPointerClick")]
    internal static class BodyClickPatch
    {
        private static readonly System.Reflection.MethodInfo Moved = AccessTools.Method(typeof(HexGridControl), "PointerMoved");
        private static readonly System.Reflection.MethodInfo Fire = AccessTools.Method(typeof(HexGridControl), "FireClickEvent");

        private static bool Prefix(HexGridControl __instance)
        {
            try
            {
                if (Fire == null || SkillList.Armed) return true;
                if (Moved != null && Moved.Invoke(__instance, null) is bool moved && moved) return true;
                var cd = FighterHint.Cd();
                var body = cd != null ? BodyClick.Body(cd) : null;
                var hex = body != null ? body.HexGridPosition : null;
                if (hex == null) return true;
                BaseStateButton.CloseDefaultButtonGroup();
                Plugin.Trace("[боец] клик по телу: " + body.Login + ", клетка " + hex.clientX + ";" + hex.clientY);
                Fire.Invoke(__instance, new object[] { hex });
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[боец] клик по телу: " + e.Message);
                return true;
            }
        }
    }
}
