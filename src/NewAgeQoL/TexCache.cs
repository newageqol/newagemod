using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class TexCache
    {
        private const int Magic = 0x31544E51;
        private const int Version = 1;

        private sealed class Blob
        {
            internal int Width, Height, Format, Mips;
            internal byte[] Data;
        }

        private static string Dir => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "tex");

        internal static IEnumerator Load(string path, string name, bool linear, bool compress, Action<Texture2D> done)
        {
            if (!Source(path, name, out long size, out long stamp, out string raw)) { done(null); yield break; }

            var cached = Task.Run(() => Read(raw, size, stamp, linear, compress));
            while (!cached.IsCompleted) yield return null;
            var tex = cached.Status == TaskStatus.RanToCompletion && cached.Result != null ? Restore(cached.Result, name, linear) : null;
            if (tex != null) { done(tex); yield break; }

            var png = Task.Run(() => File.ReadAllBytes(path));
            while (!png.IsCompleted) yield return null;
            if (png.Status != TaskStatus.RanToCompletion) { done(null); yield break; }
            done(Decode(png.Result, name, linear, compress, raw, size, stamp));
        }

        private static bool Source(string path, string name, out long size, out long stamp, out string raw)
        {
            size = stamp = 0;
            raw = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return false;
                size = info.Length;
                stamp = info.LastWriteTimeUtc.Ticks;
                raw = Path.Combine(Dir, (Path.GetFileName(Path.GetDirectoryName(path)) ?? "") + "_" + Path.GetFileNameWithoutExtension(path) + ".raw");
                return true;
            }
            catch (Exception e) { Plugin.Caught("[tex] " + name + ": " + e.Message); return false; }
        }

        private static void Look(Texture2D tex)
        {
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
        }

        private static Texture2D Restore(Blob b, string name, bool linear)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(b.Width, b.Height, (TextureFormat)b.Format, b.Mips > 1, linear) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
                if (tex.mipmapCount != b.Mips) { UnityEngine.Object.Destroy(tex); return null; }
                var dst = tex.GetRawTextureData<byte>();
                if (dst.Length != b.Data.Length) { UnityEngine.Object.Destroy(tex); return null; }
                NativeArray<byte>.Copy(b.Data, dst, b.Data.Length);
                Look(tex);
                tex.Apply(false, true);
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Caught("[tex] restore " + name + ": " + e.Message);
                if (tex != null) UnityEngine.Object.Destroy(tex);
                return null;
            }
        }

        private static Texture2D Decode(byte[] png, string name, bool linear, bool compress, string raw, long size, long stamp)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (!tex.LoadImage(png, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            Look(tex);
            if (compress && tex.width % 4 == 0 && tex.height % 4 == 0)
            {
                try { tex.Compress(true); }
                catch (Exception e) { Plugin.Caught("[tex] compress " + name + ": " + e.Message); }
            }
            if (!compress) tex.Apply(true, false);
            Blob blob = null;
            try { blob = new Blob { Width = tex.width, Height = tex.height, Format = (int)tex.format, Mips = tex.mipmapCount, Data = tex.GetRawTextureData() }; }
            catch (Exception e) { Plugin.Caught("[tex] raw " + name + ": " + e.Message); }
            tex.Apply(false, true);
            if (blob != null && blob.Data != null && blob.Data.Length > 0)
                Task.Run(() => Write(raw, blob, size, stamp, linear, compress));
            return tex;
        }

        private static Blob Read(string raw, long size, long stamp, bool linear, bool compress)
        {
            if (!File.Exists(raw)) return null;
            using (var fs = new FileStream(raw, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var r = new BinaryReader(fs))
            {
                if (r.ReadInt32() != Magic || r.ReadInt32() != Version) return null;
                if (r.ReadInt64() != size || r.ReadInt64() != stamp) return null;
                if (r.ReadBoolean() != linear || r.ReadBoolean() != compress) return null;
                var b = new Blob { Width = r.ReadInt32(), Height = r.ReadInt32(), Format = r.ReadInt32(), Mips = r.ReadInt32() };
                int length = r.ReadInt32();
                if (b.Width <= 0 || b.Height <= 0 || b.Mips <= 0 || length <= 0 || length != fs.Length - fs.Position) return null;
                b.Data = r.ReadBytes(length);
                return b.Data.Length == length ? b : null;
            }
        }

        private static void Write(string raw, Blob b, long size, long stamp, bool linear, bool compress)
        {
            string tmp = raw + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(raw));
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                using (var w = new BinaryWriter(fs))
                {
                    w.Write(Magic);
                    w.Write(Version);
                    w.Write(size);
                    w.Write(stamp);
                    w.Write(linear);
                    w.Write(compress);
                    w.Write(b.Width);
                    w.Write(b.Height);
                    w.Write(b.Format);
                    w.Write(b.Mips);
                    w.Write(b.Data.Length);
                    w.Write(b.Data);
                }
                if (File.Exists(raw)) File.Delete(raw);
                File.Move(tmp, raw);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); }
                catch { }
            }
        }
    }
}
