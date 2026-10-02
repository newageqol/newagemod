using UnityEngine;

namespace NewAgeQoL
{
    internal static class Backdrop
    {
        private const float Wait = 1.5f;
        private static readonly Color Dark = new Color(0.05f, 0.05f, 0.07f, 1f);

        private static Camera _cam;
        private static float _bareSince = -1f;

        internal static Camera Cam => _cam;

        internal static void Tick()
        {
            bool bare = SideButtons.Bare();
            if (!bare) _bareSince = -1f;
            else if (_bareSince < 0f) _bareSince = Time.unscaledTime;
            bool want = bare && Time.unscaledTime - _bareSince >= Wait;

            if (_cam == null)
            {
                if (!want) return;
                Build();
                if (_cam == null) return;
            }
            if (_cam.enabled == want) return;
            _cam.enabled = want;
            Plugin.Trace(want
                ? "[backdrop] no game camera for " + Wait + " s, screen cleared by our own"
                : "[backdrop] game camera is back, ours off");
        }

        private static void Build()
        {
            var go = new GameObject("QoLBackdrop");
            Object.DontDestroyOnLoad(go);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Dark;
            cam.cullingMask = 0;
            cam.depth = -100f;
            cam.useOcclusionCulling = false;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            _cam = cam;
        }
    }
}
