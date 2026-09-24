using System;
using UnityEngine;
using UnityEngine.UI;

namespace NewAgeQoL
{
    internal static class Changelog
    {
        private const float PanelW = 640f;
        private const float PanelH = 560f;

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static Text _state;
        private static Button _action;
        private static Text _actionText;
        private static Updater.Stage _shownStage;
        private static string _shownMessage;
        private static bool _shownAny;

        private sealed class Entry
        {
            internal string Version;
            internal string Head;
            internal string[] Lines;
        }

        private static readonly Entry[] Entries =
        {
            new Entry
            {
                Version = Plugin.Version,
                Head = "Версия 0.1.0 · первая открытая бета",
                Lines = new[]
                {
                    "Это первая открытая бета мода NewAge QoL с новым адресом: github.com/newageqol/newagemod. Отсюда же мод сам проверяет и ставит обновления. Если что-то работает не так, нажми «Сообщить об ошибке мода» в настройках игры.",
                    "Чат: панель с вкладками «Чат» и «Системные», история до 1000 строк, поиск по системным сообщениям, свои цвета каналов, смайлики как во Flash-клиенте, вкладки «Локация», «Клан» и «Альянс» над списком игроков. На карте мира общий чат переживает бои.",
                    "Бой: подсказка по бойцу при наведении, окно эффектов, умения одним рядом, отдаление камеры, горячие клавиши, кнопки банок и пополнение без ожидания анимации.",
                    "«Кто в игре»: полный список игроков онлайн с кланами, заклятиями и значками кланов, карточка игрока как во Flash-клиенте.",
                    "Окна в настройках игры: переодевалка с расчётом характеристик и рейтинга, калькулятор КУ и калькулятор крафта со всеми рецептами игры, корзиной и подсчётом нужного дропа.",
                    "Задания: окно заданий и список отслеживаемых заданий рядом с чатом с ходом выполнения.",
                    "Сумка и снаряжение: поиск, вкладка контрактов с номерами, картинки вещей у рецептов, надевание двойным щелчком и перетаскиванием, несколько лотов на рынок за раз.",
                    "Дорога: поход к точке карты, возврат в город или сразу к турнирам, зелья культов у алтаря.",
                    "Flash-вид: отдельный плагин NewAge2D рисует персонажей плоскими куклами из старого Flash-клиента. Включается в разделе «Внешний вид» настроек мода.",
                },
            },
        };

        internal static void Toggle()
        {
            if (_canvasGo != null) { Close(); return; }
            try { Build(); }
            catch (Exception e) { Plugin.Fault("[что нового] окно: " + e); Close(); }
        }

        internal static void Close()
        {
            try
            {
                if (_panelGo != null) UnityEngine.Object.Destroy(_panelGo);
                if (_canvasGo != null) CanvasFactory.ReleaseCanvas(ECanvasType.MessageBox, _canvasGo);
            }
            catch { if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo); }
            _canvasGo = null;
            _panelGo = null;
            _state = null;
            _action = null;
            _actionText = null;
            _shownAny = false;
        }

        private static bool _greeted;

        private static void Greet()
        {
            if (_greeted || Plugin.CfgSeenVersion == null) return;
            if (!SideButtons.InWorld() || SideButtons.InCombat()) return;
            if (VisualPrefabsHolder.Instance == null) return;
            _greeted = true;
            if (Plugin.CfgSeenVersion.Value == Plugin.Version) return;
            Plugin.CfgSeenVersion.Value = Plugin.Version;
            Plugin.Log?.LogInfo("[что нового] версия " + Plugin.Version + " — показываю список изменений");
            Toggle();
        }

        internal static void Tick()
        {
            Greet();
            if (_state == null) return;
            var stage = Updater.State;
            string message = Updater.Message;
            if (_shownAny && stage == _shownStage && message == _shownMessage) return;
            _shownAny = true;
            _shownStage = stage;
            _shownMessage = message;
            bool done = stage == Updater.Stage.Done;
            _state.text = done ? message : "Установлена версия " + Plugin.Version + " · " + message;
            _state.color = done ? WardrobeLook.Good : WardrobeLook.Label;
            if (_action == null || _actionText == null) return;
            bool offer = Updater.CanUpdate;
            bool busy = stage == Updater.Stage.Checking || stage == Updater.Stage.Downloading;
            _action.gameObject.SetActive(!done);
            _action.interactable = !busy;
            _actionText.text = offer ? "Обновить" : "Проверить";
        }

        internal static bool EscapeClose()
        {
            if (_canvasGo == null) return false;
            Close();
            return true;
        }

