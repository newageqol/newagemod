using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace NewAgeQoL
{
    internal static class WardrobeUpdate
    {
        internal const string BaseUrl = "https://raw.githubusercontent.com/newageqol/newagemod/main/data/wardrobe.txt";
        private const string Site = "https://nura.biz";
        private static readonly string[] Categories = { "helmets", "breastplates", "weapons", "amulets", "belts", "armsarmor", "legsarmor", "charms" };
        private static readonly int[] TypeIds = { 1, 2, 3, 4, 5, 6, 7, 12 };

        private static readonly Regex HeadRe = new Regex("<h2[^>]*>\\s*(.*?)<br><span[^>]*>\\(([^)]*)\\)</span>", RegexOptions.Singleline);
        private static readonly Regex ImageRe = new Regex("things100x100/([^\".]+)\\.png");
        private static readonly Regex LiRe = new Regex("<li>(.*?):\\s*(.*?)</li>", RegexOptions.Singleline);
        private static readonly Regex TableRe = new Regex("<table class=\"datatable\">(.*?)</table>", RegexOptions.Singleline);
        private static readonly Regex TitleRe = new Regex("<div>(.*?)</div>", RegexOptions.Singleline);
        private static readonly Regex CellRe = new Regex("<td class=\"desc\">(.*?)</td><td class=\"value\">(.*?)</td>", RegexOptions.Singleline);
        private static readonly Regex TagRe = new Regex("<[^>]+>");
        private static readonly Regex NumRe = new Regex("-?\\d+");
        private static readonly Regex DigitsRe = new Regex("\\d+");
        private static readonly Regex SpaceRe = new Regex("\\s+");

        internal static bool Busy;
        internal static string Status = "";
        private static string _cookie = "";

        private sealed class Reply
        {
            internal string Body = "";
            internal string Cookie = "";
            internal string Error = "";
            internal long Code;
        }

        private sealed class Parsed
        {
            internal string Category;
            internal string Name;
            internal int Rarity;
            internal string Image;
            internal int Level;
            internal int ReqLevel;
            internal readonly int[] Req = new int[7];
            internal readonly int[] Bonus = new int[7];
            internal int Energy;
            internal readonly int[] Armor = new int[5];
            internal readonly int[] Magic = new int[3];
            internal int DamageMin;
            internal int DamageMax;
            internal int Range;
        }

        private sealed class Job
        {
            internal volatile bool Done;
            internal string Text;
            internal string Error;
            internal int Base;
            internal int Added;
            internal int Skipped;
        }

        private static bool _autoTried;

        internal static void Auto(bool need)
        {
            if (Busy || _autoTried) return;
            bool stale = true;
            try
            {
                string file = WardrobeData.CacheFile;
                stale = !File.Exists(file) || (DateTime.Now - File.GetLastWriteTime(file)).TotalDays >= 3;
            }
            catch { }
            if (!need && !stale) return;
            _autoTried = true;
            Plugin.Trace("[переодевалка] обновляю базу сама: " + (need ? "в манекене есть вещи не из базы" : "база старше трёх дней"));
            Start();
        }

        internal static void Start()
        {
            if (Busy) return;
            if (Plugin.Instance == null) { Status = "мод ещё не готов"; return; }
            Busy = true;
            Status = "Обновляю вещи…";
            Plugin.Instance.StartCoroutine(Work());
        }

        private static IEnumerator Work()
        {
            Status = "Качаю базу вещей";
            var db = new Reply();
            yield return Get(BaseUrl + "?t=" + DateTime.UtcNow.Ticks, db, false);
            string baseText = db.Code == 200 && db.Body.IndexOf("\nI\t", StringComparison.Ordinal) >= 0 ? db.Body : null;
            bool kept = false;
            if (baseText == null)
            {
                Plugin.Warn("[переодевалка] база вещей не пришла: " + Why(db));
                if (WardrobeData.Current != null)
                {
                    baseText = WardrobeData.Current;
                    kept = true;
                }
            }

            _cookie = "";
            var pages = new List<KeyValuePair<string, string>>();
            string siteError = null;
            var first = new Reply();
            yield return Get(Site + "/veteran/things/helmets", first, true);
            if (first.Body.Length == 0) siteError = "nura.biz не ответил: " + Why(first);
            else
            {
                Take(first);
                for (int c = 0; c < Categories.Length && siteError == null; c++)
                {
                    Status = "Сверяю с nura.biz: раздел " + (c + 1) + " из " + Categories.Length;
                    var form = new StringBuilder();
                    form.Append("typeId=").Append(TypeIds[c]).Append("&minlevel=0&maxlevel=30");
                    foreach (int r in WardrobeData.RarityOrder) form.Append("&selectedItems=").Append(r);
                    form.Append("&_selectedItems=on&_preferableClasses=on");
                    var answer = new Reply();
                    yield return Post(Site + "/ajax/things/filter", form.ToString(), answer);
                    Take(answer);
                    if (answer.Code < 200 || answer.Code >= 300 || answer.Body.Trim().Length > 0)
                    {
                        siteError = "фильтр nura.biz не принял запрос: " + (answer.Body.Trim().Length > 0 ? Short(answer.Body) : Why(answer));
                        break;
                    }
                    var page = new Reply();
                    yield return Get(Site + "/ajax/things/load", page, true);
                    if (page.Body.IndexOf("thingdiv", StringComparison.Ordinal) < 0)
                    {
                        siteError = "nura.biz отдал пустой раздел «" + Categories[c] + "»: " + Why(page);
                        break;
                    }
                    pages.Add(new KeyValuePair<string, string>(Categories[c], page.Body));
                }
            }
            if (siteError != null)
            {
                Plugin.Warn("[переодевалка] " + siteError);
                pages.Clear();
            }
            if (baseText == null && pages.Count == 0)
            {
                Fail("ни база, ни nura.biz не ответили (" + (siteError ?? Why(db)) + ")");
                yield break;
            }

            Status = "Разбираю вещи…";
            var job = new Job();
            string rules = WardrobeData.Rules();
            ThreadPool.QueueUserWorkItem(ignored =>
            {
                try { Build(baseText, rules, pages, job); }
                catch (Exception e) { job.Error = e.Message; }
                finally { job.Done = true; }
            });
            while (!job.Done) yield return null;
            if (job.Error != null) { Fail("вещи не разобрались: " + job.Error); yield break; }

            int was = WardrobeData.Things.Count;
            string when = DateTime.Now.ToString("dd.MM.yyyy");
            if (!WardrobeData.Apply(job.Text, (kept ? "прежняя база" : baseText != null ? "база" : "без базы, только nura.biz") + ", " + when))
            {
                Fail("вещей пришло слишком мало, оставил прежние");
                yield break;
            }
            try
            {
                string file = WardrobeData.CacheFile;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + ".new";
                File.WriteAllText(temp, job.Text, new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
            }
            catch (Exception e) { Plugin.Warn("[переодевалка] кэш вещей не записан: " + e.Message); }

            int now = WardrobeData.Things.Count;
            var status = new StringBuilder("Вещей ").Append(now);
            if (was > 0 && now > was) status.Append(", новых ").Append(now - was);
            else if (was > 0 && now == was) status.Append(", новых нет");
            if (kept) status.Append(". Базу скачать не вышло, оставлена прежняя");
            else if (baseText == null) status.Append(". Базу скачать не вышло, вещи только с сайта игры");
            if (job.Added > 0) status.Append(". С сайта игры добавлено ").Append(job.Added);
            if (job.Skipped > 0) status.Append(", без слота ").Append(job.Skipped);
            if (siteError != null) status.Append(". С сайтом игры сверить не вышло");
            Status = status.ToString();
            Plugin.Trace("[переодевалка] " + Status);
            Busy = false;
            Wardrobe.Reloaded();
        }

        private static void Fail(string text)
        {
            Status = "Не обновилось: " + text;
            Plugin.Warn("[переодевалка] " + Status);
            Busy = false;
            Wardrobe.Reloaded();
        }

        private static string Why(Reply reply) => reply.Error.Length > 0 ? reply.Error : "код " + reply.Code;

        private static string Short(string text)
        {
            text = TagRe.Replace(text, " ").Trim();
            return text.Length > 120 ? text.Substring(0, 120) : text;
        }

        private static void Take(Reply reply)
        {
            int i = reply.Cookie.IndexOf("JSESSIONID=", StringComparison.Ordinal);
            if (i < 0) return;
            int j = reply.Cookie.IndexOf(';', i);
            _cookie = j < 0 ? reply.Cookie.Substring(i) : reply.Cookie.Substring(i, j - i);
        }

        private static IEnumerator Get(string url, Reply reply, bool session)
        {
            var req = UnityWebRequest.Get(url);
            Dress(req, session);
            yield return req.SendWebRequest();
            Grab(req, reply);
            req.Dispose();
        }

        private static IEnumerator Post(string url, string body, Reply reply)
        {
            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            Dress(req, true);
            Head(req, "Content-Type", "application/x-www-form-urlencoded");
            yield return req.SendWebRequest();
            Grab(req, reply);
            req.Dispose();
        }

        private static void Dress(UnityWebRequest req, bool session)
        {
            req.timeout = 90;
            Head(req, "User-Agent", "Mozilla/5.0 NewAgeQoL");
            Head(req, "Accept-Encoding", "gzip");
            if (session && _cookie.Length > 0) Head(req, "Cookie", _cookie);
        }

        private static void Head(UnityWebRequest req, string name, string value)
        {
            try { req.SetRequestHeader(name, value); }
            catch (Exception e) { Plugin.Trace("[переодевалка] заголовок " + name + ": " + e.Message); }
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
                using (var src = new MemoryStream(data))
                using (var gz = new GZipStream(src, CompressionMode.Decompress))
                using (var dst = new MemoryStream())
                {
                    gz.CopyTo(dst);
                    return Encoding.UTF8.GetString(dst.ToArray());
                }
            }
            return Encoding.UTF8.GetString(data);
        }

        private static void Build(string baseText, string rules, List<KeyValuePair<string, string>> pages, Job job)
        {
            var meta = new StringBuilder();
            var lines = new List<string>();
            var known = new HashSet<string>();
            string stamp = DateTime.Now.ToString("yyyy-MM-dd");
            int spare = -1;
            foreach (var line in (baseText ?? rules ?? "").Split('\n'))
            {
                string one = line.TrimEnd('\r');
                if (one.Length < 2) continue;
                if (one.StartsWith("V\t")) { stamp = one.Substring(2); continue; }
                if (!one.StartsWith("I\t")) { meta.Append(one).Append('\n'); continue; }
                if (baseText == null) continue;
                var cell = one.Split('\t');
                if (cell.Length < 16) continue;
                lines.Add(one);
                int id;
                if (int.TryParse(cell[1], out id) && id <= spare) spare = id - 1;
                known.Add(cell[7] + "|" + cell[8] + "|" + cell[4] + "|" + cell[3]);
                known.Add("~" + cell[8].ToLowerInvariant() + "|" + cell[5] + "|" + cell[3]);
            }
            job.Base = lines.Count;
            if (meta.Length == 0) throw new InvalidOperationException("нет правил игры");

            var items = new List<Parsed>();
            foreach (var page in pages) Parse(page.Key, page.Value, items);
            foreach (var item in items)
            {
                if (known.Contains(item.Name + "|" + item.Image + "|" + item.ReqLevel + "|" + item.Rarity)) continue;
                if (known.Contains("~" + item.Image.ToLowerInvariant() + "|" + item.Level + "|" + item.Rarity)) continue;
                int sub = Guess(item);
                if (sub == 0) { job.Skipped++; continue; }
                known.Add(item.Name + "|" + item.Image + "|" + item.ReqLevel + "|" + item.Rarity);
                lines.Add(new StringBuilder("I\t").Append(spare--).Append('\t').Append(sub).Append('\t').Append(item.Rarity).Append('\t')
                    .Append(item.ReqLevel).Append('\t').Append(item.Level).Append("\t0\t")
                    .Append(item.Name.Replace('\t', ' ')).Append('\t').Append(item.Image).Append('\t')
                    .Append(Csv(item.Req)).Append('\t').Append(Csv(item.Bonus)).Append('\t').Append(item.Energy).Append('\t')
                    .Append(Csv(item.Armor)).Append('\t').Append(Csv(item.Magic)).Append('\t')
                    .Append(item.DamageMin).Append('-').Append(item.DamageMax).Append('\t').Append(item.Range).ToString());
                job.Added++;
            }

            var text = new StringBuilder();
            text.Append("V\t").Append(stamp).Append('\n').Append(meta);
            foreach (var line in lines) text.Append(line).Append('\n');
            job.Text = text.ToString();
        }

        private static int Guess(Parsed item)
        {
            switch (item.Category)
            {
                case "helmets": return 1;
                case "breastplates": return 3;
                case "belts": return 6;
                case "charms": return 40;
            }
            string image = item.Image.ToLowerInvariant();
            if (image.StartsWith("image_")) image = image.Substring(6);
            if (image.StartsWith("hallow")) image = image.Substring(6);
            if (item.Category == "amulets")
            {
                if (image.StartsWith("kolie") || image.StartsWith("ncl") || image.StartsWith("chain")) return 2;
                if (image.StartsWith("ring") || image.StartsWith("rng")) return 7;
                if (image.StartsWith("ear")) return 55;
            }
            if (item.Category == "armsarmor")
            {
                if (image.StartsWith("glovse") || image.StartsWith("glv")) return 4;
                if (image.StartsWith("naruchi") || image.StartsWith("nrc")) return 5;
            }
            if (item.Category == "legsarmor")
            {
                if (image.StartsWith("bots") || image.StartsWith("bts")) return 11;
                if (image.StartsWith("nakoleniki") || image.StartsWith("nkn")) return 10;
            }
            if (item.Category == "weapons")
            {
                if (image.StartsWith("spear")) return 8;
                if (image.StartsWith("bow")) return 14;
                if (image.StartsWith("staff")) return 30;
                if (image.StartsWith("shield")) return 15;
                if (image.StartsWith("wand")) return 37;
                if (image.StartsWith("dagger") || image.StartsWith("suriken")) return 39;
                if (image.StartsWith("castet")) return 38;
            }
            return 0;
        }

        private static void Parse(string category, string page, List<Parsed> items)
        {
            const string Marker = "<div class=\"thin_border thingdiv";
            int at = page.IndexOf(Marker, StringComparison.Ordinal);
            while (at >= 0)
            {
                int next = page.IndexOf(Marker, at + Marker.Length, StringComparison.Ordinal);
                string block = next < 0 ? page.Substring(at) : page.Substring(at, next - at);
                at = next;
                var head = HeadRe.Match(block);
                if (!head.Success) continue;
                int rarity = Rarity(head.Groups[2].Value.Trim());
                if (rarity == 0) continue;
                var item = new Parsed
                {
                    Category = category,
                    Name = WebUtility.HtmlDecode(SpaceRe.Replace(head.Groups[1].Value, " ")).Trim(),
                    Rarity = rarity
                };
                var image = ImageRe.Match(block);
                item.Image = image.Success ? image.Groups[1].Value : "";
                foreach (Match li in LiRe.Matches(block))
                {
                    string key = WebUtility.HtmlDecode(TagRe.Replace(li.Groups[1].Value, "")).Trim();
                    string value = TagRe.Replace(li.Groups[2].Value, "");
                    if (key == "Уровень вещи") item.Level = Number(value);
                    else if (key == "Дальность") item.Range = Number(value);
                    else if (key == "Урон")
                    {
                        var nums = DigitsRe.Matches(value);
                        if (nums.Count == 2)
                        {
                            item.DamageMin = int.Parse(nums[0].Value);
                            item.DamageMax = int.Parse(nums[1].Value);
                        }
                    }
                }
                foreach (Match table in TableRe.Matches(block))
                {
                    var title = TitleRe.Match(table.Groups[1].Value);
                    if (!title.Success) continue;
                    string what = title.Groups[1].Value;
                    foreach (Match cell in CellRe.Matches(table.Groups[1].Value))
                    {
                        string key = WebUtility.HtmlDecode(cell.Groups[1].Value).Replace(' ', ' ').TrimEnd(':').Trim();
                        int value = Number(cell.Groups[2].Value);
                        int index;
                        if (what == "Требования")
                        {
                            if (key == "Уровень") item.ReqLevel = value;
                            else if ((index = Stat(key)) >= 0) item.Req[index] = value;
                        }
                        else if (what == "Характеристики")
                        {
                            if (key == "Энергия") item.Energy = value;
                            else if ((index = Stat(key)) >= 0) item.Bonus[index] = value;
                        }
                        else if (what == "Броня")
                        {
                            if ((index = Array.IndexOf(WardrobeData.ArmorNames, key)) >= 0) item.Armor[index] = value;
                        }
                        else if (what == "Защита от магии")
                        {
                            if ((index = Array.IndexOf(WardrobeData.MagicNames, key)) >= 0) item.Magic[index] = value;
                        }
                    }
                }
                if (item.ReqLevel == 0) item.ReqLevel = item.Level;
                items.Add(item);
            }
        }

        private static int Stat(string key)
        {
            if (key == "Cила") return 0;
            return Array.IndexOf(WardrobeData.StatNames, key);
        }

        private static int Rarity(string label)
        {
            switch (label)
            {
                case "обычная вещь": return 1;
                case "крафтовая вещь": return 2;
                case "раритетная вещь": return 3;
                case "награда": return 5;
                case "эпическая вещь": return 6;
                case "вещь ратника": return 7;
                case "мифическая вещь": return 8;
                default: return 0;
            }
        }

        private static int Number(string text)
        {
            var m = NumRe.Match(text.Replace("&nbsp;", " "));
            int value;
            return m.Success && int.TryParse(m.Value, out value) ? value : 0;
        }

        private static string Csv(int[] values)
        {
            var text = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(values[i]);
            }
            return text.ToString();
        }
    }
}
