using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Reflection;
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


        internal static bool CanUpdate => State == Stage.Newer && (_dllUrl.Length > 0 || _zipUrl.Length > 0);

        internal static void CheckSilent()
        {
            if (State != Stage.Idle) return;
            Check();
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
                Plugin.Trace("[обновление] " + Message);
                yield break;
            }

            Release release = null;
            try { release = JsonUtility.FromJson<Release>(json); }
            catch (Exception e) { Plugin.Trace("[обновление] разбор ответа: " + e.Message); }

            string tag = release != null ? release.tag_name : null;
            _dllUrl = "";
            _zipUrl = "";
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
                Message = "вышла " + LatestVersion + ", но " + DllName + " в релизе нет";
            }
            else
            {
                State = Stage.Newer;
                Message = "вышла версия " + LatestVersion;
            }
            Plugin.Trace("[обновление] установлена " + Plugin.Version + ", на сервере " + LatestVersion);
        }

        private static readonly char[] Cuts = { '/', '\\' };

        private static void Remember(string name, string url)
        {
            if (Ours(name) && _dllUrl.Length == 0) _dllUrl = url;
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && _zipUrl.Length == 0) _zipUrl = url;
        }

        private static bool Ours(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string tail = name;
            int ask = tail.IndexOf('?');
            if (ask >= 0) tail = tail.Substring(0, ask);
            int cut = tail.LastIndexOfAny(Cuts);
            if (cut >= 0) tail = tail.Substring(cut + 1);
            return tail.Equals(DllName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator UpdateRoutine()
        {
            string url = _dllUrl.Length > 0 ? _dllUrl : _zipUrl;
            bool zip = _dllUrl.Length == 0;
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", "NewAgeQoL");
            request.timeout = 120;
            yield return request.SendWebRequest();

            string error = request.error;
            byte[] data = error == null || error.Length == 0 ? request.downloadHandler.data : null;
            request.Dispose();

            if (data == null || data.Length == 0)
            {
                State = Stage.Failed;
                Message = "не скачалось: " + (error ?? "пустой файл");
                yield break;
            }

            try
            {
                if (zip) data = FromZip(data);
                Install(data);
                State = Stage.Done;
                Message = "готово. Закрой клиент полностью и запусти заново, тогда встанет версия " + LatestVersion;
            }
            catch (Exception e)
            {
                State = Stage.Failed;
                Message = "не удалось заменить файл: " + e.Message;
                Plugin.Warn("[обновление] " + e);
            }
        }

        private static byte[] FromZip(byte[] data)
        {
            using (var stream = new MemoryStream(data))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.Name.Equals(DllName, StringComparison.OrdinalIgnoreCase)) continue;
                    using (var source = entry.Open())
                    using (var target = new MemoryStream())
                    {
                        source.CopyTo(target);
                        return target.ToArray();
                    }
                }
            }
            throw new Exception("в архиве нет " + DllName);
        }

        private static void Install(byte[] data)
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
                catch (Exception e) { Plugin.Trace("[обновление] поиск файла: " + e.Message); }
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new Exception("не нашёл файл мода на диске");
            if (data.Length < 20000) throw new Exception("файл подозрительно маленький");

            string folder = Path.GetDirectoryName(path);
            string fresh = Path.Combine(folder, DllName + ".new");
            File.WriteAllBytes(fresh, data);

            string old = Path.Combine(folder, DllName + ".old_" + DateTime.Now.ToString("MMdd_HHmmss"));
            Swap(path, fresh, old);
            Sweep(folder, old);
        }

        private static void Swap(string path, string fresh, string old)
        {
            try
            {
                File.Replace(fresh, path, old);
                return;
            }
            catch (Exception e) { Plugin.Trace("[обновление] заменить одним действием не вышло: " + e.Message); }

            File.Move(path, old);
            try { File.Move(fresh, path); }
            catch (Exception e)
            {
                Plugin.Fault("[обновление] новый файл не встал, возвращаю прежний: " + e.Message);
                try { File.Move(old, path); }
                catch (Exception back) { Plugin.Fault("[обновление] прежний файл вернуть не вышло: " + back.Message); }
                throw;
            }
        }

        private static void Sweep(string folder, string keep)
        {
            try
            {
                foreach (var file in Directory.GetFiles(folder, DllName + ".old_*"))
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
