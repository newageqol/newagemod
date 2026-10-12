using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Chat;

namespace NewAgeQoL
{
    internal static class FightLang
    {
        private const int CacheSize = 4000;
        private const int TellLimit = 40;
        private static readonly Regex Token = new Regex(@"<span\s*class='(\w{1,2})'\s*(?:data-id='(-?\d+)')?>|</span>|<br>", RegexOptions.IgnoreCase);
        private static readonly Regex Bracket = new Regex(@"\[([^\[\]<>]+)\]");
        private static readonly Regex Bare = new Regex(@"(?<=: )([А-ЯЁC][А-ЯЁC \-]{2,}?)(?=[,.])");
        private static readonly Regex Player = new Regex(@"<span\s*class='cu'\s*data-id='\d+'>.*?</span>|<[^>]+>", RegexOptions.IgnoreCase);
        private static readonly Regex Russian = new Regex("[ЫыЭэЪъЁё]");
        private static readonly HashSet<string> Names = new HashSet<string> { "cd", "ca", "cs", "cq", "ce", "ut" };
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Told = new HashSet<string>(StringComparer.Ordinal);

        private static KeyValuePair<string, string>[] _phrases;
        private static Dictionary<string, string> _names;
        private static Dictionary<string, string> _races;
        private static Regex _race;

        internal static void Take(List<KeyValuePair<string, string>> phrases, Dictionary<string, string> names, Dictionary<string, string> races)
        {
            phrases.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            Regex race = null;
            if (races.Count > 0)
                race = new Regex(@"(?<!\w)(" + string.Join("|", races.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + @")(?!\w)");
            lock (Cache)
            {
                _names = names;
                _races = races;
                _race = race;
                _phrases = phrases.ToArray();
                Cache.Clear();
            }
        }

        internal static string Text(string text)
        {
            if (string.IsNullOrEmpty(text) || _phrases == null) return text;
            lock (Cache)
            {
                string done;
                if (Cache.TryGetValue(text, out done)) return done;
                done = Build(text);
                if (Cache.Count >= CacheSize) Cache.Clear();
                Cache[text] = done;
                Tell(done);
                return done;
            }
        }

        private static string Build(string text)
        {
            var sb = new StringBuilder(text.Length + 16);
            string kind = null, id = null;
            int pos = 0;
            foreach (Match m in Token.Matches(text))
            {
                sb.Append(Segment(text.Substring(pos, m.Index - pos), kind, id));
                sb.Append(m.Value);
                if (m.Groups[1].Success)
                {
                    kind = m.Groups[1].Value.ToLowerInvariant();
                    id = m.Groups[2].Success ? m.Groups[2].Value : null;
                }
                else if (m.Value.StartsWith("</", StringComparison.Ordinal))
                {
                    kind = null;
                    id = null;
                }
                pos = m.Index + m.Length;
            }
            sb.Append(Segment(text.Substring(pos), kind, id));
            return sb.ToString();
        }

        private static string Segment(string seg, string kind, string id)
        {
            if (seg.Length == 0) return seg;
            if (kind == "cu") return id != null && id.StartsWith("-", StringComparison.Ordinal) ? Look(_races, seg) : seg;
            if (kind != null && Names.Contains(kind)) return Look(_names, seg);
            return Plain(seg);
        }

        private static string Look(Dictionary<string, string> map, string seg)
        {
            string bare = seg.Trim(), to;
            if (!map.TryGetValue(bare, out to)) return seg;
            return bare.Length == seg.Length ? to : seg.Replace(bare, to);
        }

        private static string Plain(string s)
        {
            s = Bracket.Replace(s, m => "[" + string.Join(", ", m.Groups[1].Value.Split(',').Select(p => Name(p.Trim())).ToArray()) + "]");
            s = Bare.Replace(s, m => Name(m.Value));
            foreach (var p in _phrases)
                if (s.IndexOf(p.Key, StringComparison.Ordinal) >= 0) s = s.Replace(p.Key, p.Value);
            if (_race != null) s = _race.Replace(s, m => _races[m.Value]);
            return s;
        }

        private static string Name(string s)
        {
            string to;
            return _names.TryGetValue(s, out to) ? to : s;
        }

        private static void Tell(string done)
        {
            if (Told.Count >= TellLimit) return;
            string seen = Player.Replace(done, "");
            if (!Russian.IsMatch(seen) || !Told.Add(seen)) return;
            Plugin.Trace("[lang] combat line partly in Russian: " + seen);
        }
    }

    [HarmonyPatch(typeof(ChatContent), "AddSystemMessage")]
    public static class FightLangPatch
    {
        private static void Prefix(ChatResponseMessage resp, ref string __state)
        {
            __state = null;
            if (!Lang.On || resp == null || resp.Text == null) return;
            try
            {
                string to = FightLang.Text(resp.Text);
                if (to == resp.Text) return;
                __state = resp.Text;
                resp.Text = to;
            }
            catch (Exception e) { Plugin.Trace("[lang] combat line: " + e.Message); }
        }

        private static Exception Finalizer(ChatResponseMessage resp, string __state, Exception __exception)
        {
            if (__state != null && resp != null) resp.Text = __state;
            return __exception;
        }
    }
}
