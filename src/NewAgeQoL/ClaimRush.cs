using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using Transport.Messages.Responses.Chat;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;

namespace NewAgeQoL
{
    [HarmonyPatch(typeof(AdminMessagesController), "ShowMessage")]
    internal static class ClaimRushHeardPatch
    {
        private static void Postfix(ChatResponseMessage msg)
        {
            string text = msg != null ? msg.Text : null;
            try { NuarmarRush.Rush.Heard(text); }
            catch (Exception e) { Plugin.Caught("[nuarmar rush] " + e.Message); }
            try { DozenRush.Rush.Heard(text); }
            catch (Exception e) { Plugin.Caught("[dozen rush] " + e.Message); }
        }
    }

    internal static class Rushes
    {
        internal static void Tick()
        {
            try { NuarmarRush.Tick(); }
            catch (Exception e) { Plugin.Caught("[nuarmar rush] tick " + e.Message); }
            try { DozenRush.Tick(); }
            catch (Exception e) { Plugin.Caught("[dozen rush] tick " + e.Message); }
        }

        internal static void Shutdown()
        {
            NuarmarRush.Shutdown();
            DozenRush.Shutdown();
        }

        internal static void Bind()
        {
            NuarmarRush.Bind();
            DozenRush.Bind();
        }
    }

    internal sealed class ClaimBoard
    {
        private readonly string _tag;
        private readonly int _kind;
        private readonly float _shelf;
        private readonly bool _byLevel;
        private readonly Dictionary<int, float> _open = new Dictionary<int, float>();
        private INetworkConnection _on;

        internal ClaimBoard(string tag, int kind, float shelf, bool byLevel)
        {
            _tag = tag;
            _kind = kind;
            _shelf = shelf;
            _byLevel = byLevel;
        }

        internal void Tick()
        {
            Listen();
            if (_open.Count > 0 && Spectate.Map() != ClaimRush.Tournaments) _open.Clear();
        }

