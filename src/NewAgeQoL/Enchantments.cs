using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Enchantments
    {
        private const float Side = 32f;
        private const float Step = 38f;
        private const float Pad = 5f;
        private const float TextH = 13f;
        private const float Again = 600f;
        private const float Linger = 30f;
        private const int Hint = 377;
        private const int Change = 376;

        private sealed class Cell
        {
            internal int Id;
            internal GameObject Go;
            internal Image Icon;
            internal Image Frame;
            internal Text Rest;
        }

        private sealed class Known
        {
            internal double At;
            internal readonly List<long> Spans = new List<long>();
            internal string Body = "";
        }

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static readonly List<Cell> Cells = new List<Cell>();
        private static readonly Dictionary<int, Known> Times = new Dictionary<int, Known>();
        private sealed class Owed
        {
            internal int Count;
            internal double At;
        }

        private static readonly Dictionary<int, Owed> Waiting = new Dictionary<int, Owed>();
        private static readonly Dictionary<int, double> AskAt = new Dictionary<int, double>();
        private static object _on;
        private static string _sig = "";
        private static float _next;
        private static float _sendAt;
        private static double _heldAt;
        private static GameObject _tipGo;
        private static Text _tipText;
        private static int _tipFor = -1;


        private static int _who;

        internal static void Wake()
        {
            _next = 0f;
            _sig = "";
        }

        private static void Owner()
        {
            int me = Me();
            if (me <= 0 || me == _who) return;
            _who = me;
            Times.Clear();
            AskAt.Clear();
            Waiting.Clear();
        }

        internal static void Tick()
        {
            try
            {
                if (!Flowing()) _heldAt = RealTime.Now;
                if (!SideButtons.InWorld() || SideButtons.InCombat()) { Hide(); return; }
                Cover();
                Listen();
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.25f;

                Owner();
                var list = Mine();
                if (list == null || list.Count == 0) { Hide(); return; }

                var sb = new StringBuilder();
                foreach (var one in list) if (one != null) sb.Append(one.Id).Append(',');
                string sig = sb.ToString();

                if (_canvasGo == null) Build();
                if (_root == null) return;
                if (sig != _sig) { _sig = sig; Fill(list); }
                if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);

                Stale();
                Refresh();
                Stand();
                Tip();
            }
            catch (Exception e) { Plugin.Trace("[состояния] " + e.Message); }
        }

        private static CanvasGroup _hid;
        private static float _hidAt;

        private static void Cover()
        {
            if (_hid != null) { Blank(_hid); return; }
            if (Time.unscaledTime < _hidAt) return;
            _hidAt = Time.unscaledTime + 1f;
            try
            {
                var panel = UnityEngine.Object.FindObjectOfType<EnchantmentsPanel>();
                if (panel == null) return;
                var host = panel.transform.parent;
                var go = host != null && host.name.IndexOf("Enchantment", StringComparison.OrdinalIgnoreCase) >= 0
                    ? host.gameObject : panel.gameObject;
                _hid = go.GetComponent<CanvasGroup>();
                if (_hid == null) _hid = go.AddComponent<CanvasGroup>();
                Blank(_hid);
                Plugin.Trace("[состояния] игровой столбец закрыт: " + go.name);
            }
            catch (Exception e) { Plugin.Trace("[состояния] игровой столбец: " + e.Message); }
        }

        private static void Blank(CanvasGroup veil)
        {
            if (veil.alpha != 0f) veil.alpha = 0f;
            if (veil.blocksRaycasts) veil.blocksRaycasts = false;
            if (veil.interactable) veil.interactable = false;
        }

        private static List<IEnchantmentData> Mine()
        {
            try { return Controllers.User?.GlobalEnchantments?.Content; }
            catch { return null; }
        }

        private static int Me()
        {
            try
            {
                var info = Controllers.User?.UserInfo;
                return info != null ? info.UserId : 0;
            }
            catch { return 0; }
        }

        private static void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
            _tipFor = -1;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || !nc.IsConnected()) { _on = null; return; }
            if (ReferenceEquals(_on, nc)) return;
            nc.RemoveMessageListener(Hint, OnHint);
            nc.AddMessageListener(Hint, OnHint);
            nc.RemoveMessageListener(Change, OnChange);
            nc.AddMessageListener(Change, OnChange);
            _on = nc;
        }

        private static void OnChange(object m)
        {
            var msg = m as Transport.Messages.Responses.User.Enchantments.RefreshEnchantmentsPanelResponseMessage;
            if (msg == null || msg.Enchantments == null) return;
            foreach (var one in msg.Enchantments)
            {
                if (one == null) continue;
                if (one.Add) AskAt[one.Id] = 0d;
                else { Times.Remove(one.Id); AskAt.Remove(one.Id); }
            }
            _next = 0f;
        }

        private static void OnHint(object m)
        {
            var msg = m as Transport.Messages.Responses.User.Enchantments.EnchantmentHintByGroupResponseMessage;
            if (msg == null) return;
            int me = Me();
            if (me > 0 && msg.UserId != me) return;

            var known = new Known();
            known.At = RealTime.Now;
            var body = new StringBuilder();
            var said = new HashSet<string>();
            if (msg.Items != null)
                foreach (var item in msg.Items)
                {
                    if (item == null) continue;
                    if (item.Times != null)
                        foreach (var time in item.Times) known.Spans.Add(time);
                    if (item.Actions == null) continue;
                    foreach (var action in item.Actions)
                    {
                        if (action == null) continue;
                        string name = ResourceStrings.GetGlobalEnchantmentActionName(action.Id);
                        if (name == "enchantment.action." + action.Id) name = "действие " + action.Id;
                        var row = new StringBuilder();
                        row.Append("<color=#d6dae0>· ").Append(name).Append("</color>");
                        if (action.Value.HasValue)
                            row.Append("  <color=").Append(action.Value > 0 ? "#7ed68a" : "#f07a6e").Append('>')
                               .Append(action.Value > 0 ? "+" : "").Append(action.Value.Value)
                               .Append(action.Percent ? "%" : "").Append("</color>");
                        string line = row.ToString();
                        if (!said.Add(line)) continue;
                        if (body.Length > 0) body.Append('\n');
                        body.Append(line);
                    }
                }
            known.Body = body.ToString();
            known.Spans.Sort();
            Times[msg.Id] = known;
            Owed owed;
            if (Waiting.TryGetValue(msg.Id, out owed)) owed.At = RealTime.Now;
        }

        private static long Rest(Known known, int index)
        {
            if (known == null || index < 0 || index >= known.Spans.Count) return -1;
            return known.Spans[index] - (long)(RealTime.Now - known.At);
        }

        internal static bool Swallow(Transport.Messages.Responses.User.Enchantments.EnchantmentHintByGroupResponseMessage msg)
        {
            Owed owed;
            if (msg == null || !Waiting.TryGetValue(msg.Id, out owed)) return false;
            if (--owed.Count <= 0) Waiting.Remove(msg.Id);
            return true;
        }

        private static void Stale()
        {
            if (Waiting.Count == 0) return;
            List<int> gone = null;
            foreach (var pair in Waiting)
            {
                if (RealTime.Now - Math.Max(pair.Value.At, _heldAt) < Linger) continue;
                if (gone == null) gone = new List<int>();
                gone.Add(pair.Key);
            }
            if (gone == null) return;
            foreach (var id in gone) Waiting.Remove(id);
        }

        private static bool Flowing()
        {
            var nc = NetworkConnection.Instance;
            return nc != null && nc.DispatchingEnabled;
        }

        private static int _quiet;

        private static bool Quiet()
        {
            if (_quiet != 0) return _quiet > 0;
            try
            {
                var target = AccessTools.Method(typeof(DialogFactory), "ShowGlobalEnchantmentHintDialog");
                var info = target != null ? Harmony.GetPatchInfo(target) : null;
                _quiet = info != null && info.Prefixes != null && info.Prefixes.Count > 0 ? 1 : -1;
                if (_quiet < 0) Plugin.Trace("[состояния] игровое окно состояния не перехвачено, сам спрашивать не буду");
            }
            catch { _quiet = -1; }
            return _quiet > 0;
        }

        private static void Query(int id, bool mine)
        {
            try
            {
                var nc = NetworkConnection.Instance;
                if (nc == null || !nc.IsConnected()) return;
                int me = Me();
                if (me <= 0) return;
                AskAt[id] = RealTime.Now + Again;
                if (mine)
                {
                    Owed owed;
                    if (!Waiting.TryGetValue(id, out owed)) Waiting[id] = owed = new Owed();
                    owed.Count++;
                    owed.At = RealTime.Now;
                }
                nc.SendRequest(new GlobalEnchantmentsRequest(me, id));
            }
            catch (Exception e) { Plugin.Trace("[состояния] запрос " + id + ": " + e.Message); }
        }

        private static void Refresh()
        {
            bool free = Time.unscaledTime >= _sendAt && Flowing();
            foreach (var cell in Cells)
            {
                if (cell.Go == null) continue;
                Known known;
                bool have = Times.TryGetValue(cell.Id, out known);

                if (cell.Icon != null)
                {
                    var sprite = Picture(cell.Id);
                    if (sprite != null && cell.Icon.sprite != sprite) cell.Icon.sprite = sprite;
                    bool drawn = cell.Icon.sprite != null && cell.Icon.sprite.texture != null;
                    if (cell.Icon.enabled != drawn) cell.Icon.enabled = drawn;
                }

                if (cell.Frame != null && (cell.Frame.sprite == null || cell.Frame.sprite.texture == null))
                {
                    var border = Border(cell.Id);
                    if (border != null) cell.Frame.sprite = border;
                    bool edged = cell.Frame.sprite != null && cell.Frame.sprite.texture != null;
                    if (cell.Frame.enabled != edged) cell.Frame.enabled = edged;
                }

                bool timed = have && known.Spans.Count > 0;
                long left = timed ? Rest(known, 0) : -1;
                if (cell.Rest != null)
                {
                    string line = !have ? "…" : !timed ? "" : Brief(left);
                    if (cell.Rest.text != line) cell.Rest.text = line;
                    var paint = timed ? Paint(left) : (Color32)WardrobeLook.Body;
                    if (!cell.Rest.color.Equals((Color)paint)) cell.Rest.color = paint;
                }

                if (!free || !Quiet() || Waiting.ContainsKey(cell.Id)) continue;
                double when;
                bool sent = AskAt.TryGetValue(cell.Id, out when);
                bool due = !sent || RealTime.Now >= when;
                if (!due && timed && left <= 0 && RealTime.Now >= when - Again + 15f) due = true;
                if (!due) continue;
                _sendAt = Time.unscaledTime + 0.35f;
                free = false;
                Query(cell.Id, true);
            }
        }

        private static Sprite Picture(int id)
        {
            var list = Mine();
            if (list == null) return null;
            foreach (var one in list)
            {
                if (one == null || one.Id != id) continue;
                try { return one.Image; } catch { return null; }
            }
            return null;
        }

        private static Sprite Border(int id)
        {
            var list = Mine();
            if (list == null) return null;
            foreach (var one in list)
            {
                if (one == null || one.Id != id) continue;
                try { return one.Frame; } catch { return null; }
            }
            return null;
        }

        private static string Title(int id)
        {
            string name = ResourceStrings.GetGlobalEnchantmentGroupName(id);
            return name == "enchantment.group." + id ? "состояние " + id : name;
        }

        private static string Brief(long sec)
        {
            if (sec <= 0) return "<1м";
            long d = sec / 86400;
            if (d > 0) return d + "д";
            long h = sec / 3600;
            if (h > 0) return h + "ч";
            long m = sec / 60;
            return m > 0 ? m + "м" : "<1м";
        }

        private static string Full(long sec)
        {
            if (sec <= 0) return "меньше минуты";
            long d = sec / 86400;
            long h = sec % 86400 / 3600;
            long m = sec % 3600 / 60;
            if (d > 0) return d + " д " + h + " ч";
            if (h > 0) return h + " ч " + m + " мин";
            return m + " мин";
        }

        private static Color32 Paint(long sec)
        {
            if (sec < 600) return WardrobeLook.Bad;
            if (sec < 3600) return WardrobeLook.Accent;
            return WardrobeLook.Body;
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLEnchantments", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Curtain.Stage(_canvasGo);
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            _canvas = _canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 252;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var panel = new GameObject("row", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_canvasGo.transform, false);
            _root = (RectTransform)panel.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            var back = panel.GetComponent<Image>();
            back.color = new Color(WardrobeLook.Card.r, WardrobeLook.Card.g, WardrobeLook.Card.b, 0.55f);
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            Plugin.Trace("[состояния] свой ряд значков собран");
        }

        private static void Fill(List<IEnchantmentData> list)
        {
            foreach (var cell in Cells) if (cell.Go != null) UnityEngine.Object.Destroy(cell.Go);
            Cells.Clear();
            _tipFor = -1;

            var seen = new HashSet<int>();
            foreach (var one in list)
            {
                if (one == null || !seen.Add(one.Id)) continue;
                Cells.Add(Make(one));
            }
            Plugin.Trace("[состояния] значков: " + Cells.Count);
        }

        private static Cell Make(IEnchantmentData data)
        {
            var cell = new Cell { Id = data.Id };
            var go = new GameObject("cell", typeof(RectTransform), typeof(Image), typeof(Button), typeof(EventTrigger));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(Side, Side + TextH);
            var back = go.GetComponent<Image>();
            back.color = new Color(0f, 0f, 0f, 0.35f);
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)iconGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(1f, -Side - 1f), new Vector2(-1f, -1f));
            cell.Icon = iconGo.GetComponent<Image>();
            cell.Icon.preserveAspect = true;
            cell.Icon.raycastTarget = false;
            try { cell.Icon.sprite = data.Image; } catch { }

            var frameGo = new GameObject("frame", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)frameGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -Side), new Vector2(0f, 0f));
            cell.Frame = frameGo.GetComponent<Image>();
            cell.Frame.preserveAspect = true;
            cell.Frame.raycastTarget = false;
            var border = Border(data.Id);
            cell.Frame.sprite = border;
            cell.Frame.enabled = border != null && border.texture != null;

            cell.Rest = OnlineWindow.Label(go.transform, "…", 10, FontStyle.Bold, WardrobeLook.Body);
            cell.Rest.raycastTarget = false;
            var shadow = cell.Rest.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            OnlineWindow.Place(cell.Rest.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-4f, 0f), new Vector2(4f, TextH));

            int id = data.Id;
            var button = go.GetComponent<Button>();
            button.targetGraphic = back;
            button.onClick.AddListener(() => Query(id, false));

            var trigger = go.GetComponent<EventTrigger>();
            Watch(trigger, EventTriggerType.PointerEnter, e => _tipFor = id);
            Watch(trigger, EventTriggerType.PointerExit, e => { if (_tipFor == id) _tipFor = -1; });

            cell.Go = go;
            return cell;
        }

        private static void Watch(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> call)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(call);
            trigger.triggers.Add(entry);
        }

        private static void Stand()
        {
            int count = Cells.Count;
            if (count == 0) { Hide(); return; }
            float wide = Pad * 2f + count * Side + (count - 1) * (Step - Side);
            float high = Pad * 2f + Side + TextH;
            if (_root.sizeDelta.x != wide || _root.sizeDelta.y != high) _root.sizeDelta = new Vector2(wide, high);

            for (int i = 0; i < count; i++)
            {
                var rt = (RectTransform)Cells[i].Go.transform;
                var want = new Vector2(Pad + i * Step, -Pad);
                if (rt.anchoredPosition != want) rt.anchoredPosition = want;
            }

            bool freed = LeftColumn.TopBlockHidden;
            float x = freed ? 14f : 14f + SideButtons.LeftWidth + 10f;
            float y = freed ? Mathf.Max(3f, (LeftColumn.TopHeight() - high) * 0.5f) : LeftColumn.TopHeight() + 10f;
            var place = new Vector2(x, -y);
            if (_root.anchoredPosition != place) _root.anchoredPosition = place;
        }

        private static void Tip()
        {
            if (_tipFor <= 0)
            {
                if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
                return;
            }
            Cell owner = null;
            foreach (var cell in Cells) if (cell.Id == _tipFor) { owner = cell; break; }
            if (owner == null || owner.Go == null)
            {
                if (_tipGo != null && _tipGo.activeSelf) _tipGo.SetActive(false);
                return;
            }
            if (_tipGo == null) BuildTip();

            Known known;
            bool have = Times.TryGetValue(_tipFor, out known);
            var text = new StringBuilder();
            text.Append("<size=12><b><color=#eceef1>").Append(Title(_tipFor)).Append("</color></b></size>");
            if (!have) { text.Append("\n<color=#acb3bd>спрашиваю сервер…</color>"); Wake(_tipFor); }
            else
            {
                int spans = Mathf.Min(known.Spans.Count, 4);
                for (int i = 0; i < spans; i++)
                    text.Append("\n<color=#8fd8ff>осталось ").Append(Full(Rest(known, i))).Append("</color>");
                if (known.Spans.Count > spans)
                    text.Append("\n<color=#8fd8ff>и ещё ").Append(known.Spans.Count - spans).Append("</color>");
                if (known.Spans.Count == 0) text.Append("\n<color=#acb3bd>без срока</color>");
                if (!string.IsNullOrEmpty(known.Body)) text.Append('\n').Append(known.Body);
            }
            string line = text.ToString();
            if (_tipText.text != line) _tipText.text = line;

            if (!_tipGo.activeSelf) _tipGo.SetActive(true);
            var rt = (RectTransform)_tipGo.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            var cellRt = (RectTransform)owner.Go.transform;
            float x = _root.anchoredPosition.x + cellRt.anchoredPosition.x;
            float wide = _canvas != null ? ((RectTransform)_canvas.transform).rect.width : 1920f;
            if (x + rt.rect.width > wide - 8f) x = Mathf.Max(8f, wide - 8f - rt.rect.width);
            float y = -(-_root.anchoredPosition.y + _root.sizeDelta.y + 6f);
            var want = new Vector2(x, y);
            if (rt.anchoredPosition != want) rt.anchoredPosition = want;
        }

        private static void Wake(int id)
        {
            if (!Quiet() || Waiting.ContainsKey(id)) return;
            double when;
            if (AskAt.TryGetValue(id, out when) && RealTime.Now < when - Again + 3f) return;
            Query(id, true);
        }

        private static void BuildTip()
        {
            _tipGo = new GameObject("tip", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _tipGo.transform.SetParent(_canvasGo.transform, false);
            var rt = (RectTransform)_tipGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var back = _tipGo.GetComponent<Image>();
            back.color = WardrobeLook.Popup;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.raycastTarget = false;
            var edge = _tipGo.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var group = _tipGo.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(9, 9, 5, 6);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            var fit = _tipGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _tipText = OnlineWindow.Label(_tipGo.transform, "", 11, FontStyle.Normal, WardrobeLook.Body);
            _tipText.alignment = TextAnchor.UpperLeft;
            _tipText.raycastTarget = false;
            _tipText.supportRichText = true;
            _tipText.lineSpacing = 1.05f;
            _tipText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _tipText.verticalOverflow = VerticalWrapMode.Overflow;
        }
    }

    [HarmonyPatch(typeof(DialogFactory), "ShowGlobalEnchantmentHintDialog")]
    internal static class EnchantmentsQuietPatch
    {
        private static bool Prefix(Transport.Messages.Responses.User.Enchantments.EnchantmentHintByGroupResponseMessage message,
                                   ref GlobalEnchantmentHintDialog __result)
        {
            if (!Enchantments.Swallow(message)) return true;
            __result = null;
            return false;
        }
    }
}
