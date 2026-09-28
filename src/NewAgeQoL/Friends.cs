using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Responses.User.Friends;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Friends
    {
        internal const int TabId = 6;

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

        internal static Sprite Face()
        {
            try { return AtlasUtils.GetUserInfoPanelTabs(UserInfoWindowController.EUserInfoTabs.Friends); }
            catch { return null; }
        }

        internal static string Title()
        {
            try
            {
                string got = ResourceStrings.GetString("messages.userinfo.tabs.friends");
                return string.IsNullOrEmpty(got) ? "Друзья" : got;
            }
            catch { return "Друзья"; }
        }
    }

    internal sealed class FriendsTab : BasePanelContentResolver<UserMenuController.ETabs>
    {
        private FriendsUserInfoPanelContent _panel;
        private UserContextMenuResolver _menu;
        private int _me;

        public override bool InternalActivatePanel(IPanelContentWindowController parent,
                                                   StandardContentWindowPanel main,
                                                   UserMenuController.ETabs selector)
        {
            if ((int)selector != Friends.TabId) return false;
            GameObject made = null;
            try
            {
                _me = Friends.Mine();
                if (_me <= 0) { Plugin.Warn("[friends] no own id"); return false; }
                var prefab = VisualPrefabsHolder.Instance != null ? VisualPrefabsHolder.Instance.FriendsContentPanelPrefab : null;
                if (prefab == null) { Plugin.Warn("[friends] no panel prefab"); return false; }
                var nc = NetworkConnection.Instance;
                if (nc == null) { Plugin.Warn("[friends] no connection"); return false; }
                made = UnityEngine.Object.Instantiate(prefab);
                _panel = made.GetComponent<FriendsUserInfoPanelContent>();
                if (_panel == null) { Plugin.Warn("[friends] panel not built"); return false; }
                _menu = Friends.Menu();
                _panel.ContextMenuResolver = _menu;
                main.AddPanelContent(_panel);
                nc.AddMessageListener(234, OnList);
                nc.SendRequest(new FriendsListRequest(_me));
                Plugin.Trace("[friends] tab opened, requested list for " + _me);
                made = null;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Warn("[friends] tab: " + e.Message);
                return false;
            }
            finally
            {
                if (made != null)
                {
                    UnityEngine.Object.Destroy(made);
                    _panel = null;
                    _menu = null;
                }
            }
        }

        private void OnList(object msg)
        {
            try
            {
                var list = msg as FriendListResponseMessage;
                if (list == null || _panel == null || list.Userid != _me) return;
                _panel.SetFriendList(new ListWrapper<FriendMessage>(list.Friends));
                Plugin.Trace("[friends] in list " + (list.Friends != null ? list.Friends.Count : 0));
            }
            catch (Exception e) { Plugin.Trace("[friends] list: " + e.Message); }
        }

        public override bool InternalDeactivatePanel(IPanelContentWindowController parent,
                                                     StandardContentWindowPanel main,
                                                     UserMenuController.ETabs next,
                                                     bool dying)
        {
            if (!dying && (int)next == Friends.TabId) return false;
            Unhook();
            try
            {
                NetworkConnection.Instance.RemoveMessageListener(234, OnList);
                main.ClearContent();
            }
            catch (Exception e) { Plugin.Trace("[friends] close: " + e.Message); }
            _panel = null;
            _menu = null;
            return true;
        }

        private void Unhook()
        {
            try
            {
                if (_panel == null || _menu == null) return;
                var call = AccessTools.Method(typeof(FriendsUserInfoPanelContent), "OnConfigmFriend");
                if (call == null) { Plugin.Trace("[friends] no confirm handler"); return; }
                var hand = Delegate.CreateDelegate(typeof(Action<UserContextMenuData>), _panel, call) as Action<UserContextMenuData>;
                if (hand == null) return;
                _menu.ConfirmFriendEvent -= hand;
            }
            catch (Exception e) { Plugin.Trace("[friends] unsubscribe: " + e.Message); }
        }

        public override void InitializeAfterFirstActivatePanel()
        {
        }

        public override void DestroyPanel()
        {
        }
    }

    [HarmonyPatch(typeof(UserMenuController), "BuildPanelContentResolverList")]
    internal static class FriendsPanelPatch
    {
        private static void Postfix(ref IList<IPanelContentResolver<UserMenuController.ETabs>> __result)
        {
            try
            {
                if (__result == null) return;
                foreach (var one in __result) if (one is FriendsTab) return;
                __result.Add(new FriendsTab());
            }
            catch (Exception e) { Plugin.Warn("[friends] section not added: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(UserMenuController), "BuildControlTabs")]
    internal static class FriendsTabPatch
    {
        private static void Postfix(ref IList<TabView> __result)
        {
            try
            {
                if (__result == null) return;
                foreach (var one in __result) if (one != null && one.Id == Friends.TabId) return;
                __result.Add(new TabView(Friends.TabId, Friends.Title(), Friends.Face()));
                Plugin.Trace("[friends] tab placed after professions");
            }
            catch (Exception e) { Plugin.Warn("[friends] tab not added: " + e.Message); }
        }
    }
}
