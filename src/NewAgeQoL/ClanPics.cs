using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Transport.Messages.Responses.Clan;
using Transport.Messages.Responses.Newmap;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ClanPics
    {
        private const string Site = "https://files.nura.biz/site/images/clans/bigicons/";
        private const float Again = 600f;

        private sealed class Wait
        {
            internal string Icon;
            internal bool Mark;
        }

        private static readonly Dictionary<string, Sprite> Got = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Busy = new HashSet<string>();
        private static readonly Dictionary<string, float> Failed = new Dictionary<string, float>();
        private static readonly Dictionary<Image, Wait> Want = new Dictionary<Image, Wait>();

        private static string Folder => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "clans");

        internal static bool Lost(Sprite sprite) => sprite == null || sprite.name == "unknown" || sprite.texture == null;

        internal static bool Missing(string icon)
        {
            float at;
            return string.IsNullOrEmpty(icon) || Failed.TryGetValue(icon, out at) && Time.unscaledTime - at < Again;
        }

        internal static void Drop(Image image)
        {
            Wait was;
            if (image == null || !Want.TryGetValue(image, out was)) return;
            Want.Remove(image);
            if (was.Mark) image.color = Color.white;
        }

        internal static void Fill(Image image, string icon, bool mark = false)
        {
            if (image == null || string.IsNullOrEmpty(icon)) return;
            if (!Lost(image.sprite)) { Want.Remove(image); return; }
            Sprite have;
            if (Got.TryGetValue(icon, out have) && !Lost(have)) { Put(image, have); return; }
            float at;
            if (Failed.TryGetValue(icon, out at) && Time.unscaledTime - at < Again)
            {
                if (mark) Blank(image);
                return;
            }
            if (mark) image.color = new Color(1f, 1f, 1f, 0f);
            Want[image] = new Wait { Icon = icon, Mark = mark };
            if (Busy.Contains(icon) || Plugin.Instance == null) return;
            Busy.Add(icon);
            Plugin.Instance.StartCoroutine(Load(icon));
        }

        private static void Put(Image image, Sprite sprite)
        {
            Want.Remove(image);
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            image.enabled = true;
            if (!image.gameObject.activeSelf) image.gameObject.SetActive(true);
        }

        private static void Blank(Image image)
        {
            image.sprite = null;
            image.color = Color.white;
        }

        private static IEnumerator Load(string icon)
        {
            byte[] data = Disk(icon);
            bool fresh = data == null;
            if (fresh)
            {
                var req = UnityWebRequest.Get(Site + Uri.EscapeDataString(icon) + ".gif");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.responseCode == 200 && req.downloadHandler != null) data = req.downloadHandler.data;
                else Plugin.Trace("[клан] значка «" + icon + "» нет и на сайте: " + (req.error ?? "код " + req.responseCode));
                req.Dispose();
            }
            Busy.Remove(icon);
            Sprite sprite = null;
            try
            {
                var texture = Gif.Read(data);
                if (texture != null)
                {
                    texture.name = "clan_" + icon;
                    sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                    sprite.name = icon;
                }
            }
            catch (Exception e) { Plugin.Trace("[клан] значок «" + icon + "» не разобрался: " + e.Message); }
            if (sprite == null)
            {
                Failed[icon] = Time.unscaledTime;
                Settle(icon, null);
                yield break;
            }
            if (fresh) Save(icon, data);
            Got[icon] = sprite;
            Plugin.Trace("[клан] значок «" + icon + "» взят " + (fresh ? "с сайта" : "из кэша") + ": " + sprite.texture.width + "x" + sprite.texture.height);
            Settle(icon, sprite);
        }

        private static void Settle(string icon, Sprite sprite)
        {
            var done = new List<Image>();
            var lit = new List<Image>();
            var blank = new List<Image>();
            foreach (var pair in Want)
            {
                if (pair.Key == null) { done.Add(pair.Key); continue; }
                if (pair.Value.Icon != icon) continue;
                done.Add(pair.Key);
                if (!Lost(pair.Key.sprite)) continue;
                if (sprite != null) lit.Add(pair.Key);
                else if (pair.Value.Mark) blank.Add(pair.Key);
            }
            foreach (var image in done) Want.Remove(image);
            foreach (var image in lit) Put(image, sprite);
            foreach (var image in blank) Blank(image);
        }

        private static string PathOf(string icon)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) icon = icon.Replace(c, '_');
            return Path.Combine(Folder, icon + ".gif");
        }

        private static byte[] Disk(string icon)
        {
            try
            {
                string path = PathOf(icon);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch { return null; }
        }

        private static void Save(string icon, byte[] data)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllBytes(PathOf(icon), data);
            }
            catch (Exception e) { Plugin.Trace("[клан] значок «" + icon + "» не сохранился: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GeneralUserInfoPanelContent), "Init")]
    internal static class ClanPicsCardPatch
    {
        private static void Postfix(GeneralUserInfoPanelContent __instance, Unity3DUserInfoResponseMessage message)
        {
            try
            {
                var image = AccessTools.Field(typeof(GeneralUserInfoPanelContent), "ClanIconImage")?.GetValue(__instance) as Image;
                ClanPics.Drop(image);
                if (message == null || message.ClanName == null || message.ClanIcon == null) return;
                ClanPics.Fill(image, message.ClanIcon);
            }
            catch (Exception e) { Plugin.Trace("[клан] значок в карточке: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SpyglassWidget), "Initialize")]
    internal static class ClanPicsSpyglassPatch
    {
        private static void Postfix(SpyglassWidget __instance, SpyGlassInfoMessage data)
        {
            try
            {
                var image = AccessTools.Field(typeof(SpyglassWidget), "ClanIconImage")?.GetValue(__instance) as Image;
                ClanPics.Drop(image);
                var user = data != null ? data.User : null;
                if (user == null || user.ClanIcon == null) return;
                ClanPics.Fill(image, user.ClanIcon);
            }
            catch (Exception e) { Plugin.Trace("[клан] значок в подзорной трубе: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ClanItemRenderer), "Data", MethodType.Setter)]
    internal static class ClanPicsListPatch
    {
        private static void Postfix(ClanItemRenderer __instance, ClanMessage value)
        {
            try
            {
                var image = AccessTools.Field(typeof(ClanItemRenderer), "ClanImage")?.GetValue(__instance) as Image;
                ClanPics.Drop(image);
                if (value == null || value.IconCode.HasValue || value.Icon == null) return;
                ClanPics.Fill(image, value.Icon);
            }
            catch (Exception e) { Plugin.Trace("[клан] значок в списке кланов: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ClanRelationItemRenderer), "Data", MethodType.Setter)]
    internal static class ClanPicsRelationPatch
    {
        private static void Postfix(ClanRelationItemRenderer __instance, ClanRelation value)
        {
            try
            {
                var image = AccessTools.Field(typeof(ClanRelationItemRenderer), "ClanImage")?.GetValue(__instance) as Image;
                ClanPics.Drop(image);
                if (value == null || value.IconCode.HasValue || value.Icon == null) return;
                if (image == null || !image.gameObject.activeSelf) return;
                ClanPics.Fill(image, value.Icon);
            }
            catch (Exception e) { Plugin.Trace("[клан] значок в отношениях кланов: " + e.Message); }
        }
    }
}
