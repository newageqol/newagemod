using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal static class Updater
    {
        private const string Api = "https://api.github.com/repos/newageqol/newagemod/releases/latest";
        private const string DllName = "NewAgeQoL.dll";

        [Serializable]
        private class Asset
        {
            public string name = null;
            public string browser_download_url = null;
        }

        [Serializable]
        private class Release
        {
            public string tag_name = null;
            public Asset[] assets = null;
        }

        internal enum Stage { Idle, Checking, Fresh, Newer, Downloading, Done, Failed }

        internal static Stage State = Stage.Idle;
        internal static string Message = "";
        internal static string LatestVersion = "";
        private static string _dllUrl = "";
        private static string _zipUrl = "";
        private static string _dllFile = "";
        private static string _zipFile = "";
        private const string SigExt = ".sig";
        private static readonly Dictionary<string, string> Signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);


        internal static bool CanUpdate => State == Stage.Newer && (_dllUrl.Length > 0 || _zipUrl.Length > 0);

        internal static void CheckSilent()
        {
            if (State != Stage.Idle) return;
            Check();
        }

        private static bool _told;

        internal static void Remind()
        {
            if (_told || !CanUpdate) return;
            if (!SideButtons.InWorld() || SideButtons.InCombat()) return;
            _told = true;
            Notice.Show("Вышла версия мода " + LatestVersion + ". Обновить можно в настройках мода, раздел «Мод».", 15f);
        }

        internal static void Check()
        {
            if (State == Stage.Checking || State == Stage.Downloading) return;
            if (Plugin.Instance == null) return;
            State = Stage.Checking;
            Message = "проверяю…";
            Plugin.Instance.StartCoroutine(CheckRoutine());
        }

        internal static void Update()
        {
            if (!CanUpdate || Plugin.Instance == null) return;
            State = Stage.Downloading;
            Message = "качаю " + LatestVersion + "…";
            Plugin.Instance.StartCoroutine(UpdateRoutine());
        }

        private static IEnumerator CheckRoutine()
        {
            var request = UnityWebRequest.Get(Api);
            request.SetRequestHeader("User-Agent", "NewAgeQoL");
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.timeout = 15;
            yield return request.SendWebRequest();

            string error = request.error;
            string json = error == null || error.Length == 0 ? request.downloadHandler.text : "";
            request.Dispose();

            if (json.Length == 0)
            {
                State = Stage.Failed;
                Message = "не удалось проверить: " + (error ?? "нет ответа");
                Plugin.Trace("[update] " + Message);
                yield break;
            }

            Release release = null;
            try { release = JsonUtility.FromJson<Release>(json); }
            catch (Exception e) { Plugin.Trace("[update] parsing response: " + e.Message); }

            string tag = release != null ? release.tag_name : null;
            _dllUrl = "";
            _zipUrl = "";
            _dllFile = "";
            _zipFile = "";
            Signatures.Clear();
            if (release != null && release.assets != null)
                foreach (var asset in release.assets)
                {
                    if (asset == null || string.IsNullOrEmpty(asset.browser_download_url)) continue;
                    Remember(asset.name ?? "", asset.browser_download_url);
                }

            if (string.IsNullOrEmpty(tag))
            {
                var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) tag = m.Groups[1].Value;
            }
            if (_dllUrl.Length == 0 && _zipUrl.Length == 0)
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
                {
                    string url = m.Groups[1].Value;
                    Remember(url, url);
                }
            bool noSignature = _dllUrl.Length > 0 || _zipUrl.Length > 0;
            if (_zipUrl.Length > 0 && !Signatures.ContainsKey(_zipFile)) _zipUrl = "";
            if (_dllUrl.Length > 0 && !Signatures.ContainsKey(_dllFile)) _dllUrl = "";
            noSignature = noSignature && _dllUrl.Length == 0 && _zipUrl.Length == 0;

            if (string.IsNullOrEmpty(tag))
            {
                State = Stage.Failed;
                Message = "не удалось разобрать ответ сервера";
                yield break;
            }

            LatestVersion = Clean(tag);

            int cmp = Compare(LatestVersion, Plugin.Version);
            if (cmp <= 0)
            {
                State = Stage.Fresh;
                Message = "у тебя последняя версия";
            }
            else if (_dllUrl.Length == 0 && _zipUrl.Length == 0)
            {
                State = Stage.Failed;
                Message = "вышла " + LatestVersion + ", но " + (noSignature ? "у файла мода в релизе нет подписи" : DllName + " в релизе нет");
            }
            else
            {
                State = Stage.Newer;
                Message = "вышла версия " + LatestVersion;
            }
            Plugin.Trace("[update] installed " + Plugin.Version + ", on server " + LatestVersion);
        }

        private static readonly char[] Cuts = { '/', '\\' };

        private static void Remember(string name, string url)
        {
            string file = FileName(name);
            if (file.EndsWith(SigExt, StringComparison.OrdinalIgnoreCase))
            {
                Signatures[file.Substring(0, file.Length - SigExt.Length)] = url;
                return;
            }
            if (file.Equals(DllName, StringComparison.OrdinalIgnoreCase) && _dllUrl.Length == 0) { _dllUrl = url; _dllFile = file; }
            else if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && _zipUrl.Length == 0) { _zipUrl = url; _zipFile = file; }
        }

        private static string FileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string tail = name;
            int ask = tail.IndexOf('?');
            if (ask >= 0) tail = tail.Substring(0, ask);
            int cut = tail.LastIndexOfAny(Cuts);
            if (cut >= 0) tail = tail.Substring(cut + 1);
            return tail;
        }

        private static IEnumerator Fetch(string url, int timeout, Action<byte[], string> done)
        {
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", "NewAgeQoL");
            request.timeout = timeout;
            yield return request.SendWebRequest();
            string error = request.error;
            byte[] data = string.IsNullOrEmpty(error) ? request.downloadHandler.data : null;
            request.Dispose();
            done(data, error);
        }

        private static IEnumerator UpdateRoutine()
        {
            bool zip = _zipUrl.Length > 0;
            string url = zip ? _zipUrl : _dllUrl;
            string file = zip ? _zipFile : _dllFile;

            byte[] data = null;
            string error = null;
            yield return Fetch(url, 120, (d, e) => { data = d; error = e; });
            if (data == null || data.Length == 0)
            {
                State = Stage.Failed;
                Message = "не скачалось: " + (error ?? "пустой файл");
                yield break;
            }

            byte[] signature = null;
            yield return Fetch(Signatures[file], 30, (d, e) => { signature = d; error = e; });
            if (!Sign.Valid(data, signature))
            {
                State = Stage.Failed;
                Message = signature == null ? "не скачалась подпись: " + (error ?? "пустой файл") : "подпись файла не сошлась, обновление не поставлено";
                Plugin.Warn("[update] " + file + ": " + Message);
                yield break;
            }

            try
            {
                if (zip) InstallZip(data);
                else Install(data);
                State = Stage.Done;
                Message = "готово. Закрой клиент полностью и запусти заново, тогда встанет версия " + LatestVersion;
            }
            catch (Exception e)
            {
                State = Stage.Failed;
                Message = "не удалось заменить файл: " + e.Message;
                Plugin.Warn("[update] " + e);
            }
        }

        private const string PluginsPrefix = "BepInEx/plugins/";
        private const string FlashFolder = "NewAge2D/";

        private static void InstallZip(byte[] data)
        {
            byte[] main = null;
            var flash = new List<KeyValuePair<string, byte[]>>();
            using (var stream = new MemoryStream(data))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.Length == 0) continue;
                    string full = entry.FullName.Replace('\\', '/');
                    int at = full.IndexOf(PluginsPrefix, StringComparison.OrdinalIgnoreCase);
                    string inside = at >= 0 ? full.Substring(at + PluginsPrefix.Length) : full;
                    bool ours = entry.Name.Equals(DllName, StringComparison.OrdinalIgnoreCase)
                                && (at < 0 || inside.Equals(DllName, StringComparison.OrdinalIgnoreCase));
                    bool flat = at >= 0 && inside.StartsWith(FlashFolder, StringComparison.OrdinalIgnoreCase)
                                && inside.IndexOf("..", StringComparison.Ordinal) < 0;
                    if (!ours && !flat) continue;
                    if (ours && main != null) continue;
                    byte[] bytes;
                    using (var source = entry.Open())
                    using (var target = new MemoryStream())
                    {
                        source.CopyTo(target);
                        bytes = target.ToArray();
                    }
                    if (ours) main = bytes;
                    else flash.Add(new KeyValuePair<string, byte[]>(inside, bytes));
                }
            }
            if (main == null) throw new Exception("в архиве нет " + DllName);

            string root = BepInEx.Paths.PluginPath;
            var files = new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(Main(main), main) };
            foreach (var file in flash)
            {
                string path = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                if (Same(path, file.Value)) continue;
                files.Add(new KeyValuePair<string, byte[]>(path, file.Value));
            }
            Put(files);
            Plugin.Trace("[update] Flash view: " + (files.Count - 1) + " of " + flash.Count + " files replaced");
        }

        private static bool Same(string path, byte[] bytes)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length) return false;
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(path))
                {
                    byte[] a = sha.ComputeHash(bytes);
                    byte[] b = sha.ComputeHash(file);
                    for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                    return true;
                }
            }
            catch { return false; }
        }

        private static void Install(byte[] data)
        {
            Put(new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>(Main(data), data) });
        }

        private static string Main(byte[] data)
        {
            string path = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                try
                {
                    foreach (var found in Directory.GetFiles(BepInEx.Paths.PluginPath, DllName, SearchOption.AllDirectories))
                    {
                        path = found;
                        break;
                    }
                }
                catch (Exception e) { Plugin.Trace("[update] file lookup: " + e.Message); }
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new Exception("не нашёл файл мода на диске");
            if (data.Length < 20000) throw new Exception("файл подозрительно маленький");
            return path;
        }

        private static void Put(List<KeyValuePair<string, byte[]>> files)
        {
            var written = new List<string>();
            try
            {
                foreach (var file in files)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
                    string fresh = file.Key + ".new";
                    File.WriteAllBytes(fresh, file.Value);
                    written.Add(fresh);
                }
            }
            catch
            {
                foreach (var fresh in written) try { File.Delete(fresh); } catch { }
                throw;
            }
            foreach (var file in files) Commit(file.Key);
        }

        private static void Commit(string path)
        {
            string folder = Path.GetDirectoryName(path);
            string name = Path.GetFileName(path);
            string fresh = path + ".new";
            if (!File.Exists(path))
            {
                File.Move(fresh, path);
                return;
            }
            string old = Path.Combine(folder, name + ".old_" + DateTime.Now.ToString("MMdd_HHmmss"));
            Swap(path, fresh, old);
            Sweep(folder, name, old);
        }

        private static void Swap(string path, string fresh, string old)
        {
            try
            {
                File.Replace(fresh, path, old);
                return;
            }
            catch (Exception e) { Plugin.Trace("[update] atomic replace failed: " + e.Message); }

            File.Move(path, old);
            try { File.Move(fresh, path); }
            catch (Exception e)
            {
                Plugin.Fault("[update] new file did not install, restoring the previous one: " + e.Message);
                try { File.Move(old, path); }
                catch (Exception back) { Plugin.Fault("[update] could not restore the previous file: " + back.Message); }
                throw;
            }
        }

        private static void Sweep(string folder, string name, string keep)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, name + ".old_*"))
                {
                    if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        private static string Clean(string tag)
        {
            tag = (tag ?? "").Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
            return tag;
        }

        private static int Compare(string a, string b)
        {
            var left = Parts(a);
            var right = Parts(b);
            for (int i = 0; i < 4; i++)
            {
                if (left[i] != right[i]) return left[i] > right[i] ? 1 : -1;
            }
            return 0;
        }

        private static int[] Parts(string version)
        {
            var result = new int[4];
            var chunks = (version ?? "").Split('.');
            for (int i = 0; i < chunks.Length && i < 4; i++)
            {
                int value;
                var digits = "";
                foreach (var c in chunks[i]) { if (c >= '0' && c <= '9') digits += c; else break; }
                result[i] = int.TryParse(digits, out value) ? value : 0;
            }
            return result;
        }
    }
}
