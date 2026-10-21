using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Common.User;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ClanMark
    {
        private static readonly Dictionary<int, bool> Seen = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> Guess = new Dictionary<int, bool>();
        private static int _guessVersion = -1;
        private static object _on;
        private static float _next;

        internal static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Listen();
        }

        private static void Listen()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { _on = null; return; }
                if (ReferenceEquals(_on, nc)) return;
                nc.RemoveMessageListener(433, OnInfo);
                nc.AddMessageListener(433, OnInfo);
                _on = nc;
            }
            catch { _on = null; }
        }

        private static void OnInfo(object msg)
        {
            try
            {
                var info = msg as Unity3DUserInfoResponseMessage;
                if (info == null || info.UserId <= 0) return;
                Seen[info.UserId] = !string.IsNullOrEmpty(info.ClanName);
            }
            catch (Exception e) { Plugin.Trace("[clan] player info response: " + e.Message); }
        }

        private static GeneralUserInfo Me()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null ? ud.UserInfo : null;
            }
            catch { return null; }
        }

        internal static bool Left(int id)
        {
            bool had;
            return id > 0 && Seen.TryGetValue(id, out had) && !had;
        }

        internal static bool Has(int id, string login)
        {
            try
            {
                var me = Me();
                if (me != null && id > 0 && id == me.UserId) return me.InClan;

                bool had;
                if (id > 0 && Seen.TryGetValue(id, out had)) return had;

                if (string.IsNullOrEmpty(login)) return false;

                int version = OnlineList.Version;
                if (version != _guessVersion) { _guessVersion = version; Guess.Clear(); }
                bool guess;
                if (id > 0 && Guess.TryGetValue(id, out guess)) return guess;

                var one = OnlineList.ByLogin(login);
                bool clan = one != null && !string.IsNullOrEmpty(one.Clan);
                if (id > 0) Guess[id] = clan;
                return clan;
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(UserRowWidget), "RefreshClanIcon")]
    internal static class ClanMarkPatch
    {
        private static FieldInfo _icon;
        private static FieldInfo _row;

        private static void Postfix(UserRowWidget __instance)
        {
            try
            {
                if (__instance == null) return;
                if (_icon == null) _icon = AccessTools.Field(typeof(UserRowWidget), "ClanIconImage");
                if (_row == null) _row = AccessTools.Field(typeof(UserRowWidget), "_data");
                if (_icon == null || _row == null) return;

                var icon = _icon.GetValue(__instance) as Image;
                if (icon == null) return;
                ClanPics.Drop(icon);

                var data = _row.GetValue(__instance) as UserRowInfoMessage;
                bool clan = data != null && ClanMark.Has(data.UserId, data.Login);

                if (icon.gameObject.activeSelf)
                {
                    var drawn = icon.sprite;
                    if (drawn != null && drawn.name != "unknown") return;
                    if (data != null && ClanMark.Left(data.UserId)) { icon.gameObject.SetActive(false); return; }
                    if (data != null && !string.IsNullOrEmpty(data.ClanIcon))
                    {
                        ClanPics.Fill(icon, data.ClanIcon, true);
                        return;
                    }
                    if (!clan) { icon.gameObject.SetActive(false); return; }
                    icon.sprite = null;
                    icon.color = Color.white;
                    return;
                }

                if (!clan) return;
                icon.sprite = null;
                icon.color = Color.white;
                icon.gameObject.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[clan] icon: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(UserRowWidget), "Data", MethodType.Setter)]
    internal static class RowBadgePatch
    {
        private static readonly string[] Fields = { "PremiumImage", "DealerImage", "RightsImage" };
        private static readonly HashSet<string> Told = new HashSet<string>();
        private static FieldInfo[] _images;
        private static FieldInfo _row;

        private static void Postfix(UserRowWidget __instance)
        {
            try
            {
                if (__instance == null) return;
                if (_images == null)
                {
                    _images = new FieldInfo[Fields.Length];
                    for (int i = 0; i < Fields.Length; i++) _images[i] = AccessTools.Field(typeof(UserRowWidget), Fields[i]);
                    _row = AccessTools.Field(typeof(UserRowWidget), "_data");
                }
                for (int i = 0; i < _images.Length; i++)
                {
                    var image = _images[i] != null ? _images[i].GetValue(__instance) as Image : null;
                    if (image == null || !image.gameObject.activeSelf || !Quickslots.Faded(image.sprite)) continue;
                    image.gameObject.SetActive(false);
                    var data = _row != null ? _row.GetValue(__instance) as UserRowInfoMessage : null;
                    string key = Fields[i] + ":" + (data != null ? data.RightsIcon : null);
                    if (Told.Add(key)) Plugin.Trace("[row] no picture for " + Fields[i] + (data != null ? " of " + data.Login + ", rights icon \"" + data.RightsIcon + "\"" : ""));
                }
            }
            catch (Exception e) { Plugin.Trace("[row] badges: " + e.Message); }
        }
    }
}
