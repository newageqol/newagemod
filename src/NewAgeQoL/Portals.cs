using System;
using HarmonyLib;
using Transport.Messages.Responses.Quest.Dialog;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Portals
    {
        private const string Asset = "locations/portal.assetbundle";
        private const string Face = "Portal_ava_small";
        private const float Wide = 300f;
        private const float RowHigh = 26f;
        private const float Wait = 6f;
        private const int Tries = 3;

        private static int _npc;
        private static int _map = -1;
        private static QuestDialogResponseMessage _page;
        private static bool _dirty;
        private static bool _clicked;
        private static float _clickedAt;
        private static bool _going;
        private static float _askAt;
        private static float _deadline;
        private static int _tries;
        private static string _said = "";

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _root;
        private static Text _title;
        private static Text _phrase;
        private static RectTransform _list;
        private static Text _state;
        private static readonly Vector3[] Corners = new Vector3[4];
        private static BottomPanelButton _talk;
        private static BottomPanelButton _early;
        private static float _earlyAt;
        private static float _lookAt;
        private static Vector2 _spot;
        private static bool _spotKnown;

        internal static bool Mine(int npcId) => _npc > 0 && npcId == _npc;

        internal static void Tick()
        {
            try
            {
                int npc = 0, map = -1;
                if (SideButtons.InWorld() && !SideButtons.InCombat())
                {
                    var loc = Travel.LastMap;
                    var ud = Controllers.User;
                    if (loc != null && ud != null && loc.MapId == ud.CurrentLocationId)
                    {
                        npc = Keeper(loc);
                        map = loc.MapId;
                    }
                }
                if (npc != _npc || (npc > 0 && map != _map))
                {
                    _npc = npc;
                    _map = map;
                    _page = null;
                    _dirty = true;
                    _clicked = false;
                    _going = false;
                    _tries = 0;
                    _said = "";
                    _askAt = npc > 0 ? Time.unscaledTime + 0.1f : 0f;
                    if (npc > 0) Plugin.Trace("[portal] location " + map + ", portal keeper " + npc);
                    Restore();
                    _spotKnown = false;
                }
                if (_npc <= 0)
                {
                    Restore();
                    if (_early != null && Time.unscaledTime - _earlyAt > 3f) { Veil(_early, false); _early = null; }
                    if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                    return;
                }
                if (_going && Time.unscaledTime > _deadline)
                {
                    _going = false;
                    _said = "портал не ответил";
                    _askAt = Time.unscaledTime;
                    _tries = 0;
                }
                if (_page == null && _askAt > 0f && Time.unscaledTime >= _askAt) Open();
                Hide();
                if (_canvasGo == null) Build();
                if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
                Paint();
                Place();
            }
            catch (Exception e) { Plugin.Trace("[portal] " + e.Message); }
        }

        private static int Keeper(LocationMap map)
        {
            if (map.MapElements == null || map.SceneObjects == null) return 0;
            bool portal = false;
            foreach (var one in map.MapElements)
                if (one != null && one.Path == Asset) { portal = true; break; }
            if (!portal) return 0;
            int any = 0, count = 0;
            foreach (var one in map.SceneObjects)
            {
                if (one == null || one.ObjectType != EObjectType.Npc || one.Id <= 0) continue;
                if (one.SpriteName == Face) return one.Id;
                any = one.Id;
                count++;
            }
            return count == 1 ? any : 0;
        }

        private static void Open()
        {
            if (_tries >= Tries)
            {
                _askAt = 0f;
                _said = "портал не отвечает";
                return;
            }
            _tries++;
            _askAt = Time.unscaledTime + Wait;
            _clicked = false;
            NetworkConnection.Instance.SendRequest(new QuestDialogRequest(_npc, 0, EForwardType.FORWARD_DIALOG, 0));
        }

        internal static void Take(QuestDialogResponseMessage reply)
        {
            CloseGame();
            if (reply.NpcPhrase == null)
            {
                _page = null;
                _dirty = true;
                _tries = 0;
                _askAt = Time.unscaledTime + (_going ? 2f : 0.3f);
                return;
            }
            bool changed = Sig(reply) != Sig(_page);
            if (changed) _dirty = true;
            _page = reply;
            _going = false;
            _tries = 0;
            if (!_clicked) _said = "";
            if (changed || _clicked) Tell(reply);
        }

        private static string Sig(QuestDialogResponseMessage page)
        {
            if (page == null || page.Forwards == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var f in page.Forwards)
                if (f != null) sb.Append(f.ForwardId).Append(':').Append(f.ForwardText).Append('|');
            return sb.ToString();
        }

        private static void Go(QuestForward pick)
        {
            if (_going || _page == null || pick == null) return;
            var page = _page;
            _going = true;
            _clicked = true;
            _clickedAt = Time.unscaledTime;
            _deadline = Time.unscaledTime + Wait;
            _said = "";
            Plugin.Trace("[portal] option " + pick.ForwardId + " chosen at keeper " + page.NpcId);
            NetworkConnection.Instance.SendRequest(new QuestDialogRequest(page.NpcId, pick.ForwardId, (EForwardType)pick.ForwardType, page.QuestId));
        }

        private static void CloseGame()
        {
            try
            {
                var ctrl = Controllers.Get<QuestController>();
                if (ctrl != null && ctrl.QuestDialog != null) AccessTools.Method(typeof(QuestController), "CloseQuestDialog")?.Invoke(ctrl, null);
            }
            catch (Exception e) { Plugin.Trace("[portal] close dialog window: " + e.Message); }
        }

        private static void Tell(QuestDialogResponseMessage reply)
        {
            var told = new System.Text.StringBuilder();
            told.Append("[portal] keeper ").Append(reply.NpcId).Append(", quest ").Append(reply.QuestId).Append(", options:");
            if (reply.Forwards != null)
                foreach (var f in reply.Forwards)
                    if (f != null) told.Append(" [").Append(f.ForwardId).Append('/').Append(f.ForwardType).Append(']');
            Plugin.Trace(told.ToString());
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLPortals", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
            _title.text = "Портал";
            _phrase = Line(_root, 12, FontStyle.Normal, WardrobeLook.Body);

            var list = new GameObject("list", typeof(RectTransform), typeof(VerticalLayoutGroup));
            list.transform.SetParent(_root, false);
            _list = (RectTransform)list.transform;
            var v = list.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            _state = Line(_root, 12, FontStyle.Normal, WardrobeLook.Label);
            _dirty = true;
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

        private static void Rows()
        {
            for (int i = _list.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_list.GetChild(i).gameObject);
            if (_page == null || _page.Forwards == null) return;
            foreach (var f in _page.Forwards)
            {
                if (f == null || f.ForwardId < 0) continue;
                var pick = f;
                string text = (f.ForwardText ?? "").Trim().TrimEnd('.');
                var button = OnlineWindow.MakeGameButton(_list, text, Wide - 20f, RowHigh, () => Go(pick));
                foreach (var label in button.GetComponentsInChildren<Text>(true))
                {
                    label.resizeTextMaxSize = 13;
                    label.fontSize = 13;
                }
            }
        }

        private static int Count()
        {
            int n = 0;
            if (_page != null && _page.Forwards != null)
                foreach (var f in _page.Forwards) if (f != null && f.ForwardId >= 0) n++;
            return n;
        }

        private static void Paint()
        {
            if (_dirty)
            {
                _dirty = false;
                Rows();
            }
            string phrase = _page != null && _clicked && Time.unscaledTime - _clickedAt > 1.5f ? (_page.NpcPhrase ?? "").Trim() : "";
            Set(_phrase, phrase);
            if (_phrase.gameObject.activeSelf != (phrase.Length > 0)) _phrase.gameObject.SetActive(phrase.Length > 0);
            string state = _said;
            if (state.Length == 0 && !_going && !(_clicked && Time.unscaledTime - _clickedAt <= 1.5f))
            {
                if (_page == null) state = "узнаю, куда можно перенестись…";
                else if (Count() == 0) state = "Перенестись некуда: нужны руны портала.";
            }
            Set(_state, state);
            if (_state.gameObject.activeSelf != (state.Length > 0)) _state.gameObject.SetActive(state.Length > 0);
            foreach (var button in _list.GetComponentsInChildren<Button>(true))
                if (button.interactable == _going) button.interactable = !_going;
        }

        private static void Set(Text label, string text)
        {
            if (label != null && label.text != text) label.text = text;
        }

        internal static void Early(SceneObjectInfo info, UnityEngine.Events.UnityEvent click)
        {
            if (info == null || click == null || info.ObjectType != EObjectType.Npc || info.SpriteName != Face) return;
            foreach (var one in UnityEngine.Object.FindObjectsOfType<BottomPanelButton>())
            {
                if (one == null || one.button == null || one.button.onClick != click) continue;
                Veil(one, true);
                _early = one;
                _earlyAt = Time.unscaledTime;
                return;
            }
        }

        private static bool Veil(BottomPanelButton button, bool hidden)
        {
            var group = button.GetComponent<CanvasGroup>() ?? button.gameObject.AddComponent<CanvasGroup>();
            float alpha = hidden ? 0f : 1f;
            if (group.alpha == alpha && group.blocksRaycasts != hidden) return false;
            group.alpha = alpha;
            group.blocksRaycasts = !hidden;
            group.interactable = !hidden;
            return true;
        }

        private static void Hide()
        {
            if (_talk == null && _early != null) _talk = _early;
            if (_talk == null)
            {
                if (Time.unscaledTime < _lookAt) return;
                _lookAt = Time.unscaledTime + 0.25f;
                foreach (var one in UnityEngine.Object.FindObjectsOfType<BottomPanelButton>())
                    if (one != null && one.ObjectType == EObjectType.Npc && one.gameObject.activeInHierarchy) { _talk = one; break; }
                if (_talk == null) return;
            }
            Measure(_talk.transform as RectTransform);
            if (!Veil(_talk, true)) return;
            Plugin.Trace("[portal] talk button hidden, list placed at " + _spot.x.ToString("0") + "," + _spot.y.ToString("0"));
        }

        private static void Restore()
        {
            if (_talk != null) Veil(_talk, false);
            if (_early == _talk) _early = null;
            _talk = null;
        }

        private static void Measure(RectTransform rt)
        {
            if (rt == null) return;
            if (_canvasGo == null) Build();
            var host = rt.GetComponentInParent<Canvas>();
            var cam = host != null && host.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? host.rootCanvas.worldCamera : null;
            rt.GetWorldCorners(Corners);
            Vector2 left = RectTransformUtility.WorldToScreenPoint(cam, Corners[0]);
            Vector2 right = RectTransformUtility.WorldToScreenPoint(cam, Corners[3]);
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            _spot = new Vector2((left.x + right.x) * 0.5f / scale - Wide * 0.5f, Mathf.Min(left.y, right.y) / scale);
            _spotKnown = true;
        }

        private static void Place()
        {
            if (_root == null || _canvas == null) return;
            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            Vector2 spot;
            var dock = ChatDock.Root;
            if (_spotKnown)
            {
                float most = Screen.width / scale - Wide - 8f;
                spot = new Vector2(Mathf.Clamp(_spot.x, 8f, Mathf.Max(8f, most)), Mathf.Max(8f, _spot.y));
            }
            else if (dock != null)
            {
                var dockCanvas = dock.GetComponentInParent<Canvas>();
                var cam = dockCanvas != null && dockCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? dockCanvas.rootCanvas.worldCamera : null;
                dock.GetWorldCorners(Corners);
                Vector2 low = RectTransformUtility.WorldToScreenPoint(cam, Corners[1]);
                spot = new Vector2(low.x / scale, low.y / scale + 24f);
            }
            else spot = new Vector2(Screen.width / scale * 0.5f - Wide * 0.5f, Screen.height / scale * 0.3f);
            if ((_root.anchoredPosition - spot).sqrMagnitude > 0.25f) _root.anchoredPosition = spot;
            if (Math.Abs(_root.sizeDelta.x - Wide) > 0.5f) _root.sizeDelta = new Vector2(Wide, _root.sizeDelta.y);
        }
    }

    [HarmonyPatch(typeof(StaticLocationLoadController), "FindInteractionObject")]
    internal static class PortalsButtonPatch
    {
        private static void Postfix(SceneObjectInfo objectInfo, UnityEngine.Events.UnityEvent __result)
        {
            try { Portals.Early(objectInfo, __result); }
            catch (Exception e) { Plugin.Trace("[portal] keeper button: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(QuestController), "OnDialogResponse")]
    internal static class PortalsDialogPatch
    {
        private static bool Prefix(object msg)
        {
            try
            {
                var reply = msg as QuestDialogResponseMessage;
                if (reply == null || !Portals.Mine(reply.NpcId)) return true;
                Portals.Take(reply);
                return false;
            }
            catch (Exception e) { Plugin.Trace("[portal] reply: " + e.Message); return true; }
        }
    }
}
