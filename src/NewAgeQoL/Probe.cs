using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal static class Probe
    {
        private const int Tries = 12;

        private static float _next;
        private static bool _done;
        private static int _tried;
        private static string _scene = "";

        internal static void Tick()
        {
            if (Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene != _scene) { _scene = scene; _done = false; _tried = 0; }
                if (_done) return;

                var view = UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
                if (view == null)
                {
                    if (++_tried < Tries) return;
                    _done = true;
                    Plugin.Trace("[осмотр] окно заявок в сцене " + scene + " не появилось, больше не ищу");
                    return;
                }
                _done = true;

                var canvas = view.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.transform : view.transform.root;
                var text = new StringBuilder();
                text.Append("[осмотр] сцена ").Append(scene).Append(", окно заявок:\n");
                Walk(root, 0, 8, text);
                Plugin.Log?.LogInfo(text.ToString());
            }
            catch (Exception e) { Plugin.Trace("[осмотр] " + e.Message); }
        }

        private static void Walk(Transform node, int depth, int limit, StringBuilder text)
        {
            if (node == null || depth > limit) return;
            var rt = node as RectTransform;
            text.Append(new string(' ', depth * 2)).Append(node.name);
            if (!node.gameObject.activeSelf) text.Append(" [скрыт]");
            if (rt != null)
                text.Append(" ").Append(Mathf.RoundToInt(rt.rect.width)).Append("x").Append(Mathf.RoundToInt(rt.rect.height));
            foreach (var part in node.GetComponents<Component>())
            {
                if (part == null) continue;
                string name = part.GetType().Name;
                if (name == "RectTransform" || name == "CanvasRenderer" || name == "Transform") continue;
                text.Append(" ·").Append(name);
            }
            text.Append('\n');
            for (int i = 0; i < node.childCount; i++) Walk(node.GetChild(i), depth + 1, limit, text);
        }
    }
}
