using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Transport.Messages.Responses.User.Info;
using UnityEngine;

namespace NewAgeQoL
{
    internal sealed class OnlinePlayer
    {
        internal int Id;
        internal string Login = "";
        internal int Level;
        internal string Class = "";
        internal string Clan = "";
        internal int Rank;
        internal bool Vip;
        internal bool Admin;
        internal bool Dealer;
        internal bool Away;
        internal string Rights = "";
        internal string Search;
        internal List<OnlineCharm> Charms;
    }

    internal static class OnlineList
    {
        private const string Host = "nura.biz";
        private const int Port = 2000;
        private const float AskEvery = 0.12f;

        private static readonly object Gate = new object();
        private static List<OnlinePlayer> _players = new List<OnlinePlayer>();
        private static string _status = "";
        private static string _stamp = "";
        private static bool _busy;
        private static int _version;
        private static DateTime _at = DateTime.MinValue;

        private static readonly Dictionary<string, int> ClanCodes = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> ClanSprites = new Dictionary<string, string>();
        private static readonly HashSet<string> ClanNoCode = new HashSet<string>();
        private static readonly Dictionary<string, string> ClanNames = new Dictionary<string, string>();
        private static readonly Queue<OnlinePlayer> AskQueue = new Queue<OnlinePlayer>();
        private static readonly HashSet<int> Asked = new HashSet<int>();
        private static float _askAt;
        private static object _on;
        private static bool _cacheLoaded;

        internal static bool Busy { get { lock (Gate) return _busy; } }
        internal static string Status { get { lock (Gate) return _status; } }
        internal static string Stamp { get { lock (Gate) return _stamp; } }
        internal static int Version { get { lock (Gate) return _version; } }
        internal static List<OnlinePlayer> Players { get { lock (Gate) return new List<OnlinePlayer>(_players); } }

