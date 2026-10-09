using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
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
        private const float Every = 1f;
        private const float RowWide = 300f;
        private const float Shrink = 0.68f;
        private const float ListEvery = 120f;

        private const string Server = "https://newage-observers.outerlab.org/v1/roster";
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

        private static readonly object Gate = new object();
        private static Answer _good;
        private static Answer _bad;
        private static bool _busy;
        private static bool _finished;

        private static RosterHover _hover;
        private static float _since;
        private static float _askAt;
        private static bool _fresh;
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
            _hover = hover;
            _since = Time.unscaledTime;
            _askAt = _since + Delay;
            _fresh = true;
            _paintAt = 0f;
        }

        internal static void Leave(RosterHover hover)
        {
            if (_hover != hover) return;
            _hover = null;
            Hide();
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
                if (_hover == null || !_hover.isActiveAndEnabled) { _hover = null; Hide(); return; }
                float now = Time.unscaledTime;
                bool busy;
                lock (Gate)
                {
                    if (_finished)
                    {
                        _finished = false;
                        if (!_fresh) _askAt = now + Every;
                    }
                    busy = _busy;
                }
                if (!busy && now >= _askAt && Reachable()) Ask();
                if (now - _since < Delay) return;
                if (now >= _paintAt || _tipGo == null)
                {
                    _paintAt = now + 0.25f;
                    if (!Fill()) return;
                }
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

        private static int Room() => _hover != null && _hover.Room > 0 ? _hover.Room : Chaotic.Room;

        private static bool Reachable() => Array.IndexOf(Rooms, Room()) >= 0;

        private static void Ask()
        {
            int id = _hover.Id;
            bool fight = _hover.Fight;
            int room = Room();
            _fresh = false;
            _askAt = float.MaxValue;
            if (Plugin.Instance == null) return;
            lock (Gate) _busy = true;
            Plugin.Instance.StartCoroutine(Fetch(id, fight, room));
        }

        private static IEnumerator Fetch(int id, bool fight, int room)
        {
            var req = UnityWebRequest.Get(Server + "?room=" + room + "&id=" + id + "&fight=" + (fight ? "1" : "0"));
            req.timeout = 10;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            long code = req.responseCode;
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            req.Dispose();
            try { Take(id, code, body); }
            catch (Exception e) { Done(id, null, e.Message); }
        }

        private static void Take(int id, long code, string body)
        {
            if (!body.StartsWith(Mark, StringComparison.Ordinal))
            {
                Done(id, null, code == 0 ? "сервер наблюдателей недоступен" : "сервер наблюдателей ответил " + code);
                return;
            }
            var people = new List<Fighter>();
            foreach (var raw in body.Split('\n'))
            {
                var bits = raw.TrimEnd('\r').Split('\t');
                if (bits.Length >= 2 && bits[0] == "!") { Done(id, null, bits[1]); return; }
                int uid, level;
                if (bits.Length < 4 || !int.TryParse(bits[0], out uid) || bits[1].Length == 0) continue;
                int.TryParse(bits[2], out level);
                people.Add(new Fighter { Id = uid, Login = bits[1], Level = level, Clan = bits[3] });
            }
            Done(id, people, null);
        }

        private static void Done(int id, List<Fighter> people, string error)
        {
            var answer = new Answer { Id = id, People = people, Error = error, At = DateTime.UtcNow };
            lock (Gate)
            {
                if (people != null) { _good = answer; _bad = null; }
                else _bad = answer;
                _busy = false;
                _finished = true;
            }
            if (error != null) Plugin.Trace("[roster] " + id + ": " + error);
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
            lock (Gate) { good = _good; bad = _bad; }
            if (good != null && good.Id != id) good = null;
            if (bad != null && bad.Id != id) bad = null;

            var people = good != null ? good.People : null;
            bool changed = Words(_title, (fight ? "Игроки боя" : "Состав заявки") + (people != null && people.Count > 0 ? "  <color=#acb3bd>" + people.Count + "</color>" : ""));
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
            if (place.y - h < box.yMin + 8f) place.y = Mathf.Min(box.yMax - 8f, box.yMin + 8f + h);
            if (tip.anchoredPosition != place) tip.anchoredPosition = place;
        }

        private static string Note(bool fight, Answer good, Answer bad)
        {
            if (!Reachable()) return "<color=#acb3bd>при наведении состав виден только в Иллениуме:\nхаотические, турнирные и командные бои</color>";
            if (good == null) return "<color=#acb3bd>" + (bad != null ? Plain(bad.Error) : "узнаю состав…") + "</color>";
            if (bad != null) return "<color=#f07a6e>" + Plain(bad.Error) + "</color>";
            if (good.People.Count == 0) return "<color=#acb3bd>" + (fight ? "сервер не назвал игроков" : "в заявке никого") + "</color>";
            long age = (long)(DateTime.UtcNow - good.At).TotalSeconds;
            return age >= 3 ? "<color=#747b85>обновлено " + age + " с назад</color>" : "";
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
            bool retry = shape == _drawn && listed == _listSeen;
            if (retry && !Stale()) return false;
            _drawn = shape;
            _listSeen = listed;
            _hidden = false;
            if (!retry) _retryUntil = Time.unscaledTime + RetryFor;
            _retryAt = Time.unscaledTime + RetryEvery;

            for (int i = _rows.childCount - 1; i >= 0; i--) { var old = _rows.GetChild(i); old.SetParent(null, false); UnityEngine.Object.Destroy(old.gameObject); }
            bool active = people != null && people.Count > 0;
            if (_rows.gameObject.activeSelf != active) _rows.gameObject.SetActive(active);
            if (!active) return true;

            var prefab = OnlineWindow.RowPrefab();
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
                if (prefab != null) Row(prefab, p);
                else Plainly(p);
            }
            if (strangers && Time.unscaledTime - _listAt > ListEvery && !OnlineList.Busy)
            {
                _listAt = Time.unscaledTime;
                OnlineList.Refresh();
            }
            return true;
        }

        private static void Row(GameObject prefab, OnlinePlayer p)
        {
            var holder = new GameObject("QoLRosterSlot", typeof(RectTransform), typeof(LayoutElement));
            holder.transform.SetParent(_rows, false);
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
            var w = go.GetComponent<UserRowWidget>();
            if (w != null)
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
                w.Data = data;
                OnlineWindow.Flatten(w, false);
            }
            var hush = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            hush.blocksRaycasts = false;
            hush.interactable = false;
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            foreach (var image in go.GetComponentsInChildren<Image>(true))
                if (image.enabled && Dead(image.sprite)) { image.enabled = false; _hidden = true; }
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
            _drawn = "";
            _listSeen = -1;
            _tipGo = new GameObject("QoLRosterTip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
            _tipGo.transform.SetParent(area, false);
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

            var list = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(_tipGo.transform, false);
            var lines = list.GetComponent<VerticalLayoutGroup>();
            lines.spacing = 0f;
            lines.childControlWidth = true;
            lines.childControlHeight = true;
            lines.childForceExpandWidth = true;
            lines.childForceExpandHeight = false;
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

        private void OnDisable() => ClaimRoster.Leave(this);
    }
}
