using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D
{
    public sealed class ArenaStorm : MonoBehaviour
    {
        readonly List<ParticleSystem> _systems = new List<ParticleSystem>();

        public void Init(Transform root, ArenaCfg cfg, Material streak, Material cloud, bool night, Color sun)
        {
            var col = night ? ArenaCfg.Hex("4a3a30") : Color.Lerp(ArenaCfg.Hex("e0b07a"), sun, 0.25f);
            var wind = root.TransformDirection(new Vector3(1f, 0f, -0.35f).normalized);
            var center = new Vector3(cfg.center[0], 0f, -cfg.center[1]);
            if (streak != null)
            {
                var c = Color.Lerp(col, ArenaCfg.Hex("ffe2b8"), 0.12f); c.a = night ? 0.4f : 0.6f;
                var ps = Make(root, "storm_streaks", center + new Vector3(0f, 2.5f, 0f), wind, new Vector3(90f, 4.8f, 90f), streak, c, 4500);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 17f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.035f);
                var em = ps.emission; em.rateOverTime = 930f;
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.6f; noise.frequency = 0.4f; noise.scrollSpeed = 0.5f;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.05f;
                r.lengthScale = 1f;
                Fade(ps, 0.1f);
                ps.Play();
            }
            if (cloud != null)
            {
                Clouds(root, center + new Vector3(0f, 0.8f, 0f), wind, new Vector3(90f, 0.8f, 90f), cloud, col, (night ? 0.6f : 1f) * 0.65f, 18, 5f, 10f, 0.32f, 6f, 10f);
                Clouds(root, center + new Vector3(0f, 5f, 0f), wind, new Vector3(90f, 6f, 90f), cloud, col, (night ? 0.6f : 1f) * 0.42f, 28, 12f, 24f, 0.55f, 3f, 6f);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = col * (night ? 0.5f : 0.85f);
            RenderSettings.fogStartDistance = 26f;
            RenderSettings.fogEndDistance = 150f;
        }

        void Clouds(Transform root, Vector3 at, Vector3 wind, Vector3 box, Material mat, Color col, float alpha, int count, float s0, float s1, float flat, float v0, float v1)
        {
            col.a = alpha;
            var ps = Make(root, "storm_clouds", at, wind, box, mat, col, count + 4);
            var main = ps.main;
            float life = 90f / ((v0 + v1) * 0.5f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.8f, life * 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(v0, v1);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(s0, s1);
            main.startSizeY = new ParticleSystem.MinMaxCurve(s0 * flat, s1 * flat);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var em = ps.emission; em.rateOverTime = count / life;
            Fade(ps, 0.2f);
            ps.Play();
        }

        ParticleSystem Make(Transform root, string name, Vector3 local, Vector3 wind, Vector3 box, Material mat, Color col, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.rotation = Quaternion.LookRotation(wind, root.up);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startColor = col;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box; sh.position = -Vector3.forward * box.z * 0.25f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _systems.Add(ps);
            return ps;
        }

        static void Fade(ParticleSystem ps, float edge)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new UnityEngine.Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, edge), new GradientAlphaKey(1f, 1f - edge), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        void Update()
        {
            float t = Time.time * 1000f, gust = 1f + 0.35f * Mathf.Sin(t / 1900f) + 0.2f * Mathf.Sin(t / 700f);
            foreach (var ps in _systems)
            {
                if (ps == null) continue;
                var main = ps.main;
                main.simulationSpeed = gust;
            }
        }
    }
}