        private static void Build()
        {
            Close();
            var canvas = CanvasFactory.GenerateCanvas(ECanvasType.MessageBox);
            _canvasGo = canvas.gameObject;

            _panelGo = new GameObject("QoLChangelog", typeof(RectTransform), typeof(Image), typeof(Outline));
            _panelGo.transform.SetParent(canvas.transform, false);
            var prt = (RectTransform)_panelGo.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.anchoredPosition = Vector2.zero;
            var area = canvas.transform as RectTransform;
            if (area != null) prt.position = area.TransformPoint(area.rect.center);
            var pimg = _panelGo.GetComponent<Image>();
            pimg.color = WardrobeLook.Window;
            pimg.sprite = OnlineWindow.Rounded(16);
            pimg.type = Image.Type.Sliced;
            var outline = _panelGo.GetComponent<Outline>();
            outline.effectColor = WardrobeLook.Edge;
            outline.effectDistance = new Vector2(1f, -1f);

            var dragGo = new GameObject("drag", typeof(RectTransform), typeof(Image), typeof(DragMove));
            dragGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)dragGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -56f), Vector2.zero);
            dragGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            var mover = dragGo.GetComponent<DragMove>();
            mover.Target = prt;
            mover.Canvas = canvas;

            var title = OnlineWindow.Label(_panelGo.transform, "Что нового в моде", 24, FontStyle.Bold, WardrobeLook.Bright);
            OnlineWindow.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(18f, -50f), new Vector2(-70f, -8f));
            title.alignment = TextAnchor.MiddleLeft;
            title.raycastTarget = false;

            OnlineWindow.MakeCloseButton(_panelGo.transform, Close);

            var barGo = new GameObject("bar", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            barGo.transform.SetParent(_panelGo.transform, false);
            OnlineWindow.Place((RectTransform)barGo.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -104f), new Vector2(-16f, -58f));
            var hlg = barGo.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            _state = OnlineWindow.Label(barGo.transform, "Установлена версия " + Plugin.Version, 15, FontStyle.Normal, WardrobeLook.Label);
            _state.alignment = TextAnchor.MiddleLeft;
            _state.horizontalOverflow = HorizontalWrapMode.Wrap;
            _state.verticalOverflow = VerticalWrapMode.Truncate;
            _state.resizeTextForBestFit = true;
            _state.resizeTextMinSize = 10;
            _state.resizeTextMaxSize = 15;
            var stateLe = _state.gameObject.AddComponent<LayoutElement>();
            stateLe.flexibleWidth = 1f;
            stateLe.minWidth = 120f;

            _action = MakeButton(barGo.transform, "Проверить", 132f, () =>
            {
                if (Updater.CanUpdate) Updater.Update(); else Updater.Check();
            });
            _actionText = _action.GetComponentInChildren<Text>(true);
            _shownAny = false;
            if (Updater.State == Updater.Stage.Idle) Updater.Check();
            Tick();

            var scrollGo = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollGo.transform.SetParent(_panelGo.transform, false);
            var srt = (RectTransform)scrollGo.transform;
            OnlineWindow.Place(srt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 16f), new Vector2(-26f, -110f));
            var simg = scrollGo.GetComponent<Image>();
            simg.color = new Color(0f, 0f, 0f, 0.18f);
            simg.sprite = OnlineWindow.Rounded(10);
            simg.type = Image.Type.Sliced;
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var contentGo = new GameObject("content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollGo.transform, false);
            var cont = (RectTransform)contentGo.transform;
            cont.anchorMin = new Vector2(0f, 1f); cont.anchorMax = new Vector2(1f, 1f); cont.pivot = new Vector2(0.5f, 1f);
            cont.offsetMin = new Vector2(0f, 0f); cont.offsetMax = new Vector2(0f, 0f);
            var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 14, 16);
            vlg.spacing = 6f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var fit = contentGo.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = cont;
            scroll.viewport = srt;

            bool first = true;
            foreach (var entry in Entries)
            {
                string cap = string.IsNullOrEmpty(entry.Head) ? "Версия " + entry.Version + " · что нового" : entry.Head;
                var head = OnlineWindow.Label(contentGo.transform, cap,
                                              19, FontStyle.Bold, WardrobeLook.Accent);
                head.alignment = TextAnchor.MiddleLeft;
                head.horizontalOverflow = HorizontalWrapMode.Wrap;
                var hle = head.gameObject.AddComponent<LayoutElement>();
                hle.minHeight = 30f;
                hle.preferredHeight = 30f;
                if (!first) hle.preferredHeight = 34f;
                first = false;

                foreach (var line in entry.Lines)
                {
                    var text = OnlineWindow.Label(contentGo.transform, "•  " + line, 15, FontStyle.Normal, WardrobeLook.Body);
                    text.alignment = TextAnchor.UpperLeft;
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Overflow;
                    var tle = text.gameObject.AddComponent<LayoutElement>();
                    tle.flexibleHeight = 0f;
                    tle.minHeight = 22f;
                }

                var gap = new GameObject("gap", typeof(RectTransform), typeof(LayoutElement));
                gap.transform.SetParent(contentGo.transform, false);
                gap.GetComponent<LayoutElement>().minHeight = 10f;
            }

            MakeScrollbar(_panelGo.transform, scroll);
            scroll.verticalNormalizedPosition = 1f;
        }

        private static Button MakeButton(Transform host, string text, float width, Action onClick)
        {
            var button = OnlineWindow.MakeGameButton(host, text, width, 42f, onClick);
            button.gameObject.name = "QoLUpdateButton";
            var image = button.targetGraphic as Image;
            if (image != null) image.color = WardrobeLook.Accent;
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = WardrobeLook.OnAccent;
            return button;
        }

        private static void MakeScrollbar(Transform host, ScrollRect scroll)
        {
            var go = new GameObject("scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            go.transform.SetParent(host, false);
            var rt = (RectTransform)go.transform;
            OnlineWindow.Place(rt, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-22f, 16f), new Vector2(-10f, -110f));
            var track = go.GetComponent<Image>();
            track.color = WardrobeLook.Field;
            track.sprite = OnlineWindow.Rounded(6);
            track.type = Image.Type.Sliced;

            var area = new GameObject("area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            OnlineWindow.Place((RectTransform)area.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(2f, 2f), new Vector2(-2f, -2f));
            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            OnlineWindow.Place((RectTransform)handle.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var himg = handle.GetComponent<Image>();
            himg.color = WardrobeLook.FieldEdge;
            himg.sprite = OnlineWindow.Rounded(6);
            himg.type = Image.Type.Sliced;

            var bar = go.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = (RectTransform)handle.transform;
            bar.targetGraphic = himg;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }
    }
}
