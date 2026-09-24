using System.Collections.Generic;

namespace NewAgeQoL
{
    internal static class Storage
    {
        private static readonly Dictionary<int, int> Kept = new Dictionary<int, int>();
        private static bool _loaded;

        internal static void Forget()
        {
            lock (Kept) Kept.Clear();
            _loaded = false;
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
            Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string raw = Plugin.CfgStorageCache != null ? Plugin.CfgStorageCache.Value ?? "" : "";
            lock (Kept)
            {
                Kept.Clear();
                foreach (var part in raw.Split(','))
                {
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
