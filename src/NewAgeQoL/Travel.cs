using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal sealed class Spot
    {
        internal string Name;
        internal int Area;
        internal int[] Path;

        internal int Target => Path[Path.Length - 1];
    }

    internal sealed class Gate
    {
        internal int From, To, Vertex;
    }

    internal static class Travel
    {
        internal const string Moved = "сменилась локация";

        internal static string Status = "";
        internal static float StatusAt;

        private static Spot _running, _wanted;
        private static bool _abort, _ok;
        private static string _why;

        internal static bool Busy => _running != null;
        internal static bool IsRunning(Spot spot)
        {
            var now = _running;
            if (now == null || spot == null) return false;
            return ReferenceEquals(now, spot) || (now.Area == spot.Area && SamePath(now, spot));
        }

        internal static bool Enabled => false;

        private static int Town => Plugin.CfgTravelTown != null ? Plugin.CfgTravelTown.Value : 2;
        private static int Outer => Plugin.CfgTravelOuter != null ? Plugin.CfgTravelOuter.Value : 1002;

        private static string _rawSpots;
        private static List<Spot> _spots = new List<Spot>();

        internal static List<Spot> Spots()
        {
            string raw = Plugin.CfgTravelSpots != null ? Plugin.CfgTravelSpots.Value : "";
            if (raw == _rawSpots) return _spots;
            _rawSpots = raw;
            _spots = ParseSpots(raw);
            return _spots;
        }

        private static List<Spot> ParseSpots(string raw)
        {
            var list = new List<Spot>();
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var piece in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = piece.LastIndexOf(':');
                if (colon <= 0) continue;

                string name = piece.Substring(0, colon).Trim();
                int area = 0;
                int at = name.LastIndexOf('@');
                if (at > 0 && int.TryParse(name.Substring(at + 1).Trim(), out int found))
                {
                    area = found;
                    name = name.Substring(0, at).Trim();
                }

                var path = new List<int>();
                foreach (var hop in piece.Substring(colon + 1).Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(hop.Trim(), out int vertex) && vertex >= 0) path.Add(vertex);

                if (name.Length == 0 || path.Count == 0) continue;
                list.Add(new Spot { Name = name, Area = area, Path = path.ToArray() });
            }
            return list;
        }

        private static readonly System.Reflection.FieldInfo PositionField = AccessTools.Field(typeof(GlobalMapController), "_currentPosition");

        internal static int Here()
        {
            try
            {
                var map = Gmc;
                if (map == null || !MapReady() || PositionField == null) return -1;
                return (int)PositionField.GetValue(map);
            }
            catch { return -1; }
        }

        internal static void AddHere()
        {
            int area = Area;
            int vertex = Here();
            if (area <= 0 || vertex < 0)
            {
                Notice.Show("Точку можно добавить только на карте мира — встань на нужную точку", 4f);
                return;
            }
            foreach (var spot in Spots())
                if (spot.Area == area && spot.Target == vertex)
                {
                    Notice.Show("Эта точка уже есть в списке: «" + spot.Name + "»", 4f);
                    return;
                }
            TravelEdit.Ask("Новая точка: участок " + area + ", точка v" + vertex + ".\nКак её назвать?", "", name =>
            {
                var list = new List<Spot>(Spots());
                list.Add(new Spot { Name = name, Area = area, Path = new[] { vertex } });
                Save(list);
                Say("Точка «" + name + "» добавлена в список похода.");
                Plugin.Trace("[travel] добавлена точка «" + name + "» " + area + ":" + vertex);
            });
        }

        internal static void Rename(Spot spot)
        {
            if (spot == null) return;
            TravelEdit.Ask("Новое название для «" + spot.Name + "»", spot.Name, name =>
            {
                var list = new List<Spot>(Spots());
                int at = list.IndexOf(spot);
                if (at < 0) return;
                list[at] = new Spot { Name = name, Area = spot.Area, Path = spot.Path };
                Save(list);
                Plugin.Trace("[travel] точка «" + spot.Name + "» переименована в «" + name + "»");
            });
        }

        internal static void Remove(Spot spot)
        {
            if (spot == null) return;
            TravelEdit.Confirm("Удалить точку «" + spot.Name + "» из списка похода?", "Удалить", () =>
            {
                var list = new List<Spot>(Spots());
                int at = list.FindIndex(one => ReferenceEquals(one, spot) || (one.Name == spot.Name && one.Area == spot.Area && SamePath(one, spot)));
                if (at < 0) return;
                if (IsRunning(list[at])) Cancel("точка удалена из списка");
                list.RemoveAt(at);
                Save(list);
                Notice.Show("Точка «" + spot.Name + "» убрана из списка", 3f);
                Plugin.Trace("[travel] удалена точка «" + spot.Name + "»");
            });
        }

        internal static void Move(Spot spot, int to)
        {
            var list = new List<Spot>(Spots());
            int from = list.IndexOf(spot);
            if (from < 0) return;
            to = Mathf.Clamp(to, 0, list.Count - 1);
            if (to == from) return;
            list.RemoveAt(from);
            list.Insert(to, spot);
            Save(list);
            Plugin.Trace("[travel] точка «" + spot.Name + "» перенесена на место " + (to + 1));
        }

        private static bool SamePath(Spot a, Spot b)
        {
            if (a.Path == null || b.Path == null || a.Path.Length != b.Path.Length) return false;
            for (int i = 0; i < a.Path.Length; i++)
                if (a.Path[i] != b.Path[i]) return false;
            return true;
        }

        private static void Save(List<Spot> list)
        {
            if (Plugin.CfgTravelSpots == null) return;
            var parts = new List<string>();
            foreach (var spot in list)
            {
                if (spot == null || spot.Path == null || spot.Path.Length == 0) continue;
                string name = spot.Name.Replace(",", " ").Replace(";", " ").Replace(":", " ").Replace("@", " ").Replace(">", " ").Trim();
                if (name.Length == 0) continue;
                var hops = new List<string>();
                foreach (int hop in spot.Path) hops.Add(hop.ToString());
                parts.Add(name + (spot.Area > 0 ? "@" + spot.Area : "") + ":" + string.Join(">", hops.ToArray()));
            }
            Plugin.CfgTravelSpots.Value = string.Join(", ", parts.ToArray());
        }

        private static string _rawGates;
        private static List<Gate> _gates = new List<Gate>();

        private static string SharedFile => System.IO.Path.Combine(System.IO.Path.Combine(BepInEx.Paths.CachePath, "NewAgeQoL"), "gates.txt");
        private static string _shared;

        private static string Shared()
        {
            if (_shared != null) return _shared;
            try { _shared = System.IO.File.Exists(SharedFile) ? System.IO.File.ReadAllText(SharedFile) : ""; }
            catch (Exception e) { Plugin.Trace("[travel] общие переходы: " + e.Message); _shared = ""; }
            return _shared;
        }

        private static List<Gate> Gates()
        {
            var cfg = Plugin.CfgTravelGates;
            string raw = (cfg != null ? cfg.Value + "," + cfg.DefaultValue : "") + "," + Shared();
            if (raw == _rawGates) return _gates;
            _rawGates = raw;
            _gates = ParseGates(raw);
            return _gates;
        }

        private static List<Gate> ParseGates(string raw)
        {
            var list = new List<Gate>();
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (var piece in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int arrow = piece.IndexOf('>');
                int colon = piece.LastIndexOf(':');
                if (arrow <= 0 || colon <= arrow) continue;
                if (!int.TryParse(piece.Substring(0, arrow).Trim(), out int from)) continue;
                if (!int.TryParse(piece.Substring(arrow + 1, colon - arrow - 1).Trim(), out int to)) continue;
                if (!int.TryParse(piece.Substring(colon + 1).Trim(), out int vertex)) continue;
                if (from <= 0 || to <= 0 || vertex < 0) continue;
                if (list.Exists(one => one.From == from && one.To == to)) continue;
                list.Add(new Gate { From = from, To = to, Vertex = vertex });
            }
            return list;
        }

        private static List<Gate> GateRoute(int from, int to)
        {
            if (from <= 0 || to <= 0) return null;
            if (from == to) return new List<Gate>();

            var seen = new HashSet<int> { from };
            var queue = new Queue<List<Gate>>();
            queue.Enqueue(new List<Gate>());
            while (queue.Count > 0)
            {
                var road = queue.Dequeue();
                int at = road.Count == 0 ? from : road[road.Count - 1].To;
                foreach (var gate in Gates())
                {
                    if (gate.From != at || seen.Contains(gate.To)) continue;
                    var next = new List<Gate>(road) { gate };
                    if (gate.To == to) return next;
                    seen.Add(gate.To);
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        internal static void Click(Spot spot)
        {
            if (spot == null || Plugin.Instance == null) return;
            if (Artifacts.Busy) { Say("Сейчас идёт работа с хранилищем — поход подождёт."); return; }
            if (TownWalk.Busy) { Say("Сейчас идёт возврат в город — поход подождёт."); return; }
            if (SideButtons.ClaimLocked()) { Plugin.Trace("[travel] из заявки на бой поход запрещён"); return; }
            _wanted = spot;
            Plugin.Instance.StartCoroutine(Switch(spot));
        }

        private static IEnumerator Switch(Spot spot)
        {
            if (Busy)
            {
                _abort = true;
                float t = 0f;
                while (_running != null && t < 5f) { yield return null; t += Time.unscaledDeltaTime; }
            }
            if (!ReferenceEquals(_wanted, spot)) yield break;
            yield return Run(spot);
        }

        private static IEnumerator Run(Spot spot)
        {
            _running = spot; _abort = false; AutoConfirm = false;
            Plugin.Trace("[travel] «" + spot.Name + "» запущен; " + Where());
            try
            {
                if (!Connected()) { Fail("нет соединения"); yield break; }

                if (spot.Area > 0)
                {
                    for (int guard = 0; guard < 8 && Area != spot.Area; guard++)
                    {
                        if (_abort) yield break;

                        var road = MapReady() ? GateRoute(Area, spot.Area) : null;
                        if (road == null || road.Count == 0)
                        {
                            if (MapReady() && Area == Outer)
                            {
                                Plugin.Trace("[travel] нет дороги с участка " + Area + " на " + spot.Area);
                                RouteLog.Note("нет дороги " + Area + ">" + spot.Area, "поход не знает дороги с участка " + Area + " на " + spot.Area + " («" + spot.Name + "»)");
                                Fail("не знаю дороги туда");
                                yield break;
                            }
                            int gate = MapReady() ? -1 : Exit(spot.Area);
                            if (gate > 0) yield return Leave(spot, gate);
                            else yield return GoTown(spot);
                            if (!_ok) yield break;
                            continue;
                        }

                        yield return Step(spot, road[0].Vertex, true);
                        if (!_ok) yield break;
                    }
                    if (Area != spot.Area) { Fail("не добрался до места"); yield break; }

                    yield return Step(spot, spot.Target, false);
                    if (!_ok) yield break;
                }
                else
                {
                    if (!HasVertex(spot.Path[0]))
                    {
                        yield return GoTown(spot);
                        if (!_ok) yield break;
                    }
                    for (int i = 0; i < spot.Path.Length; i++)
                    {
                        yield return Step(spot, spot.Path[i], i < spot.Path.Length - 1);
                        if (!_ok) yield break;
                    }
                }

                Say("«" + spot.Name + "»: на месте.");
                Plugin.Trace("[travel] «" + spot.Name + "» пройден; " + Where());
            }
            finally { _running = null; AutoConfirm = false; }
        }

        private static IEnumerator Step(Spot spot, int vertex, bool enter)
        {
            if (Locked(vertex))
            {
                Say("«" + spot.Name + "»: точка ещё не открыта.");
                Plugin.Trace("[travel] v" + vertex + " закрыта");
                RouteLog.Note("закрыта " + Area + ":" + vertex, "точка v" + vertex + " на участке " + Area + " закрыта");
                _ok = false;
                yield break;
            }

            Say("«" + spot.Name + "»: " + (enter ? "иду к переходу…" : "иду…"));
            Plugin.Trace("[travel] «" + spot.Name + "» → v" + vertex + (enter ? " (переход)" : ""));
            AutoConfirm = enter;
            int arrived = ArrivedStamp, refused = RefusedStamp, load = LoadStamp;
            if (!Send(new BeginMoveRequest(vertex))) { _ok = false; yield break; }

            yield return WaitFor(() => ArrivedStamp != arrived || RefusedStamp != refused, 30f,
                                 () => LoadStamp != load);
            if (!_ok) { Stop("сервер не ответил на ход"); yield break; }

            if (RefusedStamp != refused)
            {
                if (RefusedAt != vertex)
                {
                    _ok = false;
                    Plugin.Trace("[travel] сервер не ведёт к v" + vertex + ", стою на v" + RefusedAt);
                    if (Locked(vertex)) Say("«" + spot.Name + "»: точка ещё не открыта.");
                    else Stop(Blocked());
                    yield break;
                }
                if (enter && !Send(new GlobalMapActionRequest())) { _ok = false; yield break; }
            }

            if (!enter) { _ok = true; yield break; }

            Say("«" + spot.Name + "»: перехожу дальше…");
            yield return WaitFor(() => LoadStamp != load && MapReady(), 120f);
            if (!_ok) { Stop("переход не открылся"); yield break; }
            AutoConfirm = false;
            yield return Settle();
            _ok = true;
        }

        private static int Exit(int target)
        {
            try
            {
                var map = LastMap;
                if (map == null || map.MapType != 0 || map.SceneObjects == null) return -1;
                int best = -1, length = int.MaxValue;
                foreach (var one in map.SceneObjects)
                {
                    if (one == null || one.ObjectType != EObjectType.LeaveTown || one.Id <= 0) continue;
                    if (!Allowed(map.MapId, one.Id)) continue;
                    var road = GateRoute(one.Id, target);
                    if (road == null || road.Count >= length) continue;
                    best = one.Id;
                    length = road.Count;
                }
                if (best > 0) Plugin.Trace("[travel] из локации " + map.MapId + " есть свой выход на участок " + best + ", до цели переходов " + length);
                return best;
            }
            catch (Exception e) { Plugin.Trace("[travel] выходы локации: " + e.Message); return -1; }
        }

        private static bool Allowed(int place, int area)
        {
            int level = 0;
            try { var info = Ud?.UserInfo; level = info != null ? info.Level : 0; }
            catch { }
            if (place == 2 && area == 1000 && level > 14) return false;
            if (place == 2 && area == 1002 && level > 0 && level < 6) return false;
            return true;
        }

        private static IEnumerator Leave(Spot spot, int area)
        {
            Say("«" + spot.Name + "»: выхожу на участок " + area + "…");
            int leave = LoadStamp;
            if (!Send(new LeaveTownRequest(area))) { _ok = false; yield break; }
            yield return WaitFor(() => LoadStamp != leave && MapReady(), 90f);
            if (!_ok)
            {
                RouteLog.Note("не выпустили " + Loc + ">" + area, "из локации " + Loc + " не удалось выйти на участок " + area);
                Stop("не вышел на участок " + area);
                yield break;
            }
            yield return Settle();
            _ok = true;
        }

        private static IEnumerator GoTown(Spot spot)
        {
            if (!InTown())
            {
                Say("«" + spot.Name + "»: телепорт в город…");
                int load = LoadStamp;
                if (!Send(new ReturnToIlleniumRequest(true))) { _ok = false; yield break; }
                yield return WaitFor(() => LoadStamp != load && InTown(), 90f);
                if (!_ok) { Stop("не дождался города"); yield break; }
                yield return Settle();
            }

            Say("«" + spot.Name + "»: выхожу из города…");
            int leave = LoadStamp;
            if (!Send(new LeaveTownRequest(Outer))) { _ok = false; yield break; }
            yield return WaitFor(() => LoadStamp != leave && MapReady(), 90f);
            if (!_ok) { Stop("не дождался внешнего мира"); yield break; }
            yield return Settle();
            _ok = true;
        }

        private static IEnumerator Settle()
        {
            float t = 0f;
            while (t < 0.2f && !_abort) { yield return null; t += Time.unscaledDeltaTime; }
        }

        private static IEnumerator WaitFor(Func<bool> done, float timeout) => WaitFor(done, timeout, null);

        private static IEnumerator WaitFor(Func<bool> done, float timeout, Func<bool> giveUp)
        {
            _ok = false; _why = null;
            float t = 0f;
            while (true)
            {
                if (done()) { _ok = true; yield break; }
                if (giveUp != null && giveUp()) { _why = Moved; yield break; }
                if (_abort) { _why = "остановлен вручную"; yield break; }
                if (InCombat()) { _why = "начался бой"; yield break; }
                if (!Connected()) { _why = "нет соединения"; yield break; }
                yield return null;
                t += Time.unscaledDeltaTime;
                if (t >= timeout) { _why = "таймаут " + Mathf.RoundToInt(timeout) + "с"; yield break; }
            }
        }

        private static IUserData Ud
        {
            get { try { return Controllers.User; } catch { return null; } }
        }

        private static int Loc { get { var user = Ud; return user != null ? user.CurrentLocationId : -1; } }

        internal static bool Connected()
        {
            try { return NetworkConnection.Instance != null && NetworkConnection.Instance.IsConnected(); }
            catch { return false; }
        }

        internal static bool InCombat() => SideButtons.InCombat();

        internal static bool InTown()
        {
            try { return Loc == Town && SceneWorkFlow.IsCurrentScene(EUnityScene.StaticLocation); }
            catch { return false; }
        }

        private static GlobalMapController Gmc
        {
            get { try { return Controllers.Get<GlobalMapController>(); } catch { return null; } }
        }

        internal static bool MapReady()
        {
            var map = Gmc;
            return map != null && map.Vertices != null && map.Vertices.Length > 0;
        }

        private static bool HasVertex(int id) => Vertex(id) != null;

        private static GlobalMapVertex Vertex(int id)
        {
            var map = Gmc;
            var all = map != null ? map.Vertices : null;
            if (all == null) return null;
            foreach (var vertex in all)
                if (vertex != null && vertex.Id == id) return vertex;
            return null;
        }

        private static string Blocked()
        {
            var shut = Shut();
            if (shut.Count == 0) return "сервер не пускает дальше";
            var list = new System.Text.StringBuilder();
            for (int i = 0; i < shut.Count && i < 5; i++)
            {
                if (list.Length > 0) list.Append(", ");
                list.Append(shut[i]);
            }
            if (shut.Count > 5) list.Append(" и ещё ").Append(shut.Count - 5);
            return "дорога закрыта: по пути не открыты точки " + list;
        }

        private static List<int> Shut()
        {
            var list = new List<int>();
            try
            {
                var map = Gmc;
                var all = map != null ? map.Vertices : null;
                if (all == null) return list;
                foreach (var vertex in all)
                    if (vertex != null && vertex.State == EGlobalMapVertexState.Closed) list.Add(vertex.Id);
                list.Sort();
            }
            catch { }
            return list;
        }

        private static bool Locked(int id)
        {
            var vertex = Vertex(id);
            return vertex != null && vertex.State == EGlobalMapVertexState.Closed;
        }

        private static string Where()
        {
            var map = Gmc;
            return "участок=" + Area + " loc=" + Loc
                 + " карта=" + (map == null ? "нет" : "вершин " + (map.Vertices != null ? map.Vertices.Length : -1));
        }

        private static bool Send(BaseRequest request)
        {
            try
            {
                var conn = NetworkConnection.Instance;
                if (conn == null || !conn.IsConnected()) { Fail("нет соединения"); return false; }
                conn.SendRequest(request);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Fault("[travel] отправка: " + e.Message);
                Fail("ошибка отправки: " + e.Message);
                return false;
            }
        }

        internal static void Say(string text)
        {
            Status = text;
            StatusAt = Time.unscaledTime;
        }

        private static void Stop(string what)
        {
            if (_abort) return;
            if (_why == Moved)
            {
                Say("«" + (_running != null ? _running.Name : "поход") + "»: отменил — ты сменил локацию.");
                return;
            }
            Fail(what);
        }

        private static void Fail(string what)
        {
            string full = "«" + (_running != null ? _running.Name : "поход") + "» прерван: " + what
                        + (_why != null ? " (" + _why + ")" : "");
            Plugin.Warn("[travel] " + full + "; " + Where());
            Say(full);
        }

        internal static int ArrivedStamp, LoadStamp, RefusedStamp;
        internal static int RefusedAt = -1;
        internal static bool AutoConfirm;
        private static int _area;

        internal static int Area => MapReady() ? _area : 0;

        internal static LocationMap LastMap;

        private static int _mapType = -1;
        private static int _seenArea = -1;
        private static int _seenVertex = -1;
        private static float _watchAt;
        private static int _viaArea = -1;
        private static int _viaVertex = -1;
        private static int _viaLocation = -1;
        private static int _lastPlace = -1;

        internal static void Watch()
        {
            if (Time.unscaledTime < _watchAt) return;
            _watchAt = Time.unscaledTime + 0.25f;
            if (_mapType != 2 || !MapReady()) return;
            int vertex = Here();
            if (vertex < 0) return;
            _seenArea = _area;
            _seenVertex = vertex;
        }

        internal static void NotifyMap(LocationMap map)
        {
            if (map == null) return;
            LastMap = map;
            int area = map.RootLocation > 0 ? map.RootLocation : map.MapId;
            int type = map.MapType;
            if (type != 1)
            {
                if (type == 2)
                {
                    if (_mapType == 2 && _seenArea > 0 && area != _seenArea && _seenVertex >= 0)
                        Learn(_seenArea, area, _seenVertex);
                    else if (_mapType == 0 && _viaArea > 0 && area != _viaArea)
                        RouteLog.Note("через " + _viaArea + ":" + _viaVertex + ">" + _viaLocation + ">" + area,
                            "с участка " + _viaArea + " (точка v" + _viaVertex + ") через локацию " + _viaLocation + " попал на участок " + area);
                    if (_mapType == 0 && _lastPlace > 0)
                        RouteLog.Note("выход " + _lastPlace + ">" + area, "из локации " + _lastPlace + " вышел на участок " + area);
                    Plugin.Trace("[travel] загружена карта мира, участок " + area);
                    _viaArea = -1;
                }
                else
                {
                    if (_mapType == 2 && _seenArea > 0)
                    {
                        _viaArea = _seenArea;
                        _viaVertex = _seenVertex;
                        _viaLocation = map.MapId;
                        Plugin.Trace("[travel] с участка " + _seenArea + " с точки v" + _seenVertex + " зашёл в локацию " + map.MapId);
                    }
                    else
                    {
                        if (_mapType == 0 && _lastPlace > 0 && _lastPlace != map.MapId) _viaArea = -1;
                        Plugin.Trace("[travel] загружена локация " + map.MapId);
                    }
                }
                _mapType = type;
                _lastPlace = type == 0 ? map.MapId : -1;
            }
            _area = area;
        }

        private static void Learn(int from, int to, int vertex)
        {
            foreach (var gate in Gates())
                if (gate.From == from && gate.To == to) return;
            string line = from + ">" + to + ":" + vertex;
            RouteLog.Note("переход " + line, "нашёл переход с участка " + from + " на " + to + " через точку v" + vertex);
            try
            {
                string shared = Shared().Trim();
                _shared = (shared.Length > 0 ? shared + ", " : "") + line;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SharedFile));
                System.IO.File.WriteAllText(SharedFile, _shared);
            }
            catch (Exception e) { Plugin.Trace("[travel] общие переходы не записаны: " + e.Message); }
            Plugin.Trace("[travel] запомнил переход " + from + ">" + to + ":" + vertex);
        }

        internal static void NotifyRefused(int vertex) { RefusedAt = vertex; RefusedStamp++; }

        internal static void NotifyManualMove() => Cancel("идёшь по своему клику");

        internal static void Cancel(string why)
        {
            if (_running == null) return;
            string name = _running.Name;
            _abort = true;
            _wanted = null;
            Say("«" + name + "»: отменил — " + why + ".");
        }
    }

    [HarmonyPatch(typeof(LocationMapBuilder), "Build")]
    public static class TravelMapBuiltPatch
    {
        private static void Postfix(string locationXml, LocationMap __result)
        {
            Travel.NotifyMap(__result);
            MapDump.Save(locationXml, __result);
        }
    }

    [HarmonyPatch(typeof(GlobalMapController), "OnAction")]
    public static class TravelArrivedPatch
    {
        private static void Postfix() { Travel.ArrivedStamp++; }
    }

    [HarmonyPatch(typeof(GlobalMapController), "OnMoveTargetSelected")]
    public static class TravelManualMovePatch
    {
        private static void Prefix() { Travel.NotifyManualMove(); }
    }

    [HarmonyPatch(typeof(SceneLoader), "OnAssetsLoaded")]
    public static class TravelLoadedPatch
    {
        private static void Postfix() { Travel.LoadStamp++; }
    }

    [HarmonyPatch(typeof(GlobalMapController), "OnBeginMoveResponse")]
    public static class TravelMovePatch
    {
        private static void Prefix(object msg)
        {
            try
            {
                var steps = msg?.GetType().GetProperty("Transitions")?.GetValue(msg) as System.Collections.IEnumerable;
                if (steps == null) return;
                int count = 0, first = -1, last = -1;
                foreach (var step in steps)
                {
                    var ty = step.GetType();
                    int from = (int)ty.GetProperty("Source").GetValue(step);
                    int to = (int)ty.GetProperty("Target").GetValue(step);
                    if (first < 0) first = from;
                    last = to;
                    count++;
                }
                if (count == 1 && first == last) Travel.NotifyRefused(first);
            }
            catch (Exception e) { Plugin.Fault("[travel] ответ на ход: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(DialogFactory), "ShowConfirmMessageBox",
        new[] { typeof(string), typeof(Sprite), typeof(Action<EMessageBoxResult>), typeof(string), typeof(object[]) })]
    public static class TravelConfirmPatch
    {
        private const string ExitCaption = "messages.confirms.globalmap.exitcaption";
        private const string ExitMessage = "messages.confirms.globalmap.exit";

        private static bool Prefix(string caption, string message, Action<EMessageBoxResult> handler, ref ConfirmMessageBox __result)
        {
            if (!Travel.AutoConfirm) return true;
            if (caption != ExitCaption && message != ExitMessage) return true;

            Travel.AutoConfirm = false;
            __result = null;
            try { handler?.Invoke(EMessageBoxResult.MB_OK); }
            catch (Exception e) { Plugin.Fault("[travel] подтверждение перехода: " + e.Message); }
            return false;
        }
    }
}
