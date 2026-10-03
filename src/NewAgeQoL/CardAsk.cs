using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Transport.Messages.Responses.User.Info;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch]
    internal static class CardAsk
    {
        private const short Answer = 433;
        private const float Patience = 3f;

        private static readonly FieldInfo Table = AccessTools.Field(typeof(NetworkConnection), "_callbacksTable");
        private static readonly Dictionary<int, float> Sent = new Dictionary<int, float>();
        private static int _id;
        private static string _login;
        private static float _at = -1f;
        private static bool _byId;
        private static object _on;

        [HarmonyPrefix, HarmonyPatch(typeof(UserContextMenuController), "ShowUserInfo")]
        private static bool FromMenu(UserContextMenuData obj) => Ask(obj);

        [HarmonyPrefix, HarmonyPatch(typeof(ChatWindowController), "ShowUserInfo")]
        private static bool FromChat(UserContextMenuData obj) => Ask(obj);

        [HarmonyPrefix, HarmonyPatch(typeof(UserInfoWindowController), nameof(UserInfoWindowController.ShowUserInfoDialog))]
        private static void Free(int userId, string login)
        {
            try
            {
                int key = userId != 0 ? userId : (login?.GetHashCode() ?? 0);
                float was;
                if (Sent.TryGetValue(key, out was) && Time.unscaledTime - was > Patience) Unstick(key);
                Sent[key] = Time.unscaledTime;
            }
            catch (Exception e) { Plugin.Trace("[card] old request not cleared: " + e.Message); }
        }

        private static bool Ask(UserContextMenuData obj)
        {
            if (obj == null || string.IsNullOrEmpty(obj.Login)) return true;
            try
            {
                var info = Controllers.Get<UserInfoWindowController>();
                if (info == null) return true;
                if (_at >= 0f) Drop(_byId ? _id : 0, _byId ? null : _login);
                _id = obj.Id;
                _login = obj.Login;
                _at = Time.unscaledTime;
                _byId = false;
                Plugin.Trace("[card] asking card of " + obj.Login + " by nick");
                info.ShowUserInfoDialog(0, obj.Login);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[card] ask by nick: " + e.Message);
                return true;
            }
        }

        private static void Drop(int id, string login)
        {
            int key = id != 0 ? id : (login?.GetHashCode() ?? 0);
            Unstick(key);
            Sent.Remove(key);
        }

        internal static void Shutdown()
        {
            try
            {
                if (_on is INetworkConnection nc) nc.RemoveMessageListener(Answer, OnCard);
            }
            catch (Exception e) { Plugin.Trace("[card] listener not removed: " + e.Message); }
            _on = null;
            _at = -1f;
            Sent.Clear();
        }

        private static void Unstick(int key)
        {
            var nc = NetworkConnection.Instance as NetworkConnection;
            if (nc == null || Table == null) return;
            if (!(Table.GetValue(nc) is Dictionary<short, List<ResponseCallbackContext>> table)) return;
            List<ResponseCallbackContext> list;
            if (!table.TryGetValue(Answer, out list) || list == null) return;
            var stale = new List<ResponseCallbackContext>();
            foreach (var context in list)
                if (context != null && context.ContainsDisableTwiceSendValue(Answer, key)) stale.Add(context);
            foreach (var context in stale) nc.UnregisterCallbackContext(context);
            if (stale.Count > 0) Plugin.Trace("[card] cleared " + stale.Count + " unanswered card request(s)");
        }

        internal static void Tick()
        {
            try
            {
                Listen();
                if (_at < 0f || Time.unscaledTime - _at < Patience) return;
                var info = Controllers.Get<UserInfoWindowController>();
                if (!_byId && _id > 0 && info != null)
                {
                    _byId = true;
                    _at = Time.unscaledTime;
                    Drop(0, _login);
                    Plugin.Trace("[card] no card of " + _login + " by nick, asking by id");
                    info.ShowUserInfoDialog(_id, null);
                    return;
                }
                _at = -1f;
                Drop(_byId ? _id : 0, _byId ? null : _login);
                Plugin.Trace("[card] server sent no card of " + _login);
                Notice.Show("Сервер не прислал карточку игрока " + _login, 5f);
            }
            catch (Exception e)
            {
                _at = -1f;
                Plugin.Trace("[card] waiting for card: " + e.Message);
            }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            if (ReferenceEquals(_on, nc)) return;
            Shutdown();
            nc.RemoveMessageListener(Answer, OnCard);
            nc.AddMessageListener(Answer, OnCard);
            _on = nc;
        }

        private static void OnCard(object m)
        {
            var info = m as Unity3DUserInfoResponseMessage;
            if (info == null || _at < 0f) return;
            if ((_id > 0 && info.UserId == _id) || string.Equals(info.Login, _login, StringComparison.CurrentCultureIgnoreCase)) _at = -1f;
        }
    }
}
