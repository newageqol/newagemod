using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(GlobalMapController), "OnGlobalPositionResponse")]
    internal static class MapCenter
    {
        private static readonly System.Reflection.FieldInfo ControlField = AccessTools.Field(typeof(GlobalMapController), "_cameraControl");
        private static readonly System.Reflection.FieldInfo PositionField = AccessTools.Field(typeof(GlobalMapController), "_currentPosition");

        private static bool _fought;
        private static float _at;

        internal static void Tick()
        {
            if (Time.unscaledTime < _at) return;
            _at = Time.unscaledTime + 0.25f;
            if (SideButtons.InCombat()) _fought = true;
        }

        private static void Postfix(GlobalMapController __instance)
        {
            try
            {
                if (!_fought || __instance == null || Plugin.Instance == null) return;
                _fought = false;
                Plugin.Instance.StartCoroutine(Later(__instance));
            }
            catch (Exception e) { Plugin.Caught("[map center] " + e.Message); }
        }

        private static IEnumerator Later(GlobalMapController map)
        {
            yield return null;
            yield return null;
            Center(map);
        }

        private static void Center(GlobalMapController map)
        {
            try
            {
                if (map == null) return;
                var cc = ControlField != null ? ControlField.GetValue(map) as CameraControl : null;
                var cam = cc != null && cc.gridCamera != null ? cc.gridCamera : Camera.main;
                if (cc == null || cam == null) return;
                var spots = map.Vertices;
                int at = PositionField != null ? (int)PositionField.GetValue(map) : -1;
                if (spots == null || at < 0 || at >= spots.Length || spots[at] == null || spots[at].VertexGameObject == null) return;
                var spot = spots[at].VertexGameObject.transform.position;

                var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                var ground = new Plane(Vector3.up, spot);
                float far;
                if (!ground.Raycast(ray, out far)) return;
                var seen = ray.GetPoint(far);
                var t = cam.transform;
                var want = t.position + new Vector3(spot.x - seen.x, 0f, spot.z - seen.z);
                cc.SetCameraPosition(want);
                map.OnCameraPositionChanged();
                Plugin.Trace("[map center] camera centered on point " + at + " after the fight: " + t.position);
            }
            catch (Exception e) { Plugin.Caught("[map center] " + e.Message); }
        }
    }
}
