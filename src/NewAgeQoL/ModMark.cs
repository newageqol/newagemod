using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Common.User;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ModMark
    {
        private const string Mark = "newage-marks 1";
        private const string BadgeName = "QoLModMark";
        private const float Every = 45f;
        private const float Retry = 90f;
        private const float LookEvery = 0.3f;
        private const int Side = 32;

        private sealed class Seen
        {
            internal int Id;
            internal string Text;
            internal int Version = -1;
            internal int Size;
        }

        private static readonly HashSet<int> Ids = new HashSet<int>();
        private static readonly Dictionary<Text, Seen> Labels = new Dictionary<Text, Seen>();
        private static readonly List<Text> Gone = new List<Text>();
        private static int _version;
        private static bool _busy;
        private static float _askAt;
        private static float _lookAt;
        private static Sprite _art;

        internal static bool Has(int userId) => userId > 0 && Ids.Contains(userId);

        private static string Where()
        {
            string url = Report.Address;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
            int at = url.LastIndexOf("/report", StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at + 7 != url.TrimEnd('/').Length) return "";
            return url.Substring(0, at) + "/marks";
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (!_busy && now >= _askAt && Plugin.Instance != null && SideButtons.InWorld())
            {
                string url = Where();
                if (url.Length == 0) _askAt = now + Every;
                else
                {
                    _busy = true;
                    Plugin.Instance.StartCoroutine(Fetch(url));
                }
            }
            if (now < _lookAt) return;
            _lookAt = now + LookEvery;
            Refresh();
        }

        private static IEnumerator Fetch(string url)
        {
            var req = UnityWebRequest.Get(url);
            req.timeout = 15;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            long code = req.responseCode;
            req.Dispose();
            bool took = false;
            try
            {
                if (body.StartsWith(Mark, StringComparison.Ordinal)) { Take(body); took = true; }
                else Plugin.Trace("[mod mark] server answered " + code);
            }
            catch (Exception e) { Plugin.Caught("[mod mark] answer: " + e.Message); }
            _busy = false;
            _askAt = Time.unscaledTime + (took ? Every : Retry);
        }

        private static void Take(string body)
        {
            var fresh = new HashSet<int>();
            foreach (var line in body.Split('\n'))
            {
                int id;
                if (int.TryParse(line.Trim(), out id) && id > 0) fresh.Add(id);
            }
            if (fresh.SetEquals(Ids)) return;
            Ids.Clear();
            Ids.UnionWith(fresh);
            _version++;
            Plugin.Trace("[mod mark] players with the mod: " + Ids.Count);
        }

        internal static void Track(Text label, int userId)
        {
            if (label == null) return;
            try
            {
                Seen seen;
                if (!Labels.TryGetValue(label, out seen)) Labels[label] = seen = new Seen();
                if (seen.Id != userId) { seen.Id = userId; seen.Version = -1; }
                Apply(label, seen);
            }
            catch (Exception e) { Plugin.Caught("[mod mark] label: " + e.Message); }
        }

        private static void Refresh()
        {
            Gone.Clear();
            foreach (var pair in Labels)
            {
                if (pair.Key == null) { Gone.Add(pair.Key); continue; }
                try { Apply(pair.Key, pair.Value); }
                catch (Exception e) { Plugin.Caught("[mod mark] refresh: " + e.Message); }
            }
            foreach (var dead in Gone) Labels.Remove(dead);
        }

        private static void Apply(Text label, Seen seen)
        {
            string text = label.text ?? "";
            int size = label.fontSize;
            if (seen.Version == _version && seen.Text == text && seen.Size == size) return;
            seen.Version = _version;
            seen.Text = text;
            seen.Size = size;

            var t = label.transform.Find(BadgeName);
            bool on = Has(seen.Id) && text.Length > 0;
            if (!on)
            {
                if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
                return;
            }
            var go = t != null ? t.gameObject : Build(label);
            if (go == null) return;
            var rt = (RectTransform)go.transform;
            var box = label.rectTransform.rect;
            float wide = label.preferredWidth;
            if (label.horizontalOverflow == HorizontalWrapMode.Wrap) wide = Mathf.Min(wide, box.width);
            float tall = Mathf.Min(label.preferredHeight, Mathf.Max(box.height, size));
            float side = Mathf.Clamp(size * 0.6f, 7f, 18f);
            int across = (int)label.alignment % 3;
            int down = (int)label.alignment / 3;
            float end = across == 0 ? box.xMin + wide : across == 1 ? box.center.x + wide * 0.5f : box.xMax;
            float top = down == 0 ? box.yMax : down == 1 ? box.center.y + tall * 0.5f : box.yMin + tall;
            rt.anchorMin = rt.anchorMax = label.rectTransform.pivot;
            rt.sizeDelta = new Vector2(side, side);
            rt.anchoredPosition = new Vector2(end + side * 0.55f + 1f, top - side * 0.45f);
            if (!go.activeSelf) go.SetActive(true);
        }

        private static GameObject Build(Text label)
        {
            var art = Art();
            if (art == null) return null;
            var go = new GameObject(BadgeName, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(label.transform, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            var image = go.GetComponent<Image>();
            image.sprite = art;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return go;
        }

        private static Sprite Art()
        {
            if (_art != null) return _art;
            try
            {
                var tex = new Texture2D(Side, Side, TextureFormat.RGBA32, false);
                var px = new Color32[Side * Side];
                var fillTop = new Color(0.55f, 0.95f, 0.88f);
                var fillLow = new Color(0.16f, 0.66f, 0.62f);
                var edge = new Color(0.03f, 0.13f, 0.14f);
                const int sub = 4;
                for (int py = 0; py < Side; py++)
                    for (int pxx = 0; pxx < Side; pxx++)
                    {
                        float r = 0f, g = 0f, b = 0f, a = 0f;
                        for (int sy = 0; sy < sub; sy++)
                            for (int sx = 0; sx < sub; sx++)
                            {
                                float x = ((pxx + (sx + 0.5f) / sub) / Side) * 2f - 1f;
                                float y = ((py + (sy + 0.5f) / sub) / Side) * 2f - 1f;
                                float d = Mathf.Abs(x) + Mathf.Abs(y);
                                if (d > 0.96f) continue;
                                Color c = d > 0.7f ? edge : Color.Lerp(fillLow, fillTop, (y + 1f) * 0.5f);
                                r += c.r; g += c.g; b += c.b; a += 1f;
                            }
                        int i = py * Side + pxx;
                        if (a <= 0f) { px[i] = new Color32(0, 0, 0, 0); continue; }
                        px[i] = new Color(r / a, g / a, b / a, a / (sub * sub));
                    }
                tex.SetPixels32(px);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply(false, true);
                tex.name = BadgeName;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _art = Sprite.Create(tex, new Rect(0, 0, Side, Side), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _art.name = BadgeName;
                _art.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            catch (Exception e) { Plugin.Caught("[mod mark] picture: " + e.Message); }
            return _art;
        }

        internal static Text Field(object owner, ref FieldInfo field, Type type, string name)
        {
            if (owner == null) return null;
            if (field == null) field = AccessTools.Field(type, name);
            return field != null ? field.GetValue(owner) as Text : null;
        }
    }

    [HarmonyPatch(typeof(UserRowWidget), "Data", MethodType.Setter)]
    internal static class ModMarkRowPatch
    {
        private static FieldInfo _login;

        private static void Postfix(UserRowWidget __instance, UserRowInfoMessage value)
        {
            try { ModMark.Track(ModMark.Field(__instance, ref _login, typeof(UserRowWidget), "LoginText"), value != null ? value.UserId : 0); }
            catch (Exception e) { Plugin.Caught("[mod mark] row: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(TroopPanelWidget), "Initialize")]
    internal static class ModMarkTroopPatch
    {
        private static FieldInfo _login;

        private static void Postfix(TroopPanelWidget __instance)
        {
            try { ModMark.Track(ModMark.Field(__instance, ref _login, typeof(TroopPanelWidget), "LoginText"), __instance.Id); }
            catch (Exception e) { Plugin.Caught("[mod mark] troop: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GeneralUserInfoPanelContent), "Init")]
    internal static class ModMarkCardPatch
    {
        private static FieldInfo _login;

        private static void Postfix(GeneralUserInfoPanelContent __instance, Unity3DUserInfoResponseMessage message)
        {
            try { ModMark.Track(ModMark.Field(__instance, ref _login, typeof(GeneralUserInfoPanelContent), "LoginText"), message != null ? message.UserId : 0); }
            catch (Exception e) { Plugin.Caught("[mod mark] card: " + e.Message); }
        }
    }
}
