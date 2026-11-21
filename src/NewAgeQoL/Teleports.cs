using System;
using System.Collections;
using System.Collections.Generic;
using Transport.Messages.Responses.Quest.Dialog;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class Teleports
    {
        internal static readonly int[] Runes =
        {
            594, 595, 673, 506, 577, 13129, 13130, 10430, 10431, 10432, 11465, 11466, 11467,
            10466, 10506, 10870, 10871, 10872, 11115, 11609, 11610,
        };

        private static readonly int[] HomeRunes = { 593, 631 };

        private static readonly Dictionary<int, int> Seeded = new Dictionary<int, int>
        {
            [594] = 1,
            [673] = 7,
            [506] = 8,
            [13129] = 12,
        };

        private static readonly Dictionary<int, int> PortalOf = new Dictionary<int, int>
        {
            [2] = 74,
            [63] = 75,
            [85] = 159,
        };

        private static readonly Dictionary<int, int[]> RunesAt = new Dictionary<int, int[]>
        {
            [74] = new[] { 593, 631 },
            [75] = new[] { 594, 595 },
            [159] = new[] { 673 },
        };

        private const int Illenium = 2;

        internal static bool Busy => _running > 0;

        private static int _running;
        private static int _asked;
        private static float _askedAt;
        private static string _raw;
        private static readonly Dictionary<int, int> Learned = new Dictionary<int, int>();
        private static HashSet<int> _offered;
        private static int _offeredAt;
        private static float _heardAt;

        internal static bool Want => SideButtons.InWorld();

        private static readonly Dictionary<int, bool> Owned = new Dictionary<int, bool>();

        internal static bool Has(int rune)
        {
            if (rune <= 0) return false;
            bool scanning = Flasks.Scanning;
            if (scanning && Owned.TryGetValue(rune, out bool was)) return was;
            bool now = Flasks.QtyOf(rune) > 0;
            if (!scanning) Owned[rune] = now;
            return now;
        }

        internal static bool Watched(int thing) => thing > 0 && (Array.IndexOf(Runes, thing) >= 0 || Array.IndexOf(HomeRunes, thing) >= 0);

        private const float LookGap = 3f;
        private static bool _wasWorld;
        private static int _lastLoc = -1;
        private static bool _lookDue;
        private static float _lookAt = -999f;

        private static void Watch()
        {
            bool world = Want;
            int loc = world ? Loc : -1;
            if (world && (!_wasWorld || loc != _lastLoc)) _lookDue = true;
            _wasWorld = world;
            _lastLoc = loc;
            if (!_lookDue || !world || Busy || TownWalk.Busy || Travel.Busy || RealTime.Now - _lookAt < LookGap) return;
            _lookDue = false;
            _lookAt = RealTime.Now;
            Flasks.RequestScanNow();
        }

        internal static bool Running(int rune) => _running == rune;

        internal static string Hint(int rune)
        {
            Flasks.AskName(rune);
            string name = Flasks.DisplayName(rune) ?? Lang.T("Руна портала");
            if (_running == rune) return name + " — " + Lang.T("уже иду");
            return name + (Fid(rune) > 0 ? "" : " — " + Lang.T("пункт портала выберешь сам один раз"));
        }

        internal static Sprite Icon(int rune) => Flasks.IconFor(rune);

        private static string Name(int rune) => Flasks.DisplayName(rune) ?? Lang.T("Руна портала");

        private static void Load()
        {
            string raw = Plugin.CfgPortalForwards != null ? Plugin.CfgPortalForwards.Value ?? "" : "";
            if (raw == _raw) return;
            _raw = raw;
            Learned.Clear();
            foreach (var piece in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = piece.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(piece.Substring(0, colon).Trim(), out int rune) || rune <= 0) continue;
                if (!int.TryParse(piece.Substring(colon + 1).Trim(), out int fid) || fid < 0) continue;
                Learned[rune] = fid;
            }
        }

        private static int Fid(int rune)
        {
            Load();
            if (Learned.TryGetValue(rune, out int fid)) return fid;
            return Seeded.TryGetValue(rune, out fid) ? fid : 0;
        }

        private static bool Taken(int fid)
        {
            Load();
            foreach (var pair in Seeded) if (pair.Value == fid) return true;
            foreach (var pair in Learned) if (pair.Value == fid) return true;
            return false;
        }

        private static void Remember(int rune, int fid, string why)
        {
            if (rune <= 0 || fid <= 0 || Fid(rune) > 0 || Taken(fid)) return;
            Learned[rune] = fid;
            var parts = new List<string>();
            foreach (var pair in Learned) parts.Add(pair.Key + ":" + pair.Value);
            _raw = string.Join(",", parts.ToArray());
            if (Plugin.CfgPortalForwards != null) Plugin.CfgPortalForwards.Value = _raw;
            Plugin.Log?.LogInfo("[teleport] rune " + rune + " is portal option " + fid + " (" + why + ")");
        }

        internal static void Heard(QuestDialogResponseMessage reply)
        {
            if (reply == null || reply.Forwards == null) return;
            var fids = new HashSet<int>();
            foreach (var f in reply.Forwards)
                if (f != null && f.ForwardId > 0) fids.Add(f.ForwardId);
            _offered = fids;
            _offeredAt = Portals.Here;
            _heardAt = RealTime.Now;
            Flasks.RequestScanNow();
        }

        internal static void Picked(int fid)
        {
            if (_asked <= 0 || RealTime.Now - _askedAt > 120f || Portals.Here <= 0) return;
            int rune = _asked;
            _asked = 0;
            Remember(rune, fid, "chosen by hand after the rune button");
        }

        private static float _warmAt;

        private static void Warm()
        {
            if (Time.unscaledTime < _warmAt) return;
            _warmAt = Time.unscaledTime + 2f;
            if (!SideButtons.InWorld()) return;
            foreach (int rune in Runes)
                if (Has(rune)) Flasks.Exact(rune);
        }

        internal static void Tick()
        {
            Watch();
            Warm();
            if (_offered == null || _heardAt <= 0f) return;
            if (Flasks.Scanning || Flasks.ScannedAt <= _heardAt) return;
            if (RealTime.Now - _heardAt > 30f) { _offered = null; return; }
            var fids = _offered;
            int portal = _offeredAt;
            _offered = null;
            Deduce(fids, portal);
        }

        private static void Deduce(HashSet<int> fids, int portal)
        {
            RunesAt.TryGetValue(portal, out var here);
            var spare = new List<int>();
            foreach (int fid in fids) if (!Taken(fid)) spare.Add(fid);
            var unknown = new List<int>();
            foreach (int rune in Runes)
                if (Fid(rune) <= 0 && Has(rune) && (here == null || Array.IndexOf(here, rune) < 0)) unknown.Add(rune);
            foreach (int rune in HomeRunes)
                if (Fid(rune) <= 0 && Has(rune) && (here == null || Array.IndexOf(here, rune) < 0)) unknown.Add(rune);
            Plugin.Trace("[teleport] portal " + portal + " offers " + string.Join(",", ToText(fids)) + "; unknown options " + spare.Count + ", runes without option " + unknown.Count);
            if (spare.Count == 1 && unknown.Count == 1) Remember(unknown[0], spare[0], "only unknown rune and option at portal " + portal);
        }

        private static string[] ToText(HashSet<int> set)
        {
            var list = new List<string>();
            foreach (int one in set) list.Add(one.ToString());
            return list.ToArray();
        }

        internal static void Click(int rune)
        {
            if (Plugin.Instance == null || Busy) return;
            if (SideButtons.InCombat()) return;
            if (SideButtons.ClaimLocked()) { Plugin.Trace("[teleport] not allowed from a fight claim"); return; }
            if (Artifacts.Busy) { Travel.Say(Lang.T("Сейчас идёт работа с хранилищем — телепорт подождёт.")); return; }
            if (TownWalk.Busy || Travel.Busy) { Travel.Say(Lang.T("Сейчас идёт другой поход — телепорт подождёт.")); return; }
            Plugin.Instance.StartCoroutine(Run(rune));
        }

        private static bool _ok;

        private static IEnumerator Run(int rune)
        {
            _running = rune;
            string name = Name(rune);
            try
            {
                if (!Travel.Connected()) { Travel.Say(Lang.T("Нет соединения — телепорт недоступен.")); yield break; }
                Plugin.Trace("[teleport] rune " + rune + " pressed at location " + Loc);
                if (RealTime.Now - Flasks.ScannedAt > LookGap)
                {
                    float asked = RealTime.Now;
                    Flasks.RequestScanNow();
                    yield return Wait(() => Flasks.ScannedAt >= asked, 8f);
                }
                if (!Has(rune)) { Travel.Say(Lang.F("«{0}»: этой руны в сумке уже нет.", name)); yield break; }
                if (Already(rune)) { Travel.Say(Lang.F("«{0}»: ты уже здесь.", name)); Plugin.Trace("[teleport] rune " + rune + " skipped, already at location " + Loc); yield break; }

                if (!RunesAt.ContainsKey(Loc))
                {
                    int city = City();
                    if (city <= 0)
                    {
                        Travel.Say(Lang.F("«{0}»: телепорт в город…", name));
                        int load = Travel.LoadStamp;
                        if (!Send(new ReturnToIlleniumRequest(true))) yield break;
                        yield return Wait(() => Travel.LoadStamp != load && Loc == Illenium, 90f);
                        if (!_ok) { Travel.Say(Lang.F("«{0}»: не дождался города.", name)); yield break; }
                        yield return Settle();
                        city = Illenium;
                    }
                    else if (Loc != city)
                    {
                        Travel.Say(Lang.F("«{0}»: иду в город…", name));
                        yield return Door(city);
                        if (!_ok) { Travel.Say(Lang.F("«{0}»: не дошёл до города.", name)); yield break; }
                    }

                    Travel.Say(Lang.F("«{0}»: иду к порталу…", name));
                    yield return Door(PortalOf[city]);
                    if (!_ok) { Travel.Say(Lang.F("«{0}»: не дошёл до портала.", name)); yield break; }
                }

                int portal = Loc;
                Travel.Say(Lang.F("«{0}»: открываю портал…", name));
                yield return Wait(() => Portals.Here == portal && Portals.Page != null && Portals.Mine(Portals.Page.NpcId) && !Portals.Going, 15f);
                if (!_ok) { Travel.Say(Lang.F("«{0}»: портал не ответил.", name)); yield break; }

                int fid = Fid(rune);
                if (fid <= 0)
                {
                    _asked = rune;
                    _askedAt = RealTime.Now;
                    Travel.Say(Lang.F("«{0}»: выбери нужный пункт портала — дальше кнопка будет переносить сама.", name));
                    yield break;
                }
                if (!Portals.Offers(fid))
                {
                    Travel.Say(Lang.F("«{0}»: портал не предлагает этот пункт — руна не подходит или её нет в сумке.", name));
                    Plugin.Log?.LogInfo("[teleport] portal " + portal + " has no option " + fid + " for rune " + rune);
                    yield break;
                }

                Travel.Say(Lang.F("«{0}»: переношусь…", name));
                int before = Travel.LoadStamp;
                if (!Portals.Choose(fid)) { Travel.Say(Lang.F("«{0}»: портал не принял выбор.", name)); yield break; }
                yield return Wait(() => Travel.LoadStamp != before && Loc != portal, 60f);
                if (!_ok) { Travel.Say(Lang.F("«{0}»: перенос не случился.", name)); yield break; }
                Travel.Say(Lang.F("«{0}»: на месте.", name));
                Landed[rune] = Loc;
                Plugin.Trace("[teleport] rune " + rune + " done, now at location " + Loc);
            }
            finally { _running = 0; }
        }

        private static int Loc
        {
            get { try { var ud = Controllers.User; return ud != null ? ud.CurrentLocationId : -1; } catch { return -1; } }
        }

        private static readonly Dictionary<int, int> Landed = new Dictionary<int, int>();

        private static int Home(int rune)
        {
            foreach (var pair in RunesAt)
            {
                if (Array.IndexOf(pair.Value, rune) < 0) continue;
                foreach (var city in PortalOf) if (city.Value == pair.Key) return city.Key;
            }
            return 0;
        }

        private static bool Already(int rune)
        {
            int home = Home(rune);
            if (home > 0 && (City() == home || Loc == PortalOf[home])) return true;
            return Landed.TryGetValue(rune, out int at) && at == Loc;
        }

        private static int City()
        {
            int loc = Loc;
            if (PortalOf.ContainsKey(loc)) return loc;
            var map = Travel.LastMap;
            if (map == null || map.MapId != loc || map.MapType != 0) return 0;
            int root = map.RootLocation;
            return PortalOf.ContainsKey(root) ? root : 0;
        }

        private static IEnumerator Door(int place)
        {
            int load = Travel.LoadStamp;
            Plugin.Trace("[teleport] → location " + place);
            if (!Send(new ChangeMapRequest(place))) { _ok = false; yield break; }
            yield return Wait(() => Travel.LoadStamp != load && Loc == place, 60f);
            if (_ok) yield return Settle();
        }

        private static IEnumerator Settle()
        {
            float t = RealTime.Now + 0.3f;
            while (RealTime.Now < t) yield return null;
            _ok = true;
        }

        private static IEnumerator Wait(Func<bool> done, float timeout)
        {
            _ok = false;
            float until = RealTime.Now + timeout;
            while (true)
            {
                if (done()) { _ok = true; yield break; }
                if (SideButtons.InCombat() || !Travel.Connected() || RealTime.Now >= until) yield break;
                yield return null;
            }
        }

        private static bool Send(BaseRequest request)
        {
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) { Travel.Say(Lang.T("Нет соединения.")); return false; }
                conn.SendRequest(request);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Fault("[teleport] send: " + e.Message);
                Travel.Say(Lang.F("Ошибка отправки: {0}", e.Message));
                return false;
            }
        }
    }
}
