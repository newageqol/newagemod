using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch]
    internal static class BundleTwins
    {
        private static readonly AccessTools.FieldRef<FileAssetBundle, string> PathOf =
            AccessTools.FieldRefAccess<FileAssetBundle, string>("assetBundlePath");
        private static readonly MethodInfo Held = AccessTools.PropertyGetter(typeof(BaseAssetBundleImpl), "assetBundle");
        private static readonly List<FileAssetBundle> Leaving = new List<FileAssetBundle>();

        [HarmonyPrefix, HarmonyPatch(typeof(AssetLoader), "UnloadAssetBundle")]
        private static void Doom(IAssetBundle assetBundle)
        {
            if (!(assetBundle is FileAssetBundle file)) return;
            Leaving.RemoveAll(b => b == null || !Loaded(b));
            Leaving.Add(file);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(FileAssetBundle), nameof(FileAssetBundle.LoadAssetBundleAsync))]
        private static void BeforeAsync(FileAssetBundle __instance) => Free(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(FileAssetBundle), nameof(FileAssetBundle.LoadAssetBundle))]
        private static void BeforeSync(FileAssetBundle __instance) => Free(__instance);

        private static void Free(FileAssetBundle fresh)
        {
            try
            {
                string path = PathOf(fresh);
                for (int i = Leaving.Count - 1; i >= 0; i--)
                {
                    var old = Leaving[i];
                    if (old == fresh || !string.Equals(PathOf(old), path, StringComparison.OrdinalIgnoreCase)) continue;
                    Leaving.RemoveAt(i);
                    if (!Loaded(old)) continue;
                    old.Unload(false);
                    Plugin.Log?.LogInfo("[загрузка] " + System.IO.Path.GetFileName(path) + " попросили снова до выгрузки прошлой копии — выгрузил её сразу");
                }
            }
            catch (Exception e) { Plugin.Warn("[загрузка] повторная модель: " + e.Message); }
        }

        private static bool Loaded(FileAssetBundle b) => Held != null && (Held.Invoke(b, null) as UnityEngine.Object) != null;
    }

    [HarmonyPatch]
    internal static class LoaderGuard
    {
        private static readonly FieldInfo Current = AccessTools.Field(typeof(AssetLoader), "_current");
        private static readonly MethodInfo Empty = AccessTools.Method(typeof(AssetLoader), "IsEmpty");
        private static readonly MethodInfo Next = AccessTools.Method(typeof(AssetLoader), "LoadNext");

        [HarmonyPostfix, HarmonyPatch(typeof(AssetLoader), "LoadNext")]
        private static void Queue(AssetLoader __instance, ref IEnumerator __result) => __result = Run(__result, "очередь", __instance);

        [HarmonyPostfix, HarmonyPatch(typeof(AssetLoader), "HandleAssetBundle")]
        private static void Handle(ref IEnumerator __result) => __result = Run(__result, "разбор", null);

        [HarmonyPostfix, HarmonyPatch(typeof(AssetData), "LoadAssetBundleData", new Type[] { })]
        private static void Whole(ref IEnumerator __result) => __result = Run(__result, "модель", null);

        [HarmonyPostfix, HarmonyPatch(typeof(AssetData), "LoadAssetBundleData", new[] { typeof(string) })]
        private static void Part(ref IEnumerator __result) => __result = Run(__result, "часть модели", null);

        private static IEnumerator Run(IEnumerator inner, string what, AssetLoader loader)
        {
            while (true)
            {
                object step = null;
                bool more = false, failed = false;
                try
                {
                    more = inner.MoveNext();
                    if (more) step = inner.Current;
                }
                catch (Exception e)
                {
                    failed = true;
                    Plugin.Warn("[загрузка] " + what + " упала, без мода игра бы встала на вечной загрузке: " + e.GetType().Name + ": " + e.Message);
                }
                if (failed)
                {
                    if (loader != null) Revive(loader);
                    yield break;
                }
                if (!more) yield break;
                yield return step;
            }
        }

        private static void Revive(AssetLoader loader)
        {
            try
            {
                if (Current?.GetValue(loader) is AssetLoadResults stuck && stuck.keepWaiting)
                {
                    try { stuck.FireAssetsLoadedEvent(); }
                    catch (Exception e) { Plugin.Warn("[загрузка] ждавшие модели: " + e.Message); }
                }
                if (Empty != null && Next != null && !(bool)Empty.Invoke(loader, null))
                    loader.StartCoroutine((IEnumerator)Next.Invoke(loader, null));
                else Current?.SetValue(loader, null);
            }
            catch (Exception e) { Plugin.Warn("[загрузка] очередь не поднялась: " + e.Message); }
        }
    }
}
