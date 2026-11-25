using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SpriteManager), nameof(SpriteManager.GetSpriteByName), new[] { typeof(string), typeof(CustomSpriteInfo) })]
    internal static class SpriteCache
    {
        private sealed class Made
        {
            internal string Name;
            internal Sprite Sprite;
            internal bool Mine;
        }

        private static readonly Dictionary<CustomSpriteInfo, Made> Sprites = new Dictionary<CustomSpriteInfo, Made>();
        private static readonly Dictionary<string, Vector2Int> Extents = new Dictionary<string, Vector2Int>();
        private static readonly HashSet<string> Told = new HashSet<string>();
        private static readonly FieldInfo Atlases = AccessTools.Field(typeof(SpriteManager), "_loadedAtlases");
        private static readonly Type Dynamic = AccessTools.Inner(typeof(SpriteManager), "DynamicAtlasInfo");
        private static readonly MethodInfo LoadDynamic = Dynamic == null ? null : AccessTools.Method(Dynamic, "Load");
        private static readonly FieldInfo Picture = Dynamic == null ? null : AccessTools.Field(Dynamic, "_dynamicAtlasTexture");
        private static readonly FieldInfo Database = AccessTools.Field(typeof(AtlasesDatabase), "Database");

        private static Sprite _fresh;

        private static bool Prefix(SpriteManager __instance, string spriteName, CustomSpriteInfo customSpriteInfo, ref Sprite __result, out bool __state)
        {
            __state = false;
            _fresh = null;
            if (spriteName == null || customSpriteInfo == null) return true;
            Heal(__instance, customSpriteInfo);
            if (Sprites.TryGetValue(customSpriteInfo, out var made) && made.Name == spriteName && made.Sprite != null && made.Sprite.texture != null)
            {
                __result = made.Sprite;
                return false;
            }
            __state = true;
            var rescaled = Rescaled(__instance, customSpriteInfo);
            if (rescaled == null) return true;
            _fresh = rescaled;
            __result = rescaled;
            return false;
        }

        private static void Postfix(string spriteName, CustomSpriteInfo customSpriteInfo, Sprite __result, bool __state)
        {
            if (!__state || __result == null) { _fresh = null; return; }
            bool mine = ReferenceEquals(__result, _fresh);
            _fresh = null;
            if (Sprites.TryGetValue(customSpriteInfo, out var had) && !ReferenceEquals(had.Sprite, __result)) Drop(had);
            Sprites[customSpriteInfo] = new Made { Name = spriteName, Sprite = __result, Mine = mine };
        }

        private static void Drop(Made made)
        {
            if (made == null || !made.Mine || made.Sprite == null) return;
            UnityEngine.Object.Destroy(made.Sprite);
        }

        private static HashSet<string> _things;

        private static HashSet<string> ThingAtlases()
        {
            if (_things != null) return _things;
            var names = new HashSet<string>();
            if (Database != null && Database.GetValue(AtlasesDatabase.Instance) is IDictionary all)
                foreach (var value in all.Values)
                    if (value is CustomSpriteInfo info && info.AtlasType == (int)EAtlasType.THINGS && info.AtlasName != null)
                        names.Add(info.AtlasName);
            if (names.Count > 0) _things = names;
            return names;
        }

        internal static bool Keep(SpriteManager manager)
        {
            if (Atlases == null || !(Atlases.GetValue(manager) is IDictionary loaded)) return false;
            var things = ThingAtlases();
            if (things.Count == 0) return false;
            var gone = new List<object>();
            int kept = 0;
            foreach (DictionaryEntry pair in loaded)
            {
                if (pair.Key is string name && things.Contains(name) && Alive(pair.Value)) { kept++; continue; }
                gone.Add(pair.Key);
            }
            foreach (var key in gone)
            {
                try { (loaded[key] as IDisposable)?.Dispose(); }
                catch (Exception e) { Plugin.Caught("[icons] atlas unload " + key + ": " + e.Message); }
                loaded.Remove(key);
            }
            if (kept > 0) Plugin.Trace("[icons] scene change: kept " + kept + " thing atlases, unloaded " + gone.Count);
            return true;
        }

        private static bool Alive(object bundle)
        {
            if (bundle == null || Picture == null || Dynamic == null || !Dynamic.IsInstanceOfType(bundle)) return false;
            var texture = Picture.GetValue(bundle) as Texture2D;
            return texture != null;
        }

        private static void Heal(SpriteManager manager, CustomSpriteInfo info)
        {
            if (info.AtlasType != (int)EAtlasType.THINGS || Atlases == null) return;
            if (!(Atlases.GetValue(manager) is IDictionary loaded)) return;
            string atlas = info.AtlasName;
            if (atlas == null || !loaded.Contains(atlas) || Alive(loaded[atlas])) return;
            try { (loaded[atlas] as IDisposable)?.Dispose(); }
            catch { }
            loaded.Remove(atlas);
            if (Sprites.TryGetValue(info, out var had)) { Drop(had); Sprites.Remove(info); }
            Plugin.Trace("[icons] kept atlas " + atlas + " lost its texture, loading again");
        }

        internal static void Forget()
        {
            foreach (var made in Sprites.Values) Drop(made);
            Sprites.Clear();
        }

        private static Sprite Rescaled(SpriteManager manager, CustomSpriteInfo info)
        {
            string atlas = null;
            try
            {
                atlas = info.AtlasName;
                if (atlas == null || !Extent(atlas, out var extent)) return null;
                var texture = Texture(manager, info, atlas);
                if (texture == null || (extent.x <= texture.width && extent.y <= texture.height)) return null;
                int wide = Mathf.NextPowerOfTwo(extent.x);
                int high = Mathf.NextPowerOfTwo(extent.y);
                float sx = (float)texture.width / wide;
                float sy = (float)texture.height / high;
                var rect = new Rect(info.X * sx, info.Y * sy, info.Width * sx, info.Height * sy);
                if (rect.xMin < 0f || rect.yMin < 0f || rect.xMax > texture.width || rect.yMax > texture.height) return null;
                if (Told.Add(atlas))
                    Plugin.Log?.LogInfo("[icons] atlas " + atlas + " is in the game at size " + texture.width + "x" + texture.height +
                                        ", but database coordinates assume " + wide + "x" + high + ": images from it are rescaled");
                return Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f * sx, 0u, SpriteMeshType.FullRect);
            }
            catch (Exception e)
            {
                if (atlas != null && Told.Add(atlas)) Plugin.Log?.LogWarning("[icons] atlas rescale " + atlas + ": " + e.Message);
                return null;
            }
        }

        private static bool Extent(string atlas, out Vector2Int extent)
        {
            if (Extents.Count == 0) Measure();
            return Extents.TryGetValue(atlas, out extent);
        }

        private static void Measure()
        {
            if (Database == null || !(Database.GetValue(AtlasesDatabase.Instance) is IDictionary all)) return;
            foreach (var value in all.Values)
            {
                if (!(value is CustomSpriteInfo info)) continue;
                string atlas = info.AtlasName;
                if (atlas == null) continue;
                Extents.TryGetValue(atlas, out var known);
                Extents[atlas] = new Vector2Int(Mathf.Max(known.x, info.X + info.Width), Mathf.Max(known.y, info.Y + info.Height));
            }
        }

        private static Texture2D Texture(SpriteManager manager, CustomSpriteInfo info, string atlas)
        {
            if (Atlases == null || LoadDynamic == null || Picture == null) return null;
            if (!(Atlases.GetValue(manager) is IDictionary loaded)) return null;
            object bundle;
            if (loaded.Contains(atlas)) bundle = loaded[atlas];
            else
            {
                bundle = LoadDynamic.Invoke(null, new object[] { manager.Reader, info });
                loaded.Add(atlas, bundle);
            }
            return bundle != null && Dynamic.IsInstanceOfType(bundle) ? Picture.GetValue(bundle) as Texture2D : null;
        }
    }

    [HarmonyPatch(typeof(SpriteManager), nameof(SpriteManager.Clear))]
    internal static class SpriteCacheClear
    {
        private static bool Prefix(SpriteManager __instance)
        {
            try { return !SpriteCache.Keep(__instance); }
            catch (Exception e) { Plugin.Caught("[icons] keep thing atlases: " + e.Message); return true; }
        }

        private static void Postfix() => SpriteCache.Forget();
    }
}
