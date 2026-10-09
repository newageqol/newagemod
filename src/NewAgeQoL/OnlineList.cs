using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.Networking;

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
        private const float AskEvery = 0.12f;
        private const string Server = "https://newage-observers.outerlab.org/v1/online";
        private const string CharmServer = "https://newage-observers.outerlab.org/v1/charms";
        private const float CharmEvery = 0.8f;
        private const int CharmRounds = 30;
        private const string Mark = "newage-observers 1";

        private static readonly object Gate = new object();
        private static List<OnlinePlayer> _players = new List<OnlinePlayer>();
        private static string _status = "";
        private static bool _busy;
        private static int _version;
        private static DateTime _made = DateTime.MinValue;
        private static int _fresh = 30;

        internal static int Age
        {
            get { lock (Gate) return _made == DateTime.MinValue ? -1 : (int)(DateTime.UtcNow - _made).TotalSeconds; }
        }

        internal static int Wait
        {
            get { lock (Gate) return _made == DateTime.MinValue ? 0 : Math.Max(0, (int)Math.Ceiling(_fresh - (DateTime.UtcNow - _made).TotalSeconds)); }
        }

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
        internal static int Version { get { lock (Gate) return _version; } }
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

        internal static OnlinePlayer ById(int id)
        {
            lock (Gate)
                for (int i = 0; i < _players.Count; i++)
                    if (_players[i] != null && _players[i].Id == id) return _players[i];
            return null;
        }

        internal static void CopyTo(List<OnlinePlayer> into)
        {
            if (into == null) return;
            into.Clear();
            lock (Gate) into.AddRange(_players);
        }

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
            catch (Exception e) { Plugin.Trace("[online] clan icons: " + e.Message); }
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
            Plugin.Trace("[online] clan cache saved");
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
            lock (Gate)
            {
                if (_busy) return;
                _busy = true;
                _status = "обновляю…";
                _version++;
            }
            if (Plugin.Instance == null) { Failed("мод ещё не готов"); return; }
            Plugin.Instance.StartCoroutine(Fetch());
        }

        private static void Failed(string why)
        {
            lock (Gate) { _status = "Ошибка: " + why; _busy = false; _version++; }
            Plugin.Warn("[online] " + why);
        }

        private static IEnumerator Fetch()
        {
            var req = UnityWebRequest.Get(Server);
            req.timeout = 20;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            long code = req.responseCode;
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            req.Dispose();
            List<OnlinePlayer> list = null;
            long gen = 0;
            try { list = Take(code, body, out gen); }
            catch (Exception e) { Failed(e.Message); }
            if (list != null) yield return Charms(list, gen);
        }

        private static List<OnlinePlayer> Take(long code, string body, out long gen)
        {
            gen = 0;
            var records = Records(code, body);
            if (records == null) return null;
            string m50 = records.Find(r => r.StartsWith("50\u001f", StringComparison.Ordinal));
            if (m50 == null) { Failed("список не пришёл"); return null; }
            gen = Field(records, "gen");
            long fresh = Field(records, "fresh");
            lock (Gate)
            {
                _made = DateTime.UtcNow.AddSeconds(-Field(records, "age"));
                if (fresh > 0) _fresh = (int)fresh;
            }

            var list = Parse(m50.Substring(3));
            lock (Gate) OnlineCharms.Carry(_players, list);
            OnlineCharms.Gathering = true;
            lock (Gate)
            {
                _players = list;
                _status = "В игре: " + list.Count;
                _busy = false;
                _version++;
            }
            Plugin.Trace("[online] received " + list.Count + " players");
            return list;
        }

        private static IEnumerator Charms(List<OnlinePlayer> list, long gen)
        {
            try
            {
                for (int round = 0; round < CharmRounds; round++)
                {
                    yield return new WaitForSecondsRealtime(CharmEvery);
                    bool current;
                    lock (Gate) current = ReferenceEquals(_players, list);
                    if (!current) yield break;
                    var req = UnityWebRequest.Get(CharmServer);
                    req.timeout = 10;
                    req.redirectLimit = 0;
                    req.SetRequestHeader("User-Agent", "NewAgeQoL");
                    yield return req.SendWebRequest();
                    long code = req.responseCode;
                    string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
                    req.Dispose();
                    lock (Gate) current = ReferenceEquals(_players, list);
                    if (!current) yield break;
                    var records = Records(code, body, false);
                    if (records == null) continue;
                    if (Field(records, "gen") != gen) yield break;
                    OnlineCharms.Apply(records, list, Field(records, "now"));
                    if (records.Contains("state\u001fdone")) yield break;
                }
                Plugin.Trace("[online] player states: server did not finish in time");
            }
            finally
            {
                bool current;
                lock (Gate) current = ReferenceEquals(_players, list);
                if (current)
                {
                    OnlineCharms.Gathering = false;
                    OnlineCharms.Version++;
                }
            }
        }

        private static List<string> Records(long code, string body, bool loud = true)
        {
            string trouble = null;
            if (!body.StartsWith(Mark, StringComparison.Ordinal))
                trouble = code == 0 ? "сервер наблюдателей недоступен" : "сервер наблюдателей ответил " + code;
            string rest = trouble == null && body.Length > Mark.Length ? body.Substring(Mark.Length + 1) : "";
            if (trouble == null && rest.StartsWith("!\t", StringComparison.Ordinal)) trouble = rest.Substring(2).Trim();
            if (trouble == null) return new List<string>(rest.Split('\0'));
            if (loud) Failed(trouble);
            else Plugin.Trace("[online] player states: " + trouble);
            return null;
        }

        private static long Field(List<string> records, string name)
        {
            string found = records.Find(r => r.StartsWith(name + "\u001f", StringComparison.Ordinal));
            long value;
            return found != null && long.TryParse(found.Substring(name.Length + 1), out value) ? value : 0;
        }

        private static List<OnlinePlayer> Parse(string xml)
        {
            var list = new List<OnlinePlayer>();
            foreach (Match u in Regex.Matches(xml, "<user\\b([^>]*)/>"))
            {
                string a = u.Groups[1].Value;
                if (!_shownRaw) { _shownRaw = true; Plugin.Trace("[online] player fields in response: " + Keys(a)); }
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
