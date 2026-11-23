using System;
using System.IO;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Baked
    {
        private static string _build;

        internal static string Build => _build ?? (_build = typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString("N"));

        private static string Dir => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "baked");

        internal static Texture2D Get(string key, string stamp, Func<Texture2D> make)
        {
            var file = Path.Combine(Dir, key + ".raw");
            var tex = Read(file, stamp);
            if (tex != null) return tex;
            tex = make();
            if (tex != null) Write(file, stamp, tex);
            return tex;
        }

        private static Texture2D Read(string file, string stamp)
        {
            try
            {
                if (!File.Exists(file)) return null;
                using (var r = new BinaryReader(File.OpenRead(file)))
                {
                    if (r.ReadString() != stamp) return null;
                    int w = r.ReadInt32(), h = r.ReadInt32(), n = r.ReadInt32();
                    if (w <= 0 || h <= 0 || n != w * h * 4) return null;
                    var data = r.ReadBytes(n);
                    if (data.Length != n) return null;
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    tex.LoadRawTextureData(data);
                    tex.Apply(false, false);
                    return tex;
                }
            }
            catch (Exception e)
            {
                Plugin.Caught("[baked] read " + Path.GetFileName(file) + ": " + e.Message);
                return null;
            }
        }

        private static void Write(string file, string stamp, Texture2D tex)
        {
            string tmp = file + ".tmp";
            try
            {
                if (tex.format != TextureFormat.RGBA32 || tex.mipmapCount != 1 || !tex.isReadable) return;
                var data = tex.GetRawTextureData();
                if (data.Length != tex.width * tex.height * 4) return;
                Directory.CreateDirectory(Dir);
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(stamp);
                    w.Write(tex.width);
                    w.Write(tex.height);
                    w.Write(data.Length);
                    w.Write(data);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(tmp, file);
            }
            catch (Exception e)
            {
                Plugin.Caught("[baked] write " + Path.GetFileName(file) + ": " + e.Message);
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
            }
        }
    }
}
