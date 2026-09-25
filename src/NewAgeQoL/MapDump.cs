using System;
using System.IO;
using System.Text;

namespace NewAgeQoL
{
    internal static class MapDump
    {
        internal static string Folder => Path.Combine(DiskJournal.Folder, "maps");

        internal static void Save(string xml, LocationMap map)
        {
            try
            {
                if (string.IsNullOrEmpty(xml) || map == null || map.MapType == 1) return;
                bool world = map.MapType == 2;
                int id = world && map.RootLocation > 0 ? map.RootLocation : map.MapId;
                if (id <= 0) return;
                string file = Path.Combine(Folder, (world ? "area_" : "place_") + id + ".xml");
                bool had = File.Exists(file);
                if (had && File.ReadAllText(file, Encoding.UTF8) == xml) return;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(file, xml, new UTF8Encoding(false));
                if (world) RouteLog.Note("карта участка " + id, "сохранил карту участка " + id);
                else RouteLog.Note("карта локации " + id, "сохранил карту локации " + id);
                if (had) Plugin.Trace("[дороги] карта " + (world ? "участка " : "локации ") + id + " изменилась, пересохранил");
            }
            catch (Exception e) { Plugin.Trace("[дороги] карта: " + e.Message); }
        }
    }
}
