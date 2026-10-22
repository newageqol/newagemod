using UnityEngine;

namespace NewAge2D
{
    public sealed class ArenaPost : MonoBehaviour
    {
        struct Key
        {
            public float h, sat, con, vig;
            public Color mul;
            public Key(float h, float r, float g, float b, float sat, float con, float vig)
            {
                this.h = h; this.sat = sat; this.con = con; this.vig = vig;
                mul = new Color(r, g, b, 1f);
            }
        }

        static readonly Key[] Keys =
        {
            new Key(0f, 1.06f, 1.04f, 1.08f, 1.05f, 1.08f, 0.14f),
            new Key(4.8f, 1.06f, 1.04f, 1.08f, 1.05f, 1.08f, 0.14f),
            new Key(5.9f, 1.04f, 0.99f, 1.04f, 1.04f, 1.05f, 0.12f),
            new Key(6.9f, 1.04f, 1f, 0.96f, 1.06f, 1.04f, 0.1f),
            new Key(9f, 1f, 1f, 0.99f, 1.04f, 1.03f, 0.08f),
            new Key(12.5f, 1f, 1f, 1f, 1.03f, 1.03f, 0.08f),
            new Key(16f, 1f, 1f, 0.99f, 1.04f, 1.03f, 0.08f),
            new Key(18.4f, 1.05f, 0.99f, 0.93f, 1.06f, 1.04f, 0.12f),
            new Key(19.6f, 1.04f, 0.99f, 1.04f, 1.04f, 1.05f, 0.12f),
            new Key(21.2f, 1.06f, 1.04f, 1.08f, 1.05f, 1.08f, 0.14f),
            new Key(24f, 1.06f, 1.04f, 1.08f, 1.05f, 1.08f, 0.14f),
        };

        static readonly int IdBloom = Shader.PropertyToID("_BloomTex");

        Material _mat;
        readonly RenderTexture[] _mips = new RenderTexture[6];

        public void Setup(Material mat, float hour, float night, float lift)
        {
            _mat = mat;
            hour = (hour % 24f + 24f) % 24f;
            int i = 0;
            while (i < Keys.Length - 2 && Keys[i + 1].h < hour) i++;
            var a = Keys[i]; var b = Keys[i + 1];
            float k = Mathf.InverseLerp(a.h, b.h, hour);
            mat.SetColor("_GradeMul", Color.Lerp(a.mul, b.mul, k) * Mathf.Lerp(1f, lift, night));
            mat.SetFloat("_Sat", Mathf.Lerp(a.sat, b.sat, k));
            mat.SetFloat("_Con", Mathf.Lerp(a.con, b.con, k));
            mat.SetFloat("_Vig", Mathf.Lerp(a.vig, b.vig, k));
            mat.SetFloat("_Night", night);
            mat.SetFloat("_Grain", 0.05f * night);
            mat.SetFloat("_BloomInt", Mathf.Lerp(0.18f, 0.35f, night));
            mat.SetFloat("_Threshold", 2f);
            mat.SetFloat("_Knee", 0.6f);
            mat.SetFloat("_Sharp", 0.25f);
            mat.SetFloat("_Lut", 0f);
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (_mat == null) { Graphics.Blit(src, dst); return; }
            var fmt = src.format;
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            int n = 0;
            _mips[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, _mips[n], _mat, 0);
            n++;
            while (n < _mips.Length && w > 48 && h > 48)
            {
                w /= 2; h /= 2;
                _mips[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(_mips[n - 1], _mips[n], _mat, 1);
                n++;
            }
            for (int i = n - 1; i > 0; i--) Graphics.Blit(_mips[i], _mips[i - 1], _mat, 2);
            _mat.SetTexture(IdBloom, _mips[0]);
            var ldr = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.Default);
            Graphics.Blit(src, ldr, _mat, 3);
            Graphics.Blit(ldr, dst, _mat, 4);
            RenderTexture.ReleaseTemporary(ldr);
            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(_mips[i]); _mips[i] = null; }
        }
    }
}
