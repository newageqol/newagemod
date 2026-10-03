using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

internal static class HexLook
{
    private const int Low = short.MinValue;
    private const int Near = 3;
    private const float Lift = 0.004f;

    private static readonly float Size = MathConsts.HEX_SIZE;
    private static readonly float Step = MathConsts.Sqrt3 * MathConsts.HEX_SIZE;

    private static readonly Color ZoneTint = new(0.45f, 0.9f, 1f, 1f);
    private static readonly Color GridTint = new(0.02f, 0.07f, 0.03f, 1f);
    private static readonly Color LineTint = new(0.82f, 0.86f, 0.74f, 1f);
    private static readonly Color HoverTint = new(1f, 1f, 1f, 1f);
    private static readonly Color Strike = new(0.5f, 0f, 0f);

    private static readonly int[,] Dirs = { { 1, 0 }, { 1, -1 }, { 0, -1 }, { -1, 0 }, { -1, 1 }, { 0, 1 } };

    private static readonly FieldInfo BoxField = AccessTools.Field(typeof(CombatData), "_selections");
    private static readonly FieldInfo OwnField = AccessTools.Field(typeof(CombatData), "_actionSelectionId");
    private static readonly FieldInfo MarkField = AccessTools.Field(typeof(AbstractCharacter), "_selectionObject");

    private sealed class Paint
    {
        internal readonly GameObject Go;
        internal readonly Mesh Mesh;
        internal readonly Material Skin;
        internal readonly List<Vector3> Points = new();
        internal readonly List<Color> Colors = new();
        internal readonly List<int> Tris = new();

        internal Paint(Transform root, string name, Shader shader, int order)
        {
            Go = new GameObject(name);
            Go.layer = root.gameObject.layer;
            Go.transform.SetParent(root, false);
            Mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            Mesh.MarkDynamic();
            Go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var view = Go.AddComponent<MeshRenderer>();
            Skin = new Material(shader) { mainTexture = Texture2D.whiteTexture };
            view.sharedMaterial = Skin;
            view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.receiveShadows = false;
            var layers = SortingLayer.layers;
            if (layers.Length > 0) view.sortingLayerID = layers[0].id;
            view.sortingOrder = order;
        }

        internal void Begin()
        {
            Points.Clear();
            Colors.Clear();
            Tris.Clear();
        }

        internal void End()
        {
            Mesh.Clear();
            Mesh.SetVertices(Points);
            Mesh.SetColors(Colors);
            Mesh.SetTriangles(Tris, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 10f, 1000f));
        }

        internal void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
        {
            int n = Points.Count;
            Points.Add(a); Points.Add(b); Points.Add(c); Points.Add(d);
            Colors.Add(ca); Colors.Add(cb); Colors.Add(cc); Colors.Add(cd);
            Tris.Add(n); Tris.Add(n + 1); Tris.Add(n + 2);
            Tris.Add(n); Tris.Add(n + 2); Tris.Add(n + 3);
        }

        internal void Fan(Vector3 middle, Color tint)
        {
            int n = Points.Count;
            Points.Add(middle);
            Colors.Add(tint);
            for (int k = 0; k < 6; k++)
            {
                Points.Add(middle + Corner(k));
                Colors.Add(tint);
            }
            for (int k = 0; k < 6; k++)
            {
                Tris.Add(n);
                Tris.Add(n + 1 + k);
                Tris.Add(n + 1 + (k + 1) % 6);
            }
        }

