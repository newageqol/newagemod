using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(StaticLocationLoadController), "AssetDataLoaded")]
    internal static class TownZoom
    {
        private static void Postfix(StaticLocationLoadController __instance)
        {
            try
            {
                var infos = Traverse.Create(__instance).Field("_sceneObjectInfos").GetValue<IList<SceneObjectInfo>>();
                if (infos == null) return;
                bool town = false;
                foreach (var i in infos)
                    if (i != null && i.ObjectType == EObjectType.LeaveTown) { town = true; break; }
                if (town && Plugin.Instance != null) Plugin.Instance.StartCoroutine(Later());
            }
            catch (Exception e) { Plugin.Trace("[zoom] " + e.Message); }
        }

        private static IEnumerator Later()
        {
            yield return null;
            yield return null;
            Out(UnityEngine.Object.FindObjectOfType<CameraControl>());
        }

        internal static void Out(CameraControl cc)
        {
            try
            {
                if (cc == null) return;
                var t = cc.gridCamera != null ? cc.gridCamera.transform : Camera.main != null ? Camera.main.transform : null;
                if (t == null || Traverse.Create(cc).Field("constraint").GetValue() == null) return;
                var check = AccessTools.Method(typeof(CameraControl), "checkConstraints");
                var start = t.position;
                var rot = t.rotation;
                for (int k = 0; k < 80; k++)
                {
                    var args = new object[] { null, t.position - t.forward * 0.25f, true };
                    if (!(bool)check.Invoke(cc, args)) break;
                    var next = (Vector3)args[0];
                    if (float.IsNaN(next.x) || (next - t.position).sqrMagnitude < 1e-6f) break;
                    t.position = next;
                }
                t.rotation = rot;
                if (!Physics.Raycast(t.position, t.forward, 300f))
                {
                    t.position = start;
                    Plugin.Trace("[zoom] camera lost the ground after zooming out, restored");
                    return;
                }
                Plugin.Trace("[zoom] town camera zoomed out: " + t.position);
            }
            catch (Exception e) { Plugin.Trace("[zoom] out: " + e.Message); }
        }
    }
}
