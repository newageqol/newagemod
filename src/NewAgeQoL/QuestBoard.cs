using System;
using System.Collections.Generic;
using Transport.Messages.Responses.Quest.Faces;
using HarmonyLib;
using UnityEngine;

namespace NewAgeQoL
{
    internal static class QuestBoard
    {
        internal sealed class Card
        {
            internal int Id;
            internal int Status;
            internal int Level;
            internal int? NpcId;
            internal string Image;
            internal string Name;
            internal string Npc;
            internal string Desc;
            internal string Goal;
            internal string Avatar;
            internal int? Location;
            internal int? Cost;
            internal List<string> Counters;
            internal bool Known;
        }

        private sealed class Detail
        {
            internal string Name;
            internal string Npc;
            internal string Desc;
            internal string Goal;
            internal string Avatar;
            internal int? Location;
            internal int? Cost;
            internal List<string> Counters;
        }

        private static readonly Dictionary<int, Detail> Told = new Dictionary<int, Detail>();
        private static readonly Dictionary<int, float> Waiting = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> Sent = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> Tries = new Dictionary<int, int>();
        private static readonly HashSet<int> Fresh = new HashSet<int>();
        private static readonly List<Card> Cards = new List<Card>();
        private static readonly List<int> Stale = new List<int>();

        private static float _pollAt;
        private static float _askAt;
        private static float _sweptAt = -100f;
        private static float _missAt;
        private static float _findAt;
        private static float _eagerUntil;
        private static bool _sceneHooked;
        private static bool _fought;
        private static bool _arrived = true;
        private static object _facesSeen;
        private static bool _told;
        private static bool _dirty;
        private static string _sign = "";
        private static GameObject _strip;
        private static INetworkConnection _bound;
        private static int _asked;
        private static readonly MessageHandler Listener = OnDetail;
        private static readonly MessageHandler Talked = OnTalk;
        private static float _talkAt;

        internal static int Version { get; private set; }

        internal static List<Card> All => Cards;


        internal static bool Busy
        {
            get
            {
                foreach (var c in Cards) if (!Fresh.Contains(c.Id)) return true;
                return false;
            }
        }

        internal static void Tick()
        {
            try
            {
                if (!_sceneHooked)
                {
                    _sceneHooked = true;
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
                    {
                        _eagerUntil = Time.unscaledTime + 3f;
                        _findAt = 0f;
                    };
                }
                Listen();
                if (SideButtons.InCombat()) { _fought = true; return; }
                if (!SideButtons.InWorld()) { _arrived = true; return; }
                if (_fought)
                {
                    _fought = false;
                    Refetch();
                }
                if (_talkAt > 0f && Time.unscaledTime >= _talkAt && !CultPotions.Running)
                {
                    _talkAt = 0f;
                    Watched("после разговора");
                }
                Strip();
                if (Time.unscaledTime >= _pollAt)
                {
                    _pollAt = Time.unscaledTime + 0.4f;
                    Refresh();
                }
                if (_arrived && Cards.Count > 0)
                {
                    _arrived = false;
                    Watched("после входа");
                }
                Ask();
            }
            catch (Exception e) { Plugin.Trace("[задания] " + e.Message); }
        }

        internal static void Reask()
        {
            Fresh.Clear();
            Tries.Clear();
            Waiting.Clear();
            _askAt = 0f;
            _sweptAt = Time.unscaledTime;
            Version++;
        }

        internal static void ReaskIfStale()
        {
            if (Time.unscaledTime - _sweptAt > 30f) Refetch();
        }

        private static void Refetch()
        {
            foreach (var card in Cards)
            {
                if (card.Known && card.Status != (int)EQuestStatus.QUEST_ACTIVE) continue;
                Fresh.Remove(card.Id);
                Tries.Remove(card.Id);
            }
            _askAt = 0f;
            _sweptAt = Time.unscaledTime;
            Version++;
        }

        private static void OnTalk(object msg)
        {
            _talkAt = Time.unscaledTime + 1.5f;
        }

