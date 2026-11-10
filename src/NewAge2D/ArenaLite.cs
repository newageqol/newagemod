using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewAge2D
{
    public sealed class ArenaLite : MonoBehaviour
    {
        private const int PixelLights = 4;
        private const float Warmup = 3f, Window = 4f, SlowFps = 27f, ShadowFar = 45f, LodBias = 0.5f;

        private static bool _slow;
        private static bool? _weakCard;

        private Transform _root;
        private Vector3 _mid;
        private bool _applied;
        private float _wait, _span;
        private int _frames;

        internal static bool WeakCard
        {
            get
            {
                if (_weakCard.HasValue) return _weakCard.Value;
                string name = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
                int memory = SystemInfo.graphicsMemorySize;
                bool intel = name.Contains("intel") && !name.Contains("arc");
                bool radeon = name.Contains("radeon") && !name.Contains("rx") && (name.Contains("graphics") || name.Contains("vega"));
                bool small = memory > 0 && memory < 2500;
                _weakCard = intel || radeon || small;
                Plugin.Log.LogInfo($"[arena3d] video card {SystemInfo.graphicsDeviceName}, memory {memory} MB, weak {_weakCard.Value}");
                return _weakCard.Value;
            }
        }

        internal void Init(Transform root, Vector3 mid)
        {
            _root = root;
            _mid = mid;
            if (WeakCard || _slow) Apply(WeakCard ? "video card" : "slow earlier");
        }

        private void Update()
        {
            if (_applied || _root == null) return;
            if (!Application.isFocused) { _wait = 0f; _span = 0f; _frames = 0; return; }
            float step = Time.unscaledDeltaTime;
            if (_wait < Warmup) { _wait += step; return; }
            _span += step;
            _frames++;
            if (_span < Window) return;
            float fps = _frames / _span;
            _span = 0f;
            _frames = 0;
            if (fps >= SlowFps) return;
            _slow = true;
            Apply($"{fps:0} fps");
        }

        private void Apply(string why)
        {
            _applied = true;
            QualitySettings.shadowDistance = Mathf.Min(QualitySettings.shadowDistance, ShadowFar);
            QualitySettings.shadowCascades = 1;
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            QualitySettings.lodBias = Mathf.Min(QualitySettings.lodBias, LodBias);
            var lights = new List<Light>();
            foreach (var light in _root.GetComponentsInChildren<Light>())
            {
                if (!light.isActiveAndEnabled) continue;
                if (light.type == LightType.Directional) { if (light.shadows == LightShadows.Soft) light.shadows = LightShadows.Hard; continue; }
                if (light.renderMode == LightRenderMode.ForceVertex || light.name.StartsWith("collapse")) continue;
                lights.Add(light);
            }
            int demoted = 0;
            foreach (var light in lights.OrderByDescending(Weight).Skip(PixelLights))
            {
                light.renderMode = LightRenderMode.ForceVertex;
                light.range *= 1.3f;
                light.intensity *= 1.2f;
                if (light.TryGetComponent<Animation>(out var flicker)) flicker.enabled = false;
                demoted++;
            }
            Plugin.Log.LogInfo($"[arena3d] light mode on ({why}), lights to vertex {demoted} of {lights.Count}");
        }

        private float Weight(Light light)
        {
            float away = (light.transform.position - _mid).magnitude;
            float reach = Mathf.Max(0f, light.range - away * 0.5f);
            return reach * light.intensity * (light.type == LightType.Spot ? 1.5f : 1f);
        }
    }
}
