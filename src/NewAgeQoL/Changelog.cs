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
                Lines = new[]
                {
                    "Ночная тема стала проще: ночной Иллениум, башня магии в 3D и свой экран загрузки. Готическое оформление убрано: окна магии, лавок, аптеки и рынка и подсказки к вещам и заклинаниям снова обычные. Мод стал почти втрое легче: около 2,7 МБ вместо 8.",
                    "Окна торговли, как и раньше, заканчиваются над чатом и не закрывают его.",
                    "В бою над ником каждого игрока видно, сколько до него клеток. Оранжевая цифра — живой враг, до которого ты достаёшь ударом, голубая — все остальные. Цифра видна, пока твоё тело на поле, и висит над павшими игроками, пока тело лежит. Над мобами, призванными существами и монолитами её нет, в чужих боях тоже. Строка «До цели» в подсказке при наведении осталась.",
                    "Чат при смене локации: личные, клановые, союзные и командные сообщения остаются сверху в прежнем порядке, а сообщения новой локации идут ниже, как во Flash. Лента больше не моргает и не прыгает при переходе.",
                    "Личное сообщение по Пробел+Shift отправляется как во Flash: только если Shift нажат, пока пробел ещё зажат. Если набрать пробел, а потом кавычки (Shift+2), сообщение больше не уходит.",
                    "Двойной щелчок по реликвии в сумке надевает её в свободный слот: сначала в основные, потом в запасные. Если заняты все восемь, реликвия заменяет надетые по кругу, а не всё время одну и ту же.",
                    "После сдачи фазы (Tab) кнопки удара, приёмов, умений, магии и предметов остаются доступны, пока раунд не ушёл в расчёт.",
                    "Клавиша, назначенная на «Преследовать цель», срабатывает и тогда, когда на кнопке «Отменить преследование».",
                    "В отчёт об ошибке теперь попадает журнал времени загрузок: по нему проще разбирать жалобы на долгие загрузки.",
                    "Починки: в ночном городе щелчки по арене и банку за обелиском больше не уводят в зал кланов; после выхода из боя общий чат локации больше не пропадает; стирание текста личного сообщения больше не сбрасывает получателя — он снимается, только если нажать Backspace в уже пустом поле.",
                },
            },
            new Entry
            {
                Version = "0.4.2",
                Lines = new[]
                {
                    "Ночная тема: при проверке обновлений мод задаёт один вопрос о загрузке — с общим размером города и башни, а не отдельно для каждой части.",
                },
            },
            new Entry
            {
                Version = "0.4.1",
                Lines = new[]
                {
                    "Починки: файлы ночной темы снова скачиваются — кнопка «Скачать» в настройках мода и вопрос о загрузке работают.",
                    "Ночная тема теперь по умолчанию выключена: включается одним переключателем в настройках мода.",
                },
            },
            new Entry
            {
                Version = "0.4.0",
                Lines = new[]
                {
                    "Ночная тема — один переключатель в настройках мода: ночной Иллениум вместо старого города, башня магии в 3D, готические окна покупок и свой экран загрузки. Файлы города и башни мод предлагает скачать один раз, там же кнопка проверки обновлений.",
                    "Ночной Иллениум грузится вместе с обычной загрузкой, без паузы после неё; экран загрузки держится, пока город не готов. Щелчок по воронке портала и верхушкам зданий теперь открывает здание.",
                    "Магазин магии в готическом оформлении; окна торговли заканчиваются над чатом и не закрывают его.",
                    "Порталы: у хранителя портала сразу открывается список направлений, выбор переносит без лишнего диалога.",
                    "Когда открывается окно покупок, кнопки мода по бокам и колонка персонажей плавно уезжают, а после закрытия возвращаются.",
                    "Кольцо загрузки заполняется плавно; вместо чёрного экрана перед загрузкой сразу показывается картинка.",
                    "Рынок: список лотов теперь долистывается до последнего лота.",
                    "Кланы: список кланов открывается с начала и больше не уползает вниз.",
                    "Переодевалка: база обновлена, около 3900 вещей; вещь, которой нет в базе, мод берёт прямо из игры.",
                    "Меню персонажа: если надеть вещь перетаскиванием не вышло, снятая вещь возвращается на место.",
                    "Сумка, вкладка «Снаряжение»: новая вещь сразу встаёт на своё место по уровню, а не в случайное место до повторного открытия сумки.",
                    "Flash-вид: кукла в окне персонажа помещается в окно и не упирается длинным оружием в ячейки.",
                    "Смена персонажа из панели мода больше не заканчивается экраном «Потеряно соединение».",
                    "«Сообщить об ошибке мода»: отчёт собирается в фоне, игра не подвисает.",
                    "Починки: окно выбора удара не открывается снова после сдачи фазы; кнопки локации над чатом держатся по центру.",
                },
            },
            new Entry
            {
                Version = "0.3.0",
                Lines = new[]
                {
                    "Чат: кнопка «Очистить чат» у поля ввода теперь стирает только ту вкладку, которая открыта, — «Чат» или «Системные». Вторая вкладка остаётся как была.",
                },
            },
            new Entry
            {
                Version = "0.2.0",
                Lines = new[]
                {
                    "Бой: павшие бойцы больше не стоят на ногах после перезахода в бой и при просмотре чужого боя — истлевшие тела убираются с поля.",
                    "Карта мира: когда кончается таймер режима движения, например марш-броска, персонаж в пути сразу идёт с новой скоростью, а не доходит до точки со старой.",
                    "Карта мира: окно выхода с карты мира подтверждается клавишей Enter.",
                    "Банки: правый щелчок по кнопке банки открывает рядом короткий список — банку в этом слоте можно сменить, не заходя в настройки. Число банок на кнопке теперь белое, его лучше видно.",
                    "Переодевалка: новая кнопка «Эликсиры». Отметь, сколько эликсиров каждой характеристики выпито, — мод учтёт их в статах и рейтинге и не даст выйти за предел своего уровня. «Обнулить» сбрасывает и эликсиры.",
                    "Переодевалка: кнопка «Копировать» делает копию текущего манекена и ставит её рядом.",
                    "Переодевалка: подсказка к надетой вещи разбита на разделы — параметры, энергия, броня, защита от магии, оружие.",
                    "Переодевалка: картинки вещей сохраняются на диск, и окно открывается быстрее; при смене расы манекен раздевается и статы сбрасываются к минимуму.",
                    "Калькулятор крафта — новое окно, кнопка в настройках игры под «Калькулятором КУ»: все рецепты игры с поиском по вещи и компоненту, раскрытие, из чего крафтится компонент, корзина и подсчёт, сколько всего нужно дропа.",
                    "Меню персонажа: вещь надевается двойным щелчком или перетаскиванием из сумки на слот, снимается двойным щелчком по слоту; вещи можно переставлять между основными и запасными слотами.",
                    "Сумка: на вкладке снаряжения вещи идут от высокого уровня к низкому, реликвии и серьги — в конце.",
                    "Ежедневный сундук: окно с сундуками стало меньше и не заезжает на чат.",
                    "Задания: отслеживаемые задания перечитываются первыми, сразу после входа в игру и после разговора с NPC. В списке справа от чата больше не висят задания, которых уже нет. Список может быть выше чата — до 60% экрана, а полоска для сворачивания стала короткой.",
                    "Чат на карте мира переживает бои: после боя возвращается общий чат той же карты, каким он был до боя; личные сообщения из боя остаются, стёртое кнопкой не возвращается.",
                    "Чат: Shift сразу после пробела переключает на личное сообщение там, где стоит курсор, а не только в конце строки, и лишний пробел в тексте не остаётся.",
                    "Чат: правый щелчок по имени адресата над полем ввода открывает меню игрока.",
                    "Чат: смайлики подстраиваются под размер шрифта чата и больше не выбиваются из строки; панель смайликов больше не прячется под другими окнами; кнопки «К» и «А» у поля ввода стали компактнее.",
                    "Flash-вид: колесо мыши над столбцом и списками мода больше не приближает поле боя.",
                    "Сосредоточенность по Shift больше не цепляется к мумификации — мумификация уходит сама по себе.",
                    "Починки: «Сообщить об ошибке мода» больше не пишет «Отчёт ушёл», если приёмник его на самом деле не принял — тогда отчёт остаётся в папке и мод прямо говорит, что прислать его нужно самому. Кнопка отчёта в настройках игры больше не остаётся низкой.",
                },
            },
            new Entry
            {
                Version = "0.1.0",
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
            catch (Exception e) { Plugin.Fault("[changelog] window: " + e); Close(); }
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
            Plugin.Log?.LogInfo("[changelog] version " + Plugin.Version + " - showing the changelog");
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
