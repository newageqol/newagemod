using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class CombatCam
    {
        private static CameraControl _control;
        private static CameraConstraint _rig;
        private static Vector3 _minPos, _maxPos, _lowMin, _lowMax;
        private static float _minAngle, _maxAngle;
        private static bool _saved;
        private static float _atZoom;
        private static float _pollAt;
        private static bool _pulled;
        private static float _panDone;
        private static float _rigAt;
        private static float _panFrozen;
        private static bool _panLocked;
        private static FieldInfo _rigField;
        private static PropertyInfo _bodyProp;
        private static bool _wired;

        private static void Wire()
        {
            if (_wired) return;
            _wired = true;
            try
            {
                _rigField = AccessTools.Field(typeof(CameraControl), "constraint");
                _bodyProp = AccessTools.Property(typeof(BaseUserInput), "cameraTransform");
            }
            catch (Exception e) { Plugin.Trace("[камера] поля камеры: " + e.Message); }
        }

        private static float Pan
        {
            get
            {
                if (!Fight || _control == null) return 0f;
                float pixels = ChatDock.CoverPixels;
                if (pixels < 1f) return 0f;
                var body = _bodyProp?.GetValue(_control) as Transform;
                var cam = body != null ? body.GetComponent<Camera>() : null;
                if (cam == null) cam = Camera.main;
                if (cam == null) return 0f;
                var floor = new Plane(Vector3.up, Vector3.zero);
                float middle = Screen.height * 0.5f;
                Vector3 a, b;
                if (!Hit(cam, floor, new Vector3(Screen.width * 0.5f, middle, 0f), out a)) return 0f;
                if (!Hit(cam, floor, new Vector3(Screen.width * 0.5f, middle + pixels, 0f), out b)) return 0f;
                float extra = Plugin.CfgCamPanExtra != null ? Mathf.Clamp(Plugin.CfgCamPanExtra.Value, 0f, 20f) : 0f;
                return Mathf.Clamp(Vector3.Distance(a, b) + extra, 0f, 40f);
            }
        }

        private static bool Hit(Camera cam, Plane floor, Vector3 screen, out Vector3 spot)
        {
            var ray = cam.ScreenPointToRay(screen);
            float along;
            if (floor.Raycast(ray, out along)) { spot = ray.GetPoint(along); return true; }
            spot = Vector3.zero;
            return false;
        }

        private static float Room =>
            Fight && Plugin.CfgCamRoomExtra != null ? Mathf.Clamp(Plugin.CfgCamRoomExtra.Value, 0f, 20f) : 0f;

        private static bool Fight => SideButtons.InCombat();

        private static float Zoom
        {
            get
            {
                var cfg = Fight ? Plugin.CfgCamZoom : null;
                return cfg == null ? 1f : Mathf.Clamp(cfg.Value, 1f, 4f);
            }
        }

        private static bool StartFar => Fight;

        internal static void Wake()
        {
            _pollAt = 0f;
        }

        internal static void Tick()
        {
            try
            {
                if (Time.unscaledTime < _pollAt) return;
                _pollAt = Time.unscaledTime + (_pulled ? 0.5f : 0.02f);
                Wire();

                if (!SideButtons.InWorld() || !Fight) { Forget(); return; }

                var control = _control;
                if (control == null)
                {
                    control = UnityEngine.Object.FindObjectOfType<CameraControl>();
                    if (control == null) { Forget(); return; }
                    _control = control;
                    _rig = null;
                }

                var rig = _rigField?.GetValue(control) as CameraConstraint;
                if (rig == null) rig = control.GetComponent<CameraConstraint>();
                if (rig == null) return;
                if (_rig != rig)
                {
                    _rig = rig;
                    _saved = false;
                }

                if (!_saved)
                {
                    _minPos = rig.minPos;
                    _maxPos = rig.maxPos;
                    _lowMin = rig.lowerMinPos;
                    _lowMax = rig.lowermaxPos;
                    _minAngle = rig.minAngle;
                    _maxAngle = rig.maxAngle;
                    _saved = true;
                    _rigAt = Time.unscaledTime;
                    _atZoom = -1f;
                    _pulled = false;
                    _panLocked = false;
                    _panFrozen = 0f;
                }

                float zoom = Zoom;
                if (!_panLocked)
                {
                    float measured = Pan;
                    if (measured > 0.001f || Time.unscaledTime - _rigAt > 3f)
                    {
                        _panFrozen = measured;
                        _panLocked = true;
                        Plugin.Trace("[камера] запас под панель посчитан один раз: " + measured.ToString("0.0"));
                    }
                    else return;
                }
                float pan = _panFrozen + Room;
                if (Mathf.Abs(zoom - _atZoom) >= 0.01f || Mathf.Abs(pan - _panDone) >= 0.01f)
                {
                    _atZoom = zoom;
                    _panDone = pan;
                    float room = Room;
                    pan -= room;
                    var span = _maxPos - _minPos;
                    var lowSpan = _lowMax - _lowMin;
                    float extra = zoom - 1f;

                    rig.minPos = _minPos;
                    rig.lowerMinPos = _lowMin;
                    rig.minAngle = _minAngle;
                    rig.maxAngle = _maxAngle;
                    rig.maxPos = _maxPos + span * extra;
                    rig.lowermaxPos = _lowMax + lowSpan * extra;
                    if (room > 0.01f)
                    {
                        var side = new Vector3(room, 0f, room);
                        rig.minPos -= side;
                        rig.lowerMinPos -= side;
                        rig.maxPos += side;
                        rig.lowermaxPos += side;
                    }
                    if (pan > 0.01f)
                    {
                        var body = _bodyProp?.GetValue(control) as Transform;
                        var flat = body != null ? Vector3.ProjectOnPlane(body.forward, Vector3.up) : Vector3.zero;
                        if (flat.sqrMagnitude > 0.0001f)
                        {
                            var back = -flat.normalized * pan;
                            rig.minPos = Wider(rig.minPos, back, true);
                            rig.maxPos = Wider(rig.maxPos, back, false);
                            rig.lowerMinPos = Wider(rig.lowerMinPos, back, true);
                            rig.lowermaxPos = Wider(rig.lowermaxPos, back, false);
                            Plugin.Trace("[камера] бой: границы сдвинуты к низу экрана на " + pan.ToString("0.0") + " по " + back);
                        }
                    }

                    Plugin.Trace("[камера] " + (Fight ? "бой" : "локация") + ": предел отдаления "
                                 + _maxPos.y.ToString("0.0") + " → " + rig.maxPos.y.ToString("0.0"));
                }
            }
            catch (Exception e) { Plugin.Trace("[камера] " + e.Message); }

            try { Pull(); }
            catch (Exception e) { Plugin.Trace("[камера] отвод: " + e.Message); }
        }

        private static Vector3 Wider(Vector3 edge, Vector3 by, bool low)
        {
            if (low)
            {
                if (by.x < 0f) edge.x += by.x;
                if (by.z < 0f) edge.z += by.z;
            }
            else
            {
                if (by.x > 0f) edge.x += by.x;
                if (by.z > 0f) edge.z += by.z;
            }
            return edge;
        }

        private static void Forget()
        {
            if (_saved && _rig != null)
            {
                try
                {
                    _rig.minPos = _minPos;
                    _rig.maxPos = _maxPos;
                    _rig.lowerMinPos = _lowMin;
                    _rig.lowermaxPos = _lowMax;
                    _rig.minAngle = _minAngle;
                    _rig.maxAngle = _maxAngle;
                }
                catch (Exception e) { Plugin.Trace("[камера] вернуть исходные границы: " + e.Message); }
            }
            _control = null;
            _rig = null;
            _saved = false;
            _pulled = false;
        }

        private static int Crowd()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null || cd.Characters == null) return 0;
                int count = 0;
                foreach (var pair in cd.Characters)
                {
                    var one = pair.Value;
                    if (one != null && !one.Dead) count++;
                }
                return count;
            }
            catch { return 0; }
        }

        private const float Base = 16f;

        private static float Span => Base * Zoom;

        private static Camera Eye(Transform body)
        {
            var cam = body != null ? body.GetComponent<Camera>() : null;
            return cam != null ? cam : Camera.main;
        }

        private static float Roomy(Transform body, float pitch)
        {
            var cam = Eye(body);
            if (cam == null || pitch < 0.01f) return 1f;
            float wide = Ground(cam);
            return wide < 0.01f ? 1f : wide / (pitch * Span);
        }

        private static readonly object[] FitArgs = new object[3];
        private static readonly object[] TurnArgs = new object[1];
        private static readonly List<AbstractCharacter> Alive = new List<AbstractCharacter>();

        private static bool Step(object control, Transform body, System.Reflection.MethodInfo fit,
                                 System.Reflection.MethodInfo turn, float angleStep, float way)
        {
            var want = body.position + body.forward * (way * 0.25f * 6f);
            FitArgs[0] = null;
            FitArgs[1] = want;
            FitArgs[2] = true;
            bool ok = (bool)fit.Invoke(control, FitArgs);
            if (!ok) return false;
            body.position = (Vector3)FitArgs[0];
            TurnArgs[0] = way * 0.25f * 6f * angleStep;
            try { turn.Invoke(control, TurnArgs); } catch { }
            return true;
        }

        private static float Pitch()
        {
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null || cd.Characters == null) return 0f;
                var all = Alive;
                all.Clear();
                foreach (var pair in cd.Characters)
                    if (pair.Value != null && !pair.Value.Dead) all.Add(pair.Value);
                float best = 0f;
                for (int i = 0; i < all.Count; i++)
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        int steps = HexUtils.range(all[i].HexGridPosition, all[j].HexGridPosition);
                        if (steps < 2) continue;
                        float span = Vector3.Distance(all[i].position, all[j].position) / steps;
                        if (span > best) best = span;
                    }
                return best;
            }
            catch (Exception e) { Plugin.Trace("[камера] шаг клетки: " + e.Message); return 0f; }
        }

        private static float Ground(Camera cam)
        {
            var floor = new Plane(Vector3.up, Vector3.zero);
            float middle = Screen.height * 0.5f;
            Vector3 left, right;
            if (!Hit(cam, floor, new Vector3(Screen.width * 0.1f, middle, 0f), out left)) return 0f;
            if (!Hit(cam, floor, new Vector3(Screen.width * 0.9f, middle, 0f), out right)) return 0f;
            return Vector3.Distance(left, right) / 0.8f;
        }

        private static bool Mine(Camera cam)
        {
            try
            {
                var cd = FighterHint.Cd();
                var me = cd != null ? cd.MyCharacter : null;
                if (me == null) return true;
                var spot = cam.WorldToScreenPoint(me.position + Vector3.up * 2f);
                if (spot.z <= 0f) return false;
                float low = ChatDock.CoverPixels + Screen.height * 0.05f;
                return spot.x > Screen.width * 0.1f && spot.x < Screen.width * 0.9f
                       && spot.y > low && spot.y < Screen.height * 0.9f;
            }
            catch { return true; }
        }

        private static bool Enough(Transform body, float pitch, out string why)
        {
            var cam = body != null ? body.GetComponent<Camera>() : null;
            if (cam == null) cam = Camera.main;
            if (cam == null) { why = "камеры нет"; return true; }
            if (pitch < 0.01f)
            {
                bool all = Fits(body);
                why = all ? "все бойцы в кадре" : "бойцы ещё не влезают";
                return all;
            }
            float wide = Ground(cam);
            bool mine = Mine(cam);
            why = "в кадре " + (wide / pitch).ToString("0.0") + " клеток из " + Span.ToString("0.0")
                  + (mine ? "" : ", свой боец у края");
            return wide >= pitch * Span && mine;
        }

        private static bool Fits(Transform body)
        {
            try
            {
                var cam = body != null ? body.GetComponent<Camera>() : null;
                if (cam == null) cam = Camera.main;
                var cd = FighterHint.Cd();
                if (cam == null || cd == null || cd.Characters == null) return false;

                float low = ChatDock.CoverPixels + Screen.height * 0.05f;
                float high = Screen.height * 0.9f;
                float left = Screen.width * 0.08f;
                float right = Screen.width * 0.92f;
                int seen = 0;
                foreach (var pair in cd.Characters)
                {
                    var one = pair.Value;
                    if (one == null || one.Dead) continue;
                    var spot = cam.WorldToScreenPoint(one.position + Vector3.up * 2f);
                    if (spot.z <= 0f) return false;
                    if (spot.x < left || spot.x > right || spot.y < low || spot.y > high) return false;
                    seen++;
                }
                return seen > 0;
            }
            catch (Exception e) { Plugin.Trace("[камера] обзор: " + e.Message); return false; }
        }

        private static void Pull()
        {
            if (_pulled || _rig == null || _control == null) return;
            if (!StartFar) { _pulled = true; return; }

            var control = _control;
            var body = _bodyProp?.GetValue(control) as Transform;
            var step = AccessTools.Field(typeof(CameraControl), "angleStep");
            var fit = AccessTools.Method(typeof(CameraControl), "checkConstraints");
            var turn = AccessTools.Method(typeof(CameraControl), "changeCameraAngle");
            if (body == null || step == null || fit == null || turn == null) { _pulled = true; return; }

            bool patient = Time.unscaledTime - _rigAt < 5f;
            int crowd = Crowd();
            if (crowd == 0 && patient) return;

            float angleStep = (float)step.GetValue(control);
            float pitch = Pitch();
            int moved = 0;
            string why = "бойцов не нашлось";
            for (int i = 0; i < 40; i++)
            {
                if (crowd > 0 && Enough(body, pitch, out why)) break;
                if (!Step(control, body, fit, turn, angleStep, -1f)) { why += ", дальше некуда"; break; }
                moved++;
            }
            if (crowd > 0 && moved == 0)
            {
                for (int i = 0; i < 40; i++)
                {
                    if (Roomy(body, pitch) <= 1.1f) break;
                    if (!Step(control, body, fit, turn, angleStep, 1f)) { why += ", ближе некуда"; break; }
                    if (!Mine(Eye(body)))
                    {
                        Step(control, body, fit, turn, angleStep, -1f);
                        why = "свой боец у края, ближе не подхожу";
                        break;
                    }
                    moved--;
                }
                if (moved < 0) Enough(body, pitch, out why);
            }

            _pulled = true;
            Plugin.Trace("[камера] вход: бойцов " + crowd + ", шагов " + moved + ", " + why);
        }
    }
}
