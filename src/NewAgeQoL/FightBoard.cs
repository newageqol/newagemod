using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class FightBoard
    {
        private const string Server = "https://newage-observers.outerlab.org/v1/fights";
        private const string Mark = "newage-observers 1";
        private const float RowW = 270f;
        private const float RowH = 36f;
        private const float TimeW = 52f;
        private const float Gap = 3f;
        private const float Pad = 6f;
        private const int Most = 10;
        private const float OpenEvery = 3f;
        private const float FoldedEvery = 15f;

        private sealed class Item
        {
            internal int Room;
            internal int Id;
            internal bool Fight;
            internal string Levels;
            internal int Players;
            internal int Seconds;
            internal bool Timed;
            internal string Text;
            internal float At;
        }

        private sealed class Line
        {
            internal Item It;
            internal Text Time;
        }

        private static GameObject _canvasGo;
        private static SideFold _fold;
        private static RectTransform _panel;
        private static Text _note;
        private static readonly List<GameObject> Rows = new List<GameObject>();
        private static readonly List<Line> Lines = new List<Line>();
        private static List<Item> _items;
        private static string _error;
        private static string _drawn = "";
        private static bool _busy;
        private static float _askAt;
        private static float _paintAt;
        private static float _top;

        internal static float Bottom => _canvasGo != null && _canvasGo.activeInHierarchy && _fold != null ? _top + _fold.Height : 0f;

        internal static bool Under() => _fold != null && _canvasGo != null && _canvasGo.activeInHierarchy && _fold.Under();

        internal static void Tick()
        {
            try
            {
                bool want = SideButtons.InWorld() && !SideButtons.InCombat() && ChatDock.Active;
                if (!want) { Drop(); return; }
                bool hide = Roster.Shopping();
                if (_canvasGo == null) Build();
                if (_canvasGo.activeSelf == hide) _canvasGo.SetActive(!hide);
                if (hide) return;

                float now = Time.unscaledTime;
                if (!_busy && now >= _askAt && Plugin.Instance != null)
                {
                    _busy = true;
                    Plugin.Instance.StartCoroutine(Fetch());
                }
                Place();
                if (now < _paintAt) return;
                _paintAt = now + 0.25f;
                Fill();
                Clocks();
            }
            catch (Exception e) { Plugin.Trace("[fights] " + e.Message); Drop(); }
        }

        private static IEnumerator Fetch()
        {
            var req = UnityWebRequest.Get(Server);
            req.timeout = 10;
            req.redirectLimit = 0;
            req.SetRequestHeader("User-Agent", "NewAgeQoL");
            yield return req.SendWebRequest();
            long code = req.responseCode;
            string body = req.downloadHandler == null ? "" : req.downloadHandler.text ?? "";
            req.Dispose();
            try { Take(code, body); }
            catch (Exception e) { _error = "ответ не разобран"; _items = null; Plugin.Trace("[fights] answer: " + e.Message); }
            _busy = false;
            _askAt = Time.unscaledTime + (_fold != null && _fold.Folded ? FoldedEvery : OpenEvery);
        }

        private static void Take(long code, string body)
        {
            if (!body.StartsWith(Mark, StringComparison.Ordinal))
            {
                _error = code == 0 ? "сервер наблюдателей недоступен" : "сервер наблюдателей ответил " + code;
                _items = null;
                return;
            }
            float at = Time.unscaledTime;
            var items = new List<Item>();
            var down = new List<int>();
            foreach (var raw in body.Split('\n'))
            {
                var bits = raw.TrimEnd('\r').Split('\t');
                if (bits.Length >= 2 && bits[0] == "!") { _error = bits[1]; _items = null; return; }
                if (bits.Length >= 3 && bits[0] == "r")
                {
                    if (bits[2] != "1") down.Add(Num(bits[1]));
                    continue;
                }
                if (bits.Length >= 10 && bits[0] == "c")
                    items.Add(new Item
                    {
                        Room = Num(bits[1]), Id = Num(bits[2]), Levels = Levels(bits[3], bits[4]), Players = Num(bits[6]),
                        Seconds = Num(bits[8]), Timed = bits[8].Length > 0, Text = bits[9], At = at,
                    });
                else if (bits.Length >= 8 && bits[0] == "f")
                    items.Add(new Item
                    {
                        Room = Num(bits[1]), Id = Num(bits[2]), Fight = true, Levels = Levels(bits[3], bits[4]), Players = Num(bits[5]),
                        Seconds = Num(bits[6]), Timed = bits[6].Length > 0, Text = bits[7], At = at,
                    });
            }
            items.Sort((a, b) => a.Fight != b.Fight ? (a.Fight ? 1 : -1) : a.Seconds.CompareTo(b.Seconds));
            _items = items;
            _error = down.Count == 3 ? "наблюдатели не в сети" : null;
        }

        private static int Num(string s)
        {
            int n;
            return int.TryParse(s, out n) ? n : 0;
        }

        private static string Levels(string low, string high)
        {
            if (low.Length == 0 && high.Length == 0) return "";
            return low == high ? low : low + "–" + high;
        }

        private static string Kind(int room) => room == 14 ? "Хаот" : room == 19 ? "Командный" : room == 334 ? "Турнир" : "Бой";

        private static void Build()
        {
            _canvasGo = new GameObject("QoLFightBoard", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Curtain.Stage(_canvasGo);
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 251;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            _fold = new SideFold(_canvasGo.transform, "fights", "Бои", Plugin.CfgFightsFolded);
            _panel = _fold.Body;
            _panel.sizeDelta = new Vector2(RowW, RowH);
            var back = _panel.gameObject.AddComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;

            _note = OnlineWindow.Label(_panel, "", 12, FontStyle.Normal, WardrobeLook.Faint);
            _note.alignment = TextAnchor.MiddleLeft;
            _note.raycastTarget = false;
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;
            _drawn = "";
            _askAt = 0f;
            _paintAt = 0f;
            Plugin.Trace("[fights] board built under the character column");
        }

        private static void Place()
        {
            float wide = HelpColumn.Wide;
            float x = wide > 0f ? -(wide + 8f) : -HelpColumn.SideGap;
            float below = Roster.Bottom;
            float y = below > 0f ? below + 10f : HelpColumn.Head;
            _top = y;
            _fold.Lay(new Vector2(x, -y), _panel.sizeDelta);
        }

        private static void Fill()
        {
            var items = _items;
            int count = items == null ? 0 : items.Count;
            _fold.Title(count > 0 ? "Бои  " + count : "Бои");

            var sig = new StringBuilder();
            sig.Append(_error ?? "").Append('|').Append(items == null ? "-" : "+").Append('|');
            if (items != null)
                foreach (var it in items) sig.Append(it.Room).Append(':').Append(it.Id).Append(':').Append(it.Fight ? 1 : 0).Append(':').Append(it.Players).Append(':').Append(it.Levels).Append(':').Append(it.Text).Append(';');
            string now = sig.ToString();
            if (now == _drawn)
            {
                if (items != null)
                    for (int i = 0; i < Lines.Count && i < items.Count; i++) Lines[i].It = items[i];
                return;
            }
            _drawn = now;

            foreach (var row in Rows) if (row != null) UnityEngine.Object.Destroy(row);
            Rows.Clear();
            Lines.Clear();

            string empty = items == null ? (_error ?? "спрашиваю наблюдателей…")
                : count == 0 ? (_error ?? "сейчас ни заявок, ни боёв") : null;
            if (empty != null)
            {
                _note.gameObject.SetActive(true);
                _note.text = empty;
                OnlineWindow.Place(_note.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 0f), new Vector2(-8f, 0f));
                _panel.sizeDelta = new Vector2(RowW, RowH + Pad * 2f);
                return;
            }
            _note.gameObject.SetActive(false);
            int shown = Mathf.Min(Most, count);
            float y = -Pad;
            for (int i = 0; i < shown; i++)
            {
                Rows.Add(Row(items[i], y));
                y -= RowH + Gap;
            }
            if (count > shown)
            {
                var more = OnlineWindow.Label(_panel, "и ещё " + (count - shown), 11, FontStyle.Normal, WardrobeLook.Faint);
                more.raycastTarget = false;
                more.alignment = TextAnchor.MiddleLeft;
                var mrt = more.rectTransform;
                mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 1f);
                mrt.pivot = new Vector2(0f, 1f);
                mrt.sizeDelta = new Vector2(RowW - Pad * 2f, 16f);
                mrt.anchoredPosition = new Vector2(Pad + 4f, y);
                Rows.Add(more.gameObject);
                y -= 16f + Gap;
            }
            _panel.sizeDelta = new Vector2(RowW, -y - Gap + Pad);
        }

        private static GameObject Row(Item it, float y)
        {
            var go = new GameObject("QoLFightRow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(RowW - Pad * 2f, RowH);
            rt.anchoredPosition = new Vector2(0f, y);
            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            back.color = it.Fight ? WardrobeLook.Card : WardrobeLook.Tab;

            string tint = it.Room == 14 ? "#e2b85c" : it.Room == 19 ? "#8fb8e8" : "#d79be0";
            float textW = RowW - Pad * 2f - 7f - TimeW - 10f;
            var name = OnlineWindow.Label(go.transform, "", 12, FontStyle.Normal, WardrobeLook.Body);
            name.alignment = TextAnchor.MiddleLeft;
            name.raycastTarget = false;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            OnlineWindow.Place(name.rectTransform, new Vector2(0f, 0.5f), Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(7f, -1f), new Vector2(-(TimeW + 10f), -2f));
            Fit(name, Plain(it.Text.Length > 0 ? it.Text : Kind(it.Room)), textW);

            var meta = OnlineWindow.Label(go.transform, "", 11, FontStyle.Normal, WardrobeLook.Faint);
            meta.supportRichText = true;
            meta.alignment = TextAnchor.MiddleLeft;
            meta.raycastTarget = false;
            meta.horizontalOverflow = HorizontalWrapMode.Overflow;
            OnlineWindow.Place(meta.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(7f, 2f), new Vector2(-(TimeW + 10f), 1f));
            Squeeze(meta, "<color=" + tint + ">" + Kind(it.Room) + "</color>", it.Levels, it.Players, textW);

            var time = OnlineWindow.Label(go.transform, "", 12, FontStyle.Bold, it.Fight ? WardrobeLook.Good : WardrobeLook.Accent);
            time.alignment = TextAnchor.MiddleRight;
            time.raycastTarget = false;
            time.horizontalOverflow = HorizontalWrapMode.Overflow;
            OnlineWindow.Place(time.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(-(TimeW + 7f), 0f), new Vector2(-7f, 0f));
            Lines.Add(new Line { It = it, Time = time });

            var hover = go.AddComponent<RosterHover>();
            hover.Id = it.Id;
            hover.Fight = it.Fight;
            hover.Room = it.Room;
            return go;
        }

        private static void Clocks()
        {
            float now = Time.unscaledTime;
            foreach (var line in Lines)
            {
                if (line.Time == null) continue;
                var it = line.It;
                string text;
                if (!it.Timed) text = it.Fight ? "идёт" : "";
                else if (it.Fight) text = Clock(it.Seconds + (int)(now - it.At));
                else
                {
                    int left = it.Seconds - (int)(now - it.At);
                    text = left > 0 ? Clock(left) : "старт";
                }
                if (line.Time.text != text) line.Time.text = text;
            }
        }

        private static string Clock(int s)
        {
            s = Math.Max(0, s);
            return s >= 3600 ? (s / 3600) + ":" + (s % 3600 / 60).ToString("00") + ":" + (s % 60).ToString("00") : (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        private static void Fit(Text label, string text, float width)
        {
            label.text = text;
            if (label.preferredWidth <= width) return;
            int keep = text.Length;
            while (keep > 1)
            {
                keep--;
                label.text = text.Substring(0, keep).TrimEnd() + "…";
                if (label.preferredWidth <= width) return;
            }
        }

        private static void Squeeze(Text label, string kind, string levels, int players, float width)
        {
            var tries = new[]
            {
                Meta(kind, levels, players, "  ·  ", "ур. ", Players(players)),
                Meta(kind, levels, players, " · ", "ур. ", Players(players)),
                Meta(kind, levels, players, " · ", "", Players(players)),
                Meta(kind, levels, players, " · ", "", players + " игр."),
                Meta(kind, levels, 0, " · ", "", ""),
                kind,
            };
            foreach (var text in tries)
            {
                label.text = text;
                if (label.preferredWidth <= width) return;
            }
        }

        private static string Meta(string kind, string levels, int players, string dot, string lv, string count)
        {
            return kind + (levels.Length > 0 ? dot + lv + levels : "") + (players > 0 ? dot + count : "");
        }

        private static string Players(int n)
        {
            int tail = n % 100;
            string word = tail >= 11 && tail <= 14 ? "игроков" : n % 10 == 1 ? "игрок" : n % 10 >= 2 && n % 10 <= 4 ? "игрока" : "игроков";
            return n + " " + word;
        }

        private static string Plain(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        private static void Drop()
        {
            if (_canvasGo == null) return;
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _fold = null;
            _panel = null;
            _note = null;
            Rows.Clear();
            Lines.Clear();
            _drawn = "";
        }
    }
}
