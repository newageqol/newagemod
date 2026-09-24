using System;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class WalkHex
    {
        private static GameObject _go;
        private static LineRenderer _line;
        private static LineRenderer _under;
        private static Transform _grid;
        private static CameraControl _rig;
        private static HexGridControl _pad;
        private static float _padAt;
        private static readonly Vector3[] Dirs = new Vector3[6];
        private static readonly float[] Turns = new float[6];
        private static readonly int[] Order = new int[6];
        private static Matrix4x4 _lastFrame;
        private static Vector3 _center;
        private static float _pitch;
        private static int _lastX = int.MinValue;
        private static int _lastY = int.MinValue;
        private static bool _shaped;
        private static float _thick;
        private static bool _thickKnown;
        private static Vector3 _eyeAt;
        private static float _lens;
        private static int _tall;

        internal static bool Enabled => Plugin.CfgWalkHex == null || Plugin.CfgWalkHex.Value;

        private static string _why = "";

        private static void Off(string why, OffsetCoord hex = null)
        {
            if (why != _why)
            {
                _why = why;
                if (why.Length > 0 && Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                    Plugin.Trace("[ходьба] обвода нет: " + why
                        + (hex != null ? ", " + hex.clientX + ";" + hex.clientY : ""));
            }
            Hide();
        }

        internal static void Tick()
        {
            try
            {
                var hex = WalkKeys.Pick;
                bool keyed = hex != null;
                if (!Enabled && !keyed) { Off(""); return; }
                if (!SideButtons.InCombat()) { Off(""); return; }
                if (Spectate.Peeking) { Off("смотрю чужой бой"); return; }
                var cd = FighterHint.Cd();
                if (cd == null) { Off("нет данных боя"); return; }
                if (cd.RoundType != RoundType.WALK_ROUND) { Off(""); return; }
                if (SkillList.Armed) { Off("наведено умение"); return; }
                if (!keyed)
                {
                    if (Unity3DHelper.IsOverInterface()) { Off("мышь над окном"); return; }
                    if (!SkillList.HexUnder(out hex)) { Off("клетку под мышью не посчитать"); return; }
                }
                if (!Walkable(cd, hex)) { Off("клетка не в зоне хода", hex); return; }

                var grid = Grid();
                if (grid == null) { Off("сетка боя не найдена"); return; }
                _why = "";
                Draw(grid, hex, keyed);
            }
            catch (Exception e) { Plugin.Trace("[ходьба] клетка под мышью: " + e.Message); Hide(); }
        }

        internal static bool Walkable(ICombatData cd, OffsetCoord hex)
        {
            try { return cd.IsCellInActionSelection(hex); }
            catch (IndexOutOfRangeException) { return false; }
            catch (ArgumentOutOfRangeException) { return false; }
            catch (Exception e) { Plugin.Trace("[ходьба] проверка клетки: " + e.Message); return false; }
        }

        private static Transform Grid()
        {
            if (_pad != null && _pad.isActiveAndEnabled) return _pad.transform;
            if (Time.unscaledTime >= _padAt)
            {
                _padAt = Time.unscaledTime + 0.25f;
                try { _pad = UnityEngine.Object.FindObjectOfType<HexGridControl>(); }
                catch (Exception e) { Plugin.Trace("[ходьба] поле боя: " + e.Message); }
                if (_pad != null) return _pad.transform;
            }
            if (_grid != null && _grid.gameObject.activeInHierarchy) return _grid;
            try
            {
                if (_rig == null || !_rig.isActiveAndEnabled) _rig = UnityEngine.Object.FindObjectOfType<CameraControl>();
                _grid = _rig != null ? AccessTools.Property(typeof(BaseUserInput), "gridTransform")?.GetValue(_rig) as Transform : null;
            }
            catch (Exception e) { Plugin.Trace("[ходьба] сетка боя: " + e.Message); }
            return _grid;
        }

        private static void Draw(Transform grid, OffsetCoord hex, bool keyed)
        {
            Build(grid);
            if (_line == null) return;

            var frame = grid.localToWorldMatrix;
            bool fresh = !_shaped || _lastX != hex.clientX || _lastY != hex.clientY || frame != _lastFrame;
            if (fresh && !Shape(grid, hex, frame)) return;

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            float alpha = 0.55f + 0.45f * wave;
            var tint = keyed ? new Color(1f, 0.82f, 0.3f, alpha) : new Color(0.45f, 0.92f, 1f, alpha);
            _line.startColor = tint;
            _line.endColor = tint;
            float width = Thick(_center, _pitch, !fresh) * (0.85f + 0.3f * wave);
            _line.widthMultiplier = width;
            if (_under != null)
            {
                var dark = new Color(0.02f, 0.05f, 0.09f, alpha * 0.85f);
                _under.startColor = dark;
                _under.endColor = dark;
                _under.widthMultiplier = width * 2.1f;
            }
            int order = FlashLook.Fight ? short.MinValue + 4 : 0;
            if (_line.sortingOrder != order) _line.sortingOrder = order;
            if (_under != null && _under.sortingOrder != order - 1) _under.sortingOrder = order - 1;
            if (!_go.activeSelf) _go.SetActive(true);
        }

        private static bool Shape(Transform grid, OffsetCoord hex, Matrix4x4 frame)
        {
            var center = HexUtils.offsetToPixelInWordSpace(grid, hex);
            var right = grid.right;
            var ahead = grid.forward;
            float pitch = 0f;
            for (int k = 0; k < 6; k++)
            {
                Dirs[k] = HexUtils.offsetToPixelInWordSpace(grid, HexUtils.getNeighbor(hex, k)) - center;
                Turns[k] = Mathf.Atan2(Vector3.Dot(Dirs[k], ahead), Vector3.Dot(Dirs[k], right));
                Order[k] = k;
                pitch += Dirs[k].magnitude;
            }
            pitch /= 6f;
            if (pitch < 0.001f) { _shaped = false; Hide(); return false; }

            for (int a = 1; a < 6; a++)
            {
                int keep = Order[a];
                int b = a - 1;
                while (b >= 0 && Turns[Order[b]] > Turns[keep]) { Order[b + 1] = Order[b]; b--; }
                Order[b + 1] = keep;
            }

            var up = grid.up;
            var lift = up * pitch * 0.05f;
            var sink = up * pitch * 0.002f;
            for (int k = 0; k < 6; k++)
            {
                var one = Dirs[Order[k]];
                var next = Dirs[Order[(k + 1) % 6]];
                var spot = center + (one + next) / 3f + lift;
                _line.SetPosition(k, spot);
                if (_under != null) _under.SetPosition(k, spot - sink);
            }

            _center = center;
            _pitch = pitch;
            _lastX = hex.clientX;
            _lastY = hex.clientY;
            _lastFrame = frame;
            _shaped = true;
            return true;
        }

        private static float Thick(Vector3 center, float pitch, bool same)
        {
            float want = Mathf.Max(0.008f, pitch * 0.018f);
            try
            {
                var eye = WalkKeys.Eye();
                int tall = Mathf.Max(1, Screen.height);
                if (eye == null) { _thickKnown = false; return want; }
                var at = eye.transform.position;
                float lens = eye.orthographic ? eye.orthographicSize : eye.fieldOfView;
                if (same && _thickKnown && tall == _tall && lens == _lens && at == _eyeAt) return _thick;
                float step = eye.orthographic
                    ? eye.orthographicSize * 2f / tall
                    : 2f * Vector3.Distance(at, center)
                      * Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad) / tall;
                _thick = step <= 0f ? want : Mathf.Clamp(Mathf.Max(want, step * 3.5f), 0.008f, pitch * 0.25f);
                _thickKnown = true;
                _tall = tall;
                _lens = lens;
                _eyeAt = at;
                return _thick;
            }
            catch (Exception e) { Plugin.Trace("[ходьба] толщина обвода: " + e.Message); _thickKnown = false; return want; }
        }

        private static void Build(Transform grid)
        {
            if (_go != null) return;
            _shaped = false;
            _thickKnown = false;
            _go = new GameObject("QoLWalkHex");
            _go.layer = grid.gameObject.layer;
            _line = _go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.loop = true;
            _line.positionCount = 6;
            _line.numCornerVertices = 2;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null) _line.material = new Material(shader);
            var tint = new Color(0.45f, 0.92f, 1f, 0.95f);
            _line.startColor = tint;
            _line.endColor = tint;

            var underGo = new GameObject("under");
            underGo.transform.SetParent(_go.transform, false);
            underGo.layer = _go.layer;
            _under = underGo.AddComponent<LineRenderer>();
            _under.useWorldSpace = true;
            _under.loop = true;
            _under.positionCount = 6;
            _under.numCornerVertices = 2;
            _under.alignment = LineAlignment.View;
            _under.textureMode = LineTextureMode.Stretch;
            _under.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _under.receiveShadows = false;
            if (shader != null) _under.material = new Material(shader);
            _under.sortingOrder = -1;

            _go.SetActive(false);
            Plugin.Trace("[ходьба] обвод клетки собран, шейдер " + (shader != null ? shader.name : "нет"));
        }

        private static void Hide()
        {
            if (_go != null && _go.activeSelf) _go.SetActive(false);
        }
    }
}
