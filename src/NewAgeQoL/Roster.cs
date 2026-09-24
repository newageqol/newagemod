using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Roster
    {
        private const int Visible = 5;
        private const float RowW = 220f;
        private const float RowH = 34f;
        private const float Gap = 4f;
        private const float Edge = 8f;
        private const float ArrowH = 18f;

        private sealed class Face
        {
            internal Image Pic;
            internal int ClassId;
            internal bool Had;
        }

        private static GameObject _canvasGo;
        private static RectTransform _panel;
        private static GameObject _up;
        private static GameObject _down;
        private static Image _upPic;
        private static Image _downPic;
        private static readonly List<GameObject> Rows = new List<GameObject>();
        private static readonly List<Face> Faces = new List<Face>();
        private static int _page;
        private static string _sig = "";
        private static float _nextAt;
        private static ConfirmMessageBox _confirm;
        private static int _enterFrame = -1;

        internal static bool Asking => _enterFrame == Time.frameCount || (_confirm != null && _confirm.isActiveAndEnabled);

        internal static void Tick()
        {
            try
            {
                Confirm();
                bool want = SideButtons.InWorld() && !SideButtons.InCombat() && ChatDock.Active && CharacterPick.Known.Count > 1;
                if (!want) { Drop(); return; }
                bool hide = Shopping();
                if (_canvasGo != null && _canvasGo.activeSelf == hide) _canvasGo.SetActive(!hide);
                if (hide) return;
                if (_canvasGo == null) Build();
                if (_panel == null) return;
                Wheel();
                Place();
                if (Time.unscaledTime < _nextAt) return;
                _nextAt = Time.unscaledTime + 0.25f;
                Fill();
                foreach (var face in Faces) Dress(face);
            }
            catch (Exception e) { Plugin.Trace("[персонажи] " + e.Message); Drop(); }
        }

        private static bool _shop;
        private static float _shopAt;
        private static readonly List<BasePanelContentWindow> Windows = new List<BasePanelContentWindow>();

        internal static void Born(BasePanelContentWindow window)
        {
            if (window == null || window is ChatWindow) return;
            if (Windows.Count >= 32)
                for (int i = Windows.Count - 1; i >= 0; i--)
                    if (Windows[i] == null) Windows.RemoveAt(i);
            Windows.Add(window);
        }

        private static bool Shopping()
        {
            if (Time.unscaledTime < _shopAt) return _shop;
            _shopAt = Time.unscaledTime + 0.2f;
            string open = null;
            for (int i = Windows.Count - 1; i >= 0; i--)
            {
                var window = Windows[i];
                if (window == null) { Windows.RemoveAt(i); continue; }
                if (open == null && window.isActiveAndEnabled) open = window.GetType().Name;
            }
            bool now = open != null;
            if (now != _shop)
                Plugin.Trace(now ? "[персонажи] открыто окно " + open + ", колонка персонажей спрятана, чтобы не закрывать крестик"
                                 : "[персонажи] окно закрыто, колонка персонажей снова видна");
            _shop = now;
            return now;
        }

        internal static bool Under()
        {
            return _panel != null && _canvasGo != null && _canvasGo.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_panel, Input.mousePosition, null);
        }

        private static void Build()
        {
            _canvasGo = new GameObject("QoLRoster", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

            var go = new GameObject("list", typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            _panel = (RectTransform)go.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.sizeDelta = new Vector2(RowW, RowH);

            _up = Arrow(true);
            _down = Arrow(false);
            _sig = "";
            _nextAt = 0f;
            Plugin.Trace("[персонажи] колонка собрана справа сверху, персонажей " + CharacterPick.Known.Count);
        }

        private static GameObject Arrow(bool up)
        {
            var go = new GameObject(up ? "up" : "down", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(RowW, ArrowH);
            var back = go.GetComponent<Image>();
            back.color = WardrobeLook.Button;
            back.sprite = OnlineWindow.Rounded(6);
            back.type = Image.Type.Sliced;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            var tip = new GameObject("mark", typeof(RectTransform), typeof(Image));
            tip.transform.SetParent(go.transform, false);
            var pic = tip.GetComponent<Image>();
            pic.sprite = Icons.Downward();
            pic.raycastTarget = false;
            pic.preserveAspect = true;
            var trt = (RectTransform)tip.transform;
            OnlineWindow.Place(trt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6f, 2f), new Vector2(-6f, -2f));
            if (up) trt.localRotation = Quaternion.Euler(0f, 0f, 180f);
            if (up) _upPic = pic; else _downPic = pic;

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            press.colors = colors;
            press.onClick.AddListener(() => Scroll(up ? -1 : 1));
            go.SetActive(false);
            return go;
        }

        private static void Scroll(int by)
        {
            int was = _page;
            _page = Mathf.Clamp(_page + by, 0, Mathf.Max(0, CharacterPick.Known.Count - Visible));
            if (_page == was) return;
            _sig = "";
            _nextAt = 0f;
        }

        private static void Wheel()
        {
            if (CharacterPick.Known.Count <= Visible) return;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!Under()) return;
            Scroll(wheel > 0f ? -1 : 1);
        }

        private static void Fill()
        {
            var all = CharacterPick.Known;
            int me = Mine();
            _page = Mathf.Clamp(_page, 0, Mathf.Max(0, all.Count - Visible));
            var sig = new StringBuilder();
            sig.Append(me).Append('|').Append(_page).Append('|');
            foreach (var one in all) sig.Append(one.UserId).Append(':').Append(one.Level).Append(':').Append(one.Login).Append(';');
            string now = sig.ToString();
            if (now == _sig) return;
            _sig = now;

            foreach (var row in Rows) if (row != null) UnityEngine.Object.Destroy(row);
            Rows.Clear();
            Faces.Clear();
            bool scroll = all.Count > Visible;
            int shown = Mathf.Min(Visible, all.Count - _page);
            float head = scroll ? ArrowH + Gap : 0f;
            for (int i = 0; i < shown; i++)
                Rows.Add(Row(all[_page + i], me, -(head + i * (RowH + Gap))));
            float high = head + shown * (RowH + Gap) - Gap + (scroll ? Gap + ArrowH : 0f);
            _panel.sizeDelta = new Vector2(RowW, Mathf.Max(RowH, high));

            if (_up != null)
            {
                if (_up.activeSelf != scroll) _up.SetActive(scroll);
                ((RectTransform)_up.transform).anchoredPosition = Vector2.zero;
            }
            if (_down != null)
            {
                if (_down.activeSelf != scroll) _down.SetActive(scroll);
                ((RectTransform)_down.transform).anchoredPosition = new Vector2(0f, -(high - ArrowH));
            }
            if (_upPic != null) _upPic.color = _page > 0 ? WardrobeLook.Bright : new Color(1f, 1f, 1f, 0.3f);
            if (_downPic != null) _downPic.color = _page < all.Count - Visible ? WardrobeLook.Bright : new Color(1f, 1f, 1f, 0.3f);
        }

        private static GameObject Row(CharacterPick.Hero one, int me, float y)
        {
            bool current = one.UserId == me;
            var go = new GameObject("QoLRosterRow", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button));
            go.transform.SetParent(_panel, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(RowW, RowH);
            rt.anchoredPosition = new Vector2(0f, y);

            var back = go.GetComponent<Image>();
            back.sprite = OnlineWindow.Rounded(8);
            back.type = Image.Type.Sliced;
            back.color = current ? WardrobeLook.Mix(WardrobeLook.Tab, WardrobeLook.Accent, 0.28f) : WardrobeLook.Tab;
            var edge = go.GetComponent<Outline>();
            edge.effectColor = current ? WardrobeLook.Accent : WardrobeLook.Edge;
            edge.effectDistance = new Vector2(1f, -1f);

            float left = RowH + 2f;
            if (one.ClassId > 0)
            {
                var iconGo = new GameObject("class", typeof(RectTransform), typeof(Image));
                iconGo.transform.SetParent(go.transform, false);
                var irt = (RectTransform)iconGo.transform;
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(RowH - 8f, RowH - 8f);
                irt.anchoredPosition = new Vector2(6f, 0f);
                var icon = iconGo.GetComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                var face = new Face { Pic = icon, ClassId = one.ClassId };
                Faces.Add(face);
                Dress(face);
            }

            var paint = one.Banned ? WardrobeLook.Faint : current ? WardrobeLook.Accent : WardrobeLook.Bright;
            var label = OnlineWindow.Label(go.transform, (one.Login ?? "?") + "  [" + one.Level + "]", 15, current ? FontStyle.Bold : FontStyle.Normal, paint);
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;
            OnlineWindow.Place(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(left, 0f), new Vector2(-6f, 0f));

            var press = go.GetComponent<Button>();
            press.targetGraphic = back;
            var colors = press.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = Color.white;
            press.colors = colors;
            press.interactable = !one.Banned;
            var hero = one;
            press.onClick.AddListener(() => Ask(hero, current));
            return go;
        }

        private static void Dress(Face face)
        {
            if (face.Pic == null) return;
            if (face.Pic.sprite == null)
            {
                Sprite art = null;
                try { art = AtlasUtils.GetSmallClassIcon((ERPGClass)face.ClassId); }
                catch { art = null; }
                if (art != null)
                {
                    face.Pic.sprite = art;
                    if (face.Had) Plugin.Trace("[персонажи] игра выгрузила иконки классов, взял заново");
                    face.Had = true;
                }
            }
            bool shown = face.Pic.sprite != null;
            if (face.Pic.enabled != shown) face.Pic.enabled = shown;
        }

        private static void Ask(CharacterPick.Hero one, bool current)
        {
            try
            {
                string text = (current ? "Перезайти персонажем " : "Войти персонажем ") + (one.Login ?? "?") + " [" + one.Level + "]?";
                _confirm = DialogFactory.ShowConfirmMessageBox("initialize_game.messages.confirms.changecharacter.caption", null, result =>
                {
                    _confirm = null;
                    if (result != EMessageBoxResult.MB_OK) return;
                    Go(one);
                }, text);
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            catch (Exception e) { Plugin.Warn("[персонажи] подтверждение: " + e.Message); }
        }

        private static void Confirm()
        {
            if (_confirm == null || !_confirm.isActiveAndEnabled) return;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;
            var ok = AccessTools.Field(typeof(ConfirmMessageBox), "MbOkButton")?.GetValue(_confirm) as Button;
            if (ok == null || !ok.isActiveAndEnabled || !ok.interactable) return;
            _enterFrame = Time.frameCount;
            Plugin.Trace("[персонажи] смена подтверждена по Enter");
            ok.onClick.Invoke();
        }

        private static void Go(CharacterPick.Hero one)
        {
            try
            {
                if (SideButtons.InCombat()) { Plugin.Trace("[персонажи] в бою персонажа не меняю"); return; }
                var session = DependencyContainer.GetContainer()?.Resolve<SessionController>();
                if (session == null) { Plugin.Trace("[персонажи] сессия игры не найдена"); return; }
                CharacterPick.Target = one.UserId;
                Plugin.Trace((one.UserId == Mine() ? "[персонажи] перезахожу персонажем " : "[персонажи] смена персонажа на ") + one.Login + " (" + one.UserId + ")");
                Drop();
                session.ChangeCharacter(true);
            }
            catch (Exception e)
            {
                CharacterPick.Target = 0;
                Plugin.Warn("[персонажи] смена: " + e.Message);
            }
        }

        private static int Mine()
        {
            try { return Controllers.User?.UserInfo?.UserId ?? 0; }
            catch { return 0; }
        }

        private static void Place()
        {
            float wide = HelpColumn.Wide;
            var want = new Vector2(wide > 0f ? -(wide + Edge) : -HelpColumn.SideGap, -HelpColumn.Head);
            if ((_panel.anchoredPosition - want).sqrMagnitude > 0.25f) _panel.anchoredPosition = want;
        }

        private static void Drop()
        {
            if (_canvasGo == null) return;
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _panel = null;
            _up = null;
            _down = null;
            _upPic = null;
            _downPic = null;
            Rows.Clear();
            Faces.Clear();
            _sig = "";
        }
    }

    [HarmonyPatch(typeof(BasePanelContentWindow), "Start")]
    internal static class RosterWindowPatch
    {
        private static void Postfix(BasePanelContentWindow __instance)
        {
            try { Roster.Born(__instance); }
            catch (Exception e) { Plugin.Trace("[персонажи] окно игры: " + e.Message); }
        }
    }
}
