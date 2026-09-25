using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using Transport.Messages.Common.User;
using Transport.Messages.Responses.Chat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class ChatDock
    {
        private const float Edge = 0f;
        private const float Gap = 8f;
        private const float Pad = 6f;
        private const float BarsWidth = 180f;
        private const float ListWidth = 208f;
        private const float TabsHeight = 20f;
        private const float InputHeight = 22f;
        private const float TabHigh = 12f;
        private static RectTransform _tabsRt;
        private static RectTransform _viewRt;
        private static float _tabsPad;
        private static bool _tabsDown;
        private const float RowGap = 4f;
        private const float Sink = 16f;

        private static readonly Color[] Paints =
        {
            new Color(0.72f, 0.16f, 0.13f, 1f),
            new Color(0.18f, 0.36f, 0.74f, 1f),
            new Color(0.78f, 0.62f, 0.14f, 1f),
            new Color(0.22f, 0.60f, 0.26f, 1f),
            new Color(0.58f, 0.38f, 0.80f, 1f),
        };

        private static readonly Text[] Values = new Text[5];
        private static readonly RectTransform[] Fills = new RectTransform[5];
        private static readonly Image[] Marks = new Image[5];
        private static GameObject _levelRow;
        private static RectTransform _levelBar;
        private static bool _levelTip;
        private static string _levelSaid;
        private const int BarFont = 11;
        private const int BarLeast = 6;
        private static readonly string[] Fitted = new string[5];
        private static readonly float[] FitRoom = new float[5];
        private const float ClockHigh = 13f;
        private const float KeySide = 24f;
        private const float KeyGap = 2f;
        private static Text _clock;
        private static Text _count;
        private static string _shown = "";
        private static bool _clockOk;

        private static readonly Button[] Sheets = new Button[2];
        private static readonly Text[] SheetLabels = new Text[2];

        internal static readonly ChatContentHolder Talk = new ChatContentHolder();
        internal static readonly ChatContentHolder Battle = new ChatContentHolder();
        private static readonly List<ChatResponseMessage> Line = new List<ChatResponseMessage>();
        private static readonly List<ChatResponseMessage> Tail = new List<ChatResponseMessage>();
        private static readonly List<ChatResponseMessage> Fresh = new List<ChatResponseMessage>();
        internal const int Keep = 1000;
        private const int Page = 150;
        private static int _window = Page;
        private static float _moreAt;
        private static readonly List<ITextScrollerContent> Range = new List<ITextScrollerContent>();
        private static readonly StringBuilder Pen = new StringBuilder();
        private static bool _redo;
        private static int _mapId = -1;
        private static int _mapType = -1;
        private static readonly List<ChatResponseMessage> World = new List<ChatResponseMessage>();
        private static bool _stashed;
        private static int _worldMap = -1;
        private static float _twinsUntil;
        private static readonly HashSet<string> Wiped = new HashSet<string>();
        private static float _flushAt;

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static ChatPanelContent _view;
        private static Transform _rowsHost;
        private static GameObject _rowPrefab;
        private static readonly ListWrapper<UserRowInfoMessage>[] Lists = new ListWrapper<UserRowInfoMessage>[3];
        private static readonly Action[] ListHooks = { () => ListChanged(0), () => ListChanged(1), () => ListChanged(2) };
        private static readonly string[] ListCaptions = { "в локации: ", "в клане: ", "в альянсе: " };
        private static readonly Button[] ListSheets = new Button[3];
        private static readonly Text[] ListLabels = new Text[3];
        private static int _list;
        private static float _listAt;
        private static int _listDue;
        private static object _listHeard;
        private static RectTransform _listTabsRt;
        private static RectTransform _boxRt;
        private static UserContextMenuResolver _resolver;
        private static InputField _input;
        private static GameObject _chip;
        private static Text _chipText;
        private static int _tab;
        private static int _to;
        private static string _toName = "";

        private static readonly Regex Paints2 = new Regex("</?color[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Names = new Regex(@"<link=(\d+)><u>( » )?([^<]*?)</u></link>", RegexOptions.Compiled);
        private static readonly Regex FirstName = new Regex("</u></link>", RegexOptions.Compiled);

        private static float _next;
        private static float _readyAt;
        private static float _bottomAt;
        private static float _tailUntil;
        private static string _scene = "";

        private sealed class Raise
        {
            internal RectTransform Rt;
            internal Vector2 Home;
            internal Vector3 Size;
            internal bool Up;
            internal float Look;
        }

        private static readonly string[] Paths = { "Canvas/CombatCommandPanel", "Canvas/BottomPanel" };
        private static readonly Dictionary<string, Raise> Raises = new Dictionary<string, Raise>();

        internal static bool Active => _root != null;

        internal static bool Typing => _input != null && _input.isFocused;

        internal static RectTransform Area => _canvasGo != null ? (RectTransform)_canvasGo.transform : null;

        private const float Want = 172f;

        internal static float PanelHeight => Fits(Want) + Edge;

        internal static float LeftPixels
        {
            get
            {
                if (_root == null || _canvasGo == null) return 0f;
                return (Screen.width - _root.sizeDelta.x * Scale(Sheet())) * 0.5f;
            }
        }

        private const float Wide = 960f;

        internal static float Span => _root != null ? _root.sizeDelta.x : Wide;

        internal static RectTransform Root => _root;

        internal static float RightEdge => _root != null
            ? _root.anchoredPosition.x + _root.sizeDelta.x * (1f - _root.pivot.x)
            : Span * 0.5f;

        internal static float PanelPixels
        {
            get
            {
                if (!Active || _canvasGo == null) return 0f;
                return PanelHeight * Scale(Sheet());
            }
        }

        internal static void Wake()
        {
            _next = 0f;
        }

        internal static void Tick()
        {
            try
            {
                Keys();
                bool deep = Time.unscaledTime >= _next;
                if (deep) _next = Time.unscaledTime + 0.1f;

                string scene = deep ? SceneManager.GetActiveScene().name : _scene;
                if (scene != _scene)
                {
                    _scene = scene;
                    Lift(0f, true);
                    Raises.Clear();
                    Homes.Clear();
                    _readyAt = Time.unscaledTime + 0.15f;
                    if (SideButtons.InCombat()) Restart();
                    if (_stashed || _mapType == 2)
                    {
                        Fresh.Clear();
                        _redo = true;
                        _flushAt = 0f;
                    }
                    else Moved(-1, -1);
                    if (_root != null) Rebind();
                }
                if ((_redo || Fresh.Count > 0) && Time.unscaledTime >= _flushAt) Render();

                if (!SideButtons.InWorld())
                {
                    if (!SideButtons.LoggedIn()) { _awayAt = 0f; Drop(); return; }
                    if (_awayAt <= 0f) _awayAt = Time.unscaledTime;
                    if (deep && Time.unscaledTime - _awayAt > 0.6f) Drop();
                    return;
                }
                _awayAt = 0f;
                if (_root == null)
                {
                    if (!deep || Time.unscaledTime < _readyAt) return;
                    _readyAt = Time.unscaledTime + 0.1f;
                    Build();
                    if (_root == null) return;
                }
                Place(deep);
                if (_listDue > 0 && Time.frameCount > _listDue) { _listDue = 0; Refill(); }
                if (deep) { Watch(); Freshen(); Aim(SideButtons.InCombat(), false); ListAim(false); Paint(); More(); }
                Bottom();
            }
            catch (Exception e) { Plugin.Fault("[док] " + e.Message); Drop(); }
        }

        private sealed class Posted
        {
            internal string Text;
            internal float At;
        }

        private static readonly List<Posted> Sent = new List<Posted>();
        private static readonly HashSet<ChatResponseMessage> Lost = new HashSet<ChatResponseMessage>();

        private static void Note(string text)
        {
            float now = Time.unscaledTime;
            for (int i = Sent.Count - 1; i >= 0; i--)
                if (now - Sent[i].At > 30f) Sent.RemoveAt(i);
            Sent.Add(new Posted { Text = text, At = now });
        }

        private static bool Gone(ChatResponseMessage message)
        {
            try
            {
                var kind = (EChatMessageType)message.Type;
                if (kind == EChatMessageType.MSG_PRIVATE || kind == EChatMessageType.MSG_OFFLINE) return false;
                string mine = ChatHighlight.Login();
                if (string.IsNullOrEmpty(mine) || !string.Equals(message.Sender, mine, StringComparison.Ordinal)) return false;

                string text = message.Text ?? "";
                int at = -1;
                for (int i = Sent.Count - 1; i >= 0; i--)
                    if (Sent[i].Text == text) { at = i; break; }
                if (at < 0) return false;
                Sent.RemoveAt(at);

                bool named = message.ReceiverId.HasValue && !string.IsNullOrEmpty(message.Receiver);
                Plugin.Trace("[док] моё сообщение вернулось: тип " + message.Type
                    + ", адресат " + (message.ReceiverId.HasValue ? message.ReceiverId.Value.ToString() : "нет")
                    + " «" + (message.Receiver ?? "") + "»");
                if (named) return false;
                Plugin.Trace("[док] адресата не видно — рисую пустые скобки");
                return true;
            }
            catch (Exception e) { Plugin.Trace("[док] адресат: " + e.Message); }
            return false;
        }

        internal static void Feed(ChatResponseMessage message)
        {
            try
            {
                if (message == null) return;
                if (GameSettings.Instance != null && GameSettings.Instance.IsIgnored(message.Sender)) return;
                if (Sent.Count > 0 && Gone(message)) Lost.Add(message);
                if (Time.unscaledTime < _twinsUntil && (EChatMessageType)message.Type != EChatMessageType.MSG_SYSTEM)
                {
                    if (Twin(message))
                    {
                        Plugin.Trace("[док] повтор сообщения после входа на карту от «" + (message.Sender ?? "?") + "» — пропускаю");
                        return;
                    }
                    if (Wiped.Contains(Key(message)))
                    {
                        Plugin.Trace("[док] сервер прислал стёртое кнопкой сообщение от «" + (message.Sender ?? "?") + "» — не показываю");
                        return;
                    }
                }
                var kind = (EChatMessageType)message.Type;
                if (kind == EChatMessageType.MSG_SYSTEM)
                {
                    Log(message);
                    if (_view != null && _tab == 1 && !ChatStay.Held) _bottomAt = Time.unscaledTime + 0.4f;
                    return;
                }
                if (Time.unscaledTime < _tailUntil) { (Loud(kind) || Personal(kind) ? Tail : Line).Add(message); _redo = true; }
                else
                {
                    if (Tail.Count > 0) { Flush(); _redo = true; }
                    Line.Add(message);
                    if (!_redo) Fresh.Add(message);
                }
                Trim();
                _flushAt = Time.unscaledTime + 0.05f;
                if (kind == EChatMessageType.MSG_TEAM && Plugin.CfgVerbose != null && Plugin.CfgVerbose.Value)
                    Plugin.Trace("[док] командное сообщение от «" + (message.Sender ?? "?") + "» принято, открыта вкладка "
                        + (_tab == 0 ? "«Чат»" : "«Системные»"));
                if (Loud(kind))
                {
                    if (_tab == 0)
                    {
                        _urgent = true;
                        _bottomAt = Time.unscaledTime + 0.6f;
                    }
                    else if (!_unread)
                    {
                        _unread = true;
                        TabMark();
                        Plugin.Trace("[док] вкладка «Чат» помечена: сообщение пришло, а открыты «Системные»");
                    }
                }
            }
            catch (Exception e) { Plugin.Trace("[док] сообщение: " + e.Message); }
        }

        internal static ChatContentHolder Board()
        {
            return _seek.Length > 0 ? Found : Battle;
        }

        private static readonly ChatContentHolder Found = new ChatContentHolder();
        private static readonly List<ChatResponseMessage> Sys = new List<ChatResponseMessage>();
        private static GameObject _seekGo;
        private static InputField _seekField;
        private static Button _seekButton;
        private static Text _seekLabel;
        private static string _seek = "";

        private static bool Hit(ChatResponseMessage message)
        {
            string text = message != null ? message.Text : null;
            if (string.IsNullOrEmpty(text)) return false;
            return Paints2.Replace(text, "").IndexOf(_seek, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Put(ChatContentHolder holder, ChatResponseMessage message)
        {
            var had = Last(holder);
            holder.AddMessage(message);
            var line = Last(holder);
            if (line != null && !ReferenceEquals(line, had))
                Repaint(line, Tint((EChatMessageType)message.Type), false, Lost.Contains(message));
        }

        private static void Sift()
        {
            Quiet(Found, () =>
            {
                Found.Clear();
                if (_seek.Length > 0)
                    foreach (var message in Sys)
                        if (Hit(message)) Put(Found, message);
            });
            if (_view != null && _tab == 1)
            {
                _window = Page;
                _view.SetChatContent(Board());
                _bottomAt = Time.unscaledTime + 0.3f;
            }
        }

        private static void Seeking(string text)
        {
            try
            {
                string clean = Flat(text ?? "").Trim();
                if (clean == _seek) return;
                _seek = clean;
                Sift();
            }
            catch (Exception e) { Plugin.Trace("[док] поиск: " + e.Message); }
        }

        private static void SeekOpen()
        {
            if (_seekGo == null) return;
            bool show = !_seekGo.activeSelf;
            _seekGo.SetActive(show);
            if (_seekField != null)
            {
                _seekField.text = "";
                if (show) _seekField.ActivateInputField();
            }
            SeekPaint();
            Plugin.Trace(show ? "[док] поиск по системным строкам открыт, строк для поиска " + Sys.Count
                              : "[док] поиск по системным строкам свёрнут");
        }

        private static void SeekPaint()
        {
            if (_seekLabel == null) return;
            bool open = _seekGo != null && _seekGo.activeSelf;
            _seekLabel.color = open ? WardrobeLook.OnAccent : WardrobeLook.Label;
            var back = _seekButton != null ? _seekButton.GetComponent<Image>() : null;
            if (back != null) back.color = open ? WardrobeLook.Accent : WardrobeLook.Tab;
        }

        private static void SeekShow(bool system)
        {
            if (_seekButton != null) _seekButton.gameObject.SetActive(system);
            if (system || _seekGo == null) { SeekPaint(); return; }
            _seekGo.SetActive(false);
            if (_seekField != null) _seekField.text = "";
            _seek = "";
            Found.Clear();
            SeekPaint();
        }

        private sealed class Logged
        {
            internal ChatResponseMessage Message;
            internal float At;
        }

        private static readonly List<Logged> Trail = new List<Logged>();

        private static void Log(ChatResponseMessage message)
        {
            Put(Battle, message);
            Sys.Add(message);
            while (Sys.Count > Keep) Sys.RemoveAt(0);
            if (_seek.Length > 0 && Hit(message)) Put(Found, message);
            float now = Time.unscaledTime;
            Trail.Add(new Logged { Message = message, At = now });
            int stale = 0;
            while (stale < Trail.Count && now - Trail[stale].At > 30f) stale++;
            if (stale > 0) Trail.RemoveRange(0, stale);
        }

        private static void Restart()
        {
            float now = Time.unscaledTime;
            var kept = new List<ChatResponseMessage>();
            foreach (var line in Trail)
                if (now - line.At <= 5f) kept.Add(line.Message);
            Trail.Clear();
            Battle.Clear();
            Sys.Clear();
            Found.Clear();
            foreach (var message in kept) Log(message);
            Plugin.Trace("[док] новый бой — прошлый боевой лог убран, начало нового оставлено: строк " + kept.Count);
        }

        internal static void Moved(int map, int type)
        {
            if (type == 1)
            {
                if (_mapType == 2 && !_stashed)
                {
                    _stashed = true;
                    _worldMap = _mapId;
                    World.Clear();
                    World.AddRange(Line);
                    World.AddRange(Tail);
                    Plugin.Trace("[док] бой с карты мира — чат карты " + _worldMap + " отложен до выхода: строк " + World.Count);
                }
                Carry();
                if (_stashed) _twinsUntil = Time.unscaledTime + 5f;
            }
            else if (_stashed && map > 0)
            {
                _stashed = false;
                if (type == 2 && map == _worldMap) Unstash();
                else
                {
                    Plugin.Trace("[док] после боя не та карта мира " + map + " (до боя " + _worldMap + ") — отложенный чат не нужен");
                    Wiped.Clear();
                    Carry();
                }
                World.Clear();
                _worldMap = -1;
            }
            else if (type == 2 && _mapType == 2 && map > 0 && map == _mapId)
            {
                _twinsUntil = Time.unscaledTime + 5f;
                Plugin.Trace("[док] снова карта мира " + map + " — общий чат оставляю");
            }
            else
            {
                Wiped.Clear();
                Carry();
            }
            if (map > 0 && type != 1)
            {
                _mapId = map;
                _mapType = type;
            }
            Fresh.Clear();
            _redo = true;
            _flushAt = 0f;
        }

        private static void Unstash()
        {
            var had = new HashSet<ChatResponseMessage>(World);
            var fresh = new List<ChatResponseMessage>();
            foreach (var message in Line)
                if (message != null && !had.Contains(message) && Kept((EChatMessageType)message.Type)) fresh.Add(message);
            foreach (var message in Tail)
                if (message != null && !had.Contains(message) && Kept((EChatMessageType)message.Type)) fresh.Add(message);
            Line.Clear();
            Tail.Clear();
            Line.AddRange(World);
            Tail.AddRange(fresh);
            Trim();
            _tailUntil = Time.unscaledTime + 2f;
            _twinsUntil = Time.unscaledTime + 5f;
            Plugin.Trace("[док] вернулся из боя на карту " + _mapId + " — чат карты вернул: строк " + World.Count
                + ", из боя оставил личных и прочих " + fresh.Count);
        }

        private static bool Twin(ChatResponseMessage message)
        {
            foreach (var one in Line) if (Same(one, message)) return true;
            foreach (var one in Tail) if (Same(one, message)) return true;
            foreach (var one in World) if (Same(one, message)) return true;
            return false;
        }

        private static string Key(ChatResponseMessage m)
        {
            return m.Type + "|" + m.SenderId + "|" + (m.ReceiverId.HasValue ? m.ReceiverId.Value.ToString() : "") + "|" + m.Time + "|" + m.Text;
        }

        private static void Forget(List<ChatResponseMessage> batch)
        {
            foreach (var message in batch)
                if (message != null && (EChatMessageType)message.Type != EChatMessageType.MSG_SYSTEM) Wiped.Add(Key(message));
        }

        private static bool Same(ChatResponseMessage a, ChatResponseMessage b)
        {
            return a != null && b != null && a.Type == b.Type && a.SenderId == b.SenderId
                && a.ReceiverId == b.ReceiverId && a.Time == b.Time && a.Text == b.Text;
        }

        private static void Render()
        {
            _flushAt = Time.unscaledTime + 0.05f;
            if (_redo)
            {
                Fresh.Clear();
                _redo = false;
                Rebuild();
                return;
            }
            try
            {
                var hook = AccessTools.Field(typeof(ChatContentHolder), "MessageAddedEvent");
                object saved = hook?.GetValue(Talk);
                if (hook != null) hook.SetValue(Talk, null);
                try { Pour(Fresh); }
                finally { if (hook != null) hook.SetValue(Talk, saved); }
                Fresh.Clear();
                if (_view != null && _tab == 0)
                {
                    AccessTools.Method(typeof(ChatPanelContent), "OnChatChanged")?.Invoke(_view, null);
                    if (!ChatStay.Held) _bottomAt = Time.unscaledTime + 0.4f;
                }
            }
            catch (Exception e) { Plugin.Trace("[док] дописать ленту: " + e.Message); Fresh.Clear(); _redo = true; }
        }

        private static void Rebuild()
        {
            try
            {
                var hook = AccessTools.Field(typeof(ChatContentHolder), "MessageAddedEvent");
                object saved = hook?.GetValue(Talk);
                if (hook != null) hook.SetValue(Talk, null);
                try
                {
                    Talk.Clear();
                    Pour(Line);
                    Pour(Tail);
                }
                finally { if (hook != null) hook.SetValue(Talk, saved); }

                if (_view != null && _tab == 0)
                {
                    AccessTools.Method(typeof(ChatPanelContent), "OnChatChanged")?.Invoke(_view, null);
                    if (!ChatStay.Held) _bottomAt = Time.unscaledTime + 0.4f;
                }
            }
            catch (Exception e) { Plugin.Trace("[док] пересборка ленты: " + e.Message); }
        }

        private static void Pour(List<ChatResponseMessage> batch)
        {
            foreach (var message in batch)
            {
                if (message == null) continue;
                var kind = (EChatMessageType)message.Type;
                var had = Last(Talk);
                Talk.AddMessage(message);
                var line = Last(Talk);
                if (line != null && !ReferenceEquals(line, had))
                    Repaint(line, Tint(kind),
                            kind == EChatMessageType.MSG_PRIVATE || kind == EChatMessageType.MSG_OFFLINE,
                            Lost.Contains(message));
            }
        }

        private static void Carry()
        {
            var kept = new List<ChatResponseMessage>();
            foreach (var message in Line)
                if (message != null && Kept((EChatMessageType)message.Type)) kept.Add(message);
            foreach (var message in Tail)
                if (message != null && Kept((EChatMessageType)message.Type)) kept.Add(message);
            Plugin.Trace("[док] смена локации: было строк " + (Line.Count + Tail.Count) + ", оставляю " + kept.Count
                + ", в бою " + Battle.Content.Count);
            Line.Clear();
            Tail.Clear();
            Tail.AddRange(kept);
            _tailUntil = Time.unscaledTime + 2f;
        }

        private static bool Kept(EChatMessageType kind)
        {
            return Personal(kind) || kind == EChatMessageType.MSG_TEAM
                || kind == EChatMessageType.MSG_ADMIN || kind == EChatMessageType.MSG_GLOBAL
                || kind == EChatMessageType.MSG_INFO;
        }

        private static void Flush()
        {
            if (Tail.Count == 0) return;
            Line.AddRange(Tail);
            Tail.Clear();
        }

        private static void Trim()
        {
            while (Line.Count + Tail.Count > Keep)
            {
                if (Line.Count > 0) { Lost.Remove(Line[0]); Line.RemoveAt(0); }
                else { Lost.Remove(Tail[0]); Tail.RemoveAt(0); }
            }
        }

        private static bool _urgent;
        private static bool _unread;

        private static void TabMark()
        {
            var label = SheetLabels[0];
            if (label == null) return;
            label.color = _tab == 0 ? WardrobeLook.OnAccent : _unread ? WardrobeLook.Accent : WardrobeLook.Label;
        }

        private static bool Loud(EChatMessageType kind)
        {
            return kind == EChatMessageType.MSG_TEAM || kind == EChatMessageType.MSG_PRIVATE
                || kind == EChatMessageType.MSG_OFFLINE;
        }

        private static bool Personal(EChatMessageType kind)
        {
            return kind == EChatMessageType.MSG_PRIVATE || kind == EChatMessageType.MSG_OFFLINE
                || kind == EChatMessageType.MSG_CLAN || kind == EChatMessageType.MSG_ALLIANCE
                || kind == EChatMessageType.MSG_TROOP;
        }

        private static string Tint(EChatMessageType kind) => ChatColors.Tint(kind);

        internal static void Recolor()
        {
            _redo = true;
            _flushAt = 0f;
        }

        internal static bool Roomy(ChatContentHolder holder)
        {
            return ReferenceEquals(holder, Talk) || ReferenceEquals(holder, Battle) || ReferenceEquals(holder, Found);
        }

        internal static IList<ITextScrollerContent> Shown(ChatContentHolder holder)
        {
            var all = holder.Content;
            if (!Roomy(holder) || all.Count <= _window) return all;
            Range.Clear();
            for (int i = all.Count - _window; i < all.Count; i++) Range.Add(all[i]);
            return Range;
        }

        internal static string Lines(ChatPanelContent panel)
        {
            var holder = ChatStay.Holder(panel);
            if (holder == null || !Roomy(holder)) return null;
            Pen.Length = 0;
            foreach (var item in Shown(holder))
            {
                if (item == null) continue;
                Pen.Append(item.GetText());
                Pen.Append(Environment.NewLine);
            }
            return Pen.ToString();
        }

        private static void More()
        {
            if (_view == null || Time.unscaledTime < _moreAt) return;
            var scroll = ChatStay.Of(_view);
            var holder = ChatStay.Holder(_view);
            if (scroll == null || holder == null || !Roomy(holder)) return;
            if (_window > Page && ChatStay.AtBottom(scroll))
            {
                _window = Page;
                Redraw();
                return;
            }
            int count = holder.Content.Count;
            if (count <= _window || !ChatStay.AtTop(scroll)) return;
            _moreAt = Time.unscaledTime + 0.3f;
            _window = Mathf.Min(count, _window + Page);
            Redraw();
            Plugin.Trace("[док] подгрузил строки выше: на экране " + _window + " из " + count);
        }

        private static void Redraw()
        {
            try { AccessTools.Method(typeof(ChatPanelContent), "OnChatChanged")?.Invoke(_view, null); }
            catch (Exception e) { Plugin.Trace("[док] перерисовка ленты: " + e.Message); }
        }

        private static void Quiet(ChatContentHolder holder, Action fill)
        {
            var hook = AccessTools.Field(typeof(ChatContentHolder), "MessageAddedEvent");
            object saved = hook?.GetValue(holder);
            if (hook != null) hook.SetValue(holder, null);
            try { fill(); }
            finally { if (hook != null) hook.SetValue(holder, saved); }
        }

        private static ITextScrollerContent Last(ChatContentHolder holder)
        {
            var lines = holder != null ? holder.Content : null;
            return lines != null && lines.Count > 0 ? lines[lines.Count - 1] : null;
        }

        private static readonly Dictionary<Type, System.Reflection.FieldInfo> TextFields =
            new Dictionary<Type, System.Reflection.FieldInfo>();

        private static System.Reflection.FieldInfo TextField(Type kind)
        {
            System.Reflection.FieldInfo got;
            if (TextFields.TryGetValue(kind, out got)) return got;
            got = AccessTools.Field(kind, "_text");
            TextFields[kind] = got;
            return got;
        }

        private static void Repaint(ITextScrollerContent item, string tint, bool secret, bool lost = false)
        {
            try
            {
                var field = TextField(item.GetType());
                if (field == null) return;
                string line = field.GetValue(item) as string;
                if (string.IsNullOrEmpty(line)) return;
                if (tint != null) line = Paints2.Replace(line, "");
                if (secret)
                    line = Names.Replace(line, m => ChatLinks.Has(m.Groups[1].Value) ? m.Value
                        : (m.Groups[2].Success ? " приватно " : "")
                        + "<link=" + m.Groups[1].Value + ">[<u>" + m.Groups[3].Value + "</u>]</link>");
                if (lost) line = FirstName.Replace(line, "</u></link> -> [] ", 1);
                if (tint != null) line = ChatLinks.Dress("<color=" + tint + ">" + line + "</color>");
                field.SetValue(item, line);
            }
            catch (Exception e) { Plugin.Trace("[док] цвет строки: " + e.Message); }
        }

        private static void Bottom()
        {
            if (_view == null || Time.unscaledTime > _bottomAt) { _urgent = false; return; }
            if (!_urgent && (ChatStay.Held || ChatStay.Touched)) { _bottomAt = 0f; return; }
            try
            {
                ChatStay.ToBottom(ChatStay.Of(_view));
                if (_urgent)
                {
                    _urgent = false;
                    ChatStay.Settle();
                    Plugin.Trace("[док] личное или командное сообщение — прокрутил чат к нему");
                }
            }
            catch (Exception e) { Plugin.Trace("[док] прокрутка: " + e.Message); }
        }

        internal static void Wipe()
        {
            try
            {
                WipeTalk();
                WipeBoard();
                if (_view != null) _view.SetChatContent(_tab == 1 ? Board() : Talk);
            }
            catch (Exception e) { Plugin.Trace("[док] очистка: " + e.Message); }
        }

        private static void WipeOpen()
        {
            try
            {
                if (_tab == 1) WipeBoard();
                else WipeTalk();
                if (_view != null) _view.SetChatContent(_tab == 1 ? Board() : Talk);
                Plugin.Trace("[док] очищена вкладка " + (_tab == 1 ? "«Системные»" : "«Чат»") + ", другая не тронута");
            }
            catch (Exception e) { Plugin.Trace("[док] очистка вкладки: " + e.Message); }
        }

        private static void WipeTalk()
        {
            Talk.Clear();
            if (Wiped.Count > 3000) Wiped.Clear();
            Forget(Line);
            Forget(Tail);
            Forget(World);
            Line.Clear();
            Tail.Clear();
            Fresh.Clear();
            Lost.Clear();
            World.Clear();
            _redo = false;
            _tailUntil = 0f;
            _window = Page;
        }

        private static void WipeBoard()
        {
            Battle.Clear();
            Sys.Clear();
            Found.Clear();
            Trail.Clear();
        }

        internal static void Recipient(UserContextMenuData data)
        {
            if (data == null) return;
            Recipient(data.Id, data.Login);
        }

        internal static void WriteTo(int userId, string login)
        {
            try
            {
                if (Active) { Recipient(userId, login); return; }
                var cw = Controllers.Get<ChatWindowController>();
                if (cw == null) return;
                if (!cw.IsWindowOpened) cw.Open(null);
                try { cw.ActiveTab = EChatTab.PRIVATE; }
                catch (Exception e) { Plugin.Trace("[док] вкладка лички: " + e.Message); }
                cw.SetMessageRecipient(new UserContextMenuData(userId, login));
            }
            catch (Exception e) { Plugin.Warn("[док] написать игроку: " + e.Message); }
        }

        internal static void Recipient(int id, string login)
        {
            _to = id;
            _toName = login ?? "";
            if (_tab != 0) Show(0);
            Chip();
            Focus();
        }

        private static int _snap;
        private static float _emptyAt;

        internal static void Focus()
        {
            if (_input == null) return;
            _input.ActivateInputField();
            _snap = 3;
            End();
        }

        internal static void Type(string text)
        {
            if (_input == null || string.IsNullOrEmpty(text)) return;
            _input.text = (_input.text ?? "") + text;
            Focus();
        }

        internal static bool Ours(InputField field)
        {
            return _input != null && ReferenceEquals(_input, field);
        }

        private static void End()
        {
            if (_input == null) return;
            int last = _input.text != null ? _input.text.Length : 0;
            if (_input.selectionAnchorPosition == last && _input.selectionFocusPosition == last) return;
            _input.caretPosition = last;
            _input.selectionAnchorPosition = last;
            _input.selectionFocusPosition = last;
        }

        internal static void TeamAim()
        {
            if (_input == null) return;
            if (Clean().Length == 0) { Focus(); return; }
            Send(EChatMessageType.MSG_TEAM);
        }

        private static void Chip()
        {
            bool set = _to > 0;
            if (_chip != null && _chip.activeSelf != set) _chip.SetActive(set);
            if (_chipText == null) { Inset(0f); return; }
            _chipText.text = set ? _toName + " :" : "";
            float wide = set ? _chipText.preferredWidth + 8f : 0f;
            var rt = (RectTransform)_chip.transform;
            rt.sizeDelta = new Vector2(wide, rt.sizeDelta.y);
            Inset(wide);
        }

        private static void Show(int tab)
        {
            _tab = tab;
            _window = Page;
            if (tab == 0) _unread = false;
            SeekShow(tab == 1);
            var holder = tab == 1 ? Board() : Talk;
            if (_view != null)
            {
                holder.CurrentScrollPosition = 0f;
                _view.SetChatContent(holder);
                _bottomAt = Time.unscaledTime + 0.4f;
            }
            for (int i = 0; i < SheetLabels.Length; i++)
            {
                if (SheetLabels[i] == null) continue;
                SheetLabels[i].color = i == tab ? WardrobeLook.OnAccent : WardrobeLook.Label;
                var back = Sheets[i] != null ? Sheets[i].GetComponent<Image>() : null;
                if (back != null) back.color = i == tab ? WardrobeLook.Accent : WardrobeLook.Tab;
            }
            TabMark();
        }

        private static bool _eat;
        private static float _probeAt = -1f;

        private static void Outside()
        {
            if (!Input.GetMouseButtonDown(0) || Time.unscaledTime - _menuAtTime < 0.3f) return;
            var menus = UnityEngine.Object.FindObjectsOfType<ContextMenu>();
            if (menus.Length == 0) return;
            foreach (var menu in menus)
            {
                var mrt = menu != null ? menu.transform as RectTransform : null;
                if (mrt == null) continue;
                var canvas = mrt.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (RectTransformUtility.RectangleContainsScreenPoint(mrt, Input.mousePosition, cam)) return;
            }
            CloseMenus();
        }

        internal static void CloseMenus()
        {
            try
            {
                foreach (var menu in UnityEngine.Object.FindObjectsOfType<ContextMenu>())
                {
                    if (menu == null) continue;
                    menu.gameObject.SetActive(false);
                    menu.Close();
                }
                var slot = AccessTools.Field(typeof(ContextMenu), "_currentInstance");
                if (slot != null) slot.SetValue(null, null);
            }
            catch (Exception e) { Plugin.Trace("[док] закрыть меню: " + e.Message); }
        }

        private static void Probe()
        {
            if (Plugin.CfgVerbose == null || !Plugin.CfgVerbose.Value) return;
            if (Input.GetMouseButtonDown(0))
            {
                var irt = (RectTransform)_input.transform;
                if (!RectTransformUtility.RectangleContainsScreenPoint(irt, Input.mousePosition, null)) return;
                _probeAt = Time.unscaledTime;
                var system = EventSystem.current;
                if (system == null) { Plugin.Trace("[док] клик по полю: нет EventSystem"); return; }
                var pointer = new PointerEventData(system) { position = Input.mousePosition };
                var hits = new List<RaycastResult>();
                system.RaycastAll(pointer, hits);
                var told = new StringBuilder();
                for (int i = 0; i < hits.Count && i < 4; i++)
                {
                    var go = hits[i].gameObject;
                    var canvas = go != null ? go.GetComponentInParent<Canvas>() : null;
                    told.Append(go != null ? go.name : "?").Append('@').Append(canvas != null ? canvas.rootCanvas.name + ":" + canvas.sortingOrder : "-").Append(' ');
                }
                Plugin.Trace("[док] клик по полю, под курсором: " + told);
                return;
            }
            if (_probeAt > 0f && Time.unscaledTime - _probeAt > 0.2f)
            {
                _probeAt = -1f;
                var system = EventSystem.current;
                var picked = system != null ? system.currentSelectedGameObject : null;
                Plugin.Trace("[док] после клика: фокус " + _input.isFocused + ", выбран " + (picked != null ? picked.name : "ничего")
                    + ", interactable " + _input.interactable);
            }
        }

        private static void Keys()
        {
            if (_input == null || !Active) return;
            Outside();
            Probe();
            if (_eat)
            {
                _eat = false;
                if (_input.text.Trim().Length == 0 && _input.text.Length > 0) _input.text = "";
            }
            if (_snap > 0) { _snap--; End(); }
            if (_input.text.Length > 0) _emptyAt = Time.unscaledTime;
            if (_to > 0 && _input.text.Length == 0 && Input.GetKey(KeyCode.Backspace)
                && (Input.GetKeyDown(KeyCode.Backspace) || Time.unscaledTime - _emptyAt > 0.25f))
            {
                _to = 0;
                _toName = "";
                _emptyAt = Time.unscaledTime;
                Chip();
                Plugin.Trace("[док] адресат снят клавишей");
            }
            if (_all)
            {
                _all = false;
                if (Clean().Length > 0) { Send(EChatMessageType.MSG_COMMON); return; }
            }
            if (_want)
            {
                _want = false;
                if (Clean().Length > 0) { Send(EChatMessageType.MSG_PRIVATE); return; }
            }
            bool shiftDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
            bool shiftUp = Input.GetKeyUp(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.RightShift);
            if (shiftDown)
            {
                string now = _input.text ?? "";
                int caret = _input.caretPosition;
                _armed = _input.isFocused && caret > 0 && caret <= now.Length && now[caret - 1] == ' ' && now.Trim().Length > 0;
                _armedAt = caret - 1;
            }
            else if (_armed && Input.anyKeyDown) _armed = false;
            if (shiftUp && _armed)
            {
                _armed = false;
                Plugin.Trace("[док] пробел и следом шифт");
                string now = _input.text ?? "";
                if (_armedAt >= 0 && _armedAt < now.Length - 1 && now[_armedAt] == ' ')
                    _input.text = now.Remove(_armedAt, 1);
                _want = true;
                return;
            }
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            if (Roster.Asking || CultPotions.Asking || TravelEnter.Asking) return;

            if (_input.isFocused)
            {
                if (_input.text.Trim().Length == 0) return;
                _all = true;
                return;
            }
            var picked = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (picked != null && picked.GetComponent<InputField>() != null) return;
            if (WalkKeys.Busy) return;
            Focus();
        }

        private static int? Addressee => _to > 0 ? _to : (int?)null;

        private static void Send(EChatMessageType kind)
        {
            try
            {
                string text = _input != null ? _input.text.Trim() : "";
                if (text.Length == 0) return;

                BaseRequest request;
                switch (kind)
                {
                    case EChatMessageType.MSG_PRIVATE:
                        if (_to <= 0) return;
                        request = new ChatRequest(_to, EChatMessageType.MSG_PRIVATE, text);
                        break;
                    case EChatMessageType.MSG_CLAN:
                        request = new ClanChatRequest(Addressee, false, text);
                        break;
                    case EChatMessageType.MSG_ALLIANCE:
                        request = new ClanChatRequest(Addressee, true, text);
                        break;
                    case EChatMessageType.MSG_TEAM:
                        request = new ChatRequest(Addressee, EChatMessageType.MSG_TEAM, text);
                        break;
                    default:
                        request = new ChatRequest(Addressee, EChatMessageType.MSG_COMMON, text);
                        break;
                }
                NetworkConnection.Instance.SendRequest(request);
                if (kind != EChatMessageType.MSG_PRIVATE && _to > 0) Note(text);
                _input.text = "";
                if (_to > 0) { _to = 0; _toName = ""; }
                Chip();
                Focus();
            }
            catch (Exception e) { Plugin.Warn("[док] отправка: " + e.Message); }
        }

        private static Canvas Sheet()
        {
            if (_canvas == null && _canvasGo != null) _canvas = _canvasGo.GetComponent<Canvas>();
            return _canvas;
        }

        private static void Place(bool deep)
        {
            float scale = Scale(Sheet());
            float high = Fits(Want);
            var area = _canvasGo != null ? _canvasGo.transform as RectTransform : null;
            float room = area != null && area.rect.width > 100f ? area.rect.width - Edge * 2f : Wide;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 0f);
            var size = new Vector2(Mathf.Min(Mathf.Max(Wide, CombatBar.Need), room), high + Sink);
            if (_root.sizeDelta != size) _root.sizeDelta = size;
            var spot = new Vector2(0f, Edge - Sink);
            if (_root.anchoredPosition != spot) _root.anchoredPosition = spot;
            Lift((high + Edge + CombatBar.Height + SpeedBar.Height) * scale, deep);

        }

        private static void Paint()
        {
            Ink();
            Clock();
            Count();
            Cap();
            Level();
            if (SideButtons.InCombat() && Fight()) return;
            var ind = Ind();
            if (ind == null) return;
            Value(0, ind.CurrentLife, ind.MaxLife);
            Value(1, ind.CurrentMana, ind.MaxMana);
            Value(2, ind.CurrentStamina, ind.MaxStamina);
            Value(3, ind.CurrentExpower, Remember(Mathf.Max(ind.MaxExpower, ind.CurrentExpower)));
        }

        private static int _seen;
        private static string _seenFor = "";

        private static int Remember(int now)
        {
            string who = "";
            try { var info = Controllers.User?.UserInfo; if (info != null) who = info.Login ?? info.UserId.ToString(); } catch { }
            if (who != _seenFor)
            {
                _seenFor = who;
                _seen = 0;
                foreach (var pair in (Plugin.CfgExpowerSeen != null ? Plugin.CfgExpowerSeen.Value : "").Split(';'))
                {
                    int at = pair.IndexOf('=');
                    if (at <= 0 || pair.Substring(0, at) != who) continue;
                    int.TryParse(pair.Substring(at + 1), out _seen);
                }
            }
            if (now > _seen)
            {
                _seen = now;
                if (Plugin.CfgExpowerSeen != null && who.Length > 0)
                {
                    var kept = new List<string>();
                    foreach (var pair in Plugin.CfgExpowerSeen.Value.Split(';'))
                        if (pair.Length > 0 && !pair.StartsWith(who + "=", StringComparison.Ordinal)) kept.Add(pair);
                    kept.Add(who + "=" + now);
                    Plugin.CfgExpowerSeen.Value = string.Join(";", kept.ToArray());
                }
            }
            return Mathf.Max(_seen, 20);
        }

        private static void Clock()
        {
            if (_clock == null) return;
            var at = Server();
            string now = at.HasValue ? at.Value.AddHours(3).ToString("HH:mm") : "--:--";
            if (now != _shown) Plugin.Trace("[док] часы: " + now + ", метка " + (_clock.gameObject.activeInHierarchy ? "видна" : "скрыта")
                + ", место " + _clock.rectTransform.anchoredPosition + " размер " + _clock.rectTransform.sizeDelta);
            if (at.HasValue && !_clockOk)
            {
                _clockOk = true;
                Plugin.Log?.LogInfo("[док] время сервера получено: " + at.Value.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            }
            if (now == _shown) return;
            _shown = now;
            _clock.text = now;
        }

        private static string _said = "";

        private static void Once(string why)
        {
            if (why == _said) return;
            _said = why;
            Plugin.Trace("[док] время сервера: " + why);
        }

        private static DateTime? Server()
        {
            try
            {
                var time = DependencyContainer.GetContainer()?.Resolve<GameTime>();
                if (time == null) { Once("часы игры не найдены"); return null; }
                if (time.GetDifference() == 0L) { Once("сдвиг от сервера ещё не получен"); return null; }
                return time.CurrentServerUTCTime;
            }
            catch (Exception e) { Plugin.Trace("[док] время сервера: " + e.Message); return null; }
        }

        internal static float CoverPixels
        {
            get
            {
                if (_canvasGo == null || !Active) return 0f;
                return (PanelHeight + CombatBar.Height) * Scale(Sheet());
            }
        }

        private static bool _fallen;

        private static bool Fight()
        {
            try
            {
                var cd = FighterHint.Cd();
                var me = cd != null ? cd.MyCharacter : null;
                var ind = me != null ? me.Indicators : null;
                if (ind == null) return false;
                var later = Outcome.Of(cd, me.UserId);
                bool fallen = ind.IsDead || cd.GetCharacter(me.UserId) == null;
                if (fallen != _fallen)
                {
                    _fallen = fallen;
                    if (fallen) Plugin.Trace("[док] свой боец мёртв или убран с поля, жизнь " + ind.CurrentLife + " не в счёт");
                }
                int life = ind.CurrentLife + later.Life;
                if (fallen && life > 0) life = 0;
                Value(0, life, ind.MaxLife);
                Value(1, ind.CurrentMana + later.Mana, ind.MaxMana);
                Value(2, ind.CurrentStamina + later.Stamina, ind.MaxStamina);
                var whole = Ind();
                int top = whole != null ? whole.MaxExpower : 0;
                if (top <= 0) top = ind.MaxExpower;
                if (top <= 0) top = 20;
                int expower = ind.CurrentExpower + later.Expower;
                Value(3, expower, Remember(Mathf.Max(top, expower)));
                return true;
            }
            catch (Exception e) { Plugin.Trace("[док] показатели боя: " + e.Message); return false; }
        }

        private const int ShroomFace = 16852;

        private static void Cap()
        {
            var mark = Marks[3];
            if (mark == null) return;
            var cap = Flasks.Exact(ShroomFace);
            if (cap != null && cap.texture != null)
            {
                if (mark.sprite == cap) return;
                mark.sprite = cap;
                mark.color = Color.white;
                return;
            }
            if (mark.sprite != null && mark.sprite.texture != null) return;
            var own = Icons.Mushroom();
            if (own == null) return;
            mark.sprite = own;
            mark.color = Paints[3];
        }

        private static void Level()
        {
            if (_levelRow == null) return;
            var info = Whose();
            bool show = info != null && info.NextLevelExperience > 0L && !SideButtons.InCombat();
            if (_levelRow.activeSelf != show)
            {
                _levelRow.SetActive(show);
                if (!show && _levelTip) { _levelTip = false; HideTip(); }
            }
            if (!show) return;
            Value(4, info.Experience, info.NextLevelExperience);
            if (!_levelTip) return;
            string tip = Left();
            if (tip == _levelSaid) return;
            _levelSaid = tip;
            ShowTip(_levelBar, tip);
        }

        private static string Left()
        {
            var info = Whose();
            if (info == null) return "";
            long left = info.NextLevelExperience - info.Experience;
            return "До уровня: " + Split(left > 0L ? left : 0L);
        }

        private static string Split(long value)
        {
            bool minus = value < 0L;
            string digits = (minus ? -value : value).ToString();
            var made = new StringBuilder(digits.Length + digits.Length / 3 + 1);
            if (minus) made.Append('-');
            int head = digits.Length % 3;
            if (head == 0) head = 3;
            made.Append(digits, 0, head);
            for (int at = head; at < digits.Length; at += 3) made.Append(' ').Append(digits, at, 3);
            return made.ToString();
        }

        private static void Fit(Text text, float room)
        {
            room -= 6f;
            if (room <= 8f) return;
            if (text.fontSize != BarFont) text.fontSize = BarFont;
            float want = text.preferredWidth;
            if (want <= room) return;
            int size = Mathf.Clamp(Mathf.FloorToInt(BarFont * room / want), BarLeast, BarFont);
            text.fontSize = size;
            while (size > BarLeast && text.preferredWidth > room) { size--; text.fontSize = size; }
        }

        private static GeneralUserInfo Whose()
        {
            try { return Controllers.User?.UserInfo; }
            catch { return null; }
        }

        private static void Value(int row, long now, long top)
        {
            var text = Values[row];
            var fill = Fills[row];
            if (text == null || fill == null) return;
            float part = top > 0L ? Mathf.Clamp01((float)((double)Math.Max(now, 0L) / top)) : 0f;
            bool drawn = part > 0.002f;
            if (fill.gameObject.activeSelf != drawn) fill.gameObject.SetActive(drawn);
            if (Mathf.Abs(fill.anchorMax.x - part) > 0.002f) fill.anchorMax = new Vector2(part, 1f);
            string line = Split(now) + " / " + Split(top);
            if (text.text != line) text.text = line;
            float room = text.rectTransform.rect.width;
            if (Fitted[row] == line && Mathf.Abs(FitRoom[row] - room) < 1f) return;
            Fitted[row] = line;
            FitRoom[row] = room;
            Fit(text, room);
        }

        private static UserIndicators Ind()
        {
            try { return Controllers.User?.Indicators; }
            catch { return null; }
        }

        private static float Scale(Canvas canvas)
        {
            return canvas != null && canvas.scaleFactor > 0.01f ? canvas.scaleFactor : 1f;
        }

        private static float Fits(float want)
        {
            var area = _canvasGo != null ? _canvasGo.transform as RectTransform : null;
            if (area == null || area.rect.height < 100f) return want;
            return Mathf.Min(want, area.rect.height - 120f);
        }

        private static void Lift(float pixels, bool deep)
        {
            bool want = pixels > 1f;
            foreach (var path in Paths)
            {
                Raise raise;
                if (!Raises.TryGetValue(path, out raise)) { raise = new Raise(); Raises[path] = raise; }

                RectTransform rt = raise.Rt;
                if (rt == null || Time.unscaledTime >= raise.Look)
                {
                    raise.Look = Time.unscaledTime + 0.5f;
                    try
                    {
                        var go = GameObject.Find(path);
                        if (go != null) rt = go.transform as RectTransform;
                    }
                    catch { }
                }
                if (rt == null) rt = raise.Rt;
                if (rt == null) continue;
                if (raise.Rt != rt)
                {
                    raise.Rt = rt;
                    raise.Home = rt.anchoredPosition;
                    raise.Size = rt.localScale;
                    raise.Up = false;
                }

                bool low = path.EndsWith("BottomPanel", StringComparison.Ordinal);
                if (!want)
                {
                    if (raise.Up)
                    {
                        rt.anchoredPosition = raise.Home;
                        if (deep && low) Grow(rt);
                        if (low) { Uncorner(); Uncenter(); }
                        raise.Up = false;
                    }
                    continue;
                }
                float scale = Scale(rt.GetComponentInParent<Canvas>());
                if (low) Trim(rt);
                var target = raise.Home + new Vector2(0f, pixels / scale);
                if ((rt.anchoredPosition - target).sqrMagnitude > 0.25f) rt.anchoredPosition = target;
                raise.Up = true;
                if (deep && low) { Corner(rt, scale); Center(rt, scale); }
            }
        }

        private static RectTransform _back;
        private static float _backAt;
        private static int _backCount;

        private static RectTransform Back(RectTransform panel)
        {
            if (_back != null && _back.IsChildOf(panel)) return _back;
            _back = null;
            if (Time.unscaledTime < _backAt) return null;
            _backAt = Time.unscaledTime + 0.5f;
            try
            {
                var bottom = panel.GetComponentInChildren<BottomPanel>(true);
                var go = bottom != null ? AccessTools.Field(typeof(BottomPanel), "returnButtonPanel")?.GetValue(bottom) as GameObject : null;
                _back = go != null ? go.transform as RectTransform : null;
                if (_back != null) Plugin.Trace("[док] панель кнопки возврата: " + _back.name);
            }
            catch (Exception e) { Plugin.Trace("[док] панель возврата: " + e.Message); }
            return _back;
        }

        private static void Corner(RectTransform panel, float scale)
        {
            var back = Back(panel);
            if (back == null) return;
            if (!Homes.ContainsKey(back)) Homes[back] = back.anchoredPosition;
            var canvas = panel.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float right = float.MinValue, bottom = float.MaxValue;
            int count = 0;
            for (int i = 0; i < back.childCount; i++)
            {
                var kid = back.GetChild(i) as RectTransform;
                if (kid == null || !kid.gameObject.activeSelf) continue;
                kid.GetWorldCorners(Corners);
                var low = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
                var high = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]);
                if (high.x > right) right = high.x;
                if (low.y < bottom) bottom = low.y;
                count++;
            }
            if (count == 0) return;
            if (count != _backCount)
            {
                _backCount = count;
                Plugin.Trace("[док] кнопок возврата в углу: " + count + ", прижимаю к краю всю группу");
            }
            float edge = 8f * scale;
            float dx = Screen.width - edge - right;
            float dy = edge - bottom;
            if (Mathf.Abs(dx) < 0.5f && Mathf.Abs(dy) < 0.5f) return;
            back.anchoredPosition += new Vector2(dx, dy) / scale;
        }

        private static void Uncorner()
        {
            Vector2 home;
            if (_back != null && Homes.TryGetValue(_back, out home)) _back.anchoredPosition = home;
        }

        private static RectTransform _row;
        private static float _rowAt;

        private static RectTransform Row(RectTransform panel)
        {
            if (_row != null && _row.IsChildOf(panel)) return _row;
            _row = null;
            if (Time.unscaledTime < _rowAt) return null;
            _rowAt = Time.unscaledTime + 0.5f;
            try
            {
                var bottom = panel.GetComponentInChildren<BottomPanel>(true);
                var go = bottom != null ? AccessTools.Field(typeof(BottomPanel), "buttonsPanel")?.GetValue(bottom) as GameObject : null;
                _row = go != null ? go.transform as RectTransform : null;
                if (_row != null) Plugin.Trace("[док] панель кнопок локации: " + _row.name);
            }
            catch (Exception e) { Plugin.Trace("[док] панель кнопок: " + e.Message); }
            return _row;
        }

        private const float TightGap = 8f;
        private static HorizontalOrVerticalLayoutGroup _rowGroup;
        private static float _rowSpacing;
        private static TextAnchor _rowAlign;
        private static bool _rowKept;
        private static bool _rowTold;

        private static void Tight(RectTransform row)
        {
            try
            {
                var group = row.GetComponent<HorizontalOrVerticalLayoutGroup>();
                if (group == null) { Pack(row); return; }
                if (!ReferenceEquals(_rowGroup, group))
                {
                    _rowGroup = group;
                    _rowSpacing = group.spacing;
                    _rowAlign = group.childAlignment;
                    _rowKept = true;
                    _rowTold = false;
                }
                if (!group.enabled) group.enabled = true;

                float slack = 0f;
                int seen = 0;
                for (int i = 0; i < row.childCount; i++)
                {
                    var kid = row.GetChild(i) as RectTransform;
                    if (kid == null || !kid.gameObject.activeSelf) continue;
                    float lo, hi;
                    if (!Edges(row, kid, out lo, out hi)) continue;
                    slack += kid.rect.width - (hi - lo);
                    seen++;
                }
                if (seen == 0) return;

                float want = TightGap - slack / seen;
                if (!_rowTold)
                {
                    _rowTold = true;
                    Plugin.Trace("[док] кнопки локации: " + seen + " шт, пустоты в слоте "
                        + Mathf.RoundToInt(slack / seen) + ", промежуток " + Mathf.RoundToInt(group.spacing)
                        + " → " + Mathf.RoundToInt(want));
                }
                if (Mathf.Abs(group.spacing - want) > 1f) group.spacing = want;
                int band = (int)group.childAlignment / 3;
                var middle = (TextAnchor)(band * 3 + 1);
                if (group.childAlignment != middle) group.childAlignment = middle;
            }
            catch (Exception e) { Plugin.Trace("[док] кнопки локации: " + e.Message); }
        }

        private static void Pack(RectTransform row)
        {
            float x = 0f;
            bool moved = false;
            for (int i = 0; i < row.childCount; i++)
            {
                var kid = row.GetChild(i) as RectTransform;
                if (kid == null || !kid.gameObject.activeSelf) continue;
                if (!Homes.ContainsKey(kid)) Homes[kid] = kid.anchoredPosition;
                float lo, hi;
                if (!Edges(row, kid, out lo, out hi))
                {
                    float half = kid.rect.width * kid.localScale.x * 0.5f;
                    float mid = row.InverseTransformPoint(kid.position).x;
                    lo = mid - half;
                    hi = mid + half;
                }
                float want = kid.anchoredPosition.x + (x - lo);
                if (Mathf.Abs(kid.anchoredPosition.x - want) > 0.5f)
                {
                    kid.anchoredPosition = new Vector2(want, kid.anchoredPosition.y);
                    moved = true;
                }
                x += hi - lo + TightGap;
            }
            if (moved) Plugin.Trace("[док] кнопки локации сдвинуты вплотную");
        }

        private static bool Edges(RectTransform row, RectTransform kid, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            foreach (var art in kid.GetComponentsInChildren<Graphic>(false))
            {
                if (art == null || !art.enabled || art.color.a < 0.02f) continue;
                art.rectTransform.GetWorldCorners(Corners);
                for (int c = 0; c < 4; c++)
                {
                    float at = row.InverseTransformPoint(Corners[c]).x;
                    if (at < lo) lo = at;
                    if (at > hi) hi = at;
                }
            }
            return hi > lo;
        }

        private static void Center(RectTransform panel, float scale)
        {
            var row = Row(panel);
            if (row == null) return;
            Tight(row);
            if (!Homes.ContainsKey(row)) Homes[row] = row.anchoredPosition;
            var canvas = panel.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float left = float.MaxValue, right = float.MinValue;
            for (int i = 0; i < row.childCount; i++)
            {
                var kid = row.GetChild(i) as RectTransform;
                if (kid == null || !kid.gameObject.activeSelf) continue;
                kid.GetWorldCorners(Corners);
                float one = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]).x;
                float two = RectTransformUtility.WorldToScreenPoint(cam, Corners[2]).x;
                if (one < left) left = one;
                if (two > right) right = two;
            }
            if (right <= left) return;
            float shift = Screen.width * 0.5f - (left + right) * 0.5f;
            if (Mathf.Abs(shift) < 0.5f) return;
            row.anchoredPosition += new Vector2(shift / scale, 0f);
        }

        private static void Uncenter()
        {
            Vector2 home;
            if (_rowKept && _rowGroup != null)
            {
                _rowGroup.spacing = _rowSpacing;
                _rowGroup.childAlignment = _rowAlign;
                if (!_rowGroup.enabled) _rowGroup.enabled = true;
                _rowKept = false;
                _rowTold = false;
            }
            if (_row == null) return;
            if (Homes.TryGetValue(_row, out home)) _row.anchoredPosition = home;
            for (int i = 0; i < _row.childCount; i++)
            {
                var kid = _row.GetChild(i) as RectTransform;
                if (kid != null && Homes.TryGetValue(kid, out home)) kid.anchoredPosition = home;
            }
        }

        private const float Small = 0.7f;

        private static readonly Dictionary<RectTransform, Vector2> Homes = new Dictionary<RectTransform, Vector2>();

        private static readonly Vector3[] Corners = new Vector3[4];

        private static void Trim(RectTransform panel)
        {
            for (int i = 0; i < panel.childCount; i++)
            {
                var group = panel.GetChild(i);
                for (int j = 0; j < group.childCount; j++)
                {
                    var item = group.GetChild(j) as RectTransform;
                    if (item == null) continue;
                    if (Mathf.Abs(item.localScale.x - Small) > 0.001f) item.localScale = Vector3.one * Small;
                }
                foreach (var art in group.GetComponents<Image>())
                {
                    var rt = art.rectTransform;
                    if (art.enabled && rt.rect.width > 600f && rt.rect.height < 30f) art.enabled = false;
                }
            }
            foreach (var art in panel.GetComponents<Image>())
            {
                var rt = art.rectTransform;
                if (art.enabled && rt.rect.width > 600f && rt.rect.height < 30f) art.enabled = false;
            }
        }

        private static void Grow(RectTransform panel)
        {
            for (int i = 0; i < panel.childCount; i++)
            {
                var group = panel.GetChild(i);
                for (int j = 0; j < group.childCount; j++)
                {
                    var item = group.GetChild(j) as RectTransform;
                    if (item != null) item.localScale = Vector3.one;
                }
                foreach (var art in group.GetComponents<Image>()) art.enabled = true;
            }
            foreach (var art in panel.GetComponents<Image>()) art.enabled = true;
        }

        private static float _awayAt;

        private static void Drop()
        {
            CombatBar.Drop();
            _to = 0;
            _toName = "";
            if (_canvasGo == null && Raises.Count == 0) return;
            try
            {
                ChatPick.Forget();
                if (_view != null) _view.OnContentLinkClick -= Clicked;
                Unbind();
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
            catch (Exception e) { Plugin.Trace("[док] снятие: " + e.Message); }
            _canvasGo = null;
            _canvas = null;
            _root = null;
            _view = null;
            _ink = null;
            _window = Page;
            _tabsRt = null;
            _viewRt = null;
            _rowsHost = null;
            Unbind();
            _listTabsRt = null;
            _boxRt = null;
            for (int i = 0; i < ListSheets.Length; i++) { ListSheets[i] = null; ListLabels[i] = null; }
            _input = null;
            _chip = null;
            _chipText = null;
            _tipGo = null;
            _tipText = null;
            _clock = null;
            _count = null;
            _shown = "";
            for (int i = 0; i < Values.Length; i++)
            {
                Values[i] = null; Fills[i] = null; Marks[i] = null; Fitted[i] = null; FitRoom[i] = 0f;
            }
            _levelRow = null;
            _levelBar = null;
            _levelTip = false;
            _levelSaid = null;
            for (int i = 0; i < Sheets.Length; i++) { Sheets[i] = null; SheetLabels[i] = null; }
            _seekGo = null;
            _seekField = null;
            _seekButton = null;
            _seek = "";
            Found.Clear();
            Lift(0f, true);
            Raises.Clear();
            Homes.Clear();
        }

        private static void Build()
        {
            if (VisualPrefabsHolder.Instance == null) return;
            try
            {
                _canvasGo = new GameObject("QoLChatDock", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                Curtain.Stage(_canvasGo, true);
                UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
                var canvas = _canvasGo.GetComponent<Canvas>();
                _canvas = canvas;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 250;
                var scaler = _canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                UiScale.Own(scaler);

                var panel = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline));
                panel.transform.SetParent(_canvasGo.transform, false);
                _root = (RectTransform)panel.transform;
                var back = panel.GetComponent<Image>();
                back.color = WardrobeLook.Window;
                back.sprite = OnlineWindow.Rounded(16);
                back.type = Image.Type.Sliced;
                var edge = panel.GetComponent<Outline>();
                edge.effectColor = WardrobeLook.Edge;
                edge.effectDistance = new Vector2(1f, -1f);

                Bars(_root);
                Middle(_root);
                Players(_root);
                Show(_tab);
                Chip();
                Plugin.Trace("[док] панель собрана");
            }
            catch (Exception e)
            {
                Plugin.Fault("[док] сборка панели: " + e.Message);
                Drop();
            }
        }

        private static void Bars(RectTransform host)
        {
            var box = new GameObject("bars", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            box.transform.SetParent(host, false);
            OnlineWindow.Place((RectTransform)box.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(Pad, Pad + Sink), new Vector2(Pad + BarsWidth, -Pad));
            var back = box.GetComponent<Image>();
            back.color = WardrobeLook.Card;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;

            var group = box.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(6, 6, 4, 4);
            group.spacing = RowGap;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;

            for (int i = 0; i < Values.Length; i++) Bar(box.transform, i);
            box.SetActive(true);
        }

        private static Sprite Mark(int index)
        {
            switch (index)
            {
                case 1: return Icons.Drop();
                case 2: return Icons.Bolt();
                case 3: return Icons.Mushroom();
                case 4: return Icons.Burst();
                default: return Icons.Heart();
            }
        }

        private static void Bar(Transform host, int index)
        {
            var row = new GameObject("row", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(host, false);
            var size = row.GetComponent<LayoutElement>();
            size.preferredHeight = 19f;
            size.minHeight = 16f;
            var line = row.GetComponent<HorizontalLayoutGroup>();
            line.spacing = 6f;
            line.childAlignment = TextAnchor.MiddleLeft;
            line.childControlWidth = true;
            line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = true;

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(row.transform, false);
            var icon = iconGo.GetComponent<Image>();
            var mark = Mark(index);
            icon.sprite = mark != null ? mark : OnlineWindow.Rounded(8);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = Paints[index];
            Marks[index] = icon;
            var ile = iconGo.GetComponent<LayoutElement>();
            ile.preferredWidth = 15f; ile.minWidth = 15f;
            ile.preferredHeight = 15f; ile.minHeight = 15f;

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            barGo.transform.SetParent(row.transform, false);
            var hollow = barGo.GetComponent<Image>();
            hollow.color = WardrobeLook.Field;
            hollow.sprite = OnlineWindow.Rounded(8);
            hollow.type = Image.Type.Sliced;
            var ble = barGo.GetComponent<LayoutElement>();
            ble.flexibleWidth = 1f;
            ble.minWidth = 92f;

            var fill = new GameObject("fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barGo.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = new Vector2(0f, 0f);
            frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = new Vector2(2f, 2f);
            frt.offsetMax = new Vector2(-2f, -2f);
            var paint = fill.GetComponent<Image>();
            paint.color = Paints[index];
            paint.sprite = OnlineWindow.Rounded(8);
            paint.type = Image.Type.Sliced;
            paint.raycastTarget = false;
            Fills[index] = frt;

            var value = OnlineWindow.Label(barGo.transform, "", BarFont, FontStyle.Bold, WardrobeLook.Bright);
            value.raycastTarget = false;
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            value.verticalOverflow = VerticalWrapMode.Overflow;
            var shadow = value.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(value.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Values[index] = value;
            Fitted[index] = null;
            FitRoom[index] = 0f;
            if (index != 4) return;
            _levelRow = row;
            _levelBar = (RectTransform)barGo.transform;
            var hover = barGo.AddComponent<HoverWatch>();
            hover.OnEnter = () => { _levelTip = true; _levelSaid = Left(); ShowTip(_levelBar, _levelSaid); };
            hover.OnExit = () => { _levelTip = false; _levelSaid = null; HideTip(); };
        }

        private static void Middle(RectTransform host)
        {
            float leftPad = Pad + BarsWidth + Gap;
            var mid = new GameObject("chat", typeof(RectTransform));
            mid.transform.SetParent(host, false);
            OnlineWindow.Place((RectTransform)mid.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(leftPad, Pad + Sink), new Vector2(-(Pad + ListWidth + Gap), -Pad));

            var view = new GameObject("view", typeof(RectTransform), typeof(Image));
            view.transform.SetParent(mid.transform, false);
            OnlineWindow.Place((RectTransform)view.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, InputHeight + 4f), Vector2.zero);
            _viewRt = (RectTransform)view.transform;
            Tabs(host, leftPad);
            var paper = view.GetComponent<Image>();
            paper.color = WardrobeLook.Field;
            paper.sprite = OnlineWindow.Rounded(8);
            paper.type = Image.Type.Sliced;

            try
            {
                var prefab = VisualPrefabsHolder.Instance.ChatPanelContentPrefab;
                if (prefab != null)
                {
                    _view = UnityEngine.Object.Instantiate(prefab).GetComponent<ChatPanelContent>();
                    _view.transform.SetParent(view.transform, false);
                    var vrt = _view.transform as RectTransform;
                    if (vrt != null)
                        OnlineWindow.Place(vrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                            new Vector2(4f, 4f), new Vector2(-4f, -4f));
                    _view.OnContentLinkClick += Clicked;
                    Smaller(_view);
                    _ink = AccessTools.Field(typeof(ChatPanelContent), "Text")?.GetValue(_view) as TMP_Text;
                    if (_ink != null) Smiles.Fit(_ink.font);
                    Ink();
                    ChatPick.Attach(_view);
                    Draggable(_view);
                }
            }
            catch (Exception e) { Plugin.Fault("[док] окно чата: " + e.Message); }

            _seekGo = Sieve(_viewRt);
            SeekShow(_tab == 1);

            var line = new GameObject("line", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            line.transform.SetParent(mid.transform, false);
            OnlineWindow.Place((RectTransform)line.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(0f, InputHeight));
            var inputRow = line.GetComponent<HorizontalLayoutGroup>();
            inputRow.spacing = 4f;
            inputRow.childAlignment = TextAnchor.MiddleLeft;
            inputRow.childControlWidth = true;
            inputRow.childControlHeight = true;
            inputRow.childForceExpandWidth = false;
            inputRow.childForceExpandHeight = true;

            _input = Field(line.transform);
            _chip = Chip(_input.transform);

            var keys = new GameObject("keys", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            keys.transform.SetParent(_root, false);
            OnlineWindow.Place((RectTransform)keys.transform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-(Pad + ListWidth), Pad + Sink), new Vector2(-(Pad + 46f), Pad + Sink + InputHeight));
            var keyRow = keys.GetComponent<HorizontalLayoutGroup>();
            keyRow.spacing = KeyGap;
            keyRow.childAlignment = TextAnchor.MiddleLeft;
            keyRow.childControlWidth = true;
            keyRow.childControlHeight = true;
            keyRow.childForceExpandWidth = false;
            keyRow.childForceExpandHeight = true;

            Tip(Pic(keys.transform, Icons.Enter(), () => Send(EChatMessageType.MSG_COMMON), WardrobeLook.OnAccent, WardrobeLook.Accent), "Всем (Enter)");
            Tip(Key(keys.transform, "К", KeySide, () => Send(EChatMessageType.MSG_CLAN)), "В клан");
            Tip(Key(keys.transform, "А", KeySide, () => Send(EChatMessageType.MSG_ALLIANCE)), "В альянс");
            Tip(Pic(keys.transform, Icons.Head(), () => Send(EChatMessageType.MSG_PRIVATE)), "Приватное сообщение (Shift+Пробел)");
            Button faces = null;
            faces = Pic(keys.transform, Smiles.Icon(), () => Smiles.Toggle((RectTransform)faces.transform), Color.white);
            Tip(faces, "Смайлики");
            Tip(Pic(keys.transform, Icons.Bin(), WipeOpen, WardrobeLook.Label), "Очистить открытую вкладку");
        }

        private static TMP_Text _ink;

        private static void Ink()
        {
            if (_ink == null) return;
            var want = ChatColors.Base;
            if (_ink.color != want) _ink.color = want;
        }

        private static void Tabs(RectTransform host, float leftPad)
        {
            var row = new GameObject("tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(host, false);
            var strip = row.GetComponent<HorizontalLayoutGroup>();
            strip.spacing = 3f;
            strip.childAlignment = TextAnchor.MiddleLeft;
            strip.childControlWidth = true;
            strip.childControlHeight = true;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = true;

            Sheets[0] = Sheet(row.transform, "Чат", 0, 40f);
            Sheets[1] = Sheet(row.transform, "Системные", 1, 64f);
            _seekButton = Leaf(row.transform, "Поиск", 46f, out _seekLabel);
            _seekButton.onClick.AddListener(SeekOpen);
            SeekPaint();
            _tabsRt = (RectTransform)row.transform;
            _tabsPad = leftPad;
            Aim(SideButtons.InCombat(), true);
        }

        private static void Aim(bool down, bool force)
        {
            if (_tabsRt == null || _viewRt == null) return;
            if (!force && down == _tabsDown) return;
            _tabsDown = down;
            float right = -(Pad + ListWidth + Gap);
            if (down)
            {
                OnlineWindow.Place(_tabsRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(_tabsPad, -(Pad + TabHigh)), new Vector2(right, -Pad));
                _viewRt.offsetMax = new Vector2(0f, -TabHigh);
            }
            else
            {
                OnlineWindow.Place(_tabsRt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(_tabsPad, 0f), new Vector2(right, TabHigh));
                _viewRt.offsetMax = Vector2.zero;
            }
            foreach (var tab in Sheets)
            {
                var back = tab != null ? tab.GetComponent<Image>() : null;
                if (back != null) back.sprite = Hood(down);
            }
            var seekHood = _seekButton != null ? _seekButton.GetComponent<Image>() : null;
            if (seekHood != null) seekHood.sprite = Hood(down);
        }

        private const string MapScene = "GlobalMapLocation";
        private static bool _listDown;

        private static void ListAim(bool force)
        {
            if (_listTabsRt == null || _boxRt == null) return;
            bool down = SideButtons.InCombat() || _scene == MapScene;
            if (!force && down == _listDown) return;
            _listDown = down;
            float lift = down ? TabHigh : 0f;
            if (down)
                OnlineWindow.Place(_listTabsRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-(Pad + ListWidth), -(Pad + TabHigh)), new Vector2(-Pad, -Pad));
            else
                OnlineWindow.Place(_listTabsRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-(Pad + ListWidth), 0f), new Vector2(-Pad, TabHigh));
            _boxRt.offsetMax = new Vector2(-Pad, -(Pad + ClockHigh + 2f + lift));
            if (_count != null)
            {
                _count.rectTransform.offsetMin = new Vector2(-(Pad + ListWidth - 6f), -(Pad + ClockHigh + lift));
                _count.rectTransform.offsetMax = new Vector2(-(Pad + 20f), -(Pad + lift));
            }
            foreach (var tab in ListSheets)
            {
                var back = tab != null ? tab.GetComponent<Image>() : null;
                if (back != null) back.sprite = Hood(down);
            }
        }

        private static void Players(RectTransform host)
        {
            var box = new GameObject("players", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            box.transform.SetParent(host, false);
            var brt = (RectTransform)box.transform;
            _boxRt = brt;
            OnlineWindow.Place(brt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-(Pad + ListWidth), Pad + Sink + InputHeight + 4f), new Vector2(-Pad, -(Pad + ClockHigh + 2f)));
            _clock = OnlineWindow.Label(host, "", 11, FontStyle.Bold, WardrobeLook.Label);
            _clock.alignment = TextAnchor.MiddleRight;
            _clock.raycastTarget = false;
            _clock.horizontalOverflow = HorizontalWrapMode.Overflow;
            _clock.verticalOverflow = VerticalWrapMode.Overflow;
            var shade = _clock.gameObject.AddComponent<Shadow>();
            shade.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shade.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(_clock.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-(Pad + 44f), Pad + Sink), new Vector2(-(Pad + 2f), Pad + Sink + InputHeight));
            var back = box.GetComponent<Image>();
            back.color = WardrobeLook.Card;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;

            var scroll = box.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var rows = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            rows.transform.SetParent(box.transform, false);
            var rrt = (RectTransform)rows.transform;
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.pivot = new Vector2(0.5f, 1f);
            rrt.offsetMin = Vector2.zero;
            rrt.offsetMax = Vector2.zero;
            var group = rows.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(4, 9, 0, 0);
            group.spacing = -4f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = false;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            var fit = rows.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = rrt;
            scroll.viewport = brt;
            _rowsHost = rows.transform;
            Rail(box.transform, scroll);
            _count = OnlineWindow.Label(host, "", 10, FontStyle.Bold, WardrobeLook.Faint);
            _count.alignment = TextAnchor.MiddleLeft;
            _count.raycastTarget = false;
            _count.horizontalOverflow = HorizontalWrapMode.Overflow;
            _count.verticalOverflow = VerticalWrapMode.Overflow;
            OnlineWindow.Place(_count.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-(Pad + ListWidth - 6f), -(Pad + ClockHigh)), new Vector2(-(Pad + 20f), -Pad));

            try
            {
                var holder = VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab?.GetComponent<ChatUserListPanelContent>();
                _rowPrefab = holder != null
                    ? AccessTools.Field(typeof(ChatUserListPanelContent), "SmallUserRowPrefab")?.GetValue(holder) as GameObject
                    : null;
                _resolver = Controllers.Get<UserContextMenuController>()?.UserContextMenuResolver;
                Bind(0);
                if (_list > 0) Bind(_list);
            }
            catch (Exception e) { Plugin.Warn("[док] список игроков: " + e.Message); }
            ListTabs(host);
        }

        private static void ListTabs(RectTransform host)
        {
            var row = new GameObject("listTabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(host, false);
            var strip = row.GetComponent<HorizontalLayoutGroup>();
            strip.spacing = 3f;
            strip.childAlignment = TextAnchor.MiddleLeft;
            strip.childControlWidth = true;
            strip.childControlHeight = true;
            strip.childForceExpandWidth = false;
            strip.childForceExpandHeight = true;
            ListSheets[0] = ListSheet(row.transform, "Локация", 0, 52f);
            ListSheets[1] = ListSheet(row.transform, "Клан", 1, 36f);
            ListSheets[2] = ListSheet(row.transform, "Альянс", 2, 46f);
            _listTabsRt = (RectTransform)row.transform;
            ListAim(true);
            ShowList(_list);
        }

        private static Button ListSheet(Transform host, string title, int index, float width)
        {
            var button = Leaf(host, title, width, out var label);
            ListLabels[index] = label;
            button.onClick.AddListener(() => ShowList(index));
            return button;
        }

        private static void ShowList(int kind)
        {
            _list = kind;
            Hear();
            if (kind > 0) Bind(kind);
            for (int i = 0; i < ListLabels.Length; i++)
            {
                if (ListLabels[i] == null) continue;
                ListLabels[i].color = i == kind ? WardrobeLook.OnAccent : WardrobeLook.Label;
                var back = ListSheets[i] != null ? ListSheets[i].GetComponent<Image>() : null;
                if (back != null) back.color = i == kind ? WardrobeLook.Accent : WardrobeLook.Tab;
            }
            Refill();
        }

        private static ListWrapper<UserRowInfoMessage> Fetch(int kind)
        {
            var chat = DependencyContainer.GetContainer()?.Resolve<IChat>();
            if (chat == null) return null;
            if (kind == 1) return chat.GetUserList(EUserListType.PLAYER_LIST_CLAN);
            if (kind == 2) return chat.GetUserList(EUserListType.PLAYER_LIST_ALIANCE);
            return chat.PlayersOnLocation;
        }

        private static void Bind(int kind)
        {
            ListWrapper<UserRowInfoMessage> now = null;
            try { now = Fetch(kind); }
            catch (Exception e) { Plugin.Trace("[док] список " + kind + ": " + e.Message); }
            var was = Lists[kind];
            if (was == now) return;
            if (was != null) was.OnChange -= ListHooks[kind];
            Lists[kind] = now;
            if (now != null) now.OnChange += ListHooks[kind];
        }

        private static void Unbind()
        {
            for (int i = 0; i < Lists.Length; i++)
            {
                if (Lists[i] != null) Lists[i].OnChange -= ListHooks[i];
                Lists[i] = null;
            }
        }

        private static void ListChanged(int kind)
        {
            if (kind == _list) Refill();
        }

        private static void Hear()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _listHeard = null; return; }
            if (ReferenceEquals(_listHeard, nc)) return;
            nc.RemoveMessageListener(460, OnPlayerList);
            nc.AddMessageListener(460, OnPlayerList);
            _listHeard = nc;
        }

        private static void OnPlayerList(object message)
        {
            try
            {
                var m = message as PlayerListResponseMessage;
                if (m == null || _list == 0) return;
                int want = _list == 1 ? (int)EUserListType.PLAYER_LIST_CLAN : (int)EUserListType.PLAYER_LIST_ALIANCE;
                if (m.Type == want) _listDue = Time.frameCount;
            }
            catch (Exception e) { Plugin.Trace("[док] ответ 460: " + e.Message); }
        }

        private static void Freshen()
        {
            Hear();
            if (_list == 0 || Time.unscaledTime < _listAt) return;
            _listAt = Time.unscaledTime + 5f;
            var was = Lists[_list];
            Bind(_list);
            if (Lists[_list] != was) Refill();
        }

        private static void Rebind()
        {
            try
            {
                Bind(0);
                if (_list > 0) Bind(_list);
                _resolver = Controllers.Get<UserContextMenuController>()?.UserContextMenuResolver;
                Refill();
                Show(_tab);
            }
            catch (Exception e) { Plugin.Trace("[док] пересборка списка: " + e.Message); }
        }

        private const float RowScale = 0.72f;
        private static float MenuScale => Dialogs.Scale;

        private static void Menu(int id, string login)
        {
            try
            {
                if (_view == null || _resolver == null) return;
                var data = new UserContextMenuData(id, login);
                CloseMenus();
                var stage = SideButtons.Area();
                ContextMenu.ShowContextMenu(stage != null ? stage.gameObject : _view.gameObject, _resolver, data, EContextMenuSide.Left);
                var at = (Vector2)Input.mousePosition;
                at.x += 8f;
                Soon(at);
            }
            catch (Exception e) { Plugin.Trace("[док] меню по нику: " + e.Message); }
        }

        private static void Nudge(GameObject row)
        {
            var rrt = row != null ? row.transform as RectTransform : null;
            if (rrt == null) return;
            var corners = new Vector3[4];
            rrt.GetWorldCorners(corners);
            var screen = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
            screen.x -= 6f;
            Soon(screen);
        }

        private static Vector2 _menuAt;
        private static float _menuAtTime = -100f;

        internal static void Dressed()
        {
            if (Time.unscaledTime - _menuAtTime > 3f) return;
            Nudge(_menuAt);
        }

        private static void Soon(Vector2 screen)
        {
            _menuAt = screen;
            _menuAtTime = Time.unscaledTime;
            Nudge(screen);
            if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(Settle(screen));
        }

        private static System.Collections.IEnumerator Settle(Vector2 screen)
        {
            yield return null;
            Nudge(screen);
        }

        private static void Nudge(Vector2 screen)
        {
            try
            {
                var menu = UnityEngine.Object.FindObjectOfType<ContextMenu>();
                var mrt = menu != null ? menu.transform as RectTransform : null;
                if (mrt == null) return;
                var canvas = mrt.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                var parent = mrt.parent as RectTransform;
                Vector3 world;
                if (parent == null || !RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen, cam, out world)) return;
                mrt.pivot = new Vector2(1f, 0f);
                mrt.localScale = new Vector3(MenuScale, MenuScale, 1f);
                mrt.position = world;
                var lift = mrt.GetComponent<Canvas>();
                if (lift == null)
                {
                    lift = mrt.gameObject.AddComponent<Canvas>();
                    mrt.gameObject.AddComponent<GraphicRaycaster>();
                }
                lift.overrideSorting = true;
                lift.sortingOrder = 300;
                var skin = mrt.GetComponent<Image>();
                if (skin != null && skin.sprite != null && skin.sprite.name != "round8")
                {
                    Plugin.Trace("[док] фон меню: " + skin.sprite.name + ", тип " + skin.type);
                    skin.sprite = OnlineWindow.Rounded(8);
                    skin.type = Image.Type.Sliced;
                }
                var told = new StringBuilder();
                if (mrt.parent != null)
                    foreach (Transform kin in mrt.parent)
                    {
                        if (kin == null || kin == mrt) continue;
                        string mark = kin.name;
                        told.Append("[сосед ").Append(mark).Append("] ");
                        if (mark.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 || mark.IndexOf("pointer", StringComparison.OrdinalIgnoreCase) >= 0)
                            kin.gameObject.SetActive(false);
                    }
                foreach (var art in mrt.GetComponentsInChildren<Image>(true))
                {
                    if (art == null || art.transform == mrt) continue;
                    string tag = art.gameObject.name + (art.sprite != null ? "=" + art.sprite.name : "");
                    told.Append(tag).Append(' ');
                    if (tag.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("pointer", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("tail", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("tri", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("strelk", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("corner", StringComparison.OrdinalIgnoreCase) >= 0)
                        art.enabled = false;
                }
                Plugin.Trace("[док] части меню: " + told);
                Plugin.Trace("[док] меню игрока: холст " + (canvas != null ? canvas.name + " порядок " + canvas.sortingOrder : "нет"));
            }
            catch (Exception e) { Plugin.Trace("[док] сдвиг меню: " + e.Message); }
        }

        private const float RowHigh = 22f;

        private static void Shrink(GameObject go)
        {
            var rt = go.transform as RectTransform;
            if (rt == null) return;
            float wide = (ListWidth - 13f) / RowScale;
            var size = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            size.preferredHeight = RowHigh;
            size.minHeight = RowHigh;
            size.flexibleHeight = 0f;
            size.preferredWidth = wide;
            size.minWidth = wide;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(wide, RowHigh / RowScale);
            rt.localScale = new Vector3(RowScale, RowScale, 1f);
        }

        private static void Rail(Transform host, ScrollRect scroll)
        {
            var go = new GameObject("rail", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            OnlineWindow.Place((RectTransform)go.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-6f, 4f), new Vector2(-2.5f, -4f));
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Field;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var slide = new GameObject("slide", typeof(RectTransform));
            slide.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)slide.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var grip = new GameObject("grip", typeof(RectTransform), typeof(Image));
            grip.transform.SetParent(slide.transform, false);
            OnlineWindow.Place((RectTransform)grip.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var art = grip.GetComponent<Image>();
            art.color = WardrobeLook.FieldEdge;
            art.sprite = OnlineWindow.Rounded(8);
            art.type = Image.Type.Sliced;
            var bar = go.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = (RectTransform)grip.transform;
            bar.targetGraphic = art;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static readonly List<UserRowInfoMessage> Watched = new List<UserRowInfoMessage>();
        private static int _castMark;

        private static bool Peek
        {
            get { try { return Spectate.Peeking; } catch { return false; } }
        }

        private static List<UserRowInfoMessage> Cast()
        {
            Watched.Clear();
            try
            {
                var cd = FighterHint.Cd();
                if (cd == null || cd.Characters == null) return Watched;
                foreach (var pair in cd.Characters)
                {
                    var one = pair.Value;
                    if (one == null || one.UserId <= 0 || string.IsNullOrEmpty(one.Login)) continue;
                    var row = new UserRowInfoMessage(one.UserId, one.Login, one.Level);
                    Cards.Dress(row);
                    OnlineWindow.Extras(row);
                    Watched.Add(row);
                }
                Watched.Sort((a, b) => string.Compare(a.Login, b.Login, StringComparison.CurrentCultureIgnoreCase));
            }
            catch (Exception e) { Plugin.Trace("[док] состав боя: " + e.Message); }
            return Watched;
        }

        private static void Watch()
        {
            if (!Peek || _list != 0) { if (_castMark != 0) { _castMark = 0; Refill(); } return; }
            int mark = 0;
            try
            {
                var cd = FighterHint.Cd();
                if (cd != null && cd.Characters != null)
                    foreach (var pair in cd.Characters)
                        if (pair.Value != null && pair.Value.UserId > 0) mark = mark * 31 + pair.Value.UserId;
                mark = mark * 31 + Cards.Stamp;
            }
            catch { }
            if (mark == _castMark) return;
            _castMark = mark;
            Refill();
        }

        private static void Count()
        {
            if (_count == null) return;
            if (Peek && _list == 0)
            {
                string watching = "в бою: " + Cast().Count;
                if (_count.text != watching) _count.text = watching;
                return;
            }
            var list = Lists[_list];
            string line = ListCaptions[_list] + (list != null && list.Content != null ? list.Content.Count : 0);
            if (_count.text != line) _count.text = line;
        }

        private static void Refill()
        {
            try
            {
                Count();
                if (_rowsHost == null || _rowPrefab == null) return;
                bool cast = Peek && _list == 0;
                var list = Lists[_list];
                for (int i = _rowsHost.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(_rowsHost.GetChild(i).gameObject);
                if (!cast && list == null) return;

                foreach (var player in (cast ? (IEnumerable<UserRowInfoMessage>)Cast() : list.Content))
                {
                    if (player == null) continue;
                    var row = player;
                    var go = UnityEngine.Object.Instantiate(_rowPrefab, _rowsHost, false);
                    go.SetActive(true);
                    Shrink(go);
                    var widget = go.GetComponent<UserRowWidget>();
                    if (widget == null) continue;
                    widget.Data = row;
                    Plain(widget, row.IsAway == true);
                    OnlineWindow.MarkAway(widget, row.IsAway == true);
                    widget.OnItemClickDelegate = item => Recipient(row.UserId, row.Login);
                    widget.OnRightButtonClickDelegate = item =>
                    {
                        try
                        {
                            CloseMenus();
                            var stage = SideButtons.Area();
                            ContextMenu.ShowContextMenu(stage != null ? stage.gameObject : go, _resolver, item.Data, EContextMenuSide.Left);
                            Nudge(go);
                        }
                        catch (Exception e) { Plugin.Trace("[док] меню игрока: " + e.Message); }
                    };
                }
            }
            catch (Exception e) { Plugin.Trace("[док] строки списка: " + e.Message); }
        }

        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        private static void Plain(UserRowWidget widget, bool away)
        {
            try
            {
                var back = widget.GetComponent<Image>();
                if (back != null) back.color = Clear;
                var pick = AccessTools.Field(typeof(UserRowWidget), "SelectionButton")?.GetValue(widget) as Button;
                var face = pick != null ? pick.GetComponent<Image>() : null;
                if (face != null && face != back) face.color = Clear;
                var login = AccessTools.Field(typeof(UserRowWidget), "LoginText")?.GetValue(widget) as Text;
                if (login != null) login.color = away ? WardrobeLook.Faint : WardrobeLook.Bright;
                var level = AccessTools.Field(typeof(UserRowWidget), "LevelText")?.GetValue(widget) as Text;
                if (level != null) level.color = WardrobeLook.Faint;
            }
            catch (Exception e) { Plugin.Trace("[док] оформление строки игрока: " + e.Message); }
        }

        private static void Draggable(ChatPanelContent view)
        {
            try
            {
                var scroll = AccessTools.Field(typeof(ChatPanelContent), "ScrollRect")?.GetValue(view) as ScrollRect;
                var bar = scroll != null ? scroll.verticalScrollbar : null;
                if (bar == null) { Plugin.Trace("[док] ползунок чата не найден"); return; }
                bar.interactable = true;
                if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
                foreach (var art in bar.GetComponentsInChildren<Image>(true))
                    if (art != null) art.raycastTarget = true;
                var own = bar.GetComponent<Image>();
                if (own != null)
                {
                    own.raycastTarget = true;
                    own.sprite = OnlineWindow.Rounded(8);
                    own.type = Image.Type.Sliced;
                    own.color = WardrobeLook.Card;
                }
                var handle = bar.handleRect != null ? bar.handleRect.GetComponent<Image>() : null;
                if (handle != null)
                {
                    handle.raycastTarget = true;
                    handle.sprite = OnlineWindow.Rounded(8);
                    handle.type = Image.Type.Sliced;
                    handle.color = WardrobeLook.FieldEdge;
                    bar.targetGraphic = handle;
                }
                bar.transition = Selectable.Transition.ColorTint;
                var tints = bar.colors;
                tints.normalColor = Color.white;
                tints.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
                tints.pressedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
                tints.selectedColor = Color.white;
                bar.colors = tints;
                Plugin.Trace("[док] ползунок чата: " + bar.name + ", ручка " + (bar.handleRect != null ? bar.handleRect.name : "нет"));
            }
            catch (Exception e) { Plugin.Trace("[док] ползунок чата: " + e.Message); }
        }

        private static void Smaller(ChatPanelContent view)
        {
            try
            {
                var body = AccessTools.Field(typeof(ChatPanelContent), "Text")?.GetValue(view) as Component;
                if (body == null) return;
                var size = AccessTools.Property(body.GetType(), "fontSize");
                if (size == null) return;
                float now = (float)size.GetValue(body, null);
                if (now > 1f) size.SetValue(body, Mathf.Max(9f, now * 0.6f), null);
            }
            catch (Exception e) { Plugin.Trace("[док] шрифт чата: " + e.Message); }
        }

        private static void Clicked(IChatLinkData data)
        {
            try
            {
                var player = data as PlayerChatLink;
                if (player != null)
                {
                    bool right = _asMenu || Input.GetMouseButton(1) || Input.GetMouseButtonUp(1);
                    Plugin.Trace("[док] клик по нику " + player.Login + (right ? " правой" : " левой"));
                    if (right) { Menu(player.PlayerId, player.Login); return; }
                    Recipient(player.PlayerId, player.Login);
                    return;
                }
                if (data != null) data.Execute();
            }
            catch (Exception e) { Plugin.Trace("[док] ссылка в чате: " + e.Message); }
        }

        private static bool _asMenu;

        internal static bool NameMenu(Component handler)
        {
            try
            {
                if (_view == null || handler == null) return false;
                var tmp = AccessTools.Field(typeof(TextClickHandler), "textMeshPro")?.GetValue(handler) as TMP_Text;
                if (tmp == null) return false;
                int at = TMP_TextUtilities.FindIntersectingLink(tmp, Input.mousePosition, null);
                if (at < 0) return false;
                int id;
                if (!int.TryParse(tmp.textInfo.linkInfo[at].GetLinkID(), out id)) return false;
                if (ChatLinks.Has(id)) return false;
                _asMenu = true;
                try { AccessTools.Method(typeof(ChatPanelContent), "OnClick")?.Invoke(_view, new object[] { id }); }
                finally { _asMenu = false; }
                return true;
            }
            catch (Exception e) { Plugin.Trace("[док] меню по строке: " + e.Message); return false; }
        }

        private static Sprite _capUp;
        private static Sprite _capDown;

        private static Sprite Hood(bool down)
        {
            if (down && _capDown != null) return _capDown;
            if (!down && _capUp != null) return _capUp;
            const int radius = 4;
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x < radius ? radius - x - 0.5f : (x >= size - radius ? x - (size - radius) + 0.5f : 0f);
                    float dy = down
                        ? (y < radius ? radius - y - 0.5f : 0f)
                        : (y >= size - radius ? y - (size - radius) + 0.5f : 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= radius - 1f ? 1f : (d >= radius ? 0f : radius - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var made = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            if (down) _capDown = made; else _capUp = made;
            return made;
        }

        private static Button Sheet(Transform host, string title, int index, float width)
        {
            var button = Leaf(host, title, width, out var label);
            SheetLabels[index] = label;
            button.onClick.AddListener(() => Show(index));
            return button;
        }

        private static Button Leaf(Transform host, string title, float width, out Text label)
        {
            var go = new GameObject("tab", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.sprite = Hood(_tabsDown);
            back.type = Image.Type.Sliced;
            back.color = WardrobeLook.Tab;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var size = go.GetComponent<LayoutElement>();
            size.preferredWidth = width;
            size.minWidth = width;
            label = OnlineWindow.Label(go.transform, title, 9, FontStyle.Bold, WardrobeLook.Label);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(3f, 0f), new Vector2(-3f, 0f));
            return Lit(go.GetComponent<Button>(), back);
        }

        private static Button Lit(Button button, Image face)
        {
            button.targetGraphic = face;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            button.colors = colors;
            return button;
        }

        private static GameObject _tipGo;
        private static Text _tipText;

        private static void Tip(Button button, string text, bool below = false)
        {
            if (button == null) return;
            var watch = button.gameObject.AddComponent<HoverWatch>();
            var rt = (RectTransform)button.transform;
            watch.OnEnter = () => ShowTip(rt, text, below);
            watch.OnExit = HideTip;
        }

        private static void ShowTip(RectTransform near, string text, bool below = false)
        {
            try
            {
                if (_canvasGo == null || near == null) return;
                if (_tipGo == null)
                {
                    _tipGo = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline));
                    _tipGo.transform.SetParent(_canvasGo.transform, false);
                    var back = _tipGo.GetComponent<Image>();
                    back.color = WardrobeLook.Popup;
                    back.sprite = OnlineWindow.Rounded(8);
                    back.type = Image.Type.Sliced;
                    back.raycastTarget = false;
                    var edge = _tipGo.GetComponent<Outline>();
                    edge.effectColor = WardrobeLook.Edge;
                    edge.effectDistance = new Vector2(1f, -1f);
                    _tipText = OnlineWindow.Label(_tipGo.transform, "", 12, FontStyle.Normal, WardrobeLook.Bright);
                    _tipText.raycastTarget = false;
                    _tipText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    OnlineWindow.Place(_tipText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(8f, 2f), new Vector2(-8f, -2f));
                }
                _tipText.text = text;
                var trt = (RectTransform)_tipGo.transform;
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.pivot = new Vector2(0.5f, below ? 1f : 0f);
                trt.sizeDelta = new Vector2(_tipText.preferredWidth + 18f, 22f);
                var corners = new Vector3[4];
                near.GetWorldCorners(corners);
                var edgeAt = below ? (corners[0] + corners[3]) * 0.5f : (corners[1] + corners[2]) * 0.5f;
                var point = RectTransformUtility.WorldToScreenPoint(null, edgeAt);
                Vector2 spot;
                var root = (RectTransform)_canvasGo.transform;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, point, null, out spot))
                    trt.anchoredPosition = spot + new Vector2(0f, below ? -4f : 4f);
                _tipGo.SetActive(true);
                _tipGo.transform.SetAsLastSibling();
            }
            catch (Exception e) { Plugin.Trace("[док] подсказка кнопки: " + e.Message); }
        }

        private static void HideTip()
        {
            if (_tipGo != null) _tipGo.SetActive(false);
        }

        private static Button Pic(Transform host, Sprite sprite, Action click, Color? tint = null, Color? face = null)
        {
            var go = new GameObject("button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.color = face ?? WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var size = go.GetComponent<LayoutElement>();
            size.preferredWidth = KeySide;
            size.minWidth = KeySide;
            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(5f, 5f), new Vector2(-5f, -5f));
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.color = tint ?? WardrobeLook.Bright;
            var button = Lit(go.GetComponent<Button>(), back);
            button.onClick.AddListener(() => click());
            return button;
        }

        private static Button Key(Transform host, string title, float width, Action click)
        {
            var go = new GameObject("button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var size = go.GetComponent<LayoutElement>();
            size.preferredWidth = width;
            size.minWidth = width;
            var label = OnlineWindow.Label(go.transform, title, 12, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(3f, 2f), new Vector2(-3f, -2f));
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 9;
            label.resizeTextMaxSize = 12;
            var button = Lit(go.GetComponent<Button>(), back);
            button.onClick.AddListener(() => click());
            return button;
        }

        private static GameObject Chip(Transform host)
        {
            var go = new GameObject("chip", typeof(RectTransform), typeof(Button));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(9f, 1f);
            rt.offsetMax = new Vector2(9f, -1f);
            rt.sizeDelta = new Vector2(0f, rt.sizeDelta.y);

            _chipText = OnlineWindow.Label(go.transform, "", 13, FontStyle.Bold, WardrobeLook.Accent);
            _chipText.alignment = TextAnchor.MiddleLeft;
            _chipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _chipText.verticalOverflow = VerticalWrapMode.Overflow;
            OnlineWindow.Place(_chipText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            go.GetComponent<Button>().onClick.AddListener(() => { _to = 0; _toName = ""; Chip(); Focus(); });
            var trigger = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(data =>
            {
                var click = data as PointerEventData;
                if (click == null || click.button != PointerEventData.InputButton.Right || _to <= 0) return;
                Plugin.Trace("[док] клик по адресату " + _toName + " правой");
                Menu(_to, _toName);
            });
            trigger.triggers.Add(entry);
            go.SetActive(false);
            return go;
        }

        private static void Inset(float left)
        {
            if (_input == null) return;
            var text = _input.textComponent != null ? _input.textComponent.rectTransform : null;
            var hint = _input.placeholder != null ? _input.placeholder.rectTransform : null;
            if (text != null) text.offsetMin = new Vector2(9f + left, text.offsetMin.y);
            if (hint != null) hint.offsetMin = new Vector2(9f + left, hint.offsetMin.y);
        }

        private static bool _want;
        private static bool _all;
        private static bool _armed;
        private static int _armedAt = -1;

        private static string Clean()
        {
            string body = _input.text != null ? _input.text.Trim() : "";
            if (_input.text != body)
            {
                _input.text = body;
            }
            _eat = true;
            return body;
        }

        private static void Ended(string text)
        {
            try
            {
                if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)
                    && !Input.GetKey(KeyCode.Return) && !Input.GetKey(KeyCode.KeypadEnter)) return;
                if (text == null || text.Trim().Length == 0) return;
                _all = true;
            }
            catch (Exception e) { Plugin.Trace("[док] конец ввода: " + e.Message); }
        }

        private static string Flat(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            bool dirty = false;
            foreach (char c in text) if (c < ' ') { dirty = true; break; }
            if (!dirty) return text;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text) sb.Append(c < ' ' ? ' ' : c);
            return sb.ToString();
        }

        private static void Typed(string now)
        {
            try
            {
                string clean = Flat(now);
                if (clean != now)
                {
                    if (_input != null) _input.text = clean;
                    return;
                }
            }
            catch (Exception e) { Plugin.Trace("[док] набор: " + e.Message); }
        }

        private static GameObject Sieve(RectTransform host)
        {
            if (host == null) return null;
            var go = new GameObject("seek", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField));
            go.transform.SetParent(host, false);
            OnlineWindow.Place((RectTransform)go.transform, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-176f, -26f), new Vector2(-18f, -6f));
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            go.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);

            var text = OnlineWindow.Label(go.transform, "", 11, FontStyle.Normal, WardrobeLook.Bright);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(7f, 0f), new Vector2(-7f, 0f));

            var hint = OnlineWindow.Label(go.transform, "искать в ленте", 11, FontStyle.Normal, WardrobeLook.Faint);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.raycastTarget = false;
            OnlineWindow.Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(7f, 0f), new Vector2(-7f, 0f));

            _seekField = go.GetComponent<InputField>();
            _seekField.onValueChanged.AddListener(Seeking);
            _seekField.targetGraphic = back;
            _seekField.textComponent = text;
            _seekField.placeholder = hint;
            _seekField.lineType = InputField.LineType.SingleLine;
            _seekField.characterLimit = 60;
            WardrobeLook.Style(_seekField);
            go.SetActive(false);
            return go;
        }

        private static InputField Field(Transform host)
        {
            var go = new GameObject("input", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            go.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);
            var size = go.GetComponent<LayoutElement>();
            size.flexibleWidth = 1f;
            size.minWidth = 170f;

            var text = OnlineWindow.Label(go.transform, "", 13, FontStyle.Normal, WardrobeLook.Bright);
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            OnlineWindow.Place(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(9f, 1f), new Vector2(-9f, -1f));

            var hint = OnlineWindow.Label(go.transform, "написать в чат", 13, FontStyle.Normal, WardrobeLook.Faint);
            hint.alignment = TextAnchor.MiddleLeft;
            hint.verticalOverflow = VerticalWrapMode.Overflow;
            hint.horizontalOverflow = HorizontalWrapMode.Overflow;
            hint.raycastTarget = false;
            OnlineWindow.Place(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(9f, 1f), new Vector2(-9f, -1f));

            var field = go.GetComponent<InputField>();
            field.onValueChanged.AddListener(Typed);
            field.onEndEdit.AddListener(Ended);
            field.targetGraphic = back;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 300;
            WardrobeLook.Style(field);
            return field;
        }
    }

    [HarmonyPatch(typeof(ChatController), "ChatMessageReceived")]
    public static class ChatDockFeedPatch
    {
        private static void Postfix(ChatResponseMessage message)
        {
            try { ChatDock.Feed(message); }
            catch (Exception e) { Plugin.Trace("[док] приём: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(ChatImpl), "Clear")]
    public static class ChatDockClearPatch
    {
        private static void Postfix()
        {
            try { ChatDock.Wipe(); }
            catch (Exception e) { Plugin.Trace("[док] сброс: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(LeftBottomMenuController), "OnChatButtonClicked")]
    public static class ChatDockMenuButtonPatch
    {
        private static bool Prefix()
        {
            if (!ChatDock.Active) return true;
            ChatDock.Focus();
            return false;
        }
    }

    [HarmonyPatch(typeof(ChatWindowController), "RestoreChat")]
    public static class ChatDockRestorePatch
    {
        private static bool Prefix()
        {
            return !ChatDock.Active;
        }
    }

    [HarmonyPatch(typeof(ChatWindowController), "SetMessageRecipient")]
    public static class ChatDockWindowRecipientPatch
    {
        private static bool Prefix(UserContextMenuData contextMenuData)
        {
            if (!ChatDock.Active) return true;
            ChatDock.Recipient(contextMenuData);
            return false;
        }
    }

    [HarmonyPatch(typeof(UserContextMenuController), "SetMessageRecipient")]
    public static class ChatDockMenuRecipientPatch
    {
        private static bool Prefix(UserContextMenuData obj)
        {
            if (!ChatDock.Active) return true;
            ChatDock.Recipient(obj);
            return false;
        }
    }

    [HarmonyPatch(typeof(SpyglassContextMenuResolver), "SendPrivate")]
    public static class ChatDockSpyglassPatch
    {
        private static bool Prefix(object contextMenuData)
        {
            if (!ChatDock.Active) return true;
            ChatDock.Recipient(contextMenuData as UserContextMenuData);
            return false;
        }
    }

    [HarmonyPatch(typeof(ContextMenu), "OnItemsReady")]
    internal static class ChatDockMenuPatch
    {
        private static void Postfix() => ChatDock.Dressed();
    }

    [HarmonyPatch(typeof(ContextMenu), "OnDestroy")]
    internal static class ChatDockMenuGonePatch
    {
        private static void Postfix(ContextMenu __instance)
        {
            try
            {
                var slot = AccessTools.Field(typeof(ContextMenu), "_currentInstance");
                if (slot == null || slot.GetValue(null) != null) return;
                foreach (var menu in UnityEngine.Object.FindObjectsOfType<ContextMenu>())
                    if (menu != null && menu != __instance) { slot.SetValue(null, menu); break; }
            }
            catch (Exception e) { Plugin.Trace("[док] учёт меню: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(TextClickHandler), "HandleMenu")]
    internal static class ChatDockLineMenuPatch
    {
        private static bool Prefix(TextClickHandler __instance)
        {
            try
            {
                if (!ChatDock.Active) return true;
                var area = ChatDock.Area;
                if (area == null || !__instance.transform.IsChildOf(area)) return true;
                ChatDock.NameMenu(__instance);
                return false;
            }
            catch (Exception e) { Plugin.Trace("[док] правый клик в чате: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(InputField), "OnFocus")]
    internal static class ChatDockNoSelectPatch
    {
        private static bool Prefix(InputField __instance)
        {
            try
            {
                if (!ChatDock.Ours(__instance)) return true;
                __instance.MoveTextEnd(false);
                return false;
            }
            catch (Exception e) { Plugin.Trace("[док] выделение при фокусе: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(ChatPanelContent), "ConcatenateLines")]
    internal static class ChatDockWindowPatch
    {
        private static bool Prefix(ChatPanelContent __instance, ref string __result)
        {
            string text;
            try { text = ChatDock.Lines(__instance); }
            catch (Exception e) { Plugin.Trace("[док] строки на экране: " + e.Message); return true; }
            if (text == null) return true;
            __result = text;
            return false;
        }
    }

    [HarmonyPatch(typeof(ChatContentHolder), "AddMessage")]
    internal static class ChatDockRoomPatch
    {
        private static readonly AccessTools.FieldRef<ChatContentHolder, List<ITextScrollerContent>> Lines =
            AccessTools.FieldRefAccess<ChatContentHolder, List<ITextScrollerContent>>("_content");
        private static readonly System.Reflection.FieldInfo Added = AccessTools.Field(typeof(ChatContentHolder), "MessageAddedEvent");
        private static readonly System.Reflection.MethodInfo Fire = AccessTools.Method(typeof(ChatContentHolder), "FireContentLinkClick");

        private static bool Prefix(ChatContentHolder __instance, ChatResponseMessage resp)
        {
            if (!ChatDock.Roomy(__instance)) return true;
            ChatContent made;
            try
            {
                var user = DependencyContainer.GetContainer().Resolve<IUserData>();
                if (user == null || !user.LoggedIn || user.UserInfo == null) return false;
                var click = (Action<IChatLinkData>)Delegate.CreateDelegate(typeof(Action<IChatLinkData>), __instance, Fire);
                made = new ChatContent(resp, click);
            }
            catch (Exception e) { Plugin.Trace("[док] длинная лента: " + e.Message); return true; }
            var lines = Lines(__instance);
            lines.Add(made);
            while (lines.Count > ChatDock.Keep) lines.RemoveAt(0);
            (Added?.GetValue(__instance) as Action)?.Invoke();
            return false;
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "OnMapResponse")]
    internal static class ChatDockMapPatch
    {
        private static readonly System.Text.RegularExpressions.Regex MapRe =
            new System.Text.RegularExpressions.Regex("<Map\\b[^>]*>");

        private static void Postfix(object msg)
        {
            try
            {
                var xml = (msg as Transport.Messages.Responses.Locations.LocationResponseMessage)?.LocationXml;
                string head = null;
                if (!string.IsNullOrEmpty(xml))
                {
                    var hit = MapRe.Match(xml);
                    if (hit.Success) head = hit.Value;
                }
                ChatDock.Moved(Number(head, "id"), Number(head, "mapType"));
            }
            catch (Exception e) { Plugin.Trace("[док] смена локации: " + e.Message); }
        }

        private static int Number(string head, string name)
        {
            if (head == null) return -1;
            var hit = System.Text.RegularExpressions.Regex.Match(head, "\\b" + name + "\\s*=\\s*\"(-?\\d+)\"");
            int value;
            return hit.Success && int.TryParse(hit.Groups[1].Value, out value) ? value : -1;
        }
    }
}
