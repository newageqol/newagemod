using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NewAgeQoL
{
    internal static class RouteLog
    {
        internal static string File => Path.Combine(DiskJournal.Folder, "routes.log");

        private static HashSet<string> _seen;

        internal static void Note(string key, string text)
        {
            try
            {
                Load();
                if (!_seen.Add(key)) return;
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | ур. " + Level() + " | " + text + " | " + key;
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                System.IO.File.AppendAllText(File, line + Environment.NewLine, new UTF8Encoding(false));
                Plugin.Trace("[дороги] " + text);
            }
            catch (Exception e) { Plugin.Trace("[дороги] запись: " + e.Message); }
        }

        private static void Load()
        {
            if (_seen != null) return;
            _seen = new HashSet<string>();
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (var line in System.IO.File.ReadAllLines(File, Encoding.UTF8))
                {
                    int bar = line.LastIndexOf(" | ", StringComparison.Ordinal);
                    if (bar >= 0) _seen.Add(line.Substring(bar + 3).Trim());
                }
            }
            catch (Exception e) { Plugin.Trace("[дороги] чтение: " + e.Message); }
        }

        private static string Level()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null ? info.Level.ToString() : "?";
            }
            catch { return "?"; }
        }
    }
}
