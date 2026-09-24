using System;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using UnityDI;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(SessionController), "OnGamersUpdate")]
    public static class LocationListDedupePatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                var m = msg as GamersUpdateResponseMessage;
                if (m == null || m.Type != 1) return;
                int id = m.UserInfo != null ? m.UserInfo.UserId : m.UserId;
                if (id <= 0) return;
                var chat = DependencyContainer.GetContainer().Resolve<IChat>();
                var list = chat?.PlayersOnLocation;
                if (list == null) return;
                int removed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var row = list.Content[i];
                    if (row != null && row.UserId == id) { list.RemoveItemAt(i); removed++; }
                }
                if (removed > 0) Plugin.Trace("[локация] убран повтор игрока " + id + " ×" + removed);
            }
            catch (Exception e) { Plugin.Trace("[локация] дубли: " + e.Message); }
        }
    }
}
