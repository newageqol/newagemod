using System;
using System.Collections.Generic;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Storage
    {
        private static readonly Dictionary<int, int> Kept = new Dictionary<int, int>();
        private static bool _loaded;
        private static long _at;
        private static bool _near;
        private static float _askAt;

        internal static void Forget()
        {
            lock (Kept) Kept.Clear();
            _loaded = false;
            _at = 0;
            _near = false;
            _askAt = 0f;
        }

        internal static DateTime? Updated
        {
            get
            {
                Load();
                if (_at <= 0) return null;
                return DateTimeOffset.FromUnixTimeSeconds(_at).UtcDateTime;
            }
        }

        private static object _on;

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(ResponseConsts.THING_CONTEXT_ACTION_RESPONSE, OnAction);
            nc.AddMessageListener(ResponseConsts.THING_CONTEXT_ACTION_RESPONSE, OnAction);
            _on = nc;
        }

        private static void OnAction(object m)
        {
            var reply = m as Transport.Messages.Responses.Things.Actions.ThingContextActionResponseMessage;
            if (reply == null || !reply.Success || !_near) return;
            if (reply.WindowId != (int)EThingContextWindow.WINDOW_PUT_THINGS_IN_BOX && reply.WindowId != (int)EThingContextWindow.DEFAULT_WINDOW_BY_LOCATION) return;
            _askAt = Time.unscaledTime + 1f;
        }

        internal static void Tick()
        {
            bool near = Artifacts.NearStorage();
            if (near && !_near)
            {
                _askAt = Time.unscaledTime + 1.5f;
                Plugin.Trace("[storage] entered storage, reading contents shortly");
            }
            _near = near;
            Listen();
            if (_askAt <= 0f || Time.unscaledTime < _askAt) return;
            if (!near) { _askAt = 0f; return; }
            if (Artifacts.Busy) { _askAt = Time.unscaledTime + 1f; return; }
            _askAt = 0f;
            Artifacts.RequestStorage();
        }

        internal static Dictionary<int, int> Worn()
        {
            return Artifacts.WornMap();
        }

        internal static Dictionary<int, Transport.Messages.Responses.User.Inventory.InventoryWearResponseMessageItem> WornSlots()
        {
            return Artifacts.WornSlots();
        }

        internal static Dictionary<int, int> Things()
        {
            Load();
            var copy = new Dictionary<int, int>();
            lock (Kept) foreach (var pair in Kept) copy[pair.Key] = pair.Value;
            return copy;
        }

        internal static int QtyOf(int thingId)
        {
            Load();
            int qty;
            lock (Kept) return Kept.TryGetValue(thingId, out qty) ? qty : 0;
        }

        internal static void Remember(Dictionary<int, int> contents)
        {
            if (contents == null) return;
            lock (Kept)
            {
                Kept.Clear();
                foreach (var pair in contents) if (pair.Key > 0 && pair.Value > 0) Kept[pair.Key] = pair.Value;
            }
            _loaded = true;
            _at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string raw = Plugin.CfgStorageCache != null ? Plugin.CfgStorageCache.Value ?? "" : "";
            _at = 0;
            lock (Kept)
            {
                Kept.Clear();
                foreach (var part in raw.Split(','))
                {
                    long at;
                    if (part.StartsWith("t=") && long.TryParse(part.Substring(2).Trim(), out at)) { _at = at; continue; }
                    var pair = part.Split(':');
                    if (pair.Length != 2) continue;
                    int thing, qty;
                    if (!int.TryParse(pair[0].Trim(), out thing) || !int.TryParse(pair[1].Trim(), out qty)) continue;
                    if (thing > 0 && qty > 0) Kept[thing] = qty;
                }
            }
        }

        private static void Save()
        {
            if (Plugin.CfgStorageCache == null) return;
            var text = new System.Text.StringBuilder();
            if (_at > 0) text.Append("t=").Append(_at);
            lock (Kept)
                foreach (var pair in Kept)
                {
                    if (text.Length > 0) text.Append(',');
                    text.Append(pair.Key).Append(':').Append(pair.Value);
                }
            Plugin.CfgStorageCache.Value = text.ToString();
        }
    }
}
