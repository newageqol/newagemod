using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace NewAgeQoL
{
    internal sealed class WardrobeManikin
    {
        internal string Key;
        internal string Title;
        internal string Body;
        internal bool Received;
        internal string From;
        internal long At;
        internal bool Seen;
        internal int Server;
    }

    internal static class WardrobeStore
    {
        internal const int MaxOwn = 10;
        internal const int MaxTitle = 40;

        internal static readonly List<WardrobeManikin> All = new List<WardrobeManikin>();
        internal static string ActiveKey;
        private static bool _loaded;
        private static bool _dirty;
        private static float _saveAt;

        internal static string FilePath => Path.Combine(Path.Combine(BepInEx.Paths.ConfigPath, "newage.qol"), "manikins.txt");

        internal static int OwnCount
        {
            get
            {
                Load();
                int n = 0;
                foreach (var m in All) if (!m.Received) n++;
                return n;
            }
        }

        internal static int Unseen
        {
            get
            {
                Load();
                int n = 0;
                foreach (var m in All) if (m.Received && !m.Seen) n++;
                return n;
            }
        }

        internal static WardrobeManikin Active
        {
            get
            {
                Load();
                foreach (var m in All) if (m.Key == ActiveKey) return m;
                return null;
            }
        }

        internal static string Clean(string text)
        {
            text = (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > MaxTitle ? text.Substring(0, MaxTitle) : text;
        }

        internal static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    var cell = line.Split('\t');
                    if (cell.Length >= 2 && cell[0] == "A") ActiveKey = cell[1];
                    else if (cell.Length >= 4 && cell[0] == "M")
                        All.Add(new WardrobeManikin { Key = cell[1], Title = cell[2], Body = cell[3] });
                    else if (cell.Length >= 8 && cell[0] == "R")
                    {
                        long at;
                        int server;
                        long.TryParse(cell[4], out at);
                        int.TryParse(cell[6], out server);
                        All.Add(new WardrobeManikin
                        {
                            Key = cell[1], Title = cell[2], From = cell[3], At = at, Seen = cell[5] == "1",
                            Server = server, Body = cell[7], Received = true
                        });
                    }
                }
                Plugin.Trace("[переодевалка] манекенов прочитано: " + All.Count);
            }
            catch (Exception e) { Plugin.Warn("[переодевалка] манекены не прочитаны: " + e.Message); }
        }

        internal static void Touch()
        {
            _dirty = true;
            _saveAt = Time.unscaledTime + 1f;
        }

        internal static void Tick()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) Flush();
        }

        internal static bool Flush()
        {
            if (!_dirty) return true;
            try
            {
                var text = new StringBuilder();
                if (!string.IsNullOrEmpty(ActiveKey)) text.Append("A\t").Append(ActiveKey).Append('\n');
                foreach (var m in All)
                {
                    if (m.Received)
                        text.Append("R\t").Append(m.Key).Append('\t').Append(Clean(m.Title)).Append('\t').Append(Clean(m.From)).Append('\t')
                            .Append(m.At).Append('\t').Append(m.Seen ? "1" : "0").Append('\t').Append(m.Server).Append('\t').Append(m.Body ?? "").Append('\n');
                    else
                        text.Append("M\t").Append(m.Key).Append('\t').Append(Clean(m.Title)).Append('\t').Append(m.Body ?? "").Append('\n');
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string temp = FilePath + ".new";
                File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(temp, FilePath);
                _dirty = false;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Warn("[переодевалка] манекены не сохранены: " + e.Message);
                _saveAt = Time.unscaledTime + 10f;
                return false;
            }
        }

        internal static WardrobeManikin Add(string title, string body)
        {
            Load();
            if (OwnCount >= MaxOwn) return null;
            var m = new WardrobeManikin { Key = Guid.NewGuid().ToString("N"), Title = Clean(title), Body = body };
            int at = All.FindIndex(x => x.Received);
            if (at < 0) All.Add(m);
            else All.Insert(at, m);
            Touch();
            return m;
        }

        internal static void Remove(WardrobeManikin m)
        {
            Load();
            All.Remove(m);
            if (ActiveKey == m.Key) ActiveKey = null;
            Touch();
        }

        internal static bool Receive(int server, string from, long at, string title, string body)
        {
            Load();
            foreach (var m in All)
                if (m.Received && m.Server == server && m.From == from) return false;
            All.Add(new WardrobeManikin
            {
                Key = Guid.NewGuid().ToString("N"), Title = Clean(title), From = Clean(from), At = at,
                Server = server, Body = body, Received = true, Seen = false
            });
            Touch();
            return true;
        }

        internal static string FreeTitle()
        {
            for (int n = 1; n <= MaxOwn + 1; n++)
            {
                string title = "Манекен " + n;
                bool taken = false;
                foreach (var m in All) if (!m.Received && m.Title == title) taken = true;
                if (!taken) return title;
            }
            return "Манекен";
        }
    }
}
