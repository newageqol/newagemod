using UnityEngine;

namespace NewAge2D;

internal sealed class ArenaCam
{
    private readonly float MaxDist = 75f;
    private readonly float HomeDist = 38f;
    private const float MinDist = 8f;
    private const float LiftMax = 1.45f;
    private readonly float PitchMax = 1.35f;
    private readonly float PitchMin;
    private readonly float HomePitch = Mathf.Asin(Field.Lean);
    private readonly Vector3 HomeTarget;
    private readonly float StandsTop, CenterX, CenterZ, RadiusX, RadiusZ, CellX0, CellX1, CellZ0, CellZ1, PanX, PanZ, PanCX, PanCZ, ZMin;
    private readonly ArenaHeight _height;
    private readonly float[] _clear;
    private readonly ArenaHeight _ground;
    private readonly float CamTop, WallGap, CamLow;

    private float _yaw, _pitch, _dist;
    private float _wantYaw, _wantPitch, _wantDist;
    private float _boom = -1f, _lift = -1f;
    private Vector3 _target, _wantTarget;

    internal ArenaCam(ArenaCfg cfg)
    {
        cfg ??= new ArenaCfg();
        HomeTarget = new Vector3(cfg.home[0], cfg.home[1], cfg.home[2]);
        _target = _wantTarget = HomeTarget;
        StandsTop = cfg.standsTop;
        if (cfg.pitchMax > 0.3f) PitchMax = cfg.pitchMax;
        if (cfg.homePitch > 0f) HomePitch = cfg.homePitch * Mathf.Deg2Rad;
        if (cfg.homeDist > 0f) HomeDist = cfg.homeDist;
        _pitch = _wantPitch = HomePitch;
        _dist = _wantDist = HomeDist;
        MaxDist = Mathf.Max(HomeDist + 4f, cfg.maxDist > 0f ? cfg.maxDist : 75f);
        CenterX = cfg.center[0]; CenterZ = cfg.center[1];
        RadiusX = cfg.radii[0]; RadiusZ = cfg.radii[1];
        ZMin = cfg.zMin;
        PitchMin = cfg.pitchMin > 0f ? cfg.pitchMin : 0.62f;
        PanX = cfg.pan != null && cfg.pan.Length > 1 ? cfg.pan[0] : RadiusX * 0.6f;
        PanZ = cfg.pan != null && cfg.pan.Length > 1 ? cfg.pan[1] : RadiusZ * 0.6f;
        PanCX = cfg.pan != null && cfg.pan.Length > 1 ? HomeTarget.x : CenterX;
        PanCZ = cfg.pan != null && cfg.pan.Length > 1 ? HomeTarget.z : CenterZ;
        CellX0 = cfg.cells[0]; CellX1 = cfg.cells[1]; CellZ0 = cfg.cells[2]; CellZ1 = cfg.cells[3];
        _height = cfg.height != null && cfg.height.v != null && cfg.height.v.Length > 0 ? cfg.height : null;
        CamTop = cfg.camTop;
        WallGap = cfg.wallGap;
        CamLow = _height != null && cfg.camAbove > -900f ? _height.At(HomeTarget.x, HomeTarget.z) + cfg.camAbove : -999f;
        if (_height != null) _clear = Clearance(_height, HomeTarget, cfg.openRise, cfg.openDrop);
        if (_height != null) _ground = new ArenaHeight { x0 = _height.x0, z0 = _height.z0, step = _height.step, nx = _height.nx, nz = _height.nz, v = System.Array.ConvertAll(_height.v, h => h >= 400f ? -50f : h) };
    }

    private bool _pressed, _moving;
    private int _button;
    private Vector3 _pressAt;
    private float _fromYaw, _fromPitch;
    private Vector3 _fromTarget;
    private int _frame = -1;
    private float _floor;
    private bool _floorSet;

    private Vector3? _startTarget;

