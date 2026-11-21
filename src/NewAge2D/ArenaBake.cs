using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace NewAge2D;

internal sealed class ArenaBake
{
    [Serializable]
    private sealed class Data
    {
        public string preset = "";
        public string[] color = Array.Empty<string>(), dir = Array.Empty<string>(), mask = Array.Empty<string>();
        public int sun = -1;
        public bool directional;
        public float[] rs = Array.Empty<float>(), sh = Array.Empty<float>();
    }

    private readonly Data _data;
    private readonly LightmapData[] _maps;
    private readonly AssetBundle _bundle;

    private ArenaBake(Data data, LightmapData[] maps, AssetBundle bundle)
    {
        _data = data;
        _maps = maps;
        _bundle = bundle;
    }

    internal string Name => _data.preset;

    private static bool Live(string name) => name.StartsWith("fieldlight") || name.StartsWith("filllight") || name == "NewAge2D.FieldGlow";

    private static Texture2D _flat;

    private static Texture2D Flat()
    {
        if (_flat != null) return _flat;
        _flat = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "NewAge2D.FlatDir", hideFlags = HideFlags.DontUnloadUnusedAsset };
        _flat.SetPixel(0, 0, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        _flat.Apply(false, true);
        return _flat;
    }

    internal static string Preset(float hour) => ArenaLook.IsNight(hour) ? "night" : hour >= 17.8f || hour < 7f ? "dusk" : "day";

    internal static float Hour(string preset) => preset == "night" ? 1f : preset == "dusk" ? 18.6f : 13f;

    internal static string File(string dir, string bundle, string preset) => Path.Combine(dir, bundle + "_" + preset);

    internal void Unload()
    {
        if (_bundle != null) _bundle.Unload(true);
    }

    internal static IEnumerator Load(string path, Action<ArenaBake> done)
    {
        if (!System.IO.File.Exists(path)) { done(null); yield break; }
        var req = AssetBundle.LoadFromFileAsync(path);
        yield return req;
        var bundle = req.assetBundle;
        if (bundle == null) { done(null); yield break; }
        var all = bundle.LoadAllAssetsAsync();
        yield return all;
        Data data = null;
        var textures = new Dictionary<string, Texture2D>();
        foreach (var asset in all.allAssets)
        {
            if (asset is TextAsset text) data = JsonUtility.FromJson<Data>(text.text);
            else if (asset is Texture2D tex) textures[tex.name] = tex;
        }
        if (data == null || data.color.Length == 0)
        {
            bundle.Unload(true);
            done(null);
            yield break;
        }
        Texture2D Get(string[] names, int i) => i < names.Length && !string.IsNullOrEmpty(names[i]) && textures.TryGetValue(names[i], out var t) ? t : null;
        var maps = new LightmapData[data.color.Length];
        for (int i = 0; i < maps.Length; i++)
            maps[i] = new LightmapData { lightmapColor = Get(data.color, i), lightmapDir = Get(data.dir, i), shadowMask = Get(data.mask, i) };
        done(new ArenaBake(data, maps, bundle));
    }

    internal sealed class Applied
    {
        public LightmapData[] Ours;
        public LightmapData[] Was;
        public LightmapsMode Mode;
        public ShadowmaskMode Mask;
    }

    internal Applied Apply(Renderer[] views, Transform root)
    {
        var was = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
        int offset = was.Length;
        var all = new LightmapData[offset + _maps.Length];
        Array.Copy(was, all, offset);
        int flat = 0;
        if (_data.directional)
            for (int i = 0; i < offset; i++)
            {
                var w = was[i];
                if (w == null || w.lightmapColor == null || w.lightmapDir != null) continue;
                all[i] = new LightmapData { lightmapColor = w.lightmapColor, lightmapDir = Flat(), shadowMask = w.shadowMask };
                flat++;
            }
        Array.Copy(_maps, 0, all, offset, _maps.Length);
        var applied = new Applied { Ours = _maps, Was = was, Mode = LightmapSettings.lightmapsMode, Mask = QualitySettings.shadowmaskMode };
        LightmapSettings.lightmaps = all;
        LightmapSettings.lightmapsMode = _data.directional ? LightmapsMode.CombinedDirectional : LightmapsMode.NonDirectional;
        bool mask = _data.sun >= 0;
        if (mask) QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
        int lit = 0;
        for (int k = 0; k + 5 < _data.rs.Length; k += 6)
        {
            int i = (int)_data.rs[k];
            if (i < 0 || i >= views.Length || views[i] == null) continue;
            var view = views[i];
            view.lightmapIndex = offset + (int)_data.rs[k + 1];
            view.lightmapScaleOffset = new Vector4(_data.rs[k + 2], _data.rs[k + 3], _data.rs[k + 4], _data.rs[k + 5]);
            if (mask) view.shadowCastingMode = ShadowCastingMode.Off;
            lit++;
        }
        var block = new MaterialPropertyBlock();
        var sh = new SphericalHarmonicsL2[1];
        int probed = 0;
        for (int k = 0; k + 27 < _data.sh.Length; k += 28)
        {
            int i = (int)_data.sh[k];
            if (i < 0 || i >= views.Length || views[i] == null) continue;
            var p = new SphericalHarmonicsL2();
            for (int c = 0; c < 3; c++)
                for (int n = 0; n < 9; n++)
                    p[c, n] = _data.sh[k + 1 + c * 9 + n];
            sh[0] = p;
            var view = views[i];
            view.lightProbeUsage = LightProbeUsage.CustomProvided;
            view.GetPropertyBlock(block);
            block.CopySHCoefficientArraysFrom(sh);
            view.SetPropertyBlock(block);
            probed++;
        }
        int off = 0;
        Light sun = null;
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (light.name == "ArenaSun") { sun = light; continue; }
            if (!light.enabled || Live(light.name)) continue;
            light.enabled = false;
            off++;
        }
        if (sun != null && mask)
            sun.bakingOutput = new LightBakingOutput
            {
                isBaked = true,
                lightmapBakeType = LightmapBakeType.Mixed,
                mixedLightingMode = MixedLightingMode.Shadowmask,
                occlusionMaskChannel = _data.sun,
                probeOcclusionLightIndex = -1,
            };
        Plugin.Log.LogInfo($"[arena3d] baked light {_data.preset}: lightmaps {_maps.Length}, lit {lit}, probed {probed}, lamps off {off}, sun mask {_data.sun}, game maps {offset}, flat dir {flat}");
        return applied;
    }

    internal static void Undo(Applied applied)
    {
        if (applied == null) return;
        var now = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
        var ours = new HashSet<Texture2D>(applied.Ours.Where(m => m.lightmapColor != null).Select(m => m.lightmapColor));
        int keep = now.Length;
        while (keep > 0 && now[keep - 1] != null && now[keep - 1].lightmapColor != null && ours.Contains(now[keep - 1].lightmapColor)) keep--;
        if (keep != now.Length)
        {
            LightmapSettings.lightmaps = applied.Was != null && keep == applied.Was.Length ? applied.Was : now.Take(keep).ToArray();
            LightmapSettings.lightmapsMode = applied.Mode;
        }
        QualitySettings.shadowmaskMode = applied.Mask;
    }
}
