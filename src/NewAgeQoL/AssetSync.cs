using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal static class AssetSync
    {
        private const string Base = "https://github.com/newageqol/newagemod/releases/download/assets/";
        private const string ManifestName = "manifest.txt";
        private static readonly string[] Legacy = { "NightTown", "MagicTower" };

        private class Entry
        {
            public string Path;
            public long Size;
            public string Sha;
            public string Asset;
        }

        private class Group
        {
            public Func<bool> InUse;
            public Action Release;
        }

        private static readonly Dictionary<string, Group> Groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase)
        {
            ["NightTown"] = new Group { InUse = NightTown.Busy, Release = NightTown.Unload },
            ["BankHall"] = new Group { InUse = BankHall.InUse, Release = BankHall.Unload },
        };

        private static float _startAt = 20f;
        private static bool _running;
        private static bool _done;
        private static Dictionary<string, string> _known;
        private static readonly Dictionary<string, string> Verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static bool Loading { get; private set; }

        internal static string Root => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";

        private static string Stage => Path.Combine(Root, ".assets");

        private static string StateFile => Path.Combine(Stage, "state.txt");

        internal static void Tick()
        {
            if (Plugin.Instance == null) return;
            if (!Loading) NightTown.Warm();
            if (_running || _done || Time.unscaledTime < _startAt) return;
            _running = true;
            Plugin.Instance.StartCoroutine(Run());
        }

        internal static void Boot()
        {
            try
            {
                foreach (var g in Legacy)
                {
                    var old = Path.Combine(Path.Combine(Root, g), ".download");
                    if (Directory.Exists(old)) Directory.Delete(old, true);
                }
            }
            catch (Exception e) { Plugin.Trace("[assets] removing old downloads: " + e.Message); }
            foreach (var g in Staged()) Settle(g);
        }

        internal static bool Settle(string group)
        {
            try
            {
                var from = Path.Combine(Stage, group);
                if (!Directory.Exists(from)) return false;
                var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories);
                var ready = new List<string>();
                foreach (var f in files) if (!f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) ready.Add(f);
                if (ready.Count == 0) return false;
                if (Groups.TryGetValue(group, out var hooks))
                {
                    if (hooks.InUse != null && hooks.InUse()) return false;
                    hooks.Release?.Invoke();
                }
                else
                {
                    if (Locations.InUse(group)) return false;
                    Locations.Release(group);
                }
                foreach (var f in ready)
                {
                    var rel = f.Substring(Stage.Length + 1);
                    var to = Path.Combine(Root, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(f, to);
                    Remember(rel.Replace('\\', '/'), to);
                }
                SaveState();
                Plugin.Trace("[assets] " + group + ": " + ready.Count + " new files in place");
                return true;
            }
            catch (Exception e) { Plugin.Warn("[assets] installing " + group + ": " + e.Message); return false; }
        }

        private static IEnumerable<string> Staged()
        {
            if (!Directory.Exists(Stage)) yield break;
            foreach (var d in Directory.GetDirectories(Stage)) yield return Path.GetFileName(d);
        }

        private static IEnumerator Run()
        {
            bool finished = false;
            try
            {
                var request = UnityWebRequest.Get(Base + ManifestName + "?t=" + DateTime.UtcNow.Ticks);
                request.SetRequestHeader("User-Agent", "NewAgeQoL");
                request.timeout = 30;
                yield return request.SendWebRequest();
                string error = request.error;
                string text = string.IsNullOrEmpty(error) ? request.downloadHandler.text ?? "" : "";
                long code = request.responseCode;
                request.Dispose();
                if (text.Length == 0)
                {
                    Plugin.Log.LogInfo("[assets] manifest unavailable: code " + code + ", " + (error ?? "empty"));
                    yield break;
                }
                var entries = Parse(text);
                Plugin.Log.LogInfo("[assets] manifest: " + entries.Count + " files");
                LoadState();

                var need = new List<Entry>();
                foreach (var e in entries)
                {
                    string have = null;
                    yield return Hash(Path.Combine(Root, e.Path), e.Path, h => have = h);
                    if (have == e.Sha) continue;
                    string staged = null;
                    yield return Hash(Path.Combine(Stage, e.Path), null, h => staged = h);
                    if (staged == e.Sha) { Verified[e.Path] = e.Sha; continue; }
                    need.Add(e);
                }
                SaveState();
                long total = 0;
                foreach (var e in need) total += e.Size;
                if (need.Count > 0) Plugin.Log.LogInfo("[assets] downloading " + need.Count + " files, " + total + " bytes");

                Loading = need.Count > 0;
                foreach (var e in need)
                {
                    bool ok = false;
                    yield return Fetch(e, r => ok = r);
                    if (!ok) { Plugin.Log.LogInfo("[assets] stopped at " + e.Path + ", will continue next launch"); yield break; }
                }
                finished = true;
            }
            finally
            {
                Loading = false;
                _running = false;
                _done = true;
                foreach (var g in Staged()) Settle(g);
                if (finished) Plugin.Trace("[assets] all files current");
            }
        }

        private static List<Entry> Parse(string text)
        {
            var list = new List<Entry>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = line.Split(' ');
                if (p.Length < 4 || !long.TryParse(p[1], out long size)) continue;
                var path = p[0];
                if (!Safe(path) || !Regex.IsMatch(p[2], "^[0-9a-f]{64}$") || !Regex.IsMatch(p[3], "^[A-Za-z0-9._-]{1,200}$")) continue;
                list.Add(new Entry { Path = path, Size = size, Sha = p[2], Asset = p[3] });
            }
            return list;
        }

        private static bool Safe(string path)
        {
            if (!Regex.IsMatch(path, @"^[A-Za-z0-9_]+(/[A-Za-z0-9._-]+)+$")) return false;
            if (path.Contains("..")) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".dll" || ext == ".exe" || ext == ".cfg") return false;
            var top = path.Substring(0, path.IndexOf('/'));
            return !top.Equals("NewAge2D", StringComparison.OrdinalIgnoreCase) && !top.Equals("config", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator Fetch(Entry e, Action<bool> done)
        {
            var target = Path.Combine(Stage, e.Path);
            var part = target + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            int failures = 0;
            while (true)
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > e.Size) { File.Delete(part); have = 0; }
                if (have == e.Size)
                {
                    string sha = null;
                    yield return Hash(part, null, h => sha = h);
                    if (sha != e.Sha)
                    {
                        Plugin.Log.LogInfo("[assets] checksum mismatch for " + e.Path + ", downloading again");
                        File.Delete(part);
                        if (++failures > 2) { done(false); yield break; }
                        continue;
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    Verified[e.Path] = e.Sha;
                    done(true);
                    yield break;
                }
                while (SideButtons.InCombat()) yield return new WaitForSecondsRealtime(2f);

                var request = new UnityWebRequest(Base + e.Asset, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerFile(part, have > 0) { removeFileOnAbort = false };
                request.SetRequestHeader("User-Agent", "NewAgeQoL");
                if (have > 0) request.SetRequestHeader("Range", "bytes=" + have + "-");
                var op = request.SendWebRequest();
                bool paused = false, restart = false, stalled = false;
                ulong seen = 0;
                float quietSince = Time.unscaledTime;
                while (!op.isDone)
                {
                    if (have > 0 && request.responseCode == 200) { request.Abort(); restart = true; break; }
                    if (SideButtons.InCombat()) { request.Abort(); paused = true; break; }
                    if (request.downloadedBytes != seen) { seen = request.downloadedBytes; quietSince = Time.unscaledTime; }
                    else if (Time.unscaledTime - quietSince > 60f) { request.Abort(); stalled = true; break; }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                string error = stalled ? "no data for 60 s" : request.error;
                long code = request.responseCode;
                request.Dispose();
                if (restart) { try { File.Delete(part); } catch { } continue; }
                if (paused) continue;
                if (!string.IsNullOrEmpty(error) || (code != 200 && code != 206))
                {
                    Plugin.Log.LogInfo("[assets] downloading " + e.Path + ": " + (error ?? "code " + code));
                    if (++failures > 2) { done(false); yield break; }
                    yield return new WaitForSecondsRealtime(10f);
                }
            }
        }

        private static IEnumerator Hash(string file, string key, Action<string> done)
        {
            if (!File.Exists(file)) { done(null); yield break; }
            var info = new FileInfo(file);
            string stamp = info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            if (key != null && _known != null && _known.TryGetValue(key, out var cached))
            {
                int bar = cached.LastIndexOf('|');
                if (bar > 0 && cached.Substring(0, bar) == stamp) { done(cached.Substring(bar + 1)); yield break; }
            }
            string result = null;
            bool over = false;
            var worker = new Thread(() =>
            {
                try
                {
                    using (var sha = SHA256.Create())
                    using (var s = File.OpenRead(file))
                    {
                        var sb = new StringBuilder(64);
                        foreach (var b in sha.ComputeHash(s)) sb.Append(b.ToString("x2"));
                        result = sb.ToString();
                    }
                }
                catch { result = null; }
                over = true;
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal };
            worker.Start();
            while (!over) yield return null;
            if (key != null && result != null && _known != null) _known[key] = stamp + "|" + result;
            done(result);
        }

        private static void Remember(string key, string file)
        {
            if (_known == null) LoadState();
            try
            {
                if (!Verified.TryGetValue(key, out var sha)) { _known.Remove(key); return; }
                Verified.Remove(key);
                var info = new FileInfo(file);
                _known[key] = info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + sha;
            }
            catch { _known.Remove(key); }
        }

        private static void LoadState()
        {
            _known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(StateFile)) return;
                foreach (var line in File.ReadAllLines(StateFile))
                {
                    var p = line.Split(' ');
                    if (p.Length == 2) _known[p[0]] = p[1];
                }
            }
            catch { }
        }

        private static void SaveState()
        {
            if (_known == null) return;
            try
            {
                Directory.CreateDirectory(Stage);
                var lines = new List<string>();
                foreach (var kv in _known) lines.Add(kv.Key + " " + kv.Value);
                File.WriteAllLines(StateFile, lines.ToArray());
            }
            catch (Exception e) { Plugin.Trace("[assets] saving state: " + e.Message); }
        }
    }
}