    private bool _homeLock = true;
    private float _fovExtra = -1f, _wantFovExtra;
    private readonly Vector3[] _box = new Vector3[8];

    internal void Focus(Vector3 gridLocal)
    {
        _wantTarget = Clamp(new Vector3(gridLocal.x, 0f, -gridLocal.z));
        _target = _wantTarget;
        _startTarget ??= _wantTarget;
        _homeLock = true;
    }

    private void Span(Vector3 pos, Vector3 t, out float lo, out float hi, out float wide)
    {
        var f = (t - pos).normalized;
        var r = Vector3.Cross(Vector3.up, f).normalized;
        var u = Vector3.Cross(f, r);
        lo = float.MaxValue; hi = float.MinValue; wide = 0f;
        foreach (var p in _box)
        {
            var v = p - pos;
            float z = Mathf.Max(0.5f, Vector3.Dot(v, f)), y = Vector3.Dot(v, u) / z;
            lo = Mathf.Min(lo, y);
            hi = Mathf.Max(hi, y);
            wide = Mathf.Max(wide, Mathf.Abs(Vector3.Dot(v, r) / z));
        }
    }

    private void FitHome(Camera eye, Vector3 pos)
    {
        const float pad = 0.9f, rise = 3f, top = 0.16f, side = 0.04f;
        for (int i = 0; i < 8; i++)
            _box[i] = new Vector3((i & 1) == 0 ? CellX0 - pad : CellX1 + pad, (i & 4) == 0 ? 0f : rise, (i & 2) == 0 ? CellZ0 - pad : CellZ1 + pad);
        Span(pos, _target, out float lo, out float hi, out float wide);
        var view = _target - pos;
        float down = Mathf.Max(0.2f, -view.normalized.y), len = view.magnitude;
        var home = _startTarget ?? HomeTarget;
        var fwd = new Vector3(-Mathf.Sin(_wantYaw), 0f, -Mathf.Cos(_wantYaw));
        float mid = (lo + hi) * 0.5f, along = Vector3.Dot(_target - home, fwd);
        _wantTarget = Clamp(home + fwd * (along + Mathf.Clamp(mid * len / down * 0.5f, -2f, 2f)));
        float free = Mathf.Max(0.3f, 1f - DockShare() - top);
        float need = Mathf.Max(Mathf.Max(hi, -lo) / free, wide / (Mathf.Max(0.5f, eye.aspect) * (1f - side)));
        _wantFovExtra = Mathf.Clamp(Mathf.Atan(need) * 2f * Mathf.Rad2Deg - 30f, 0f, 24f);
    }

    internal void Home()
    {
        _yaw = Mathf.Repeat(_yaw + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
        _wantYaw = 0f;
        _wantPitch = HomePitch;
        _wantDist = HomeDist;
        _wantTarget = _startTarget ?? HomeTarget;
        _pressed = false;
        _moving = false;
        _homeLock = true;
    }

    private static Vector3 ToGrid(Vector3 three) => new(three.x, three.y, -three.z);

    private float Ground(float x, float z)
    {
        if (_ground != null) return _ground.At(x, z);
        const float sand = -0.6f, pitY = -4.2f, pitX = 13f, pitZ = 15.6f, pitRx = 26f, pitRz = 11.5f;
        float r = Mathf.Sqrt(Mathf.Pow((x - pitX) / pitRx, 2) + Mathf.Pow((z - pitZ) / pitRz, 2));
        float n = Mathf.Sin(x * 0.9f) * Mathf.Cos(z * 0.7f) * 0.08f + Mathf.Sin(x * 0.23f + z * 0.41f) * 0.14f;
        return sand + (pitY - sand) * (1f - Smooth(0.78f, 1.06f, r)) + n * Smooth(0.7f, 1.15f, r);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    private Vector3 Clamp(Vector3 t)
    {
        float dx = (t.x - PanCX) / PanX, dz = (t.z - PanCZ) / PanZ, r = Mathf.Sqrt(dx * dx + dz * dz);
        if (r > 1f) { t.x = PanCX + (t.x - PanCX) / r; t.z = PanCZ + (t.z - PanCZ) / r; }
        for (int i = 0; i < 40 && Clear(t.x, t.z) < WallGap + 1f; i++) t = Vector3.Lerp(t, HomeTarget, 0.1f);
        return t;
    }

    private Vector3 At(float yaw, float pitch, float dist, Vector3 t)
    {
        float pe = pitch - Mathf.Max(0f, (dist - HomeDist) / (MaxDist - HomeDist)) * 0.18f;
        return t + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pe), Mathf.Sin(pe), Mathf.Cos(yaw) * Mathf.Cos(pe)) * dist;
    }