        private static void Watched(string why)
        {
            bool any = false;
            foreach (var card in Cards)
            {
                if (!QuestTrack.Tracked(card.Id)) continue;
                Fresh.Remove(card.Id);
                Tries.Remove(card.Id);
                any = true;
            }
            if (!any) return;
            _askAt = 0f;
            Version++;
            Plugin.Trace("[задания] " + why + " перечитываю отслеживаемые");
        }

        private static void Listen()
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || ReferenceEquals(nc, _bound)) return;
                _bound = nc;
                nc.AddAfterMessageListener(365, Listener);
                nc.AddAfterMessageListener(245, Talked);
                nc.AddAfterMessageListener(431, Talked);
                Plugin.Trace("[задания] слушаю ответы по заданиям");
            }
            catch (Exception e) { Plugin.Trace("[задания] подписка: " + e.Message); }
        }

        private static void OnDetail(object msg)
        {
            try
            {
                var detail = msg as NpcFaceDetailResponseMessage;
                if (detail == null) return;
                Keep(detail);
                if (detail.QuestId != _asked) return;
                _asked = 0;
                if (!Hooked()) Own(msg);
            }
            catch (Exception e) { Plugin.Trace("[задания] ответ: " + e.Message); }
        }

        private static bool Hooked()
        {
            try
            {
                var nc = NetworkConnection.Instance as NetworkConnection;
                if (nc == null) return true;
                var table = AccessTools.Field(typeof(NetworkConnection), "_eventTable")?.GetValue(nc) as System.Collections.IDictionary;
                if (table == null) return true;
                var handler = table[(short)365] as Delegate;
                if (handler == null) return false;
                foreach (var one in handler.GetInvocationList())
                    if (one.Target is QuestController) return true;
                return false;
            }
            catch { return true; }
        }

        private static void Own(object msg)
        {
            try
            {
                var ctrl = Controllers.Get<QuestController>();
                if (ctrl == null)
                {
                    Notice.Show("Здесь задание не открыть — вернись в обычную локацию", 5f);
                    return;
                }
                AccessTools.Method(typeof(QuestController), "OnDetailInfoResponse")?.Invoke(ctrl, new[] { msg });
                Plugin.Trace("[задания] окно задания открыто модом");
            }
            catch (Exception e) { Plugin.Warn("[задания] окно задания: " + e.Message); }
        }

        internal static int NewOnes()
        {
            int n = 0;
            foreach (var c in Cards) if (c.Status == (int)EQuestStatus.QUEST_NOT_FOUND) n++;
            return n;
        }

        internal static int Ready()
        {
            int n = 0;
            foreach (var c in Cards) if (c.Status == (int)EQuestStatus.QUEST_SUCCESS) n++;
            return n;
        }

        internal static Sprite Icon()
        {
            return Icons.Quest() ?? Icons.Scroll() ?? LeftColumn.MenuSprite("DailyQuestsButton");
        }

        private static Sprite _newMark, _goMark;

        internal static Sprite Mark(int status)
        {
            bool isNew = status == (int)EQuestStatus.QUEST_NOT_FOUND;
            if (!isNew && status != (int)EQuestStatus.QUEST_ACTIVE) return null;
            var have = isNew ? _newMark : _goMark;
            if (have != null && have.texture != null) return have;
            try
            {
                var got = AtlasUtils.GetQuestStatusIcon(isNew ? EQuestStatus.QUEST_NOT_FOUND : EQuestStatus.QUEST_ACTIVE);
                if (got == null || got.name == "unknown") return null;
                if (isNew) _newMark = got; else _goMark = got;
                return got;
            }
            catch (Exception e) { Plugin.Trace("[задания] значок статуса: " + e.Message); return null; }
        }

        internal static void Talk(int questId)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                Waiting.Remove(questId);
                Sent.Remove(questId);
                Wake(questId);
                _asked = questId;
                nc.SendRequest(new DetailInfoFaceRequest(questId));
                Plugin.Trace("[задания] открываю разговор по заданию " + questId);
            }
            catch (Exception e) { Plugin.Warn("[задания] разговор: " + e.Message); }
        }

        private static void Wake(int questId)
        {
            try
            {
                foreach (var face in Faces())
                    if (face != null && face.FaceMessage != null && face.FaceMessage.QuestId == questId) face.DisableBlink();
            }
            catch { }
        }

        private static List<NpcFaceMessageWrapper> Faces()
        {
            try
            {
                var ud = Controllers.User;
                var list = ud != null ? ud.NpcFaces : null;
                return list != null ? list.Content : null;
            }
            catch { return null; }
        }

        private static void Refresh()
        {
            var faces = Faces();
            if (faces == null) return;

            var sign = new System.Text.StringBuilder(";");
            foreach (var face in faces)
            {
                var msg = face != null ? face.FaceMessage : null;
                if (msg == null) continue;
                sign.Append(msg.QuestId).Append(':').Append(msg.Status).Append(';');
            }
            string now = sign.ToString();
            bool changed = now != _sign;
            if (changed)
            {
                foreach (var face in faces)
                {
                    var msg = face != null ? face.FaceMessage : null;
                    if (msg == null) continue;
                    if (_sign.IndexOf(";" + msg.QuestId + ":" + msg.Status + ";", StringComparison.Ordinal) < 0)
                    {
                        Fresh.Remove(msg.QuestId);
                        Tries.Remove(msg.QuestId);
                    }
                }
                _sign = now;
            }
            if (!changed && !_dirty && ReferenceEquals(faces, _facesSeen)) return;
            _facesSeen = faces;

            Cards.Clear();
            foreach (var face in faces)
            {
                var msg = face != null ? face.FaceMessage : null;
                if (msg == null) continue;
                var card = new Card
                {
                    Id = msg.QuestId,
                    Status = msg.Status,
                    Level = msg.Level,
                    NpcId = msg.NpcId,
                    Image = msg.Image,
                };
                Detail known;
                if (Told.TryGetValue(msg.QuestId, out known) && known != null)
                {
                    card.Known = true;
                    card.Name = known.Name;
                    card.Npc = known.Npc;
                    card.Desc = known.Desc;
                    card.Goal = known.Goal;
                    card.Avatar = known.Avatar;
                    card.Location = known.Location;
                    card.Cost = known.Cost;
                    card.Counters = known.Counters;
                }
                Cards.Add(card);
            }
            Cards.Sort(ByStatus);
            if (!changed && !_dirty) return;
            _dirty = false;
            Version++;
        }

        private static int ByStatus(Card a, Card b)
        {
            int c = Weight(a.Status).CompareTo(Weight(b.Status));
            if (c != 0) return c;
            c = a.Level.CompareTo(b.Level);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }

        private static int Weight(int status)
        {
            switch ((EQuestStatus)status)
            {
                case EQuestStatus.QUEST_SUCCESS: return 0;
                case EQuestStatus.QUEST_NOT_FOUND: return 1;
                case EQuestStatus.QUEST_ACTIVE: return 2;
            }
            return 3;
        }

        private static void Ask()
        {
            bool open = QuestWindow.Open;
            if (Time.unscaledTime < _askAt) return;

            if (Waiting.Count > 0)
            {
                Stale.Clear();
                foreach (var pair in Waiting)
                    if (Time.unscaledTime - pair.Value > 6f) Stale.Add(pair.Key);
                for (int i = 0; i < Stale.Count; i++) Waiting.Remove(Stale[i]);
                Stale.Clear();
            }
            if (Waiting.Count >= (open ? 10 : 2)) return;

            int want = open ? QuestWindow.Picked : 0;
            if (want > 0 && Send(want, open)) return;
            foreach (var card in Cards) if (QuestTrack.Tracked(card.Id) && Send(card.Id, open)) return;
            foreach (var card in Cards) if (Send(card.Id, open)) return;
        }

        private static bool Send(int questId, bool open)
        {
            if (Fresh.Contains(questId) || Waiting.ContainsKey(questId)) return false;
            int tries;
            if (Tries.TryGetValue(questId, out tries) && tries >= 3) { Fresh.Add(questId); return false; }
            Tries[questId] = tries + 1;
            Waiting[questId] = Time.unscaledTime;
            _askAt = Time.unscaledTime + (open ? 0.04f : 0.35f);
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) { Waiting.Remove(questId); return true; }
                nc.SendRequest(new DetailInfoFaceRequest(questId));
                Sent[questId] = Time.unscaledTime;
                Plugin.Trace("[задания] спрашиваю " + questId);
            }
            catch (Exception e)
            {
                Waiting.Remove(questId);
                Plugin.Trace("[задания] запрос " + questId + ": " + e.Message);
            }
            return true;
        }

        internal static bool Ours(NpcFaceDetailResponseMessage msg)
        {
            if (msg == null) return false;
            bool waited = Waiting.Remove(msg.QuestId);
            float at;
            bool sent = Sent.TryGetValue(msg.QuestId, out at) && Time.unscaledTime - at < 20f;
            if (sent) Sent.Remove(msg.QuestId);
            return waited || sent;
        }

        private static void Keep(NpcFaceDetailResponseMessage msg)
        {
            try
            {
                var detail = new Detail
                {
                    Name = string.IsNullOrEmpty(msg.QuestName) ? msg.Name : msg.QuestName,
                    Npc = msg.Name,
                    Desc = msg.Desc,
                    Goal = msg.Goal,
                    Avatar = msg.Avatar,
                    Location = msg.NpcLocation,
                    Cost = msg.TransferCost,
                };
                if (msg.Counters != null && msg.Counters.Count > 0)
                {
                    detail.Counters = new List<string>();
                    foreach (var c in msg.Counters)
                        if (c != null) detail.Counters.Add((c.Name ?? "цель") + ": " + c.Counter + " / " + c.Max);
                }
                Told[msg.QuestId] = detail;
                Fresh.Add(msg.QuestId);
                Waiting.Remove(msg.QuestId);
                Tries[msg.QuestId] = 0;
                _dirty = true;
                _pollAt = 0f;
                Plugin.Trace("[задания] " + msg.QuestId + " «" + detail.Name + "»");
            }
            catch (Exception e) { Plugin.Trace("[задания] разбор ответа: " + e.Message); }
        }

        private static void Strip()
        {
            try
            {
                if (_strip == null)
                {
                    float now = Time.unscaledTime;
                    if (now < _findAt) return;
                    _findAt = now + (now < _eagerUntil ? 0.15f : 1f);
                    var canvas = GameObject.Find("Canvas");
                    var found = canvas != null ? canvas.transform.Find("NpcFaces") : null;
                    if (found == null)
                    {
                        var grid = UnityEngine.Object.FindObjectOfType<NpcFaceWidgetManager>();
                        found = grid != null ? grid.transform.parent : null;
                    }
                    if (found == null)
                    {
                        if (Time.unscaledTime >= _missAt)
                        {
                            _missAt = Time.unscaledTime + 30f;
                            Plugin.Trace("[задания] колонка лиц не найдена");
                        }
                        return;
                    }
                    _strip = found.gameObject;
                }
                if (!_strip.activeSelf) return;
                _strip.SetActive(false);
                if (_told) return;
                _told = true;
                Plugin.Trace("[задания] лица справа убраны");
            }
            catch (Exception e) { Plugin.Trace("[задания] колонка лиц: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(QuestController), "OnDetailInfoResponse")]
    internal static class QuestBoardDetailPatch
    {
        private static bool Prefix(object msg)
        {
            try
            {
                var detail = msg as NpcFaceDetailResponseMessage;
                if (detail == null) return true;
                return !QuestBoard.Ours(detail);
            }
            catch (Exception e) { Plugin.Trace("[задания] перехват ответа: " + e.Message); return true; }
        }
    }
}
