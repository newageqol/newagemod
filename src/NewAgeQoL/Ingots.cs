using System;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Quest.Dialog;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Ingots
    {
        private const int Location = 278;
        private const int Npc = 17;
        private const int Ingot = 8888;
        private const int DealsFid = 1;
        private const int HandFid = 5;
        private const int AgainFid = 9;
        private const int OneFid = 11;
        private const int TenFid = 13;
        private const float Per = 0.5f;
        private const float Wait = 6f;
        private const float Grace = 1.5f;
        private const float Linger = 3f;
        private const float Pause = 0.12f;
        private const float Wide = 236f;
        private const float RowHigh = 20f;

        private enum Step { Idle, Talk, Paid }

        private static readonly Regex Digits = new Regex(@"(\d+)", RegexOptions.Compiled);

        private static object _heard;
        private static bool _here;
        private static Step _step;
        private static float _deadline;
        private static float _nextAt;
        private static float _sentAt;
        private static float _replyAt;
        private static int _want;
        private static int _done;
        private static int _batch;
        private static bool _noTen;
        private static int _hops;
        private static float _goldBefore;
        private static int _startCount = -1;
        private static int _cap = -1;
        private static QuestDialogResponseMessage _reply;
        private static bool _replyFresh;
        private static string _said = "";

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static Text _title;
        private static Text _state;
        private static Text _progress;
        private static InputField _count;
        private static Button _give;
        private static Button _most;
        private static Text _giveLabel;

        internal static bool Running => _step != Step.Idle;

        internal static bool Mine(int npcId) => Running && npcId == Npc;

        internal static void Tick()
        {
            try
            {
                Listen();
                Confirm();
                bool here = Here();
                if (here != _here)
                {
                    if (Running) Stop("ушёл из хранилища — остановлено");
                    _here = here;
                    _said = "";
                    if (_here) Flasks.RequestScanNow();
                }
                bool show = _here && SideButtons.InWorld() && !SideButtons.InCombat();
                if (!show)
                {
                    if (Running) Stop("начался бой — остановлено");
                    if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                    return;
                }
                if (_canvasGo == null) Build();
                if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
                Place();
                Drive();
                Paint();
                Clamp();
            }
            catch (Exception e) { Plugin.Trace("[ingots] " + e.Message); Stop("ошибка: " + e.Message); }
        }

        private static bool Here()
        {
            try
            {
                var ud = Controllers.User;
                return ud != null && ud.CurrentLocationId == Location;
            }
            catch { return false; }
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || ReferenceEquals(nc, _heard)) return;
            Unhear();
            _heard = nc;
            nc.AddMessageListener(245, OnDialog);
        }

        private static void Unhear()
        {
            var old = _heard as INetworkConnection;
            _heard = null;
            if (old == null) return;
            try { old.RemoveMessageListener(245, OnDialog); }
            catch (Exception e) { Plugin.Trace("[ingots] unsubscribe: " + e.Message); }
        }

        internal static void Shutdown()
        {
            _step = Step.Idle;
            Unhear();
            try
            {
                var original = AccessTools.Method(typeof(QuestController), "OnDialogResponse");
                var prefix = AccessTools.Method(typeof(IngotsDialogPatch), "Prefix");
                if (original != null && prefix != null) new Harmony(Plugin.Guid).Unpatch(original, prefix);
            }
            catch (Exception e) { Plugin.Trace("[ingots] unpatch: " + e.Message); }
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            _root = null;
            _title = null;
            _state = null;
            _progress = null;
            _count = null;
            _give = null;
            _most = null;
            _giveLabel = null;
        }

        private static void OnDialog(object msg)
        {
            var reply = msg as QuestDialogResponseMessage;
            if (reply == null || reply.NpcId != Npc || !Running) return;
            _reply = reply;
            _replyFresh = true;
            _replyAt = Time.unscaledTime;
        }

        private static float Gold()
        {
            var ud = Controllers.User;
            return ud != null && ud.Cash != null ? ud.Cash.Gold : 0f;
        }

        private static int InBag()
        {
            if (Flasks.Scanning || Flasks.BagThings().Count == 0) return -1;
            return Flasks.QtyOf(Ingot);
        }

        private static int Left()
        {
            if (Running) return _startCount >= 0 ? Math.Max(0, _startCount - _done) : -1;
            return InBag();
        }

        private static void GiveSome()
        {
            if (Running) { Stop("остановлено"); return; }
            int want;
            string text = _count != null ? _count.text.Trim() : "";
            if (!int.TryParse(text, out want) || want <= 0) { _said = "впиши, сколько слитков сдать"; return; }
            int have = InBag();
            if (have >= 0 && want > have) want = have;
            if (want <= 0) { _said = "в сумке нет слитков"; return; }
            Ask("Сдать " + want + " " + Bars(want) + " и получить " + Num(want * Per) + " золота?", () => Start(want));
        }

        private static void Most()
        {
            if (Running || _count == null) return;
            int have = InBag();
            if (have <= 0) return;
            _count.text = have.ToString(CultureInfo.InvariantCulture);
        }

        private static string Bars(int n)
        {
            int tail = n % 100;
            if (tail >= 11 && tail <= 14) return "слитков";
            switch (n % 10)
            {
                case 1: return "слиток";
                case 2: case 3: case 4: return "слитка";
            }
            return "слитков";
        }

        private static ConfirmMessageBox _confirm;
        private static int _enterFrame = -1;

        internal static bool Asking => _enterFrame == Time.frameCount || (_confirm != null && _confirm.isActiveAndEnabled);

        private static void Ask(string text, Action yes)
        {
            try
            {
                _confirm = DialogFactory.ShowConfirmMessageBox("dialogs.artworkshop.confirm.caption", null, result =>
                {
                    _confirm = null;
                    if (result == EMessageBoxResult.MB_OK) yes();
                }, text);
                if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            catch (Exception e) { Plugin.Warn("[ingots] confirmation: " + e.Message); }
        }

        private static void Confirm()
        {
            if (_confirm == null || !_confirm.isActiveAndEnabled) return;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            var ok = AccessTools.Field(typeof(ConfirmMessageBox), "MbOkButton")?.GetValue(_confirm) as Button;
            if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
            _enterFrame = Time.frameCount;
            ok.onClick.Invoke();
        }

        private static void Start(int want)
        {
            if (Running || !_here) return;
            _want = want;
            _done = 0;
            _hops = 0;
            _noTen = false;
            _nextAt = 0f;
            _startCount = InBag();
            _cap = -1;
            Close();
            Send(0, 1, 0);
            _said = "говорю с кассиршей…";
            Plugin.Trace("[ingots] starting: wanted " + want + ", in bag " + _startCount + ", gold " + Gold());
        }

        private static void Stop(string why)
        {
            if (_step == Step.Idle) return;
            _step = Step.Idle;
            _said = (_done > 0 ? "сдано слитков: " + _done + " · " : "") + why;
            Plugin.Trace("[ingots] " + _said);
            Flasks.RequestScanNow();
        }

        private static void Finish(string why)
        {
            _step = Step.Idle;
            _said = "сдано слитков: " + _done + ", получено " + Num(_done * Per) + " золота" + (string.IsNullOrEmpty(why) ? "" : " · " + why);
            Plugin.Trace("[ingots] " + _said);
            Flasks.RequestScanNow();
        }

        private static void Send(int fid, int type, int quest)
        {
            _replyFresh = false;
            _step = Step.Talk;
            _deadline = Time.unscaledTime + Wait;
            NetworkConnection.Instance.SendRequest(new QuestDialogRequest(Npc, fid, (EForwardType)type, quest));
        }

        private static void Drive()
        {
            if (!Running) return;
            if (Time.unscaledTime < _nextAt) return;
            switch (_step)
            {
                case Step.Talk:
                    if (!_replyFresh)
                    {
                        if (Time.unscaledTime > _deadline) Stop("кассирша не отвечает");
                        return;
                    }
                    _replyFresh = false;
                    Walk(_reply);
                    return;
                case Step.Paid:
                    bool paid = Gold() >= _goldBefore + _batch * Per - 0.01f;
                    if (paid)
                    {
                        if (!_replyFresh && Time.unscaledTime - _sentAt < Linger) return;
                        Took();
                        return;
                    }
                    if (_replyFresh && Time.unscaledTime - _replyAt >= Grace)
                    {
                        Refused(_reply);
                        return;
                    }
                    if (Time.unscaledTime > _deadline)
                    {
                        Tell(_reply);
                        Stop("кассирша слитки не приняла");
                    }
                    return;
            }
        }

        private static void Took()
        {
            bool fresh = _replyFresh;
            _replyFresh = false;
            _done += _batch;
            _said = "сдано " + _done + " из " + _want + "…";
            if (_done >= _want) { Finish(""); return; }
            _hops = 0;
            _nextAt = Time.unscaledTime + Pause;
            if (fresh && _reply != null && _reply.Forwards != null && _reply.Forwards.Count > 0) Walk(_reply);
            else Send(0, 1, 0);
        }

        private static void Refused(QuestDialogResponseMessage reply)
        {
            _replyFresh = false;
            int have = -1;
            var found = Digits.Matches(reply != null ? reply.NpcPhrase ?? "" : "");
            if (found.Count > 0) int.TryParse(found[found.Count - 1].Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out have);
            Tell(reply);
            if (have == 0)
            {
                Finish(_done > 0 ? "слитки кончились" : "в сумке нет слитков");
                return;
            }
            if (_batch > 1 && have > 0)
            {
                _noTen = true;
                _cap = _done + have;
                _startCount = _cap;
                _hops = 0;
                _nextAt = Time.unscaledTime + Pause;
                Send(0, 1, 0);
                return;
            }
            Stop("кассирша слитки не приняла");
        }

        private static void Walk(QuestDialogResponseMessage reply)
        {
            if (++_hops > 8) { Tell(reply); Stop("диалог пошёл не туда"); return; }
            if (reply == null || reply.NpcPhrase == null || reply.Forwards == null || reply.Forwards.Count == 0)
            {
                Tell(reply);
                Stop("кассирша закрыла разговор");
                return;
            }
            var one = ById(reply, OneFid);
            var ten = ById(reply, TenFid);
            if (one != null || ten != null)
            {
                int rest = _want - _done;
                if (_cap >= 0) rest = Math.Min(rest, _cap - _done);
                var pick = rest >= 10 && !_noTen && ten != null ? ten : one;
                if (pick == null || rest <= 0)
                {
                    Finish(rest <= 0 ? "" : "кассирша не предлагает сдать");
                    return;
                }
                _batch = pick == ten ? 10 : 1;
                _goldBefore = Gold();
                _replyFresh = false;
                _step = Step.Paid;
                _sentAt = Time.unscaledTime;
                _deadline = Time.unscaledTime + Wait;
                NetworkConnection.Instance.SendRequest(new QuestDialogRequest(reply.NpcId, pick.ForwardId, (EForwardType)pick.ForwardType, reply.QuestId));
                return;
            }
            var hand = ById(reply, HandFid) ?? ById(reply, AgainFid);
            if (hand != null) { Send(hand.ForwardId, hand.ForwardType, reply.QuestId); return; }
            var deals = ById(reply, DealsFid);
            if (deals != null) { Send(deals.ForwardId, deals.ForwardType, reply.QuestId); return; }
            Tell(reply);
            Stop("кассирша на незнакомой странице — варианты записаны в журнал");
        }

        private static QuestForward ById(QuestDialogResponseMessage reply, int id)
        {
            foreach (var f in reply.Forwards) if (f != null && f.ForwardId == id) return f;
            return null;
        }

        private static void Tell(QuestDialogResponseMessage reply)
        {
            try
            {
                if (reply == null) return;
                var told = new System.Text.StringBuilder();
                told.Append("[ingots] cashier reply, quest ").Append(reply.QuestId)
                    .Append(": ").Append(Flat(reply.NpcPhrase)).Append(" | options:");
                if (reply.Forwards != null)
                    foreach (var f in reply.Forwards)
                        if (f != null) told.Append(" [").Append(f.ForwardId).Append('/').Append(f.ForwardType).Append("] ").Append(Flat(f.ForwardText));
                Plugin.Log?.LogInfo(told.ToString());
            }
            catch { }
        }

        private static string Flat(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 90 ? text : text.Substring(0, 89) + "…";
        }

        private static void Close()
        {
            try
            {
                var ctrl = Controllers.Get<QuestController>();
                if (ctrl != null) AccessTools.Method(typeof(QuestController), "CloseQuestDialog")?.Invoke(ctrl, null);
            }
            catch (Exception e) { Plugin.Trace("[ingots] close dialog window: " + e.Message); }
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLIngots", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            _canvas = _canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 240;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            UiScale.Own(scaler);

            var panel = new GameObject("panel", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel.transform.SetParent(_canvasGo.transform, false);
            _root = (RectTransform)panel.transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(Wide, 80f);
            var back = panel.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var edge = panel.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var group = panel.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(8, 8, 6, 7);
            group.spacing = 4f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = Line(_root, 13, FontStyle.Bold, WardrobeLook.Accent);
            _title.text = "Сдача золотых слитков";
            _state = Line(_root, 11, FontStyle.Normal, WardrobeLook.Body);

            var row = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_root, false);
            row.GetComponent<LayoutElement>().preferredHeight = RowHigh;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 4f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            Small(OnlineWindow.MakeGameButton(row.transform, "−", RowHigh, RowHigh, () => Bump(-1)));
            _count = OnlineWindow.MakeInput(row.transform, 36f, "");
            _count.contentType = InputField.ContentType.IntegerNumber;
            _count.characterLimit = 5;
            _count.text = "1";
            _count.onValueChanged.AddListener(v => Clamp());
            Low(_count.gameObject);
            foreach (var label in _count.GetComponentsInChildren<Text>(true))
            {
                label.fontSize = 12;
                label.alignment = TextAnchor.MiddleCenter;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            }
            Small(OnlineWindow.MakeGameButton(row.transform, "+", RowHigh, RowHigh, () => Bump(1)));
            _most = OnlineWindow.MakeGameButton(row.transform, "Макс", 50f, RowHigh, Most);
            Small(_most);
            _give = OnlineWindow.MakeGameButton(row.transform, "Сдать", 70f, RowHigh, GiveSome);
            Small(_give);
            _giveLabel = _give != null ? _give.GetComponentInChildren<Text>() : null;

            _progress = Line(_root, 11, FontStyle.Normal, WardrobeLook.Label);
        }

        private static Text Line(Transform host, int size, FontStyle style, Color color)
        {
            var label = OnlineWindow.Label(host, "", size, style, color);
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.gameObject.AddComponent<LayoutElement>();
            return label;
        }

        private static void Low(GameObject go)
        {
            var size = go != null ? go.GetComponent<LayoutElement>() : null;
            if (size == null) return;
            size.preferredHeight = RowHigh;
            size.minHeight = RowHigh;
        }

        private static void Small(Button button)
        {
            if (button == null) return;
            Low(button.gameObject);
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.resizeTextMaxSize = 12;
                label.fontSize = 12;
            }
        }

        private static void Clamp()
        {
            if (_count == null || Running) return;
            int now;
            if (!int.TryParse(_count.text.Trim(), out now)) return;
            int most = InBag();
            if (most < 0) return;
            int want = Math.Max(Math.Min(now, most), most > 0 ? 1 : 0);
            if (want == now) return;
            string text = want.ToString(CultureInfo.InvariantCulture);
            _count.text = text;
            _count.caretPosition = text.Length;
        }

        private static void Bump(int by)
        {
            if (_count == null) return;
            int now;
            if (!int.TryParse(_count.text.Trim(), out now)) now = 0;
            now = Math.Max(1, now + by);
            int most = InBag();
            if (most > 0) now = Math.Min(now, most);
            _count.text = now.ToString(CultureInfo.InvariantCulture);
        }

        private static void Paint()
        {
            if (_title == null) return;
            int left = Left();
            string state;
            bool can;
            if (left < 0)
            {
                state = Running ? "Сдаю слитки…" : "Считаю слитки в сумке…";
                can = true;
            }
            else if (left == 0)
            {
                state = "В сумке нет золотых слитков.";
                can = false;
            }
            else
            {
                state = "В сумке: " + left + " · за все " + Num(left * Per) + " золота";
                can = true;
            }
            Set(_state, state);
            Set(_progress, _said);
            if (_give != null) _give.interactable = Running || can;
            if (_most != null) _most.interactable = !Running && can && left > 0;
            if (_count != null) _count.interactable = !Running;
            if (_giveLabel != null) Set(_giveLabel, Running ? "Стоп" : "Сдать");
            if (_progress != null && _progress.gameObject.activeSelf != (_said.Length > 0)) _progress.gameObject.SetActive(_said.Length > 0);
        }

        private static void Set(Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        private static string Num(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 1e-6 ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));
        }

        private static void Place()
        {
            if (_root == null) return;
            var at = Enchantments.NextTo();
            if ((_root.anchoredPosition - at).sqrMagnitude > 0.25f) _root.anchoredPosition = at;
            if (Math.Abs(_root.sizeDelta.x - Wide) > 0.5f) _root.sizeDelta = new Vector2(Wide, _root.sizeDelta.y);
        }
    }

    [HarmonyPatch(typeof(QuestController), "OnDialogResponse")]
    internal static class IngotsDialogPatch
    {
        private static bool Prefix(object msg)
        {
            try
            {
                var reply = msg as QuestDialogResponseMessage;
                return reply == null || !Ingots.Mine(reply.NpcId);
            }
            catch { return true; }
        }
    }
}
