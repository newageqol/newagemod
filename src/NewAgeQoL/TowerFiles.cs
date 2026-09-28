using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class TowerFiles
    {
        private const string Api = "https://api.github.com/repos/newageqol/newagemod/releases/tags/magictower";
        private const string VersionFile = "version.txt";
        private const string ReadyFile = "ready.txt";
        private static readonly string[] Names = { "tower_magic", "tower_magic.txt", "tower_magic_fx" };

        [Serializable]
        private class Asset
        {
            public string name = null;
            public string browser_download_url = null;
            public long size = 0;
        }

        [Serializable]
        private class Release
        {
            public Asset[] assets = null;
        }

        private static float _checkAt = 55f;
        private static bool _busy;
        private static bool _loading;
        private static bool _ask;
        private static string _remote = "";
        private static Dictionary<string, Asset> _assets;
        private static GameObject _canvasGo;
        private static Text _text;
        private static float _shownAt;

        internal static NightTheme.Pack Last;

        internal static bool Busy => _busy || _loading;

        internal static bool Missing => Local().Length == 0;

        internal static bool Behind => _remote.Length > 0 && Local().Length > 0 && _remote != Local();

        internal static long Need => Size();

        internal static IEnumerator Probe()
        {
            _ask = false;
            _checkAt = 0f;
            _busy = true;
            yield return Run(false, true);
        }

        internal static void Seen()
        {
            if (MagicTower.Asked != null && _remote.Length > 0) MagicTower.Asked.Value = _remote;
        }

        internal static IEnumerator Fetch()
        {
            if (MagicTower.Asked != null) MagicTower.Asked.Value = _remote;
            _busy = true;
            yield return Download();
        }

        private static string Spare => Path.Combine(MagicTower.Folder, ".download");

        internal static void Tick()
        {
            if (Plugin.Instance == null) return;
            if (_ask && !_busy && SideButtons.InWorld() && !SideButtons.InCombat()) { _ask = false; Ask(); return; }
            if (_busy || _checkAt <= 0f || Time.unscaledTime < _checkAt) return;
            if (MagicTower.Enabled == null || !MagicTower.Enabled.Value) return;
            _checkAt = 0f;
            _busy = true;
            Plugin.Instance.StartCoroutine(Run(false, false));
        }

        internal static void Manual()
        {
            if (Plugin.Instance == null) return;
            if (_loading) { Notice.Show("Башня магии уже качается, прогресс в левом нижнем углу.", 4f); return; }
            if (_busy) { Notice.Show("Проверяю башню магии, подожди немного.", 3f); return; }
            _ask = false;
            _checkAt = 0f;
            _busy = true;
            Plugin.Instance.StartCoroutine(Run(true, false));
        }

        private static void Ask()
        {
            try
            {
                string version = _remote;
                if (MagicTower.Asked != null) MagicTower.Asked.Value = version;
                string text = (Local().Length == 0 ? "Для мода есть 3D-башня магии вместо картинки" : "Вышло обновление 3D-башни магии")
                    + " (" + Mb(Size()) + " МБ). Скачать сейчас? Загрузка идёт в фоне и не мешает игре, в бою встаёт на паузу. "
                    + "Если откажешься, скачать можно позже кнопкой в настройках мода.";
                DialogFactory.ShowConfirmMessageBox("dialogs.artworkshop.confirm.caption", result =>
                {
                    if (result != EMessageBoxResult.MB_OK) { Plugin.Trace("[tower] player declined tower version " + version); return; }
                    if (_busy || Plugin.Instance == null) return;
                    _busy = true;
                    Plugin.Instance.StartCoroutine(Download());
                }, text);
            }
            catch (Exception e) { Plugin.Trace("[tower] ask dialog: " + e.Message); }
        }

        internal static string Local()
        {
            try
            {
                var path = Path.Combine(MagicTower.Folder, VersionFile);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
            catch { return ""; }
        }

        private static long Size()
        {
            long total = 0;
            if (_assets != null) foreach (var name in Names) if (_assets.TryGetValue(name, out var a)) total += a.size;
            return total;
        }

        internal static bool Settle()
        {
            try
            {
                var ready = Path.Combine(Spare, ReadyFile);
                if (!File.Exists(ready)) return false;
                if (MagicTower.SceneLoaded()) return false;
                MagicTower.Unload();
                foreach (var name in Names)
                {
                    var from = Path.Combine(Spare, name);
                    if (!File.Exists(from)) continue;
                    var to = Path.Combine(MagicTower.Folder, name);
                    if (File.Exists(to)) File.Delete(to);
                    File.Move(from, to);
                }
                var version = File.ReadAllText(ready).Trim();
                File.WriteAllText(Path.Combine(MagicTower.Folder, VersionFile), version);
                Directory.Delete(Spare, true);
                Plugin.Trace("[tower] tower files installed, version " + version);
                return true;
            }
            catch (Exception e) { Plugin.Warn("[tower] installing tower files: " + e.Message); return false; }
        }

        private static IEnumerator Run(bool manual, bool probe)
        {
            bool fetch = false;
            Last = NightTheme.Pack.Failed;
            try
            {
                var release = default(Release);
                var request = UnityWebRequest.Get(Api);
                request.SetRequestHeader("User-Agent", "NewAgeQoL");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.timeout = 20;
                yield return request.SendWebRequest();
                string error = request.error;
                string json = string.IsNullOrEmpty(error) ? request.downloadHandler.text : "";
                request.Dispose();
                if (json.Length == 0) { Say(manual, "Не удалось проверить башню магии: " + (error ?? "нет ответа")); Plugin.Trace("[tower] release check failed: " + (error ?? "no answer")); yield break; }
                try { release = JsonUtility.FromJson<Release>(json); }
                catch (Exception e) { Plugin.Trace("[tower] parsing release: " + e.Message); }
                if (release == null || release.assets == null) { Say(manual, "Не удалось разобрать ответ сервера."); yield break; }

                var assets = new Dictionary<string, Asset>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in release.assets)
                    if (a != null && !string.IsNullOrEmpty(a.name) && !string.IsNullOrEmpty(a.browser_download_url)) assets[a.name] = a;
                bool complete = assets.ContainsKey(VersionFile);
                foreach (var name in Names) if (!assets.ContainsKey(name)) complete = false;
                if (!complete) { Say(manual, "На сервере пока нет файлов башни магии."); Plugin.Trace("[tower] release is missing files"); yield break; }

                request = UnityWebRequest.Get(assets[VersionFile].browser_download_url);
                request.SetRequestHeader("User-Agent", "NewAgeQoL");
                request.timeout = 20;
                yield return request.SendWebRequest();
                string remote = string.IsNullOrEmpty(request.error) ? (request.downloadHandler.text ?? "").Trim() : "";
                request.Dispose();
                if (remote.Length == 0 || !Regex.IsMatch(remote, "^[0-9A-Za-z._-]{1,32}$")) { Say(manual, "Не удалось узнать версию башни магии."); Plugin.Trace("[tower] bad remote version"); yield break; }
                _assets = assets;
                _remote = remote;

                Settle();
                bool whole = true;
                foreach (var name in Names)
                {
                    var path = Path.Combine(MagicTower.Folder, name);
                    if (!File.Exists(path) || new FileInfo(path).Length != assets[name].size) { whole = false; break; }
                }
                bool waiting = File.Exists(Path.Combine(Spare, ReadyFile));
                if ((whole && Local() == remote) || waiting)
                {
                    Last = NightTheme.Pack.Current;
                    if (probe) yield break;
                    Plugin.Trace("[tower] tower files are current, version " + remote);
                    Say(manual, waiting ? "Башня магии уже скачана, появится при следующем входе в башню." : "У тебя последняя версия башни магии.");
                    yield break;
                }
                Plugin.Trace("[tower] tower version " + remote + " available, local " + (Local().Length > 0 ? Local() : "none"));
                Last = Local().Length == 0 ? NightTheme.Pack.Missing : NightTheme.Pack.Outdated;
                if (probe) yield break;
                var agreed = Path.Combine(Spare, VersionFile);
                if (manual) fetch = true;
                else if (File.Exists(agreed) && File.ReadAllText(agreed).Trim() == remote) { fetch = true; Plugin.Trace("[tower] resuming agreed download"); }
                else if (MagicTower.Asked == null || MagicTower.Asked.Value != remote) _ask = true;
            }
            finally
            {
                _busy = fetch;
                if (fetch) Plugin.Instance.StartCoroutine(Download());
            }
        }

        private static void Say(bool manual, string text)
        {
            if (manual) Notice.Show(text, 5f);
        }

        private static IEnumerator Download()
        {
            bool ok = false;
            _loading = true;
            try
            {
                if (_assets == null || _remote.Length == 0) yield break;
                string remote = _remote;
                var assets = _assets;
                Directory.CreateDirectory(Spare);
                var stale = Path.Combine(Spare, VersionFile);
                if (!File.Exists(stale) || File.ReadAllText(stale).Trim() != remote)
                {
                    foreach (var f in Directory.GetFiles(Spare)) File.Delete(f);
                    File.WriteAllText(stale, remote);
                }

                long total = Size();
                long before = 0;
                Plugin.Trace("[tower] downloading tower version " + remote + ", " + total + " bytes");
                foreach (var name in Names)
                {
                    var asset = assets[name];
                    var part = Path.Combine(Spare, name);
                    yield return Fetch(asset, part, before, total);
                    if (!File.Exists(part) || new FileInfo(part).Length != asset.size)
                    {
                        Plugin.Trace("[tower] download of " + name + " is incomplete");
                        yield break;
                    }
                    before += asset.size;
                }
                File.WriteAllText(Path.Combine(Spare, ReadyFile), remote);
                ok = true;
                Settle();
                Show(null);
                Notice.Show("Башня магии скачана. Появится при следующем входе в башню.", 6f);
            }
            finally
            {
                _loading = false;
                _busy = false;
                if (!ok)
                {
                    Show(null);
                    Notice.Show("Не удалось скачать башню магии. Докачать можно кнопкой в настройках мода.", 6f);
                }
            }
        }

        private static IEnumerator Fetch(Asset asset, string part, long before, long total)
        {
            while (true)
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > asset.size) { File.Delete(part); have = 0; }
                if (have == asset.size) yield break;

                while (SideButtons.InCombat())
                {
                    Show("Башня магии: загрузка ждёт конца боя (" + Percent(before + have, total) + ")");
                    yield return new WaitForSecondsRealtime(2f);
                }

                var request = new UnityWebRequest(asset.browser_download_url, UnityWebRequest.kHttpVerbGET);
                request.downloadHandler = new DownloadHandlerFile(part, have > 0) { removeFileOnAbort = false };
                request.SetRequestHeader("User-Agent", "NewAgeQoL");
                if (have > 0) request.SetRequestHeader("Range", "bytes=" + have + "-");
                var op = request.SendWebRequest();
                bool paused = false, restart = false;
                while (!op.isDone)
                {
                    if (have > 0 && request.responseCode == 200) { request.Abort(); restart = true; break; }
                    if (SideButtons.InCombat()) { request.Abort(); paused = true; break; }
                    Show("Башня магии: качаю " + Percent(before + have + (long)request.downloadedBytes, total)
                        + " (" + Mb(before + have + (long)request.downloadedBytes) + " из " + Mb(total) + " МБ)");
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                string error = request.error;
                long code = request.responseCode;
                request.Dispose();
                if (restart)
                {
                    Plugin.Trace("[tower] server ignored the range, restarting " + asset.name);
                    try { File.Delete(part); } catch { }
                    continue;
                }
                if (paused) { Plugin.Trace("[tower] download paused for combat"); continue; }
                if (!string.IsNullOrEmpty(error) || (code != 200 && code != 206))
                {
                    Plugin.Trace("[tower] downloading " + asset.name + ": " + (error ?? "code " + code));
                    yield break;
                }
            }
        }

        private static string Percent(long done, long total) =>
            (total > 0 ? Mathf.Clamp((int)(done * 100 / total), 0, 100) : 0) + "%";

        private static string Mb(long bytes) => (bytes / (1024 * 1024)).ToString();

        private static void Show(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    if (_canvasGo != null) _canvasGo.SetActive(false);
                    return;
                }
                if (_text != null && _text.text == text && _canvasGo.activeSelf) return;
                if (Time.unscaledTime - _shownAt < 0.5f && _canvasGo != null && _canvasGo.activeSelf) return;
                _shownAt = Time.unscaledTime;
                Build();
                if (_text == null) return;
                _text.text = text;
                _canvasGo.SetActive(true);
            }
            catch (Exception e) { Plugin.Trace("[tower] progress: " + e.Message); }
        }

        private static void Build()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("QoLTowerDownload", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 890;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);
            var veil = _canvasGo.GetComponent<CanvasGroup>();
            veil.blocksRaycasts = false;
            veil.interactable = false;
            veil.alpha = 0.85f;

            var panelGo = new GameObject("plate", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            panelGo.transform.SetParent(_canvasGo.transform, false);
            var prt = (RectTransform)panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0f);
            prt.pivot = new Vector2(0f, 0f);
            prt.anchoredPosition = new Vector2(12f, 12f);

            var back = panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;

            var box = panelGo.GetComponent<HorizontalLayoutGroup>();
            box.padding = new RectOffset(10, 10, 5, 6);
            box.childControlWidth = true;
            box.childControlHeight = true;
            box.childForceExpandWidth = false;
            box.childForceExpandHeight = false;

            var fit = panelGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _text = OnlineWindow.Label(panelGo.transform, "", 14, FontStyle.Normal, WardrobeLook.Bright);
            _text.alignment = TextAnchor.MiddleLeft;
            _text.raycastTarget = false;
            _canvasGo.SetActive(false);
        }
    }
}
