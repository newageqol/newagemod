using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NewAgeQoL
{
    internal static class Health
    {
        private const int SpotCap = 400;
        private const int TopSpots = 8;
        private const float NoticeSeconds = 20f;

        private sealed class Spot
        {
            internal string Where;
            internal string Last;
            internal int Count;
            internal int Told;
        }

        private static readonly object Gate = new object();
        private static readonly List<string> Broken = new List<string>();
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static readonly Dictionary<string, Spot> Spots = new Dictionary<string, Spot>();
        private static readonly HashSet<string> Owners = new HashSet<string>();
        private const string Ours = "NewAge";

        private static int _patched;
        private static int _unpatched;
        private static int _numbers;
        private static int _toldBroken;
        private static int _slips;
        private static int _toldSlips;
        private static bool _listening;
        private static bool _noticed;

        private static readonly KeyValuePair<string, short>[] Built =
        {
            Pair(nameof(ResponseConsts.CHAT_RESPONCE), ResponseConsts.CHAT_RESPONCE),
            Pair(nameof(ResponseConsts.USER_MASTERY_RESPONCE), ResponseConsts.USER_MASTERY_RESPONCE),
            Pair(nameof(ResponseConsts.UPDATE_FIGHT_ANONCE), ResponseConsts.UPDATE_FIGHT_ANONCE),
            Pair(nameof(ResponseConsts.NEW_ROUND_RESPONCE), ResponseConsts.NEW_ROUND_RESPONCE),
            Pair(nameof(ResponseConsts.ANIMATION_RESPONCE), ResponseConsts.ANIMATION_RESPONCE),
            Pair(nameof(ResponseConsts.WEAR_RESPONCE), ResponseConsts.WEAR_RESPONCE),
            Pair(nameof(ResponseConsts.MARKET_CONTENT_RESPONCE), ResponseConsts.MARKET_CONTENT_RESPONCE),
            Pair(nameof(ResponseConsts.USER_ENCHANTMENT_RESPONCE), ResponseConsts.USER_ENCHANTMENT_RESPONCE),
            Pair(nameof(ResponseConsts.USE_BUTTON_RESPONCE), ResponseConsts.USE_BUTTON_RESPONCE),
            Pair(nameof(ResponseConsts.COMBAT_INFO_RESPONCE), ResponseConsts.COMBAT_INFO_RESPONCE),
            Pair(nameof(ResponseConsts.FRIENDS_LIST_RESPONCE), ResponseConsts.FRIENDS_LIST_RESPONCE),
            Pair(nameof(ResponseConsts.QUEST_DIALOG_RESPONCE), ResponseConsts.QUEST_DIALOG_RESPONCE),
            Pair(nameof(ResponseConsts.QUICK_SLOTS_RESPONSE), ResponseConsts.QUICK_SLOTS_RESPONSE),
            Pair(nameof(ResponseConsts.NEW_COMBAT_RESULT_RESPONCE), ResponseConsts.NEW_COMBAT_RESULT_RESPONCE),
            Pair(nameof(ResponseConsts.THING_TYPE_TABS_RESPONSE), ResponseConsts.THING_TYPE_TABS_RESPONSE),
            Pair(nameof(ResponseConsts.THING_TAB_INVENTORY_RESPONSE), ResponseConsts.THING_TAB_INVENTORY_RESPONSE),
            Pair(nameof(ResponseConsts.BOX_TAB_RESPONSE), ResponseConsts.BOX_TAB_RESPONSE),
            Pair(nameof(ResponseConsts.GENERAL_THING_HINT_RESPONSE), ResponseConsts.GENERAL_THING_HINT_RESPONSE),
            Pair(nameof(ResponseConsts.CONTEXT_INVENTORY_THING_HINT_RESPONSE), ResponseConsts.CONTEXT_INVENTORY_THING_HINT_RESPONSE),
            Pair(nameof(ResponseConsts.GENERAL_RECIPE_HINT_RESPONSE), ResponseConsts.GENERAL_RECIPE_HINT_RESPONSE),
            Pair(nameof(ResponseConsts.ARTEFACT_INFO_RESPONSE), ResponseConsts.ARTEFACT_INFO_RESPONSE),
            Pair(nameof(ResponseConsts.ARTEFACT_CREATE_RESPONSE), ResponseConsts.ARTEFACT_CREATE_RESPONSE),
            Pair(nameof(ResponseConsts.ARTEFACT_RESHOE_RESPONSE), ResponseConsts.ARTEFACT_RESHOE_RESPONSE),
            Pair(nameof(ResponseConsts.ARTEFACT_UPDATE_RESPONSE), ResponseConsts.ARTEFACT_UPDATE_RESPONSE),
            Pair(nameof(ResponseConsts.ARTEFACT_LIST_FOR_OPERATION_RESPONSE), ResponseConsts.ARTEFACT_LIST_FOR_OPERATION_RESPONSE),
            Pair(nameof(ResponseConsts.COMBAT_USERS_WITH_CHANGING_STATES_RESPONSE), ResponseConsts.COMBAT_USERS_WITH_CHANGING_STATES_RESPONSE),
            Pair(nameof(ResponseConsts.STATE_GROUPS_ON_USER_RESPONSE), ResponseConsts.STATE_GROUPS_ON_USER_RESPONSE),
            Pair(nameof(ResponseConsts.GET_ACTION_EFFECT_DESCRIPTION_RESPONSE), ResponseConsts.GET_ACTION_EFFECT_DESCRIPTION_RESPONSE),
            Pair(nameof(ResponseConsts.INVENTORY_WEAR_RESPONSE), ResponseConsts.INVENTORY_WEAR_RESPONSE),
            Pair(nameof(ResponseConsts.THING_CONTEXT_ACTION_RESPONSE), ResponseConsts.THING_CONTEXT_ACTION_RESPONSE),
            Pair(nameof(ResponseConsts.DYNAMIC_HINT_RESPONSE), ResponseConsts.DYNAMIC_HINT_RESPONSE),
            Pair(nameof(ResponseConsts.INSUFFICIENT_CASH_RESPONSE), ResponseConsts.INSUFFICIENT_CASH_RESPONSE),
            Pair(nameof(ResponseConsts.QUEST_REWARD_RESPONSE), ResponseConsts.QUEST_REWARD_RESPONSE),
            Pair(nameof(ResponseConsts.USER_SPELLBOOK_DODGES_LIST_RESPONSE), ResponseConsts.USER_SPELLBOOK_DODGES_LIST_RESPONSE),
            Pair(nameof(ResponseConsts.UNITY3D_USER_INFO_RESPONSE), ResponseConsts.UNITY3D_USER_INFO_RESPONSE),
            Pair(nameof(ResponseConsts.USER_PROFESSION_LIST_RESPONSE), ResponseConsts.USER_PROFESSION_LIST_RESPONSE),
            Pair(nameof(ResponseConsts.UNITY3D_GET_DAILY_REWARD_RESPONSE), ResponseConsts.UNITY3D_GET_DAILY_REWARD_RESPONSE),
            Pair(nameof(ResponseConsts.UNITY3D_PLAYER_LIST_RESPONSE), ResponseConsts.UNITY3D_PLAYER_LIST_RESPONSE),
            Pair(nameof(ResponseConsts.UNITY3D_MONSTER_INFO_RESPONSE), ResponseConsts.UNITY3D_MONSTER_INFO_RESPONSE),
        };

        private static KeyValuePair<string, short> Pair(string name, short built) => new KeyValuePair<string, short>(name, built);

        internal static void Listen()
        {
            if (_listening) return;
            _listening = true;
            try { HarmonyLib.Tools.Logger.MessageReceived += OnHarmony; }
            catch (Exception e) { Plugin.Log?.LogWarning("[health] Harmony messages are not watched: " + e.Message); }
        }

        internal static void Silence()
        {
            if (!_listening) return;
            _listening = false;
            try { HarmonyLib.Tools.Logger.MessageReceived -= OnHarmony; }
            catch (Exception e) { Plugin.Log?.LogWarning("[health] Harmony messages subscription not removed: " + e.Message); }
        }

        private static void OnHarmony(object sender, HarmonyLib.Tools.Logger.LogEventArgs e)
        {
            try
            {
                if (e == null || string.IsNullOrEmpty(e.Message)) return;
                var loud = HarmonyLib.Tools.Logger.LogChannel.Warn | HarmonyLib.Tools.Logger.LogChannel.Error;
                if ((e.LogChannel & loud) == 0) return;
                lock (Gate)
                {
                    if (!Seen.Add(e.Message)) return;
                }
                string owner = Owner();
                if (owner == null) return;
                Break(owner, e.Message);
            }
            catch { }
        }

        private static string Owner()
        {
            var frames = new StackTrace(2, false).GetFrames();
            if (frames == null) return null;
            foreach (var frame in frames)
            {
                var type = frame.GetMethod()?.DeclaringType;
                if (type == null || type == typeof(Health)) continue;
                if (!type.Assembly.GetName().Name.StartsWith(Ours, StringComparison.Ordinal)) continue;
                while (type.DeclaringType != null) type = type.DeclaringType;
                return type.Name;
            }
            return null;
        }

        private static void Break(string owner, string what)
        {
            lock (Gate)
            {
                Broken.Add(owner + ": " + what);
                Owners.Add(owner);
            }
        }

        internal static void Patched() => _patched++;

        internal static void Unpatched(Type patch, Exception e)
        {
            _unpatched++;
            Break(patch != null ? patch.Name : "?", "patch not applied - " + (e != null ? e.Message : "?"));
        }

        internal static void Heard(string source, string text)
        {
            const string mark = "Harmony: ";
            if (text == null || !text.StartsWith(mark, StringComparison.Ordinal)) return;
            _unpatched++;
            Break(source, "patch not applied - " + text.Substring(mark.Length));
        }

        internal static void Number(string name, short built)
        {
            try
            {
                _numbers++;
                var field = typeof(ResponseConsts).GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (field == null)
                {
                    Break("protocol", "server message " + name + " (" + built + ") is gone from the game");
                    return;
                }
                short now = Convert.ToInt16(field.GetRawConstantValue());
                if (now != built) Break("protocol", "server message " + name + " was " + built + " when the mod was built, the game now has " + now);
            }
            catch (Exception e) { Break("protocol", "server message " + name + " not checked - " + e.Message); }
        }

        internal static void Start()
        {
            foreach (var pair in Built) Number(pair.Key, pair.Value);
            Tell(true);
        }

        internal static void Count(string text, string who, int line)
        {
            string key = who + ":" + line;
            lock (Gate)
            {
                _slips++;
                Spot spot;
                if (!Spots.TryGetValue(key, out spot))
                {
                    if (Spots.Count >= SpotCap) return;
                    Spots[key] = spot = new Spot { Where = key };
                }
                spot.Count++;
                spot.Last = text;
            }
        }

        internal static void Tell(bool always = false)
        {
            try
            {
                List<string> fresh;
                string slips = null;
                int broken;
                lock (Gate)
                {
                    broken = Broken.Count;
                    fresh = Broken.Skip(_toldBroken).ToList();
                    _toldBroken = broken;
                    if (_slips != _toldSlips)
                    {
                        var grown = Spots.Values.Where(s => s.Count != s.Told).OrderByDescending(s => s.Count - s.Told).ToList();
                        slips = "[health] warnings and caught errors: " + (_slips - _toldSlips) + " new, " + _slips + " since start; most new: " +
                                string.Join("; ", grown.Take(TopSpots).Select(s => s.Where + " x" + (s.Count - s.Told) + " (" + Short(s.Last) + ")"));
                        foreach (var s in grown) s.Told = s.Count;
                        _toldSlips = _slips;
                    }
                }
                if (always)
                    Plugin.Log?.LogInfo("[health] patches applied " + _patched + ", not applied " + _unpatched + "; server message numbers checked " +
                                        _numbers + "; problems " + broken);
                foreach (var one in fresh) Plugin.Log?.LogWarning("[health] " + one);
                if (slips != null) Plugin.Log?.LogInfo(slips);
            }
            catch { }
        }

        internal static void Tick()
        {
            if (_noticed) return;
            string owners;
            lock (Gate)
            {
                if (Owners.Count == 0) return;
                owners = string.Join(", ", Owners.OrderBy(o => o).Take(6)) + (Owners.Count > 6 ? "…" : "");
            }
            if (!SideButtons.InWorld()) return;
            _noticed = true;
            Notice.Show(Lang.F("После обновления игры часть мода может не работать: {0}. Нажми «Сообщить об ошибке» в настройках — список неполадок приложится сам.", owners), NoticeSeconds);
        }

        internal static string Text()
        {
            var sb = new StringBuilder();
            lock (Gate)
            {
                sb.Append("mod ").Append(Plugin.Version).Append('\n');
                sb.Append("patches applied ").Append(_patched).Append(", not applied ").Append(_unpatched).Append('\n');
                sb.Append("server message numbers checked ").Append(_numbers).Append('\n');
                sb.Append("problems ").Append(Broken.Count).Append('\n');
                foreach (var one in Broken) sb.Append("  ").Append(one).Append('\n');
                sb.Append("warnings and caught errors since start ").Append(_slips).Append(", places ").Append(Spots.Count).Append('\n');
                foreach (var s in Spots.Values.OrderByDescending(s => s.Count))
                    sb.Append("  ").Append(s.Where).Append(" x").Append(s.Count).Append("  ").Append(Short(s.Last)).Append('\n');
            }
            return sb.ToString();
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace('\n', ' ').Replace('\r', ' ');
            return text.Length > 160 ? text.Substring(0, 160) + "…" : text;
        }
    }
}