    private static float[] Clearance(ArenaHeight h, Vector3 home, float rise, float drop)
    {
        int nx = h.nx, nz = h.nz, n = nx * nz;
        float limit = h.At(home.x, home.z) + rise, floor = h.At(home.x, home.z) - drop;
        var open = new bool[n];
        int si = Mathf.Clamp(Mathf.RoundToInt((home.x - h.x0) / h.step), 0, nx - 1), sj = Mathf.Clamp(Mathf.RoundToInt((home.z - h.z0) / h.step), 0, nz - 1);
        var q = new System.Collections.Generic.Queue<int>();
        if (h.v[sj * nx + si] <= limit) { open[sj * nx + si] = true; q.Enqueue(sj * nx + si); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(), i = c % nx, j = c / nx;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int a = i + di, b = j + dj;
                if (a < 0 || b < 0 || a >= nx || b >= nz) continue;
                int k = b * nx + a;
                if (open[k] || h.v[k] > limit || h.v[k] < floor) continue;
                open[k] = true;
                q.Enqueue(k);
            }
        }
        var d = new float[n];
        for (int k = 0; k < n; k++)
        {
            int i = k % nx, j = k / nx;
            bool edge = i == 0 || j == 0 || i == nx - 1 || j == nz - 1;
            d[k] = open[k] && !edge ? 999f : 0f;
            if (d[k] == 0f) q.Enqueue(k);
        }
        while (q.Count > 0)
        {
            int c = q.Dequeue(), i = c % nx, j = c / nx;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                int a = i + di, b = j + dj;
                if (a < 0 || b < 0 || a >= nx || b >= nz) continue;
                int k = b * nx + a;
                float w = d[c] + (di != 0 && dj != 0 ? 1.414f : 1f) * h.step;
                if (w >= d[k]) continue;
                d[k] = w;
                q.Enqueue(k);
            }
        }
        return d;
    }

    private float Clear(float x, float z)
    {
        if (_clear == null) return 999f;
        int i = Mathf.RoundToInt((x - _height.x0) / _height.step), j = Mathf.RoundToInt((z - _height.z0) / _height.step);
        if (i < 0 || j < 0 || i >= _height.nx || j >= _height.nz) return 0f;
        return _clear[j * _height.nx + i];
    }

    private float Around(float x, float z, float r)
    {
        float g = Ground(x, z);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.PI / 4f;
            g = Mathf.Max(g, (Ground(x + Mathf.Cos(a) * r, z + Mathf.Sin(a) * r)));
            g = Mathf.Max(g, (Ground(x + Mathf.Cos(a) * r * 0.5f, z + Mathf.Sin(a) * r * 0.5f)));
        }
        return g;
    }


    private float Reach(float yaw, float pitch, float dist, Vector3 t)
    {
        var from = t + Vector3.up * 1.2f;
        var dir = At(yaw, pitch, dist, t) - from;
        float len = dir.magnitude;
        if (len < 0.01f) return dist;
        dir /= len;
        for (float d = 2f; d <= len; d += 0.4f)
        {
            var q = from + dir * d;
            float g = Around(q.x, q.z, 1.2f);
            if (q.y < g + Mathf.Min(1.5f, 0.2f + 0.15f * d) || q.y > CamTop || Clear(q.x, q.z) < WallGap) return Mathf.Max(MinDist * 0.5f, dist * (d - 0.8f) / len);
        }
        return dist;
    }

    private bool Inside(Vector3 q) =>
        Mathf.Sqrt(Mathf.Pow((q.x - CenterX) / (RadiusX * 0.97f), 2) + Mathf.Pow((q.z - CenterZ) / (RadiusZ * 0.97f), 2)) <= 1f && q.z >= ZMin || q.y > StandsTop + 3f;

    private static float DragPixels => Screen.dpi > 1f ? Screen.dpi / 2.54f * 0.5f : 6f;

    private bool DeckHit(Camera eye, Transform grid, Vector3 mouse, out Vector3 three)
    {
        three = default;
        var ray = eye.ScreenPointToRay(mouse);
        var plane = new Plane(grid.up, grid.position);
        if (!plane.Raycast(ray, out float enter)) return false;
        var local = grid.InverseTransformPoint(ray.GetPoint(enter));
        three = new Vector3(local.x, 0f, -local.z);
        return true;
    }

    internal void Steer(Camera eye, Transform grid, bool overUi)
    {
        float wheel = Input.mouseScrollDelta.y;
        if (!overUi && Mathf.Abs(wheel) > 0.01f)
        {
            _homeLock = false;
            float old = _wantDist;
            if (wheel < 0f)
            {
                _wantDist = old < HomeDist - 0.01f ? Mathf.Min(HomeDist, old * 1.15f) : Mathf.Min(MaxDist, old + Mathf.Min(old * 0.15f, (MaxDist - HomeDist) / 4f));
                float f = old >= HomeDist ? 1f : Mathf.Min(1f, (_wantDist - old) / (HomeDist - old));
                _wantTarget = Vector3.Lerp(_wantTarget, HomeTarget, f);
            }
            else
            {
                _wantDist = Mathf.Max(MinDist, _wantDist / 1.15f);
                if (DeckHit(eye, grid, Input.mousePosition, out var hit))
                {
                    hit.x = Mathf.Clamp(hit.x, CellX0, CellX1);
                    hit.z = Mathf.Clamp(hit.z, CellZ0, CellZ1);
                    _wantTarget = Clamp(Vector3.Lerp(_wantTarget, new Vector3(hit.x, _wantTarget.y, hit.z), 1f - _wantDist / old));
                }
            }
        }
        if (!_pressed && !overUi)
        {
            foreach (int button in new[] { 0, 1 })
            {
                if (!Input.GetMouseButtonDown(button)) continue;
                _pressed = true;
                _moving = false;
                _button = button;
                _pressAt = Input.mousePosition;
                _fromYaw = _wantYaw;
                _fromPitch = _wantPitch;
                _fromTarget = _wantTarget;
                break;
            }
        }
        if (!_pressed) return;
        if (!Input.GetMouseButton(_button))
        {
            _pressed = false;
            _moving = false;
            return;
        }
        var delta = Input.mousePosition - _pressAt;
        if (!_moving && delta.magnitude < DragPixels) return;
        _moving = true;
        _homeLock = false;
        if (_button == 1)
        {
            _wantYaw = _fromYaw - delta.x * 0.004f;
            _wantPitch = Mathf.Clamp(_fromPitch - delta.y * 0.004f, PitchMin, PitchMax);
            return;
        }
        var right = new Vector3(Mathf.Cos(_yaw), 0f, -Mathf.Sin(_yaw));
        var fwd = new Vector3(-Mathf.Sin(_yaw), 0f, -Mathf.Cos(_yaw));
        float k = _dist / Mathf.Max(1, Screen.height) * 1.1f;
        _wantTarget = Clamp(_fromTarget - right * (delta.x * k) - fwd * (delta.y * k / Mathf.Max(0.4f, Mathf.Sin(_pitch))));
    }

    internal void Aim(Camera eye, Transform grid)
    {
        float dt = _frame == Time.frameCount ? 0f : Mathf.Clamp(Time.unscaledDeltaTime * 60f, 0f, 3f);
        _frame = Time.frameCount;
        float ease = 1f - Mathf.Pow(1f - 0.12f, dt);
        _fovExtra = _fovExtra < 0f ? _wantFovExtra : _fovExtra + (_wantFovExtra - _fovExtra) * ease;
        _yaw += (_wantYaw - _yaw) * ease;
        _pitch += (_wantPitch - _pitch) * ease;
        _dist += (_wantDist - _dist) * ease;
        _target = Vector3.Lerp(_target, _wantTarget, ease);
        while (_wantDist > MinDist && !Inside(At(_wantYaw, _wantPitch, _wantDist, _wantTarget))) _wantDist -= 0.25f;
        float need = Mathf.Min(_dist, Mathf.Max(18f, _dist * 0.8f)), lift = _pitch, reach = Reach(_yaw, lift, _dist, _target);
        while (reach < need && lift < LiftMax)
        {
            lift = Mathf.Min(LiftMax, lift + 0.03f);
            reach = Reach(_yaw, lift, _dist, _target);
        }
        _lift = _lift < 0f || lift > _lift ? lift : _lift + (lift - _lift) * ease;
        reach = Reach(_yaw, _lift, _dist, _target);
        _boom = _boom < 0f || reach < _boom ? reach : _boom + (reach - _boom) * ease;
        var pos = At(_yaw, _lift, _boom, _target);
        float floor = Mathf.Max(Around(pos.x, pos.z, 1.2f) + 2.5f, CamLow);
        _floor = _floorSet && floor < _floor ? _floor + (floor - _floor) * (1f - Mathf.Pow(1f - 0.08f, dt)) : floor;
        _floorSet = true;
        if (pos.y < _floor) pos.y = _floor;
        if (_homeLock) FitHome(eye, pos);
        var worldPos = grid.TransformPoint(ToGrid(pos));
        var worldTarget = grid.TransformPoint(ToGrid(_target));
        eye.orthographic = false;
        eye.fieldOfView = 30f + Mathf.Max(0f, (_dist - HomeDist) / (MaxDist - HomeDist)) * 16f + _fovExtra * Mathf.Clamp01((_dist - MinDist) / (HomeDist - MinDist));
        eye.nearClipPlane = 0.3f;
        eye.farClipPlane = 600f;
        eye.transform.position = worldPos;
        eye.transform.rotation = Quaternion.LookRotation(worldTarget - worldPos, grid.up);
        _shift += (DockShare() - _shift) * ease;
        eye.ResetProjectionMatrix();
        if (_shift > 0.002f)
        {
            var p = eye.projectionMatrix;
            p.m12 = -_shift;
            eye.projectionMatrix = p;
        }
    }

    private float _shift;
    private static System.Func<RectTransform> _dock;
    private static bool _dockLooked;
    private static readonly Vector3[] Corners = new Vector3[4];

    private static float DockShare()
    {
        if (!_dockLooked)
        {
            _dockLooked = true;
            try
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var prop = asm.GetType("NewAgeQoL.ChatDock", false)?.GetProperty("Root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop == null) continue;
                    _dock = (System.Func<RectTransform>)System.Delegate.CreateDelegate(typeof(System.Func<RectTransform>), prop.GetGetMethod(true));
                    break;
                }
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[arena3d] chat dock: " + e.Message); }
        }
        RectTransform rt = null;
        try { if (_dock != null) rt = _dock(); }
        catch { }
        if (rt == null || !rt.gameObject.activeInHierarchy || Screen.height <= 0) return 0f;
        rt.GetWorldCorners(Corners);
        float top = Mathf.Max(Corners[1].y, Corners[2].y);
        return Mathf.Clamp(top / Screen.height, 0f, 0.45f);
    }
}
