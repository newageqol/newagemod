using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal sealed class KuSkill
    {
        internal int Id;
        internal int Parent;
        internal string Name;
        internal string Kind;
        internal readonly string[] Texts = new string[KuData.Top];
        internal readonly int[][] Costs = new int[KuData.Top][];
    }

    internal sealed class KuSub
    {
        internal int N;
        internal string Name;
        internal int Level;
    }

    internal sealed class KuClass
    {
        internal int Id;
        internal string Name;
        internal int[] Left = new int[0];
        internal int[] Right = new int[0];
        internal string LeftName = "";
        internal string RightName = "";
        internal readonly List<KuSub> Subs = new List<KuSub>();

        internal int Root => Left.Length > 0 ? Left[0] : 0;

        internal int Side(int skill)
        {
            if (skill == Root) return 0;
            if (Array.IndexOf(Left, skill) > 0) return 1;
            if (Array.IndexOf(Right, skill) > 0) return 2;
            return -1;
        }

        internal KuSub Sub(int n)
        {
            foreach (var sub in Subs) if (sub.N == n) return sub;
            return null;
        }
    }

    internal static class KuData
    {
        internal const int Top = 5;
        internal static readonly int[] Gain = { 6, 10, 14, 18, 22, 26 };
        internal static readonly string[] Steps = { "Новичок", "Продвинутый", "Эксперт", "Мастер", "Грандмастер" };

        internal const string Url = "https://raw.githubusercontent.com/newageqol/newagemod/main/data/ku.txt";

        private static readonly List<KuClass> ClassList = new List<KuClass>();
        private static readonly Dictionary<int, KuSkill> Skills = new Dictionary<int, KuSkill>();
        private static bool _loaded;
        private static bool _fetched;

        internal static int Version { get; private set; }

        internal static string CacheFile => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "ku.txt");

        internal static List<KuClass> Classes
        {
            get
            {
                Load();
                return ClassList;
            }
        }

        internal static bool Ready => Classes.Count > 0 && Skills.Count > 0;

        internal static KuClass Class(int id)
        {
            foreach (var klass in Classes) if (klass.Id == id) return klass;
            return null;
        }

        internal static KuSkill Skill(int id)
        {
            Load();
            KuSkill skill;
            return Skills.TryGetValue(id, out skill) ? skill : null;
        }

        internal static int Points(int subs)
        {
            int sum = 0;
            for (int i = 0; i < subs && i < Gain.Length; i++) sum += Gain[i];
            return sum;
        }

        internal static int Cost(int level) => level * (level + 1) / 2;

        internal static string Step(int level) => level >= 1 && level <= Top ? Steps[level - 1] : "не изучено";

        internal static void Fetch()
        {
            if (_fetched || Plugin.Instance == null) return;
            _fetched = true;
            Plugin.Instance.StartCoroutine(Download());
        }

        private static IEnumerator Download()
        {
            var req = UnityWebRequest.Get(Url + "?t=" + DateTime.UtcNow.Ticks);
            req.timeout = 60;
            req.SetRequestHeader("User-Agent", "Mozilla/5.0 NewAgeQoL");
            yield return req.SendWebRequest();
            long code = req.responseCode;
            string error = req.error;
            string text = code == 200 && string.IsNullOrEmpty(error) && req.downloadHandler != null ? req.downloadHandler.text : null;
            req.Dispose();
            if (text == null || !Read(text).Full)
            {
                Plugin.Trace("[калькулятор ку] умения с GitHub не пришли: " + (string.IsNullOrEmpty(error) ? "код " + code : error));
                yield break;
            }
            text = text.Replace("\r\n", "\n");
            if (text == Cached())
            {
                Plugin.Trace("[калькулятор ку] умения на GitHub те же, что в кэше");
                yield break;
            }
            if (!Store(text)) yield break;
            _loaded = false;
            Load();
            Version++;
            Plugin.Trace("[калькулятор ку] умения обновлены с GitHub");
        }

        private static bool Store(string text)
        {
            try
            {
                string file = CacheFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + ".new";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Warn("[калькулятор ку] кэш умений не записан: " + e.Message);
                return false;
            }
        }

        private static string Cached()
        {
            try { return File.Exists(CacheFile) ? File.ReadAllText(CacheFile, Encoding.UTF8) : null; }
            catch { return null; }
        }

        private sealed class Lines
        {
            internal readonly List<string[]> Classes = new List<string[]>();
            internal readonly List<string[]> Subs = new List<string[]>();
            internal readonly List<string[]> Trees = new List<string[]>();
            internal readonly List<string[]> Skills = new List<string[]>();
            internal readonly List<string[]> Costs = new List<string[]>();

            internal bool Full
            {
                get
                {
                    if (Trees.Count < 6) return false;
                    var known = new HashSet<int>();
                    foreach (var cell in Skills) known.Add(Int(cell[1]));
                    foreach (var cell in Skills)
                    {
                        int parent = Int(cell[2]);
                        if (parent != 0 && !known.Contains(parent)) return false;
                    }
                    foreach (var cell in Trees)
                        for (int side = 2; side <= 3; side++)
                        {
                            var ids = Ids(cell[side]);
                            if (ids.Length != 8) return false;
                            foreach (int id in ids) if (!known.Contains(id)) return false;
                        }
                    return true;
                }
            }
        }

        private static Lines Read(string text)
        {
            var lines = new Lines();
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var cell = line.Split('\t');
                    switch (cell[0])
                    {
                        case "C":
                            if (cell.Length >= 3) lines.Classes.Add(cell);
                            break;
                        case "S":
                            if (cell.Length >= 5) lines.Subs.Add(cell);
                            break;
                        case "KB":
                            if (cell.Length >= 6) lines.Trees.Add(cell);
                            break;
                        case "K":
                            if (cell.Length >= 5 + Top) lines.Skills.Add(cell);
                            break;
                        case "KC":
                            if (cell.Length >= 2 + Top) lines.Costs.Add(cell);
                            break;
                    }
                }
            }
            return lines;
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            ClassList.Clear();
            Skills.Clear();
            try
            {
                string rules = WardrobeData.Rules();
                if (rules == null) { Plugin.Warn("[калькулятор ку] в сборке нет правил"); return; }
                var own = Read(rules);
                var tree = own;
                string from = "вшитые";
                string cached = Cached();
                if (cached != null)
                {
                    var got = Read(cached);
                    if (got.Full) { tree = got; from = "из кэша GitHub"; }
                }
                foreach (var cell in own.Classes) ClassList.Add(new KuClass { Id = Int(cell[1]), Name = cell[2] });
                foreach (var cell in own.Subs)
                {
                    var klass = FindLoaded(Int(cell[1]));
                    if (klass != null) klass.Subs.Add(new KuSub { N = Int(cell[2]), Name = cell[3], Level = Int(cell[4]) });
                }
                foreach (var cell in tree.Skills)
                {
                    var skill = new KuSkill { Id = Int(cell[1]), Parent = Int(cell[2]), Name = cell[3], Kind = cell[4] };
                    for (int i = 0; i < Top; i++) skill.Texts[i] = cell[5 + i];
                    Skills[skill.Id] = skill;
                }
                foreach (var cell in tree.Trees)
                {
                    var klass = FindLoaded(Int(cell[1]));
                    if (klass == null) continue;
                    klass.Left = Ids(cell[2]);
                    klass.Right = Ids(cell[3]);
                    klass.LeftName = cell[4];
                    klass.RightName = cell[5];
                }
                foreach (var cell in tree.Costs)
                {
                    KuSkill skill;
                    if (!Skills.TryGetValue(Int(cell[1]), out skill)) continue;
                    for (int i = 0; i < Top; i++) skill.Costs[i] = Ids(cell[2 + i]);
                }
                ClassList.RemoveAll(k => k.Left.Length != 8 || k.Right.Length != 8);
                Plugin.Trace("[калькулятор ку] классов " + ClassList.Count + ", умений " + Skills.Count + ", данные " + from);
            }
            catch (Exception e)
            {
                ClassList.Clear();
                Skills.Clear();
                Plugin.Warn("[калькулятор ку] правила не прочитаны: " + e.Message);
            }
        }

        private static KuClass FindLoaded(int id)
        {
            foreach (var klass in ClassList) if (klass.Id == id) return klass;
            return null;
        }

        private static int[] Ids(string text)
        {
            var parts = text.Split(',');
            var list = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) list[i] = Int(parts[i]);
            return list;
        }

        private static int Int(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
    }
}
