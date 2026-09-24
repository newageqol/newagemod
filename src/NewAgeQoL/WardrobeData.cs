using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NewAgeQoL
{
    internal sealed class WardrobeThing
    {
        internal int Id;
        internal int Sub;
        internal int Rarity;
        internal int Level;
        internal int ItemLevel;
        internal int Classes;
        internal string Name;
        internal string Image;
        internal readonly int[] Req = new int[7];
        internal readonly int[] Bonus = new int[7];
        internal int Energy;
        internal readonly int[] Armor = new int[5];
        internal readonly int[] Magic = new int[3];
        internal int DamageMin;
        internal int DamageMax;
        internal int Range;
        internal bool Art;
    }

    internal sealed class WardrobeArtRule
    {
        internal int Sub;
        internal int Level;
        internal int Points;
        internal int MaxStat;
        internal int MaxArmor;
        internal int MaxMagic;
        internal int MaxEnergy;
        internal int DamageMin;
        internal int DamageMax;
        internal int Range;
        internal int Price;
    }

    internal sealed class WardrobeArtKind
    {
        internal int Sub;
        internal string Name;
        internal string Image;
    }

    internal sealed class WardrobeRace
    {
        internal int Id;
        internal string Name;
        internal readonly int[] Base = new int[7];
        internal int Fortress;
        internal readonly List<int> Classes = new List<int>();
        internal readonly SortedDictionary<int, int[]> Magic = new SortedDictionary<int, int[]>();
    }

    internal sealed class WardrobeSub
    {
        internal int Class;
        internal int N;
        internal string Name;
        internal int Level;
        internal readonly int[] Req = new int[7];
        internal readonly int[] Bonus = new int[7];
        internal readonly int[] Armor = new int[5];
        internal readonly int[] Magic = new int[3];
    }

    internal sealed class WardrobeClass
    {
        internal int Id;
        internal string Name;
        internal readonly List<WardrobeSub> Subs = new List<WardrobeSub>();
    }

    internal sealed class WardrobeRank
    {
        internal int N;
        internal string Name;
        internal int MinCon;
        internal double Mult;
    }

    internal static class WardrobeData
    {
        internal const int Ranger = 1;

        internal static readonly string[] StatNames = { "Сила", "Ловкость", "Сложение", "Интеллект", "Мудрость", "Удача", "Реакция" };
        internal static readonly int[] StatOrder = { 0, 1, 5, 6, 3, 4, 2 };
        internal static readonly string[] ArmorNames = { "Голова", "Корпус", "Левая рука", "Правая рука", "Ноги" };
        internal static readonly string[] MagicNames = { "Рассвет", "Полнолуние", "Астрал" };
        internal static readonly int[] RaceOrder = { 13, 12, 11, 8, 9, 10 };
        internal static readonly int[] RarityOrder = { 1, 2, 3, 5, 6, 7, 8 };

        private static readonly int[] OneHand = { 12, 13, 15, 31, 33, 34, 37, 38, 39 };
        private static readonly int[] TwoHand = { 8, 9, 14, 30, 32, 35, 36 };
        private static readonly int[][] Groups = { new[] { 7, 8, 9, 10 }, new[] { 15, 16, 17, 18 }, new[] { 25, 26 } };

        private sealed class Pack
        {
            internal readonly List<WardrobeRace> Races = new List<WardrobeRace>();
            internal readonly List<WardrobeClass> Classes = new List<WardrobeClass>();
            internal readonly List<WardrobeRank> Ranks = new List<WardrobeRank>();
            internal readonly List<WardrobeThing> Things = new List<WardrobeThing>();
            internal readonly Dictionary<int, WardrobeThing> ById = new Dictionary<int, WardrobeThing>();
            internal readonly List<WardrobeArtKind> ArtKinds = new List<WardrobeArtKind>();
            internal readonly List<WardrobeArtRule> ArtRules = new List<WardrobeArtRule>();
            internal readonly StringBuilder Meta = new StringBuilder();
            internal string Stamp = "";

            internal WardrobeRace Race(int id)
            {
                foreach (var race in Races) if (race.Id == id) return race;
                return null;
            }

            internal WardrobeClass Class(int id)
            {
                foreach (var klass in Classes) if (klass.Id == id) return klass;
                return null;
            }
        }

        private static Pack _pack = new Pack();
        private static bool _loaded;
        private static string _error;

        internal static List<WardrobeRace> Races => _pack.Races;
        internal static List<WardrobeClass> Classes => _pack.Classes;
        internal static List<WardrobeRank> Ranks => _pack.Ranks;
        internal static List<WardrobeThing> Things => _pack.Things;
        internal static string Stamp => _pack.Stamp;
        internal static string Meta => _pack.Meta.ToString();
        internal static string Source = "";
        private static string _text;
        internal static string Current => _text;
        internal static string Error => _error;

        internal static string CacheFile => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "wardrobe.txt");

        internal static bool Ready()
        {
            if (!_loaded) Load();
            return _error == null && _pack.Things.Count > 0;
        }

        internal static bool HasRules()
        {
            if (!_loaded) Load();
            return _pack.Races.Count > 0 && _pack.Classes.Count > 0 && _pack.Ranks.Count > 0;
        }

        internal static WardrobeRace Race(int id)
        {
            var race = _pack.Race(id);
            return race ?? (_pack.Races.Count > 0 ? _pack.Races[0] : null);
        }

        internal static WardrobeClass Class(int id)
        {
            var klass = _pack.Class(id);
            return klass ?? (_pack.Classes.Count > 0 ? _pack.Classes[0] : null);
        }

        internal static WardrobeThing Thing(int id)
        {
            WardrobeThing thing;
            return _pack.ById.TryGetValue(id, out thing) ? thing : null;
        }

        internal static WardrobeThing Named(string name, string image, int level)
        {
            foreach (var thing in _pack.Things)
                if (thing.Name == name && thing.Image == image && thing.ItemLevel == level) return thing;
            return null;
        }

        internal static WardrobeThing Look(string image, int? level, int rarity)
        {
            if (string.IsNullOrEmpty(image)) return null;
            WardrobeThing found = null;
            foreach (var thing in _pack.Things)
            {
                if (thing.Id > 0 || !string.Equals(thing.Image, image, StringComparison.OrdinalIgnoreCase)) continue;
                if ((level != null && thing.ItemLevel != level.Value) || thing.Rarity != rarity) continue;
                if (found != null) return null;
                found = thing;
            }
            return found;
        }

        internal static WardrobeThing Same(WardrobeThing old)
        {
            if (old == null) return null;
            if (old.Art) return WardrobeArt.Recheck(old);
            if (old.Id > 0)
            {
                var byId = Thing(old.Id);
                if (byId != null) return byId;
            }
            foreach (var thing in _pack.Things)
                if (thing.Name == old.Name && thing.Image == old.Image && thing.Level == old.Level) return thing;
            return null;
        }

        internal static bool IsTwoHand(int sub) => Array.IndexOf(TwoHand, sub) >= 0;

        internal static bool IsSlot(int slot) => (slot >= 1 && slot <= 18) || slot == 25 || slot == 26;

        internal static List<WardrobeArtKind> ArtKinds(int slot)
        {
            int target = slot == 11 ? 12 : slot;
            var list = new List<WardrobeArtKind>();
            foreach (var kind in _pack.ArtKinds)
                if (Fits(target, kind.Sub) && ArtLevels(kind.Sub).Count > 0) list.Add(kind);
            return list;
        }

        internal static WardrobeArtKind ArtKind(int sub)
        {
            foreach (var kind in _pack.ArtKinds) if (kind.Sub == sub) return kind;
            return null;
        }

        internal static WardrobeArtRule ArtRule(int sub, int level)
        {
            foreach (var rule in _pack.ArtRules) if (rule.Sub == sub && rule.Level == level) return rule;
            return null;
        }

        internal static List<int> ArtLevels(int sub)
        {
            var list = new List<int>();
            foreach (var rule in _pack.ArtRules) if (rule.Sub == sub) list.Add(rule.Level);
            list.Sort();
            return list;
        }

        internal static int[] Group(int slot)
        {
            foreach (var group in Groups)
                if (Array.IndexOf(group, slot) >= 0) return group;
            return null;
        }

        internal static bool Fits(int slot, int sub)
        {
            switch (slot)
            {
                case 1: return sub == 1;
                case 2: return sub == 2;
                case 3: return sub == 3;
                case 4: return sub == 4;
                case 5: return sub == 5;
                case 6: return sub == 6;
                case 7: case 8: case 9: case 10: return sub == 7;
                case 11: return Array.IndexOf(OneHand, sub) >= 0;
                case 12: return Array.IndexOf(OneHand, sub) >= 0 || Array.IndexOf(TwoHand, sub) >= 0;
                case 13: return sub == 10;
                case 14: return sub == 11;
                case 15: case 16: case 17: case 18: return sub == 40;
                case 25: case 26: return sub == 55;
                default: return false;
            }
        }

        internal static string SlotName(int slot)
        {
            switch (slot)
            {
                case 1: return "Шлем";
                case 2: return "Амулет";
                case 3: return "Доспех";
                case 4: return "Перчатки";
                case 5: return "Наручи";
                case 6: return "Пояс";
                case 7: case 8: case 9: case 10: return "Кольцо";
                case 11: return "Левая рука";
                case 12: return "Правая рука";
                case 13: return "Поножи";
                case 14: return "Сапоги";
                case 15: case 16: case 17: case 18: return "Реликвия";
                case 25: case 26: return "Серьга";
                default: return "Слот " + slot;
            }
        }

        internal static string RarityName(int rarity)
        {
            switch (rarity)
            {
                case 1: return "обычная";
                case 2: return "крафтовая";
                case 3: return "раритетная";
                case 4: return "артефакт";
                case 5: return "награда";
                case 6: return "эпическая";
                case 7: return "ратника";
                case 8: return "мифическая";
                default: return "";
            }
        }

        internal static string RarityGroup(int rarity)
        {
            switch (rarity)
            {
                case 1: return "Обычные";
                case 2: return "Крафтовые";
                case 3: return "Раритетные";
                case 4: return "Артефакты";
                case 5: return "Награды";
                case 6: return "Эпические";
                case 7: return "Ратника";
                case 8: return "Мифические";
                default: return "";
            }
        }

        internal static UnityEngine.Color32 RarityColor(int rarity)
        {
            switch (rarity)
            {
                case 2: return new UnityEngine.Color32(120, 205, 100, 255);
                case 3: return new UnityEngine.Color32(125, 150, 255, 255);
                case 4: return new UnityEngine.Color32(255, 96, 72, 255);
                case 5: return new UnityEngine.Color32(236, 150, 80, 255);
                case 6: return new UnityEngine.Color32(80, 180, 255, 255);
                case 7: return new UnityEngine.Color32(225, 105, 225, 255);
                case 8: return new UnityEngine.Color32(185, 120, 245, 255);
                default: return new UnityEngine.Color32(236, 226, 200, 255);
            }
        }

        private static void Load()
        {
            _loaded = true;
            try
            {
                if (File.Exists(CacheFile))
                {
                    string text = File.ReadAllText(CacheFile, Encoding.UTF8);
                    string when = File.GetLastWriteTime(CacheFile).ToString("dd.MM.yyyy");
                    if (Apply(text, "обновлены с сайта " + when)) return;
                    Plugin.Warn("[переодевалка] кэш вещей битый, вещи скачаются заново");
                }
            }
            catch (Exception e) { Plugin.Warn("[переодевалка] кэш вещей не прочитан: " + e.Message); }
            try
            {
                string rules = Rules();
                if (rules == null) { _error = "в сборке нет правил игры"; return; }
                var pack = Read(rules);
                if (pack.Races.Count == 0 || pack.Classes.Count == 0 || pack.Ranks.Count == 0) { _error = "правила игры пустые"; return; }
                _pack = pack;
                _error = null;
                Source = "вещей пока нет";
            }
            catch (Exception e)
            {
                _error = "правила игры не прочитаны: " + e.Message;
                Plugin.Fault("[переодевалка] " + _error);
            }
        }

        internal static string Rules()
        {
            var bytes = Grab("wardrobe-rules.txt");
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        private static Pack Read(string text)
        {
            var pack = new Pack();
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null) Parse(pack, line);
            }
            foreach (var klass in pack.Classes) klass.Subs.Sort((a, b) => a.N.CompareTo(b.N));
            pack.Ranks.Sort((a, b) => a.N.CompareTo(b.N));
            return pack;
        }

        internal static bool Apply(string text, string source)
        {
            var pack = Read(text);
            if (pack.Races.Count == 0 || pack.Classes.Count == 0 || pack.Ranks.Count == 0 || pack.Things.Count < 500) return false;
            if (pack.ArtRules.Count == 0) Borrow(pack);
            _pack = pack;
            _error = null;
            _loaded = true;
            _text = text;
            Source = source;
            Plugin.Trace("[переодевалка] данные " + pack.Stamp + " (" + source + "): рас " + pack.Races.Count
                         + ", классов " + pack.Classes.Count + ", вещей " + pack.Things.Count);
            return true;
        }

        private static void Borrow(Pack pack)
        {
            string rules = Rules();
            if (rules == null) return;
            foreach (var line in rules.Split('\n'))
                if (line.StartsWith("T\t") || line.StartsWith("A\t")) Parse(pack, line.TrimEnd('\r'));
        }

        private static void Parse(Pack pack, string line)
        {
            if (line.Length < 2) return;
            var cell = line.Split('\t');
            if (cell[0] != "I" && cell[0] != "V") pack.Meta.Append(line).Append('\n');
            switch (cell[0])
            {
                case "V":
                    pack.Stamp = cell[1];
                    break;
                case "F":
                    pack.Ranks.Add(new WardrobeRank
                    {
                        N = Int(cell[1]),
                        Name = cell[2],
                        MinCon = Int(cell[3]),
                        Mult = double.Parse(cell[4], CultureInfo.InvariantCulture)
                    });
                    break;
                case "R":
                {
                    var race = new WardrobeRace { Id = Int(cell[1]), Name = cell[2], Fortress = Int(cell[4]) };
                    Fill(race.Base, cell[3]);
                    foreach (var part in cell[5].Split(',')) if (part.Length > 0) race.Classes.Add(Int(part));
                    pack.Races.Add(race);
                    break;
                }
                case "M":
                {
                    var race = pack.Race(Int(cell[1]));
                    if (race == null) break;
                    var magic = new int[3];
                    Fill(magic, cell[3]);
                    race.Magic[Int(cell[2])] = magic;
                    break;
                }
                case "C":
                    pack.Classes.Add(new WardrobeClass { Id = Int(cell[1]), Name = cell[2] });
                    break;
                case "T":
                    if (cell.Length < 4) break;
                    pack.ArtKinds.Add(new WardrobeArtKind { Sub = Int(cell[1]), Name = cell[2], Image = cell[3] });
                    break;
                case "A":
                {
                    if (cell.Length < 11) break;
                    var rule = new WardrobeArtRule
                    {
                        Sub = Int(cell[1]),
                        Level = Int(cell[2]),
                        Points = Int(cell[3]),
                        MaxStat = Int(cell[4]),
                        MaxArmor = Int(cell[5]),
                        MaxMagic = Int(cell[6]),
                        MaxEnergy = Int(cell[7]),
                        Range = Int(cell[9]),
                        Price = Int(cell[10])
                    };
                    var damage = cell[8].Split('-');
                    if (damage.Length == 2) { rule.DamageMin = Int(damage[0]); rule.DamageMax = Int(damage[1]); }
                    if (rule.Points > 0) pack.ArtRules.Add(rule);
                    break;
                }
                case "S":
                {
                    var klass = pack.Class(Int(cell[1]));
                    if (klass == null) break;
                    var sub = new WardrobeSub { Class = klass.Id, N = Int(cell[2]), Name = cell[3], Level = Int(cell[4]) };
                    Fill(sub.Req, cell[5]);
                    Fill(sub.Bonus, cell[6]);
                    Fill(sub.Armor, cell[7]);
                    Fill(sub.Magic, cell[8]);
                    klass.Subs.Add(sub);
                    break;
                }
                case "I":
                {
                    if (cell.Length < 16) break;
                    var thing = new WardrobeThing
                    {
                        Id = Int(cell[1]),
                        Sub = Int(cell[2]),
                        Rarity = Int(cell[3]),
                        Level = Int(cell[4]),
                        ItemLevel = Int(cell[5]),
                        Classes = Int(cell[6]),
                        Name = cell[7],
                        Image = cell[8],
                        Energy = Int(cell[11])
                    };
                    Fill(thing.Req, cell[9]);
                    Fill(thing.Bonus, cell[10]);
                    Fill(thing.Armor, cell[12]);
                    Fill(thing.Magic, cell[13]);
                    var damage = cell[14].Split('-');
                    if (damage.Length == 2) { thing.DamageMin = Int(damage[0]); thing.DamageMax = Int(damage[1]); }
                    thing.Range = Int(cell[15]);
                    pack.Things.Add(thing);
                    if (thing.Id > 0) pack.ById[thing.Id] = thing;
                    break;
                }
            }
        }

        private static void Fill(int[] target, string csv)
        {
            var parts = csv.Split(',');
            for (int i = 0; i < target.Length && i < parts.Length; i++) target[i] = Int(parts[i]);
        }

        private static int Int(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        private static byte[] Grab(string tail)
        {
            var asm = typeof(WardrobeData).Assembly;
            string found = null;
            foreach (var one in asm.GetManifestResourceNames())
                if (one.EndsWith("." + tail, StringComparison.OrdinalIgnoreCase)) { found = one; break; }
            if (found == null) return null;
            using (var stream = asm.GetManifestResourceStream(found))
            using (var box = new MemoryStream())
            {
                stream.CopyTo(box);
                return box.ToArray();
            }
        }
    }
}
