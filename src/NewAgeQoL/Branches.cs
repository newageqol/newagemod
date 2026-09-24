using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal sealed class BranchRec
    {
        internal int Cls = -1;
        internal int Reg = -1;
        internal int Elite = -1;
        internal long Hour;

        internal bool Full { get { return Reg >= 0 && Elite >= 0; } }
    }

    internal static class Branches
    {
        private const string Site = "https://nura.biz";
        private const string Archive = Site + "/events/combats";
        private const string Detail = "/events/combats/detail/";
        private const string Profile = "/community/profile/";
        private const string Record = "id=\"log\"";
        private const string Ability = "[<span class='ca'>";
        private const string Owned = "]<span class='cu'";
        private const string Tag = "data-id='";

        private const string SkillTable = ""
            + "ЯДОВИТЫЕ СТРЕЛЫ=0|НЕЙРОТОКСИН=0|ТОКСИКОЛОГИЯ=0|"
            + "РИКОШЕТ=0|РАССЧИТАННЫЙ УДАР=0|АУРА МАСКИРОВКИ=0|"
            + "ПАРАЛИЗУЮЩИЙ ЯД=0|СТРЕЛЫ ДУХОВ=2|УДАР В ГЛАЗ=2|"
            + "ТРОЙНОЙ ВЫСТРЕЛ=2|ЗНАНИЕ ПРОТИВНИКА=2|ШОКИРУЮЩИЕ СТРЕЛЫ=2|"
            + "АУРА ТОЧНОСТИ=2|ГРАЦИЯ=2|ГЕМОТОКСИН=1|"
            + "ДВОЙНОЙ ВЫСТРЕЛ=1|СТРЕЛЬБА НАВЕСОМ=1|ПЕСНЬ=3|"
            + "ПОСЛЕДНИЙ ГЕРОЙ=3|МАНТРА=3|РАССЕКАЮЩИЙ УДАР=4|"
            + "РАЗРЫВАНИЕ=4|ПЕРЕЛОМАТЬ КОСТИ=4|ПОСЛЕДНЯЯ ВОЛЯ=4|"
            + "БОЕВАЯ СТОЙКА=4|АУРА БОЛИ=4|ГАРПУН=4|"
            + "ОБЕЗОРУЖИВАНИЕ=6|ВИХРЬ УДАРОВ=6|ВОЛЯ ГОРЦА=6|"
            + "ГНЕВ=6|ЯРОСТЬ ТОПОРА=6|АУРА КРОВОЖАДНОСТИ=6|"
            + "ЭНЕРГЕТИЧЕСКИЙ ВАМПИРИЗМ=6|РЕЗНЯ=5|РАСЧЛЕНЕНИЕ=5|"
            + "КАНАЛ КРОВИ=5|ДЕМОНИЧЕСКИЙ ДОГОВОР=7|ДИКОСТЬ=7|"
            + "ИНВЕРСИЯ=7|МАСТЕРСКИЙ БРОСОК=8|МЕСТЬ=8|"
            + "ШИРОКИЙ ЩИТ=8|ПАРИРОВАНИЕ=8|НАСМЕШКА=8|"
            + "АУРА ОБРАЩЕНИЯ=8|УГРОЗА=8|ОТРАЖЕНИЕ МАГИИ=10|"
            + "ОБЕСКУРАЖИВАНИЕ=10|УПРЕЖДАЮЩИЙ УДАР=10|АУРА РУННОЙ ЗАЩИТЫ=10|"
            + "РУННОЕ СЛОВО=10|РУННЫЙ УДАР=10|ПОКРОВИТЕЛЬСТВО=10|"
            + "ПОДАВЛЕНИЕ=9|АУРА УСТОЙЧИВОСТИ=9|РУННАЯ БРОНЯ=9|"
            + "НЕИСТОВСТВО=11|ГЕРОИЗМ=11|АЛЬТРУИЗМ=11|"
            + "КАЛЕЧАЩИЙ УДАР=12|СПРИНТ=12|БОЕВАЯ ЯРОСТЬ=12|"
            + "АУРА СТРАХА=12|ПОГЛОЩЕНИЕ УРОНА=12|АУРА БРОНИ=12|"
            + "САФАРИ=12|СОКРУШАЮЩИЙ УДАР=14|ШТУРМ=14|"
            + "КОНТРАТАКА=14|ТРИУМФ=14|САМОПОЖЕРТВОВАНИЕ=14|"
            + "ИЗБАВЛЕНИЕ=14|УКРЕПЛЕННАЯ БРОНЯ=14|ФЕХТОВАНИЕ=13|"
            + "НЕРУШИМОСТЬ=13|ВУДУИЗМ=13|ПРЕДАННОСТЬ=15|"
            + "ЭНЕРГИЧНОСТЬ=15|ЭМПАТИЯ=15|ЧУМА=16|"
            + "ГИПНОЗ=16|АУРА НЕЧЕСТИВОСТИ=16|РАЗЛОЖЕНИЕ=16|"
            + "ИЗГИБ РЕАЛЬНОСТИ=16|КОЛЛАПС=16|АУРА СПАСЕНИЯ=16|"
            + "ОБНОВЛЕНИЕ=18|АСТРАЛЬНАЯ ТЮРЬМА=18|АУРА СВЯТОСТИ=18|"
            + "ПАНАЦЕЯ=18|АНТИМАГИЯ=18|ТАИНСТВО=18|"
            + "БЕЗМОЛВНЫЙ СТРАЖ=18|ПРИЗЫВ ВОРОНА=17|КРИСТАЛЛИЗАЦИЯ=17|"
            + "СИМУЛЯКР=17|ПРИЗЫВ ГОЛУБЯ=19|ТРАНСМУТАЦИЯ=19|"
            + "МЕНТАЛИЗМ=19|ПРОНИКАЮЩЕЕ ЗАКЛИНАНИЕ=20|МЕДИТАЦИЯ=20|"
            + "МАГИСТР ТЕМНОЙ МАГИИ=20|КРИТИЧЕСКОЕ ЗАКЛИНАНИЕ=20|ПРИЗЫВ ТЕНИ=20|"
            + "АУРА ПРЕВОСХОДСТВА=20|ПРЕДСКАЗАНИЕ=20|УСИЛЕННОЕ ЗАКЛИНАНИЕ=22|"
            + "СВЯЗЬ МАНЫ=22|МАГИСТР СВЕТЛОЙ МАГИИ=22|МЕТАМАГИЯ=22|"
            + "ПРИЗЫВ ДУХА=22|МОЗГОВОЙ ШТУРМ=22|ОЗАРЕНИЕ=22|"
            + "ХАОС=21|ЭНТРОПИЯ=21|МАГНЕТИЗМ=21|"
            + "ОТКРОВЕНИЕ=23|ТРАНС=23|ЭМАНАЦИЯ=23|"
            + "ПЕРЕЛОМ КОСТИ=4|";

        private const string RootTable = "ВЕНДЕТТА=0|ВСПЛЕСК АДРЕНАЛИНА=1|БРОНЯ ЛЕЗВИЙ=2|МАСТЕРСТВО БРОНИ=3|МАГИЧЕСКИЙ ВАМПИРИЗМ=4|ТАЙНОЕ ИСКУССТВО=5";

        private static readonly string[] BranchNames =
        {
            "Яд", "Навес", "СД", "Песня",
            "Стойка", "Расчленение", "Бросок", "Дикость",
            "Антифиз", "Подав", "Антимаг", "Неиста",
            "Поглощение", "Вудуизм", "Штурм", "Эмпатия",
            "Чума", "Симулякр", "Обновление", "Ментализм",
            "Тёмный", "Хаос", "Светлый", "Откровение"
        };

        private static readonly string[] ClassNames =
        {
            "Рейнджер", "Варвар", "Мастер щита", "Джаггернаут", "Жрец", "Маг"
        };

        private static readonly Dictionary<string, int> Skills = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> Roots = new Dictionary<string, int>();
        private static readonly Dictionary<int, BranchRec> Known = new Dictionary<int, BranchRec>();
        private static readonly Dictionary<int, string> Logins = new Dictionary<int, string>();
        private static readonly List<int> Pending = new List<int>();
        private static readonly HashSet<string> Fetched = new HashSet<string>();

        private static bool _tables, _loaded, _busy, _dirty, _session;
        private static string _cookie = "";
        private static int _searches, _pages, _hunting, _era;
        private static float _saveAt, _nextAt;

        private sealed class Row
        {
            internal string Uid = "";
            internal readonly List<int> Ids = new List<int>();
        }

        private sealed class Reply
        {
            internal string Body = "";
            internal string Location = "";
            internal string Cookie = "";
            internal string Error = "";
            internal long Code;
        }

        private static bool On
        {
            get { return Plugin.CfgBranchesOn != null && Plugin.CfgBranchesOn.Value; }
        }

        internal static void Tick()
        {
            try
            {
                if (!On) return;
                Tables();
                Load();
                Flush();
                if (_busy || Pending.Count == 0 || Plugin.Instance == null) return;
                if (_searches >= Num(Plugin.CfgBranchSearches, 60)) return;
                _busy = true;
                Plugin.Instance.StartCoroutine(Work());
            }
            catch (Exception e) { Plugin.Trace("[ветки] " + e.Message); }
        }

        internal static void NewFight()
        {
            _era++;
            Pending.Clear();
            Fetched.Clear();
            _searches = 0;
            _pages = 0;
            _hunting = 0;
        }

        internal static void Want(int userId, string login)
        {
            if (!On || userId <= 0 || string.IsNullOrEmpty(login)) return;
            Tables();
            Load();
            Logins[userId] = login;
            if (!Stale(userId) || _hunting == userId || Pending.Contains(userId)) return;
            Pending.Add(userId);
        }

        internal static string Text(int userId)
        {
            if (!On || userId <= 0) return "";
            Tables();
            Load();
            BranchRec r;
            if (!Known.TryGetValue(userId, out r)) r = null;
            var sb = new StringBuilder();
            sb.Append("КУ: ").Append(r != null && r.Reg >= 0 ? BranchNames[r.Cls * 4 + r.Reg * 2] : Soon(userId));
            sb.Append("      ЭКУ: ").Append(r != null && r.Elite >= 0 ? BranchNames[r.Cls * 4 + r.Elite * 2 + 1] : Soon(userId));
            return sb.ToString();
        }

        private static string Soon(int userId)
        {
            return _hunting == userId || Pending.Contains(userId) ? "ищу…" : "?";
        }

        private static long Hours()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalHours;
        }

        private static int Num(BepInEx.Configuration.ConfigEntry<int> cfg, int fallback)
        {
            return cfg == null ? fallback : cfg.Value;
        }

        private static bool Stale(int userId)
        {
            BranchRec r;
            if (!Known.TryGetValue(userId, out r)) return true;
            if (r.Full) return Hours() - r.Hour >= Num(Plugin.CfgBranchHours, 24);
            if (r.Cls >= 0) return true;
            return Hours() - r.Hour >= Num(Plugin.CfgBranchMissHours, 6);
        }

        private static BranchRec Rec(int userId)
        {
            BranchRec r;
            if (!Known.TryGetValue(userId, out r)) { r = new BranchRec(); Known[userId] = r; }
            return r;
        }

        private static bool Fresh(int userId, long since)
        {
            BranchRec r;
            return Known.TryGetValue(userId, out r) && r.Full && r.Hour >= since;
        }

        private static IEnumerator Work()
        {
            int era = _era;
            try
            {
                while (era == _era && _searches < Num(Plugin.CfgBranchSearches, 60))
                {
                    int who = 0;
                    while (Pending.Count > 0)
                    {
                        int id = Pending[0];
                        Pending.RemoveAt(0);
                        if (Stale(id)) { who = id; break; }
                    }
                    if (who == 0) break;
                    _hunting = who;
                    yield return Hunt(who, era);
                    if (era == _era) _hunting = 0;
                }
            }
            finally { _busy = false; }
            if (_searches > 0 || _pages > 0)
                Plugin.Trace("[ветки] за бой: поисков " + _searches + ", логов " + _pages
                    + ", в очереди осталось " + Pending.Count + ", в памяти " + Known.Count);
        }

        private static IEnumerator Hunt(int userId, int era)
        {
            string login;
            if (!Logins.TryGetValue(userId, out login) || login.Length == 0) yield break;
            long since = Hours();
            int days = Mathf.Clamp(Num(Plugin.CfgBranchDays, 7), 1, 14);
            var today = DateTime.UtcNow.AddHours(Num(Plugin.CfgBranchShift, 3)).Date;
            for (int back = 0; back < days; back += 2)
            {
                if (era != _era) yield break;
                if (Fresh(userId, since) || _searches >= Num(Plugin.CfgBranchSearches, 60)) break;
                var rows = new List<Row>();
                var to = today.AddDays(-back);
                _searches++;
                yield return Find(login, to.AddDays(-1), to, rows);
                if (era != _era) yield break;
                if (rows.Count == 0) continue;
                yield return Mine(userId, rows, since, era);
            }
            if (era != _era) yield break;
            Rec(userId).Hour = Hours();
            _dirty = true;
            Plugin.Trace("[ветки] " + login + ": " + Text(userId));
        }

        private static IEnumerator Mine(int userId, List<Row> rows, long since, int era)
        {
            while (era == _era && !Fresh(userId, since) && _pages < Num(Plugin.CfgBranchPages, 30))
            {
                int at = -1, best = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (Fetched.Contains(rows[i].Uid)) continue;
                    int gain = Gain(rows[i]);
                    if (at < 0 || gain > best) { at = i; best = gain; }
                }
                if (at < 0) yield break;
                Fetched.Add(rows[at].Uid);
                _pages++;
                var reply = new Reply();
                yield return Get(Site + Detail + rows[at].Uid, reply);
                if (era != _era) yield break;
                if (reply.Body.Length == 0) continue;
                yield return Read(reply.Body);
            }
        }

        private static int Gain(Row row)
        {
            int n = 0;
            for (int i = 0; i < row.Ids.Count; i++) if (Stale(row.Ids[i])) n++;
            return n;
        }

        private static IEnumerator Find(string login, DateTime from, DateTime to, List<Row> rows)
        {
            if (!_session) yield return Session();
            if (!_session) yield break;
            var form = new WWWForm();
            form.AddField("beginDate", from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            form.AddField("endDate", to.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
            form.AddField("login", login, Encoding.UTF8);
            var sent = new Reply();
            yield return Post(Archive, form, sent);
            string go = sent.Location;
            if (go.Length == 0)
            {
                if (sent.Body.IndexOf(Detail, StringComparison.Ordinal) < 0)
                {
                    _session = false;
                    Plugin.Trace("[ветки] поиск по " + login + ": ответ " + sent.Code + ", переход не пришёл, боёв в ответе нет");
                    yield break;
                }
                List(sent.Body, rows);
                Plugin.Trace("[ветки] поиск по " + login + ": боёв " + rows.Count + " (из ответа на отправку)");
                yield break;
            }
            if (go.StartsWith("/")) go = Site + go;
            var page = new Reply();
            yield return Get(go, page);
            List(page.Body, rows);
            Plugin.Trace("[ветки] поиск по " + login + " " + from.ToString("dd.MM", CultureInfo.InvariantCulture)
                + "…" + to.ToString("dd.MM", CultureInfo.InvariantCulture) + ": страница " + page.Body.Length + " знаков, боёв " + rows.Count);
        }

        private static void List(string html, List<Row> rows)
        {
            int pos = 0;
            while (pos < html.Length)
            {
                int a = html.IndexOf("<tr", pos, StringComparison.Ordinal);
                if (a < 0) break;
                int b = html.IndexOf("<tr", a + 3, StringComparison.Ordinal);
                if (b < 0) b = html.Length;
                pos = b;
                int d = html.IndexOf(Detail, a, b - a, StringComparison.Ordinal);
                if (d < 0) continue;
                var row = new Row();
                row.Uid = Word(html, d + Detail.Length, b, '"', '\'');
                if (row.Uid.Length == 0) continue;
                int k = a;
                while (true)
                {
                    int p = html.IndexOf(Profile, k, StringComparison.Ordinal);
                    if (p < 0 || p >= b) break;
                    int id = Count(html, p + Profile.Length);
                    if (id > 0) row.Ids.Add(id);
                    k = p + Profile.Length;
                }
                rows.Add(row);
            }
        }

        private static IEnumerator Read(string html)
        {
            int pos = html.IndexOf(Record, StringComparison.Ordinal);
            if (pos < 0) pos = 0;
            var cls = new Dictionary<int, int>();
            var reg = new Dictionary<int, int>();
            var eli = new Dictionary<int, int>();
            var bad = new HashSet<int>();
            int steps = 0;
            while (pos < html.Length)
            {
                int end = html.IndexOf("<br>", pos, StringComparison.Ordinal);
                if (end < 0) end = html.Length;
                Line(html, pos, end, cls, reg, eli, bad);
                pos = end + 4;
                if (++steps % 500 == 0) yield return null;
            }
            Apply(cls, reg, eli, bad);
        }

        private static void Line(string s, int a, int b, Dictionary<int, int> cls, Dictionary<int, int> reg, Dictionary<int, int> eli, HashSet<int> bad)
        {
            if (b > s.Length) b = s.Length;
            if (b <= a) return;
            for (int i = Find(s, Ability, a, b); i >= 0; i = Find(s, Ability, i + Ability.Length, b))
            {
                int close = s.IndexOf(']', i, b - i);
                if (close < 0) break;
                if (Starts(s, close + 1, Owned)) continue;
                Take(Before(s, a, i), Skill(s, i + 1, close + 1), cls, reg, eli, bad);
            }
            for (int i = Find(s, Owned, a, b); i >= 0; i = Find(s, Owned, i + 1, b))
            {
                int open = s.LastIndexOf('[', i, i - a + 1);
                if (open < 0) continue;
                Take(After(s, i, b), Skill(s, open + 1, i + 1), cls, reg, eli, bad);
            }
        }

        private static int Find(string s, string part, int from, int to)
        {
            if (from >= to) return -1;
            return s.IndexOf(part, from, to - from, StringComparison.Ordinal);
        }

        private static bool Starts(string s, int p, string part)
        {
            return p >= 0 && p + part.Length <= s.Length && string.CompareOrdinal(s, p, part, 0, part.Length) == 0;
        }

        private static void Take(int userId, string skill, Dictionary<int, int> cls, Dictionary<int, int> reg, Dictionary<int, int> eli, HashSet<int> bad)
        {
            if (userId <= 0 || skill.Length == 0) return;
            string key = Norm(skill);
            int code;
            if (Skills.TryGetValue(key, out code))
            {
                Mark(userId, code / 4, cls, bad);
                var box = (code & 1) == 1 ? eli : reg;
                int branch = (code >> 1) & 1, had;
                if (box.TryGetValue(userId, out had) && had != branch) bad.Add(userId);
                else box[userId] = branch;
                return;
            }
            if (Roots.TryGetValue(key, out code)) Mark(userId, code, cls, bad);
        }

        private static void Mark(int userId, int c, Dictionary<int, int> cls, HashSet<int> bad)
        {
            int had;
            if (cls.TryGetValue(userId, out had) && had != c) bad.Add(userId);
            else cls[userId] = c;
        }

        private static void Apply(Dictionary<int, int> cls, Dictionary<int, int> reg, Dictionary<int, int> eli, HashSet<int> bad)
        {
            int fresh = 0;
            foreach (var kv in cls)
            {
                if (bad.Contains(kv.Key)) continue;
                var r = Rec(kv.Key);
                r.Cls = kv.Value;
                r.Hour = Hours();
                int v;
                if (reg.TryGetValue(kv.Key, out v)) r.Reg = v;
                if (eli.TryGetValue(kv.Key, out v)) r.Elite = v;
                fresh++;
            }
            if (fresh == 0) return;
            _dirty = true;
            Plugin.Trace("[ветки] из боя разобрано бойцов: " + fresh + (bad.Count > 0 ? ", спорных пропущено: " + bad.Count : ""));
        }

        private static int Before(string s, int a, int i)
        {
            int found = -1, k = a;
            while (true)
            {
                int p = s.IndexOf(Tag, k, StringComparison.Ordinal);
                if (p < 0 || p >= i) break;
                found = p;
                k = p + Tag.Length;
            }
            return found < 0 ? 0 : Count(s, found + Tag.Length);
        }

        private static int After(string s, int i, int b)
        {
            int p = s.IndexOf(Tag, i, StringComparison.Ordinal);
            return p < 0 || p >= b ? 0 : Count(s, p + Tag.Length);
        }

        private static string Skill(string s, int p, int b)
        {
            if (p < b && s[p] == '<')
            {
                int g = s.IndexOf('>', p);
                if (g < 0 || g >= b) return "";
                p = g + 1;
            }
            return Word(s, p, b, '<', ']');
        }

        private static string Word(string s, int p, int b, params char[] stop)
        {
            if (b > s.Length) b = s.Length;
            int e = p;
            while (e < b && Array.IndexOf(stop, s[e]) < 0) e++;
            return e <= p ? "" : s.Substring(p, e - p).Trim();
        }

        private static int Count(string s, int p)
        {
            int n = 0, e = p;
            while (e < s.Length && s[e] >= '0' && s[e] <= '9') { n = n * 10 + (s[e] - '0'); e++; }
            return e == p ? 0 : n;
        }

        private static string Norm(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool gap = false;
            foreach (char c in s)
            {
                char u = char.ToUpperInvariant(c);
                if (u == 'Ё') u = 'Е';
                if (u == ' ' || u == '\n' || u == '\r' || u == '\t' || u == ' ')
                {
                    if (sb.Length > 0) gap = true;
                    continue;
                }
                if (gap) { sb.Append(' '); gap = false; }
                sb.Append(u);
            }
            return sb.ToString();
        }

        private static void Tables()
        {
            if (_tables) return;
            _tables = true;
            Feed(SkillTable, Skills);
            Feed(RootTable, Roots);
        }

        private static void Feed(string raw, Dictionary<string, int> into)
        {
            foreach (var part in raw.Split('|'))
            {
                int i = part.LastIndexOf('=');
                if (i <= 0) continue;
                int code;
                if (int.TryParse(part.Substring(i + 1), out code)) into[Norm(part.Substring(0, i))] = code;
            }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            string raw = Plugin.CfgBranchCache == null ? "" : Plugin.CfgBranchCache.Value ?? "";
            foreach (var part in raw.Split(';'))
            {
                var bit = part.Split('.');
                if (bit.Length != 5) continue;
                int id;
                if (!int.TryParse(bit[0], out id) || id <= 0) continue;
                var r = new BranchRec();
                int v;
                r.Cls = int.TryParse(bit[1], out v) ? v : -1;
                r.Reg = int.TryParse(bit[2], out v) ? v : -1;
                r.Elite = int.TryParse(bit[3], out v) ? v : -1;
                long h;
                r.Hour = long.TryParse(bit[4], out h) ? h : 0;
                if (r.Cls < -1 || r.Cls >= ClassNames.Length) continue;
                if (r.Cls < 0) { r.Reg = -1; r.Elite = -1; }
                if (r.Reg > 1) r.Reg = -1;
                if (r.Elite > 1) r.Elite = -1;
                Known[id] = r;
            }
            if (Known.Count > 0) Plugin.Trace("[ветки] в памяти бойцов: " + Known.Count);
        }

        private static void Flush()
        {
            if (!_dirty || Time.unscaledTime < _saveAt) return;
            _saveAt = Time.unscaledTime + 20f;
            _dirty = false;
            if (Plugin.CfgBranchCache == null) return;
            int cap = Mathf.Clamp(Num(Plugin.CfgBranchKeep, 4000), 200, 20000);
            var ids = new List<int>(Known.Keys);
            if (ids.Count > cap)
            {
                ids.Sort(delegate (int x, int y) { return Known[y].Hour.CompareTo(Known[x].Hour); });
                for (int i = cap; i < ids.Count; i++) Known.Remove(ids[i]);
                ids.RemoveRange(cap, ids.Count - cap);
            }
            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                var r = Known[id];
                if (sb.Length > 0) sb.Append(';');
                sb.Append(id).Append('.').Append(r.Cls).Append('.').Append(r.Reg).Append('.').Append(r.Elite).Append('.').Append(r.Hour);
            }
            Plugin.CfgBranchCache.Value = sb.ToString();
        }

        private static IEnumerator Session()
        {
            var r = new Reply();
            yield return Get(Archive, r);
            if (r.Body.Length == 0)
            {
                Plugin.Trace("[ветки] архив битв не ответил: " + (r.Error.Length > 0 ? r.Error : "пусто") + ", код " + r.Code);
                yield break;
            }
            int i = r.Cookie.IndexOf("JSESSIONID=", StringComparison.Ordinal);
            if (i >= 0)
            {
                int j = r.Cookie.IndexOf(';', i);
                _cookie = j < 0 ? r.Cookie.Substring(i) : r.Cookie.Substring(i, j - i);
            }
            _session = true;
            Plugin.Trace("[ветки] сессия открыта, страница " + r.Body.Length + " знаков"
                + (_cookie.Length > 0 ? ", ключ виден" : ", ключ ведёт сам клиент"));
        }

        private static IEnumerator Pause()
        {
            while (Time.unscaledTime < _nextAt) yield return null;
            float gap = Plugin.CfgBranchGap == null ? 1.5f : Plugin.CfgBranchGap.Value;
            _nextAt = Time.unscaledTime + Mathf.Max(0.25f, gap);
        }

        private static IEnumerator Get(string url, Reply reply)
        {
            yield return Pause();
            var req = UnityWebRequest.Get(url);
            Dress(req);
            yield return req.SendWebRequest();
            Grab(req, reply);
            req.Dispose();
        }

        private static IEnumerator Post(string url, WWWForm form, Reply reply)
        {
            yield return Pause();
            var req = UnityWebRequest.Post(url, form);
            Dress(req);
            req.redirectLimit = 0;
            yield return req.SendWebRequest();
            reply.Location = req.GetResponseHeader("Location") ?? "";
            Grab(req, reply);
            req.Dispose();
        }

        private static void Dress(UnityWebRequest req)
        {
            req.timeout = 25;
            Head(req, "User-Agent", "NewAgeQoL");
            Head(req, "Accept-Encoding", "gzip");
            if (_cookie.Length > 0) Head(req, "Cookie", _cookie);
        }

        private static void Head(UnityWebRequest req, string name, string value)
        {
            try { req.SetRequestHeader(name, value); }
            catch (Exception e) { Plugin.Trace("[ветки] заголовок " + name + ": " + e.Message); }
        }

        private static void Grab(UnityWebRequest req, Reply reply)
        {
            string set = req.GetResponseHeader("Set-Cookie");
            if (!string.IsNullOrEmpty(set)) reply.Cookie = set;
            reply.Code = req.responseCode;
            reply.Error = req.error ?? "";
            var handler = req.downloadHandler;
            reply.Body = handler == null ? "" : Unpack(handler.data);
        }

        private static string Unpack(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b)
            {
                try
                {
                    using (var src = new MemoryStream(data))
                    using (var gz = new GZipStream(src, CompressionMode.Decompress))
                    using (var dst = new MemoryStream())
                    {
                        var buf = new byte[16384];
                        int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                        return Encoding.UTF8.GetString(dst.ToArray());
                    }
                }
                catch (Exception e) { Plugin.Trace("[ветки] распаковка: " + e.Message); return ""; }
            }
            return Encoding.UTF8.GetString(data);
        }
    }
}
