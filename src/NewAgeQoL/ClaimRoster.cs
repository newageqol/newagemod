using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Transport.Messages.Common.User;
using Transport.Messages.Responses.User.Info;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ClaimRoster
    {
        private const float Delay = 0.3f;
        private const float Grace = 0.3f;
        private const float RowWide = 300f;
        private const float Shrink = 0.68f;
        private const float ListEvery = 120f;
        private const float Gap = 8f;
        private const int Above = 29000;
        private const int Batch = 12;
        private static readonly List<OnlinePlayer> Waiting = new List<OnlinePlayer>();

        private const string AllClaims = "https://newage-observers.outerlab.org/v1/claims";
        private const float BoardEvery = 0.85f;
        private const float BoardForget = 120f;
        private const string Mark = "newage-observers 1";
        private static readonly int[] Rooms = { 14, 19, 334 };

        private sealed class Fighter
        {
            internal int Id;
            internal string Login;
            internal int Level;
            internal string Clan;
        }

        private sealed class Answer
        {
            internal int Id;
            internal List<Fighter> People;
            internal string Error;
            internal DateTime At;
        }

        private sealed class Board
        {
            internal Dictionary<int, List<Fighter>> Claims;
            internal Dictionary<int, List<Fighter>> Fights;
            internal DateTime At;
            internal string Error;
            internal bool Busy;
            internal float Next;
        }

        private static readonly Dictionary<int, Board> Boards = new Dictionary<int, Board>();
        private static readonly HashSet<RosterHover> Shown = new HashSet<RosterHover>();
        private static readonly HashSet<int> Polled = new HashSet<int>();

        private static RosterHover _hover;
        private static float _since;
        private static int _heldId;
        private static int _heldRoom;
        private static bool _heldFight;
        private static float _lostAt;
        private static float _paintAt;
        private static float _listAt = -ListEvery;

        private sealed class Card
        {
            internal int ClassId;
            internal string ClanIcon;
            internal int? ClanCode;
            internal int Rank;
            internal int Level;
        }

        private const float CardEvery = 0.12f;
        private const float CardAgain = 600f;
        private static readonly Dictionary<int, Card> Cards = new Dictionary<int, Card>();
        private static readonly Dictionary<int, float> CardAsked = new Dictionary<int, float>();
        private static readonly Queue<KeyValuePair<int, string>> CardWant = new Queue<KeyValuePair<int, string>>();
        private static float _cardAt;
        private static int _cards;
        private static object _on;

        private static GameObject _tipGo;
        private static Text _title;
        private static Text _note;
        private static Transform _rows;
        private static string _drawn = "";
        private static int _listSeen = -1;

        internal static void Enter(RosterHover hover)
        {
            if (hover == null) return;
            bool same = (_hover != null || _lostAt > 0f) && hover.Id == _heldId && hover.Room == _heldRoom && hover.Fight == _heldFight;
            _hover = hover;
            _lostAt = 0f;
            if (same) return;
            _heldId = hover.Id;
            _heldRoom = hover.Room;
            _heldFight = hover.Fight;
            _since = Time.unscaledTime;
            _paintAt = 0f;
        }

        internal static void Leave(RosterHover hover)
        {
            if (_hover != hover) return;
            _hover = null;
            _lostAt = 0f;
            Hide();
        }

        internal static void Appear(RosterHover hover)
        {
            if (hover != null) Shown.Add(hover);
        }

        internal static void Lost(RosterHover hover)
        {
            Shown.Remove(hover);
            if (_hover != hover) return;
            _hover = null;
            _lostAt = Time.unscaledTime;
        }

        private static void Hide()
        {
            if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
        }

        internal static void Tick()
        {
            try
            {
                AskCards();
                PollBoards();
                float now = Time.unscaledTime;
                if (_hover == null || !_hover.isActiveAndEnabled)
                {
                    _hover = null;
                    if (_lostAt > 0f && now - _lostAt < Grace) return;
                    _lostAt = 0f;
                    Hide();
                    return;
                }
                if (now - _since < Delay) return;
                if (now >= _paintAt || _tipGo == null)
                {
                    _paintAt = now + 0.25f;
                    if (!Fill()) return;
                }
                Grow();
                Place();
            }
            catch (Exception e) { Plugin.Trace("[roster] " + e.Message); _hover = null; Hide(); }
        }

        private static void AskCards()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; CardWant.Clear(); return; }
            if (!ReferenceEquals(_on, nc))
            {
                nc.RemoveMessageListener(433, OnCard);
                nc.AddMessageListener(433, OnCard);
                _on = nc;
            }
            if (CardWant.Count == 0 || Time.unscaledTime < _cardAt) return;
            var who = CardWant.Dequeue();
            _cardAt = Time.unscaledTime + CardEvery;
            nc.SendRequest(new UserInfoRequest(who.Key, who.Value));
        }

        private static void OnCard(object m)
        {
            var info = m as Unity3DUserInfoResponseMessage;
            if (info == null || !CardAsked.ContainsKey(info.UserId)) return;
            Cards[info.UserId] = new Card { ClassId = info.ClassId, ClanIcon = info.ClanIcon, ClanCode = info.ClanIconCode, Rank = info.Rank, Level = info.Level };
            _cards++;
        }

        private static void WantCards(List<Fighter> people)
        {
            float now = Time.unscaledTime;
            foreach (var f in people)
            {
                if (f.Id <= 0) continue;
                float at;
                if (CardAsked.TryGetValue(f.Id, out at) && (Cards.ContainsKey(f.Id) ? now - at < CardAgain : now - at < 30f)) continue;
                CardAsked[f.Id] = now;
                CardWant.Enqueue(new KeyValuePair<int, string>(f.Id, f.Login));
            }
        }

        private static int Room() => RoomOf(_hover);

        private static int RoomOf(RosterHover hover) => hover != null && hover.Room > 0 ? hover.Room : Chaotic.Room;

        private static bool Reachable() => Array.IndexOf(Rooms, Room()) >= 0;

        private static void PollBoards()
        {
            if (Shown.Count == 0 || Plugin.Instance == null) return;
            Polled.Clear();
            Shown.RemoveWhere(h => h == null);
            foreach (var hover in Shown)
            {
                if (!hover.isActiveAndEnabled) continue;
                int room = RoomOf(hover);
                if (Array.IndexOf(Rooms, room) >= 0) Polled.Add(room);
            }
            float now = Time.unscaledTime;
            foreach (int room in Polled)
            {
                Board board;
                if (!Boards.TryGetValue(room, out board)) Boards[room] = board = new Board();
                if (board.Busy || now < board.Next) continue;
                board.Busy = true;
                Plugin.Instance.StartCoroutine(FetchBoard(room, board));
            }
        }

        private static IEnumerator FetchBoard(int room, Board board)
        {
            var req = UnityWebRequest.Get(AllClaims + "?room=" + room);
            req.timeout = 10;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            long code = req.responseCode;
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            req.Dispose();
            try { TakeBoard(room, board, code, body); }
            catch (Exception e) { board.Error = e.Message; }
            board.Busy = false;
            board.Next = Time.unscaledTime + BoardEvery;
        }

        private static void TakeBoard(int room, Board board, long code, string body)
        {
            if (!body.StartsWith(Mark, StringComparison.Ordinal))
            {
                board.Error = code == 0 ? Lang.T("сервер наблюдателей недоступен") : Lang.F("сервер наблюдателей ответил {0}", code);
                Plugin.Trace("[roster] room " + room + ": " + board.Error);
                return;
            }
            var claims = new Dictionary<int, List<Fighter>>();
            var fights = new Dictionary<int, List<Fighter>>();
            List<Fighter> people = null;
            foreach (var raw in body.Split('\n'))
            {
                var bits = raw.TrimEnd('\r').Split('\t');
                if (bits.Length >= 2 && bits[0] == "!") { board.Error = bits[1]; return; }
                int uid, level;
                if (bits.Length >= 2 && (bits[0] == "c" || bits[0] == "f"))
                {
                    int cid;
                    people = int.TryParse(bits[1], out cid) ? (bits[0] == "c" ? claims : fights)[cid] = new List<Fighter>() : null;
                    continue;
                }
                if (people == null || bits.Length < 4 || !int.TryParse(bits[0], out uid) || bits[1].Length == 0) continue;
                int.TryParse(bits[2], out level);
                people.Add(new Fighter { Id = uid, Login = bits[1], Level = level, Clan = bits[3] });
            }
            board.Claims = claims;
            board.Fights = fights;
            board.At = DateTime.UtcNow;
            board.Error = null;
        }

        private static void FromBoard(int id, bool fight, out Answer good, out Answer bad)
        {
            good = null;
            bad = null;
            Board board;
            if (!Boards.TryGetValue(Room(), out board)) return;
            bool known = board.Claims != null && (DateTime.UtcNow - board.At).TotalSeconds < BoardForget;
            List<Fighter> people;
            if (known && (fight ? board.Fights : board.Claims).TryGetValue(id, out people)) good = new Answer { Id = id, People = people, At = board.At };
            else if (!known && board.Error != null) bad = new Answer { Id = id, Error = board.Error, At = DateTime.UtcNow };
        }

        private static Canvas _canvas;

        private static bool Fill()
        {
            var canvas = _hover.GetComponentInParent<Canvas>();
            if (canvas != null) canvas = canvas.rootCanvas;
            if (canvas == null) { Hide(); return false; }
            _canvas = canvas;
            var area = (RectTransform)canvas.transform;
            if (_tipGo == null || _tipGo.transform.parent != area) Build(area);

            int id = _hover.Id;
            bool fight = _hover.Fight;
            Answer good, bad;
            FromBoard(id, fight, out good, out bad);
            if (good != null && good.Id != id) good = null;
            if (bad != null && bad.Id != id) bad = null;

            var people = good != null ? good.People : null;
            bool changed = Words(_title, (fight ? Lang.T("Игроки боя") : Lang.T("Состав заявки")) + (people != null && people.Count > 0 ? "  <color=#acb3bd>" + people.Count + "</color>" : ""));
            changed |= Words(_note, Note(fight, good, bad));
            changed |= Rows(people);

            if (!_tipGo.activeSelf) { _tipGo.SetActive(true); changed = true; }
            if (_tipGo.transform.GetSiblingIndex() != _tipGo.transform.parent.childCount - 1) _tipGo.transform.SetAsLastSibling();
            if (changed) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_tipGo.transform);
            return true;
        }

        private static void Place()
        {
            if (_tipGo == null || !_tipGo.activeSelf || _canvas == null) return;
            var area = (RectTransform)_canvas.transform;
            var tip = (RectTransform)_tipGo.transform;
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            Vector2 mouse;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(area, Input.mousePosition, cam, out mouse)) return;
            var box = area.rect;
            float w = tip.rect.width, h = tip.rect.height;
            var place = new Vector2(mouse.x + 18f, mouse.y - 18f);
            if (place.x + w > box.xMax - 8f) place.x = mouse.x - 18f - w;
            place.x = Mathf.Clamp(place.x, box.xMin + 8f, Mathf.Max(box.xMin + 8f, box.xMax - 8f - w));
            if (place.y - h < box.yMin + Gap) place.y = Mathf.Min(box.yMax - Gap, box.yMin + Gap + h);
            if (tip.anchoredPosition != place) tip.anchoredPosition = place;
        }

        private static string Note(bool fight, Answer good, Answer bad)
        {
            if (!Reachable()) return "<color=#acb3bd>" + Lang.T("при наведении состав виден только в Иллениуме:\nхаотические, турнирные и командные бои") + "</color>";
            if (good == null) return "<color=#acb3bd>" + (bad != null ? Plain(bad.Error) : Lang.T("узнаю состав…")) + "</color>";
            if (bad != null) return "<color=#f07a6e>" + Plain(bad.Error) + "</color>";
            if (good.People.Count == 0) return "<color=#acb3bd>" + (fight ? Lang.T("сервер не назвал игроков") : Lang.T("в заявке никого")) + "</color>";
            long age = (long)(DateTime.UtcNow - good.At).TotalSeconds;
            return age >= 3 ?"<color=#747b85>" + Lang.F("обновлено {0} с назад", age) + "</color>" : "";
        }

        private static bool Words(Text label, string text)
        {
            bool changed = false;
            if (label.text != text) { label.text = text; changed = true; }
            bool show = text.Length > 0;
            if (label.gameObject.activeSelf != show) { label.gameObject.SetActive(show); changed = true; }
            return changed;
        }

        private static bool Rows(List<Fighter> people)
        {
            int listed = OnlineList.Version * 7919 + _cards;
            if (people != null) WantCards(people);
            var sb = new StringBuilder();
            if (people != null)
                foreach (var f in people) sb.Append(f.Id).Append(':').Append(f.Level).Append(',');
            string shape = sb.ToString();
            bool same = shape == _drawn;
            bool retry = same && listed == _listSeen;
            if (retry && !Stale()) return false;
            _drawn = shape;
            _listSeen = listed;
            if (!same) _retryUntil = Time.unscaledTime + RetryFor;
            _retryAt = Time.unscaledTime + RetryEvery;

            bool active = people != null && people.Count > 0;
            var prefab = active ? OnlineWindow.RowPrefab() : null;
            bool switched = active && prefab != null && _plain;
            if (!active || prefab == null || switched || !Unique(people)) Clear();
            _plain = active && prefab == null;
            if (switched) same = false;
            if (_rows.gameObject.activeSelf != active) _rows.gameObject.SetActive(active);
            if (!active) return true;

            var players = Players(people);
            if (prefab == null)
            {
                Columns(null, people.Count);
                Waiting.AddRange(players);
                return true;
            }

            bool moved = !same;
            if (!same) Columns(prefab, people.Count);
            Keep.Clear();
            foreach (var p in players) Keep.Add(p.Id);
            Ready.Clear();
            foreach (var pair in Slots) if (!Keep.Contains(pair.Key)) Ready.Add(pair.Key);
            foreach (int id in Ready)
            {
                var gone = Slots[id].Holder;
                Slots.Remove(id);
                WaitingSlots.Remove(id);
                if (gone != null) { gone.transform.SetParent(null, false); UnityEngine.Object.Destroy(gone); }
            }

            _hidden = false;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                Slot slot;
                if (!Slots.TryGetValue(p.Id, out slot) || slot.Holder == null)
                {
                    var holder = new GameObject("QoLRosterSlot", typeof(RectTransform), typeof(LayoutElement));
                    holder.transform.SetParent(_rows, false);
                    Slots[p.Id] = slot = new Slot { Holder = holder };
                    WaitingSlots[p.Id] = p;
                    moved = true;
                }
                else if (slot.Widget == null) WaitingSlots[p.Id] = p;
                else moved |= Refresh(slot, p);
                if (slot.Holder.transform.GetSiblingIndex() != i) { slot.Holder.transform.SetSiblingIndex(i); moved = true; }
                if (slot.Hidden) _hidden = true;
            }
            return moved;
        }

        private static readonly HashSet<int> Keep = new HashSet<int>();
        private static bool _plain;

        private static void Clear()
        {
            Waiting.Clear();
            WaitingSlots.Clear();
            Slots.Clear();
            _hidden = false;
            for (int i = _rows.childCount - 1; i >= 0; i--) { var old = _rows.GetChild(i); old.SetParent(null, false); UnityEngine.Object.Destroy(old.gameObject); }
        }

        private static bool Unique(List<Fighter> people)
        {
            Keep.Clear();
            foreach (var f in people) if (!Keep.Add(f.Id)) return false;
            return true;
        }

        private sealed class Slot
        {
            internal GameObject Holder;
            internal UserRowWidget Widget;
            internal string Sign;
            internal bool Hidden;
            internal readonly List<Image> Muted = new List<Image>();
        }

        private static readonly Dictionary<int, Slot> Slots = new Dictionary<int, Slot>();
        private static readonly Dictionary<int, OnlinePlayer> WaitingSlots = new Dictionary<int, OnlinePlayer>();

        private static List<OnlinePlayer> Players(List<Fighter> people)
        {
            var list = new List<OnlinePlayer>(people.Count);
            bool strangers = false;
            foreach (var f in people)
            {
                var known = f.Id > 0 ? OnlineList.ById(f.Id) : null;
                if (known == null) strangers = true;
                var p = new OnlinePlayer
                {
                    Id = f.Id,
                    Login = known != null && !string.IsNullOrEmpty(known.Login) ? known.Login : f.Login,
                    Level = f.Level > 0 ? f.Level : known != null ? known.Level : 0,
                    Class = known != null ? known.Class : "",
                    Clan = known != null && !string.IsNullOrEmpty(known.Clan) ? known.Clan : f.Clan,
                    Rights = known != null ? known.Rights : "",
                    Rank = known != null ? known.Rank : 0,
                    Vip = known != null && known.Vip,
                    Dealer = known != null && known.Dealer,
                };
                list.Add(p);
            }
            if (strangers && Time.unscaledTime - _listAt > ListEvery && !OnlineList.Busy)
            {
                _listAt = Time.unscaledTime;
                OnlineList.Refresh();
            }
            return list;
        }

        private static void Grow()
        {
            if ((Waiting.Count == 0 && WaitingSlots.Count == 0) || _tipGo == null || !_tipGo.activeSelf) return;
            var prefab = OnlineWindow.RowPrefab();
            int n = Mathf.Min(Batch, Waiting.Count);
            for (int i = 0; i < n; i++) Plainly(Waiting[i]);
            Waiting.RemoveRange(0, n);
            if (WaitingSlots.Count > 0 && prefab != null)
            {
                Ready.Clear();
                foreach (var pair in WaitingSlots)
                {
                    Slot slot;
                    if (Slots.TryGetValue(pair.Key, out slot) && slot.Holder != null) Row(prefab, slot, pair.Value);
                    Ready.Add(pair.Key);
                }
                foreach (int id in Ready) WaitingSlots.Remove(id);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_tipGo.transform);
        }

        private static readonly List<int> Ready = new List<int>();

        private static void Columns(GameObject prefab, int count)
        {
            var grid = _rows.GetComponent<GridLayoutGroup>();
            float h = 20f;
            if (prefab != null)
            {
                var rt = (RectTransform)prefab.transform;
                h = (rt.rect.height > 4f ? rt.rect.height : (rt.sizeDelta.y > 4f ? rt.sizeDelta.y : 34f)) * Shrink;
            }
            grid.cellSize = new Vector2(RowWide * Shrink, h);
            float high = _canvas != null ? ((RectTransform)_canvas.transform).rect.height : 1080f;
            int fit = Mathf.Max(5, Mathf.FloorToInt((high - 2f * Gap - 70f) / h));
            int columns = Mathf.CeilToInt(count / (float)fit);
            grid.constraintCount = Mathf.Max(1, Mathf.CeilToInt(count / (float)Mathf.Max(1, columns)));
        }

        private static void Row(GameObject prefab, Slot slot, OnlinePlayer p)
        {
            var holder = slot.Holder;
            var go = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
            Clones.StripHotkeys(go, prefab);
            go.name = "QoLRosterRow";
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            float h = rt.rect.height > 4f ? rt.rect.height : (rt.sizeDelta.y > 4f ? rt.sizeDelta.y : 34f);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(RowWide, h);
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = new Vector3(Shrink, Shrink, 1f);
            var le = holder.GetComponent<LayoutElement>();
            le.preferredHeight = h * Shrink;
            le.minHeight = h * Shrink;
            le.preferredWidth = RowWide * Shrink;
            var hush = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            hush.blocksRaycasts = false;
            hush.interactable = false;
            slot.Widget = go.GetComponent<UserRowWidget>();
            slot.Sign = null;
            if (slot.Widget != null) Paint(slot, p);
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            if (slot.Widget == null) Mute(slot);
        }

        private static bool Refresh(Slot slot, OnlinePlayer p)
        {
            var data = Data(p);
            string sign = Sign(data);
            bool retry = slot.Hidden && Time.unscaledTime < _retryUntil;
            if (sign == slot.Sign && !retry) return false;
            Paint(slot, data, sign);
            return true;
        }

        private static void Paint(Slot slot, OnlinePlayer p)
        {
            var data = Data(p);
            Paint(slot, data, Sign(data));
        }

        private static void Paint(Slot slot, UserRowInfoMessage data, string sign)
        {
            slot.Sign = sign;
            foreach (var image in slot.Muted) if (image != null) image.enabled = true;
            slot.Muted.Clear();
            slot.Widget.Data = data;
            OnlineWindow.Flatten(slot.Widget, false);
            Mute(slot);
        }

        private static void Mute(Slot slot)
        {
            slot.Hidden = false;
            if (slot.Holder == null) return;
            foreach (var image in slot.Holder.GetComponentsInChildren<Image>(true))
                if (image.enabled && Dead(image.sprite)) { image.enabled = false; slot.Muted.Add(image); slot.Hidden = true; _hidden = true; }
        }

        private static UserRowInfoMessage Data(OnlinePlayer p)
        {
            var data = OnlineWindow.Row(p);
            Card card;
            if (Cards.TryGetValue(p.Id, out card))
            {
                if (card.ClassId > 0) data.ClassId = card.ClassId;
                if (!string.IsNullOrEmpty(card.ClanIcon) && !ClanPics.Missing(card.ClanIcon)) { data.ClanIcon = card.ClanIcon; data.ClanIconCode = null; }
                else if (card.ClanCode.HasValue && OnlineWindow.ClanArt(card.ClanCode.Value) != null) { data.ClanIconCode = card.ClanCode; data.ClanIcon = null; }
                if (card.Rank > 0) data.Rank = card.Rank;
                if (p.Level <= 0 && card.Level > 0) data.Level = card.Level;
            }
            return data;
        }

        private static string Sign(UserRowInfoMessage data)
        {
            var sb = new StringBuilder();
            foreach (var prop in typeof(UserRowInfoMessage).GetProperties())
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = prop.GetValue(data); } catch { v = null; }
                sb.Append(prop.Name).Append('=').Append(v).Append(';');
            }
            return sb.ToString();
        }

        private const float RetryFor = 15f;
        private const float RetryEvery = 1f;
        private static bool _hidden;
        private static float _retryUntil;
        private static float _retryAt;

        private static bool Dead(Sprite sprite) => Quickslots.Faded(sprite);

        private static bool Stale()
        {
            if (_rows == null) return false;
            float now = Time.unscaledTime;
            if (_hidden && now >= _retryAt && now < _retryUntil) return true;
            foreach (var image in _rows.GetComponentsInChildren<Image>())
            {
                if (image.enabled && Dead(image.sprite)) return true;
            }
            return false;
        }

        private static void Plainly(OnlinePlayer p)
        {
            var label = OnlineWindow.Label(_rows, Plain(p.Login) + (p.Level > 0 ? " <color=#acb3bd>(" + p.Level + ")</color>" : ""), 13, FontStyle.Normal, WardrobeLook.Body);
            label.supportRichText = true;
            label.raycastTarget = false;
        }

        private static string Plain(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        private static void Build(RectTransform area)
        {
            if (_tipGo != null) UnityEngine.Object.Destroy(_tipGo);
            Slots.Clear();
            WaitingSlots.Clear();
            Waiting.Clear();
            _drawn = "";
            _listSeen = -1;
            _tipGo = new GameObject("QoLRosterTip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            _tipGo.transform.SetParent(area, false);
            var lift = _tipGo.AddComponent<Canvas>();
            lift.overrideSorting = true;
            lift.sortingOrder = Above;
            var rt = (RectTransform)_tipGo.transform;
            rt.anchorMin = rt.anchorMax = area.pivot;
            rt.pivot = new Vector2(0f, 1f);
            var hush = _tipGo.GetComponent<CanvasGroup>();
            hush.blocksRaycasts = false;
            hush.interactable = false;
            var back = _tipGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var group = _tipGo.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(8, 8, 6, 8);
            group.spacing = 4f;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = Caption(13, FontStyle.Bold, WardrobeLook.Bright);

            var list = new GameObject("rows", typeof(RectTransform), typeof(GridLayoutGroup));
            list.transform.SetParent(_tipGo.transform, false);
            var lines = list.GetComponent<GridLayoutGroup>();
            lines.spacing = new Vector2(Gap, 0f);
            lines.startCorner = GridLayoutGroup.Corner.UpperLeft;
            lines.startAxis = GridLayoutGroup.Axis.Vertical;
            lines.childAlignment = TextAnchor.UpperLeft;
            lines.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            lines.constraintCount = 1;
            _rows = list.transform;

            _note = Caption(12, FontStyle.Normal, WardrobeLook.Label);
            _tipGo.SetActive(false);
        }

        private static Text Caption(int size, FontStyle style, Color tint)
        {
            var label = OnlineWindow.Label(_tipGo.transform, "", size, style, tint);
            label.alignment = TextAnchor.UpperLeft;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var size2 = label.gameObject.AddComponent<LayoutElement>();
            size2.preferredWidth = RowWide * Shrink;
            return label;
        }
    }

    internal sealed class RosterHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal int Id;
        internal bool Fight;
        internal int Room;

        public void OnPointerEnter(PointerEventData e) => ClaimRoster.Enter(this);

        public void OnPointerExit(PointerEventData e) => ClaimRoster.Leave(this);

        private void OnEnable() => ClaimRoster.Appear(this);

        private void OnDisable() => ClaimRoster.Lost(this);
    }
}
