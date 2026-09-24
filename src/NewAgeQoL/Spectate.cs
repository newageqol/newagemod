using System;
using System.Collections.Generic;
using Transport.Messages.Responses.Combat;
using Transport.Messages.Responses.Locations.Arena;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Spectate
    {
        private const float Keep = 15f * 60f;
        private const float Every = 20f;
        private const float Answer = 4f;
        private const float Quiet = 1f;
        private const int QuietFrames = 30;
        private const float GiveUp = 20f;
        private static readonly Color Cross = new Color(0.62f, 0.09f, 0.09f, 0.8f);

        private sealed class Fight
        {
            internal int Id;
            internal string Desc;
            internal int MinLevel, MaxLevel, Count;
            internal int Kind, Max;
            internal int Room;
            internal float Seen;
        }

        private sealed class Slot
        {
            internal GameObject Row;
            internal RectTransform Mark;
            internal CanvasGroup Veil;
        }

        private static readonly Dictionary<int, Fight> Live = new Dictionary<int, Fight>();
        private static readonly Dictionary<int, Slot> Rows = new Dictionary<int, Slot>();
        private static readonly Dictionary<int, Fight> Origins = new Dictionary<int, Fight>();
        private static readonly HashSet<int> HeardFights = new HashSet<int>();
        private static readonly HashSet<int> HeardClaims = new HashSet<int>();
        private static readonly Dictionary<int, int> Missed = new Dictionary<int, int>();
        private static object _on;
        private static float _listenAt, _viewAt;
        private static int _children;
        private static BaseEnterfightView _view;
        private static int _watched;
        private static int _backTo;
        private static float _leaveAt;
        private static float _watchedAt;
        private static bool _inFight;
        private static int _map = -1;
        private static float _askAt, _askUntil, _heardAt;
        private static int _heardFrame;
        private static bool _asking, _answered;


        internal static void Tick()
        {
            try
            {
                Mine();
                if (Time.unscaledTime >= _listenAt)
                {
                    _listenAt = Time.unscaledTime + 1f;
                    Listen();
                }
                if (Time.unscaledTime < _viewAt) return;
                _viewAt = Time.unscaledTime + 0.5f;
                Room();
                Watching();
                Leave();
                Ask();
                Prune();
                Show();
            }
            catch (Exception e) { Plugin.Trace("[бои] " + e.Message); }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(161, OnCombat);
            nc.AddMessageListener(161, OnCombat);
            nc.RemoveMessageListener(17, OnAnnounce);
            nc.AddMessageListener(17, OnAnnounce);
            nc.RemoveMessageListener(351, OnResult);
            nc.AddMessageListener(351, OnResult);
            _on = nc;
        }

        private static void OnCombat(object message)
        {
            var info = message as CombatInfoResponseMessage;
            if (info == null || info.Id <= 0) return;
            Room();
            bool over = string.IsNullOrEmpty(info.Desc) && !info.FighterCount.HasValue
                        && !info.MinLevel.HasValue && !info.MaxLevel.HasValue;
            if (over)
            {
                if (Live.Remove(info.Id)) Plugin.Trace("[бои] бой " + info.Id + " закончился, строка снята");
                Drop(info.Id);
                return;
            }
            if (_asking) { HeardFights.Add(info.Id); Heard(); }
            Fight fight;
            bool fresh = !Live.TryGetValue(info.Id, out fight);
            if (fresh)
            {
                fight = new Fight { Id = info.Id };
                Live[info.Id] = fight;
            }
            Fight origin;
            int kind, max;
            if (fight.Kind == 0 && Origins.TryGetValue(info.Id, out origin))
            {
                fight.Kind = origin.Kind;
                fight.Max = origin.Max;
            }
            else if (fight.Kind == 0 && Claims.Origin(info.Desc, out kind, out max))
            {
                fight.Kind = kind;
                fight.Max = max;
                Origins[info.Id] = new Fight { Id = info.Id, Kind = kind, Max = max, Seen = Time.unscaledTime };
            }
            fight.Desc = string.IsNullOrEmpty(info.Desc) ? "бой идёт" : info.Desc;
            fight.MinLevel = info.MinLevel ?? 0;
            fight.MaxLevel = info.MaxLevel ?? 0;
            fight.Count = info.FighterCount ?? 0;
            fight.Seen = Time.unscaledTime;
            fight.Room = Map();
            if (fresh) Plugin.Trace("[бои] идёт бой " + info.Id + " «" + fight.Desc + "»");
        }

        private static void OnAnnounce(object message)
        {
            var one = message as FightAnnounce;
            if (one == null || one.Id <= 0 || one.Type != 1 || !_asking) return;
            HeardClaims.Add(one.Id);
            Heard();
        }

        private static void Heard()
        {
            _answered = true;
            _heardAt = Time.unscaledTime;
            _heardFrame = Time.frameCount;
        }

        private static void Watching()
        {
            bool inFight = SideButtons.InCombat();
            if (inFight) { _inFight = true; return; }
            if (_inFight) { _inFight = false; _watched = 0; return; }
            if (_watched <= 0 || Time.unscaledTime - _watchedAt < 15f) return;
            int gone = _watched;
            _watched = 0;
            if (Live.Remove(gone)) Plugin.Trace("[бои] бой " + gone + " не открылся, строка снята");
            Drop(gone);
        }

        private static void OnResult(object message)
        {
            if (_watched <= 0) return;
            int id = _watched;
            _watched = 0;
            if (Live.Remove(id)) Plugin.Trace("[бои] бой " + id + " закончился при мне, строка снята");
            Drop(id);
            if (_backTo > 0) _leaveAt = Time.unscaledTime + 2.5f;
        }

        internal static int ClaimMax(int id)
        {
            Fight fight;
            return Live.TryGetValue(id, out fight) ? fight.Max : 0;
        }

        internal static bool Peeking
        {
            get
            {
                if (!SideButtons.InCombat()) return false;
                try
                {
                    var cd = FighterHint.Cd();
                    if (cd == null) return _backTo > 0;
                    if (cd.MyCharacter != null) return false;
                    if (_backTo > 0) return true;
                    return cd.Characters != null && cd.Characters.Count > 0;
                }
                catch { return false; }
            }
        }

        internal static bool Fallen
        {
            get
            {
                if (!SideButtons.InCombat()) return false;
                try
                {
                    var cd = FighterHint.Cd();
                    var me = cd != null ? cd.MyCharacter : null;
                    return me != null && me.Dead;
                }
                catch { return false; }
            }
        }

        internal static bool Rotted
        {
            get
            {
                if (!SideButtons.InCombat()) return false;
                try
                {
                    var cd = FighterHint.Cd();
                    var me = cd != null ? cd.MyCharacter : null;
                    if (me == null || !me.Dead) return false;
                    if (!me.Initialized) return true;
                    var live = me.Indicators;
                    return live != null && live.IsDecayed;
                }
                catch { return false; }
            }
        }

        private static void Mine()
        {
            if (_backTo <= 0 || !SideButtons.InCombat()) return;
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null || cd.MyCharacter == null) return;
                _backTo = 0;
                _leaveAt = 0f;
                Plugin.Trace("[бои] это мой бой, метка просмотра снята");
            }
            catch { }
        }

        internal static void Exit()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                if (SideButtons.InCombat())
                {
                    nc.SendRequest(new ChangeMapRequest(0));
                    Plugin.Trace("[бои] выход из просмотра боя");
                    _backTo = 0;
                    _leaveAt = 0f;
                    return;
                }
                if (_backTo <= 0) return;
                _leaveAt = Time.unscaledTime;
                Leave();
            }
            catch (Exception e) { Plugin.Warn("[бои] выход из просмотра: " + e.Message); }
        }

        private static void Leave()
        {
            if (_leaveAt <= 0f || Time.unscaledTime < _leaveAt) return;
            _leaveAt = 0f;
            if (_backTo <= 0 || !SideButtons.InCombat()) { _backTo = 0; return; }
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                nc.SendRequest(new ChangeMapRequest(_backTo));
                Plugin.Trace("[бои] бой досмотрен, возвращаюсь на карту " + _backTo);
            }
            catch (Exception e) { Plugin.Warn("[бои] выход из просмотра: " + e.Message); }
            _backTo = 0;
        }

        private static void Room()
        {
            int map = Map();
            if (map == _map) return;
            _map = map;
            int old = Live.Count;
            foreach (int id in new List<int>(Live.Keys)) Drop(id);
            Live.Clear();
            Missed.Clear();
            _asking = false;
            _askAt = Time.unscaledTime + Every;
            if (old > 0) Plugin.Trace("[бои] карта " + map + ": старые строки боёв сняты (" + old + "), список пришлёт сервер");
        }

        private static void Ask()
        {
            if (_asking)
            {
                if (Time.unscaledTime < _askUntil) return;
                if (_answered && (Time.unscaledTime - _heardAt < Quiet || Time.frameCount - _heardFrame < QuietFrames))
                {
                    if (Time.unscaledTime < _askUntil + GiveUp) return;
                    _asking = false;
                    Plugin.Trace("[бои] ответ на обновление списка не затих за " + GiveUp + " с, строки не трогаю");
                    return;
                }
                _asking = false;
                if (_answered) Settle();
                else Plugin.Trace("[бои] на обновление списка сервер ничего не прислал, строки не трогаю");
                return;
            }
            if (Time.unscaledTime < _askAt) return;
            _askAt = Time.unscaledTime + Every;
            if (SideButtons.InCombat()) return;
            var view = _view != null ? _view : UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
            if (!(view is ChaoticEnterfightView) && !(view is TeamEnterfightView)) return;
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) return;
            HeardFights.Clear();
            HeardClaims.Clear();
            _answered = false;
            _asking = true;
            _askUntil = Time.unscaledTime + Answer;
            nc.SendRequest(new MapLoadedRequest());
        }

        private static bool Twice(int id)
        {
            int count;
            Missed.TryGetValue(id, out count);
            count++;
            if (count < 2) { Missed[id] = count; return false; }
            Missed.Remove(id);
            return true;
        }

        private static void Settle()
        {
            foreach (int id in HeardFights) Missed.Remove(id);
            foreach (int id in HeardClaims) Missed.Remove(id);

            var fights = new List<int>();
            if (HeardFights.Count > 0)
                foreach (var pair in Live)
                    if (!HeardFights.Contains(pair.Key) && Twice(pair.Key)) fights.Add(pair.Key);
            foreach (int id in fights)
            {
                Live.Remove(id);
                Drop(id);
            }

            var claims = new List<int>();
            var view = _view != null ? _view : UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
            if (view != null && view.Widgets != null)
                foreach (var widget in view.Widgets)
                {
                    if (widget == null || HeardClaims.Contains(widget.Id) || claims.Contains(widget.Id)) continue;
                    if (Unity3DHelper.FindInChild(widget.gameObject, "QoLStarted") != null) continue;
                    if (Twice(widget.Id)) claims.Add(widget.Id);
                }
            foreach (int id in claims)
            {
                int gone = 0;
                while (gone < 20 && view.RemoveWidgetById(id)) gone++;
            }

            if (fights.Count > 0 || claims.Count > 0)
                Plugin.Trace("[бои] обновление списка: сервер прислал боёв " + HeardFights.Count + ", заявок " + HeardClaims.Count
                    + "; снято боёв " + fights.Count + " [" + string.Join(",", fights) + "], заявок " + claims.Count + " [" + string.Join(",", claims) + "]");
        }

        private static int Map()
        {
            try
            {
                var ud = Controllers.User;
                return ud == null ? -1 : ud.MapId;
            }
            catch { return -1; }
        }

        private static void Prune()
        {
            if (Origins.Count > 0)
            {
                var old = new List<int>();
                foreach (var pair in Origins)
                    if (Time.unscaledTime - pair.Value.Seen > 2f * Keep) old.Add(pair.Key);
                foreach (int id in old) Origins.Remove(id);
            }
            if (Live.Count == 0) return;
            var stale = new List<int>();
            foreach (var pair in Live)
                if (Time.unscaledTime - pair.Value.Seen > Keep) stale.Add(pair.Key);
            foreach (int id in stale)
            {
                Live.Remove(id);
                Drop(id);
                Plugin.Trace("[бои] бой " + id + " давно не подтверждался, строка снята");
            }
        }

        private static void Drop(int id)
        {
            Slot slot;
            if (!Rows.TryGetValue(id, out slot)) return;
            Rows.Remove(id);
            if (slot != null && slot.Row != null) UnityEngine.Object.Destroy(slot.Row);
        }

        private static void Show()
        {
            if (Live.Count == 0 && Rows.Count == 0) return;
            var view = _view;
            if (view == null)
            {
                view = UnityEngine.Object.FindObjectOfType<BaseEnterfightView>();
                if (view == null)
                {
                    if (Rows.Count > 0) Rows.Clear();
                    _view = null;
                    return;
                }
                _view = view;
                Rows.Clear();
                _children = 0;
            }
            int room = Map();
            bool added = false;
            foreach (var pair in Live)
            {
                Slot slot;
                bool here = pair.Value.Room == 0 || pair.Value.Room == room;
                if (!here) { if (Rows.TryGetValue(pair.Key, out slot)) Drop(pair.Key); continue; }
                if (Rows.TryGetValue(pair.Key, out slot) && slot != null && slot.Row != null) continue;
                var made = Make(view, pair.Value);
                if (made == null) continue;
                Rows[pair.Key] = made;
                added = true;
            }

            foreach (var slot in Rows.Values) Fit(slot);

            int children = 0;
            foreach (var slot in Rows.Values)
                if (slot != null && slot.Row != null && slot.Row.transform.parent != null)
                { children = slot.Row.transform.parent.childCount; break; }
            if (added || children != _children)
            {
                _children = children;
                foreach (var slot in Rows.Values)
                    if (slot != null && slot.Row != null) slot.Row.transform.SetAsLastSibling();
            }
        }

        private static Slot Make(BaseEnterfightView view, Fight fight)
        {
            var announce = new FightAnnounce
            {
                Id = fight.Id,
                Type = 1,
                MaxCount = fight.Count,
                LeaderLogin = fight.Desc,
                MinLevel = fight.MinLevel,
                MaxLevel = fight.MaxLevel,
                Timeout = 600000,
                RoundTimeout = 0,
                ClaimType = fight.Kind > 0 ? fight.Kind : 1,
            };
            var row = view.Add(announce);
            if (row == null) return null;

            var timer = row.GetComponent<CountdownTimer>();
            if (timer != null) timer.enabled = false;

            var slot = Mark(row);

            var button = Unity3DHelper.FindInChild(row, "EnterButton");
            var enter = button != null ? button.GetComponent<Button>() : null;
            if (enter != null)
            {
                int id = fight.Id;
                enter.onClick.RemoveAllListeners();
                enter.onClick.AddListener(() => Watch(id));
            }
            Plugin.Trace("[бои] строка идущего боя " + fight.Id + " добавлена в список заявок");
            return slot;
        }

        private static RectTransform Card(GameObject row)
        {
            RectTransform card = null;
            float best = 0f;
            foreach (var image in row.GetComponentsInChildren<Image>(true))
            {
                if (image == null || !image.enabled || !image.gameObject.activeInHierarchy) continue;
                var rt = image.rectTransform;
                float area = rt.rect.width * rt.rect.height;
                if (area <= best) continue;
                best = area;
                card = rt;
            }
            return card ?? (RectTransform)row.transform;
        }

        private static Slot Mark(GameObject row)
        {
            var markGo = new GameObject("QoLStarted", typeof(RectTransform), typeof(CanvasGroup), typeof(RectMask2D));
            markGo.transform.SetParent(Card(row), false);
            var mrt = (RectTransform)markGo.transform;
            mrt.anchorMin = Vector2.zero;
            mrt.anchorMax = Vector2.one;
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;
            var group = markGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            markGo.GetComponent<RectMask2D>().padding = new Vector4(1f, 1f, 1f, 1f);
            Bar(mrt);
            Bar(mrt);
            markGo.transform.SetAsLastSibling();
            group.alpha = 0f;
            var slot = new Slot { Row = row, Mark = mrt, Veil = group };
            Fit(slot);
            return slot;
        }

        private static void Bar(RectTransform host)
        {
            var go = new GameObject("bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var image = go.GetComponent<Image>();
            image.color = Cross;
            image.raycastTarget = false;
        }

        private static void Fit(Slot slot)
        {
            if (slot == null || slot.Row == null) return;
            var mark = slot.Mark;
            if (mark == null || mark.childCount < 2) return;
            var box = mark.parent as RectTransform;
            if (box == null) return;
            float width = box.rect.width, height = box.rect.height;
            var veil = slot.Veil;
            if (width < 4f || height < 4f)
            {
                if (veil != null) veil.alpha = 0f;
                return;
            }
            float length = Mathf.Sqrt(width * width + height * height);
            float angle = Mathf.Atan2(height, width) * Mathf.Rad2Deg;
            for (int i = 0; i < 2; i++)
            {
                var bar = mark.GetChild(i) as RectTransform;
                if (bar == null) continue;
                bar.sizeDelta = new Vector2(length, 11f);
                bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? angle : -angle);
            }
            if (veil != null && veil.alpha < 1f) veil.alpha = 1f;
        }

        private static void Watch(int id)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                _backTo = Map();
                nc.SendRequest(new ChangeMapRequest(id));
                _watched = id;
                _watchedAt = Time.unscaledTime;
                Plugin.Trace("[бои] иду смотреть бой " + id);
            }
            catch (Exception e) { Plugin.Warn("[бои] переход в бой: " + e.Message); }
        }
    }
}
