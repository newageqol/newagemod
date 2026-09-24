using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class MapLabels
    {
        private const string LabelName = "QoLVertexLabel";

        private sealed class Row
        {
            internal TextMesh Text;
            internal GlobalMapVertex Vertex;
        }

        private static readonly List<Row> Live = new List<Row>();
        private static Font _font;

        internal static bool Enabled => Plugin.CfgMapLabels == null || Plugin.CfgMapLabels.Value;

        private static bool WithType => Plugin.CfgMapLabelType == null || Plugin.CfgMapLabelType.Value;

        private const float Size = 0.08f;

        private static int Sharpness => Plugin.CfgMapLabelFont != null ? Plugin.CfgMapLabelFont.Value : 48;

        private static float Lift => Plugin.CfgMapLabelLift != null ? Plugin.CfgMapLabelLift.Value : 0.7f;

        private static bool ToCamera => Plugin.CfgMapLabelBillboard == null || Plugin.CfgMapLabelBillboard.Value;

        internal static void Apply(GlobalMapVertex vertex)
        {
            try
            {
                var go = vertex != null ? vertex.VertexGameObject : null;
                if (go == null) return;

                var found = go.transform.Find(LabelName);
                if (!Enabled)
                {
                    if (found != null && found.gameObject.activeSelf) found.gameObject.SetActive(false);
                    return;
                }

                var label = found != null ? found.GetComponent<TextMesh>() : Build(go);
                if (label == null) return;
                Remember(label, vertex);
                Dress(label, vertex);
            }
            catch (Exception e) { Plugin.Fault("[map] метка точки: " + e.Message); }
        }

        internal static void Refresh()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var row = Live[i];
                if (row.Text == null) { Live.RemoveAt(i); continue; }
                if (!Enabled) { row.Text.gameObject.SetActive(false); continue; }
                Dress(row.Text, row.Vertex);
            }
        }

        private static void Remember(TextMesh label, GlobalMapVertex vertex)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i].Text == null) { Live.RemoveAt(i); continue; }
                if (Live[i].Text == label) { Live[i].Vertex = vertex; return; }
            }
            Live.Add(new Row { Text = label, Vertex = vertex });
        }

        private static void Dress(TextMesh label, GlobalMapVertex vertex)
        {
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);

            string text = Caption(vertex);
            if (text != null && label.text != text) label.text = text;
            label.characterSize = Size;
            label.fontSize = Sharpness;
            label.color = LabelColor();
            label.transform.localPosition = new Vector3(0f, Lift, 0f);

            if (ToCamera) MapLabelBillboard.Ensure(label.transform);
            else label.transform.localRotation = Quaternion.identity;
        }

        internal static bool Spinning => Enabled && ToCamera && Live.Count > 0;

        internal static void Aim(Quaternion turn)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var text = Live[i].Text;
                if (text == null) { Live.RemoveAt(i); continue; }
                text.transform.rotation = turn;
            }
        }

        private static string Caption(GlobalMapVertex vertex)
        {
            if (vertex == null) return null;
            string text = "v" + vertex.Id;
            if (!WithType) return text;
            switch (vertex.VertexType)
            {
                case EGlobalMapVertexType.Combat: return text + " бой";
                case EGlobalMapVertexType.Road: return text + " дорога";
                default: return text + " переход";
            }
        }

        private static Color LabelColor()
        {
            string hex = Plugin.CfgMapLabelColor != null ? Plugin.CfgMapLabelColor.Value : null;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.yellow;
        }

        private static TextMesh Build(GameObject vertexGo)
        {
            var go = new GameObject(LabelName);
            go.layer = vertexGo.layer;
            go.transform.SetParent(vertexGo.transform, worldPositionStays: false);
            go.transform.localRotation = Quaternion.identity;

            var label = go.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;

            var font = Face();
            if (font != null) label.font = font;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (font != null && font.material != null) mr.material = font.material;

                mr.sortingLayerName = "bilding";
                mr.sortingOrder = 1000;
            }
            return label;
        }

        private static Font Face()
        {
            if (_font != null) return _font;
            try { _font = Font.CreateDynamicFontFromOSFont("Arial", 32); }
            catch (Exception e) { Plugin.Warn("[map] шрифт метки: " + e.Message); }
            return _font;
        }
    }

    public class MapLabelBillboard : MonoBehaviour
    {
        private static MapLabelBillboard _one;
        private static Camera _cam;
        private static Quaternion _last;
        private static bool _aimed;

        internal static void Ensure(Transform label)
        {
            if (_one == null && Plugin.Instance != null)
            {
                _one = Plugin.Instance.gameObject.GetComponent<MapLabelBillboard>();
                if (_one == null) _one = Plugin.Instance.gameObject.AddComponent<MapLabelBillboard>();
            }
            var eye = Eye();
            if (eye != null && label != null) label.rotation = eye.transform.rotation;
        }

        private static Camera Eye()
        {
            if (_cam == null) _cam = Camera.main != null ? Camera.main : Any();
            return _cam;
        }

        private void LateUpdate()
        {
            if (!MapLabels.Spinning) return;
            var eye = Eye();
            if (eye == null) return;
            var turn = eye.transform.rotation;
            if (_aimed && turn == _last) return;
            _last = turn;
            _aimed = true;
            MapLabels.Aim(turn);
        }

        private static Camera Any()
        {
            if (Camera.current != null) return Camera.current;
            var all = Camera.allCameras;
            return all != null && all.Length > 0 ? all[0] : null;
        }
    }

    [HarmonyPatch(typeof(GlobalMapVertex), "ApplyState")]
    public static class MapVertexLabelPatch
    {
        private static void Postfix(GlobalMapVertex __instance) => MapLabels.Apply(__instance);
    }
}
