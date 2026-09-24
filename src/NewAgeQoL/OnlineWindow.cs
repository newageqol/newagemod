using System;
using System.Collections.Generic;
using HarmonyLib;
using Transport.Messages.Common.User;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class OnlineWindow
    {
        private const float BarH = 42f;
        private const float PanelW = 420f;
        private const float PanelH = 580f;
        private const float MinH = 260f;
        private const float MinW = 404f;
        private const float TopH = 56f;
        private const float GripH = 14f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Transform _rows;
        private static ScrollRect _scroll;
        private static GameObject _rowPrefab;
        private static UserContextMenuResolver _resolver;
        private static Text _title;
        private static Text _status;
        private static CanvasGroup _rowsFade;
        private static Button _refresh;
        private static InputField _filter;
        private static Text _modeText;
        private static bool _byClan;
        private static string _query = "";
        private static int _seenVersion = -1;
        private static float _pollAt;
        private static float _iconAt;
        private static bool _wantOpen;
        private static float _reopenAt;
        private static Sprite _round16;
        private static Sprite _round8;
        private static readonly List<KeyValuePair<UserRowWidget, OnlinePlayer>> Pending = new List<KeyValuePair<UserRowWidget, OnlinePlayer>>();
        private static readonly Dictionary<string, string> RightsNames = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> ClassByName = BuildClasses();

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            Open();
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        internal static void Open()
        {
            try
            {
                string trouble = OnlineList.SameOne();
                if (trouble != null)
                {
                    Notice.Show(trouble, 7f);
                    Plugin.Warn("[онлайн] " + trouble);
                    return;
                }
                _query = "";
                Build();
                _wantOpen = true;
                _seenVersion = -1;
                if (!OnlineList.Busy) OnlineList.Refresh();
                Rebuild();
            }
            catch (Exception e) { Plugin.Fault("[онлайн] окно: " + e); Close(); }
        }

        private static void Reopen()
        {
            try
            {
                Build();
                _seenVersion = -1;
                Rebuild();
                Plugin.Trace("[онлайн] окно пересоздано после смены сцены");
            }
            catch (Exception e) { Plugin.Trace("[онлайн] пересоздание: " + e.Message); Teardown(); }
        }

        internal static void Close()
        {
            _wantOpen = false;
            Teardown();
        }

        private static void Teardown()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.UserMenuWindow, _canvasGo);
            }
            catch (Exception e)
            {
                Plugin.Trace("[онлайн] закрытие: " + e.Message);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
            _canvasGo = null;
            _panelGo = null;
            _rows = null;
            _rowsFade = null;
            _scroll = null;
            _title = null;
            _status = null;
            _refresh = null;
            _filter = null;
            _modeText = null;
            Pending.Clear();
            OnlineCharms.Forget();
        }

        internal static string KeyHint()
        {
            string spec = Hotkeys.Text(Hotkeys.Of("win:online"));
            return string.IsNullOrEmpty(spec) || spec == "—" ? "" : " (" + spec + ")";
        }

        internal static void Tick()
        {
            if (_wantOpen && _panelGo == null)
            {
                if (Time.unscaledTime < _reopenAt) return;
                _reopenAt = Time.unscaledTime + 0.5f;
                Teardown();
                if (SideButtons.InWorld()) Reopen();
                return;
            }
            if (_canvasGo == null) return;
            OnlineCharms.Tick();
            if (Time.unscaledTime >= _iconAt)
            {
                _iconAt = Time.unscaledTime + 1f;
                RetryIcons();
                OnlineCharms.Retry();
            }
            if (Time.unscaledTime < _pollAt) return;
            _pollAt = Time.unscaledTime + 0.2f;
            int v = OnlineList.Version;
            if (v == _seenVersion) return;
            _seenVersion = v;
            Rebuild();
        }

        private static void Build()
        {
            Teardown();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.UserMenuWindow);
            _canvasGo = canvas.gameObject;

            var holder = VisualPrefabsHolder.Instance.ChatUserListPanelContentPrefab?.GetComponent<ChatUserListPanelContent>();
            _rowPrefab = holder != null ? AccessTools.Field(typeof(ChatUserListPanelContent), "SmallUserRowPrefab")?.GetValue(holder) as GameObject : null;
            if (_rowPrefab == null) throw new Exception("не нашёл префаб строки игрока");
            _resolver = Controllers.Get<UserContextMenuController>().UserContextMenuResolver;

            _panelGo = new GameObject("QoLOnlineWindow", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 1f);
            _byClan = Plugin.CfgOnlineByClan != null && Plugin.CfgOnlineByClan.Value;
            float h, w; Vector2 pos;
            LoadRect(out pos, out h, out w);
            prt.sizeDelta = new Vector2(w, h);
            prt.anchoredPosition = pos;
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = WardrobeLook.Window;
            pimg.sprite = Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -TopH), new Vector2(0f, 0f));
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;
            mover.OnDone = SaveRect;

            var gripGo = new GameObject("grip", typeof(RectTransform), typeof(Image), typeof(DragResize));
            gripGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)gripGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0f), new Vector2(0f, GripH));
            gripGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            var sizer = gripGo.GetComponent<DragResize>();
            sizer.Target = prt;
            sizer.Canvas = canvas;
            sizer.Min = MinH;
            sizer.OnDone = SaveRect;
            var gripMark = Label(gripGo.transform, "• • •", 12, FontStyle.Bold, WardrobeLook.Faint);
            Place(gripMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            gripMark.verticalOverflow = VerticalWrapMode.Overflow;
            gripMark.horizontalOverflow = HorizontalWrapMode.Overflow;
            gripMark.raycastTarget = false;

            var sideGo = new GameObject("gripSide", typeof(RectTransform), typeof(Image), typeof(DragResize));
            sideGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)sideGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-GripH, GripH), new Vector2(0f, -TopH));
            sideGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            var wider = sideGo.GetComponent<DragResize>();
            wider.Target = prt;
            wider.Canvas = canvas;
            wider.Min = 0f;
            wider.MinWide = MinW;
            wider.OnDone = SaveRect;
            var sideMark = Label(sideGo.transform, "•\n•\n•", 12, FontStyle.Bold, WardrobeLook.Faint);
            Place(sideMark.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            _title = Label(_panelGo.transform, "Кто в игре", 20, FontStyle.Bold, WardrobeLook.Bright);
            Place(_title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -45f), new Vector2(184f, -13f));
            _title.raycastTarget = false;
            _title.alignment = TextAnchor.MiddleLeft;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            _title.resizeTextForBestFit = true;
            _title.resizeTextMinSize = 12;
            _title.resizeTextMaxSize = 20;

            var mode = MakeGameButton(_panelGo.transform, "", 118f, BarH, SwitchMode);
            Place((RectTransform)mode.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(192f, -50f), new Vector2(310f, -8f));
            _modeText = mode.GetComponentInChildren<Text>(true);
            if (_modeText != null) _modeText.supportRichText = true;
            PaintMode();

            MakeCloseButton(_panelGo.transform, Close);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -108f), new Vector2(-16f, -66f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _filter = MakeInput(barGo.transform, 168f, "поиск по нику");
            _filter.text = _query;
            _filter.onValueChanged.AddListener(v => { _query = Norm(v); Rebuild(); });
            _refresh = MakeGameButton(barGo.transform, "Обновить", 118f, BarH, () => { if (!OnlineList.Busy) OnlineList.Refresh(); });
            _status = Label(barGo.transform, "", 13, FontStyle.Normal, WardrobeLook.Label);
            _status.alignment = TextAnchor.MiddleLeft;
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 9;
            _status.resizeTextMaxSize = 13;
            var sle = _status.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f; sle.preferredHeight = BarH; sle.minWidth = 70f;

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(14f, GripH + 6f), new Vector2(-30f, -116f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = Rounded(8);
            simg.type = Image.Type.Sliced;
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.scrollSensitivity = 35f;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = Vector2.zero; cont.offsetMax = Vector2.zero;
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 3f;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = cont;
            _scroll.viewport = srt;
            _rows = contentGo.transform;
            _rowsFade = contentGo.AddComponent<CanvasGroup>();

            MakeScrollbar(_panelGo.transform);
        }

        internal static void LoadRect(BepInEx.Configuration.ConfigEntry<string> cfg, float wideBy, float tallBy,
                                     float minW, float minH, out Vector2 pos, out float h, out float w)
        {
            pos = new Vector2(0f, tallBy * 0.5f);
            h = tallBy;
            w = wideBy;
            try
            {
                var parts = (cfg?.Value ?? "").Split(';');
                if (parts.Length < 3) return;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float x, y, hh, ww;
                if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out y)
                    && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, ci, out hh))
                {
                    pos = new Vector2(x, y);
                    h = Mathf.Max(minH, hh);
                }
                if (parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, ci, out ww))
                    w = Mathf.Max(minW, ww);
            }
            catch { }
        }

        internal static void SaveRect(BepInEx.Configuration.ConfigEntry<string> cfg, GameObject panelGo)
        {
            try
            {
                if (panelGo == null || cfg == null) return;
                var rt = (RectTransform)panelGo.transform;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                cfg.Value = rt.anchoredPosition.x.ToString("0", ci) + ";" + rt.anchoredPosition.y.ToString("0", ci)
                            + ";" + rt.sizeDelta.y.ToString("0", ci) + ";" + rt.sizeDelta.x.ToString("0", ci);
            }
            catch { }
        }

        private static void LoadRect(out Vector2 pos, out float h, out float w)
        {
            LoadRect(Plugin.CfgOnlineWindow, PanelW, PanelH, MinW, MinH, out pos, out h, out w);
        }

        private static void SaveRect()
        {
            SaveRect(Plugin.CfgOnlineWindow, _panelGo);
        }

        private static void MakeScrollbar(Transform host)
        {
            var go = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            Place(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-26f, GripH + 6f), new Vector2(-14f, -116f));
            go.GetComponent<Image>().color = WardrobeLook.Field;
            var sb = go.GetComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            var hrt = (RectTransform)handle.transform;
            Place(hrt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handle.GetComponent<Image>().color = WardrobeLook.FieldEdge;
            sb.handleRect = hrt;
            sb.targetGraphic = handle.GetComponent<Image>();
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private static readonly List<OnlinePlayer> Everyone = new List<OnlinePlayer>();
        private static readonly List<OnlinePlayer> Shown = new List<OnlinePlayer>();

        private static void Rebuild()
        {
            if (_rows == null) return;
            var players = Everyone;
            OnlineList.CopyTo(players);
            players.Sort(ByLevel);
            OnlineList.AskClanCodes(players);

            for (int i = _rows.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_rows.GetChild(i).gameObject);
            Pending.Clear();
            OnlineCharms.Clear();

            var shown = Shown;
            shown.Clear();
            foreach (var p in players)
                if (_query.Length == 0 || Key(p).IndexOf(_query, StringComparison.Ordinal) >= 0) shown.Add(p);

            var stack = _rows.GetComponent<VerticalLayoutGroup>();
            if (stack != null) stack.spacing = _byClan ? 14f : 3f;
            if (_byClan) ByClans(shown);
            else foreach (var p in shown) Line(p, _rows);

            bool busy = OnlineList.Busy;
            if (_title != null) _title.text = "Кто в игре" + (players.Count > 0 ? ": " + players.Count : "");
            if (_rowsFade != null) _rowsFade.alpha = busy ? 0.4f : 1f;
            if (_status != null)
            {
                string st = OnlineList.Stamp;
                if (st.Length == 0) st = OnlineList.Status ?? "";
                if (_query.Length > 0) st = "найдено " + shown.Count + (st.Length > 0 ? " · " + st : "");
                _status.text = players.Count == 0 && !OnlineList.Busy && !OnlineList.Configured
                    ? "нет запасного аккаунта"
                    : st;
            }
            if (_refresh != null) _refresh.interactable = !busy;
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static int ByLevel(OnlinePlayer a, OnlinePlayer b)
        {
            int c = b.Level.CompareTo(a.Level);
            return c != 0 ? c : string.Compare(a.Login, b.Login, StringComparison.OrdinalIgnoreCase);
        }

        private static void Line(OnlinePlayer p, Transform host)
        {
            try { AddRow(p, host); }
            catch (Exception e) { Plugin.Trace("[онлайн] строка " + p.Login + ": " + e.Message); }
        }

        private static void ByClans(List<OnlinePlayer> list)
        {
            var order = new List<string>();
            var groups = new Dictionary<string, List<OnlinePlayer>>();
            foreach (var p in list)
            {
                string key = p.Clan ?? "";
                List<OnlinePlayer> one;
                if (!groups.TryGetValue(key, out one)) { one = new List<OnlinePlayer>(); groups[key] = one; order.Add(key); }
                one.Add(p);
            }
            order.Sort((a, b) =>
            {
                if (a.Length == 0 || b.Length == 0) return a.Length == b.Length ? 0 : (a.Length == 0 ? 1 : -1);
                int c = groups[b].Count.CompareTo(groups[a].Count);
                return c != 0 ? c : string.Compare(ClanTitle(a), ClanTitle(b), StringComparison.OrdinalIgnoreCase);
            });
            foreach (var key in order)
            {
                var one = groups[key];
                one.Sort(ByLevel);
                var host = AddGroup(ClanTitle(key), one.Count, key.Length == 0);
                foreach (var p in one) Line(p, host);
            }
        }

        private static string ClanTitle(string icon)
        {
            if (string.IsNullOrEmpty(icon)) return "Без клана";
            string name = OnlineList.ClanName(icon);
            return name.Length > 0 ? name : icon;
        }

        private static Transform AddGroup(string name, int count, bool loose)
        {
            Color tone = loose ? WardrobeLook.Label : WardrobeLook.Accent;
            var go = new GameObject("QoLClan", typeof(RectTransform), typeof(VerticalLayoutGroup));
            go.transform.SetParent(_rows, false);
            Stack(go.GetComponent<VerticalLayoutGroup>(), new RectOffset(0, 0, 0, 0), 6f);

            var headGo = new GameObject("head", typeof(RectTransform), typeof(LayoutElement));
            headGo.transform.SetParent(go.transform, false);
            var le = headGo.GetComponent<LayoutElement>();
            le.preferredHeight = 30f; le.minHeight = 30f; le.flexibleWidth = 1f;

            var t = Label(headGo.transform, name, 17, FontStyle.Bold, tone);
            t.alignment = TextAnchor.MiddleLeft;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 2f), new Vector2(-56f, 0f));

            var n = Label(headGo.transform, count.ToString(), 15, FontStyle.Bold, WardrobeLook.Faint);
            n.alignment = TextAnchor.MiddleRight;
            n.raycastTarget = false;
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(n.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-52f, 2f), new Vector2(-6f, 0f));

            var lineGo = new GameObject("line", typeof(RectTransform), typeof(Image));
            lineGo.transform.SetParent(headGo.transform, false);
            Place((RectTransform)lineGo.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(2f, 0f), new Vector2(-2f, 1f));
            var line = lineGo.GetComponent<Image>();
            line.color = new Color(tone.r, tone.g, tone.b, 0.35f);
            line.raycastTarget = false;

            var bodyGo = new GameObject("rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            bodyGo.transform.SetParent(go.transform, false);
            Stack(bodyGo.GetComponent<VerticalLayoutGroup>(), new RectOffset(16, 0, 0, 0), 3f);

            var railGo = new GameObject("rail", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            railGo.transform.SetParent(bodyGo.transform, false);
            railGo.GetComponent<LayoutElement>().ignoreLayout = true;
            Place((RectTransform)railGo.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(5f, 2f), new Vector2(7f, -2f));
            var rail = railGo.GetComponent<Image>();
            rail.color = new Color(tone.r, tone.g, tone.b, 0.35f);
            rail.raycastTarget = false;
            return bodyGo.transform;
        }

        private static void Stack(VerticalLayoutGroup vlg, RectOffset padding, float spacing)
        {
            vlg.padding = padding;
            vlg.spacing = spacing;
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        private static void PaintMode()
        {
            if (_modeText == null) return;
            _modeText.text = _byClan
                ? "Кланы: <color=#e2b85c>вкл</color>"
                : "Кланы: <color=#747b85>выкл</color>";
        }

        private static void SwitchMode()
        {
            _byClan = !_byClan;
            if (Plugin.CfgOnlineByClan != null) Plugin.CfgOnlineByClan.Value = _byClan;
            PaintMode();
            Rebuild();
        }

        private static void AddRow(OnlinePlayer p, Transform host)
        {
            var go = UnityEngine.Object.Instantiate(_rowPrefab, host, false);
            Clones.StripHotkeys(go, _rowPrefab);
            go.name = "QoLRow";
            go.SetActive(true);
            var rt = (RectTransform)go.transform;
            float h = rt.rect.height > 4f ? rt.rect.height : (rt.sizeDelta.y > 4f ? rt.sizeDelta.y : 34f);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h; le.flexibleWidth = 1f;

            var w = go.GetComponent<UserRowWidget>();
            if (w == null) throw new Exception("в префабе нет UserRowWidget");
            var msg = Row(p);
            bool drawn = !msg.ClanIconCode.HasValue || ClanArt(msg.ClanIconCode.Value) != null;
            w.Data = msg;
            Flatten(w, p.Away);
            MarkAway(w, p.Away);
            OnlineCharms.Decorate(w, p, (RectTransform)_panelGo.transform);
            var resolver = _resolver;
            w.OnItemClickDelegate = item => { try { ChatDock.WriteTo(p.Id, p.Login); } catch (Exception e) { Plugin.Trace("[онлайн] клик: " + e.Message); } };
            w.OnRightButtonClickDelegate = item =>
            {
                try { ContextMenu.ShowContextMenu(go, resolver, item.Data, EContextMenuSide.Left); }
                catch (Exception e) { Plugin.Trace("[онлайн] меню: " + e.Message); }
            };
            if (!drawn) Pending.Add(new KeyValuePair<UserRowWidget, OnlinePlayer>(w, p));
        }

        private static Sprite ClanArt(int code)
        {
            try
            {
                var s = ClanIconCodeMaker.GetIcon(code);
                if (s != null && s.texture == null)
                {
                    var made = AccessTools.Field(typeof(ClanIconCodeMaker), "GeneratedIcons")?.GetValue(null) as Dictionary<int, Sprite>;
                    if (made != null && made.Remove(code)) s = ClanIconCodeMaker.GetIcon(code);
                }
                return Quickslots.Faded(s) ? null : s;
            }
            catch { return null; }
        }

        private static void RetryIcons()
        {
            if (Pending.Count == 0) return;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var w = Pending[i].Key;
                var p = Pending[i].Value;
                if (w == null) { Pending.RemoveAt(i); continue; }
                int code;
                if (!OnlineList.ClanCode(p.Clan, out code)) { Pending.RemoveAt(i); continue; }
                if (ClanArt(code) == null) continue;
                try { w.Data = Row(p); Flatten(w, p.Away); MarkAway(w, p.Away); } catch { }
                Pending.RemoveAt(i);
            }
        }

        internal static void MakeCloseButton(Transform host, Action onClose)
        {
            GameObject go = null;
            try
            {
                var proto = VisualPrefabsHolder.Instance.SummonPlayerDialog?.GetComponent<SummonPlayerDialog>()?.CloseButton;
                if (proto != null)
                {
                    go = UnityEngine.Object.Instantiate(proto.gameObject, host, false);
                    Clones.StripHotkeys(go, proto.gameObject);
                    go.name = "QoLClose";
                    var rt = (RectTransform)go.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(14f, 14f);
                    rt.localScale = Vector3.one * 0.8f;
                    var b = go.GetComponent<Button>();
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(() => onClose());
                    go.SetActive(true);
                }
            }
            catch (Exception e) { Plugin.Trace("[онлайн] крестик игры не взялся: " + e.Message); if (go != null) UnityEngine.Object.Destroy(go); go = null; }
            if (go != null) return;

            var closeGo = new GameObject("close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeGo.transform.SetParent(host, false);
            var crt = (RectTransform)closeGo.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(1f, 1f);
            crt.sizeDelta = new Vector2(34f, 34f); crt.anchoredPosition = new Vector2(-8f, -8f);
            closeGo.GetComponent<Image>().color = WardrobeLook.Danger;
            closeGo.GetComponent<Button>().onClick.AddListener(() => onClose());
            var x = Label(closeGo.transform, "X", 20, FontStyle.Bold, Color.white);
            Place(x.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        internal static Button MakeGameButton(Transform host, string text, float width, float height, Action onClick)
        {
            var go = new GameObject("QoLRefresh", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var image = go.GetComponent<Image>();
            image.sprite = Rounded(8);
            image.type = Image.Type.Sliced;
            image.color = WardrobeLook.Button;
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = height; le.minHeight = height;

            var t = Label(go.transform, text, 15, FontStyle.Bold, WardrobeLook.Bright);
            float padX = Mathf.Min(14f, width * 0.1f);
            float padY = Mathf.Min(6f, height * 0.14f);
            Place(t.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(padX, padY), new Vector2(-padX, -padY));
            t.alignment = TextAnchor.MiddleCenter;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 10;
            t.resizeTextMaxSize = 15;

            var b = go.GetComponent<Button>();
            b.targetGraphic = image;
            var colors = b.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            return b;
        }

        private static void Flatten(UserRowWidget widget, bool away)
        {
            try
            {
                var back = widget.GetComponent<Image>();
                if (back != null)
                {
                    back.sprite = Rounded(8);
                    back.type = Image.Type.Sliced;
                    back.color = WardrobeLook.Tab;
                }
                var pick = AccessTools.Field(typeof(UserRowWidget), "SelectionButton")?.GetValue(widget) as Button;
                var face = pick != null ? pick.GetComponent<Image>() : null;
                if (face != null && face != back)
                {
                    face.sprite = Rounded(8);
                    face.type = Image.Type.Sliced;
                    face.color = WardrobeLook.Tab;
                }
                var login = AccessTools.Field(typeof(UserRowWidget), "LoginText")?.GetValue(widget) as Text;
                if (login != null) login.color = away ? WardrobeLook.Faint : WardrobeLook.Bright;
                var level = AccessTools.Field(typeof(UserRowWidget), "LevelText")?.GetValue(widget) as Text;
                if (level != null) level.color = WardrobeLook.Faint;
                var clan = AccessTools.Field(typeof(UserRowWidget), "ClanIconImage")?.GetValue(widget) as Image;
                if (clan != null) clan.enabled = !Quickslots.Faded(clan.sprite);
            }
            catch (Exception e) { Plugin.Trace("[онлайн] оформление строки: " + e.Message); }
        }

        internal static void MarkAway(UserRowWidget widget, bool away)
        {
            if (widget == null || !away) return;
            try
            {
                var fade = widget.GetComponent<CanvasGroup>();
                if (fade == null) fade = widget.gameObject.AddComponent<CanvasGroup>();
                fade.alpha = 0.42f;
                fade.blocksRaycasts = true;
                fade.interactable = true;
                var label = AccessTools.Field(typeof(UserRowWidget), "LoginText")?.GetValue(widget) as Text;
                if (label != null) label.color = new Color32(150, 150, 150, 255);
            }
            catch (Exception e) { Plugin.Trace("[онлайн] отметка афк: " + e.Message); }
        }

        internal static void Extras(UserRowInfoMessage row)
        {
            try
            {
                if (row == null || string.IsNullOrEmpty(row.Login) || !OnlineList.Configured) return;
                var p = OnlineList.ByLogin(row.Login);
                if (p == null) return;
                var full = Row(p);
                if (!row.ClassId.HasValue) row.ClassId = full.ClassId;
                if (row.ClanIcon == null && !row.ClanIconCode.HasValue)
                {
                    row.ClanIcon = full.ClanIcon;
                    row.ClanIconCode = full.ClanIconCode;
                }
                if (!row.Rank.HasValue) row.Rank = full.Rank;
                row.RightsIcon = full.RightsIcon;
                row.Vip = full.Vip;
                row.Dealer = full.Dealer;
            }
            catch (Exception e) { Plugin.Trace("[онлайн] добавки к строке: " + e.Message); }
        }

        private static readonly Dictionary<string, KeyValuePair<bool, float>> Atlas = new Dictionary<string, KeyValuePair<bool, float>>();

        private static bool InAtlas(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return false;
            KeyValuePair<bool, float> known;
            if (Atlas.TryGetValue(sprite, out known) && (known.Key || Time.unscaledTime - known.Value < 30f)) return known.Key;
            bool found = false;
            try
            {
                var s = AtlasUtils.GetUserRowIcon(sprite);
                found = s != null && s.name != "unknown";
            }
            catch { }
            Atlas[sprite] = new KeyValuePair<bool, float>(found, Time.unscaledTime);
            return found;
        }

        private static UserRowInfoMessage Row(OnlinePlayer p)
        {
            var m = new UserRowInfoMessage(p.Id, p.Login, p.Level);
            int code;
            string sprite;
            if (OnlineList.ClanSprite(p.Clan, out sprite) && (InAtlas(sprite) || !ClanPics.Missing(sprite))) m.ClanIcon = sprite;
            else if (OnlineList.ClanCode(p.Clan, out code)) m.ClanIconCode = code;
            m.RightsIcon = RightsSprite(p.Rights);
            if (p.Vip) m.Vip = true;
            if (p.Dealer) m.Dealer = true;
            if (p.Rank > 0) m.Rank = p.Rank;
            if (p.Away) m.IsAway = true;
            int cid;
            if (ClassByName.TryGetValue(Norm(p.Class), out cid)) m.ClassId = cid;
            return m;
        }

        private static string RightsSprite(string rights)
        {
            if (string.IsNullOrEmpty(rights)) return null;
            string found;
            if (RightsNames.TryGetValue(rights, out found)) return found;
            found = null;
            foreach (var name in new[] { rights, "AdminIcon", "ModerIcon", "ModeratorIcon", "OperatorIcon", "OpIcon", "HelperIcon", "InfoIcon", "SupportIcon" })
            {
                try
                {
                    var s = AtlasUtils.GetUserRowIcon(name);
                    if (s != null && s.name != "unknown") { found = name; break; }
                }
                catch { }
            }
            RightsNames[rights] = found;
            Plugin.Trace("[онлайн] значок прав «" + rights + "» → " + (found ?? "нет"));
            return found;
        }

        private static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');

        private static string Key(OnlinePlayer p)
        {
            if (p.Search == null) p.Search = Norm(p.Login);
            return p.Search;
        }

        private static Dictionary<string, int> BuildClasses()
        {
            var d = new Dictionary<string, int>();
            string[][] names =
            {
                new[] { "рейнджер", "стрелок", "лучник", "егерь", "снайпер", "древний", "завоеватель" },
                new[] { "варвар", "рубака", "гладиатор", "берсерк", "вождь", "жнец", "атаман" },
                new[] { "мастер щита", "щитоносец", "легионер", "центурион", "чемпион", "монолит", "полководец" },
                new[] { "джаггернаут", "защитник", "гвардеец", "рыцарь", "кавалер", "титан", "исполин" },
                new[] { "жрец", "священник", "клирик", "крестоносец", "паладин", "епископ", "кардинал" },
                new[] { "маг", "посвященный", "аколит", "адепт", "магистр", "патриарх", "властелин" },
            };
            for (int i = 0; i < names.Length; i++)
                foreach (var n in names[i]) d[n] = i + 1;
            return d;
        }

        internal static InputField MakeInput(Transform host, float width, string placeholder)
        {
            var go = new GameObject("filter", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(host, false);
            var img = go.GetComponent<Image>();
            img.color = WardrobeLook.Field;
            img.sprite = Rounded(8);
            img.type = Image.Type.Sliced;
            var ol = go.GetComponent<Outline>();
            ol.effectColor = WardrobeLook.FieldEdge;
            ol.effectDistance = new Vector2(1f, -1f);
            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = width; le.minWidth = width; le.preferredHeight = BarH; le.minHeight = BarH;

            var txt = Label(go.transform, "", 16, FontStyle.Normal, WardrobeLook.Bright);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.supportRichText = false;
            Place(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));

            var ph = Label(go.transform, placeholder, 16, FontStyle.Normal, WardrobeLook.Faint);
            ph.alignment = TextAnchor.MiddleLeft;
            Place(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10f, 4f), new Vector2(-10f, -4f));

            var input = go.GetComponent<InputField>();
            input.targetGraphic = img;
            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine;
            input.caretColor = WardrobeLook.Bright;
            input.customCaretColor = true;
            input.selectionColor = new Color(WardrobeLook.Accent.r, WardrobeLook.Accent.g, WardrobeLook.Accent.b, 0.35f);
            return input;
        }

        private static Sprite _disc, _ring;

        internal static Sprite Disc()
        {
            if (_disc != null && _disc.texture != null) return _disc;
            _disc = Circle(96, 0);
            return _disc;
        }

        internal static Sprite Ring()
        {
            if (_ring != null && _ring.texture != null) return _ring;
            _ring = Circle(96, 3);
            return _ring;
        }

        private static Sprite Circle(int size, int thick)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float middle = (size - 1) * 0.5f;
            float outer = size * 0.5f - 1f;
            float inner = thick > 0 ? outer - thick : 0f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - middle, dy = y - middle;
                    float far = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(outer - far);
                    if (thick > 0) a = Mathf.Min(a, Mathf.Clamp01(far - inner));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private static readonly System.Collections.Generic.Dictionary<int, Sprite> Rounds =
            new System.Collections.Generic.Dictionary<int, Sprite>();

        internal static Sprite RoundedExact(int radius)
        {
            Sprite got;
            if (Rounds.TryGetValue(radius, out got) && got != null && got.texture != null) return got;
            got = Bake(radius);
            Rounds[radius] = got;
            return got;
        }

        internal static Sprite Rounded(int radius)
        {
            if (radius >= 12 && _round16 != null && _round16.texture != null) return _round16;
            if (radius < 12 && _round8 != null && _round8.texture != null) return _round8;
            var made = Bake(radius);
            if (radius >= 12) _round16 = made; else _round8 = made;
            return made;
        }

        private static Sprite Bake(int radius)
        {
            int size = radius * 2 + 8;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x < radius ? radius - x - 0.5f : (x >= size - radius ? x - (size - radius) + 0.5f : 0f);
                    float dy = y < radius ? radius - y - 0.5f : (y >= size - radius ? y - (size - radius) + 0.5f : 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d <= radius - 1f ? 1f : (d >= radius ? 0f : radius - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        internal static void Place(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot, Vector2 offMin, Vector2 offMax)
        {
            rt.anchorMin = min; rt.anchorMax = max; rt.pivot = pivot;
            rt.offsetMin = offMin; rt.offsetMax = offMax;
        }

        internal static Text Label(Transform host, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var t = go.GetComponent<Text>();
            t.font = FlaskPicker.Font();
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.text = text;
            return t;
        }
    }

    internal sealed class DragMove : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        internal RectTransform Target;
        internal Canvas Canvas;
        internal Action OnDone;

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            float s = Canvas != null && Canvas.scaleFactor > 0f ? Canvas.scaleFactor : 1f;
            var p = Target.anchoredPosition + e.delta / s;
            var area = Canvas != null ? (RectTransform)Canvas.transform : null;
            if (area != null)
            {
                float hw = area.rect.width * 0.5f, hh = area.rect.height * 0.5f;
                p.x = Mathf.Clamp(p.x, -hw + Target.rect.width * 0.5f, hw - Target.rect.width * 0.5f);
                p.y = Mathf.Clamp(p.y, -hh + 40f, hh);
            }
            Target.anchoredPosition = p;
        }

        public void OnEndDrag(PointerEventData e) => OnDone?.Invoke();
    }

    internal sealed class DragResize : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        internal RectTransform Target;
        internal Canvas Canvas;
        internal float Min = 200f;
        internal float MinWide;
        internal Action OnDone;

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            float s = Canvas != null && Canvas.scaleFactor > 0f ? Canvas.scaleFactor : 1f;
            var area = Canvas != null ? (RectTransform)Canvas.transform : null;
            var size = Target.sizeDelta;
            if (Min > 0f)
            {
                float max = area != null ? area.rect.height - 20f : 2000f;
                size.y = Mathf.Clamp(size.y - e.delta.y / s, Min, max);
            }
            if (MinWide > 0f)
            {
                float max = area != null ? area.rect.width - 20f : 3000f;
                float wide = Mathf.Clamp(size.x + e.delta.x / s, MinWide, Mathf.Max(MinWide, max));
                Target.anchoredPosition += new Vector2((wide - size.x) * 0.5f, 0f);
                size.x = wide;
            }
            Target.sizeDelta = size;
        }

        public void OnEndDrag(PointerEventData e) => OnDone?.Invoke();
    }
}
