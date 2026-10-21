using System;
using System.Collections.Generic;
using Transport.Messages.Responses.User.Friends;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Friends
    {
        private const float AfterChange = 0.8f;

        internal static readonly List<FriendMessage> List = new List<FriendMessage>();
        internal static int Version { get; private set; }
        internal static bool Loaded { get; private set; }

        private static bool _hooked;
        private static int _owner;
        private static float _again;
        private static UserContextMenuResolver _watched;

        internal static int Mine()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null && ud.UserInfo != null ? ud.UserInfo.UserId : 0;
            }
            catch { return 0; }
        }

        internal static UserContextMenuResolver Menu()
        {
            try { return Controllers.Get<UserContextMenuController>()?.MyFriendsContextMenuResolver; }
            catch { return null; }
        }

        internal static void Ask()
        {
            try
            {
                int me = Mine();
                var nc = NetworkConnection.Instance;
                if (me <= 0 || nc == null) { Plugin.Trace("[friends] list not asked: no own id or connection"); return; }
                if (me != _owner)
                {
                    _owner = me;
                    List.Clear();
                    Loaded = false;
                    Version++;
                }
                if (!_hooked)
                {
                    nc.AddMessageListener(234, OnList);
                    _hooked = true;
                }
                nc.SendRequest(new FriendsListRequest(me));
                Plugin.Trace("[friends] requested list for " + me);
            }
            catch (Exception e) { Plugin.Warn("[friends] ask: " + e.Message); }
        }

        internal static void Drop()
        {
            _again = 0f;
            if (!_hooked) return;
            try { NetworkConnection.Instance?.RemoveMessageListener(234, OnList); }
            catch (Exception e) { Plugin.Trace("[friends] unlisten: " + e.Message); }
            _hooked = false;
        }

        internal static void Watch()
        {
            var menu = Menu();
            if (menu == null || menu == _watched) return;
            Unwatch();
            menu.ConfirmFriendEvent += Changed;
            menu.RemoveFriendEvent += Changed;
            _watched = menu;
        }

        internal static void Unwatch()
        {
            if (_watched == null) return;
            _watched.ConfirmFriendEvent -= Changed;
            _watched.RemoveFriendEvent -= Changed;
            _watched = null;
        }

        internal static void Tick()
        {
            if (_again <= 0f || Time.unscaledTime < _again) return;
            _again = 0f;
            Ask();
        }

        private static void Changed(UserContextMenuData data)
        {
            _again = Time.unscaledTime + AfterChange;
        }

        private static void OnList(object msg)
        {
            try
            {
                var list = msg as FriendListResponseMessage;
                if (list == null || list.Userid != Mine()) return;
                List.Clear();
                if (list.Friends != null)
                    foreach (var one in list.Friends)
                        if (one != null) List.Add(one);
                Loaded = true;
                Version++;
                Plugin.Trace("[friends] in list " + List.Count);
            }
            catch (Exception e) { Plugin.Trace("[friends] list: " + e.Message); }
        }
    }
}
