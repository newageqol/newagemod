using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D
{
    public sealed class ArenaLook : MonoBehaviour
    {
        struct Key
        {
            public float h, el, si, hi, exp, nk;
            public Color sun, sky, gnd, fog;
            public Key(float h, float el, int sun, float si, int sky, int gnd, float hi, int fog, float exp, float nk)
            {
                this.h = h; this.el = el; this.si = si; this.hi = hi; this.exp = exp; this.nk = nk;
                this.sun = Hex(sun); this.sky = Hex(sky); this.gnd = Hex(gnd); this.fog = Hex(fog);
            }
        }

        static Color Hex(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);

        static readonly Key Night = new Key(0, 55, 0x9fb6ff, 1.0f, 0x3a4c84, 0x18140f, 0.7f, 0x0e1322, 1.05f, 1f);
        static readonly Key Dusk = new Key(0, 20, 0xe8968a, 1.7f, 0x9a8ab4, 0x5a4838, 1.15f, 0x4a3a4c, 1.1f, 0.3f);
        static readonly Key Dawn = new Key(0, 24, 0xffb08a, 2.2f, 0xd6c4d6, 0x9a7a5c, 1.05f, 0xd9b9a6, 1.0f, 0f);
        static readonly Key Day = new Key(0, 46, 0xffe0b0, 2.6f, 0xdfe6ff, 0xc9a070, 1.1f, 0xd9c7a4, 1.0f, 0f);
        static readonly Key Noon = new Key(0, 58, 0xffe0b0, 2.8f, 0xdfe6ff, 0xc9a070, 1.1f, 0xd9c7a4, 1.0f, 0f);
        static readonly Key Eve = new Key(0, 24, 0xff9a5a, 2.3f, 0xdcb494, 0x8a6042, 1.0f, 0xcf9472, 1.0f, 0f);

        static readonly (float h, Key k)[] Keys =
        {
            (0, Night), (4.8f, Night), (5.9f, Dusk), (6.9f, Dawn), (9, Day), (12.5f, Noon), (16, Day),
            (18.4f, Eve), (19.6f, Dusk), (21.2f, Night), (24, Night),
        };

        public static float ShadowStrength = 0.85f, NightBoost;
        public static GameObject FireField, FireWall;
        static Color? FireColor;
        public static float SunScale = 0.5f, AmbientScale = 0.62f, FireIntensity = 2.2f, FireRange = 15f, FireUp = 0.9f, NightSun = 1f, FogStart = 90f, FogEnd = 230f, NightFogStart = -1f, NightFogEnd = -1f;
        static Color? NightFog;

        static float ShadowBias = -1f, NormalBias = -1f;

        public static void Use(ArenaCfg cfg)
        {
            ShadowBias = cfg.shadowBias;
            NormalBias = cfg.shadowNormalBias;
            FireIntensity = cfg.fireIntensity * 0.6f;
            FireColor = string.IsNullOrEmpty(cfg.fireColor) ? (Color?)null : ArenaCfg.Hex(cfg.fireColor);
            FireRange = cfg.fireRange;
            FireUp = cfg.fireUp;
            Electric = cfg.electric;
            SunScale = cfg.sunScale > 0f ? cfg.sunScale : 0.5f;
            AmbientScale = cfg.ambientScale > 0f ? cfg.ambientScale : 0.62f;
            ShadowStrength = cfg.shadowStrength > 0f ? cfg.shadowStrength : 0.85f;
            NightBoost = cfg.nightBoost;
            NightSun = cfg.nightSun > 0f ? cfg.nightSun : 1f;
            FogStart = cfg.fogStart > 0f ? cfg.fogStart : 90f;
            FogEnd = cfg.fogEnd > FogStart ? cfg.fogEnd : 230f;
            NightFogStart = cfg.nightFogStart;
            NightFogEnd = cfg.nightFogEnd > cfg.nightFogStart ? cfg.nightFogEnd : -1f;
            NightFog = !string.IsNullOrEmpty(cfg.nightFog) && int.TryParse(cfg.nightFog, System.Globalization.NumberStyles.HexNumber, null, out var nf) ? Hex(nf) : (Color?)null;
        }

        public static float GameHour() => (float)Kyiv.Now.TimeOfDay.TotalHours;
        public static bool IsNight(float hour) => hour >= 19.4f || hour < 6f;
        public static bool IsEvening(float hour) => hour >= 17.8f || hour < 6.4f;

        public float Hour { get; private set; }
        public float NightGrade { get; private set; }
        public Vector3 SunDir { get; private set; }
        public Color SunColor { get; private set; }
        Light _sun;
        static bool Electric;
        readonly List<Light> _fires = new List<Light>();
        readonly List<float> _phase = new List<float>();

        public void Apply(Transform root, float hour, Material flame)
        {
            Hour = hour;
            Shader.SetGlobalFloat("_ArenaClipY", root.position.y + 0.02f);
            Shader.SetGlobalFloat("_ArenaT0", Time.timeSinceLevelLoad);
            int i = 0;
            while (Keys[i + 1].h < hour) i++;
            var a = Keys[i].k; var b = Keys[i + 1].k;
            float span = Keys[i + 1].h - Keys[i].h, k = span > 0 ? (hour - Keys[i].h) / span : 0;
            float el = Mathf.Lerp(a.el, b.el, k), si = Mathf.Lerp(a.si, b.si, k), hi = Mathf.Lerp(a.hi, b.hi, k), exp = Mathf.Lerp(a.exp, b.exp, k);
            NightGrade = Mathf.Lerp(a.nk, b.nk, k);
            float amb = AmbientScale * (1f + NightBoost * NightGrade);
            Color sun = Color.Lerp(a.sun, b.sun, k), sky = Color.Lerp(a.sky, b.sky, k), gnd = Color.Lerp(a.gnd, b.gnd, k), fog = Color.Lerp(a.fog, b.fog, k);
            if (NightFog.HasValue) fog = Color.Lerp(fog, NightFog.Value, NightGrade);
            bool lit = hour >= 5f && hour < 20.6f;
            float baseHead = Mathf.Atan2(34f, -40f);
            float head = lit ? baseHead + (hour - 12.5f) / 7.5f * 1.15f : baseHead + 0.6f;
            float er = el * Mathf.Deg2Rad;
            var dirThree = new Vector3(Mathf.Cos(er) * Mathf.Cos(head), Mathf.Sin(er), Mathf.Cos(er) * Mathf.Sin(head));
            var dir = root.TransformDirection(new Vector3(dirThree.x, dirThree.y, -dirThree.z));

            if (_sun == null)
            {
                var go = new GameObject("ArenaSun");
                go.transform.SetParent(root, false);
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
                _sun.shadows = LightShadows.Soft;
                _sun.shadowBias = ShadowBias > 0f ? ShadowBias : 0.05f;
                _sun.shadowNormalBias = NormalBias > 0f ? NormalBias : 0.45f;
                _sun.renderMode = LightRenderMode.ForcePixel;
            }
            SunDir = dir;
            SunColor = sun;
            _sun.transform.rotation = Quaternion.LookRotation(-dir);
            _sun.color = sun;
            _sun.intensity = si * SunScale * exp * Mathf.Lerp(1f, NightSun, NightGrade);
            _sun.shadowStrength = ShadowStrength;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky * hi * amb * exp;
            RenderSettings.ambientEquatorColor = Color.Lerp(sky, gnd, 0.5f) * hi * amb * exp;
            RenderSettings.ambientGroundColor = gnd * hi * amb * exp;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = NightFogEnd > 0f ? Mathf.Lerp(FogStart, NightFogStart, NightGrade) : FogStart;
            RenderSettings.fogEndDistance = NightFogEnd > 0f ? Mathf.Lerp(FogEnd, NightFogEnd, NightGrade) : FogEnd;

            var skyDome = root.Find("Sky");
            var skyR = skyDome != null ? skyDome.GetComponent<Renderer>() : null;
            if (skyR != null && skyR.sharedMaterial != null)
            {
                var m = skyR.sharedMaterial;
                var zen = Color.Lerp(Color.Lerp(sky, new Color(0.22f, 0.4f, 0.78f), 0.75f), new Color(0.05f, 0.07f, 0.14f), NightGrade);
                m.SetColor("_Zenith", zen);
                m.SetColor("_Horizon", fog);
                m.SetVector("_SunDir", dir);
                m.SetColor("_SunCol", sun);
                m.SetFloat("_Night", NightGrade);
            }

            bool night = IsNight(hour);
            var nightGroup = root.Find("Night");
            if (nightGroup != null) nightGroup.gameObject.SetActive(night);
            var dayGroup = root.Find("Day");
            if (dayGroup != null) dayGroup.gameObject.SetActive(!night);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("_night", StringComparison.Ordinal)) t.gameObject.SetActive(night);
            var eveGroup = root.Find("Evening_night");
            if (eveGroup != null) eveGroup.gameObject.SetActive(IsEvening(hour));
            var fires = root.Find("Fires");
            if (fires != null)
            {
                fires.gameObject.SetActive(night);
                if (night && _fires.Count == 0)
                {
                    var spots = new List<Transform>();
                    foreach (Transform f in fires) spots.Add(f);
                    foreach (var f in spots) MakeFire(f, flame);
                }
            }
        }

        void MakeFire(Transform at, Material flame)
        {
            bool field = at.name.EndsWith("field");
            var pack = field ? FireField : FireWall;
            bool cards = at.childCount > 0;
            foreach (Transform c in at) if (c.name != "flame") cards = false;
            if (!Electric && pack != null && (at.childCount == 0 || cards))
            {
                foreach (Transform c in at) c.gameObject.SetActive(false);
                Spawn(at, pack, cards ? 1.5f : 1f, cards ? 0.08f : 0f);
            }
            else if (at.childCount == 0 && !Electric) Particles(at, flame);
            if (!field || FireIntensity <= 0f) return;
            var lg = new GameObject("fireLight");
            lg.transform.SetParent(at.parent, false);
            lg.transform.localPosition = at.localPosition + new Vector3(0f, FireUp, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = FireColor ?? (Electric ? new Color(1f, 0.78f, 0.5f) : new Color(1f, 0.6f, 0.28f));
            l.range = FireRange;
            l.intensity = FireIntensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            _fires.Add(l);
            _phase.Add(_fires.Count * 1.7f);
        }

        static void Spawn(Transform at, GameObject pack, float k, float drop)
        {
            var go = Instantiate(pack, at.parent, false);
            go.name = "flame";
            go.transform.localPosition = at.localPosition - new Vector3(0f, drop * at.localScale.x, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * (at.localScale.x * k);
        }

        static void Particles(Transform at, Material flame)
        {
            float s = at.localScale.x;
            var go = new GameObject("flame");
            go.transform.SetParent(at.parent, false);
            go.transform.localPosition = at.localPosition;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f; main.loop = true; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f * s, 0.9f * s);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f * s, 0.26f * s);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 60;
            main.startColor = Color.white;
            var em = ps.emission; em.rateOverTime = 50f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.06f * s;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new UnityEngine.Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.1f), 0.35f), new GradientColorKey(new Color(0.75f, 0.15f, 0.02f), 0.75f), new GradientColorKey(new Color(0.2f, 0.04f, 0f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.1f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = flame;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        void Update()
        {
            if (Electric) return;
            float t = Time.time * 1000f;
            for (int i = 0; i < _fires.Count; i++)
                if (_fires[i] != null)
                    _fires[i].intensity = FireIntensity * (1f + Mathf.Sin(t / 80f + _phase[i]) * 0.08f + Mathf.Sin(t / 29f + _phase[i] * 1.35f) * 0.05f);
        }
    }

    public sealed class ArenaGrade : MonoBehaviour
    {
        public Material Mat;
        public float Night;

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (Mat == null || Night <= 0.001f) { Graphics.Blit(src, dst); return; }
            Mat.SetFloat("_Night", Night);
            Graphics.Blit(src, dst, Mat);
        }
    }
}
