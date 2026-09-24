using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NewAge2D;

[BepInPlugin(Guid, "New Age 2D", Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "newage.2d";
    public const string Version = "0.2.12";

    internal static ManualLogSource Log;
    internal static Plugin Instance;
    internal static SwfStore Store;

    private Harmony _harmony;

    internal static ConfigEntry<bool> CfgEnabled;
    internal static ConfigEntry<bool> CfgUnityOnce;
    internal static ConfigEntry<bool> CfgVerbose;
    internal static ConfigEntry<bool> CfgTrace;
    internal static ConfigEntry<bool> CfgQuiet;
    internal static ConfigEntry<float> CfgScale;
    internal static ConfigEntry<string> CfgServer;
    internal static ConfigEntry<bool> CfgCombat;
    internal static ConfigEntry<float> CfgDollSize;
    internal static ConfigEntry<bool> CfgCombatFlip;
    internal static ConfigEntry<bool> CfgFlashSpeed;
    internal static ConfigEntry<float> CfgHexSeconds;
    internal static ConfigEntry<bool> CfgCompress;
    internal static ConfigEntry<float> CfgDepthBias;
    internal static ConfigEntry<bool> CfgFlashHexes;
    internal static ConfigEntry<UnityEngine.Color> CfgHexColor;
    internal static ConfigEntry<float> CfgHeight;
    internal static ConfigEntry<bool> CfgFitCapsule;
    internal static ConfigEntry<float> CfgBarsLift;
    internal static ConfigEntry<bool> CfgKeepMagic;
    internal static ConfigEntry<bool> CfgHoverGlow;
    internal static ConfigEntry<bool> CfgFlashMagic;
    internal static ConfigEntry<bool> CfgSpellMarks;
    internal static ConfigEntry<float> CfgVivid;
    internal static ConfigEntry<int> CfgFrameMemory;
    internal static ConfigEntry<bool> CfgField;
    internal static ConfigEntry<float> CfgFieldView;
    internal static ConfigEntry<float> CfgFieldOffsetX;
    internal static ConfigEntry<float> CfgFieldOffsetY;
    internal static ConfigEntry<int> CfgFieldZone;
    internal static ConfigEntry<float> CfgSoundBefore;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        Trace.MainThreadId = Thread.CurrentThread.ManagedThreadId;
        Journal.Attach(Log);

        CfgEnabled = Config.Bind("General", "Enabled", false, "Рисовать Flash-куклу вместо 3D-модели в окне персонажа и в бою. ВЫКЛ — везде модели и анимации Unity. Это главный выключатель: при ВЫКЛ разделы Combat и Field не работают.");
        CfgVerbose = Config.Bind("General", "Verbose", true, "Подробный журнал");
        CfgTrace = Config.Bind("General", "Trace", true, "Журнал боя по кадрам: появление и пропадание кукол, 3D-модели, действия, очереди рисования и выгрузки, задержки кадров. Каждый бой — отдельный файл в BepInEx/cache/NewAge2D/trace, хранятся последние 20, вместе не больше 20 МБ");
        CfgQuiet = Config.Bind("General", "QuietOnce", false, "Служебная отметка: подробные журналы после обновления мода уже выключены. Пока её нет, мод один раз выключит их, даже если в старых настройках они были включены. После этого включённые журналы — твоя настройка, мод их не трогает.");
        if (!CfgQuiet.Value)
        {
            CfgQuiet.Value = true;
            CfgVerbose.Value = false;
            CfgTrace.Value = false;
        }
        var loud = Config.Bind("General", "LoudOnce", false, "Служебная отметка: на время отладки подробные журналы уже включены. Пока её нет, мод один раз включит их, даже если в старых настройках они были выключены. После этого выключенные журналы — твоя настройка, мод их не трогает.");
        if (!loud.Value)
        {
            loud.Value = true;
            CfgVerbose.Value = true;
            CfgTrace.Value = true;
        }
        CfgScale = Config.Bind("General", "Scale", 3f, "Масштаб растеризации куклы: 1 = размер как во Flash");
        CfgServer = Config.Bind("Files", "FileServer", "http://files.nura.biz/", "Откуда качать ролики вещей и карт; тела персонажей и эффекты заклинаний лежат в папке мода swf");
        CfgCombat = Config.Bind("Combat", "Enabled", true, "Рисовать игроков в бою Flash-куклами вместо 3D-моделей");
        CfgDollSize = Config.Bind("Combat", "DollSize", 0.85f, "Размер кукол на экране, один масштаб Flash для всех: 1 — пиксель в пиксель с клетками Flash, меньше — мельче. Пропорции рас как во Flash");
        CfgCombatFlip = Config.Bind("Combat", "Flip", false, "Зеркалить кукол в бою наоборот, если смотрят не в ту сторону");
        CfgFlashSpeed = Config.Bind("Combat", "FlashSpeed", true, "Скорости как во Flash: шаг HexSeconds на клетку у кукол, удары и касты в родном темпе Flash. 3D-монстры ходят со скоростью Unity. ВЫКЛ — скорость Unity: шаг и анимации кукол ускоряются под модели Unity");
        CfgHexSeconds = Config.Bind("Combat", "HexSeconds", 1.35f, "Секунд на одну клетку при FlashSpeed; во Flash было 27 кадров по 20 в секунду");
        CfgCompress = Config.Bind("Combat", "Compress", true, "Сжимать кадры кукол в DXT5: вчетверо меньше видеопамяти, тонкие линии чуть мягче");
        CfgFlashHexes = Config.Bind("Combat", "FlashHexes", true, "Красить доступные для хода гексы бирюзовым, как во Flash, вместо цвета из карты");
        CfgHexColor = Config.Bind("Combat", "HexColor", new UnityEngine.Color(0f, 0.17f, 0.2f), "Добавочный цвет доступных гексов (складывается с землёй)");
        CfgDepthBias = Config.Bind("Combat", "DepthBias", 0.6f, "Сдвиг куклы и аур к камере вдоль луча взгляда (в единицах мира), чтобы их не резали земля, контур гекса и трава у ног; размер на экране не меняется");
        CfgHeight = Config.Bind("Combat", "Height", 1f, "Множитель роста кукол игроков относительно капсулы персонажа");
        CfgHoverGlow = Config.Bind("Combat", "HoverGlow", true, "Подсвечивать зелёным ореолом бойца под мышью, как во Flash-клиенте");
        CfgFitCapsule = Config.Bind("Combat", "FitCapsule", true, "Подгонять капсулу персонажа под экранную высоту куклы: по капсуле игра ставит ник, полоски, цифры урона и эффекты сверху");
        CfgBarsLift = Config.Bind("Combat", "BarsLift", 1.15f, "Дополнительный подъём капсулы, а с ней ника и полосок над куклой: 1 = ровно по макушке");
        CfgKeepMagic = Config.Bind("Combat", "UnityMagic", false, "Оставлять 3D-эффекты заклинаний Unity в режиме Flash");
        CfgFlashMagic = Config.Bind("Combat", "FlashMagic", true, "Рисовать ауры школ магии из Flash-клиента (BM, WM, NM) и эффект вещей (EF) на цели заклинания");
        CfgSpellMarks = Config.Bind("Combat", "SpellTargets", true, "Показывать, на кого легло заклинание: над целью появляется игровой значок заклинания, как в 3D-клиенте. У массовых заклинаний — над каждым, на кого легло");
        CfgVivid = Config.Bind("Combat", "Vivid", 1.15f,
            "Сочность кукол, вещей и эффектов: 1 — цвета ровно как во Flash, больше — насыщеннее и контрастнее. Считается один раз при отрисовке кадра, в бою ничего не стоит");
        CfgFrameMemory = Config.Bind("Combat", "FrameMemory", 0,
            "Предел видеопамяти под кадры кукол, МБ; 0 — сам по видеокарте (четверть её памяти, от 256 МБ до 6 ГБ). Это предохранитель от разрастания: при превышении выгружаются целиком самые давно не нужные наборы кадров, а то, что нужно прямо сейчас (стойка, ходьба, текущее действие живых бойцов), остаётся. Слишком низкий предел заставляет мод перерисовывать кадры заново, и анимации начинают запаздывать — в больших боях это видно по ходьбе");
        CfgField = Config.Bind("Field", "Enabled", true, "Поле боя как во Flash: плоская камера под углом Flash и картинка карты из Flash вместо 3D-локации. Работает, только когда включён General/Enabled");
        CfgFieldView = Config.Bind("Field", "ViewHeight", 0f, "Сколько пикселей Flash видно на экране по высоте; 0 — отдалить до краёв карты. Колесо мыши меняет и запоминает, дальше краёв карты не отдаляет");
        CfgFieldOffsetX = Config.Bind("Field", "OffsetX", 0f, "Сдвиг картинки поля вправо в пикселях Flash, если клетки разошлись с рисунком");
        CfgFieldOffsetY = Config.Bind("Field", "OffsetY", 0f, "Сдвиг картинки поля вниз в пикселях Flash");
        CfgFieldZone = Config.Bind("Field", "ServerHours", 3, "Часовой пояс сервера относительно UTC: по нему карта берётся утренняя, дневная или ночная");

        CfgSoundBefore = Config.Bind("General", "GameSoundBeforeFlash", -1f,
            "Служебное: громкость звука игры до включения Flash-вида. При Flash-виде звук игры уходит в 0, а ползунок «Звук» в настройках игры прячется: свои звуки игра привязывает к 3D-моделям и анимациям Unity. При выключении вида громкость возвращается. -1 — мод звук не глушил.");

        CfgUnityOnce = Config.Bind("General", "SwitchedToUnity", false,
            "Служебная отметка: мод один раз перевёл вид на Unity, когда Flash-кукла перестала быть видом по умолчанию. Дальше выбор твой, обратно мод не переключает.");
        if (!CfgUnityOnce.Value)
        {
            CfgUnityOnce.Value = true;
            if (CfgEnabled.Value)
            {
                CfgEnabled.Value = false;
                Log.LogInfo("Вид переведён на модели Unity: Flash-кукла больше не включена по умолчанию, вернуть можно ключом General/Enabled");
            }
        }

        if (CfgEnabled.Value)
        {
            CfgCombat.Value = true;
            CfgField.Value = true;
        }

        System.Net.ServicePointManager.DefaultConnectionLimit = Math.Max(System.Net.ServicePointManager.DefaultConnectionLimit, 16);
        Native.Preload(Log);
        Store = new SwfStore
        {
            CacheDir = Path.Combine(Paths.CachePath, "NewAge2D", "swf"),
            BundleDir = Path.Combine(Path.GetDirectoryName(Info.Location) ?? "", "swf"),
            Server = CfgServer.Value,
            Log = text => Log.LogInfo("[файлы] " + text),
        };
        DollWorker.Store = Store;
        DollWorker.Compress = CfgCompress.Value;
        Doll.Packer = CfgCompress.Value ? new Action<DollPicture>(Dxt.PackOne) : null;
        CfgCompress.SettingChanged += (_, _) =>
        {
            DollWorker.Compress = CfgCompress.Value;
            Doll.Packer = CfgCompress.Value ? new Action<DollPicture>(Dxt.PackOne) : null;
        };

        if (!SkiaWorks()) Log.LogError("Skia не завёлся, кукла рисоваться не будет");

        Doll.Vivid = UnityEngine.Mathf.Clamp(CfgVivid.Value, 1f, 2f);
        CfgVivid.SettingChanged += (_, _) => Doll.Vivid = UnityEngine.Mathf.Clamp(CfgVivid.Value, 1f, 2f);

        _harmony = new Harmony(Guid);
        _harmony.PatchAll();
        CfgEnabled.SettingChanged += (_, _) =>
        {
            if (CfgEnabled.Value)
            {
                CfgCombat.Value = true;
                CfgField.Value = true;
            }
            Apply();
        };
        CfgCombat.SettingChanged += (_, _) => Apply();
        CfgField.SettingChanged += (_, _) => Apply();
        CfgKeepMagic.SettingChanged += (_, _) => Effects.Set(HideMagic);
        Log.LogInfo($"New Age 2D {Version} загружен, кэш роликов: {Store.CacheDir}, потоков рисования {DollWorker.WorkerCount}");
    }

    internal static bool FlashLook => CfgEnabled != null && CfgEnabled.Value;

    internal static bool FlashFight => FlashLook && CfgCombat != null && CfgCombat.Value;

    internal static bool FlashField => FlashLook && CfgField != null && CfgField.Value;

    internal static bool FlashSpeed => CfgFlashSpeed == null || CfgFlashSpeed.Value;

    internal static bool HideMagic => FlashFight && CfgKeepMagic != null && !CfgKeepMagic.Value;

    internal static int Smooth => 3;

    internal static float CombatScale => Screen.height > 1800 ? 2f : 1.5f;

    internal static long FrameMemory
    {
        get
        {
            int megabytes = CfgFrameMemory != null ? CfgFrameMemory.Value : 0;
            if (megabytes <= 0)
            {
                int video = SystemInfo.graphicsMemorySize;
                megabytes = video > 0 ? video / 4 : 1600;
            }
            return (long)Mathf.Clamp(megabytes, 256, 6144) * 1048576L;
        }
    }

    private static void Apply()
    {
        try
        {
            Doll.Vivid = CfgVivid != null ? UnityEngine.Mathf.Clamp(CfgVivid.Value, 1f, 2f) : 1f;
            WindowDoll.Set(FlashLook);
            Fighters.Set(FlashFight);
            Effects.Set(HideMagic);
            Field.Set(FlashField);
            Log.LogInfo($"Flash-вид: окно {(FlashLook ? "вкл" : "выкл")}, бой {(FlashFight ? "вкл" : "выкл")}, поле {(FlashField ? "вкл" : "выкл")}");
        }
        catch (Exception ex) { Log.LogError("переключение вида: " + ex); }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        _harmony = null;
        Journal.Detach(Log);
        Field.Dispose();
        Fighters.ClearAll();
        FlashQueue.Clear();
        StatesColumn.Clear();
        Effects.Set(false);
        ThingImages.Stop();
        DollWorker.Clear();
        MainThread.Clear();
        Trace.Close();
        if (Instance == this) Instance = null;
    }

    private bool SkiaWorks()
    {
        try
        {
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(4, 4));
            surface.Canvas.Clear(SkiaSharp.SKColors.Red);
            Log.LogInfo("Skia работает");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError("Skia: " + ex);
            return false;
        }
    }

    private void Update()
    {
        MainThread.Drain();
        FrameCache.Tick();
        ThingImages.Tick();
        Warmup.Tick();
        Fighters.Tick();
        BodyClick.Tick();
        Glow.Tick();
        StatesColumn.Tick();
        Captions.Order();
        GameSound.Tick();
        Trace.Tick();
        Beauty.Tick();
    }
}
