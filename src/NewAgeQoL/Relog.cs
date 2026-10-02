using System;
using HarmonyLib;
using UnityDI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Relog
    {
        private const float Round = 56f;
        private const float Margin = 8f;
        private const float Confirm = 3f;
        private const float Longest = 60f;

        private static readonly Color Ink = new Color(1f, 0.94f, 0.78f);
        private static readonly Color Warn = new Color(1f, 0.62f, 0.45f);
        private static readonly Color Gold = new Color(0.79f, 0.64f, 0.36f);

        private static GameObject _barGo;
        private static RectTransform _bar;
        private static Image _glyph;
        private static GameObject _plate;
        private static Text _label;
        private static RelogHover _over;
        private static float _armedUntil;

        private static GameObject _coverGo;
        private static Text _coverText;
        private static string _who;
        private static float _coverAt;
        private static bool _left;

        internal static void Tick()
        {
            try { Show(); }
            catch (Exception e) { Plugin.Trace("[relog] button: " + e.Message); }
            try { Watch(); }
            catch (Exception e) { Plugin.Trace("[relog] cover: " + e.Message); }
        }

        private static void Show()
        {
            var area = ChatDock.Area;
            if (area == null || !SideButtons.InCombat() || Spectate.Peeking || _coverGo != null)
            {
                Hide();
                return;
            }
            if (_bar == null || _bar.parent != area) Build(area);
            if (_bar == null) return;
            if (!_barGo.activeSelf) _barGo.SetActive(true);
            if (_bar.GetSiblingIndex() != area.childCount - 1) _bar.SetAsLastSibling();

            bool armed = Time.unscaledTime < _armedUntil;
            bool tell = armed || (_over != null && _over.Over);
            if (_plate.activeSelf != tell) _plate.SetActive(tell);
            string caption = armed ? "Точно? Нажмите ещё раз" : "Перезайти этим персонажем";
            if (_label.text != caption) _label.text = caption;
            var paint = armed ? Warn : Ink;
            if (_label.color != paint) _label.color = paint;
            if (_glyph.color != paint) _glyph.color = paint;
        }

        private static void Hide()
        {
            _armedUntil = 0f;
            if (_barGo != null && _barGo.activeSelf) _barGo.SetActive(false);
        }

        private static void Build(RectTransform area)
        {
            if (_barGo != null) UnityEngine.Object.Destroy(_barGo);
            _barGo = new GameObject("QoLRelog", typeof(RectTransform), typeof(Image), typeof(Outline), typeof(Button), typeof(RelogHover));
            Curtain.Stage(_barGo);
            _barGo.transform.SetParent(area, false);
            _bar = (RectTransform)_barGo.transform;
            _bar.localScale = Vector3.one;
            _bar.anchorMin = _bar.anchorMax = new Vector2(1f, 0f);
            _bar.pivot = new Vector2(1f, 0f);
            _bar.sizeDelta = new Vector2(Round, Round);
            _bar.anchoredPosition = new Vector2(-Margin, Margin);
            _over = _barGo.GetComponent<RelogHover>();

            var back = _barGo.GetComponent<Image>();
            back.color = new Color(0.13f, 0.10f, 0.07f, 0.95f);
            back.sprite = OnlineWindow.RoundedExact(28);
            back.type = Image.Type.Sliced;
            back.raycastTarget = true;
            var edge = _barGo.GetComponent<Outline>();
            edge.effectColor = Gold;
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            var button = _barGo.GetComponent<Button>();
            button.targetGraphic = back;
            var tints = button.colors;
            tints.normalColor = Color.white;
            tints.highlightedColor = new Color(1.35f, 1.2f, 1f, 1f);
            tints.pressedColor = new Color(0.8f, 0.75f, 0.7f, 1f);
            tints.selectedColor = Color.white;
            tints.colorMultiplier = 1f;
            tints.fadeDuration = 0.05f;
            button.colors = tints;
            button.onClick.AddListener(Click);

            var glyphGo = new GameObject("glyph", typeof(RectTransform), typeof(Image));
            glyphGo.transform.SetParent(_bar, false);
            var grt = (RectTransform)glyphGo.transform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(34f, 34f);
            _glyph = glyphGo.GetComponent<Image>();
            _glyph.sprite = Icons.Again();
            _glyph.preserveAspect = true;
            _glyph.raycastTarget = false;

            _plate = new GameObject("caption", typeof(RectTransform), typeof(Image));
            _plate.transform.SetParent(_bar, false);
            var prt = (RectTransform)_plate.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.sizeDelta = new Vector2(230f, 30f);
            prt.anchoredPosition = new Vector2(-8f, 0f);
            var plate = _plate.GetComponent<Image>();
            plate.color = new Color(0.09f, 0.075f, 0.055f, 0.94f);
            plate.sprite = OnlineWindow.Rounded(8);
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = false;

            var go = new GameObject("label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(prt, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _label = go.GetComponent<Text>();
            _label.font = SideButtons.GameFont();
            _label.fontSize = 14;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.raycastTarget = false;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            _plate.SetActive(false);
            _barGo.SetActive(false);
        }

        private static void Click()
        {
            if (Time.unscaledTime >= _armedUntil)
            {
                _armedUntil = Time.unscaledTime + Confirm;
                return;
            }
            _armedUntil = 0f;
            Go();
        }

        private static void Go()
        {
            int id = 0;
            string login = null;
            try
            {
                var cd = FighterHint.Cd();
                var me = cd != null ? cd.MyCharacter : null;
                if (me != null && me.UserId > 0) { id = me.UserId; login = me.Login; }
            }
            catch { }
            if (id <= 0) id = Plugin.CfgLastCharacter != null ? Plugin.CfgLastCharacter.Value : 0;
            if (id <= 0)
            {
                Notice.Show("Не знаю, каким персонажем перезайти", 4f);
                return;
            }
            var session = DependencyContainer.GetContainer()?.Resolve<SessionController>();
            if (session == null)
            {
                Notice.Show("Перезайти не получилось: сессия игры не найдена", 4f);
                return;
            }
            _who = string.IsNullOrEmpty(login) ? Name(id) : login;
            Plugin.Trace("[relog] relogging from the fight as " + (_who ?? "?") + " (" + id + ")");
            Cover();
            CharacterPick.Target = id;
            try { session.ChangeCharacter(true); }
            catch (Exception e)
            {
                CharacterPick.Target = 0;
                Uncover("error");
                Plugin.Warn("[relog] change character: " + e.Message);
                Notice.Show("Перезайти не получилось", 4f);
            }
        }

        private static string Name(int id)
        {
            foreach (var one in CharacterPick.Known)
                if (one.UserId == id) return one.Login;
            return null;
        }

        private static void Watch()
        {
            if (_coverGo == null) return;
            float now = Time.unscaledTime;
            if (!_left && !SideButtons.InWorld()) _left = true;
            if (_left && SideButtons.InWorld()) { Uncover("back in the game"); return; }
            if (now - _coverAt > Longest) { Uncover("timeout"); return; }
            if (_coverText != null)
            {
                int dots = 1 + (int)(now * 2.5f) % 3;
                string text = "Перезаход" + (string.IsNullOrEmpty(_who) ? "" : ": " + _who) + new string('.', dots);
                if (_coverText.text != text) _coverText.text = text;
            }
        }

        private static void Cover()
        {
            if (_coverGo != null) UnityEngine.Object.Destroy(_coverGo);
            _coverGo = new GameObject("QoLRelogCover", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_coverGo);
            var canvas = _coverGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _coverGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;

            var backGo = new GameObject("back", typeof(RectTransform), typeof(Image));
            backGo.transform.SetParent(_coverGo.transform, false);
            var brt = (RectTransform)backGo.transform;
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = Vector2.zero;
            brt.offsetMax = Vector2.zero;
            var back = backGo.GetComponent<Image>();
            back.color = new Color(0.055f, 0.06f, 0.07f, 1f);
            back.raycastTarget = true;

            _coverText = Say(_coverGo.transform, "Перезаход", 30, FontStyle.Bold, WardrobeLook.Accent, Vector2.zero);

            _coverAt = Time.unscaledTime;
            _left = false;
        }

        private static Text Say(Transform host, string text, int size, FontStyle style, Color color, Vector2 at)
        {
            var go = new GameObject("text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1200f, 60f);
            rt.anchoredPosition = at;
            var label = go.GetComponent<Text>();
            label.font = SideButtons.GameFont();
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.text = text;
            return label;
        }

        internal static void Uncover(string why)
        {
            if (_coverGo == null) return;
            Plugin.Trace("[relog] cover removed: " + why);
            UnityEngine.Object.Destroy(_coverGo);
            _coverGo = null;
            _coverText = null;
        }
    }

    internal sealed class RelogHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal bool Over;

        public void OnPointerEnter(PointerEventData data) => Over = true;

        public void OnPointerExit(PointerEventData data) => Over = false;

        private void OnDisable() => Over = false;
    }

    [HarmonyPatch(typeof(LaunchSceneController), "Login")]
    internal static class RelogLoginPatch
    {
        private static void Prefix() => Relog.Uncover("login needed");
    }

    [HarmonyPatch(typeof(FatalErrorSceneScript), nameof(FatalErrorSceneScript.LoadFatalErrorScene))]
    internal static class RelogFatalPatch
    {
        private static void Prefix() => Relog.Uncover("error screen");
    }
}
