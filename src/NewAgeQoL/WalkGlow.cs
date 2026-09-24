using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class WalkGlow
    {
        private const float Grain = 0.004f;
        private const float Linger = 1f;
        private const int Corners = 7;

        private sealed class Ring
        {
            internal int Id;
            internal int X;
            internal int Y;
        }

        private static readonly Color Blank = new Color(0f, 0f, 0f);
        private static readonly Color Combat = new Color(0.5f, 0f, 0f);
        private static readonly Color Good = Color.yellow;
        private static readonly Color Harm = Color.red;
        private static readonly Dictionary<Cell, Color> Want = new Dictionary<Cell, Color>();
        private static readonly List<Cell> Fix = new List<Cell>();
        private static readonly List<int> Extra = new List<int>();
        private static readonly HashSet<Cell> Now = new HashSet<Cell>();
        private static readonly HashSet<Cell> Had = new HashSet<Cell>();
        private static readonly Dictionary<int, Ring> Rings = new Dictionary<int, Ring>();
        private static readonly HashSet<int> Kept = new HashSet<int>();
        private static readonly HashSet<int> Live = new HashSet<int>();
        private static readonly List<int> Lost = new List<int>();
        private static readonly List<BotCharacter> Moved = new List<BotCharacter>();
        private static readonly Dictionary<GameObject, AbstractCharacter> Marks = new Dictionary<GameObject, AbstractCharacter>();
        private static readonly List<GameObject> Stale = new List<GameObject>();
        private static readonly List<Color> Paints = new List<Color>();

        private static float _at;
        private static float _meshAt;
        private static bool _soon;
        private static bool _said;
        private static bool _noted;
        private static HexGrid _grid;
        private static MeshFilter _filter;
        private static Type _cdType;
        private static System.Reflection.FieldInfo _boxField;
        private static System.Reflection.FieldInfo _ownField;
        private static System.Reflection.FieldInfo _paintField;
        private static System.Reflection.FieldInfo _gridField;
        private static float _phaseAt;
        private static int _missId;
        private static int _miss;
        private static System.Reflection.MethodInfo _ring;
        private static bool _ringSaid;
        private static System.Reflection.FieldInfo _mark;
        private static float _markAt;
        private static System.Reflection.FieldInfo _aimField;
        private static Color _walkPaint;
        private static bool _walkKnown;

        internal static bool Enabled => Plugin.CfgWalkGlow == null || Plugin.CfgWalkGlow.Value;

        internal static void Phase()
        {
            _phaseAt = Time.unscaledTime;
            _miss = 0;
            _noted = false;
        }

        internal static void Soon()
        {
            _soon = true;
            _meshAt = 0f;
        }

        internal static void Dirty()
        {
            _meshAt = 0f;
        }

        internal static void Tick()
        {
            if (!Enabled) return;
            if (!_soon && Time.unscaledTime < _at) return;
            bool fresh = _soon;
            _soon = false;
            _at = Time.unscaledTime + 0.25f;
            try
            {
                if (!SideButtons.InCombat()) { Rings.Clear(); Marks.Clear(); return; }
                var cd = FighterHint.Cd();
                if (cd == null) return;
                var box = Box(cd);
                if (box == null) return;
                if (fresh) Census(cd, box);
                Auras(cd, box);
                Wipe(cd, box);
                Ghosts(cd);
                Walk(cd, box);
                Rub(cd, box);
                if (Time.unscaledTime >= _meshAt)
                {
                    _meshAt = Time.unscaledTime + 1f;
                    Canvas(cd);
                }
                Note(cd, box);
            }
            catch (Exception e) { Plugin.Trace("[подсветка] уборка: " + e.Message); }
        }

        private static void Bind(ICombatData cd)
        {
            var type = cd.GetType();
            if (ReferenceEquals(type, _cdType)) return;
            _cdType = type;
            _boxField = AccessTools.Field(type, "_selections");
            _ownField = AccessTools.Field(type, "_actionSelectionId");
            _paintField = AccessTools.Field(type, "InvalidateCellsEvent");
        }

        private static Dictionary<int, GridSelection> Box(ICombatData cd)
        {
            Bind(cd);
            var box = (_boxField != null ? _boxField.GetValue(cd) : null) as Dictionary<int, GridSelection>;
            if (box == null && !_said)
            {
                _said = true;
                Plugin.Trace("[подсветка] списка выделений не видно");
            }
            return box;
        }

        private static void Wipe(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            int own = Own(cd);
            bool walking = cd.RoundType == RoundType.WALK_ROUND;
            GridSelection mine = null;
            if (own != 0) box.TryGetValue(own, out mine);
            bool haveMine = mine != null;

            if (walking && haveMine)
            {
                _walkPaint = mine.SelectionColor;
                _walkKnown = true;
            }
            else if (!walking && haveMine && _walkKnown && Same(mine.SelectionColor, _walkPaint))
            {
                cd.ClearActionSelection();
                cd.MakeCombatSelection();
                Plugin.Trace("[подсветка] в фазе боя осталась зона ходьбы, своё выделение пересобрано");
                return;
            }

            Live.Clear();
            if (own != 0) Live.Add(own);
            foreach (int id in Kept) Live.Add(id);
            SkillList.Aims(Live);
            if (BodyClick.LitId != 0) Live.Add(BodyClick.LitId);
            int aimed = Aimed();
            if (aimed != 0) Live.Add(aimed);

            Extra.Clear();
            foreach (var pair in box)
                if (!Live.Contains(pair.Key)) Extra.Add(pair.Key);
            if (Extra.Count == 0) return;

            foreach (int id in Extra) cd.ClearSelection(id);
            Plugin.Trace("[подсветка] снято выделений, за которыми никто не стоит: " + Extra.Count
                + (walking ? ", фаза ходьбы" : ", фаза боя"));
        }

        private static int Aimed()
        {
            try
            {
                var dialog = Controllers.Get<ConfirmActionDialogController>();
                if (dialog == null || !dialog.Opened) return 0;
                if (_aimField == null) _aimField = AccessTools.Field(typeof(ConfirmActionDialogController), "_cellSelection");
                var got = _aimField != null ? _aimField.GetValue(dialog) : null;
                return got is int id ? id : 0;
            }
            catch { return 0; }
        }

        private static void Auras(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            var folk = cd.Characters;
            if (folk == null) return;

            Kept.Clear();
            Moved.Clear();
            foreach (var one in folk.Values)
            {
                var bot = one as BotCharacter;
                if (bot == null || bot.FitmentAura == null) continue;
                int id = bot.FitmentAura.SelectionId;
                var spot = bot.HexGridPosition;
                if (id == 0 || spot == null) continue;
                Ring ring;
                if (!Rings.TryGetValue(bot.UserId, out ring) || ring.Id != id)
                {
                    Rings[bot.UserId] = new Ring { Id = id, X = spot.clientX, Y = spot.clientY };
                    Kept.Add(id);
                    continue;
                }
                if (ring.X == spot.clientX && ring.Y == spot.clientY) { Kept.Add(id); continue; }
                Moved.Add(bot);
            }

            foreach (var bot in Moved)
            {
                Ring ring;
                if (!Rings.TryGetValue(bot.UserId, out ring)) continue;
                var spot = bot.HexGridPosition;
                if (spot == null) continue;
                int fresh = Remake(cd, bot, ring.Id);
                ring.Id = fresh;
                ring.X = spot.clientX;
                ring.Y = spot.clientY;
                if (fresh != 0) Kept.Add(fresh);
                Plugin.Trace("[подсветка] круг вокруг бойца " + bot.UserId + " ушёл за ним в клетку "
                    + spot.clientX + ";" + spot.clientY + (fresh == 0 ? ", заново не собрался" : ""));
            }

            Lost.Clear();
            foreach (var pair in box)
            {
                var one = pair.Value;
                if (one == null) continue;
                if (!Same(one.SelectionColor, Good) && !Same(one.SelectionColor, Harm)) continue;
                if (Kept.Contains(pair.Key)) continue;
                Lost.Add(pair.Key);
            }
            if (Lost.Count == 0) return;
            foreach (int id in Lost) cd.ClearSelection(id);
            Plugin.Trace("[подсветка] снято кругов без хозяина: " + Lost.Count);
        }

        private static int Remake(ICombatData cd, BotCharacter bot, int id)
        {
            try
            {
                cd.ClearSelection(id);
                if (_ring == null)
                    _ring = AccessTools.Method(cd.GetType(), "MakeFitmentSelection", new[] { typeof(BotCharacter) });
                if (_ring == null)
                {
                    if (!_ringSaid)
                    {
                        _ringSaid = true;
                        Plugin.Trace("[подсветка] круг заново не собрать: игра прячет сборку");
                    }
                    return 0;
                }
                _ring.Invoke(cd, new object[] { bot });
                return bot.FitmentAura.SelectionId;
            }
            catch (Exception e)
            {
                Plugin.Trace("[подсветка] круг вокруг бойца " + bot.UserId + ": " + e.Message);
                return 0;
            }
        }

        internal static void Mark(AbstractCharacter owner)
        {
            if (owner == null) return;
            if (_mark == null) _mark = AccessTools.Field(typeof(AbstractCharacter), "_selectionObject");
            var ring = _mark != null ? _mark.GetValue(owner) as GameObject : null;
            if (ring == null) return;
            if (Marks.Count > 64) Marks.Clear();
            Marks[ring] = owner;
        }

        private static void Ghosts(ICombatData cd)
        {
            if (Marks.Count == 0 || Time.unscaledTime < _markAt) return;
            _markAt = Time.unscaledTime + 0.5f;
            var picked = cd.SelectedCharacter;
            Stale.Clear();
            foreach (var pair in Marks)
            {
                var ring = pair.Key;
                if (ring == null) { Stale.Add(ring); continue; }
                if (ReferenceEquals(pair.Value, picked) && Here(cd, pair.Value)) continue;
                Stale.Add(ring);
                UnityEngine.Object.Destroy(ring);
                Plugin.Trace("[подсветка] снят кружок выделения без хозяина: боец "
                    + (pair.Value != null ? pair.Value.UserId.ToString() : "?") + " в бою больше не выбран");
            }
            foreach (var ring in Stale) Marks.Remove(ring);
        }

        private static bool Here(ICombatData cd, AbstractCharacter one)
        {
            if (one == null) return false;
            var folk = cd.Characters;
            AbstractCharacter now;
            return folk != null && folk.TryGetValue(one.UserId, out now) && ReferenceEquals(now, one);
        }

        private static void Walk(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            if (cd.RoundType != RoundType.WALK_ROUND) { _miss = 0; return; }
            int own = Own(cd);
            GridSelection mine;
            if (own == 0 || !box.TryGetValue(own, out mine) || mine == null) { _miss = 0; return; }
            var me = cd.MyCharacter;
            var cells = cd.Cells;
            if (me == null || cells == null) return;

            int reach = me.Dead ? 0 : cd.TimeUnitsManager.CalculateWalkDistance();
            Now.Clear();
            if (reach > 0)
                foreach (var cell in CellUtils.getRoundRange(cells, me.HexGridPosition, reach, false, false))
                    if (cell != null) Now.Add(cell);
            Had.Clear();
            if (mine.Cells != null)
                foreach (var cell in mine.Cells)
                    if (cell != null) Had.Add(cell);
            if (Now.SetEquals(Had)) { _miss = 0; return; }

            if (_missId != own) { _missId = own; _miss = 0; }
            if (++_miss < 2) return;
            _miss = 0;
            cd.ClearActionSelection();
            cd.MakeWalkSelection();
            Plugin.Trace("[подсветка] зона ходьбы пересчитана: было клеток " + Had.Count + ", можно дойти до " + Now.Count + ", дальность " + reach);
        }

        private static void Rub(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            var cells = cd.Cells;
            if (cells == null) return;

            Want.Clear();
            foreach (var one in box.Values)
            {
                if (one == null || one.Cells == null) continue;
                foreach (var cell in one.Cells)
                {
                    if (cell == null) continue;
                    Color had;
                    Want[cell] = (Want.TryGetValue(cell, out had) ? had : Blank) + one.SelectionColor;
                }
            }

            Fix.Clear();
            int wide = cells.GetLength(0);
            int high = cells.GetLength(1);
            for (int x = 0; x < wide; x++)
                for (int y = 0; y < high; y++)
                {
                    var cell = cells[x, y];
                    if (cell == null) continue;
                    Color want;
                    if (!Want.TryGetValue(cell, out want)) want = Blank;
                    if (Bare(want) ? Exact(cell.CellColor, want) : Same(cell.CellColor, want)) continue;
                    cell.CellColor = want;
                    Fix.Add(cell);
                }
            if (Fix.Count == 0) return;

            Paint(cd, Fix.ToArray());
            _meshAt = 0f;
            Plugin.Trace("[подсветка] возвращено к своему цвету клеток: " + Fix.Count);
        }

        private static void Canvas(ICombatData cd)
        {
            var cells = cd.Cells;
            if (cells == null) return;
            if (_grid == null) { _grid = UnityEngine.Object.FindObjectOfType<HexGrid>(); _filter = null; }
            if (_grid == null) return;
            if (_filter == null) _filter = _grid.GetComponent<MeshFilter>();
            if (_filter == null) return;
            var mesh = _filter.mesh;
            if (mesh == null) return;
            mesh.GetColors(Paints);
            if (Paints.Count == 0) return;

            int redone = 0;
            int wide = cells.GetLength(0);
            int high = cells.GetLength(1);
            for (int x = 0; x < wide; x++)
                for (int y = 0; y < high; y++)
                {
                    var cell = cells[x, y];
                    if (cell == null) continue;
                    int at = cell.VertexOffset;
                    if (at < 0 || at + Corners > Paints.Count) continue;
                    var tone = cell.CellColor;
                    bool bare = Bare(tone);
                    bool off = false;
                    for (int k = 0; k < Corners && !off; k++) off = bare ? !Exact(Paints[at + k], tone) : !Same(Paints[at + k], tone);
                    if (!off) continue;
                    for (int k = 0; k < Corners; k++) Paints[at + k] = tone;
                    redone++;
                }
            if (redone == 0) return;
            mesh.SetColors(Paints);
            Plugin.Trace("[подсветка] сетка расходилась с клетками, перекрашено клеток: " + redone);
        }

        private static void Note(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            if (_noted) return;
            if (Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) { _noted = true; return; }
            if (Time.unscaledTime - _phaseAt < Linger + 0.2f) return;
            _noted = true;
            var line = new StringBuilder();
            line.Append("[подсветка] через секунду после начала фазы ").Append(cd.RoundType == RoundType.WALK_ROUND ? "ходьбы" : "боя")
                .Append(": выделений ").Append(box.Count);
            foreach (var pair in box)
            {
                if (pair.Value == null) continue;
                line.Append(' ').Append(Tone(pair.Value.SelectionColor)).Append('×')
                    .Append(pair.Value.Cells == null ? 0 : pair.Value.Cells.Length);
            }
            int tinted = 0;
            var cells = cd.Cells;
            if (cells != null)
                foreach (var cell in cells)
                    if (cell != null && !Same(cell.CellColor, Blank)) tinted++;
            line.Append(", закрашено клеток ").Append(tinted);
            int grids = UnityEngine.Object.FindObjectsOfType<HexGrid>().Length;
            if (grids != 1) line.Append(", сеток на сцене ").Append(grids);
            if (_gridField == null) _gridField = AccessTools.Field(typeof(HexGrid), "_combatData");
            if (_grid != null && !ReferenceEquals(_gridField != null ? _gridField.GetValue(_grid) : null, cd))
                line.Append(", сетка слушает другие данные боя");
            Plugin.Trace(line.ToString());
        }

        private static void Census(ICombatData cd, Dictionary<int, GridSelection> box)
        {
            if (Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            var line = new StringBuilder();
            line.Append("[подсветка] фаза ").Append(cd.RoundType == RoundType.WALK_ROUND ? "ходьбы" : "боя")
                .Append(", раунд ").Append(cd.RoundNum)
                .Append(", своё выделение ").Append(Own(cd))
                .Append(", цвет ходьбы ").Append(Tone(cd.WalkSelectionColor))
                .Append(", живых выделений ").Append(box.Count);
            foreach (var pair in box)
            {
                line.Append(" | ").Append(pair.Key).Append(':');
                if (pair.Value == null) { line.Append("пусто"); continue; }
                line.Append(Tone(pair.Value.SelectionColor)).Append('×')
                    .Append(pair.Value.Cells == null ? 0 : pair.Value.Cells.Length);
            }
            Plugin.Log?.LogInfo(line.ToString());
        }

        private static string Tone(Color one)
        {
            return Mathf.RoundToInt(one.r * 255f) + "," + Mathf.RoundToInt(one.g * 255f) + ","
                 + Mathf.RoundToInt(one.b * 255f) + "," + Mathf.RoundToInt(one.a * 255f);
        }

        private static int Own(ICombatData cd)
        {
            Bind(cd);
            var got = _ownField != null ? _ownField.GetValue(cd) : null;
            return got is int ? (int)got : 0;
        }

        private static void Paint(ICombatData cd, Cell[] cells)
        {
            Bind(cd);
            var call = (_paintField != null ? _paintField.GetValue(cd) : null) as InvalidateCellsEventHandler;
            if (call == null) { Plugin.Trace("[подсветка] перерисовку не вызвать"); return; }
            call(cells);
        }

        private static bool Bare(Color one)
        {
            return one.r == 0f && one.g == 0f && one.b == 0f;
        }

        private static bool Exact(Color one, Color two)
        {
            return one.r == two.r && one.g == two.g && one.b == two.b && one.a == two.a;
        }

        private static bool Same(Color one, Color two)
        {
            return Mathf.Abs(one.r - two.r) < Grain
                && Mathf.Abs(one.g - two.g) < Grain
                && Mathf.Abs(one.b - two.b) < Grain
                && Mathf.Abs(one.a - two.a) < Grain;
        }
    }

    [HarmonyPatch(typeof(AbstractCharacter), "AttachSelection")]
    internal static class WalkGlowMarkPatch
    {
        private static void Postfix(AbstractCharacter __instance)
        {
            try { WalkGlow.Mark(__instance); }
            catch (Exception e) { Plugin.Trace("[подсветка] кружок выделения: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(CombatData), "ClearSelection")]
    internal static class WalkGlowClearPatch
    {
        private static void Postfix()
        {
            try { WalkGlow.Dirty(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CombatData), "CharacterTeleported")]
    internal static class WalkGlowTeleportPatch
    {
        private static void Postfix()
        {
            try { WalkGlow.Soon(); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CombatData), "StartNewRound")]
    internal static class WalkGlowRoundPatch
    {
        private static void Prefix()
        {
            try { WalkGlow.Phase(); }
            catch (Exception e) { Plugin.Trace("[подсветка] начало фазы: " + e.Message); }
        }

        private static void Postfix()
        {
            try { WalkGlow.Soon(); }
            catch (Exception e) { Plugin.Trace("[подсветка] новый раунд: " + e.Message); }
        }
    }
}