        internal static OnlinePlayer ByLogin(string login)
        {
            if (string.IsNullOrEmpty(login)) return null;
            lock (Gate)
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    var p = _players[i];
                    if (p != null && string.Equals(p.Login, login, StringComparison.CurrentCultureIgnoreCase)) return p;
                }
            }
            return null;
        }

        internal static void CopyTo(List<OnlinePlayer> into)
        {
            if (into == null) return;
            into.Clear();
            lock (Gate) into.AddRange(_players);
        }

        internal static bool Configured =>
            !string.IsNullOrEmpty(Plugin.CfgOnlineLogin?.Value?.Trim()) && !string.IsNullOrEmpty(Plugin.CfgOnlinePassword?.Value);

        internal static void Tick()
        {
            try
            {
                Listen();
                LoadCache();
                Flush();
                if (AskQueue.Count == 0 || Time.unscaledTime < _askAt) return;
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { AskQueue.Clear(); Asked.Clear(); return; }
                var p = AskQueue.Dequeue();
                _askAt = Time.unscaledTime + AskEvery;
                nc.SendRequest(new UserInfoRequest(p.Id, p.Login));
            }
            catch (Exception e) { Plugin.Trace("[онлайн] значки клана: " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(433, OnUserInfo);
            nc.AddMessageListener(433, OnUserInfo);
            _on = nc;
        }

        private static void LoadCache()
        {
            if (_cacheLoaded) return;
            _cacheLoaded = true;
            string raw = Plugin.CfgOnlineClanCache?.Value ?? "";
            foreach (var part in raw.Split(','))
            {
                int i = part.LastIndexOf(':');
                if (i <= 0) continue;
                int code;
                string value = part.Substring(i + 1);
                if (int.TryParse(value, out code)) ClanCodes[part.Substring(0, i)] = code;
                else if (value.Length > 1 && value[0] == '@') ClanSprites[part.Substring(0, i)] = value.Substring(1);
            }
            foreach (var part in (Plugin.CfgOnlineClanNames?.Value ?? "").Split(','))
            {
                int i = part.IndexOf('=');
                if (i <= 0 || i + 1 >= part.Length) continue;
                ClanNames[part.Substring(0, i)] = part.Substring(i + 1);
            }
        }

        private static bool _cacheDirty;
        private static bool _namesDirty;
        private static float _flushAt;

        private static void Flush()
        {
            if (!_cacheDirty && !_namesDirty) return;
            if (Time.unscaledTime < _flushAt) return;
            _flushAt = Time.unscaledTime + 10f;
            if (_cacheDirty) { _cacheDirty = false; SaveCache(); }
            if (_namesDirty) { _namesDirty = false; SaveNames(); }
            Plugin.Trace("[онлайн] кэш кланов сохранён");
        }

        private static void SaveCache()
        {
            if (Plugin.CfgOnlineClanCache == null) return;
            var sb = new StringBuilder();
            foreach (var kv in ClanCodes)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key.Replace(',', '_').Replace(':', '_')).Append(':').Append(kv.Value);
            }
            foreach (var kv in ClanSprites)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key.Replace(',', '_').Replace(':', '_')).Append(":@").Append(kv.Value.Replace(',', '_').Replace(':', '_'));
            }
            Plugin.CfgOnlineClanCache.Value = sb.ToString();
        }

        private static void SaveNames()
        {
            if (Plugin.CfgOnlineClanNames == null) return;
            var sb = new StringBuilder();
            foreach (var kv in ClanNames)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(kv.Key.Replace(',', '_').Replace('=', '_')).Append('=').Append(kv.Value.Replace(',', ' '));
            }
            Plugin.CfgOnlineClanNames.Value = sb.ToString();
        }

        internal static string ClanName(string icon)
        {
            string name;
            return !string.IsNullOrEmpty(icon) && ClanNames.TryGetValue(icon, out name) ? name : "";
        }

        internal static bool ClanCode(string icon, out int code)
        {
            code = 0;
            return !string.IsNullOrEmpty(icon) && ClanCodes.TryGetValue(icon, out code);
        }

        internal static bool ClanSprite(string icon, out string sprite)
        {
            sprite = null;
            return !string.IsNullOrEmpty(icon) && ClanSprites.TryGetValue(icon, out sprite);
        }

        internal static void AskClanCodes(List<OnlinePlayer> players)
        {
            var wanted = new HashSet<string>();
            foreach (var p in players)
            {
                if (string.IsNullOrEmpty(p.Clan)) continue;
                if ((ClanCodes.ContainsKey(p.Clan) || ClanSprites.ContainsKey(p.Clan) || ClanNoCode.Contains(p.Clan)) && ClanNames.ContainsKey(p.Clan)) continue;
                if (!wanted.Add(p.Clan)) continue;
                if (Asked.Contains(p.Id)) continue;
                Asked.Add(p.Id);
                AskQueue.Enqueue(p);
            }
        }

        private static void OnUserInfo(object m)
        {
            var info = m as Unity3DUserInfoResponseMessage;
            if (info == null) return;
            OnlinePlayer p = null;
            lock (Gate) foreach (var x in _players) if (x.Id == info.UserId) { p = x; break; }
            if (p == null || string.IsNullOrEmpty(p.Clan)) return;
            bool changed = false;
            int known;
            if (info.ClanIconCode.HasValue && (!ClanCodes.TryGetValue(p.Clan, out known) || known != info.ClanIconCode.Value))
            {
                ClanCodes[p.Clan] = info.ClanIconCode.Value;
                changed = true;
            }
            string drawn;
            if (!string.IsNullOrEmpty(info.ClanIcon) && (!ClanSprites.TryGetValue(p.Clan, out drawn) || drawn != info.ClanIcon))
            {
                ClanSprites[p.Clan] = info.ClanIcon;
                changed = true;
            }
            if (changed) _cacheDirty = true;
            if (!info.ClanIconCode.HasValue && string.IsNullOrEmpty(info.ClanIcon)) ClanNoCode.Add(p.Clan);
            if (!string.IsNullOrEmpty(info.ClanName) && (!ClanNames.ContainsKey(p.Clan) || ClanNames[p.Clan] != info.ClanName))
            {
                ClanNames[p.Clan] = info.ClanName;
                _namesDirty = true;
            }
            lock (Gate) _version++;
        }

        internal static void Refresh()
        {
            string login = (Plugin.CfgOnlineLogin?.Value ?? "").Trim();
            string pass = Plugin.CfgOnlinePassword?.Value ?? "";
            string ver = (Plugin.CfgOnlineVersion?.Value ?? "").Trim();
            if (ver.Length == 0) ver = "11073";
            if (login.Length == 0 || pass.Length == 0)
            {
                Set("Укажи логин и пароль запасного аккаунта в настройках мода");
                return;
            }
            string me = Mine();
            if (me.Length > 0 && string.Equals(me, login, StringComparison.OrdinalIgnoreCase))
            {
                Set("В настройках указан тот же персонаж, которым ты играешь — нужен запасной");
                return;
            }
            lock (Gate)
            {
                if (_busy) return;
                _busy = true;
                _status = "обновляю…";
                _stamp = "";
                _version++;
            }
            var t = new Thread(() => Work(login, pass, ver)) { IsBackground = true, Name = "QoLOnlineList" };
            t.Start();
        }

        internal static string SameOne()
        {
            string login = (Plugin.CfgOnlineLogin?.Value ?? "").Trim();
            if (login.Length == 0) return null;
            string me = Mine();
            if (me.Length == 0 || !string.Equals(me, login, StringComparison.OrdinalIgnoreCase)) return null;
            return "В настройках указан тот же персонаж, которым ты играешь — для списка нужен запасной аккаунт";
        }

        private static string Mine()
        {
            try
            {
                var ud = Controllers.User;
                var info = ud != null ? ud.UserInfo : null;
                return info != null && !string.IsNullOrEmpty(info.Login) ? info.Login.Trim() : "";
            }
            catch { return ""; }
        }

        private static void Set(string status)
        {
            lock (Gate) { _status = status; _stamp = ""; _version++; }
        }

        private static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

        private static void Work(string login, string pass, string ver)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.NoDelay = true;
                    var ar = client.BeginConnect(Host, Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(8000)) throw new Exception("нет соединения с " + Host);
                    client.EndConnect(ar);
                    var stream = client.GetStream();
                    var acc = new List<byte>();

                    Pump(stream, acc, 1.5, null);

                    Send(stream, "<Message type=\"315\"><auth account=\"" + Esc(login) + "\" password=\"" + Esc(pass) + "\" ver=\"" + Esc(ver) + "\" site=\"1\" /></Message>");
                    string lr = null;
                    foreach (var m in Pump(stream, acc, 8, m => m.Contains("LoginResponce")))
                        if (m.Contains("LoginResponce")) lr = m;
                    if (lr == null) throw new Exception("сервер не ответил на вход");
                    if (!lr.Contains("LoggedIn=\"1\""))
                    {
                        var mm = Regex.Match(lr, "Msg=\"([^\"]*)\"");
                        throw new Exception("вход отклонён" + (mm.Success && mm.Groups[1].Value.Length > 0 ? ": " + System.Net.WebUtility.HtmlDecode(mm.Groups[1].Value) : ""));
                    }

                    Send(stream, "<Message type=\"49\" />");
                    string m50 = null;
                    foreach (var m in Pump(stream, acc, 12, m => m.Contains("type=\"50\"")))
                        if (m.Contains("type=\"50\"")) m50 = m;
                    if (m50 == null) throw new Exception("список не пришёл");

                    var list = Parse(m50);
                    lock (Gate)
                    {
                        OnlineCharms.Carry(_players, list);
                        _players = list;
                        _at = DateTime.Now;
                        _stamp = _at.ToString("HH:mm:ss");
                        _status = "В игре: " + list.Count + " · " + _stamp;
                        _version++;
                    }
                    Plugin.Trace("[онлайн] получено " + list.Count + " игроков");
                    OnlineCharms.Fetch(stream, acc, list);
                }
            }
            catch (Exception e)
            {
                Set("Ошибка: " + e.Message);
                Plugin.Warn("[онлайн] " + e.Message);
            }
            finally
            {
                lock (Gate) { _busy = false; _version++; }
            }
        }

        private static void Send(NetworkStream s, string xml)
        {
            var b = Encoding.UTF8.GetBytes(xml + "\0");
            s.Write(b, 0, b.Length);
            s.Flush();
        }

        private static List<string> Pump(NetworkStream s, List<byte> acc, double seconds, Func<string, bool> stopWhen)
        {
            var got = new List<string>();
            var end = DateTime.UtcNow.AddSeconds(seconds);
            var tmp = new byte[65536];
            while (DateTime.UtcNow < end)
            {
                if (!s.DataAvailable) { Thread.Sleep(40); continue; }
                int n = s.Read(tmp, 0, tmp.Length);
                if (n <= 0) break;
                for (int i = 0; i < n; i++)
                {
                    if (tmp[i] != 0) { acc.Add(tmp[i]); continue; }
                    if (acc.Count == 0) continue;
                    string m = Encoding.UTF8.GetString(acc.ToArray());
                    acc.Clear();
                    if (m.Trim().Length == 0) continue;
                    got.Add(m);
                    if (stopWhen != null && stopWhen(m)) return got;
                }
            }
            return got;
        }

        private static List<OnlinePlayer> Parse(string xml)
        {
            var list = new List<OnlinePlayer>();
            foreach (Match u in Regex.Matches(xml, "<user\\b([^>]*)/>"))
            {
                string a = u.Groups[1].Value;
                if (!_shownRaw) { _shownRaw = true; Plugin.Trace("[онлайн] поля игрока в ответе: " + Keys(a)); }
                var p = new OnlinePlayer
                {
                    Id = Int(Attr(a, "id")),
                    Login = Attr(a, "login"),
                    Level = Int(Attr(a, "level")),
                    Class = Attr(a, "race"),
                    Clan = Attr(a, "icon"),
                    Rank = Int(Attr(a, "rank")),
                    Vip = Attr(a, "vip") == "1",
                    Dealer = Attr(a, "dealer") == "1",
                    Rights = Attr(a, "admin"),
                    Away = Yes(a, "afk") || Yes(a, "away") || Yes(a, "idle") || Yes(a, "sleep"),
                };
                p.Admin = p.Rights.Length > 0;
                if (p.Login.Length > 0) list.Add(p);
            }
            return list;
        }

        private static bool _shownRaw;

        private static string Keys(string attrs)
        {
            var names = new List<string>();
            foreach (Match one in Regex.Matches(attrs ?? "", "([A-Za-z_][A-Za-z0-9_-]*)=\"")) names.Add(one.Groups[1].Value);
            return string.Join(", ", names.ToArray());
        }

        private static bool Yes(string a, string key)
        {
            string got = Attr(a, key);
            return got == "1" || got == "true";
        }

        private static string Attr(string a, string key)
        {
            var m = Regex.Match(a, "\\b" + key + "=\"([^\"]*)\"");
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : "";
        }

        private static int Int(string s)
        {
            int n;
            return int.TryParse(s, out n) ? n : 0;
        }
    }
}
