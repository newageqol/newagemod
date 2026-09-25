using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Report
    {
        private const float PanelW = 660f;
        private const float HeadH = 98f;
        private const float FieldH = 230f;
        private const float LineH = 20f;
        private const float TopH = 34f;
        private const float ButtonW = 176f;
        private const float ButtonH = 34f;
        private const int MostFiles = 12;
        private const int OneFileMost = 20 * 1024 * 1024;
        private const int Room = 44 * 1024 * 1024;
        private const int Least = 10;
        private const string BakedUrl = "https://newagemod.outerlab.org/report";
        private const string BakedKey = "";

        private sealed class Item
        {
            internal string Name;
            internal byte[] Data;
        }

        private sealed class Bundle
        {
            internal byte[] Zip;
            internal string File;
            internal string Trouble;
            internal bool Saved;
        }

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static RectTransform _panel;
        private static RectTransform _fieldRt;
        private static RectTransform _listRt;
        private static RectTransform _rowRt;
        private static RectTransform _stateRt;
        private static InputField _told;
        private static Text _state;
        private static ConfigEntry<string> _url;
        private static ConfigEntry<string> _key;
        private static readonly List<Item> Files = new List<Item>();
        private static bool _busy;
        private static int _era;

        private static void Bind()
        {
            var home = Plugin.Instance != null ? Plugin.Instance.Config : null;
            if (home == null) return;
            if (_url == null)
                _url = home.Bind("Report", "Url", BakedUrl,
                    "Куда уходит отчёт об ошибке: адрес приёмника. Пусто — отчёт только складывается в папку, отправлять его нужно самому.");
            if (_key == null)
                _key = home.Bind("Report", "Key", BakedKey,
                    "Общий ключ приёмника, чтобы туда не слал кто попало.");
        }

        internal static void Open()
        {
            if (_busy)
            {
                try { Notice.Show("Отчёт ещё уходит, подожди пару секунд.", 5f); } catch { }
                return;
            }
            if (_panelGo != null) { Close(); return; }
            Files.Clear();
            Bind();
            try { Settings.Close(); } catch { }
            try
            {
                var setup = UnityEngine.Object.FindObjectOfType<SetupDialog>();
                if (setup != null) setup.Close();
            }
            catch (Exception e) { Plugin.Trace("[отчёт] окно настроек игры: " + e.Message); }
            try
            {
                Build();
                Say("Опиши, что случилось. Журналы мода лягут в отчёт сами.");
            }
            catch (Exception e) { Plugin.Warn("[отчёт] окно: " + e.Message); Close(); }
        }

        internal static bool EscapeClose()
        {
            if (_panelGo == null) return false;
            Close();
            return true;
        }

        internal static void Close()
        {
            _era++;
            Files.Clear();
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); }
            _canvasGo = null;
            _panelGo = null;
            _panel = null;
            _fieldRt = null;
            _listRt = null;
            _rowRt = null;
            _stateRt = null;
            _told = null;
            _state = null;
        }

        private static void Focus()
        {
            if (_told == null) return;
            try
            {
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_told.gameObject);
                _told.ActivateInputField();
                _told.caretPosition = _told.text != null ? _told.text.Length : 0;
            }
            catch (Exception e) { Plugin.Trace("[отчёт] поле ввода: " + e.Message); }
        }

        internal static void Paste()
        {
            if (_busy || _panelGo == null) return;
            try
            {
                var got = Clipboard.Take();
                if (got == null || got.Count == 0) { Say("В буфере нет ни картинки, ни файла."); return; }
                foreach (var one in got)
                {
                    if (one.Data != null) Add(one.Name, one.Data);
                    else AddFile(one.Path);
                }
                Marks();
            }
            catch (Exception e) { Plugin.Warn("[отчёт] буфер: " + e.Message); Say("Из буфера взять не вышло: " + e.Message); }
        }

        internal static void Choose()
        {
            if (_busy || _panelGo == null) return;
            try
            {
                var paths = Clipboard.Ask();
                if (paths == null || paths.Count == 0) { Focus(); return; }
                foreach (var path in paths) AddFile(path);
                Marks();
            }
            catch (Exception e) { Plugin.Warn("[отчёт] выбор файла: " + e.Message); Say("Файл взять не вышло: " + e.Message); }
            Focus();
        }

        private static readonly string[] Locked = { ".env", ".cfg", ".pem", ".key", ".ppk" };

        private static readonly string[] LockedNames = { "account.local.json", "password.txt", "id_rsa", "device.id" };

        private static bool Secret(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string low = name.ToLowerInvariant();
            foreach (var one in LockedNames) if (low == one) return true;
            foreach (var one in Locked) if (low.EndsWith(one, StringComparison.Ordinal)) return true;
            return low.Contains("password") || low.Contains("passwd") || low.Contains("secret")
                || low.Contains("token") || low.Contains("пароль");
        }

        private static void AddFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                string name = Path.GetFileName(path);
                if (Files.Count >= MostFiles) { Say("Больше " + MostFiles + " вложений не кладу."); return; }
                if (Secret(name)) { Say("Такое не прикладываю, там могут быть учётные данные: " + name); return; }
                var about = new FileInfo(path);
                if (!about.Exists) { Say("Файла нет: " + name); return; }
                if (about.Length > OneFileMost) { Say("Файл больше " + (OneFileMost / 1024 / 1024) + " МБ не кладу: " + name); return; }
                var data = Read(path, OneFileMost);
                if (data == null) { Say("Прочитать не вышло: " + name); return; }
                Add(name, data);
            }
            catch (Exception e) { Plugin.Warn("[отчёт] вложение: " + e.Message); Say("Файл взять не вышло: " + e.Message); }
        }

        private static byte[] Read(string path, int cap)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (file.Length > cap || file.Length <= 0) return null;
                    var data = new byte[file.Length];
                    int done = 0;
                    while (done < data.Length)
                    {
                        int got = file.Read(data, done, data.Length - done);
                        if (got <= 0) break;
                        done += got;
                    }
                    if (done == data.Length) return data;
                    var cut = new byte[done];
                    Buffer.BlockCopy(data, 0, cut, 0, done);
                    return cut;
                }
            }
            catch (Exception e) { Plugin.Trace("[отчёт] чтение " + path + ": " + e.Message); return null; }
        }

        private static void Add(string name, byte[] data)
        {
            if (data == null || data.Length == 0) { Say("Пустой файл не прикладываю."); return; }
            if (Files.Count >= MostFiles) { Say("Больше " + MostFiles + " вложений не кладу."); return; }
            if (Secret(name)) { Say("Такое не прикладываю, там могут быть учётные данные: " + name); return; }
            if (data.Length > OneFileMost) { Say("Файл больше " + (OneFileMost / 1024 / 1024) + " МБ не кладу: " + name); return; }
            string plain = Plain(name);
            int same = 1;
            string want = plain;
            while (Has(want)) { same++; want = same + "-" + plain; }
            Files.Add(new Item { Name = want, Data = data });
            Say("Приложил: " + want + " (" + Size(data.Length) + ")");
        }

        private static bool Has(string name)
        {
            foreach (var one in Files)
                if (string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Plain(string name)
        {
            if (string.IsNullOrEmpty(name)) return "вложение";
            var box = new StringBuilder();
            foreach (char c in name)
                box.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return box.ToString();
        }

        private static string Size(long bytes)
        {
            if (bytes >= 1024L * 1024L) return (bytes / 1024f / 1024f).ToString("0.0") + " МБ";
            return Math.Max(1L, bytes / 1024L) + " КБ";
        }

        private static void Go()
        {
            var host = Plugin.Instance;
            if (host == null || _busy) return;
            _busy = true;
            try { host.StartCoroutine(Work()); }
            catch (Exception e) { _busy = false; Plugin.Warn("[отчёт] сбор: " + e.Message); }
        }

        private static IEnumerator Work()
        {
            int era = _era;
            try
            {
                string told = _told != null ? _told.text.Trim() : "";
                if (told.Length < Least)
                {
                    Say("Напиши хотя бы пару слов, что случилось. Щёлкни по полю и набери текст.");
                    Focus();
                    yield break;
                }

                Say("Собираю отчёт…");
                yield return null;
                if (era != _era) yield break;
                var box = Pack(told);
                if (box.Zip == null)
                {
                    Say("Отчёт собрать не вышло: " + (box.Trouble ?? "неизвестно почему"));
                    yield break;
                }
                Plugin.Log?.LogInfo("[отчёт] собран " + (box.File ?? "без файла") + ", " + (box.Zip.Length / 1024)
                                    + " КБ, вложений " + Files.Count + ", записан " + (box.Saved ? "да" : "НЕТ"));

                Bind();
                string url = _url != null && _url.Value != null ? _url.Value.Trim() : "";
                string key = _key != null && _key.Value != null ? _key.Value.Trim() : "";
                if (url.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Keep(box, "Отправлять некуда: приёмник не настроен. Отчёт лежит в папке, пришли файл сам.");
                    yield break;
                }
                if (box.Zip.Length > Room)
                {
                    Keep(box, "Отчёт вышел большой (" + Size(box.Zip.Length) + "), сам он не уйдёт. Пришли файл из папки.");
                    yield break;
                }

                Say("Отправляю " + Size(box.Zip.Length) + "…");
                var web = Ready(url, key, told, box);
                if (web == null) { Keep(box, "Отправку начать не вышло. Отчёт лежит в папке, пришли файл сам."); yield break; }
                using (web)
                {
                    yield return web.SendWebRequest();
                    string answer = web.downloadHandler != null ? web.downloadHandler.text ?? "" : "";
                    bool taken = web.responseCode >= 200 && web.responseCode < 300 && answer.Replace(" ", "").Contains("\"ok\":true");
                    if (taken)
                    {
                        Plugin.Log?.LogInfo("[отчёт] отправлен, ответ " + web.responseCode);
                        Say("Отчёт ушёл. Спасибо!");
                        Thanks();
                        yield return new WaitForSecondsRealtime(1.2f);
                        if (era != _era) yield break;
                        Files.Clear();
                        Close();
                        yield break;
                    }
                    string where = web.GetResponseHeader("Location") ?? "";
                    Plugin.Warn("[отчёт] не ушёл: код " + web.responseCode + ", " + (web.error ?? "")
                                + (where.Length > 0 ? ", переадресация на " + (where.Length > 80 ? where.Substring(0, 80) : where) : "")
                                + (answer.Length > 0 ? ", ответ " + (answer.Length > 120 ? answer.Substring(0, 120) : answer).Replace((char)10, ' ') : ""));
                    string why = web.responseCode >= 300 && web.responseCode < 400 ? "приёмник закрыт переадресацией"
                        : web.responseCode >= 200 && web.responseCode < 300 ? "приёмник ответил не то" : web.responseCode.ToString();
                    Keep(box, "Отправить не вышло (" + why + "). Отчёт лежит в папке, пришли файл сам.");
                }
            }
            finally { _busy = false; }
        }

        private static void Thanks()
        {
            try { Notice.Show("Отчёт отправлен. Спасибо!", 6f); } catch { }
            if (_told != null) _told.text = "";
        }

        private static UnityWebRequest Ready(string url, string key, string told, Bundle box)
        {
            try
            {
                var form = new WWWForm();
                form.AddField("who", Login(), Encoding.UTF8);
                form.AddField("mod", Badge(), Encoding.UTF8);
                form.AddField("text", Short(told), Encoding.UTF8);
                form.AddBinaryData("document", box.Zip, string.IsNullOrEmpty(box.File) ? "report.zip" : Path.GetFileName(box.File), "application/zip");
                var web = UnityWebRequest.Post(url, form);
                web.timeout = 300;
                web.redirectLimit = 0;
                if (key.Length > 0) web.SetRequestHeader("X-Report-Key", key);
                return web;
            }
            catch (Exception e) { Plugin.Warn("[отчёт] запрос не собрался: " + e.Message); return null; }
        }

        private static string Short(string told)
        {
            string tail = told.Replace("\r", " ").Replace("\n", " ");
            return tail.Length > 700 ? tail.Substring(0, 700) + "…" : tail;
        }

        private static void Keep(Bundle box, string text)
        {
            if (!box.Saved)
            {
                Say("Отчёт на диск не записался: " + (box.Trouble ?? "неизвестно почему"));
                return;
            }
            Say(text);
            try
            {
                string dir = Path.GetDirectoryName(box.File);
                if (!string.IsNullOrEmpty(dir)) Application.OpenURL("file:///" + dir.Replace("\\", "/"));
            }
            catch (Exception e) { Plugin.Trace("[отчёт] папка: " + e.Message); }
        }

        private static Bundle Pack(string told)
        {
            var box = new Bundle();
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true, Encoding.UTF8))
                    {
                        Put(zip, "описание.txt", Encoding.UTF8.GetBytes(told + "\r\n\r\n" + Facts()));
                        foreach (var one in Files) Put(zip, "вложения/" + one.Name, one.Data);
                        Logs(zip);
                    }
                    box.Zip = ms.ToArray();
                }
            }
            catch (Exception e)
            {
                box.Trouble = e.Message;
                Plugin.Warn("[отчёт] сборка: " + e);
                return box;
            }

            try
            {
                string dir = Path.Combine(DiskJournal.Folder, "reports");
                Directory.CreateDirectory(dir);
                box.File = Path.Combine(dir, "report_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".zip");
                File.WriteAllBytes(box.File, box.Zip);
                box.Saved = true;
            }
            catch (Exception e)
            {
                box.Trouble = e.Message;
                Plugin.Warn("[отчёт] запись файла: " + e.Message);
            }
            return box;
        }

        private static void Logs(ZipArchive zip)
        {
            string flash = Path.Combine(Paths.CachePath, "NewAge2D");
            Tail(zip, Path.Combine(DiskJournal.Folder, "qol.log"), "мод/qol.log", 4000000);
            Tail(zip, Path.Combine(DiskJournal.Folder, "qol.old.log"), "мод/qol.old.log", 1500000);
            Tail(zip, RouteLog.File, "мод/routes.log", 8000000);
            Maps(zip);
            Tail(zip, Path.Combine(flash, "plugin.log"), "flash/plugin.log", 2000000);
            Tail(zip, Path.Combine(flash, "plugin.log.old"), "flash/plugin.log.old", 1000000);
            foreach (var path in Latest(Path.Combine(flash, "trace"), "*.log", 3))
                Tail(zip, path, "бои/" + Path.GetFileName(path), 3000000);
            foreach (var path in Latest(Path.Combine(Paths.ConfigPath, "NewAgeQoL-replays"), "*.natape", 3))
                Whole(zip, path, "записи-боёв/" + Path.GetFileName(path), 8000000);
            Tail(zip, Path.Combine(Paths.BepInExRootPath, "LogOutput.log"), "игра/LogOutput.log", 2000000);
            string player = Application.consoleLogPath;
            Tail(zip, player, "игра/Player.log", 2000000);
            string near = Path.GetDirectoryName(player);
            if (!string.IsNullOrEmpty(near)) Tail(zip, Path.Combine(near, "Player-prev.log"), "игра/Player-prev.log", 1000000);
        }

        private static void Maps(ZipArchive zip)
        {
            try
            {
                if (!Directory.Exists(MapDump.Folder)) return;
                long left = 6000000;
                foreach (var path in Directory.GetFiles(MapDump.Folder, "*.xml"))
                {
                    long size = new FileInfo(path).Length;
                    if (size > left) continue;
                    left -= size;
                    Whole(zip, path, "карты/" + Path.GetFileName(path), (int)size + 1);
                }
            }
            catch (Exception e) { Plugin.Trace("[отчёт] карты: " + e.Message); }
        }

        private static void Put(ZipArchive zip, string name, byte[] data)
        {
            Put(zip, name, data, data != null ? data.Length : 0);
        }

        private static void Put(ZipArchive zip, string name, byte[] data, int count)
        {
            if (data == null || count <= 0) return;
            var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
            using (var into = entry.Open()) into.Write(data, 0, count);
        }

        private static void Whole(ZipArchive zip, string path, string name, int cap)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                long high = new FileInfo(path).Length;
                if (high > cap)
                {
                    Plugin.Trace("[отчёт] " + Path.GetFileName(path) + " на " + Size(high) + " — целиком не влезает, обрезать её нельзя, пропускаю");
                    return;
                }
                var data = Read(path, cap);
                if (data == null) return;
                Put(zip, name, data);
            }
            catch (Exception e) { Plugin.Trace("[отчёт] файл " + name + ": " + e.Message); }
        }

        private static void Tail(ZipArchive zip, string path, string name, long cap)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long from = Math.Max(0L, file.Length - cap);
                    file.Seek(from, SeekOrigin.Begin);
                    var buf = new byte[file.Length - from];
                    int got = file.Read(buf, 0, buf.Length);
                    Put(zip, name, buf, got);
                }
            }
            catch (Exception e) { Plugin.Trace("[отчёт] журнал " + name + ": " + e.Message); }
        }

        private static List<string> Latest(string dir, string mask, int count)
        {
            var got = new List<string>();
            try
            {
                if (!Directory.Exists(dir)) return got;
                var all = new List<string>(Directory.GetFiles(dir, mask));
                all.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
                for (int i = 0; i < all.Count && i < count; i++) got.Add(all[i]);
            }
            catch (Exception e) { Plugin.Trace("[отчёт] папка " + dir + ": " + e.Message); }
            return got;
        }

        private static string Login()
        {
            try { return Controllers.User?.UserInfo?.Login ?? ""; }
            catch { return ""; }
        }

        private static string Badge()
        {
            string mod = Plugin.Instance != null ? Plugin.Instance.Info.Metadata.Name : "New Age QoL";
            return mod + " " + Plugin.Version;
        }

        private static string Who()
        {
            string login = Login();
            return (login.Length > 0 ? login + " — " : "") + Badge();
        }

        private static string Facts()
        {
            var box = new StringBuilder();
            try
            {
                box.Append("мод: ").Append(Who()).Append("\r\n");
                box.Append("игра: ").Append(Application.version).Append(", Unity ").Append(Application.unityVersion).Append("\r\n");
                box.Append("экран: ").Append(Screen.width).Append('×').Append(Screen.height)
                   .Append(Screen.fullScreen ? ", во весь экран" : ", в окне").Append("\r\n");
                box.Append("время: ").Append(DateTime.UtcNow.ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture))
                   .Append(" UTC, по игре ").Append(DateTime.UtcNow.AddHours(3).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
                try
                {
                    var user = Controllers.User;
                    if (user != null)
                        box.Append("персонаж: ").Append(user.UserInfo != null ? user.UserInfo.Login : "?")
                           .Append(" (").Append(user.UserInfo != null ? user.UserInfo.UserId : 0).Append(')')
                           .Append(", локация ").Append(user.CurrentLocationId).Append(", карта ").Append(user.MapId).Append("\r\n");
                }
                catch { }
                box.Append("в бою: ").Append(SideButtons.InCombat() ? "да" : "нет").Append("\r\n");
                box.Append("вложений: ").Append(Files.Count).Append("\r\n");
                box.Append("плагины: ").Append(Plugins()).Append("\r\n");
                box.Append("\r\nНастройки и пароли в отчёт не кладутся.\r\n");
            }
            catch (Exception e) { box.Append("сведения собрать не вышло: ").Append(e.Message); }
            return box.ToString();
        }

        private static string Plugins()
        {
            try
            {
                var names = new List<string>();
                foreach (var path in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
                    names.Add(Path.GetFileName(path));
                names.Sort(StringComparer.OrdinalIgnoreCase);
                return string.Join(", ", names.ToArray());
            }
            catch { return "?"; }
        }

        private static void Say(string text)
        {
            if (_state != null) _state.text = text;
            Plugin.Trace("[отчёт] " + text);
        }

        private static void Marks()
        {
            if (_listRt == null) return;
            for (int i = _listRt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_listRt.GetChild(i).gameObject);

            if (Files.Count == 0)
            {
                var none = OnlineWindow.Label(_listRt, "вложений нет — картинку можно вставить по Ctrl+V", 12, FontStyle.Normal,
                    WardrobeLook.Faint);
                OnlineWindow.Place(none.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -LineH), Vector2.zero);
                none.alignment = TextAnchor.MiddleLeft;
                none.raycastTarget = false;
            }
            else for (int i = 0; i < Files.Count; i++) Line(i);

            Lay();
        }

        private static void Line(int at)
        {
            var one = Files[at];
            var go = new GameObject("file", typeof(RectTransform));
            go.transform.SetParent(_listRt, false);
            OnlineWindow.Place((RectTransform)go.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(at + 1) * LineH), new Vector2(0f, -at * LineH));

            var killGo = new GameObject("kill", typeof(RectTransform), typeof(Image), typeof(Button));
            killGo.transform.SetParent(go.transform, false);
            var krt = (RectTransform)killGo.transform;
            krt.anchorMin = krt.anchorMax = new Vector2(0f, 0.5f);
            krt.pivot = new Vector2(0f, 0.5f);
            krt.sizeDelta = new Vector2(16f, 16f);
            krt.anchoredPosition = Vector2.zero;
            var back = killGo.GetComponent<Image>();
            back.color = WardrobeLook.Danger;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            var cross = OnlineWindow.Label(killGo.transform, "×", 14, FontStyle.Bold, WardrobeLook.DangerText);
            cross.alignment = TextAnchor.MiddleCenter;
            cross.raycastTarget = false;
            OnlineWindow.Place(cross.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 1f));
            var press = killGo.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            press.colors = colors;
            var item = one;
            press.onClick.AddListener(() =>
            {
                if (_busy) return;
                Files.Remove(item);
                Marks();
                Say("Убрал: " + item.Name);
                Focus();
            });

            var label = OnlineWindow.Label(go.transform, one.Name + "  —  " + Size(one.Data.Length), 12, FontStyle.Bold,
                WardrobeLook.Bright);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(22f, 0f), Vector2.zero);
        }

        private static void Lay()
        {
            if (_panel == null) return;
            int rows = Math.Max(1, Files.Count);
            float listTop = HeadH + FieldH + 8f;
            float listH = rows * LineH;
            float rowTop = listTop + listH + 10f;
            float stateTop = rowTop + ButtonH + 8f;
            float high = stateTop + 22f + 12f;
            var size = new Vector2(PanelW, high);
            if (_panel.sizeDelta != size) _panel.sizeDelta = size;
            Slot(_fieldRt, HeadH, FieldH);
            Slot(_listRt, listTop, listH);
            Slot(_rowRt, rowTop, ButtonH);
            Slot(_stateRt, stateTop, 22f);
        }

        private static void Slot(RectTransform rt, float fromTop, float high)
        {
            if (rt == null) return;
            OnlineWindow.Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(14f, -(fromTop + high)), new Vector2(-14f, -fromTop));
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLReport", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            _panel = (RectTransform)_panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.sizeDelta = new Vector2(PanelW, HeadH + FieldH + 120f);
            _panel.anchoredPosition = new Vector2(0f, (HeadH + FieldH + 120f) * 0.5f);
            var back = _panelGo.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(16);
            back.type = Image.Type.Sliced;
            var edge = _panelGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -TopH), Vector2.zero);
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = _panel;
            mover.Canvas = canvas;

            var title = OnlineWindow.Label(_panelGo.transform, "Сообщить об ошибке мода", 17, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(14f, -32f), new Vector2(-52f, -8f));
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;

            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var hint = OnlineWindow.Label(_panelGo.transform,
                "Опиши, что случилось и что делал перед этим. Журналы мода и боёв уйдут вместе с отчётом. Картинку можно вставить прямо из буфера по Ctrl+V или добавить файлом. Настройки и пароли в отчёт не попадают.",
                13, FontStyle.Normal, WardrobeLook.Label);
            OnlineWindow.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(14f, -94f), new Vector2(-72f, -38f));
            hint.alignment = TextAnchor.UpperLeft;
            hint.raycastTarget = false;

            _told = Field(_panelGo.transform);
            _fieldRt = (RectTransform)_told.transform;

            var listGo = new GameObject("files", typeof(RectTransform));
            listGo.transform.SetParent(_panelGo.transform, false);
            _listRt = (RectTransform)listGo.transform;

            var rowGo = new GameObject("buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGo.transform.SetParent(_panelGo.transform, false);
            _rowRt = (RectTransform)rowGo.transform;
            var row = rowGo.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 12f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            Fixed(Wardrobe.GameButton(rowGo.transform, "Выбрать файлы", Choose, false));
            var send = Wardrobe.GameButton(rowGo.transform, "Отправить", Go, false);
            Fixed(send);
            send.GetComponent<Image>().color = WardrobeLook.Accent;
            var sendText = send.GetComponentInChildren<Text>();
            if (sendText != null) sendText.color = WardrobeLook.OnAccent;

            _state = OnlineWindow.Label(_panelGo.transform, "", 13, FontStyle.Normal, WardrobeLook.Label);
            _state.alignment = TextAnchor.MiddleLeft;
            _state.raycastTarget = false;
            _stateRt = _state.rectTransform;

            _panelGo.AddComponent<ReportKeys>();
            Marks();
            Focus();
        }

        private static void Fixed(RectTransform button)
        {
            var fit = button.gameObject.AddComponent<LayoutElement>();
            fit.minWidth = fit.preferredWidth = ButtonW;
            fit.minHeight = fit.preferredHeight = ButtonH;
        }

        private static InputField Field(Transform host)
        {
            var go = new GameObject("told", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Field;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.FieldEdge;
            edge.effectDistance = new Vector2(1f, -1f);

            var text = OnlineWindow.Label(go.transform, "", 15, FontStyle.Normal, WardrobeLook.Bright);
            text.alignment = TextAnchor.UpperLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 8f), new Vector2(-10f, -8f));

            var ph = OnlineWindow.Label(go.transform, "Что случилось, что делал перед этим, повторяется ли", 15, FontStyle.Normal, WardrobeLook.Faint);
            ph.alignment = TextAnchor.UpperLeft;
            ph.horizontalOverflow = HorizontalWrapMode.Wrap;
            OnlineWindow.Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 8f), new Vector2(-10f, -8f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = back;
            input.textComponent = text;
            input.placeholder = ph;
            input.lineType = InputField.LineType.MultiLineNewline;
            input.characterLimit = 2000;
            input.caretColor = WardrobeLook.Bright;
            input.customCaretColor = true;
            return input;
        }
    }

    internal sealed class ReportKeys : MonoBehaviour
    {
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            if (!Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl)) return;
            if (!Input.GetKeyDown(KeyCode.V)) return;
            _next = Time.unscaledTime + 0.4f;
            Report.Paste();
        }
    }

    internal sealed class Grab
    {
        internal string Name;
        internal byte[] Data;
        internal string Path;
    }

    internal static class Clipboard
    {
        private const uint CfDib = 8;
        private const uint CfDrop = 15;
        private const int Biggest = 64 * 1024 * 1024;
        private const int Widest = 8192;
        private const int Chars = 8192;

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr handle);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr GlobalSize(IntPtr handle);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint DragQueryFile(IntPtr drop, uint at, StringBuilder name, uint room);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Dialog
        {
            public int Size;
            public IntPtr Owner;
            public IntPtr Instance;
            public string Filter;
            public string CustomFilter;
            public int CustomFilterRoom;
            public int FilterIndex;
            public IntPtr File;
            public int FileRoom;
            public string FileTitle;
            public int FileTitleRoom;
            public string StartDir;
            public string Title;
            public int Flags;
            public short FileOffset;
            public short ExtensionOffset;
            public string DefExtension;
            public IntPtr Data;
            public IntPtr Hook;
            public string TemplateName;
            public IntPtr Reserved1;
            public int Reserved2;
            public int FlagsEx;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetOpenFileName(ref Dialog dialog);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        internal static List<Grab> Take()
        {
            var got = new List<Grab>();
            if (!OpenClipboard(IntPtr.Zero)) return got;
            try
            {
                uint png = RegisterClipboardFormat("PNG");
                if (png != 0 && IsClipboardFormatAvailable(png))
                {
                    var data = Bytes(GetClipboardData(png), Biggest);
                    if (data != null) { got.Add(new Grab { Name = Stamp("png"), Data = data }); return got; }
                }
                if (IsClipboardFormatAvailable(CfDib))
                {
                    var handle = GetClipboardData(CfDib);
                    if (Fits(handle))
                    {
                        var data = Bytes(handle, Biggest);
                        var made = data != null ? Draw(data) : null;
                        if (made != null) { got.Add(new Grab { Name = Stamp("png"), Data = made }); return got; }
                    }
                }
                if (IsClipboardFormatAvailable(CfDrop))
                {
                    var drop = GetClipboardData(CfDrop);
                    if (drop != IntPtr.Zero)
                    {
                        uint count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
                        for (uint i = 0; i < count && i < 12; i++)
                        {
                            var name = new StringBuilder(1024);
                            if (DragQueryFile(drop, i, name, 1024) == 0) continue;
                            string path = name.ToString();
                            if (path.Length == 0) continue;
                            got.Add(new Grab { Name = System.IO.Path.GetFileName(path), Path = path });
                        }
                    }
                }
            }
            finally { CloseClipboard(); }
            return got;
        }

        internal static List<string> Ask()
        {
            var got = new List<string>();
            IntPtr room = Marshal.AllocHGlobal(Chars * 2);
            try
            {
                for (int i = 0; i < Chars * 2; i++) Marshal.WriteByte(room, i, 0);
                var dialog = new Dialog
                {
                    Owner = GetActiveWindow(),
                    Filter = "Картинки и журналы\0*.png;*.jpg;*.jpeg;*.gif;*.log;*.txt\0Все файлы\0*.*\0\0",
                    File = room,
                    FileRoom = Chars,
                    FileTitle = new string('\0', 256),
                    FileTitleRoom = 256,
                    Title = "Что приложить к отчёту",
                    Flags = 0x00081A0C,
                };
                dialog.Size = Marshal.SizeOf(dialog);
                Plugin.Trace("[отчёт] открываю окно выбора файлов, хозяин окна " + dialog.Owner);
                if (!GetOpenFileName(ref dialog))
                {
                    int trouble = CommDlgExtendedError();
                    if (trouble != 0) Plugin.Warn("[отчёт] окно выбора файлов не открылось, код 0x" + trouble.ToString("X"));
                    else Plugin.Trace("[отчёт] окно выбора закрыто без выбора");
                    return got;
                }

                var parts = new List<string>();
                int at = 0;
                while (at < Chars)
                {
                    string one = Marshal.PtrToStringUni(IntPtr.Add(room, at * 2));
                    if (string.IsNullOrEmpty(one)) break;
                    parts.Add(one);
                    at += one.Length + 1;
                }
                if (parts.Count == 1) got.Add(parts[0]);
                else for (int i = 1; i < parts.Count && i <= 12; i++) got.Add(System.IO.Path.Combine(parts[0], parts[i]));
            }
            catch (Exception e) { Plugin.Warn("[отчёт] окно выбора файла: " + e.Message); }
            finally { Marshal.FreeHGlobal(room); }
            return got;
        }

        private static string Stamp(string kind)
        {
            return "картинка_" + DateTime.Now.ToString("HH-mm-ss") + "." + kind;
        }

        private static byte[] Bytes(IntPtr handle, int cap)
        {
            if (handle == IntPtr.Zero) return null;
            var at = GlobalLock(handle);
            if (at == IntPtr.Zero) return null;
            try
            {
                ulong size = GlobalSize(handle).ToUInt64();
                if (size == 0UL) return null;
                if (size > (ulong)cap)
                {
                    Plugin.Trace("[отчёт] в буфере " + (size / 1024UL / 1024UL) + " МБ, столько не беру");
                    return null;
                }
                var data = new byte[(int)size];
                Marshal.Copy(at, data, 0, data.Length);
                return data;
            }
            finally { GlobalUnlock(handle); }
        }

        private static bool Fits(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return false;
            var at = GlobalLock(handle);
            if (at == IntPtr.Zero) return false;
            try
            {
                ulong size = GlobalSize(handle).ToUInt64();
                if (size < 40UL || size > (ulong)Biggest)
                {
                    Plugin.Trace("[отчёт] картинка в буфере на " + size + " байт, не беру");
                    return false;
                }
                var head = new byte[40];
                Marshal.Copy(at, head, 0, head.Length);
                int wide = BitConverter.ToInt32(head, 4);
                int high = BitConverter.ToInt32(head, 8);
                int bits = BitConverter.ToInt16(head, 14);
                if (wide <= 0 || wide > Widest || high == 0 || Math.Abs(high) > Widest)
                {
                    Plugin.Trace("[отчёт] картинка в буфере " + wide + "×" + high + ", не беру");
                    return false;
                }
                if (bits != 24 && bits != 32)
                {
                    Plugin.Trace("[отчёт] картинка в буфере на " + bits + " бит, не беру");
                    return false;
                }
                return true;
            }
            catch (Exception e) { Plugin.Trace("[отчёт] шапка картинки: " + e.Message); return false; }
            finally { GlobalUnlock(handle); }
        }

        private static byte[] Draw(byte[] dib)
        {
            Texture2D pic = null;
            try
            {
                if (dib.Length < 40) return null;
                int head = BitConverter.ToInt32(dib, 0);
                int wide = BitConverter.ToInt32(dib, 4);
                int high = BitConverter.ToInt32(dib, 8);
                int bits = BitConverter.ToInt16(dib, 14);
                int packed = BitConverter.ToInt32(dib, 16);
                if (wide <= 0 || wide > Widest || high == 0 || Math.Abs(high) > Widest) return null;
                if (bits != 24 && bits != 32) return null;
                if (packed != 0 && packed != 3) return null;

                bool up = high > 0;
                int rows = Math.Abs(high);
                int from = head + (packed == 3 && head == 40 ? 12 : 0);
                int step = ((wide * bits / 8) + 3) / 4 * 4;
                if (from + step * rows > dib.Length) return null;

                pic = new Texture2D(wide, rows, TextureFormat.RGB24, false);
                var line = new Color32[wide];
                for (int y = 0; y < rows; y++)
                {
                    int at = from + step * y;
                    for (int x = 0; x < wide; x++)
                    {
                        int p = at + x * (bits / 8);
                        line[x] = new Color32(dib[p + 2], dib[p + 1], dib[p], 255);
                    }
                    pic.SetPixels32(0, up ? y : rows - 1 - y, wide, 1, line);
                }
                pic.Apply();
                return pic.EncodeToPNG();
            }
            catch (Exception e) { Plugin.Trace("[отчёт] картинка из буфера: " + e.Message); return null; }
            finally { if (pic != null) UnityEngine.Object.Destroy(pic); }
        }
    }
}
