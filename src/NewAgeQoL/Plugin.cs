using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace NewAgeQoL
{
    [BepInPlugin(Guid, "New Age QoL", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "newage.qol";
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> CfgArtifactButtons;
        internal static ConfigEntry<bool> CfgTownTournament;
        internal static ConfigEntry<int> CfgTownArenaId;
        internal static ConfigEntry<int> CfgTownTournamentId;
        internal static ConfigEntry<string> CfgTownArenaWords;
        internal static ConfigEntry<string> CfgTownTournamentWords;
        internal static ConfigEntry<string> CfgSeenVersion;
        internal static ConfigEntry<string> CfgHotkeys;
        internal static ConfigEntry<string> CfgHotkeysAll;
        internal static ConfigEntry<bool> CfgHotkeysDone;
        internal static ConfigEntry<string> CfgHotkeySeededOwn;
        internal static ConfigEntry<string> CfgHotkeySkills;
        internal static ConfigEntry<string> CfgHotkeySeeded;
        internal static ConfigEntry<string> CfgHotkeyOld;
        internal static ConfigEntry<bool> CfgHotkeyOldTaken;
        internal static ConfigEntry<string> CfgStash;
        internal static ConfigEntry<int> CfgContractsTabId;
        internal static ConfigEntry<int> CfgContractsIcon;
        internal static ConfigEntry<string> CfgContractsIconImage;
        internal static ConfigEntry<string> CfgContractCache;
        internal static ConfigEntry<string> CfgRecipeIconTabs;
        internal static ConfigEntry<bool> CfgFlaskFillToMax;
        internal static ConfigEntry<int> CfgFlaskHpId;
        internal static ConfigEntry<int> CfgFlaskManaId;
        internal static ConfigEntry<int> CfgFlaskEnergyId;
        internal static ConfigEntry<int> CfgFlaskMushroomId;
        internal static ConfigEntry<string> CfgFlaskHpName;
        internal static ConfigEntry<string> CfgFlaskManaName;
        internal static ConfigEntry<string> CfgFlaskEnergyName;
        internal static ConfigEntry<string> CfgFlaskMushroomName;
        internal static ConfigEntry<bool> CfgCounterAuto;
        internal static ConfigEntry<int> CfgCounterId;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<string> CfgManikin;
        internal static ConfigEntry<string> CfgStorageCache;
        internal static ConfigEntry<bool> CfgManikinOn;
        internal static ConfigEntry<string> CfgManikinTaken;
        internal static ConfigEntry<float> CfgCamZoom;
        internal static ConfigEntry<float> CfgCamPanExtra;
        internal static ConfigEntry<float> CfgCamRoomExtra;
        internal static ConfigEntry<string> CfgExpowerSeen;
        internal static ConfigEntry<float> CfgDialogScale;
        internal static ConfigEntry<string> CfgOnlineLogin;
        internal static ConfigEntry<string> CfgOnlinePassword;
        internal static ConfigEntry<string> CfgOnlineVersion;
        internal static ConfigEntry<string> CfgOnlineClanCache;
        internal static ConfigEntry<string> CfgOnlineClanNames;
        internal static ConfigEntry<bool> CfgOnlineByClan;
        internal static ConfigEntry<int> CfgLastCharacter;
        internal static ConfigEntry<string> CfgOnlineWindow;
        internal static ConfigEntry<string> CfgQuestWindow;
        internal static ConfigEntry<string> CfgQuestTracked;
        internal static ConfigEntry<bool> CfgDailyToast;
        internal static ConfigEntry<bool> CfgWalkHex;
        internal static ConfigEntry<bool> CfgWalkKeys;
        internal static ConfigEntry<bool> CfgWalkGlow;
        internal static ConfigEntry<float> CfgSoundVolume;
        internal static ConfigEntry<float> CfgSmallScreen;
        internal static ConfigEntry<string> CfgEffectsWindow;
        internal static ConfigEntry<bool> CfgHelpFolded;
        internal static ConfigEntry<bool> CfgTravelButton;
        internal static ConfigEntry<string> CfgTravelSpots;
        internal static ConfigEntry<string> CfgTravelGates;
        internal static ConfigEntry<int> CfgTravelTown;
        internal static ConfigEntry<int> CfgTravelOuter;
        internal static ConfigEntry<bool> CfgMapLabels;
        internal static ConfigEntry<bool> CfgMapLabelType;
        internal static ConfigEntry<bool> CfgMapLabelBillboard;
        internal static ConfigEntry<int> CfgMapLabelFont;
        internal static ConfigEntry<float> CfgMapLabelLift;
        internal static ConfigEntry<string> CfgMapLabelColor;
        internal static ConfigEntry<bool> CfgBranchesOn;
        internal static ConfigEntry<int> CfgBranchDays;
        internal static ConfigEntry<int> CfgBranchSearches;
        internal static ConfigEntry<int> CfgBranchPages;
        internal static ConfigEntry<float> CfgBranchGap;
        internal static ConfigEntry<int> CfgBranchHours;
        internal static ConfigEntry<int> CfgBranchMissHours;
        internal static ConfigEntry<int> CfgBranchShift;
        internal static ConfigEntry<int> CfgBranchKeep;
        internal static ConfigEntry<string> CfgBranchCache;
        internal static ConfigEntry<bool> CfgArmorZones;
        internal static ConfigEntry<bool> CfgTradeTotal;

        private void Awake()
        {
            Log = Logger;
            DiskJournal.Attach(Logger);
            Instance = this;
            ModSwitch.Bind(Config);
            Perf.Bind(Config);
            if (!ModSwitch.On)
            {
                _off = true;
                try { new Harmony(Guid).CreateClassProcessor(typeof(ModSwitchButtonPatch)).Patch(); }
                catch (System.Exception e) { Log.LogError("Harmony: ModSwitchButtonPatch — " + e.Message); }
                Log.LogInfo("Мод выключен: клиент работает как обычный, включить можно кнопкой «Включить мод» в настройках игры.");
                return;
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnScene;
            Chars.Home(Config);


            Chars.Own("Town", "ButtonGoesToTournament", false,
                "Кнопка возврата ведёт дальше города: сама зайдёт на арену и оттуда к турнирам. Это поведение ТОЛЬКО у кнопки: сдача вещей и поход, когда им нужен город, возвращаются просто в город.", e => CfgTownTournament = e);
            Chars.Own("Town", "ArenaDoorId", 0,
                "id двери арены в городе. 0 — искать дверь по ArenaDoorWords.", e => CfgTownArenaId = e);
            Chars.Own("Town", "TournamentDoorId", 0,
                "id двери турниров на арене. 0 — искать дверь по TournamentDoorWords.", e => CfgTownTournamentId = e);
            Chars.Own("Town", "ArenaDoorWords", "aren",
                "Части служебных имён двери арены среди дверей города: имени объекта, ключа подписи или имени картинки (через запятую, регистр не важен). Подпись на языке игры сюда не попадает. Если дверь не нашлась, полный список дверей с их id пишется в лог.", e => CfgTownArenaWords = e);
            Chars.Own("Town", "TournamentDoorWords", "tourn",
                "Части служебных имён двери турниров на арене: имени объекта, ключа подписи или имени картинки. Если дверь не нашлась, список дверей с их id пишется в лог.", e => CfgTownTournamentWords = e);

            Chars.Own("Town", "ArtifactButtons", true,
                "Показывать над кнопкой города кнопку хранилища. Пока вещи на руках, она сдаёт их в хранилище и переодевает в запасной набор, после этого меняет картинку и возвращает всё обратно. Мод сам доходит до банка и хранилища, пропуская пройденные шаги.", e => CfgArtifactButtons = e);

            Chars.Own("Inventory", "ContractsTabNumber", 40,
                "Номер вкладки «Контракты». Должен быть свободным: сервер шлёт номера 1…23.", e => CfgContractsTabId = e);
            Chars.Own("Inventory", "ContractsTabIcon", 16,
                "Запасная картинка вкладки «Контракты» — номер чужой вкладки, у которой её одолжить, пока мод не увидел ни одного контракта.", e => CfgContractsIcon = e);
            CfgContractsIconImage = Config.Bind("Inventory", "ContractsTabIconImage", "",
                "Картинка предмета-контракта для вкладки. Заполняется сама при первом увиденном контракте.");
            CfgContractCache = Config.Bind("Inventory", "ContractNumberCache", "",
                "Узнанные номера контрактов в виде «id:номер» через запятую. Заполняется сама и нужна, чтобы вкладка «Контракты» сразу открывалась по порядку, не дожидаясь названий с сервера.");

            Chars.Own("Flasks", "FillToMax", true,
                "ВКЛ: жизнь/мана/энергия — пить до ПОЛНОГО (после каждого глотка мод ждёт обновления полосы и останавливается, как только она заполнилась); грибы — пока сервер даёт, но не больше 20 штук за нажатие. ВЫКЛ: любая кнопка использует ровно ОДНУ штуку за нажатие.", e => CfgFlaskFillToMax = e);
            Chars.Own("Flasks", "HpThingId", 0,
                "thingId внебоевой банки ЖИЗНИ, выбирается в настройках мода. 0 — кнопки нет.", e => CfgFlaskHpId = e);
            Chars.Own("Flasks", "ManaThingId", 0,
                "thingId внебоевой банки МАНЫ, выбирается в настройках мода. 0 — кнопки нет.", e => CfgFlaskManaId = e);
            Chars.Own("Flasks", "EnergyThingId", 0,
                "thingId внебоевой банки ЭНЕРГИИ, выбирается в настройках мода. 0 — кнопки нет.", e => CfgFlaskEnergyId = e);
            Chars.Own("Flasks", "MushroomThingId", 0,
                "thingId грибов (пополнение зарядов), выбирается в настройках мода. 0 — кнопки нет.", e => CfgFlaskMushroomId = e);
            Chars.Own("Flasks", "HpThingName", "",
                "Старая настройка: названия больше не сравниваются. Если здесь что-то записано, а HpThingId = 0, мод один раз возьмёт из сумки банку жизни по списку известных id, запишет её id и очистит это поле.", e => CfgFlaskHpName = e);
            Chars.Own("Flasks", "ManaThingName", "",
                "Старая настройка, как HpThingName: переводится в ManaThingId по списку id банок маны.", e => CfgFlaskManaName = e);
            Chars.Own("Flasks", "EnergyThingName", "",
                "Старая настройка, как HpThingName: переводится в EnergyThingId по списку id банок энергии.", e => CfgFlaskEnergyName = e);
            Chars.Own("Flasks", "MushroomThingName", "",
                "Старая настройка, как HpThingName: переводится в MushroomThingId по списку id грибов.", e => CfgFlaskMushroomName = e);


            Chars.Own("Combat", "CounterOnPlayers", true,
                "Один раз за бой, в первой же фазе, ставить контрприём на себя, если среди врагов есть живой ИГРОК (хаотические бои, арена, нападение в мире): мобов это не касается, приёмами бьют только игроки. Дальше мод контрприём не обновляет — следующие ставишь сам. Заряды проверяются ОДИН раз, в начале боя: не хватало их на старте — в этом бою мод больше не лезет, даже если заряды потом пополнить.", e => CfgCounterAuto = e);
            Chars.Own("Combat", "CounterDodgeId", 4,
                "id приёма «Контрприем» среди приёмов. Менять не нужно: 4 — его номер в игре.", e => CfgCounterId = e);

            Chars.Own("Travel", "ShowButton", true,
                "Кнопка похода со списком точек: мод сам вернётся в город, выйдет во внешний мир и доведёт до выбранной точки. Дорогу видно строкой рядом с кнопкой, повторное нажатие пункта останавливает поход.", e => CfgTravelButton = e);
            Chars.Own("Travel", "Spots",
                "Скорпионы@1002:19, Баньши@1002:52, Сокрушители@1002:38, Тигры@1002:21, Зомби@1002:51, Ифриты@1002:34, "
                + "Рыцари смерти@1003:47, Наги@1003:63, Сатиры@1003:31, "
                + "Дриады@1005:19, Миносы@1005:57, Личи@1005:41",
                "Пункты списка: «название@участок:вершина» через запятую, в том порядке, в каком они нужны. Номер вершины — тот же, что мод подписывает на карте, а участок нужен потому, что номера у участков свои. Дорогу на другой участок мод прокладывает сам по разделу Gates. Участок можно и не писать («Руины:60») — тогда мод ищет вершину на той карте, где стоишь.", e => CfgTravelSpots = e);
            Chars.Own("Travel", "Gates",
                "1002>1003:64, 1002>1006:69, 1002>1014:75, 1002>1011:79, 1003>1002:15, 1003>1015:69, 1004>1014:69, 1007>1008:14, 1007>1009:34, 1007>1024:29, 1008>1007:0, 1009>1010:22, 1009>1007:0, 1015>1003:0, 1015>1023:24, 1024>1007:0, 1006>1002:0, 1006>1014:21, 1011>1002:0, 1011>1014:9, 1011>1012:28, 1011>1005:35, 1011>1021:18, 1014>1002:0, 1014>1004:14, 1014>1011:17, 1014>1006:24, 1018>1019:17, 1018>1020:35, 1019>1018:0, 1019>1020:18, 1005>1011:0",
                "Переходы между участками: «откуда>куда:вершина». По ним мод сам прокладывает дорогу к нужному участку, хоть через несколько переходов, и подтверждает каждый за игрока. 1002>1011:79 — с внешнего мира на соседний участок через v79, обратно — через v0.", e => CfgTravelGates = e);

            Upgrade(CfgTravelSpots,
                "Скорпионы:19, Баньши:52, Сокрушители:38, Тигры:21, Зомби:51, Ифриты:34",
                "Скорпионы@1002:19, Баньши@1002:52, Сокрушители@1002:38, Тигры@1002:21, Зомби@1002:51, Ифриты@1002:34, "
                + "Рыцари смерти@1003:47, Наги@1003:63, Сатиры@1003:31");
            Upgrade(CfgTravelGates, "1002>1003:64, 1003>1002:15", "1002>1003:64, 1003>1002:15, 1002>1011:79, 1011>1002:0, 1011>1005:35, 1005>1011:0", "1002>1003:64, 1002>1006:69, 1002>1014:75, 1002>1011:79, 1003>1002:15, 1003>1015:69, 1004>1014:69, 1006>1002:0, 1006>1014:21, 1008>1007:0, 1009>1007:0, 1009>1010:22, 1011>1002:0, 1011>1014:9, 1011>1012:28, 1011>1005:35, 1011>1021:18, 1014>1002:0, 1014>1004:14, 1014>1011:17, 1014>1006:24, 1015>1003:0, 1015>1023:24, 1018>1019:17, 1018>1020:35, 1019>1018:0, 1019>1020:18, 1005>1011:0");
            Chars.Own("Travel", "TownLocation", 2,
                "id города, из которого начинается поход. 2 — Иллениум.", e => CfgTravelTown = e);
            Chars.Own("Travel", "OuterWorldLocation", 1002,
                "id участка карты, куда выводит выход из этого города. 1002 — внешний мир вокруг Иллениума.", e => CfgTravelOuter = e);

            Chars.Own("Map", "VertexLabels", false,
                "Подписывать точки внешнего мира их номером: «v12». По номеру видно, куда ведёт дорога, им удобно объяснять маршрут другим и задавать точки в списке похода.", e => CfgMapLabels = e);
            Chars.Own("Map", "VertexLabelType", false,
                "Дописывать к номеру, что это за точка: «бой», «переход» (сохранение, вход в город или на соседний участок) или «дорога».", e => CfgMapLabelType = e);
            Chars.Own("Map", "VertexLabelBillboard", true,
                "Держать метку повёрнутой к камере, чтобы она читалась при любом наклоне карты.", e => CfgMapLabelBillboard = e);
            Chars.Own("Map", "VertexLabelSharpness", 48,
                "Разрешение шрифта метки. Влияет на чёткость, а не на размер.", e => CfgMapLabelFont = e);
            Chars.Own("Map", "VertexLabelLift", 0.7f,
                "Насколько поднять метку над точкой, чтобы она не легла на саму иконку.", e => CfgMapLabelLift = e);
            Chars.Own("Map", "VertexLabelColor", "#FFEE00",
                "Цвет метки в виде #RRGGBB.", e => CfgMapLabelColor = e);

            Chars.Watch((s, e) =>
            {
                if (e.ChangedSetting == null) return;
                if (e.ChangedSetting.Definition.Section == "Map") MapLabels.Refresh();
            });



            CfgHotkeysAll = Config.Bind("Hotkeys", "Shared", "",
                "Горячие клавиши мода, общие для всех персонажей аккаунта: «действие=клавиши» через |. Окна, мир, бой, банки и приёмы: приёмы у всех персонажей одни и те же. Умения и заклинания у каждого персонажа свои и лежат в его файле. Заполняется из окна горячих клавиш.");
            CfgHotkeyOld = Config.Bind("Hotkeys", "SharedSkillKeys", "",
                "Клавиши заклинаний, которые раньше были общими на аккаунт: «действие=клавиши» через |. Теперь заклинания у каждого персонажа свои, и мод один раз раздаёт отсюда каждому персонажу те, которые тот знает. Заполняется сам.");
            CfgHotkeysDone = Config.Bind("Hotkeys", "SharedReady", false,
                "Служебная отметка: общий список клавиш аккаунта уже заведён. Пока её нет, мод один раз возьмёт общие клавиши у персонажа, которым зайдёшь первым. После этого пустой общий список — твоя настройка, старые клавиши из файлов персонажей мод больше не подтягивает.");
            CfgHotkeySeeded = Config.Bind("Hotkeys", "Seeded", "",
                "Действия, которым мод уже один раз выставил клавишу по умолчанию, через |. Нужно, чтобы новые клавиши доехали до старых настроек, но не возвращались, если ты их снял.");
            Chars.Fresh("Hotkeys", "Seeded", "",
                "Старые отметки клавиш по умолчанию этого персонажа. Мод один раз переносит их в общий файл аккаунта и дальше не использует.", e => CfgHotkeySeededOwn = e);
            Chars.Fresh("Hotkeys", "Bindings", "",
                "Клавиши умений и заклинаний этого персонажа: «действие=клавиши» через |. Они только его: на другом персонаже та же клавиша свободна. Приёмы и остальные клавиши общие для аккаунта и лежат в newage.qol.cfg.", e => CfgHotkeys = e);
            Chars.Fresh("Hotkeys", "SkillKeysTaken", false,
                "Служебная отметка: этот персонаж уже забрал себе старые общие клавиши заклинаний из newage.qol.cfg. Дальше его клавиши заклинаний только его.", e => CfgHotkeyOldTaken = e);
            Chars.Fresh("Hotkeys", "KnownSkills", "",
                "Список умений, приёмов и заклинаний персонажа, чтобы их можно было назначить вне боя. Заполняется сам.", e => CfgHotkeySkills = e);
            Chars.Own("Mod", "SeenVersion", "",
                "Версия мода, список изменений которой уже показали. Заполняется сама.", e => CfgSeenVersion = e);
            Chars.Fresh("Town", "StashedArtifacts", "",
                "Что нужно вернуть после сдачи, в виде «вещь;слот» через запятую. Слот 0 значит, что вещь была в сумке. Заполняется и очищается кнопкой хранилища.", e => CfgStash = e);


            Chars.Own("Inventory", "RecipeIconTabs", "5,15,17,22",
                "Номера вкладок, где рецепт показывается иконкой создаваемой вещи.", e => CfgRecipeIconTabs = e);


            Chars.Fresh("Artifacts", "Manikin", "",
                "Запасной набор: во что переодеться, когда вещи сданы в хранилище. Пары «слот:номер вещи» через запятую, заполняется сам из окна набора в настройках мода.", e => CfgManikin = e);
            Chars.Fresh("Artifacts", "ManikinWorn", false,
                "Сейчас надет запасной набор, а прежние вещи ждут возврата. Ставится и снимается сам кнопками сдачи и возврата.", e => CfgManikinOn = e);
            Chars.Fresh("Artifacts", "SpareSetTaken", "",
                "Что запасной набор взял из хранилища, пары «номер вещи:количество». При возврате эти вещи уезжают обратно в хранилище, остальные остаются в сумке. Заполняется само.", e => CfgManikinTaken = e);
            Chars.Fresh("Artifacts", "StorageCache", "",
                "Что мод в последний раз видел в хранилище, пары «номер вещи:количество». Нужно, чтобы окно запасного набора показывало вещи из хранилища, когда ты не в нём. Заполняется само.", e => CfgStorageCache = e);
            Chars.Own("Online", "Login", "",
                "Логин запасного аккаунта для списка «Кто в игре». Пусто — кнопка подскажет, что настроить.", e => CfgOnlineLogin = e);
            Chars.Own("Online", "Password", "",
                "Пароль запасного аккаунта. Хранится в этом файле открытым текстом.", e => CfgOnlinePassword = e);
            Chars.Own("Online", "FlashVersion", "11073",
                "Номер версии старого 2D-клиента, который мод называет серверу при входе запасным аккаунтом. Менять только если сервер отвечает «Обновите версию игры».", e => CfgOnlineVersion = e);
            CfgOnlineClanCache = Config.Bind("Online", "ClanIconCache", "",
                "Узнанные коды значков кланов для окна «Кто в игре» в виде «значок:код» через запятую. Заполняется само, чтобы значки появлялись сразу.");
            CfgOnlineClanNames = Config.Bind("Online", "ClanNameCache", "",
                "Узнанные названия кланов в виде «значок=название» через запятую. Заполняется само, чтобы в режиме «по кланам» заголовки групп были с названиями, а не с именами значков.");
            Chars.Own("Online", "GroupByClan", true,
                "В окне «Кто в игре» группировать игроков по кланам: сначала заголовок клана, под ним его игроки. ВЫКЛ — общим списком по уровню. По умолчанию ВКЛ. Переключается кнопкой в самом окне.", e => CfgOnlineByClan = e);
            Chars.Once("Online", "GroupByClanOn",
                "Служебная отметка: группировка по кланам после обновления мода уже включена. Пока её нет, мод один раз включит её, даже если в старых настройках она была выключена. После этого выключенная группировка — твоя настройка, мод её не трогает.", () => CfgOnlineByClan.Value = true);
            ChatColors.Bind();
            Chars.Own("Combat", "CameraRange", 1f,
                "Насколько дальше игрового можно ОТДАЛИТЬ камеру в бою: 1 — как в игре, 2 — вдвое дальше и выше, до 4. Приближение остаётся игровым, ближе игрового предела камера не подойдёт. Мод только раздвигает дальний предел, управление игровое: колесо мыши и перетаскивание. Действует только в обычном 3D-виде боя: во Flash-виде камерой управляет сам Flash-вид, и эта настройка на него не влияет.", e => CfgCamZoom = e);
            Chars.Own("Combat", "WalkHexHighlight", true,
                "В фазе ходьбы подсвечивать клетку под мышью, чтобы было видно, куда именно попадёт клик.", e => CfgWalkHex = e);
            Chars.Own("Combat", "WalkKeys", true,
                "В фазе ходьбы выбирать клетку стрелками на клавиатуре, а Enter'ом идти на выбранную. Выбранная клетка обведена жёлтым; сдвинул мышь — обвод снова идёт за ней. Клавиши меняются в окне горячих клавиш.", e => CfgWalkKeys = e);
            Chars.Own("Combat", "WalkGlowFix", true,
                "Убирать залипшую подсветку клеток: в фазе боя снимаются остатки зоны ходьбы, в фазе ходьбы подсвечены только клетки, куда можно дойти с текущими очками хода. Круг вокруг монолитов, мемориалов и призванных существ мод переставляет следом за хозяином, если того перенесло по полю (например, «Изгибом реальности»), и снимает круг, когда хозяина не стало. Любая другая заливка клеток снимается как забытая: остаются только своё выделение, круги живых хозяев, прицел мода и метка открытого окна выбора действия. Заодно мод возвращает свой цвет клеткам, на которых краска осталась от выделения, которое игра забыла снять.", e => CfgWalkGlow = e);
            Chars.Own("Combat", "EffectsWindow", "",
                "Положение окна «Эффекты» в виде «x;y». Заполняется само при перетаскивании.", e => CfgEffectsWindow = e);
            Chars.Own("Help", "Folded", false,
                "Столбец «Помощь» справа свёрнут. Меняется кнопкой со стрелкой над столбцом, состояние сохраняется между боями и входами в игру.", e => CfgHelpFolded = e);
            Chars.Own("Sounds", "FlashVolume", 1f,
                "Громкость звуков из старого клиента, от 0 (тишина) до 1. Не зависит от громкости в настройках игры: та остаётся для звуков самой игры. Заменённые звуки игры молчат в любом случае, пока включён звук мода.", e => CfgSoundVolume = e);
            CfgSmallScreen = Config.Bind("Interface", "SmallScreenScale", 1.2f,
                "Во сколько раз крупнее делать интерфейс мода (чат, приёмы, подсказки, кнопки) на экранах ниже 1080 точек по высоте: 1 — как в игре, 1.2 — на пятую часть крупнее, до 1.5. Крупнее пиксель в пиксель не растёт, на 1080p и больше ничего не меняется.");
            CfgSoundVolume.Value = UnityEngine.Mathf.Clamp01(CfgSoundVolume.Value);
            CfgCamZoom.Value = UnityEngine.Mathf.Clamp(CfgCamZoom.Value, 1f, 4f);
            Chars.Own("Combat", "CameraPanExtra", 0.5f,
                "Небольшой запас в единицах поля сверх того, что нужно, чтобы показать клетки под панелью чата: мод сам считает высоту панели в единицах поля и раздвигает границу камеры ровно на неё, а это число добавляется сверху (0 — ровно, до 20).", e => CfgCamPanExtra = e);
            CfgCamPanExtra.Value = UnityEngine.Mathf.Clamp(CfgCamPanExtra.Value, 0f, 20f);
            if (UnityEngine.Mathf.Abs(CfgCamPanExtra.Value - 4f) < 0.01f) CfgCamPanExtra.Value = 0.5f;
            if (UnityEngine.Mathf.Abs(CfgCamPanExtra.Value - 8f) < 0.01f) CfgCamPanExtra.Value = 0.5f;
            if (UnityEngine.Mathf.Abs(CfgCamPanExtra.Value - 5f) < 0.01f) CfgCamPanExtra.Value = 0.5f;
            Chars.Own("Combat", "CameraRoomExtra", 0f,
                "Насколько раздвинуть границы камеры боя во все стороны, когда включена панель чата, в единицах поля (0 — как в игре, до 20).", e => CfgCamRoomExtra = e);
            if (UnityEngine.Mathf.Abs(CfgCamRoomExtra.Value - 3f) < 0.01f) CfgCamRoomExtra.Value = 0f;
            if (UnityEngine.Mathf.Abs(CfgCamRoomExtra.Value - 2f) < 0.01f) CfgCamRoomExtra.Value = 0f;
            CfgCamRoomExtra.Value = UnityEngine.Mathf.Clamp(CfgCamRoomExtra.Value, 0f, 20f);
            Chars.Own("Combat", "ResultDialogScale", 0.7f,
                "Размер окон «Победа»/«Поражение» после боя и окна разведки «Нападение»: 1 — как в игре, 0.4 — меньше вдвое с лишним. Окно остаётся по центру экрана.", e => CfgDialogScale = e);
            CfgDialogScale.Value = UnityEngine.Mathf.Clamp(CfgDialogScale.Value, 0.4f, 1f);
            Chars.Fresh("State", "ExpowerSeen", "",
                "Служебное: наибольший предел зарядов, который игра показывала каждому персонажу. Заполняется само.", e => CfgExpowerSeen = e);
            Chars.Own("Quests", "DailyTaskPopups", false,
                "Показывать всплывающие карточки хода ежедневных заданий над чатом: «Торжество IV, 1/10» и подобные. ВЫКЛ — не показывать, весь список всё равно открывается кнопкой заданий дня. Окно уже выполненного задания с наградой этой галкой не трогается, оно открывается как обычно.", e => CfgDailyToast = e);
            Chars.Own("Quests", "Window", "",
                "Положение и размер окна «Задания» в виде «x;y;высота;ширина». Заполняется само, когда окно двигаешь или тянешь за нижний или правый край. Пусто — по центру, размер по умолчанию.", e => CfgQuestWindow = e);
            Chars.Own("Quests", "Tracked", "",
                "Задания, за которыми следишь, — номера через «;». Заполняется само кнопкой «Следить» в окне «Задания», крестик в списке справа от чата убирает задание. Пусто — список не показывается.", e => CfgQuestTracked = e);
            Chars.Own("Online", "Window", "",
                "Положение и размер окна «Кто в игре» в виде «x;y;высота;ширина». Заполняется само, когда окно двигаешь или тянешь за нижний или правый край. Пусто — по центру, размер по умолчанию.", e => CfgOnlineWindow = e);
            CfgLastCharacter = Config.Bind("Launch", "LastCharacter", 0,
                "id персонажа, которым ты в последний раз входил в игру через этот клиент. Заполняется само. На экране выбора персонажа мод сразу показывает его, а не того, кто заходил последним по данным сервера (например, запасного для окна «Кто в игре»). 0 — как в игре.");
            Chars.Own("Branches", "ShowBranches", true,
                "В подсказке бойца показывать его ветку классовых умений и ветку элитных. Мод ищет их в архиве битв на сайте игры по открытым логам боёв: одно классовое умение в логе однозначно называет ветку. Найденное запоминается, чтобы не спрашивать сайт заново.", e => CfgBranchesOn = e);
            Chars.Own("Branches", "SearchDays", 7,
                "На сколько дней назад заглядывать в архив битв, от 1 до 14. Архив ищет окнами по два дня, так что 7 дней — это до четырёх запросов поиска на бойца, и то лишь пока ветка не найдена.", e => CfgBranchDays = e);
            CfgBranchDays.Value = UnityEngine.Mathf.Clamp(CfgBranchDays.Value, 1, 14);
            Chars.Own("Branches", "SearchesPerFight", 60,
                "Предохранитель: сколько поисков в архиве разрешено на один бой. Не для экономии, а чтобы мод не зациклился — мод ищет по ВСЕМ бойцам боя, пока не обойдёт всех.", e => CfgBranchSearches = e);
            CfgBranchSearches.Value = UnityEngine.Mathf.Clamp(CfgBranchSearches.Value, 1, 200);
            Chars.Own("Branches", "LogsPerFight", 30,
                "Предохранитель: сколько страниц с логами боёв разрешено скачать на один бой. Мод берёт те бои, где неизвестных бойцов больше всего, начиная со свежих.", e => CfgBranchPages = e);
            CfgBranchPages.Value = UnityEngine.Mathf.Clamp(CfgBranchPages.Value, 1, 200);
            Chars.Own("Branches", "SecondsBetweenRequests", 1.5f,
                "Пауза между обращениями к сайту, в секундах. Меньше 0.25 не ставится: сайт игры не должен получать от мода поток запросов.", e => CfgBranchGap = e);
            CfgBranchGap.Value = UnityEngine.Mathf.Clamp(CfgBranchGap.Value, 0.25f, 30f);
            Chars.Own("Branches", "KnownHours", 24,
                "Сколько часов доверять уже найденной ветке, прежде чем проверить её заново, от 1 до 720. Люди иногда меняют ветки, поэтому раз в сутки мод перепроверяет.", e => CfgBranchHours = e);
            CfgBranchHours.Value = UnityEngine.Mathf.Clamp(CfgBranchHours.Value, 1, 720);
            Chars.Own("Branches", "UnknownHours", 6,
                "Сколько часов ждать перед новой попыткой по бойцу, о котором ничего не нашлось или нашлась только половина, от 1 до 720.", e => CfgBranchMissHours = e);
            CfgBranchMissHours.Value = UnityEngine.Mathf.Clamp(CfgBranchMissHours.Value, 1, 720);
            Chars.Own("Branches", "SiteHoursFromUtc", 3,
                "На сколько часов время сайта игры опережает UTC. Нужно, чтобы у поиска были те же даты, что в архиве битв.", e => CfgBranchShift = e);
            CfgBranchShift.Value = UnityEngine.Mathf.Clamp(CfgBranchShift.Value, -12, 14);
            Chars.Own("Branches", "RememberFighters", 4000,
                "Сколько бойцов держать в памяти веток, от 200 до 20000. Лишние вытесняются, начиная с самых давних.", e => CfgBranchKeep = e);
            CfgBranchKeep.Value = UnityEngine.Mathf.Clamp(CfgBranchKeep.Value, 200, 20000);
            Chars.Own("Info", "ArmorByZones", true,
                "Показывать броню по точкам: в карточке игрока пятью строками (голова, туловище, руки, ноги) и в окне атаки под именем цели. Если выключено, в карточке одна строка — средняя броня по пяти точкам.", e => CfgArmorZones = e);
            Chars.Own("Info", "ProfessionTotal", true,
                "На вкладке «Профессии» писать справа от названия изученной профессии её общее значение. Полоска под названием показывает только остаток до следующего уровня, по ней не видно, сколько набрано всего.", e => CfgTradeTotal = e);
            CfgBranchCache = Config.Bind("Branches", "Cache", "",
                "Служебное: найденные ветки в виде «id.класс.ветка.элита.час» через точку с запятой. Заполняется само.");

            Chars.Own("Log", "Verbose", true,
                "Писать подробности работы: что нажато, что найдено в сумке, какие окна перестроены. Подробности идут только в BepInEx/cache/NewAgeQoL/qol.log, он ограничен двумя файлами по 10 МБ; в LogOutput.log попадают только ошибки и важные события.", e => CfgVerbose = e);
            Chars.Once("Log", "VerboseQuiet",
                "Служебная отметка: подробный журнал после обновления мода уже выключен. Пока её нет, мод один раз выключит журнал, даже если в старых настройках он был включён. После этого включённый журнал — твоя настройка, мод его не трогает.", () => CfgVerbose.Value = false);
            Chars.Once("Log", "VerboseDebug",
                "Служебная отметка: на время отладки подробный журнал уже включён. Пока её нет, мод один раз включит журнал, даже если в старых настройках он был выключен. После этого выключенный журнал — твоя настройка, мод его не трогает.", () => CfgVerbose.Value = true);

            Chars.Legacy(CfgLastCharacter.Value);

            try
            {
                var harmony = new Harmony(Guid);
                foreach (var t in typeof(Plugin).Assembly.GetTypes())
                {
                    if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                    try { harmony.CreateClassProcessor(t).Patch(); }
                    catch (System.Exception e) { Log.LogError("Harmony: " + t.Name + " — " + e.Message); }
                }
            }
            catch (System.Exception e) { Log.LogError("Harmony: " + e); }

            Log.LogInfo("New Age QoL " + Version + " загружен.");
        }

        private static void Upgrade(ConfigEntry<string> cfg, params string[] wasBefore)
        {
            if (cfg == null) return;
            foreach (var old in wasBefore)
                if (cfg.Value == old) { cfg.Value = (string)cfg.DefaultValue; return; }
        }

        private sealed class Site
        {
            internal System.DateTime At;
            internal int Hushed;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Site> Sites =
            new System.Collections.Generic.Dictionary<string, Site>();

        private static string Tag(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '[') return "";
            int end = text.IndexOf(']');
            return end > 0 ? text.Substring(0, end + 1) : "";
        }

        private static bool Allow(string text, string who, int line, double gap, out int hushed)
        {
            hushed = 0;
            string key = Tag(text) + who + ":" + line;
            var now = System.DateTime.UtcNow;
            lock (Sites)
            {
                Site site;
                if (!Sites.TryGetValue(key, out site)) { Sites[key] = new Site { At = now }; return true; }
                if ((now - site.At).TotalSeconds < gap) { site.Hushed++; return false; }
                hushed = site.Hushed;
                site.Hushed = 0;
                site.At = now;
                return true;
            }
        }

        private static string Again(int hushed)
        {
            return hushed > 0 ? " (и ещё " + hushed + " таких же подряд)" : "";
        }

        internal static void Trace(string text,
            [System.Runtime.CompilerServices.CallerMemberName] string who = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (CfgVerbose == null || !CfgVerbose.Value || Log == null) return;
            int hushed;
            if (!Allow(text, who, line, 2d, out hushed)) return;
            Log.LogDebug(text + Again(hushed));
        }

        internal static void Warn(string text,
            [System.Runtime.CompilerServices.CallerMemberName] string who = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Log == null) return;
            int hushed;
            if (!Allow(text, who, line, 60d, out hushed)) return;
            Log.LogWarning(text + Again(hushed));
        }

        internal static void Fault(string text,
            [System.Runtime.CompilerServices.CallerMemberName] string who = null,
            [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (Log == null) return;
            int hushed;
            if (!Allow(text, who, line, 60d, out hushed)) return;
            Log.LogError(text + Again(hushed));
        }

        private static float _updateAt = 20f;

        private void OnScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Wake();
        }

        internal static void Wake()
        {
            Curtain.Raise();
            try
            {
                CombatBar.Wake();
                SideButtons.Wake();
                ChatDock.Wake();
                Enchantments.Wake();
                HelpColumn.Wake();
                SpeedBar.Wake();
                Chaotic.Wake();
                ShopCards.Wake();
                Counter.Wake();
                Contracts.Wake();
                CombatCam.Wake();
            }
            catch (System.Exception e) { Trace("[сцена] пробуждение: " + e.Message); }
        }

        private sealed class Part
        {
            internal string Name;
            internal System.Action Do;
            internal float Said;
            internal Meter Meter;
        }

        private static Part[] _parts;
        private static Part[] _late;

        private static void Renew()
        {
            if (_updateAt > 0f && UnityEngine.Time.unscaledTime > _updateAt) { _updateAt = 0f; Updater.CheckSilent(); }
        }

        private static Part[] Parts()
        {
            if (_parts != null) return _parts;
            _parts = new[]
            {
                new Part { Name = "Chars.Tick", Do = Chars.Tick },
                new Part { Name = "SideButtons.Tick", Do = SideButtons.Tick },
                new Part { Name = "Flasks.Tick", Do = Flasks.Tick },
                new Part { Name = "Settings.Tick", Do = Settings.Tick },
                new Part { Name = "Changelog.Tick", Do = Changelog.Tick },
                new Part { Name = "Hotkeys.Tick", Do = Hotkeys.Tick },
                new Part { Name = "Updater", Do = Renew },
                new Part { Name = "SlotSwap.Tick", Do = SlotSwap.Tick },
                new Part { Name = "ContractNumbers.Tick", Do = ContractNumbers.Tick },
                new Part { Name = "Counter.Tick", Do = Counter.Tick },
                new Part { Name = "Market.Tick", Do = Market.Tick },
                new Part { Name = "SkillList.Tick", Do = SkillList.Tick },
                new Part { Name = "SkillList.Aim", Do = SkillList.Aim },
                new Part { Name = "Focus.Tick", Do = Focus.Tick },
                new Part { Name = "Chest.Tick", Do = Chest.Tick },
                new Part { Name = "Roster.Tick", Do = Roster.Tick },
                new Part { Name = "UiScale.Tick", Do = UiScale.Tick },
                new Part { Name = "HintZones.Tick", Do = HintZones.Tick },
                new Part { Name = "Dialogs.Tick", Do = Dialogs.Tick },
                new Part { Name = "Claims.Tick", Do = Claims.Tick },
                new Part { Name = "ClanMark.Tick", Do = ClanMark.Tick },
                new Part { Name = "TabPanelFitPatch.Tick", Do = TabPanelFitPatch.Tick },
                new Part { Name = "Workshop.Net", Do = Workshop.Net },
                new Part { Name = "Strike.Tick", Do = Strike.Tick },
                new Part { Name = "WalkKeys.Tick", Do = WalkKeys.Tick },
                new Part { Name = "WalkGlow.Tick", Do = WalkGlow.Tick },
                new Part { Name = "Cards.Tick", Do = Cards.Tick },
                new Part { Name = "WalkHex.Tick", Do = WalkHex.Tick },
                new Part { Name = "Notice.Tick", Do = Notice.Tick },
                new Part { Name = "CombatCam.Tick", Do = CombatCam.Tick },
                new Part { Name = "FlaskPicker.Tick", Do = FlaskPicker.Tick },
                new Part { Name = "OnlineList.Tick", Do = OnlineList.Tick },
                new Part { Name = "OnlineWindow.Tick", Do = OnlineWindow.Tick },
                new Part { Name = "QuestBoard.Tick", Do = QuestBoard.Tick },
                new Part { Name = "QuestWindow.Tick", Do = QuestWindow.Tick },
                new Part { Name = "QuestTrack.Tick", Do = QuestTrack.Tick },
                new Part { Name = "CultPotions.Tick", Do = CultPotions.Tick },
                new Part { Name = "TravelEnter.Tick", Do = TravelEnter.Tick },
                new Part { Name = "MovePace.Tick", Do = MovePace.Tick },
                new Part { Name = "Spectate.Tick", Do = Spectate.Tick },
                new Part { Name = "Manikin.Tick", Do = Manikin.Tick },
                new Part { Name = "FighterHint.Tick", Do = FighterHint.Tick },
                new Part { Name = "BodyClick.Tick", Do = BodyClick.Tick },
                new Part { Name = "Branches.Tick", Do = Branches.Tick },
                new Part { Name = "Armor.Tick", Do = Armor.Tick },
                new Part { Name = "AttackHead.Tick", Do = AttackHead.Tick },
                new Part { Name = "EffectsWindow.Tick", Do = EffectsWindow.Tick },
                new Part { Name = "ChatDock.Tick", Do = ChatDock.Tick },
                new Part { Name = "Smiles.Tick", Do = Smiles.Tick },
                new Part { Name = "CombatBar.Tick", Do = CombatBar.Tick },
                new Part { Name = "LeftColumn.Tick", Do = LeftColumn.Tick },
                new Part { Name = "RealTime.Tick", Do = RealTime.Tick },
                new Part { Name = "Enchantments.Tick", Do = Enchantments.Tick },
                new Part { Name = "Quickslots.Tick", Do = Quickslots.Tick },
                new Part { Name = "HelpColumn.Tick", Do = HelpColumn.Tick },
                new Part { Name = "SpeedBar.Tick", Do = SpeedBar.Tick },
                new Part { Name = "Probe.Tick", Do = Probe.Tick },
                new Part { Name = "Chaotic.Tick", Do = Chaotic.Tick },
                new Part { Name = "Sounds.Tick", Do = Sounds.Tick },
                new Part { Name = "Curtain.Tick", Do = Curtain.Tick },
            };
            return _parts;
        }

        private static Part[] Late()
        {
            if (_late != null) return _late;
            _late = new[]
            {
                new Part { Name = "LeftColumn.Keep", Do = LeftColumn.Keep },
                new Part { Name = "CombatBar.Keep", Do = CombatBar.Keep },
                new Part { Name = "Chaotic.Keep", Do = Chaotic.Keep },
                new Part { Name = "ChatPick.Tick", Do = ChatPick.Tick },
            };
            return _late;
        }

        private static void Run(Part[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var mark = Perf.Mark();
                try { part.Do(); }
                catch (System.Exception e) { Stumble(part, e); }
                if (mark.At == 0L) continue;
                if (part.Meter == null) part.Meter = Perf.Track("мод", part.Name);
                Perf.Add(part.Meter, mark);
            }
        }

        private static void Stumble(Part part, System.Exception e)
        {
            if (part.Said > 0f && UnityEngine.Time.unscaledTime - part.Said < 5f) return;
            part.Said = UnityEngine.Time.unscaledTime;
            Log?.LogWarning("[кадр] " + part.Name + " споткнулся, остальное мод доделал: " + e);
        }

        private static bool _off;

        private void LateUpdate()
        {
            if (_off) return;
            Run(Late());
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnScene;
            CultPotions.Shutdown();
            DressDrag.Shutdown();
            DiskJournal.Detach(Logger);
        }

        private void Update()
        {
            Perf.Tick();
            if (_off) return;
            Run(Parts());
        }
    }
}