        private void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected() || ReferenceEquals(_on, nc)) return;
            Shutdown();
            nc.RemoveMessageListener(ResponseConsts.UPDATE_FIGHT_ANONCE, OnAnnounce);
            nc.AddMessageListener(ResponseConsts.UPDATE_FIGHT_ANONCE, OnAnnounce);
            _on = nc;
        }

        internal void Shutdown()
        {
            try { if (_on != null) _on.RemoveMessageListener(ResponseConsts.UPDATE_FIGHT_ANONCE, OnAnnounce); }
            catch { }
            _on = null;
            _open.Clear();
        }

        private void OnAnnounce(object msg)
        {
            try
            {
                var one = msg as FightAnnounce;
                if (one == null || one.Id <= 0) return;
                if (one.Type != 1) { _open.Remove(one.Id); return; }
                if (one.ClaimType != _kind) return;
                if (_byLevel)
                {
                    int level = ClaimRush.Level();
                    if (level > 0 && (level < one.MinLevel || level > one.MaxLevel)) return;
                }
                _open[one.Id] = Time.realtimeSinceStartup;
                Plugin.Trace("[" + _tag + "] claim " + one.Id + " open, levels " + one.MinLevel + "-" + one.MaxLevel);
            }
            catch (Exception e) { Plugin.Caught("[" + _tag + "] announce: " + e.Message); }
        }

        internal int Pick()
        {
            int best = 0;
            float bestAt = float.MinValue;
            float now = Time.realtimeSinceStartup;
            foreach (var pair in _open)
                if ((_shelf <= 0f || now - pair.Value <= _shelf) && pair.Value > bestAt) { best = pair.Key; bestAt = pair.Value; }
            return best;
        }
    }

    internal static class NuarmarRush
    {
        internal const string Title = "Нуармар: по объявлению сразу в турниры и в заявку";

        private static ConfigEntry<bool> _cfg;
        private static readonly ClaimBoard Board = new ClaimBoard("nuarmar rush", 9, 60f, false);

        internal static readonly ClaimRush Rush = new ClaimRush("nuarmar rush", "Нуармар", "Нуармара", "⚓", "нуармар", 90f, Enabled, Board.Pick, null);

        internal static ConfigEntry<bool> Enabled() => _cfg;

        internal static void Bind()
        {
            Chars.Own("Town", "NuarmarRush", false,
                "По системному объявлению о Нуармаре сразу идёт в турниры и входит в заявку. Если в этот момент ты в бою или в другой заявке — объявление пропускается. Каждый шаг — уведомлением Windows.",
                e => _cfg = e);
        }

        internal static void Tick()
        {
            Board.Tick();
            Rush.Tick();
        }

        internal static void Shutdown() => Board.Shutdown();
    }

    internal static class DozenRush
    {
        private const int Kind = (int)EClaimType.Dozen;

        internal const string Title = "Дюжина клинков: по объявлению для моего уровня в турниры и в заявку";

        private static ConfigEntry<bool> _cfg;
        private static readonly ClaimBoard Board = new ClaimBoard("dozen rush", Kind, 0f, true);

        internal static readonly ClaimRush Rush = new ClaimRush("dozen rush", "Дюжина клинков", "Дюжины клинков", "⚔️", "дюжина клинк", 600f, Enabled, Pick, Fits);

        internal static ConfigEntry<bool> Enabled() => _cfg;

        internal static void Bind()
        {
            Chars.Own("Town", "DozenRush", false,
                "По системному объявлению о Дюжине клинков для моего уровня идёт в турниры и входит в заявку. Если в этот момент ты в бою или в другой заявке — объявление пропускается; после входа 30 мин не возвращается. Каждый шаг — уведомлением Windows.",
                e => _cfg = e);
        }

        internal static void Tick()
        {
            Board.Tick();
            Rush.Tick();
        }

        internal static void Shutdown() => Board.Shutdown();

        private static int Pick()
        {
            int best = Board.Pick();
            if (best > 0) Dozen.Joining(best, Kind);
            return best;
        }

        private static bool Fits(string text)
        {
            int level = ClaimRush.Level();
            if (level <= 0) return true;
            var range = Regex.Match(text, @"(\d+)\s*[-–—]\s*(\d+)");
            if (range.Success)
                return level >= int.Parse(range.Groups[1].Value) && level <= int.Parse(range.Groups[2].Value);
            var from = Regex.Match(text, @"(\d+)\s*\+");
            if (from.Success) return level >= int.Parse(from.Groups[1].Value);
            return true;
        }
    }

    internal sealed class ClaimRush
    {
        private const int Arena = 3;
        internal const int Tournaments = 334;
        private const int TournamentWait = 335;
        private const float Again = 120f;
        private const float Rest = 1800f;
        private const int MostTries = 3;

        internal static Func<ClaimRush, bool> FromFight;
        internal static Action<ClaimRush> Going;
        internal static Action<string, string> Told;
        internal static bool Later;

        private static bool _busy;
        private static bool _ok;

        private readonly string _tag;
        private readonly string _name;
        private readonly string _whose;
        private readonly string _icon;
        private readonly string _word;
        private readonly float _window;
        private readonly Func<ConfigEntry<bool>> _enabled;
        private readonly Func<int> _pick;
        private readonly Func<string, bool> _fits;
        private float _lastAt = float.NegativeInfinity;
        private float _pendingAt = -1f;
        private float _inAt = float.NegativeInfinity;
        private readonly HashSet<int> _taken = new HashSet<int>();
        private readonly Dictionary<int, int> _tries = new Dictionary<int, int>();

        internal ClaimRush(string tag, string name, string whose, string icon, string word, float window,
            Func<ConfigEntry<bool>> enabled, Func<int> pick, Func<string, bool> fits)
        {
            _tag = tag;
            _name = name;
            _whose = whose;
            _icon = icon;
            _word = word;
            _window = window;
            _enabled = enabled;
            _pick = pick;
            _fits = fits;
        }

        internal string Name => _name;
        internal string Whose => _whose;
        internal string Tag => _tag;
        internal string Icon => _icon;

        internal void Wait() => _pendingAt = Time.realtimeSinceStartup;

        private bool On()
        {
            var on = _enabled();
            return on != null && on.Value;
        }

        internal void Info(string text) => Plugin.Log.LogInfo("[" + _tag + "] " + text);

        private void Tell(string icon, string text)
        {
            Blink.Notify(Lang.T(_name), Lang.T(text));
            try { Told?.Invoke(icon, Lang.T(_name) + ": " + Lang.T(text)); }
            catch (Exception e) { Plugin.Caught("[" + _tag + "] tell: " + e.Message); }
        }

        internal static int Level()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null ? info.Level : 0;
            }
            catch { return 0; }
        }

        internal void Heard(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(_word, StringComparison.OrdinalIgnoreCase) < 0) return;
            Info("announcement: " + text);
            if (!On() || Plugin.Instance == null) return;
            if (_fits != null && !_fits(text)) { Info("not for my level, staying"); return; }
            float now = Time.realtimeSinceStartup;
            if (_busy || now - _lastAt < Again) return;
            if (now - _inAt < Rest) { Info("already joined this event, not going back"); return; }
            _lastAt = now;
            bool handled = false;
            try { handled = FromFight != null && FromFight(this); }
            catch (Exception e) { Plugin.Caught("[" + _tag + "] from fight: " + e.Message); }
            if (handled) return;
            if (SideButtons.InCombat() || InClaim())
            {
                if (!Later)
                {
                    Info("busy in a fight or claim, skipping this announcement");
                    Tell(_icon, "объявление пришло, но ты в бою или в заявке — в этот раз не иду");
                    return;
                }
                _pendingAt = now;
                Info("busy in a fight or claim, will go once free");
                Tell(_icon, "объявление пришло, пойду в заявку, как только освобожусь");
                return;
            }
            Go(now);
        }

        internal void Tick()
        {
            if (_pendingAt < 0f || _busy) return;
            if (!On() || Time.realtimeSinceStartup - _pendingAt > _window) { _pendingAt = -1f; return; }
            if (SideButtons.InCombat() || InClaim() || !Travel.Connected() || Spectate.Map() >= 100000) return;
            float at = _pendingAt;
            _pendingAt = -1f;
            Info("free now, going");
            Go(at);
        }

        private void Go(float heardAt)
        {
            if (Plugin.Instance == null) return;
            Travel.Cancel(Lang.T("ушёл в заявку ") + Lang.T(_whose));
            Tell(_icon, "иду в заявку");
            try { Going?.Invoke(this); }
            catch (Exception e) { Plugin.Caught("[" + _tag + "] going: " + e.Message); }
            Plugin.Instance.StartCoroutine(Run(heardAt));
        }

        private static bool InClaim()
        {
            try
            {
                return SideButtons.IsScene(EUnityScene.ChaoticWait) || SideButtons.IsScene(EUnityScene.QuestCombatWait)
                       || Spectate.Map() == TournamentWait;
            }
            catch { return false; }
        }

        private IEnumerator Run(float heardAt)
        {
            _busy = true;
            try
            {
                if (!Travel.Connected()) { Fail("нет соединения"); yield break; }
                Info("going from map " + Spectate.Map());

                if (Spectate.Map() != Tournaments)
                {
                    if (Spectate.Map() != Arena && !Exit(Tournaments))
                    {
                        if (Exit(Arena))
                        {
                            Travel.Say(Lang.T(_name) + Lang.T(": выхожу в арену…"));
                            yield return Hop(Arena, Arena);
                            if (!_ok) { Fail("не дошёл до арены"); yield break; }
                        }
                        else
                        {
                            if (!Travel.InTown())
                            {
                                Travel.Say(Lang.T(_name) + Lang.T(": телепорт в город…"));
                                int load = Travel.LoadStamp;
                                if (!TownWalk.Send(new ReturnToIlleniumRequest(true))) { Fail("телепорт не ушёл"); yield break; }
                                yield return Until(() => Travel.LoadStamp != load && Travel.InTown(), 90f, true);
                                if (!_ok) { Fail("не дождался города"); yield break; }
                                yield return Pause();
                            }
                            int door = Plugin.CfgTownArenaId != null && Plugin.CfgTownArenaId.Value > 0
                                ? Plugin.CfgTownArenaId.Value : TownWalk.Door(Plugin.CfgTownArenaWords);
                            if (door <= 0) { Fail("не нашёл вход в арену"); yield break; }
                            Travel.Say(Lang.T(_name) + Lang.T(": иду в арену…"));
                            yield return Hop(door, Arena);
                            if (!_ok) { Fail("не дошёл до арены"); yield break; }
                        }
                    }
                    Travel.Say(Lang.T(_name) + Lang.T(": иду в турниры…"));
                    yield return Hop(Tournaments, Tournaments);
                    if (!_ok) { Fail("не дошёл до турниров"); yield break; }
                }

                Travel.Say(Lang.T(_name) + Lang.T(": жду заявку…"));
                while (Time.realtimeSinceStartup - heardAt < _window)
                {
                    if (!Travel.Connected()) { Fail("нет соединения"); yield break; }
                    if (SideButtons.InCombat() || InClaim()) break;
                    if (Spectate.Map() != Tournaments) { Fail("ушёл из турниров"); yield break; }
                    int id = _pick();
                    if (id <= 0 || _taken.Contains(id)) { yield return null; continue; }
                    _tries.TryGetValue(id, out int tried);
                    if (tried >= MostTries) { _taken.Add(id); continue; }
                    _tries[id] = tried + 1;
                    var conn = NetworkConnection.Instance;
                    if (conn == null || !conn.IsConnected()) { Fail("нет соединения"); yield break; }
                    Info("joining claim " + id);
                    conn.SendRequest(new FightJoinRequest(id));
                    yield return Until(() => InClaim() || SideButtons.InCombat(), 1f, false);
                    if (_ok) { _taken.Add(id); break; }
                    Info("claim " + id + " did not take, map " + Spectate.Map() + ", scene " + SideButtons.Scene());
                }

                if (InClaim() || SideButtons.InCombat())
                {
                    _inAt = Time.realtimeSinceStartup;
                    Info("in, map " + Spectate.Map() + ", scene " + SideButtons.Scene());
                    Travel.Say(Lang.T(_name) + Lang.T(": в заявке."));
                    Tell("✅", "ты в заявке");
                }
                else Fail("заявку не поймал");
            }
            finally { _busy = false; }
        }

        private void Fail(string why)
        {
            Info(why + ", map " + Spectate.Map());
            Travel.Say(Lang.T(_name) + ": " + Lang.T(why) + ".");
            Tell("⚠️", why);
        }

        private static IEnumerator Hop(int to, int map)
        {
            _ok = false;
            int load = Travel.LoadStamp;
            if (!TownWalk.Send(new ChangeMapRequest(to))) yield break;
            yield return Until(() => Travel.LoadStamp != load && Spectate.Map() == map, 60f, true);
            if (_ok) yield return Pause();
        }

        private static bool Exit(int to)
        {
            var map = Travel.LastMap;
            if (map == null || map.SceneObjects == null) return false;
            foreach (var door in map.SceneObjects)
                if (door != null && door.ObjectType == EObjectType.Link && door.Id == to) return true;
            return false;
        }

        private static IEnumerator Pause()
        {
            yield return null;
            yield return null;
        }

        private static IEnumerator Until(Func<bool> done, float timeout, bool peace)
        {
            _ok = false;
            float t = 0f;
            while (true)
            {
                if (done()) { _ok = true; yield break; }
                if (!Travel.Connected() || peace && SideButtons.InCombat()) yield break;
                yield return null;
                t += Time.unscaledDeltaTime;
                if (t >= timeout) yield break;
            }
        }
    }
}
