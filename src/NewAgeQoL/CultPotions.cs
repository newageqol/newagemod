using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Transport.Messages.Responses.Quest.Dialog;
using Transport.Messages.Responses.User.Professions;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class CultPotions
    {
        private sealed class Cult
        {
            internal int Location;
            internal int Npc;
            internal int Ability;
            internal string Name;
        }

        private static readonly Cult[] Cults =
        {
            new Cult { Location = 200, Npc = 9, Ability = 18, Name = "Огня" },
            new Cult { Location = 203, Npc = 10, Ability = 22, Name = "Воды" },
            new Cult { Location = 205, Npc = 11, Ability = 21, Name = "Земли" },
            new Cult { Location = 204, Npc = 12, Ability = 17, Name = "Воздуха" },
            new Cult { Location = 217, Npc = 13, Ability = 16, Name = "Жизни" },
            new Cult { Location = 210, Npc = 14, Ability = 15, Name = "Созидания" },
            new Cult { Location = 208, Npc = 15, Ability = 19, Name = "Разрушения" },
            new Cult { Location = 209, Npc = 16, Ability = 20, Name = "Смерти" },
        };

        private const int CurseFid = 25;
        private const int YesFid = 32;
        private const int NoFid = 56;
        private const int BlessFid = 48;
        private const int BlessNoFid = 55;
        private const int BlessYesFid = 42;
        private static readonly HashSet<int> NotEnough = new HashSet<int> { 8, 65 };
        private static readonly HashSet<int> Backs = new HashSet<int> { 12, 16, 17, 28, 38, 39, 44, 55, 56, 57, 58, 62, 67, 70 };
        private static readonly HashSet<int> Closers = new HashSet<int> { 14, 37, 74 };
        private const int MenuMark = 37;
        private static readonly HashSet<int>[] Missing = { new HashSet<int>(), new HashSet<int>() };
        private static int _kind;
        private static readonly Button[] Kinds = new Button[2];
        private const float Wait = 6f;
        private const float Pause = 0.12f;
        private const float Wide = 340f;

        private enum Step { Idle, Talk, Yes, Favor }

        private static readonly Regex Digits = new Regex(@"(\d+)", RegexOptions.Compiled);
        private static readonly Dictionary<int, int> Prices = new Dictionary<int, int> { { 190, 5 } };

        private static object _heard;
        private static Cult _here;
        private static int _mine;
        private static double _favor = -1d;
        private static bool _favorKnown;
        private static float _favorAskedAt = -100f;
        private static float _favorDue;

        private static Step _step;
        private static float _deadline;
        private static float _nextAt;
        private static int _want;
        private static int _got;
        private static bool _all;
        private static double _before;
        private static QuestDialogResponseMessage _reply;
        private static bool _replyFresh;
        private static bool _favorFresh;
        private static int _hops;
        private static int _rescue;
        private static bool _reopen;
        private static string _said = "";

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static Text _title;
        private static Text _state;
        private static Text _progress;
        private static InputField _count;
        private static Button _take;
        private static Text _takeLabel;
        private static Button _every;
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static bool Running => _step != Step.Idle;

        internal static bool Mine(int npcId) => Running && _here != null && _here.Npc == npcId;

        internal static void Tick()
        {
            try
            {
                Listen();
                Confirm();
                var cult = Where();
                if (cult != _here)
                {
                    if (Running) Stop("ушёл с алтаря — остановлено");
                    _here = cult;
                    _said = "";
                    _favorKnown = false;
                    if (_here != null) AskFavor();
                }
                bool show = _here != null && SideButtons.InWorld() && !SideButtons.InCombat() && ChatDock.Active;
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
                if (!Running && ((_favorDue > 0f && Time.unscaledTime >= _favorDue) || Time.unscaledTime - _favorAskedAt > 15f))
                {
                    _favorDue = 0f;
                    AskFavor();
                }
                Paint();
                Clamp();
            }
            catch (Exception e) { Plugin.Trace("[культ] " + e.Message); Stop("ошибка: " + e.Message); }
        }

        private static Cult Where()
        {
            try
            {
                var ud = Controllers.User;
                int loc = ud != null ? ud.CurrentLocationId : -1;
                foreach (var c in Cults) if (c.Location == loc) return c;
            }
            catch { }
            return null;
        }

        private static void Listen()
        {
            var nc = NetworkConnection.Instance;
            if (nc == null || ReferenceEquals(nc, _heard)) return;
            Unhear();
            _heard = nc;
            nc.AddMessageListener(245, OnDialog);
            nc.AddMessageListener(440, OnFavor);
        }

        private static void Unhear()
        {
            var old = _heard as INetworkConnection;
            _heard = null;
            if (old == null) return;
            try
            {
                old.RemoveMessageListener(245, OnDialog);
                old.RemoveMessageListener(440, OnFavor);
            }
            catch (Exception e) { Plugin.Trace("[культ] отписка: " + e.Message); }
        }

        internal static void Shutdown()
        {
            _step = Step.Idle;
            Unhear();
            try
            {
                var original = AccessTools.Method(typeof(QuestController), "OnDialogResponse");
                var prefix = AccessTools.Method(typeof(CultPotionsDialogPatch), "Prefix");
                if (original != null && prefix != null) new Harmony(Plugin.Guid).Unpatch(original, prefix);
            }
            catch (Exception e) { Plugin.Trace("[культ] снятие патча: " + e.Message); }
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            _root = null;
            _title = null;
            _state = null;
            _progress = null;
            _count = null;
            _take = null;
            _takeLabel = null;
            _every = null;
            Kinds[0] = null;
            Kinds[1] = null;
        }

        private static void OnDialog(object msg)
        {
            var reply = msg as QuestDialogResponseMessage;
            if (reply == null || _here == null || reply.NpcId != _here.Npc) return;
            if (!Running) { _favorDue = Time.unscaledTime + 0.4f; Tell(reply); return; }
            _reply = reply;
            _replyFresh = true;
        }

        private static void OnFavor(object msg)
        {
            var list = msg as UserProfessionListResponseMessage;
            if (list == null || list.Items == null) return;
            _mine = 0;
            double favor = 0d;
            foreach (var item in list.Items)
            {
                if (item == null || item.ProfessionId < 15 || item.ProfessionId > 22 || item.CurrentLevel <= 0) continue;
                _mine = item.ProfessionId;
                favor = item.CurrentValue.GetValueOrDefault();
            }
            _favor = _mine > 0 ? favor : 0d;
            _favorKnown = true;
            _favorFresh = true;
        }

        private static void AskFavor()
        {
            try
            {
                _favorAskedAt = Time.unscaledTime;
                NetworkConnection.Instance.SendRequest(new UserProfessionListRequest());
            }
            catch (Exception e) { Plugin.Trace("[культ] запрос благосклонности: " + e.Message); }
        }

        private static string NameOf(int ability)
        {
            foreach (var c in Cults) if (c.Ability == ability) return c.Name;
            return "?";
        }

        private static int PriceKey => _here != null ? _here.Ability * 10 + _kind : 0;

        private static int PriceHere => _here != null && Prices.TryGetValue(PriceKey, out int p) ? p : 0;

        private static string KindName => _kind == 1 ? "благословения" : "проклятия";

        private static bool Ours => _favorKnown && _here != null && _mine == _here.Ability;

        private static int Affordable()
        {
            int price = PriceHere;
            if (!Ours || price <= 0) return -1;
            return (int)Math.Floor(_favor / price + 1e-6);
        }

        private static void TakeSome()
        {
            if (Running || !Ours) return;
            int want;
            string text = _count != null ? _count.text.Trim() : "";
            if (!int.TryParse(text, out want) || want <= 0) { _said = "впиши, сколько зелий взять"; return; }
            int most = Affordable();
            if (most >= 0 && want > most) want = most;
            if (want <= 0) { _said = "не хватает благосклонности"; return; }
            int price = PriceHere;
            string cost = price > 0 ? " за " + want * price + " благосклонности" : "";
            Ask("Забрать " + want + " " + Potions(want) + " " + KindName + " культа " + _here.Name + cost + "?", () => Start(want, false));
        }

        private static void TakeAll()
        {
            if (Running) { Stop("остановлено"); return; }
            if (!Ours) return;
            int most = Affordable();
            int price = PriceHere;
            string what = most > 0 ? most + " " + Potions(most) + " за " + most * price + " благосклонности" : "зелья на всю благосклонность (" + Num(_favor) + ")";
            Ask("Забрать все зелья " + KindName + " культа " + _here.Name + ": " + what + "?", () => Start(0, true));
        }

        private static string Potions(int n)
        {
            int tail = n % 100;
            if (tail >= 11 && tail <= 14) return "зелий";
            switch (n % 10)
            {
                case 1: return "зелье";
                case 2: case 3: case 4: return "зелья";
            }
            return "зелий";
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
            catch (Exception e) { Plugin.Warn("[культ] подтверждение: " + e.Message); }
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

        private static void Start(int want, bool all)
        {
            if (Running || !Ours) return;
            _all = all;
            _want = _all ? int.MaxValue : want;
            _got = 0;
            _hops = 0;
            _rescue = 0;
            _reopen = false;
            _nextAt = 0f;
            Close();
            Send(0, 1, 0);
            _said = "говорю со служителем…";
            Plugin.Trace("[культ] начинаю: культ " + _here.Name + ", нужно " + (_all ? "все" : want.ToString()) + ", благосклонность " + _favor);
        }

        private static void Stop(string why)
        {
            if (_step == Step.Idle) return;
            _step = Step.Idle;
            _said = (_got > 0 ? "забрано зелий: " + _got + " · " : "") + why;
            Plugin.Trace("[культ] " + _said);
            AskFavor();
        }

        private static void Finish(string why)
        {
            _step = Step.Idle;
            _said = "забрано зелий: " + _got + (string.IsNullOrEmpty(why) ? "" : " · " + why);
            Plugin.Trace("[культ] " + _said);
        }

        private static void Send(int fid, int type, int quest)
        {
            _replyFresh = false;
            _step = Step.Talk;
            _deadline = Time.unscaledTime + Wait;
            NetworkConnection.Instance.SendRequest(new QuestDialogRequest(_here.Npc, fid, (EForwardType)type, quest));
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
                        if (Time.unscaledTime > _deadline) Stop("служитель не отвечает");
                        return;
                    }
                    _replyFresh = false;
                    Walk(_reply);
                    return;
                case Step.Yes:
                    if (!_replyFresh && Time.unscaledTime < _deadline) return;
                    _replyFresh = false;
                    _favorFresh = false;
                    _step = Step.Favor;
                    _deadline = Time.unscaledTime + Wait;
                    AskFavor();
                    return;
                case Step.Favor:
                    if (!_favorFresh)
                    {
                        if (Time.unscaledTime > _deadline) Stop("не узнать благосклонность после покупки");
                        return;
                    }
                    _favorFresh = false;
                    int price = PriceHere;
                    if (_favor > _before - Math.Max(1, price) + 1e-6)
                    {
                        Stop("служитель зелье не выдал");
                        return;
                    }
                    _got++;
                    int spent = (int)Math.Round(_before - _favor);
                    if (spent > 0) Prices[PriceKey] = spent;
                    _said = "забрано " + _got + (_all ? "" : " из " + _want) + "…";
                    if (_got >= _want) { Finish(""); return; }
                    if (price > 0 && _favor + 1e-6 < price) { Finish("благосклонность кончилась"); return; }
                    _nextAt = Time.unscaledTime + Pause;
                    _hops = 0;
                    _rescue = 0;
                    if (_reply != null && _reply.NpcPhrase != null && _reply.Forwards != null && _reply.Forwards.Count > 0) Walk(_reply);
                    else Send(0, 1, 0);
                    return;
            }
        }

        private static void Walk(QuestDialogResponseMessage reply)
        {
            if (++_hops > 10) { Tell(reply); Stop("диалог пошёл не туда"); return; }
            if (_reopen && reply != null && reply.NpcPhrase == null)
            {
                _reopen = false;
                Send(0, (int)EForwardType.FORWARD_DIALOG, 0);
                return;
            }
            if (reply == null || reply.NpcPhrase == null || reply.Forwards == null || reply.Forwards.Count == 0)
            {
                Stop("служитель закрыл разговор");
                return;
            }
            int offer = _kind == 1 ? BlessFid : CurseFid;
            int refuse = _kind == 1 ? BlessNoFid : NoFid;
            if (ById(reply, refuse) != null)
            {
                QuestForward yes = null;
                bool lacking = false;
                foreach (var f in reply.Forwards)
                {
                    if (f == null || f.ForwardId == refuse) continue;
                    if (NotEnough.Contains(f.ForwardId)) { lacking = true; continue; }
                    if (f.ForwardId == (_kind == 1 ? BlessYesFid : YesFid)) yes = f;
                }
                int price = PriceHere;
                var match = Digits.Match(reply.NpcPhrase ?? "");
                if (price <= 0 && match.Success) price = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (price > 0 && !Prices.ContainsKey(PriceKey)) Prices[PriceKey] = price;
                if (yes == null || lacking || (price > 0 && _favor + 1e-6 < price))
                {
                    Finish(_got == 0 ? "не хватает благосклонности" + (price > 0 ? ": нужно " + price : "") : "благосклонность кончилась");
                    return;
                }
                _before = _favor;
                _replyFresh = false;
                _step = Step.Yes;
                _deadline = Time.unscaledTime + 3f;
                NetworkConnection.Instance.SendRequest(new QuestDialogRequest(reply.NpcId, yes.ForwardId, (EForwardType)yes.ForwardType, reply.QuestId));
                return;
            }
            var pick = ById(reply, offer);
            if (pick != null) { Send(pick.ForwardId, pick.ForwardType, reply.QuestId); return; }
            if (ById(reply, MenuMark) != null)
            {
                Missing[_kind].Add(_here.Ability);
                string none = "у культа " + _here.Name + " нет " + (_kind == 1 ? "благословения" : "проклятия");
                if (!Missing[1 - _kind].Contains(_here.Ability)) _kind = 1 - _kind;
                Stop(none);
                return;
            }
            if (reply.Forwards.Count == 1 && reply.Forwards[0] != null && reply.Forwards[0].ForwardType == (int)EForwardType.FORWARD_QUEST_SELECTOR)
            {
                var entry = reply.Forwards[0];
                Send(entry.ForwardId, entry.ForwardType, reply.QuestId);
                return;
            }
            foreach (var f in reply.Forwards)
                if (f != null && Backs.Contains(f.ForwardId)) { Send(f.ForwardId, f.ForwardType, reply.QuestId); return; }
            Tell(reply);
            if (_rescue < 2)
                foreach (var f in reply.Forwards)
                    if (f != null && Closers.Contains(f.ForwardId))
                    {
                        _rescue++;
                        _reopen = true;
                        Plugin.Log?.LogInfo("[культ] незнакомая страница — закрываю разговор вариантом " + f.ForwardId + " и открываю заново");
                        Send(f.ForwardId, f.ForwardType, reply.QuestId);
                        return;
                    }
            Stop("служитель на незнакомой странице — варианты записаны в журнал");
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
                told.Append("[культ] ответ служителя ").Append(reply.NpcId).Append(", задание ").Append(reply.QuestId)
                    .Append(": ").Append(Flat(reply.NpcPhrase)).Append(" | варианты:");
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
            catch (Exception e) { Plugin.Trace("[культ] закрыть окно диалога: " + e.Message); }
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLCultPotions", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            _root.anchorMin = _root.anchorMax = Vector2.zero;
            _root.pivot = Vector2.zero;
            _root.sizeDelta = new Vector2(Wide, 80f);
            var back = panel.GetComponent<Image>();
            back.color = WardrobeLook.Window;
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            var edge = panel.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);
            var group = panel.GetComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(10, 10, 8, 9);
            group.spacing = 5f;
            group.childAlignment = TextAnchor.UpperLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            var fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = Line(_root, 14, FontStyle.Bold, WardrobeLook.Accent);

            var kinds = new GameObject("kinds", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            kinds.transform.SetParent(_root, false);
            kinds.GetComponent<LayoutElement>().preferredHeight = RowHigh;
            var kh = kinds.GetComponent<HorizontalLayoutGroup>();
            kh.spacing = 5f;
            kh.childAlignment = TextAnchor.MiddleLeft;
            kh.childControlWidth = true;
            kh.childControlHeight = true;
            kh.childForceExpandWidth = true;
            kh.childForceExpandHeight = true;
            Kinds[0] = OnlineWindow.MakeGameButton(kinds.transform, "Проклятие", 100f, RowHigh, () => Pick(0));
            Kinds[1] = OnlineWindow.MakeGameButton(kinds.transform, "Благословение", 100f, RowHigh, () => Pick(1));
            foreach (var k in Kinds)
            {
                Small(k);
                var size = k != null ? k.GetComponent<LayoutElement>() : null;
                if (size != null) size.flexibleWidth = 1f;
            }

            _state = Line(_root, 12, FontStyle.Normal, WardrobeLook.Body);

            var row = new GameObject("row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(_root, false);
            row.GetComponent<LayoutElement>().preferredHeight = RowHigh;
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 5f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            Small(OnlineWindow.MakeGameButton(row.transform, "−", RowHigh, RowHigh, () => Bump(-1)));
            _count = OnlineWindow.MakeInput(row.transform, 52f, "");
            _count.contentType = InputField.ContentType.IntegerNumber;
            _count.characterLimit = 4;
            _count.text = "1";
            _count.onValueChanged.AddListener(v => Clamp());
            Low(_count.gameObject);
            foreach (var label in _count.GetComponentsInChildren<Text>(true))
            {
                label.fontSize = 13;
                label.alignment = TextAnchor.MiddleCenter;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(4f, 0f), new Vector2(-4f, 0f));
            }
            Small(OnlineWindow.MakeGameButton(row.transform, "+", RowHigh, RowHigh, () => Bump(1)));
            _take = OnlineWindow.MakeGameButton(row.transform, "Забрать", 90f, RowHigh, TakeSome);
            Small(_take);
            var takeSize = _take != null ? _take.GetComponent<LayoutElement>() : null;
            if (takeSize != null) takeSize.flexibleWidth = 1f;

            _every = OnlineWindow.MakeGameButton(_root, "Забрать все", Wide - 20f, RowHigh + 2f, TakeAll);
            Small(_every);
            _takeLabel = _every != null ? _every.GetComponentInChildren<Text>() : null;

            _progress = Line(_root, 12, FontStyle.Normal, WardrobeLook.Label);
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

        private const float RowHigh = 22f;

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
                label.resizeTextMaxSize = 13;
                label.fontSize = 13;
            }
        }

        private static void Clamp()
        {
            if (_count == null || Running) return;
            int now;
            if (!int.TryParse(_count.text.Trim(), out now)) return;
            int most = Affordable();
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
            int most = Affordable();
            if (most > 0) now = Math.Min(now, most);
            _count.text = now.ToString(CultureInfo.InvariantCulture);
        }

        private static void Pick(int kind)
        {
            if (Running || _kind == kind) return;
            _kind = kind;
            _said = "";
        }

        private static void Paint()
        {
            if (_title == null || _here == null) return;
            if (!Running && Missing[_kind].Contains(_here.Ability) && !Missing[1 - _kind].Contains(_here.Ability)) _kind = 1 - _kind;
            for (int i = 0; i < Kinds.Length; i++)
            {
                var k = Kinds[i];
                if (k == null) continue;
                bool visible = !Missing[i].Contains(_here.Ability);
                if (k.gameObject.activeSelf != visible) k.gameObject.SetActive(visible);
                var face = k.targetGraphic as Image;
                var want = i == _kind ? WardrobeLook.Accent : WardrobeLook.Button;
                if (face != null && face.color != want) face.color = want;
                var label = k.GetComponentInChildren<Text>();
                var ink = i == _kind ? WardrobeLook.OnAccent : WardrobeLook.Bright;
                if (label != null && label.color != ink) label.color = ink;
                k.interactable = !Running;
            }
            Set(_title, "Зелья культа " + _here.Name);
            string state;
            bool can = false;
            if (!_favorKnown)
            {
                state = "узнаю благосклонность…";
                if (Time.unscaledTime - _favorAskedAt > Wait) AskFavor();
            }
            else if (_mine == 0) state = "Ты не состоишь ни в одном культе — здесь зелья не выдадут.";
            else if (_mine != _here.Ability)
                state = "Ты последователь культа " + NameOf(_mine) + " (благосклонность " + Num(_favor) + "). Здесь зелья не выдадут — нужен алтарь культа " + NameOf(_mine) + ".";
            else
            {
                int price = PriceHere;
                state = "Благосклонность: " + Num(_favor);
                if (price > 0) state += " · зелье стоит " + price + " · хватит на " + Affordable();
                else state += " · цену назовёт служитель";
                can = price <= 0 || _favor + 1e-6 >= price;
            }
            Set(_state, state);
            Set(_progress, _said);
            if (_take != null) _take.interactable = !Running && can;
            if (_every != null) _every.interactable = Running || can;
            if (_count != null) _count.interactable = !Running;
            if (_takeLabel != null) Set(_takeLabel, Running ? "Стоп" : "Забрать все");
            if (_progress != null && _progress.gameObject.activeSelf != (_said.Length > 0)) _progress.gameObject.SetActive(_said.Length > 0);
        }

        private static void Set(Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        private static string Num(double value)
        {
            return Math.Abs(value - Math.Round(value)) < 1e-6 ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static void Place()
        {
            var dock = ChatDock.Root;
            if (_root == null || _canvas == null || dock == null) return;
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            var dockCanvas = dock.GetComponentInParent<Canvas>();
            var cam = dockCanvas != null && dockCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? dockCanvas.rootCanvas.worldCamera : null;
            dock.GetWorldCorners(Corners);
            Vector2 low = RectTransformUtility.WorldToScreenPoint(cam, Corners[1]);
            var spot = new Vector2(low.x / scale, low.y / scale + 24f);
            if ((_root.anchoredPosition - spot).sqrMagnitude > 0.25f) _root.anchoredPosition = spot;
            if (Math.Abs(_root.sizeDelta.x - Wide) > 0.5f) _root.sizeDelta = new Vector2(Wide, _root.sizeDelta.y);
        }
    }

    [HarmonyPatch(typeof(QuestController), "OnDialogResponse")]
    internal static class CultPotionsDialogPatch
    {
        private static bool Prefix(object msg)
        {
            try
            {
                var reply = msg as QuestDialogResponseMessage;
                return reply == null || !CultPotions.Mine(reply.NpcId);
            }
            catch { return true; }
        }
    }
}
