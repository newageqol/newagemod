using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

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

        private static long _gotBytes, _totalBytes;
        private static List<Entry> _need;
        private static int _answer;
        private static bool _running;
        private static bool _done;
        private static Dictionary<string, string> _known;
        private static readonly Dictionary<string, string> Verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static bool Loading { get; private set; }

        internal static bool Declined { get; private set; }

        internal static bool Asking => _running && !_done;

        internal static bool Off(string group) =>
            !Plugin.NewPlaces || Declined && _need != null && _need.Any(e => e.Path.StartsWith(group + "/", StringComparison.OrdinalIgnoreCase));

        internal static float Progress => _totalBytes > 0 ? Mathf.Clamp01(_gotBytes / (float)_totalBytes) : 0f;

        internal static string Done => Mb(_gotBytes) + " из " + Mb(_totalBytes) + " МБ";

        internal static string Mb(long bytes) => Math.Max(1L, (bytes + 512L * 1024L) / (1024L * 1024L)).ToString();

        internal static void Answer(bool yes) => _answer = yes ? 1 : 2;

        internal static bool Pending => Declined && !_running && _need != null && _need.Count > 0;

        internal static string PendingSize => Mb(_need == null ? 0 : _need.Sum(e => e.Size));

        internal static void Retry()
        {
            if (!Pending || Plugin.Instance == null) return;
            _running = true;
            Plugin.Instance.StartCoroutine(Again());
        }

        private static IEnumerator Again()
        {
            bool ok = false;
            try
            {
                Plugin.Log.LogInfo("[assets] download started from the settings");
                _totalBytes = _need.Sum(e => e.Size);
                AssetGate.Show();
                yield return Get(r => ok = r);
            }
            finally
            {
                Loading = false;
                _running = false;
                foreach (var g in Staged()) Settle(g);
            }
            if (!ok) yield break;
            Declined = false;
            AssetGate.Close();
            try { Settings.Close(); }
            catch (Exception e) { Plugin.Trace("[assets] closing settings: " + e.Message); }
            Notice.Show("Новые версии старых локаций скачаны. Они появятся при следующем входе в локацию.", 6f);
        }

        private static IEnumerator Get(Action<bool> done)
        {
            Loading = true;
            _gotBytes = 0;
            AssetGate.Downloading();
            var left = new List<Entry>(_need);
            foreach (var e in left)
            {
                bool ok = false;
                long before = _gotBytes;
                yield return Fetch(e, r => ok = r, b => _gotBytes = before + b);
                _gotBytes = before + e.Size;
                if (!ok)
                {
                    Plugin.Log.LogInfo("[assets] stopped at " + e.Path + ", will continue later");
                    Declined = true;
                    AssetGate.Failed();
                    done(false);
                    yield break;
                }
                _need.Remove(e);
            }
            done(true);
        }

        internal static string Root => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";

        private static string Stage => Path.Combine(Root, ".assets");

        private static string StateFile => Path.Combine(Stage, "state.txt");

        internal static void Tick()
        {
            if (Plugin.Instance == null) return;
            if (!Loading) NightTown.Warm();
            if (!Loading) Locations.Warm();
            if (_running || _done) return;
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
                if (need.Count == 0) { finished = true; yield break; }
                Plugin.Log.LogInfo("[assets] " + need.Count + " files to download, " + total + " bytes, asking the player");
                _totalBytes = total;
                _gotBytes = 0;
                _answer = 0;
                _need = need;
                while (SideButtons.InCombat()) yield return new WaitForSecondsRealtime(1f);
                AssetGate.Ask(need.Count, total);
                while (_answer == 0) yield return null;
                if (_answer == 2)
                {
                    Declined = true;
                    AssetGate.Close();
                    Plugin.Log.LogInfo("[assets] player declined the download, mod locations are off this session");
                    yield break;
                }
                bool got = false;
                yield return Get(r => got = r);
                if (!got) yield break;
                finished = true;
                AssetGate.Close();
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

        private static IEnumerator Fetch(Entry e, Action<bool> done, Action<long> progress)
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
                    if (request.downloadedBytes != seen) { seen = request.downloadedBytes; quietSince = Time.unscaledTime; progress(have + (long)seen); }
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
    internal static class AssetGate
    {
        private static GameObject _root;
        private static Text _text;
        private static RectTransform _bar;
        private static GameObject _buttons, _barBox, _fail;
        private static bool _downloading;

        private static void Build()
        {
            if (_root != null) return;
            _root = new GameObject("QoLAssetGate", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var veil = new GameObject("veil", typeof(RectTransform), typeof(Image));
            veil.transform.SetParent(_root.transform, false);
            var vrt = (RectTransform)veil.transform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = vrt.offsetMax = Vector2.zero;
            veil.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

            var box = new GameObject("box", typeof(RectTransform), typeof(Image));
            box.transform.SetParent(_root.transform, false);
            var brt = (RectTransform)box.transform;
            brt.sizeDelta = new Vector2(720f, 270f);
            var bimg = box.GetComponent<Image>();
            bimg.color = WardrobeLook.Popup;
            bimg.sprite = OnlineWindow.Rounded(10);
            bimg.type = Image.Type.Sliced;

            _text = OnlineWindow.Label(box.transform, "", 18, FontStyle.Normal, WardrobeLook.Bright);
            _text.alignment = TextAnchor.UpperCenter;
            var trt = _text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(36f, 92f);
            trt.offsetMax = new Vector2(-36f, -30f);

            _buttons = new GameObject("buttons", typeof(RectTransform));
            _buttons.transform.SetParent(box.transform, false);
            Fill((RectTransform)_buttons.transform);
            var yes = OnlineWindow.MakeGameButton(_buttons.transform, "Скачать", 220f, 46f, () => AssetSync.Answer(true));
            var no = OnlineWindow.MakeGameButton(_buttons.transform, "Не сейчас", 220f, 46f, () => AssetSync.Answer(false));
            Place(yes.transform as RectTransform, -120f);
            Place(no.transform as RectTransform, 120f);

            _fail = new GameObject("fail", typeof(RectTransform));
            _fail.transform.SetParent(box.transform, false);
            Fill((RectTransform)_fail.transform);
            var go = OnlineWindow.MakeGameButton(_fail.transform, "Продолжить", 260f, 46f, Close);
            Place(go.transform as RectTransform, 0f);

            _barBox = new GameObject("bar", typeof(RectTransform), typeof(Image));
            _barBox.transform.SetParent(box.transform, false);
            var bb = (RectTransform)_barBox.transform;
            bb.anchorMin = bb.anchorMax = new Vector2(0.5f, 0f);
            bb.pivot = new Vector2(0.5f, 0f);
            bb.sizeDelta = new Vector2(520f, 18f);
            bb.anchoredPosition = new Vector2(0f, 40f);
            _barBox.GetComponent<Image>().color = WardrobeLook.Field;
            var fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(_barBox.transform, false);
            _bar = (RectTransform)fill.transform;
            _bar.anchorMin = new Vector2(0f, 0f);
            _bar.anchorMax = new Vector2(0f, 1f);
            _bar.pivot = new Vector2(0f, 0.5f);
            _bar.offsetMin = _bar.offsetMax = Vector2.zero;
            fill.GetComponent<Image>().color = new Color32(196, 156, 72, 255);
            _root.AddComponent<AssetGateTicker>();
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rt, float x)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le != null) rt.sizeDelta = new Vector2(le.preferredWidth, le.preferredHeight);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, 28f);
        }

        private static void Mode(bool buttons, bool bar, bool fail)
        {
            _buttons.SetActive(buttons);
            _barBox.SetActive(bar);
            _fail.SetActive(fail);
            _root.SetActive(true);
        }

        internal static void Ask(int files, long bytes)
        {
            try
            {
                Build();
                _downloading = false;
                _text.text = "Новые версии старых локаций: " + AssetSync.Mb(bytes) + " МБ.\nСкачать сейчас? Войти в игру можно будет после загрузки.\nБез них будут обычные карты игры.\nСкачать можно и позже, в настройках мода.";
                Mode(true, false, false);
            }
            catch (Exception e) { Plugin.Warn("[assets] dialog: " + e.Message); AssetSync.Answer(false); }
        }

        internal static void Show()
        {
            Build();
            _root.SetActive(true);
        }

        internal static void Downloading()
        {
            if (_root == null) return;
            _downloading = true;
            Mode(false, true, false);
            Tick();
        }

        internal static void Failed()
        {
            if (_root == null) return;
            _downloading = false;
            _text.text = "Не удалось скачать новые версии старых локаций.\nПока будут обычные карты игры.\nДокачать можно в настройках мода или при следующем запуске.";
            Mode(false, false, true);
        }

        internal static void Close()
        {
            _downloading = false;
            if (_root != null) _root.SetActive(false);
        }

        internal static void Tick()
        {
            if (_root != null)
            {
                var canvas = _root.GetComponent<Canvas>();
                bool fight = SideButtons.InCombat();
                if (canvas.enabled == fight) canvas.enabled = !fight;
            }
            if (!_downloading || _text == null) return;
            _text.text = "Загружаю новые версии старых локаций…\n" + AssetSync.Done + (SideButtons.InWorld() ? "\nОкно закроется само, когда загрузка закончится." : "\nВойти в игру можно будет после загрузки.");
            _bar.anchorMax = new Vector2(AssetSync.Progress, 1f);
        }
    }

    internal class AssetGateTicker : MonoBehaviour
    {
        private void Update() => AssetGate.Tick();
    }
}
