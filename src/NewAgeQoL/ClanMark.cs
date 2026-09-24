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
            catch (Exception e) { Plugin.Trace("[клан] ответ о игроке: " + e.Message); }
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

        internal static bool Has(int id, string login)
        {
            try
            {
                var me = Me();
                if (me != null && id > 0 && id == me.UserId) return me.InClan;

                bool had;
                if (id > 0 && Seen.TryGetValue(id, out had)) return had;

                if (string.IsNullOrEmpty(login) || !OnlineList.Configured) return false;

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
            catch (Exception e) { Plugin.Trace("[клан] значок: " + e.Message); }
        }
    }
}
