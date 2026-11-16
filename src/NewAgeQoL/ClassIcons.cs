using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class ClassIcons
    {
        private const int First = 1;
        private const int Last = 6;
        private static readonly Dictionary<int, Sprite> Own = new Dictionary<int, Sprite>();

        internal static void Wake()
        {
            if (Own.Count >= Last - First + 1) return;
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                int made = 0;
                for (int id = First; id <= Last; id++)
                    if (!Own.ContainsKey(id) && Copy(id) != null) made++;
                if (made > 0) Plugin.Trace("[class icons] copied " + made + " class icons while the scene loads, " + watch.ElapsedMilliseconds + " ms");
            }
            catch (Exception e) { Plugin.Caught("[class icons] " + e.Message); }
        }

        internal static Sprite Get(int classId)
        {
            Sprite art;
            if (Own.TryGetValue(classId, out art) && art != null) return art;
            try { return Copy(classId); }
            catch { return null; }
        }

        private static Sprite Copy(int classId)
        {
            Sprite game = AtlasUtils.GetSmallClassIcon((ERPGClass)classId);
            if (game == null || game.texture == null) return null;
            Rect box = game.textureRect;
            var source = game.texture;
            int w = Mathf.Max(1, Mathf.RoundToInt(box.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(box.height));
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var was = RenderTexture.active;
            Texture2D copy;
            try
            {
                Graphics.Blit(source, rt, new Vector2(box.width / source.width, box.height / source.height), new Vector2(box.x / source.width, box.y / source.height));
                RenderTexture.active = rt;
                copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply(false, true);
            }
            finally
            {
                RenderTexture.active = was;
                RenderTexture.ReleaseTemporary(rt);
            }
            copy.name = "QoLClassIcon" + classId;
            copy.filterMode = source.filterMode;
            copy.wrapMode = TextureWrapMode.Clamp;
            var pivot = new Vector2(game.pivot.x / Mathf.Max(1f, game.rect.width), game.pivot.y / Mathf.Max(1f, game.rect.height));
            var art = Sprite.Create(copy, new Rect(0, 0, w, h), pivot, game.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            art.name = game.name;
            copy.hideFlags = HideFlags.DontUnloadUnusedAsset;
            art.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Own[classId] = art;
            return art;
        }
    }
}
