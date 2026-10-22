using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using Transport.Messages.Responses.Things.Thingtabs;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Lang
    {
        internal const string Asset = "Lang/uk.gz";
        private const string Letters = "ЁёІіЇїЄєҐґ";
        private const string SpareSource = "arial";
        private const float FontEvery = 5f;
        private const float TellEvery = 60f;
        private static readonly Regex Number = new Regex(@"\d+(?:[.,]\d+)?");
        private static readonly Type[] ItemTypes = { typeof(GeneralThingInfoMessage), typeof(GeneralPrescriptionInfoMessage), typeof(ThingItemMessage) };

        internal static ConfigEntry<bool> Cfg;
        internal static ConfigEntry<bool> Asked;
        internal static bool Synced;

        internal static bool Wants => Cfg != null && Cfg.Value && !Synced;

        internal static bool Present => File.Exists(Local);

        private static string Local => Path.Combine(Path.Combine(AssetSync.Root, "Lang"), "uk.gz");

        private static volatile bool _on;
        private static volatile Dictionary<string, string> _texts;
        private static volatile Dictionary<string, string> _mod;
        private static volatile Dictionary<int, Item> _items;
        private static Dictionary<string, string> _keys;
        private static FieldInfo _table;
        private static Assembly _wire;
        private static TMP_FontAsset _spare;
        private static float _fontAt = -100f;
        private static float _toldAt;
        private static int _swapped;
        private static int _kept;
        private static int _told;
        private static string _broken;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> Russian = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, Shape> Shapes = new Dictionary<Type, Shape>();
        private static readonly HashSet<int> Seen = new HashSet<int>();

        private sealed class Shape
        {
            internal PropertyInfo Id;
            internal PropertyInfo Name;
            internal PropertyInfo Description;
            internal PropertyInfo[] Kids;
        }

        private sealed class Item
        {
            internal string RuName;
            internal string UkName;
            internal string RuDescription;
            internal string UkDescription;
        }

        internal static void Bind(ConfigFile home)
        {
            Cfg = home.Bind("Interface", "Ukrainian", false,
                "Украинский язык игры: интерфейс, названия и описания вещей, окна мода и лог боя в чате. Файл перевода мод скачивает сам из отдельного релиза и только при включённом украинском. Часть надписей меняется сразу, полностью — после перезапуска игры. Задания и чат, которые сервер присылает готовым текстом, остаются как есть.");
            Asked = home.Bind("Interface", "LanguageAsked", false,
                "Язык уже выбран в окне при запуске игры. Выключить — окно выбора языка покажется при следующем запуске.");
            _on = Cfg.Value;
            Cfg.SettingChanged += (s, e) =>
            {
                _on = Cfg.Value;
                Apply();
            };
        }

        internal static bool On => _on;

        internal static bool Decided => Asked == null || Asked.Value || LangGate.Broken;

        internal static void Choose(string code)
        {
            bool ukrainian = code == "uk";
            Cfg.Value = false;
            GameSettings.ClientLanguage = ukrainian ? "ru" : code;
            ResourceStrings.InitializeInlineStringResources();
            Cfg.Value = ukrainian;
            Asked.Value = true;
            Plugin.Log?.LogInfo("[lang] language chosen at start: " + code);
        }

        internal static string T(string text)
        {
            if (!_on || text == null) return text;
            var mod = _mod;
            string to;
            return mod != null && mod.TryGetValue(text, out to) ? to : text;
        }

        internal static string F(string format, params object[] args)
        {
            string pattern = T(format);
            try { return string.Format(pattern, args); }
            catch (FormatException) { return string.Format(format, args); }
        }

        internal static string Game(string text)
        {
            if (!_on || string.IsNullOrEmpty(text)) return text;
            var texts = _texts;
            if (texts == null) return text;
            return Translate(text, texts) ?? text;
        }

        internal static string Original(string key)
        {
            string was;
            if (Russian.TryGetValue(key, out was) && was != null) return was;
            return ResourceStrings.GetString(key);
        }

        internal static void Apply()
        {
            try
            {
                var table = Table();
                if (table == null) return;
                if (_on)
                {
                    if (!Load()) return;
                    foreach (var pair in _keys)
                    {
                        string was;
                        bool had = table.TryGetValue(pair.Key, out was);
                        if (had && was == pair.Value) continue;
                        if (!Russian.ContainsKey(pair.Key)) Russian[pair.Key] = had ? was : null;
                        table[pair.Key] = pair.Value;
                    }
                    return;
                }
                foreach (var pair in Russian)
                {
                    if (pair.Value == null) table.Remove(pair.Key);
                    else table[pair.Key] = pair.Value;
                }
                Russian.Clear();
            }
            catch (Exception e) { Plugin.Warn("[lang] apply: " + e.Message); }
        }

        internal static void Swap(object message)
        {
            if (!_on || message == null) return;
            var items = _items;
            if (items == null) return;
            try { Walk(message, items, 0); }
            catch (Exception e) { _broken = message.GetType().Name + ": " + e.Message; }
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (_broken != null)
            {
                Plugin.Warn("[lang] incoming message: " + _broken);
                _broken = null;
            }
            if (_swapped != _told && now - _toldAt > TellEvery)
            {
                _toldAt = now;
                _told = _swapped;
                Plugin.Trace("[lang] item texts swapped to Ukrainian: " + _told + ", left as sent (text differs from the catalog): " + _kept);
            }
            if (now - _fontAt < FontEvery) return;
            _fontAt = now;
            Fonts();
        }

        private static Dictionary<string, string> Table()
        {
            if (_table == null) _table = AccessTools.Field(typeof(ResourceStrings), "STRINGS");
            return _table?.GetValue(null) as Dictionary<string, string>;
        }

        internal static void Reload()
        {
            lock (Gate)
            {
                _keys = null;
                _texts = null;
                _mod = null;
                _items = null;
            }
            Apply();
        }

        private static bool Load()
        {
            if (_keys != null) return true;
            lock (Gate)
            {
                if (_keys != null) return true;
                var keys = new Dictionary<string, string>(StringComparer.Ordinal);
                var texts = new Dictionary<string, string>(StringComparer.Ordinal);
                var mod = new Dictionary<string, string>(StringComparer.Ordinal);
                var items = new Dictionary<int, Item>();
                var phrases = new List<KeyValuePair<string, string>>();
                var names = new Dictionary<string, string>(StringComparer.Ordinal);
                var races = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    if (!File.Exists(Local))
                    {
                        Plugin.Trace("[lang] " + Asset + " is not downloaded yet");
                        return false;
                    }
                    using (var stream = File.OpenRead(Local))
                    using (var unzip = new GZipStream(stream, CompressionMode.Decompress))
                    using (var reader = new StreamReader(unzip, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var parts = line.Split('\t');
                            if (parts.Length < 3 || parts[0].Length != 1) continue;
                            char kind = parts[0][0];
                            if (kind == 'I')
                            {
                                int id;
                                if (parts.Length < 6 || !int.TryParse(parts[1], out id)) continue;
                                items[id] = new Item
                                {
                                    RuName = Unescape(parts[2]),
                                    UkName = Some(Unescape(parts[3])),
                                    RuDescription = Unescape(parts[4]),
                                    UkDescription = Some(Unescape(parts[5]))
                                };
                                continue;
                            }
                            string from = Unescape(parts[1]);
                            string to = Unescape(parts[2]);
                            if (kind == 'K') keys[from] = to;
                            else if (kind == 'T') texts[from] = to;
                            else if (kind == 'M') mod[from] = to;
                            else if (kind == 'P') phrases.Add(new KeyValuePair<string, string>(from, to));
                            else if (kind == 'B') names[from] = to;
                            else if (kind == 'R') races[from] = to;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Warn("[lang] read " + Asset + ": " + e.Message);
                    return false;
                }
                _texts = texts;
                _mod = mod;
                _items = items;
                try { FightLang.Take(phrases, names, races, keys.Values.Concat(texts.Values).Concat(mod.Values)); }
                catch (Exception e) { Plugin.Warn("[lang] combat log data: " + e.Message); }
                _keys = keys;
                Plugin.Log?.LogInfo("[lang] Ukrainian loaded: " + keys.Count + " strings, " + texts.Count + " game texts, " + items.Count + " items, " + mod.Count + " mod texts, combat log " + phrases.Count + " phrases, " + names.Count + " names, " + races.Count + " creatures");
                return true;
            }
        }

        private static string Some(string s) => string.IsNullOrEmpty(s) ? null : s;

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else if (n == 't') sb.Append('\t');
                else sb.Append(n);
            }
            return sb.ToString();
        }

        private static void Walk(object node, Dictionary<int, Item> items, int depth)
        {
            if (node == null || depth > 8) return;
            var list = node as IList;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) Walk(list[i], items, depth + 1);
                return;
            }
            var shape = ShapeOf(node.GetType());
            if (shape == null) return;
            Item item;
            if (shape.Id != null && items.TryGetValue((int)shape.Id.GetValue(node, null), out item))
            {
                Put(node, shape.Name, item.RuName, item.UkName);
                Put(node, shape.Description, item.RuDescription, item.UkDescription);
            }
            foreach (var p in shape.Kids) Walk(p.GetValue(node, null), items, depth + 1);
        }

        private static void Put(object node, PropertyInfo field, string ru, string uk)
        {
            if (field == null || uk == null) return;
            var now = field.GetValue(node, null) as string;
            if (string.IsNullOrEmpty(now)) return;
            string bare = now.Trim();
            if (bare == uk) return;
            string to = bare == ru ? uk : Renumber(bare, ru, uk);
            if (to == null)
            {
                _kept++;
                return;
            }
            int head = now.IndexOf(bare, StringComparison.Ordinal);
            field.SetValue(node, now.Substring(0, head) + to + now.Substring(head + bare.Length), null);
            _swapped++;
        }

        private static string Renumber(string now, string ru, string uk)
        {
            var have = Number.Matches(now);
            var was = Number.Matches(ru);
            if (have.Count == 0 || have.Count != was.Count) return null;
            if (Number.Replace(now, "#") != Number.Replace(ru, "#")) return null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < was.Count; i++)
            {
                string from = was[i].Value, to = have[i].Value, old;
                if (map.TryGetValue(from, out old)) { if (old != to) return null; }
                else map[from] = to;
            }
            return Number.Replace(uk, m =>
            {
                string to;
                return map.TryGetValue(m.Value, out to) ? to : m.Value;
            });
        }

        private static string Translate(string value, Dictionary<string, string> texts)
        {
            string to;
            if (texts.TryGetValue(value, out to)) return to;
            string bare = value.Trim();
            if (bare.Length == 0 || bare.Length == value.Length || !texts.TryGetValue(bare, out to)) return null;
            int head = value.IndexOf(bare, StringComparison.Ordinal);
            return value.Substring(0, head) + to + value.Substring(head + bare.Length);
        }

        private static Shape ShapeOf(Type type)
        {
            lock (Shapes) return Build(type);
        }

        private static Shape Build(Type type)
        {
            Shape shape;
            if (Shapes.TryGetValue(type, out shape)) return shape;
            Shapes[type] = null;
            if (!Ours(type)) return null;
            var made = new Shape();
            if (Array.IndexOf(ItemTypes, type) >= 0)
            {
                made.Id = Text(type, "ThingId", typeof(int));
                made.Name = Text(type, "Name", typeof(string));
                made.Description = Text(type, "Description", typeof(string));
            }
            var kids = new List<PropertyInfo>();
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                var inner = Inner(p.PropertyType);
                if (inner != null && inner != type && Build(inner) != null) kids.Add(p);
            }
            made.Kids = kids.ToArray();
            if (made.Id == null && made.Kids.Length == 0) return null;
            Shapes[type] = made;
            return made;
        }

        private static PropertyInfo Text(Type type, string name, Type kind)
        {
            var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p != null && p.PropertyType == kind && p.CanRead && (kind != typeof(string) || p.CanWrite) ? p : null;
        }

        private static Type Inner(Type t)
        {
            if (t.IsPrimitive || t.IsEnum || t == typeof(string)) return null;
            if (t.IsArray) t = t.GetElementType();
            else if (t.IsGenericType && typeof(IList).IsAssignableFrom(t)) t = t.GetGenericArguments()[0];
            return Ours(t) ? t : null;
        }

        private static bool Ours(Type t) => t != null && t.IsClass && t.Assembly == Wire();

        private static Assembly Wire()
        {
            if (_wire == null) _wire = typeof(GeneralThingInfoMessage).Assembly;
            return _wire;
        }

        private static void Fonts()
        {
            Font source = null;
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name == SpareSource) { source = f; break; }
            if (source == null) return;
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (asset == null || asset == _spare) continue;
                int id = asset.GetInstanceID();
                if (Seen.Contains(id)) continue;
                if (asset.atlasPopulationMode == AtlasPopulationMode.Dynamic || Full(asset))
                {
                    Seen.Add(id);
                    continue;
                }
                if (_spare == null)
                {
                    _spare = TMP_FontAsset.CreateFontAsset(source);
                    if (_spare == null) return;
                    _spare.name = SpareSource + " SDF uk";
                    _spare.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                if (asset.fallbackFontAssetTable == null) asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
                if (!asset.fallbackFontAssetTable.Contains(_spare)) asset.fallbackFontAssetTable.Add(_spare);
                Seen.Add(id);
                Plugin.Trace("[lang] font '" + asset.name + "' takes missing letters Ё and Ukrainian from " + SpareSource);
            }
        }

        private static bool Full(TMP_FontAsset asset)
        {
            foreach (char c in Letters)
                if (!asset.HasCharacter(c, true)) return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "InitializeDialog")]
    public static class LangChoicePatch
    {
        internal const string Title = "Українська";

        private static void Postfix(SetupDialog __instance)
        {
            try
            {
                var box = AccessTools.Field(typeof(SetupDialog), "LanguageDropdown")?.GetValue(__instance) as UnityEngine.UI.Dropdown;
                if (box == null) return;
                int at = SystemConsts.SUPPORTED_LOCALES.Length;
                if (box.options.Count == at) box.options.Add(new UnityEngine.UI.Dropdown.OptionData(Title));
                if (Lang.Cfg != null && Lang.Cfg.Value && GameSettings.ClientLanguage == "ru") box.SetValueWithoutNotify(at);
                box.RefreshShownValue();
            }
            catch (Exception e) { Plugin.Warn("[lang] language list: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(SetupDialog), "OnApplicationRestart")]
    public static class LangRestartPatch
    {
        private static bool Prefix(EMessageBoxResult result, int lang)
        {
            if (result != EMessageBoxResult.MB_OK || Lang.Cfg == null) return true;
            bool ukrainian = lang >= SystemConsts.SUPPORTED_LOCALES.Length;
            Lang.Cfg.Value = ukrainian;
            if (!ukrainian) return true;
            GameSettings.ClientLanguage = "ru";
            Plugin.Log?.LogInfo("[lang] Ukrainian chosen in the game settings, closing the client");
            Application.Quit();
            return false;
        }
    }

    [HarmonyPatch(typeof(ResourceStrings), "InitializeInlineStringResources")]
    public static class LangInlinePatch
    {
        private static void Prefix()
        {
            if (Lang.Cfg == null || !Lang.Cfg.Value || GameSettings.ClientLanguage == "ru") return;
            Plugin.Log?.LogInfo("[lang] Ukrainian is on, game language " + GameSettings.ClientLanguage + " switched to ru");
            GameSettings.ClientLanguage = "ru";
        }

        private static void Postfix() => Lang.Apply();
    }

    [HarmonyPatch(typeof(ResourceStrings), "InitializeExternalStringResources")]
    public static class LangExternalPatch
    {
        private static bool Prefix(ref Action onFinish)
        {
            if (!Lang.Decided && LangGate.Hold(onFinish)) return false;
            if (Lang.Wants && LangGate.Refresh(onFinish)) return false;
            var next = onFinish;
            onFinish = () =>
            {
                Lang.Apply();
                next?.Invoke();
            };
            return true;
        }
    }

    [HarmonyPatch(typeof(NetworkConnection), "AsyncProcessMessagesHandle")]
    public static class LangWirePatch
    {
        private static void Prefix(object message) => Lang.Swap(message);
    }
}
