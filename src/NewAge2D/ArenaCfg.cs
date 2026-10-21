using System;
using UnityEngine;

namespace NewAge2D
{
    [Serializable]
    public sealed class ArenaHeight
    {
        public float x0, z0, step = 1f;
        public int nx, nz;
        public float[] v;

        public float At(float x, float z)
        {
            if (v == null || nx < 2 || nz < 2) return 0f;
            float fx = Mathf.Clamp((x - x0) / step, 0f, nx - 1.001f), fz = Mathf.Clamp((z - z0) / step, 0f, nz - 1.001f);
            int i = (int)fx, j = (int)fz;
            float a = fx - i, b = fz - j;
            float h0 = Mathf.Lerp(v[j * nx + i], v[j * nx + i + 1], a);
            float h1 = Mathf.Lerp(v[(j + 1) * nx + i], v[(j + 1) * nx + i + 1], a);
            return Mathf.Lerp(h0, h1, b);
        }
    }

    [Serializable]
    public sealed class ArenaCfg
    {
        public float[] center = { 13f, 15.6f };
        public float[] radii = { 48f, 37f };
        public float[] pan;
        public float zMin = -999f;
        public float[] home = { 12.6f, 0f, 15.3f };
        public float[] cells = { 0f, 25.63f, 13.2f, 18f };
        public float standsTop = 29.6f;
        public ArenaHeight height;
        public float fireIntensity = 2.2f, fireRange = 15f, fireUp = 0.9f;
        public float pitchMin = -1f, nightSun = 1f, fogStart = 90f, fogEnd = 230f, nightFogStart = -1f, nightFogEnd = -1f;
        public float sunScale = -1f, ambientScale = -1f, shadowStrength = -1f, nightBoost = 0f, maxDist = 75f, homePitch = -1f, homeDist = -1f, camTop = 999f, openRise = 4.5f, openDrop = 2f, wallGap = 2.5f, camAbove = -999f, pitchMax = 1.35f;
        public bool electric;
        public bool storm;
        public string bgDay = "000000", bgNight = "000000", nightFog = "";
        public float[] motes = { 13f, 2.5f, 15.6f, 47f, 9f, 26f };

        public static ArenaCfg Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return new ArenaCfg();
            try { return JsonUtility.FromJson<ArenaCfg>(json) ?? new ArenaCfg(); }
            catch { return new ArenaCfg(); }
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.black;
    }
}
