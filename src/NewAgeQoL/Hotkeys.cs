using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Hotkeys
    {
        internal sealed class Act
        {
            internal string Key;
            internal string Group;
            internal string Title;
            internal string Fallback;
            internal Action Do;
        }

        private const char PairSplit = '|';
        private const char NameSplit = '~';

        private static readonly Dictionary<string, string> Bound = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Learned = new Dictionary<string, string>();
        private static bool _read;
        private static bool _knownRead;

        private static void Read()
        {
            if (_read) return;
            _read = true;
            Bound.Clear();
            string mine = Plugin.CfgHotkeys != null ? Plugin.CfgHotkeys.Value : "";
            string all = Plugin.CfgHotkeysAll != null ? Plugin.CfgHotkeysAll.Value : "";
            bool moved = false;
            var done = Plugin.CfgHotkeysDone;
            if (done != null && !done.Value)
            {
                done.Value = true;
                string seeded = Plugin.CfgHotkeySeededOwn != null ? Plugin.CfgHotkeySeededOwn.Value : "";
                if (!string.IsNullOrEmpty(seeded) && Plugin.CfgHotkeySeeded != null && string.IsNullOrEmpty(Plugin.CfgHotkeySeeded.Value))
                {
                    Plugin.CfgHotkeySeeded.Value = seeded;
                    Plugin.Trace("[клавиши] отметки клавиш по умолчанию перенесены в общий файл");
                }
                if (Plugin.CfgHotkeysAll != null && string.IsNullOrEmpty(all) && !string.IsNullOrEmpty(mine))
                {
                    all = OnlyCommon(mine);
                    if (!string.IsNullOrEmpty(all))
                    {
                        Plugin.CfgHotkeysAll.Value = all;
                        moved = true;
                        Plugin.Trace("[клавиши] общие клавиши аккаунта взяты у этого персонажа");
                    }
                }
            }
            all = Unstash(all);
            string common = OnlyCommon(all);
            if (common != all)
            {
                Stash(all);
                if (Plugin.CfgHotkeysAll != null) Plugin.CfgHotkeysAll.Value = common;
                all = common;
                moved = true;
            }
            Fill(all, false);
            Fill(mine, true);
            bool tidy = moved;
            if (Adopt()) tidy = true;
            foreach (string key in Gone) if (Bound.Remove(key)) tidy = true;
            bool fresh = Seed(!string.IsNullOrEmpty(mine) || !string.IsNullOrEmpty(all));
            if (fresh || tidy) Save();
            Untangle();
        }

        private static string Unstash(string all)
        {
            var old = Plugin.CfgHotkeyOld;
            if (old == null || string.IsNullOrEmpty(old.Value)) return all;
            var keep = new StringBuilder();
            var back = new StringBuilder(all ?? "");
            int moved = 0;
            foreach (string part in old.Value.Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at);
                if (Skilly(key))
                {
                    if (keep.Length > 0) keep.Append(PairSplit);
                    keep.Append(part);
                    continue;
                }
                moved++;
                if (Has(back.ToString(), key)) continue;
                if (back.Length > 0) back.Append(PairSplit);
                back.Append(part);
            }
            if (moved == 0) return all;
            old.Value = keep.ToString();
            if (Plugin.CfgHotkeysAll != null) Plugin.CfgHotkeysAll.Value = back.ToString();
            Plugin.Trace("[клавиши] клавиш приёмов вернулось в общий список аккаунта: " + moved);
            return back.ToString();
        }

        private static bool Has(string saved, string key)
        {
            if (string.IsNullOrEmpty(saved)) return false;
            foreach (string part in saved.Split(PairSplit))
            {
                int at = part.IndexOf('=');
                if (at > 0 && part.Substring(0, at) == key) return true;
            }
            return false;
        }

        private static void Stash(string all)
        {
            var old = Plugin.CfgHotkeyOld;
            if (old == null) return;
            var order = new List<string>();
            var map = new Dictionary<string, string>();
            foreach (string part in (old.Value ?? "").Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at);
                if (map.ContainsKey(key)) continue;
                order.Add(key);
                map[key] = part.Substring(at + 1);
            }
            int added = 0;
            foreach (string part in (all ?? "").Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at);
                if (!Skilly(key) || map.ContainsKey(key)) continue;
                order.Add(key);
                map[key] = part.Substring(at + 1);
                added++;
            }
            if (added == 0) return;
            var text = new StringBuilder();
            foreach (string key in order)
            {
                if (text.Length > 0) text.Append(PairSplit);
                text.Append(key).Append('=').Append(map[key]);
            }
            old.Value = text.ToString();
            Plugin.Trace("[клавиши] клавиш заклинаний вынуто из общего списка аккаунта: " + added);
        }

        private static bool Adopt()
        {
            var done = Plugin.CfgHotkeyOldTaken;
            var old = Plugin.CfgHotkeyOld;
            if (done == null || done.Value || old == null || string.IsNullOrEmpty(old.Value)) return false;
            Known();
            if (Learned.Count == 0) return false;
            int took = 0;
            foreach (string part in old.Value.Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at);
                if (!Learned.ContainsKey(key) || Bound.ContainsKey(key)) continue;
                Bound[key] = part.Substring(at + 1);
                took++;
            }
            done.Value = true;
            Plugin.Trace("[клавиши] персонажу достались его старые клавиши заклинаний: " + took);
            return took > 0;
        }

        private static void Untangle()
        {
            var clash = new List<string>();
            foreach (var pair in new List<KeyValuePair<string, string>>(Bound))
            {
                if (!Skilly(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
                foreach (var other in Bound)
                {
                    if (Skilly(other.Key) || other.Value != pair.Value) continue;
                    if (Shared(pair.Key, other.Key)) continue;
                    clash.Add(pair.Key);
                    break;
                }
            }
            if (clash.Count == 0) return;
            foreach (string key in clash)
            {
                Plugin.Trace("[клавиши] у умения " + key + " клавиша " + Bound[key] + " занята общим действием, снял");
                Bound.Remove(key);
            }
            Save();
            try { Notice.Show("Снято клавиш у умений и заклинаний: " + clash.Count + ". Эти клавиши заняты общими для аккаунта, назначь им другие", 7f); }
            catch (Exception e) { Plugin.Trace("[клавиши] сообщение о занятых клавишах: " + e.Message); }
        }

        private static void Fill(string saved, bool own)
        {
            if (string.IsNullOrEmpty(saved)) return;
            foreach (string part in saved.Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0) continue;
                string key = part.Substring(0, at);
                if (own ? !Owned(key) : Skilly(key)) continue;
                Bound[key] = part.Substring(at + 1);
            }
        }

        private static readonly string[] Gone =
        {
            "fight:hexup", "fight:hexdown", "fight:hexleft", "fight:hexright", "fight:hexgo", "win:tapes",
        };

        private static readonly string[] Older = { "win:inventory", "win:online", "fight:phase", "fight:strike" };

        private static bool Seed(bool lived)
        {
            var seen = new List<string>();
            string was = Plugin.CfgHotkeySeeded != null ? Plugin.CfgHotkeySeeded.Value : "";
            foreach (string part in was.Split(PairSplit))
                if (!string.IsNullOrEmpty(part)) seen.Add(part);
            if (seen.Count == 0 && lived)
                foreach (string key in Older) seen.Add(key);

            bool fresh = false;
            bool grown = false;
            foreach (var act in Fixed())
            {
                if (string.IsNullOrEmpty(act.Fallback) || seen.Contains(act.Key)) continue;
                seen.Add(act.Key);
                grown = true;
                if (Bound.ContainsKey(act.Key)) continue;
                if (Taken(act.Fallback, act.Key, true) != null) continue;
                Bound[act.Key] = act.Fallback;
                fresh = true;
            }
            if (grown && Plugin.CfgHotkeySeeded != null)
                Plugin.CfgHotkeySeeded.Value = string.Join(PairSplit.ToString(), seen.ToArray());
            return fresh;
        }

        private static void Save()
        {
            var mine = new StringBuilder();
            var all = new StringBuilder();
            foreach (var pair in Bound)
            {
                if (string.IsNullOrEmpty(pair.Value)) continue;
                var text = Skilly(pair.Key) ? mine : all;
                if (text.Length > 0) text.Append(PairSplit);
                text.Append(pair.Key).Append('=').Append(pair.Value);
            }
            if (Plugin.CfgHotkeys != null) Plugin.CfgHotkeys.Value = mine.ToString();
            if (Plugin.CfgHotkeysAll != null) Plugin.CfgHotkeysAll.Value = all.ToString();
            _parsed = false;
        }

        internal static bool Skilly(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return key.StartsWith("skill:", StringComparison.Ordinal)
                || key.StartsWith("spell:", StringComparison.Ordinal);
        }

        private static bool Owned(string key)
        {
            return Skilly(key) || (!string.IsNullOrEmpty(key) && key.StartsWith("trick:", StringComparison.Ordinal));
        }

        internal static string OnlyCommon(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return saved;
            var text = new StringBuilder();
            foreach (string part in saved.Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf('=');
                if (at <= 0 || Skilly(part.Substring(0, at))) continue;
                if (text.Length > 0) text.Append(PairSplit);
                text.Append(part);
            }
            return text.ToString();
        }

        internal static void Forget()
        {
            Stop();
            Bound.Clear();
            Learned.Clear();
            Live.Clear();
            _read = false;
            _knownRead = false;
            _parsed = false;
            _listAt = 0f;
        }

        internal static string Of(string key)
        {
            Read();
            return Bound.TryGetValue(key, out var value) ? value : "";
        }

        internal static void Set(string key, string binding)
        {
            Read();
            if (string.IsNullOrEmpty(binding)) Bound.Remove(key);
            else Bound[key] = binding;
            Save();
        }

        internal static string Taken(string binding, string except)
        {
            return Taken(binding, except, false);
        }

        private static bool Flask(string key)
        {
            return !string.IsNullOrEmpty(key) && key.StartsWith("flask:", StringComparison.Ordinal);
        }

        private static bool Shared(string one, string two)
        {
            return (Fighty(one) && Flask(two)) || (Flask(one) && Fighty(two));
        }

        internal static bool Fighty(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return key.StartsWith("fight:", StringComparison.Ordinal)
                || key.StartsWith("trick:", StringComparison.Ordinal)
                || key.StartsWith("spell:", StringComparison.Ordinal)
                || key.StartsWith("skill:", StringComparison.Ordinal);
        }

        private static string Taken(string binding, string except, bool sameSide)
        {
            if (string.IsNullOrEmpty(binding)) return null;
            Read();
            foreach (var pair in Bound)
            {
                if (pair.Key == except || pair.Value != binding) continue;
                if (sameSide && Shared(except, pair.Key)) continue;
                foreach (var act in All())
                    if (act.Key == pair.Key) return act.Title;
                return pair.Key;
            }
            return null;
        }

        internal static string Tail(string key)
        {
            string binding = Of(key);
            if (string.IsNullOrEmpty(binding)) return "";
            return " (" + Text(binding) + ")";
        }

        internal static string Text(string binding)
        {
            var key = KeyOf(binding);
            return key == KeyCode.None ? "—" : Name(key);
        }

        private static string Name(KeyCode key)
        {
            string text = key.ToString();
            if (text.StartsWith("Alpha", StringComparison.Ordinal)) return text.Substring(5);
            if (text.StartsWith("Keypad", StringComparison.Ordinal)) return "Num " + text.Substring(6);
            switch (key)
            {
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "Ctrl (правый)";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "Shift (правый)";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.RightAlt: return "Alt (правый)";
                case KeyCode.Space: return "Пробел";
                case KeyCode.Return: return "Enter";
                default: return text;
            }
        }

        private static KeyCode KeyOf(string binding)
        {
            if (string.IsNullOrEmpty(binding)) return KeyCode.None;
            KeyCode key;
            return Enum.TryParse(binding, true, out key) ? key : KeyCode.None;
        }

        private static readonly List<KeyValuePair<KeyCode, Act>> Live = new List<KeyValuePair<KeyCode, Act>>();
        private static bool _parsed;
        private static float _listAt;

        private static void Ready()
        {
            if (_parsed && Time.unscaledTime - _listAt < 2f) return;
            _listAt = Time.unscaledTime;
            _parsed = true;
            Live.Clear();
            foreach (var act in All())
            {
                var key = KeyOf(Of(act.Key));
                if (key != KeyCode.None) Live.Add(new KeyValuePair<KeyCode, Act>(key, act));
            }
        }

        internal static void Tick()
        {
            try
            {
                Mute();
                Learn();
                AskMasteries();
                CaptureTick();
                if (Capturing || Time.unscaledTime < _hushUntil) return;
                if (!SideButtons.InWorld()) return;
                if (!Input.anyKeyDown) return;
                bool typing = Typing();
                Ready();
                if (Live.Count == 0) return;

                foreach (KeyCode key in Keys())
                {
                    if (!Input.GetKeyDown(key)) continue;
                    if (Fire(key, typing)) return;
                }
            }
            catch (Exception e) { Plugin.Trace("[клавиши] " + e.Message); }
        }

        private static bool Fire(KeyCode key, bool typing)
        {
            bool fight = SideButtons.InCombat();
            bool safe = key == KeyCode.Tab || (key >= KeyCode.F1 && key <= KeyCode.F15);
            Act pick = null;
            foreach (var pair in Live)
            {
                if (pair.Key != key) continue;
                if (typing && !safe) continue;
                if (Fighty(pair.Value.Key) == fight) { pick = pair.Value; break; }
                if (pick == null) pick = pair.Value;
            }
            if (pick == null) return false;
            Run(pick);
            return true;
        }

        private static void Run(Act act)
        {
            if (act == null || act.Do == null) return;
            Plugin.Trace("[клавиши] " + act.Title + " (" + act.Key + ")");
            try { act.Do(); }
            catch (Exception e) { Plugin.Warn("[клавиши] " + act.Title + ": " + e.Message); }
        }

        private static KeyCode[] _keys;

        private static KeyCode[] Keys()
        {
            if (_keys != null) return _keys;
            var list = new List<KeyCode>();
            foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
            {
                if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) continue;
                if (key == KeyCode.None) continue;
                list.Add(key);
            }
            _keys = list.ToArray();
            return _keys;
        }

        private static bool Typing()
        {
            try
            {
                if (ChatDock.Typing) return true;
                var system = EventSystem.current;
                var picked = system != null ? system.currentSelectedGameObject : null;
                return picked != null && (picked.GetComponent<InputField>() != null || picked.GetComponent<TMPro.TMP_InputField>() != null);
            }
            catch { return false; }
        }

        private static string _capKey;
        private static Text _capText;
        private static Color _capHome;
        private static float _capMsgUntil;
        private static float _hushUntil;

        internal static bool Capturing => _capKey != null;

        internal static void Begin(string key, Text label)
        {
            if (Capturing) Stop();
            _capKey = key;
            _capText = label;
            _capMsgUntil = 0f;
            if (_capText != null)
            {
                _capHome = _capText.color;
                _capText.text = "жми клавишу…";
            }
        }

        internal static void Stop()
        {
            if (_capText != null)
            {
                _capText.color = _capHome;
                if (_capKey != null) _capText.text = Text(Of(_capKey));
            }
            _capKey = null;
            _capText = null;
            _capMsgUntil = 0f;
        }

        private static void Keep(string binding)
        {
            string busy = Taken(binding, _capKey, true);
            if (busy != null)
            {
                if (_capText != null)
                {
                    _capText.color = new Color32(255, 120, 100, 255);
                    _capText.text = "занято: " + busy;
                }
                _capMsgUntil = Time.unscaledTime + 2f;
                return;
            }
            Set(_capKey, binding);
            _hushUntil = Time.unscaledTime + 0.4f;
            Stop();
        }

        private static void CaptureTick()
        {
            if (!Capturing) return;
            if (_capMsgUntil > 0f)
            {
                if (Time.unscaledTime < _capMsgUntil) return;
                Stop();
                return;
            }
            if (!Input.anyKeyDown) return;

            foreach (KeyCode key in Keys())
            {
                if (!Input.GetKeyDown(key)) continue;
                if (key == KeyCode.Escape) { Stop(); return; }
                if (key == KeyCode.Delete || key == KeyCode.Backspace)
                {
                    Set(_capKey, "");
                    Stop();
                    return;
                }
                Keep(key.ToString());
                return;
            }
        }

        private static float _muteAt;

        private static void Mute()
        {
            if (Time.unscaledTime < _muteAt) return;
            _muteAt = Time.unscaledTime + 2f;
            try
            {
                var dispatcher = HotkeyDispatcher.Instance;
                var entries = dispatcher != null ? dispatcher.HotkeyEntries : null;
                if (entries == null) return;
                int off = 0;
                foreach (var entry in entries)
                {
                    if (entry == null || entry.KeyCode == KeyCode.None) continue;
                    entry.KeyCode = KeyCode.None;
                    off++;
                }
                if (off > 0) Plugin.Trace("[клавиши] игровых клавиш снято: " + off);
            }
            catch (Exception e) { Plugin.Trace("[клавиши] игровые клавиши: " + e.Message); }
        }

        private static float _learnAt;
        private static int _askedFor;
        private static float _askAt;
        private static bool _boundMastery;
        private static int _heard;
        private static int _tries;
        private const int Dodges = 4;
        private static readonly int[] Schools = { 1, 2, 3 };
        private const int HeardAll = (1 << 5) - 1;

        private static void AskMasteries()
        {
            if (Chars.Who == 0 || SideButtons.InCombat() || !SideButtons.InWorld()) return;
            if (_askedFor != Chars.Who)
            {
                _askedFor = Chars.Who;
                _heard = 0;
                _tries = 0;
            }
            if (_heard == HeardAll || _tries >= 3) return;
            if (Time.unscaledTime < _askAt) return;
            _askAt = Time.unscaledTime + 10f;
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                if (!_boundMastery)
                {
                    nc.AddMessageListener(11, OnMasteries);
                    nc.AddMessageListener(432, OnDodges);
                    _boundMastery = true;
                }
                _tries++;
                if ((_heard & 1) == 0) nc.SendRequest(new UserMasteryRequest());
                if ((_heard & 2) == 0) nc.SendRequest(new UserSpellbookDodgesListRequest(Dodges));
                for (int i = 0; i < Schools.Length; i++)
                    if ((_heard & (4 << i)) == 0) nc.SendRequest(new UserSpellbookDodgesListRequest(Schools[i]));
                Plugin.Trace("[клавиши] спросил у игры умения, приёмы и заклинания персонажа " + Chars.Who + ", попытка " + _tries);
            }
            catch (Exception e) { Plugin.Trace("[клавиши] запрос умений: " + e.Message); }
        }

        private static void OnMasteries(object m)
        {
            try
            {
                var msg = m as Transport.Messages.Responses.User.UserAvailableMasteryResponseMessage;
                if (msg == null) return;
                _heard |= 1;
                Known();
                int added = 0, saw = 0, passive = 0, noName = 0;
                if (msg.ClassMasteries != null)
                    foreach (var one in msg.ClassMasteries)
                    {
                        if (one == null || one.SkillLevel <= 0) continue;
                        saw++;
                        int id = one.SkillId;
                        if (id <= 0) continue;
                        if (!Active(id)) { passive++; continue; }
                        string name = Named(id);
                        if (name == null) { noName++; continue; }
                        string key = "skill:" + id;
                        string was;
                        if (Learned.TryGetValue(key, out was) && was == name) continue;
                        Learned[key] = name;
                        added++;
                    }
                Plugin.Trace("[клавиши] классовых умений у персонажа " + saw + ": записал " + added
                             + ", пассивных пропустил " + passive + ", без названия " + noName
                             + "; общие умения не беру, они пассивные");
                if (added > 0) KeepKnown();
            }
            catch (Exception e) { Plugin.Trace("[клавиши] список умений: " + e.Message); }
        }

        private static void OnDodges(object m)
        {
            try
            {
                var msg = m as Transport.Messages.Responses.User.UserSpellbookDodgesListResponseMessage;
                if (msg == null || msg.Items == null) return;
                bool dodges = msg.Selector == Dodges;
                int school = Array.IndexOf(Schools, msg.Selector);
                if (!dodges && school < 0) return;
                _heard |= dodges ? 2 : 4 << school;
                Known();
                int added = 0, noName = 0;
                foreach (var item in msg.Items)
                {
                    if (item == null) continue;
                    int id = dodges ? item.Value3 : item.Value1;
                    if (id <= 0) continue;
                    string name = dodges ? Dodged(id) : Cast(id);
                    if (name == null) { noName++; continue; }
                    string key = (dodges ? "trick:" : "spell:") + id;
                    string was;
                    if (Learned.TryGetValue(key, out was) && was == name) continue;
                    Learned[key] = name;
                    added++;
                }
                Plugin.Trace("[клавиши] " + (dodges ? "приёмов" : "заклинаний школы " + msg.Selector) + " у персонажа " + msg.Items.Count
                             + ": записал " + added + ", без названия " + noName);
                if (added > 0) KeepKnown();
            }
            catch (Exception e) { Plugin.Trace("[клавиши] список приёмов и заклинаний: " + e.Message); }
        }

        private static string Cast(int id)
        {
            try
            {
                string text = ResourceStrings.GetString("spells.spell" + id + ".name");
                if (string.IsNullOrEmpty(text) || text.StartsWith("spells.", StringComparison.Ordinal)) return null;
                return SkillList.Spoken(text);
            }
            catch { return null; }
        }

        private static string Dodged(int id)
        {
            try
            {
                string key = "dodges.dodge" + id + ".name";
                string text = ResourceStrings.GetString(key);
                if (string.IsNullOrEmpty(text) || text.StartsWith("dodges.", StringComparison.Ordinal)) return null;
                return SkillList.Spoken(text);
            }
            catch { return null; }
        }

        private static bool Active(int id)
        {
            try
            {
                var db = ClassSkillsDatabase.Instance;
                var row = db != null ? db.GetData(id) : null;
                return row == null || row.Active;
            }
            catch { return true; }
        }

        private static string Named(int id)
        {
            try
            {
                string text = ResourceStrings.GetSkillName(id);
                if (string.IsNullOrEmpty(text) || text.StartsWith("abilities.", StringComparison.Ordinal)) return null;
                return SkillList.Spoken(text);
            }
            catch { return null; }
        }

        private static void Learn()
        {
            if (Time.unscaledTime < _learnAt) return;
            _learnAt = Time.unscaledTime + 3f;
            if (!SideButtons.InCombat()) return;
            Known();
            bool fresh = false;
            var counted = new List<string>();
            foreach (var kind in new[] { SkillList.Kind.Abilities, SkillList.Kind.Tricks, SkillList.Kind.Spells })
            {
                var seenNow = SkillList.Of(kind);
                counted.Add(Mark(kind) + " " + seenNow.Count);
                foreach (var one in seenNow)
                {
                    if (one == null || string.IsNullOrEmpty(one.Name)) continue;
                    string key = Mark(kind) + ":" + one.Id;
                    string name = SkillList.Spoken(one.Name);
                    if (Learned.TryGetValue(key, out var was) && was == name) continue;
                    Learned[key] = name;
                    fresh = true;
                }
            }
            Plugin.Trace("[клавиши] в бою видно кнопок: " + string.Join(", ", counted.ToArray()));
            if (fresh) KeepKnown();
        }

        private static string Mark(SkillList.Kind kind)
        {
            return kind == SkillList.Kind.Tricks ? "trick" : kind == SkillList.Kind.Spells ? "spell" : "skill";
        }

        private static void Known()
        {
            if (_knownRead) return;
            _knownRead = true;
            Learned.Clear();
            string saved = Plugin.CfgHotkeySkills != null ? Plugin.CfgHotkeySkills.Value : "";
            if (string.IsNullOrEmpty(saved)) return;
            foreach (string part in saved.Split(PairSplit))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int at = part.IndexOf(NameSplit);
                if (at <= 0) continue;
                Learned[part.Substring(0, at)] = part.Substring(at + 1);
            }
        }

        private static void KeepKnown()
        {
            if (Plugin.CfgHotkeySkills == null) return;
            var text = new StringBuilder();
            foreach (var pair in Learned)
            {
                if (text.Length > 0) text.Append(PairSplit);
                text.Append(pair.Key).Append(NameSplit).Append(pair.Value);
            }
            Plugin.CfgHotkeySkills.Value = text.ToString();
            _parsed = false;
            Plugin.Trace("[клавиши] запомнено умений и приёмов: " + Learned.Count);
        }

        private static List<Act> Fixed()
        {
            return new List<Act>
            {
                new Act { Key = "win:inventory", Group = "Окна", Title = "Инвентарь", Fallback = "I",
                    Do = () => Spells.Menu(UserMenuController.ETabs.Inventory) },
                new Act { Key = "win:spells", Group = "Окна", Title = "Книга магии", Do = Spells.Open },
                new Act { Key = "win:daily", Group = "Окна", Title = "Задания дня",
                    Do = () => DependencyContainer.GetContainer()?.Resolve<DailyTasksWindowController>()?.OpenByButton() },
                new Act { Key = "win:quests", Group = "Окна", Title = "Задания списком", Do = QuestWindow.Toggle },
                new Act { Key = "win:online", Group = "Окна", Title = "Кто в игре", Fallback = "F9", Do = OnlineWindow.Toggle },
                new Act { Key = "win:set", Group = "Окна", Title = "Запасной набор", Do = Manikin.Toggle },

                new Act { Key = "world:town", Group = "Мир", Title = "Вернуться в город", Do = SideButtons.GoHome },
                new Act { Key = "world:stash", Group = "Мир", Title = "Хранилище: сдать или забрать вещи",
                    Do = () => { if (Artifacts.HasStash) Artifacts.Restore(); else Artifacts.Stash(); } },

                new Act { Key = "fight:phase", Group = "Бой", Title = "Завершить фазу", Fallback = "Tab", Do = Phase },
                new Act { Key = "fight:strike", Group = "Бой", Title = "Удар", Fallback = "Space", Do = Strike.Key },
                new Act { Key = "fight:next", Group = "Бой", Title = "Следующий враг", Do = Targets.Next },
                new Act { Key = "fight:effects", Group = "Бой", Title = "Окно эффектов", Do = EffectsWindow.Toggle },
                new Act { Key = "fight:zones", Group = "Бой", Title = "Показать зоны наведения на бойцов", Do = HintZones.Toggle },

                new Act { Key = "flask:0", Group = "Банки", Title = "Банка жизни", Do = () => Flasks.Use(0) },
                new Act { Key = "flask:1", Group = "Банки", Title = "Банка маны", Do = () => Flasks.Use(1) },
                new Act { Key = "flask:2", Group = "Банки", Title = "Банка энергии", Do = () => Flasks.Use(2) },
                new Act { Key = "flask:3", Group = "Банки", Title = "Грибы", Do = () => Flasks.Use(3) },
            };
        }

        private static void Phase()
        {
            try
            {
                var ctrl = Controllers.Get<CombatButtonsController>();
                if (ctrl == null) return;
                var button = HarmonyLib.AccessTools.Property(typeof(CombatButtonsController), "EndPhaseButton")
                    ?.GetValue(ctrl, null) as EndPhaseButton;
                if (button == null) { Plugin.Trace("[клавиши] кнопки фазы нет"); return; }
                button.OnClick();
            }
            catch (Exception e) { Plugin.Trace("[клавиши] фаза: " + e.Message); }
        }

        internal static List<Act> All()
        {
            var list = Fixed();
            Known();
            var live = new Dictionary<string, string>();
            if (SideButtons.InCombat())
                foreach (var kind in new[] { SkillList.Kind.Abilities, SkillList.Kind.Tricks, SkillList.Kind.Spells })
                    foreach (var one in SkillList.Of(kind))
                        if (one != null && !string.IsNullOrEmpty(one.Name)) live[Mark(kind) + ":" + one.Id] = SkillList.Spoken(one.Name);

            foreach (var pair in Learned)
                if (!live.ContainsKey(pair.Key)) live[pair.Key] = SkillList.Spoken(pair.Value);

            foreach (var pair in live)
            {
                int at = pair.Key.IndexOf(':');
                if (at <= 0) continue;
                string kind = pair.Key.Substring(0, at);
                int id;
                if (!int.TryParse(pair.Key.Substring(at + 1), out id)) continue;
                string group = kind == "trick" ? "Приёмы" : kind == "spell" ? "Магия" : "Умения";
                list.Add(new Act
                {
                    Key = pair.Key,
                    Group = group,
                    Title = pair.Value,
                    Do = () => SkillList.Use(kind, id),
                });
            }
            list.Sort((a, b) =>
            {
                int byGroup = Order(a.Group).CompareTo(Order(b.Group));
                if (byGroup != 0) return byGroup;
                if (Order(a.Group) < 4) return 0;
                return string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        private static int Order(string group)
        {
            switch (group)
            {
                case "Окна": return 0;
                case "Мир": return 1;
                case "Бой": return 2;
                case "Банки": return 3;
                case "Умения": return 4;
                case "Приёмы": return 5;
                default: return 6;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(StandaloneInputModule), "SendSubmitEventToSelectedObject")]
    internal static class SubmitOffPatch
    {
        private static bool _told;

        private static bool Prefix(StandaloneInputModule __instance, ref bool __result)
        {
            try
            {
                var system = EventSystem.current;
                var picked = system != null ? system.currentSelectedGameObject : null;
                if (picked == null) return true;
                if (picked.GetComponent<InputField>() != null || picked.GetComponent<TMPro.TMP_InputField>() != null) return true;
                __result = false;
                var input = __instance.input;
                if (input == null) return false;
                if (input.GetButtonDown(__instance.submitButton) && !_told)
                {
                    _told = true;
                    Plugin.Trace("[клавиши] пробел и Enter не нажимают выбранную кнопку «" + picked.name + "», клавиши работают только по списку мода");
                }
                if (input.GetButtonDown(__instance.cancelButton))
                {
                    var data = new BaseEventData(system);
                    ExecuteEvents.Execute(picked, data, ExecuteEvents.cancelHandler);
                    __result = data.used;
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Trace("[клавиши] выбранная кнопка: " + e.Message);
                return true;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ButtonHotkeyHandler), "Update")]
    internal static class ButtonKeysOffPatch
    {
        private static bool Prefix() => false;
    }

    [HarmonyLib.HarmonyPatch(typeof(ButtonHotkeyHandler), "GetHotkeyCaption")]
    internal static class ButtonKeysBlankPatch
    {
        private static bool Prefix(ref string __result)
        {
            __result = "";
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(SetupDialog), "Start")]
    internal static class HotkeysBeforeLoginPatch
    {
        private static readonly string[] Parts = { "HotkeysGroup", "HotkeysCaptionText", "HotkeysButton" };

        private static void Postfix(SetupDialog __instance)
        {
            if (SideButtons.InWorld()) return;
            try
            {
                foreach (string name in Parts)
                {
                    var value = HarmonyLib.AccessTools.Field(typeof(SetupDialog), name)?.GetValue(__instance);
                    var go = value as GameObject ?? (value as Component)?.gameObject;
                    if (go != null) go.SetActive(false);
                }
            }
            catch (Exception e) { Plugin.Trace("[клавиши] настройки до входа: " + e.Message); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(SetupDialogController), "OnHotkeysButtonClick")]
    internal static class HotkeysButtonPatch
    {
        private static bool Prefix()
        {
            if (!SideButtons.InWorld()) return true;
            try { Settings.ToggleHotkeys(); return false; }
            catch (Exception e) { Plugin.Fault("[клавиши] окно: " + e.Message); return true; }
        }
    }
}