        internal void Drop()
        {
            if (Go != null) UnityEngine.Object.Destroy(Go);
            if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
            if (Skin != null) UnityEngine.Object.Destroy(Skin);
        }
    }

    private sealed class Mark
    {
        internal GameObject Go;
        internal SpriteRenderer View;
        internal GameObject CoreGo;
        internal SpriteRenderer Core;
        internal GameObject FrontGo;
        internal SpriteRenderer Front;

        internal void Drop()
        {
            if (Go != null) UnityEngine.Object.Destroy(Go);
            if (CoreGo != null) UnityEngine.Object.Destroy(CoreGo);
            if (FrontGo != null) UnityEngine.Object.Destroy(FrontGo);
        }
    }

    private static Transform _grid;
    private static MeshRenderer _gameGrid;
    private static bool _gridHidden;
    private static GameObject _root;
    private static Material _spriteSkin;
    private static Paint _faint;
    private static Paint _zone;
    private static Paint _marks;
    private static int _zoneSign;
    private static float _zoneAt;
    private static bool _failed;
    private static Transform _failedGrid;

    private static Cell[,] _indexed;
    private static float _countAt;
    private static readonly Dictionary<Cell, int> Where = new();
    private static readonly HashSet<int> Set = new();
    private static readonly HashSet<int> Other = new();
    private static readonly HashSet<int> Walk = new();
    private static readonly HashSet<long> Edges = new();
    private static readonly Dictionary<AbstractCharacter, Mark> Marks = new();
    private static readonly List<AbstractCharacter> Lost = new();
    private static readonly HashSet<AbstractCharacter> Reach = new();
    private static readonly HashSet<FighterDoll> Threats = new();
    private static readonly List<FighterDoll> Calm = new();
    private static FighterDoll _chosenDoll;
    private static GameObject _gameMark;

    private static Sprite _base;
    private static Sprite _core;
    private static Sprite _front;

    internal static void Tick()
    {
        try { Run(); }
        catch (Exception ex)
        {
            _failed = true;
            _failedGrid = _grid;
            Plugin.Log.LogError("[hexes] " + ex);
            Drop();
        }
    }

    private static void Run()
    {
        var cd = Fighters.Combat() as CombatData;
        var location = CombatView.Get();
        var grid = location != null && location.HexGrid != null ? location.HexGrid.transform : null;
        bool on = Plugin.FlashFight && Field.Active && cd != null && grid != null && cd.Cells != null;
        if (_failed && grid != _failedGrid) _failed = false;
        if (!on || _failed)
        {
            Drop();
            return;
        }
        if (_grid != grid)
        {
            Drop();
            Build(grid);
        }
        HideGame(grid);
        Index(cd);
        var box = BoxField?.GetValue(cd) as Dictionary<int, GridSelection>;
        int own = OwnField != null ? (int)OwnField.GetValue(cd) : 0;
        if (box == null) return;
        var eye = location.CombatCamera != null ? location.CombatCamera : Camera.main;
        Zone(cd, box, own);
        Cursor(cd, box, own, eye);
        Threat(cd, box, own);
        Rings(cd);
    }

    private static void Build(Transform grid)
    {
        _grid = grid;
        _root = new GameObject("NewAge2D.HexLook");
        _root.layer = grid.gameObject.layer;
        _root.transform.SetParent(grid, false);
        _root.transform.localPosition = new Vector3(0f, Lift, 0f);
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        _spriteSkin = new Material(shader);
        _faint = new Paint(_root.transform, "NewAge2D.HexFaint", shader, Low + 4);
        _zone = new Paint(_root.transform, "NewAge2D.HexZone", shader, Low + 5);
        _marks = new Paint(_root.transform, "NewAge2D.HexMarks", shader, Low + 6);
        _zoneSign = 0;
        if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo("[hexes] own cell look on: game grid hidden, zone contour, cursor grid, rings, auras");
    }

    private static void HideGame(Transform grid)
    {
        if (_gameGrid == null) _gameGrid = grid.GetComponent<MeshRenderer>();
        if (_gameGrid != null && _gameGrid.enabled)
        {
            _gameGrid.enabled = false;
            _gridHidden = true;
        }
    }

    internal static void Drop()
    {
        if (_gridHidden && _gameGrid != null) _gameGrid.enabled = true;
        _gridHidden = false;
        _gameGrid = null;
        if (_gameMark != null) _gameMark.SetActive(true);
        _gameMark = null;
        foreach (var doll in Threats)
            if (doll != null) doll.Threat(false);
        Threats.Clear();
        if (_chosenDoll != null) _chosenDoll.Chosen(false, Glow.Foe);
        _chosenDoll = null;
        _faint?.Drop();
        _zone?.Drop();
        _marks?.Drop();
        _faint = null;
        _zone = null;
        _marks = null;
        foreach (var mark in Marks.Values)
            mark.Drop();
        Marks.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_spriteSkin != null) UnityEngine.Object.Destroy(_spriteSkin);
        _root = null;
        _spriteSkin = null;
        _grid = null;
        _indexed = null;
        Where.Clear();
    }

    private static int Key(int x, int y) => (x << 16) | (y & 0xFFFF);

    private static int KeyX(int key) => key >> 16;

    private static int KeyY(int key) => (short)(key & 0xFFFF);

    private static void Index(CombatData cd)
    {
        var cells = cd.Cells;
        if (ReferenceEquals(cells, _indexed) && Time.unscaledTime < _countAt) return;
        _countAt = Time.unscaledTime + 1f;
        int open = 0;
        foreach (var cell in cells)
            if (cell != null) open++;
        if (ReferenceEquals(cells, _indexed) && open == Where.Count) return;
        _indexed = cells;
        Where.Clear();
        for (int x = 0; x < cells.GetLength(0); x++)
            for (int y = 0; y < cells.GetLength(1); y++)
                if (cells[x, y] != null) Where[cells[x, y]] = Key(x, y);
        Faint();
    }

    private static void Faint()
    {
        const float bright = 0.4f;
        _faint.Begin();
        Edges.Clear();
        Set.Clear();
        var shade = With(GridTint, Mathf.Min(0.8f, bright * 1.3f));
        var thin = With(LineTint, Mathf.Min(0.7f, bright * 1.1f));
        var wallShade = With(GridTint, Mathf.Min(0.9f, bright * 1.6f + 0.15f));
        var wall = With(LineTint, Mathf.Min(0.85f, bright * 1.5f + 0.1f));
        var blocked = new Color(0f, 0f, 0f, Mathf.Min(0.45f, bright * 0.6f + 0.1f));
        foreach (int key in Where.Values)
        {
            var middle = Middle(key);
            for (int k = 0; k < 6; k++)
            {
                int next = Neighbor(key, k);
                bool open = Open(next);
                if (!open) Set.Add(next);
                long edge = key < next ? ((long)key << 32) | (uint)next : ((long)next << 32) | (uint)key;
                if (!Edges.Add(edge)) continue;
                Side(key, next, out var a, out var b);
                var inward = (middle - (a + b) * 0.5f).normalized;
                var dark = inward * (open ? 0.028f : 0.05f);
                var core = inward * (open ? 0.009f : 0.018f);
                var under = open ? shade : wallShade;
                var over = open ? thin : wall;
                _faint.Quad(a - dark, b - dark, b + dark, a + dark, under, under, under, under);
                _faint.Quad(a - core, b - core, b + core, a + core, over, over, over, over);
            }
        }
        foreach (int key in Set)
            _faint.Fan(Middle(key), blocked);
        _faint.End();
        if (Plugin.CfgVerbose.Value) Plugin.Log.LogInfo($"[hexes] field grid: {Where.Count} cells, {Edges.Count} edges, {Set.Count} blocked cells along the edge, brightness {bright:0.##}");
    }

    private static Vector3 Center(int x, int y) => new(Step * (x + 0.5f * (y & 1)), 0f, -Size * 1.5f * y);

    private static Vector3 Corner(int k)
    {
        float angle = (30f + 60f * k) * Mathf.Deg2Rad;
        return new Vector3(Size * Mathf.Cos(angle), 0f, Size * Mathf.Sin(angle));
    }

    private static int Neighbor(int key, int k)
    {
        int x = KeyX(key), y = KeyY(key);
        int q = x - (y - (y & 1)) / 2 + Dirs[k, 0];
        int r = y + Dirs[k, 1];
        return Key(q + (r - (r & 1)) / 2, r);
    }

    private static bool Open(int key)
    {
        int x = KeyX(key), y = KeyY(key);
        var cells = _indexed;
        return cells != null && x >= 0 && y >= 0 && x < cells.GetLength(0) && y < cells.GetLength(1) && cells[x, y] != null;
    }

    private static int Apart(int one, int two)
    {
        int y1 = KeyY(one), y2 = KeyY(two);
        int q1 = KeyX(one) - (y1 - (y1 & 1)) / 2, q2 = KeyX(two) - (y2 - (y2 & 1)) / 2;
        int dq = q2 - q1, dr = y2 - y1;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    private static void Around(int middle, int range, HashSet<int> into)
    {
        into.Clear();
        int mx = KeyX(middle), my = KeyY(middle);
        for (int y = my - range; y <= my + range; y++)
            for (int x = mx - range - 1; x <= mx + range + 1; x++)
            {
                int key = Key(x, y);
                if ((key == middle || Open(key)) && Apart(middle, key) <= range) into.Add(key);
            }
    }

    private static Vector3 Middle(int key) => Center(KeyX(key), KeyY(key));

    private static void Side(int from, int to, out Vector3 a, out Vector3 b)
    {
        var one = Middle(from);
        var two = Middle(to);
        var mid = (one + two) * 0.5f;
        var dir = (two - one).normalized;
        var across = new Vector3(-dir.z, 0f, dir.x) * (Size * 0.5f);
        a = mid + across;
        b = mid - across;
    }

    private static void Band(Paint paint, Vector3 a, Vector3 b, Vector3 toward, float depth, Color edge, Color inner)
    {
        var a2 = a + (toward - a) * depth;
        var b2 = b + (toward - b) * depth;
        paint.Quad(a, b, b2, a2, edge, edge, inner, inner);
    }

    private static Color With(Color tint, float alpha) => new(tint.r, tint.g, tint.b, alpha);

    private static void Contour(Paint paint, HashSet<int> set, HashSet<int> solid, Color tint, float fill, float line, float glow, float outside)
    {
        foreach (int key in set)
        {
            var middle = Middle(key);
            if (fill > 0f && solid.Contains(key)) paint.Fan(middle, With(tint, fill));
            for (int k = 0; k < 6; k++)
            {
                int next = Neighbor(key, k);
                if (set.Contains(next)) continue;
                Side(key, next, out var a, out var b);
                if (glow > 0f) Band(paint, a, b, middle, 0.5f, With(tint, glow), With(tint, 0f));
                if (line > 0f) Band(paint, a, b, middle, 0.07f, With(tint, line), With(tint, line * 0.6f));
                if (outside > 0f) Band(paint, a, b, Middle(next), 0.28f, With(tint, outside), With(tint, 0f));
            }
        }
    }

    private static void Gather(GridSelection selection, HashSet<int> into)
    {
        into.Clear();
        if (selection?.Cells == null) return;
        foreach (var cell in selection.Cells)
            if (cell != null && Where.TryGetValue(cell, out int key)) into.Add(key);
    }

    private static void Zone(CombatData cd, Dictionary<int, GridSelection> box, int own)
    {
        bool walk = cd.RoundType == RoundType.WALK_ROUND && own != 0 && box.ContainsKey(own);
        int sign = walk ? own * 397 ^ (box[own].Cells?.Length ?? 0) : 0;
        if (sign != _zoneSign)
        {
            _zoneSign = sign;
            _zoneAt = Time.unscaledTime;
            _zone.Begin();
            Walk.Clear();
            if (walk)
            {
                Gather(box[own], Other);
                Walk.UnionWith(Other);
                Set.Clear();
                Set.UnionWith(Other);
                Plug(cd);
                Contour(_zone, Set, Other, ZoneTint, 0.1f, 0.85f, 0.32f, 0.2f);
            }
            _zone.End();
        }
        float shown = Mathf.Clamp01((Time.unscaledTime - _zoneAt) / 0.3f);
        _zone.Skin.color = new Color(1f, 1f, 1f, shown * shown * (3f - 2f * shown));
    }

    private static void Plug(CombatData cd)
    {
        foreach (var character in cd.Characters.Values)
        {
            var spot = character?.HexGridPosition;
            if (spot == null) continue;
            int key = Key(spot.clientX, spot.clientY);
            if (Set.Contains(key)) continue;
            int around = 0;
            for (int k = 0; k < 6; k++)
                if (Set.Contains(Neighbor(key, k))) around++;
            if (around >= 3 || (cd.MyCharacter != null && ReferenceEquals(character, cd.MyCharacter) && around > 0)) Set.Add(key);
        }
    }

    private static bool Hover(GridSelection selection, out AbstractCharacter body)
    {
        body = null;
        var color = selection.SelectionColor;
        if (Mathf.Abs(color.r - 0.45f) > 0.01f || Mathf.Abs(color.g - 0.95f) > 0.01f || Mathf.Abs(color.b - 0.45f) > 0.01f) return false;
        if (selection.Cells == null || selection.Cells.Length != 1) return false;
        body = selection.Cells[0]?.occupiedBy;
        return body != null;
    }

    private static bool Aura(Color color) => color == Color.yellow || color == Color.red;

    private static Color Bright(Color color)
    {
        float top = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        return top > 1f ? new Color(color.r / top, color.g / top, color.b / top, 1f) : new Color(color.r, color.g, color.b, 1f);
    }

    private static void Cursor(CombatData cd, Dictionary<int, GridSelection> box, int own, Camera eye)
    {
        _marks.Begin();
        foreach (var pair in box)
        {
            var selection = pair.Value;
            if (selection == null || pair.Key == own || Aura(selection.SelectionColor)) continue;
            var tint = Bright(selection.SelectionColor);
            if (Hover(selection, out var body)) tint = Glow.Side(body);
            Gather(selection, Other);
            foreach (int key in Other)
            {
                var middle = Middle(key);
                _marks.Fan(middle, With(tint, 0.2f));
                for (int k = 0; k < 6; k++)
                {
                    Side(key, Neighbor(key, k), out var a, out var b);
                    Band(_marks, a, b, middle, 0.45f, With(tint, 0.35f), With(tint, 0f));
                    Band(_marks, a, b, middle, 0.08f, With(tint, 0.95f), With(tint, 0.6f));
                }
            }
        }
        foreach (var character in cd.Characters.Values)
        {
            if (!(character is BotCharacter bot) || bot.FitmentAura == null || bot.Dead || bot.HexGridPosition == null) continue;
            if (!box.TryGetValue(bot.FitmentAura.SelectionId, out var selection) || selection == null) continue;
            Around(Key(bot.HexGridPosition.clientX, bot.HexGridPosition.clientY), bot.FitmentAura.Range, Other);
            Contour(_marks, Other, Other, Bright(selection.SelectionColor), 0.05f, 0.5f, 0.12f, 0f);
        }
        if (eye != null && !Unity3DHelper.IsOverInterface() && Mouse(eye, out var spot, out int hover)) Lattice(spot, hover);
        _marks.End();
    }

    private static bool Mouse(Camera eye, out Vector3 spot, out int hover)
    {
        spot = default;
        hover = 0;
        var floor = new Plane(_grid.up, _grid.position);
        var ray = eye.ScreenPointToRay(Input.mousePosition);
        if (!floor.Raycast(ray, out float along)) return false;
        spot = _grid.InverseTransformPoint(ray.GetPoint(along));
        var coord = HexUtils.pixelToOffset(spot);
        if (coord == null) return false;
        hover = Key(coord.clientX, coord.clientY);
        return true;
    }

    private static void Lattice(Vector3 spot, int hover)
    {
        Edges.Clear();
        float reach = Step * (Near + 0.3f);
        int hx = KeyX(hover), hy = KeyY(hover);
        for (int y = hy - Near - 1; y <= hy + Near + 1; y++)
            for (int x = hx - Near - 1; x <= hx + Near + 1; x++)
            {
                int key = Key(x, y);
                if (!Open(key)) continue;
                var middle = Center(x, y);
                if ((middle - spot).magnitude > reach + Step) continue;
                for (int k = 0; k < 6; k++)
                {
                    int next = Neighbor(key, k);
                    long edge = key < next ? ((long)key << 32) | (uint)next : ((long)next << 32) | (uint)key;
                    if (!Edges.Add(edge)) continue;
                    Side(key, next, out var a, out var b);
                    float fa = Fade(a, spot, reach);
                    float fb = Fade(b, spot, reach);
                    if (fa <= 0f && fb <= 0f) continue;
                    var across = (middle - (a + b) * 0.5f).normalized * 0.018f;
                    _marks.Quad(a - across, b - across, b + across, a + across,
                        With(GridTint, fa), With(GridTint, fb), With(GridTint, fb), With(GridTint, fa));
                }
            }
        if (!Open(hover) || Walk.Contains(hover)) return;
        var center = Middle(hover);
        for (int k = 0; k < 6; k++)
        {
            Side(hover, Neighbor(hover, k), out var a, out var b);
            Band(_marks, a, b, center, 0.06f, With(HoverTint, 0.75f), With(HoverTint, 0.45f));
            Band(_marks, a, b, center, 0.35f, With(HoverTint, 0.16f), With(HoverTint, 0f));
        }
    }

    private static float Fade(Vector3 point, Vector3 spot, float reach)
    {
        float t = Mathf.Clamp01(1f - (point - spot).magnitude / reach);
        return t * t * 0.55f;
    }

    private static void Threat(CombatData cd, Dictionary<int, GridSelection> box, int own)
    {
        Reach.Clear();
        if (cd.RoundType == RoundType.COMBAT_ROUND && own != 0 && box.TryGetValue(own, out var selection) && selection?.Cells != null && selection.SelectionColor == Strike)
            foreach (var cell in selection.Cells)
                if (cell?.occupiedBy != null && !cell.occupiedBy.Dead) Reach.Add(cell.occupiedBy);
        Calm.Clear();
        foreach (var doll in Threats)
            if (doll == null || !Reach.Contains(doll.Owner)) Calm.Add(doll);
        foreach (var doll in Calm)
        {
            if (doll != null) doll.Threat(false);
            Threats.Remove(doll);
        }
        foreach (var doll in Fighters.Dolls)
        {
            if (doll == null || doll.Owner == null || !Reach.Contains(doll.Owner)) continue;
            if (Threats.Add(doll)) doll.Threat(true);
        }
    }

    private static void Rings(CombatData cd)
    {
        var chosen = cd.SelectedCharacter;
        var me = cd.MyCharacter;
        var shown = chosen != null && MarkField != null ? MarkField.GetValue(chosen) as GameObject : null;
        if (shown != _gameMark)
        {
            if (_gameMark != null) _gameMark.SetActive(true);
            _gameMark = shown;
        }
        if (_gameMark != null && _gameMark.activeSelf) _gameMark.SetActive(false);
        var picked = chosen != null && !chosen.Dead ? Fighters.DollOf(chosen) : null;
        var paint = chosen != null ? Tone(chosen, me) : Glow.Foe;
        if (!ReferenceEquals(picked, _chosenDoll))
        {
            if (_chosenDoll != null) _chosenDoll.Chosen(false, paint);
            _chosenDoll = picked;
        }
        if (_chosenDoll != null) _chosenDoll.Chosen(true, paint);

        Lost.Clear();
        Lost.AddRange(Marks.Keys);
        if (chosen != null && chosen.Initialized && !chosen.Dead)
        {
            Lost.Remove(chosen);
            if (!Marks.TryGetValue(chosen, out var mark) || mark.Go == null)
            {
                mark?.Drop();
                mark = new Mark();
                mark.View = Piece("NewAge2D.ChosenRing", BaseSprite, Low + 8, out mark.Go);
                mark.Core = Piece("NewAge2D.ChosenCore", CoreSprite, Low + 9, out mark.CoreGo);
                mark.Front = Piece("NewAge2D.ChosenFront", FrontSprite, Low + 9, out mark.FrontGo);
                Marks[chosen] = mark;
            }
            var world = picked != null && picked.Feet != Vector3.zero ? picked.Feet : chosen.position;
            var local = _grid.InverseTransformPoint(world);
            local.y = 0f;
            float wide = Step * 1.3f;
            var size = new Vector3(wide, wide, 1f);
            var flat = Quaternion.Euler(90f, 0f, 0f);
            mark.Go.transform.localPosition = local;
            mark.Go.transform.localRotation = flat * Quaternion.Euler(0f, 0f, Time.unscaledTime * 35f);
            mark.Go.transform.localScale = size;
            foreach (var go in new[] { mark.CoreGo, mark.FrontGo })
            {
                go.transform.localPosition = local;
                go.transform.localRotation = flat;
                go.transform.localScale = size;
            }
            var bright = Color.Lerp(paint, Color.white, 0.15f);
            mark.View.color = With(bright, 1f);
            mark.Core.color = With(Color.Lerp(paint, Color.white, 0.75f), 0.95f);
            mark.Front.color = With(bright, 1f);
            bool front = picked != null && picked.HasPicture;
            if (mark.Front.enabled != front) mark.Front.enabled = front;
            if (front && mark.Front.sortingOrder != picked.SortingOrder + 1) mark.Front.sortingOrder = picked.SortingOrder + 1;
        }
        foreach (var character in Lost)
        {
            if (Marks.TryGetValue(character, out var mark)) mark.Drop();
            Marks.Remove(character);
        }
    }

    private static Color Tone(AbstractCharacter character, AbstractCharacter me) =>
        me != null && character.Team == me.Team ? Glow.Friend : Glow.Foe;

    private static void Order(SpriteRenderer view, int order)
    {
        var layers = SortingLayer.layers;
        if (layers.Length > 0) view.sortingLayerID = layers[0].id;
        view.sortingOrder = order;
    }

    private static Sprite Flat(Color32[] pixels, int size, string name)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
    }

    private static Sprite BaseSprite => _base != null ? _base : _base = Disk(0);

    private static Sprite CoreSprite => _core != null ? _core : _core = Disk(1);

    private static Sprite FrontSprite => _front != null ? _front : _front = Disk(2);

    private static SpriteRenderer Piece(string name, Sprite sprite, int order, out GameObject go)
    {
        go = new GameObject(name);
        go.layer = _root.layer;
        go.transform.SetParent(_root.transform, false);
        var view = go.AddComponent<SpriteRenderer>();
        view.sharedMaterial = _spriteSkin;
        view.sprite = sprite;
        Order(view, order);
        return view;
    }

    private static float Bump(float d, float at, float half)
    {
        float t = Mathf.Clamp01(1f - Mathf.Abs(d - at) / half);
        return t * t * (3f - 2f * t);
    }

    private static Sprite Disk(int kind)
    {
        const int size = 256;
        const float rim = 0.78f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha;
                if (kind == 1) alpha = Bump(d, rim, 0.028f);
                else
                {
                    alpha = Mathf.Max(Bump(d, rim, 0.09f), Bump(d, rim, 0.22f) * 0.7f);
                    if (kind == 0)
                    {
                        if (d < rim) alpha = Mathf.Max(alpha, 0.32f * (d / rim) * (d / rim));
                        float angle = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) * 14f;
                        float part = angle - Mathf.Floor(angle);
                        if (part < 0.6f) alpha = Mathf.Max(alpha, Bump(d, 0.95f, 0.035f) * Mathf.Clamp01(Mathf.Min(part, 0.6f - part) * 30f));
                    }
                    else alpha *= Mathf.Clamp01(-dy * 4f);
                }
                if (d > 1f) alpha = 0f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        return Flat(pixels, size, kind == 0 ? "NewAge2D.ChosenRing" : kind == 1 ? "NewAge2D.ChosenCore" : "NewAge2D.ChosenFront");
    }
}
