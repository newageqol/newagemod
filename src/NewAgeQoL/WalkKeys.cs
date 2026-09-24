using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class WalkKeys
    {
        private const int Up = 0;
        private const int Right = 1;
        private const int Down = 2;
        private const int Left = 3;

        private const float Fits = 0.35f;
        private const float Bend = 4f;
        private const float Nudge = 0.02f;

        private static OffsetCoord _pick;
        private static Vector3 _mouse;
        private static int _round = -1;
        private static int _ate = -1;
        private static HexGridControl _pad;
        private static CameraControl _rig;
        private static Transform _grid;
        private static readonly List<OffsetCoord> Zone = new List<OffsetCoord>();

        internal static bool Enabled => Plugin.CfgWalkKeys == null || Plugin.CfgWalkKeys.Value;

        internal static OffsetCoord Pick => _pick;

        internal static bool Busy => _ate == Time.frameCount;

        internal static void Drop()
        {
            _pick = null;
        }

        internal static bool EscapeClose()
        {
            if (_pick == null) return false;
            Drop();
            return true;
        }

        internal static void Tick()
        {
            Keys();
            if (_pick == null) return;
            try
            {
                if (!Enabled || !SideButtons.InCombat() || Spectate.Peeking || SkillList.Armed) { Drop(); return; }
                var cd = FighterHint.Cd();
                if (cd == null || cd.RoundType != RoundType.WALK_ROUND) { Drop(); return; }
                if (cd.RoundNum != _round) { Drop(); return; }
                if ((Input.mousePosition - _mouse).sqrMagnitude > 64f) { Drop(); return; }
                if (!Walkable(cd, _pick)) { Drop(); return; }
            }
            catch (Exception e) { Plugin.Trace("[ходьба] выбор клетки: " + e.Message); Drop(); }
        }

        private static void Keys()
        {
            try
            {
                if (!Enabled || !Input.anyKeyDown) return;
                if (Hotkeys.Capturing || Settings.Capturing || Typing() || Roster.Asking) return;
                if (Input.GetKeyDown(KeyCode.UpArrow)) { Step(Up); return; }
                if (Input.GetKeyDown(KeyCode.DownArrow)) { Step(Down); return; }
                if (Input.GetKeyDown(KeyCode.LeftArrow)) { Step(Left); return; }
                if (Input.GetKeyDown(KeyCode.RightArrow)) { Step(Right); return; }
                bool main = Input.GetKeyDown(KeyCode.Return);
                bool pad = Input.GetKeyDown(KeyCode.KeypadEnter);
                bool typed = !main && !pad && Crlf();
                if (!main && !pad && !typed) return;
                Plugin.Trace("[ходьба] ввод: " + (main ? "Enter" : pad ? "Enter на намлоке" : "Enter по символу")
                    + ", клетка " + (_pick != null ? _pick.clientX + ";" + _pick.clientY : "не выбрана"));
                Go();
            }
            catch (Exception e) { Plugin.Trace("[ходьба] клавиши: " + e.Message); }
        }

        private static bool Crlf()
        {
            string typed = Input.inputString;
            if (string.IsNullOrEmpty(typed)) return false;
            for (int i = 0; i < typed.Length; i++)
                if (typed[i] == (char)13 || typed[i] == (char)10) return true;
            return false;
        }

        private static bool Typing()
        {
            try
            {
                var system = EventSystem.current;
                var picked = system != null ? system.currentSelectedGameObject : null;
                return picked != null && picked.GetComponent<InputField>() != null;
            }
            catch { return false; }
        }

        private static void Step(int way)
        {
            try
            {
                if (!Enabled) return;
                if (!SideButtons.InCombat() || Spectate.Peeking || SkillList.Armed) return;
                var cd = FighterHint.Cd();
                if (cd == null) return;
                if (cd.RoundType != RoundType.WALK_ROUND) { Plugin.Trace("[ходьба] сейчас не фаза ходьбы"); return; }
                var me = cd.MyCharacter;
                if (me == null) return;

                Build(cd, me.HexGridPosition);
                if (Zone.Count == 0) { Plugin.Trace("[ходьба] ходить некуда"); return; }

                if (_pick != null && (cd.RoundNum != _round || !Walkable(cd, _pick))) Drop();
                var from = _pick;
                if (from == null)
                {
                    OffsetCoord under;
                    from = SkillList.HexUnder(out under) && Walkable(cd, under) ? under : me.HexGridPosition;
                }

                Vector2 home;
                if (!Spot(from, out home)) { Plugin.Trace("[ходьба] клетку не перевести в экранную точку"); return; }

                float wide, high;
                if (!Scale(from, home, out wide, out high)) { Plugin.Trace("[ходьба] размер клетки на экране не посчитать"); return; }

                bool tall = way == Up || way == Down;
                float wantX = way == Right ? 1f : way == Left ? -1f : 0f;
                float wantY = way == Up ? 1f : way == Down ? -1f : 0f;
                float turn = ((tall ? from.clientY : from.clientX) & 1) == 0 ? 1f : -1f;

                OffsetCoord best = null;
                float mark = float.MaxValue;
                foreach (var one in Zone)
                {
                    if (one.Equals(from)) continue;
                    Vector2 spot;
                    if (!Spot(one, out spot)) continue;
                    float stepX = (spot.x - home.x) / wide;
                    float stepY = (spot.y - home.y) / high;
                    float span = Mathf.Sqrt(stepX * stepX + stepY * stepY);
                    if (span < 0.2f) continue;
                    float fit = (stepX * wantX + stepY * wantY) / span;
                    if (fit < Fits) continue;
                    int far = HexUtils.range(from, one);
                    if (far < 1 || far > 100) continue;
                    float lean = Mathf.Sign(stepX * wantY - stepY * wantX) * turn;
                    float score = far + (1f - fit) * Bend + lean * Nudge;
                    if (score >= mark) continue;
                    mark = score;
                    best = one;
                }

                if (best == null) { Plugin.Trace("[ходьба] в эту сторону идти некуда"); return; }
                Plugin.Trace("[ходьба] выбор " + best.clientX + ";" + best.clientY + ", шагов " + HexUtils.range(from, best));

                _pick = best;
                _round = cd.RoundNum;
                _mouse = Input.mousePosition;
            }
            catch (Exception e) { Plugin.Trace("[ходьба] выбор клетки: " + e.Message); }
        }

        internal static void Go()
        {
            try
            {
                if (!Enabled) return;
                var hex = _pick;
                if (hex == null) return;
                _ate = Time.frameCount;
                var cd = FighterHint.Cd();
                if (cd == null || cd.RoundType != RoundType.WALK_ROUND) { Drop(); return; }
                if (!Walkable(cd, hex)) { Notice.Show("До этой клетки не дойти", 3f); Drop(); return; }
                var units = cd.TimeUnitsManager;
                if (units != null && units.MovementTimeUnits <= 0) { Notice.Show("Ходов не осталось", 3f); return; }
                Drop();
                Ring(hex);
            }
            catch (Exception e) { Plugin.Trace("[ходьба] шаг: " + e.Message); }
        }

        private static void Build(ICombatData cd, OffsetCoord centre)
        {
            Zone.Clear();
            int reach = cd.TimeUnitsManager != null ? cd.TimeUnitsManager.CalculateWalkDistance() : 0;
            if (reach <= 0) return;
            foreach (var one in HexUtils.getRoundRange(centre, reach))
                if (Walkable(cd, one)) Zone.Add(one);
        }

        private static bool Scale(OffsetCoord hex, Vector2 home, out float wide, out float high)
        {
            wide = 0f;
            high = 0f;
            for (int k = 0; k < 6; k++)
            {
                Vector2 side;
                if (!Spot(HexUtils.getNeighbor(hex, k), out side)) continue;
                var step = side - home;
                if (step.sqrMagnitude < 1f) continue;
                if (Mathf.Abs(step.x) > wide) wide = Mathf.Abs(step.x);
                if (Mathf.Abs(step.y) > high) high = Mathf.Abs(step.y);
            }
            return wide > 1f && high > 1f;
        }

        private static bool Inside(ICombatData cd, OffsetCoord hex)
        {
            var cells = cd.Cells;
            if (cells == null || hex == null) return false;
            return hex.clientX >= 0 && hex.clientY >= 0
                && hex.clientX < cells.GetLength(0) && hex.clientY < cells.GetLength(1);
        }

        private static bool Walkable(ICombatData cd, OffsetCoord hex)
        {
            try { return Inside(cd, hex) && cd.IsCellInActionSelection(hex); }
            catch { return false; }
        }

        private static bool Spot(OffsetCoord hex, out Vector2 screen)
        {
            screen = Vector2.zero;
            var grid = Grid();
            var eye = Eye();
            if (grid == null || eye == null) return false;
            var point = eye.WorldToScreenPoint(HexUtils.offsetToPixelInWordSpace(grid, hex));
            if (point.z <= 0f) return false;
            screen = new Vector2(point.x, point.y);
            return true;
        }

        private static HexGridControl Pad()
        {
            if (_pad != null) return _pad;
            try { _pad = UnityEngine.Object.FindObjectOfType<HexGridControl>(); }
            catch (Exception e) { Plugin.Trace("[ходьба] поле боя: " + e.Message); }
            return _pad;
        }

        private static Transform Grid()
        {
            var pad = Pad();
            if (pad != null) return pad.transform;
            if (_grid != null) return _grid;
            try
            {
                if (_rig == null) _rig = UnityEngine.Object.FindObjectOfType<CameraControl>();
                _grid = _rig != null ? AccessTools.Property(typeof(BaseUserInput), "gridTransform")?.GetValue(_rig) as Transform : null;
            }
            catch (Exception e) { Plugin.Trace("[ходьба] сетка боя: " + e.Message); }
            return _grid;
        }

        internal static Camera Eye()
        {
            var pad = Pad();
            var eye = pad != null ? pad.gridCamera : null;
            return eye != null ? eye : Camera.main;
        }

        private static void Ring(OffsetCoord hex)
        {
            var pad = Pad();
            if (pad == null) { Plugin.Trace("[ходьба] поля боя нет, шаг не отправить"); return; }
            var call = Bell(pad);
            if (call == null) { Plugin.Trace("[ходьба] отклика поля нет, шаг не отправить"); return; }
            Plugin.Trace("[ходьба] шаг на клетку " + hex.clientX + ";" + hex.clientY);
            call(new HexClickInfo(hex));
        }

        private static Action<HexClickInfo> Bell(HexGridControl pad)
        {
            try
            {
                var field = AccessTools.Field(typeof(HexGridControl), "HexClickEvent");
                if (field != null) return field.GetValue(pad) as Action<HexClickInfo>;
                foreach (var one in typeof(HexGridControl).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    if (typeof(Action<HexClickInfo>).IsAssignableFrom(one.FieldType))
                        return one.GetValue(pad) as Action<HexClickInfo>;
            }
            catch (Exception e) { Plugin.Trace("[ходьба] отклик поля: " + e.Message); }
            return null;
        }
    }
}
