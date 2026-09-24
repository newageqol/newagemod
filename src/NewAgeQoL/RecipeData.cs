using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal sealed class RecipeThing
    {
        internal int Id;
        internal string Name;
        internal string Image;
        internal int Rarity;
        internal int Level;
    }

    internal sealed class RecipePart
    {
        internal int Id;
        internal int Count;
    }

    internal sealed class Recipe
    {
        internal int Id;
        internal string Name;
        internal string Image;
        internal int Rarity;
        internal int Profession;
        internal int Skill;
        internal int Chance;
        internal string Price;
        internal int Result;
        internal int ResultCount;
        internal int ResultLevel;
        internal int ResultRarity;
        internal string ResultImage;
        internal readonly List<RecipePart> Parts = new List<RecipePart>();
        internal string Find;
    }

    internal static class RecipeData
    {
        internal const string Url = "https://raw.githubusercontent.com/newageqol/newagemod/main/data/recipes.txt";

        private sealed class Pack
        {
            internal string Stamp;
            internal readonly List<Recipe> All = new List<Recipe>();
            internal readonly Dictionary<int, RecipeThing> Things = new Dictionary<int, RecipeThing>();
            internal readonly Dictionary<int, List<Recipe>> ByResult = new Dictionary<int, List<Recipe>>();
            internal readonly List<int> Crafts = new List<int>();
        }

        private static bool _loaded;
        private static bool _fetched;
        private static Pack _pack = new Pack();

        internal static int Version { get; private set; }

        internal static string CacheFile => Path.Combine(Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "recipes.txt");

        internal static string Stamp
        {
            get
            {
                Load();
                return _pack.Stamp;
            }
        }

        internal static List<Recipe> Recipes
        {
            get
            {
                Load();
                return _pack.All;
            }
        }

        internal static List<int> Professions
        {
            get
            {
                Load();
                return _pack.Crafts;
            }
        }

        internal static bool Ready => Recipes.Count > 0;

        internal static RecipeThing Thing(int id)
        {
            Load();
            RecipeThing thing;
            return _pack.Things.TryGetValue(id, out thing) ? thing : null;
        }

        internal static List<Recipe> Making(int id)
        {
            Load();
            List<Recipe> list;
            return _pack.ByResult.TryGetValue(id, out list) ? list : null;
        }

        internal static string ThingName(int id)
        {
            var thing = Thing(id);
            return thing != null && !string.IsNullOrEmpty(thing.Name) ? thing.Name : "Предмет " + id;
        }

        internal static string Title(Recipe recipe)
        {
            var thing = Thing(recipe.Result);
            if (thing != null && !string.IsNullOrEmpty(thing.Name)) return thing.Name;
            return string.IsNullOrEmpty(recipe.Name) ? "Рецепт " + recipe.Id : recipe.Name;
        }

        internal static string Profession(int id)
        {
            try
            {
                string name = ResourceStrings.GetString("professions.id" + id + ".name");
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("professions.", StringComparison.Ordinal)) return name;
            }
            catch { }
            return "Профессия " + id;
        }

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
            Pack pack = null;
            if (text != null)
            {
                text = text.Replace("\r\n", "\n");
                try { pack = Read(text); }
                catch (Exception e) { Plugin.Warn("[рецепты] файл с GitHub не разобран: " + e.Message); }
            }
            if (pack == null || pack.All.Count == 0)
            {
                Plugin.Trace("[рецепты] рецепты с GitHub не пришли: " + (string.IsNullOrEmpty(error) ? "код " + code : error));
                yield break;
            }
            Load();
            if (When(pack) < When(_pack))
            {
                Plugin.Trace("[рецепты] на GitHub рецепты старше тех, что уже есть (" + pack.Stamp + " против " + _pack.Stamp + "), оставляю свои");
                yield break;
            }
            if (text == Cached())
            {
                Plugin.Trace("[рецепты] рецепты на GitHub те же, что в кэше");
                yield break;
            }
            Store(text);
            _pack = pack;
            _loaded = true;
            Version++;
            Plugin.Trace("[рецепты] обновлены с GitHub: " + Describe(pack));
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            Pack cache = null;
            Pack built = null;
            string cached = Cached();
            if (cached != null)
            {
                try
                {
                    cache = Read(cached);
                    if (cache.All.Count == 0) cache = null;
                }
                catch (Exception e) { Plugin.Warn("[рецепты] кэш рецептов битый: " + e.Message); }
            }
            try
            {
                var bytes = Grab("recipes.txt");
                if (bytes != null) built = Read(Encoding.UTF8.GetString(bytes));
                if (built != null && built.All.Count == 0) built = null;
            }
            catch (Exception e) { Plugin.Fault("[рецепты] список в сборке не прочитан: " + e); }
            if (cache != null && (built == null || When(cache) >= When(built)))
            {
                _pack = cache;
                Plugin.Trace("[рецепты] из кэша: " + Describe(cache));
            }
            else if (built != null)
            {
                _pack = built;
                Plugin.Trace("[рецепты] из сборки: " + Describe(built));
            }
            else Plugin.Warn("[рецепты] рецептов нет ни в кэше, ни в сборке");
        }

        private static DateTime When(Pack pack)
        {
            DateTime when;
            return DateTime.TryParseExact(pack.Stamp ?? "", "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out when) ? when : DateTime.MinValue;
        }

        private static string Describe(Pack pack) =>
            (pack.Stamp ?? "без даты") + ", рецептов " + pack.All.Count + ", предметов " + pack.Things.Count + ", профессий " + pack.Crafts.Count;

        private static Pack Read(string text)
        {
            var pack = new Pack();
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null) Parse(pack, line);
            }
            foreach (var recipe in pack.All)
            {
                List<Recipe> list;
                if (!pack.ByResult.TryGetValue(recipe.Result, out list)) pack.ByResult[recipe.Result] = list = new List<Recipe>();
                list.Add(recipe);
                if (!pack.Crafts.Contains(recipe.Profession)) pack.Crafts.Add(recipe.Profession);
                var find = new StringBuilder();
                find.Append(NameIn(pack, recipe.Result, recipe.Name)).Append('\n').Append(recipe.Name);
                foreach (var part in recipe.Parts) find.Append('\n').Append(NameIn(pack, part.Id, ""));
                recipe.Find = find.ToString().ToLowerInvariant();
            }
            pack.Crafts.Sort();
            return pack;
        }

        private static string NameIn(Pack pack, int id, string fallback)
        {
            RecipeThing thing;
            return pack.Things.TryGetValue(id, out thing) && !string.IsNullOrEmpty(thing.Name) ? thing.Name : fallback;
        }

        private static void Parse(Pack pack, string line)
        {
            var cell = line.Split('\t');
            switch (cell[0])
            {
                case "V":
                    if (cell.Length > 1) pack.Stamp = cell[1];
                    break;
                case "T":
                    if (cell.Length < 6) return;
                    var thing = new RecipeThing
                    {
                        Id = Int(cell[1]),
                        Name = cell[2],
                        Image = cell[3],
                        Rarity = Int(cell[4]),
                        Level = Int(cell[5]),
                    };
                    pack.Things[thing.Id] = thing;
                    break;
                case "R":
                    if (cell.Length < 16) return;
                    var recipe = new Recipe
                    {
                        Id = Int(cell[1]),
                        Name = cell[2],
                        Image = cell[3],
                        Rarity = Int(cell[4]),
                        Profession = Int(cell[5]),
                        Skill = Int(cell[6]),
                        Chance = Int(cell[7]),
                        Price = cell[8],
                        Result = Int(cell[9]),
                        ResultCount = Int(cell[10]),
                        ResultLevel = Int(cell[11]),
                        ResultRarity = Int(cell[12]),
                        ResultImage = cell[13],
                    };
                    foreach (var one in cell[15].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var pair = one.Split(':');
                        if (pair.Length == 2) recipe.Parts.Add(new RecipePart { Id = Int(pair[0]), Count = Int(pair[1]) });
                    }
                    pack.All.Add(recipe);
                    break;
            }
        }

        private static int Int(string text)
        {
            int value;
            return int.TryParse(text, out value) ? value : 0;
        }

        private static void Store(string text)
        {
            try
            {
                string file = CacheFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + ".new";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
            }
            catch (Exception e) { Plugin.Warn("[рецепты] кэш рецептов не записан: " + e.Message); }
        }

        private static string Cached()
        {
            try { return File.Exists(CacheFile) ? File.ReadAllText(CacheFile, Encoding.UTF8) : null; }
            catch { return null; }
        }

        private static byte[] Grab(string tail)
        {
            var asm = typeof(RecipeData).Assembly;
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
