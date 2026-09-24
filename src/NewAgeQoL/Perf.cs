using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NewAgeQoL
{
    internal sealed class Meter
    {
        internal string Group;
        internal string Name;
        internal long Frame;
        internal int FrameCalls;
        internal long Total;
        internal long Peak;
        internal long Calls;
        internal bool FrameGc;
        internal bool PeakGc;
        internal int GcHits;
        internal long FrameBytes;
        internal long Bytes;
    }

    internal struct PerfMark
    {
        internal long At;
        internal int Gc;
        internal long Bytes;
    }

    internal static class Perf
    {
        private const string Mod = "мод";
        private const string Flash = "Flash-вид";
        private const string Ui = "интерфейс";
        private const string Detail = "подробно";
        private const string Quiet = "холостой ход";
        private const string DetailBase =
            "BodyClick.Body, BodyClick.OverUi, BodyClick.Hovered, BodyClick.Walk, FlashLook.Under, " +
            "SkillList.HexUnder, SkillList.Look, SkillList.Pad, WalkHex.Walkable, " +
            "FighterHint.Cd, FighterHint.Under, FighterHint.Ray, FighterHint.OnHex, " +
            "NewAge2D.BodyClick.Body, NewAge2D.BodyClick.OverUi, NewAge2D.BodyClick.Covers, NewAge2D.BodyClick.Cell, NewAge2D.Fighters.DollOf, " +
            "SideButtons.Layout, SideButtons.Layer, SideButtons.Shopping, SideButtons.UpdateStatus, SideButtons.InWorld, SideButtons.InCombat, " +
            "Workshop.Here, Hotkeys.Tail";

        private const string DetailDefault = "";
        private static readonly string[] Loops = { "Update", "LateUpdate", "FixedUpdate", "OnGUI" };
        private static readonly double TickMs = 1000.0 / Stopwatch.Frequency;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static ConfigEntry<bool> Enabled;
        private static ConfigEntry<int> _every;
        private static ConfigEntry<int> _spike;
        private static ConfigEntry<string> _detail;

        internal static bool On { get; private set; }

        private static readonly List<Meter> Meters = new List<Meter>();
        private static readonly Dictionary<IntPtr, Meter> Hooked = new Dictionary<IntPtr, Meter>();
        private static readonly List<float> Frames = new List<float>(8192);
        private static readonly Dictionary<string, int> UnityLines = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> ModLines = new Dictionary<string, int>();
        private static readonly Dictionary<string, double> Places = new Dictionary<string, double>();
        private static readonly object Gate = new object();

        private static bool _started;
        private static bool _broken;
        private static bool _countersBroken;
        private static long _last;
        private static long _windowAt;
        private static double _windowMs;
        private static string _windowPlace;
        private static string _windowScene = "";
        private static float _worst;
        private static int _slow;
        private static int _stalls;
        private static int _gc;
        private static int _collections;
        private static int _spikes;
        private static int _hushed;
        private static float _saidAt = -10f;
        private static int _unityFrame;
        private static int _unityTotal;
        private static int _unityRest;
        private static int _modFrame;
        private static bool _loaded;
        private static bool _exactBytes;
        private static long _lastBytes;
        private static long _windowBytes;
        private static Meter _noise;
        private static double _spin;
        private static Harmony _harmony;
        private static Lines _listener;

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Spin()
        {
            double sum = 0.0;
            for (int i = 1; i < 400; i++) sum += i * 0.5;
            _spin = sum;
        }

        private static long Noise => _noise != null ? _noise.Bytes : 0L;

        internal static void Bind(ConfigFile home)
        {
            Enabled = home.Bind("Perf", "Enabled", false,
                "Замер производительности: раз в минуту пишет в журнал частоту кадров, рывки и то, на что уходит время кадра (модули мода, вид как во Flash, перестройка интерфейса, сообщения Unity). Работает и при выключенном моде, чтобы можно было сравнить.");
            _every = home.Bind("Perf", "ReportSeconds", 60,
                "Раз во сколько секунд писать сводку замера, от 10 до 600. При смене локации сводка пишется раньше.");
            _spike = home.Bind("Perf", "SpikeMs", 100,
                "Кадр дольше стольких миллисекунд сразу записывается в журнал как рывок, от 20 до 5000.");
            _detail = home.Bind("Perf", "DetailMethods", DetailDefault,
                "Дополнительные методы мода, время которых замер показывает отдельной строкой «подробно», через запятую в виде Класс.Метод. Свой список замера уже есть в моде, здесь можно дописать свои. Читается при первом включении замера после запуска игры.");
        }

        internal static void Tick()
        {
            if (_broken) return;
            try
            {
                bool want = Enabled != null && Enabled.Value;
                if (want != On)
                {
                    if (want) Begin();
                    else End();
                }
                if (!On) return;
                var idle = Mark();
                Spin();
                Add(_noise ?? (_noise = Track(Quiet, "холостая проба")), idle);
                long now = Stopwatch.GetTimestamp();
                if (_last != 0L) Close((now - _last) * TickMs, now);
                _last = now;
            }
            catch (Exception e)
            {
                _broken = true;
                On = false;
                Plugin.Log?.LogWarning("[замер] остановлен из-за ошибки: " + e);
            }
        }

        internal static Meter Track(string group, string name)
        {
            foreach (var m in Meters)
                if (m.Group == group && m.Name == name) return m;
            var meter = new Meter { Group = group, Name = name };
            Meters.Add(meter);
            return meter;
        }

        internal static PerfMark Mark() =>
            On ? new PerfMark { At = Stopwatch.GetTimestamp(), Gc = GC.CollectionCount(0), Bytes = Counted() } : default(PerfMark);

        internal static void Add(Meter meter, PerfMark mark)
        {
            if (mark.At == 0L) return;
            meter.Frame += Stopwatch.GetTimestamp() - mark.At;
            meter.FrameCalls++;
            if (GC.CollectionCount(0) != mark.Gc) meter.FrameGc = true;
            if (!_exactBytes) return;
            long bytes = Counted() - mark.Bytes;
            if (bytes > 0L) meter.FrameBytes += bytes;
        }

        private static long Counted()
        {
            if (!_exactBytes) return 0L;
            try { return ThreadBytes(); }
            catch { _exactBytes = false; return 0L; }
        }

        private static long Allocated()
        {
            if (_exactBytes)
            {
                try { return ThreadBytes(); }
                catch { _exactBytes = false; }
            }
            return GC.GetTotalMemory(false);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long ThreadBytes() => GC.GetAllocatedBytesForCurrentThread();

        private static bool ExactBytes()
        {
            try
            {
                long before = ThreadBytes();
                var probe = new byte[65536];
                return ThreadBytes() - before >= probe.Length;
            }
            catch { return false; }
        }

        private static void Begin()
        {
            if (!_started)
            {
                _started = true;
                Application.logMessageReceived += OnUnityLog;
                SceneManager.sceneLoaded += OnScene;
                _listener = new Lines();
                BepInEx.Logging.Logger.Listeners.Add(_listener);
                _exactBytes = ExactBytes();
                Say("[замер] выделения памяти считаются " + (_exactBytes ? "точно, по основному потоку" : "приблизительно, по росту кучи (с другими потоками)"));
                Hook();
                try { PerfCounters.Open(); }
                catch (Exception e) { CountersFailed(e); }
            }
            On = true;
            _last = 0L;
            _lastBytes = 0L;
            _gc = GC.CollectionCount(0);
            Restart(Stopwatch.GetTimestamp());
            Say("[замер] включён. " + Machine());
        }

        private static void End()
        {
            if (Frames.Count > 0 && _windowMs >= 5000.0) Report();
            On = false;
            Unhook();
            Say("[замер] выключен");
        }

        private static void Unhook()
        {
            if (!_started) return;
            _started = false;
            try { Application.logMessageReceived -= OnUnityLog; }
            catch (Exception e) { Say("[замер] подписка на сообщения Unity не снялась: " + e.Message); }
            try { SceneManager.sceneLoaded -= OnScene; }
            catch (Exception e) { Say("[замер] подписка на смену сцены не снялась: " + e.Message); }
            try
            {
                if (_listener != null)
                {
                    BepInEx.Logging.Logger.Listeners.Remove(_listener);
                    _listener.Dispose();
                }
            }
            catch (Exception e) { Say("[замер] слушатель журналов не снялся: " + e.Message); }
            _listener = null;
            try { _harmony?.UnpatchSelf(); }
            catch (Exception e) { Say("[замер] замеры методов не сняты: " + e.Message); }
            _harmony = null;
            Hooked.Clear();
            try { PerfCounters.Close(); }
            catch (Exception e) { Say("[замер] счётчики Unity не закрылись: " + e.Message); }
            _countersBroken = false;
        }

        private static void Close(double ms, long now)
        {
            Frames.Add((float)ms);
            _windowMs += ms;
            if (ms > _worst) _worst = (float)ms;
            if (ms > 1000.0 / 30.0) _slow++;
            if (ms > 100.0) _stalls++;

            int gc = GC.CollectionCount(0);
            bool collected = gc != _gc;
            if (collected) _collections += gc - _gc;
            _gc = gc;

            long allocated = Allocated();
            if (_lastBytes != 0L && allocated > _lastBytes) _windowBytes += allocated - _lastBytes;
            _lastBytes = allocated;

            string place = Place();
            double had;
            Places.TryGetValue(place, out had);
            Places[place] = had + ms;

            int unity = _unityFrame;
            _unityFrame = 0;
            int mod;
            lock (Gate)
            {
                mod = _modFrame;
                _modFrame = 0;
            }

            if (!_countersBroken)
            {
                try { PerfCounters.Sample(); }
                catch (Exception e) { CountersFailed(e); }
            }

            int spike = _spike == null ? 100 : Mathf.Clamp(_spike.Value, 20, 5000);
            if (ms >= spike) Spike(ms, place, collected, unity, mod);

            for (int i = 0; i < Meters.Count; i++)
            {
                var m = Meters[i];
                m.Total += m.Frame;
                if (m.Frame > m.Peak)
                {
                    m.Peak = m.Frame;
                    m.PeakGc = m.FrameGc;
                }
                if (m.FrameGc) m.GcHits++;
                m.Calls += m.FrameCalls;
                m.Bytes += m.FrameBytes;
                m.Frame = 0L;
                m.FrameCalls = 0;
                m.FrameGc = false;
                m.FrameBytes = 0L;
            }
            _loaded = false;

            if (_windowPlace == null)
            {
                _windowPlace = place;
                _windowScene = SceneName();
            }
            double age = (now - _windowAt) * TickMs / 1000.0;
            int every = _every == null ? 60 : Mathf.Clamp(_every.Value, 10, 600);
            if (age >= every)
            {
                Report();
                Restart(now);
            }
            else if (place != _windowPlace)
            {
                if (age >= 10.0)
                {
                    Report();
                    Restart(now);
                }
                _windowPlace = place;
            }
        }

        private static void Restart(long now)
        {
            Frames.Clear();
            _windowAt = now;
            _windowMs = 0.0;
            _windowPlace = null;
            _windowScene = "";
            _worst = 0f;
            _slow = 0;
            _stalls = 0;
            _collections = 0;
            _spikes = 0;
            _unityTotal = 0;
            _unityRest = 0;
            UnityLines.Clear();
            Places.Clear();
            lock (Gate) ModLines.Clear();
            foreach (var m in Meters)
            {
                m.Total = 0L;
                m.Peak = 0L;
                m.Calls = 0L;
                m.PeakGc = false;
                m.GcHits = 0;
                m.Bytes = 0L;
            }
            _windowBytes = 0L;
            if (_countersBroken) return;
            try { PerfCounters.Restart(); }
            catch (Exception e) { CountersFailed(e); }
        }

        private static void CountersFailed(Exception e)
        {
            _countersBroken = true;
            Say("[замер] счётчики Unity недоступны: " + e.GetType().Name + ": " + e.Message);
        }

        private static string Place()
        {
            BaseLocationView view;
            try { view = BaseLocationView.GetInstance(); }
            catch { return "?"; }
            if (view == null) return "без локации";
            if (view is CombatLocationView) return "бой";
            if (view is GlobalMapLocationView) return "карта мира";
            if (view is StaticLocationView) return "город";
            return view.GetType().Name;
        }

        private static void Spike(double ms, string place, bool collected, int unity, int mod)
        {
            _spikes++;
            float now = Time.unscaledTime;
            if (now - _saidAt < 1f)
            {
                _hushed++;
                return;
            }
            _saidAt = now;
            var sb = new StringBuilder("[замер] рывок ").Append(ms.ToString("0", Inv)).Append(" мс · ").Append(place);
            if (_loaded) sb.Append(" · загружалась сцена");
            if (collected) sb.Append(" · сборка мусора");
            var heavy = Meters.Where(m => m.Frame * TickMs >= 1.0).OrderByDescending(m => m.Frame).Take(5)
                .Select(m => m.Name + " " + (m.Frame * TickMs).ToString("0.0", Inv) + (m.FrameCalls > 1 ? " ×" + m.FrameCalls : "") + (m.FrameGc ? " (сборка мусора)" : ""))
                .ToArray();
            if (heavy.Length > 0) sb.Append(" · дольше всего: ").Append(string.Join(", ", heavy));
            if (unity > 0) sb.Append(" · сообщений Unity: ").Append(unity);
            if (mod > 0) sb.Append(" · строк журналов модов: ").Append(mod);
            if (_hushed > 0)
            {
                sb.Append(" · до этого ещё рывков без записи: ").Append(_hushed);
                _hushed = 0;
            }
            Say(sb.ToString());
        }

        private static void Report()
        {
            int n = Frames.Count;
            if (n == 0 || _windowMs <= 0.0) return;
            Frames.Sort();
            double seconds = _windowMs / 1000.0;
            double avg = _windowMs / n;
            float median = Frames[n / 2];
            float p99 = Frames[Math.Min(n - 1, (int)(n * 0.99))];
            Say("[замер] " + seconds.ToString("0", Inv) + " с · " + Where() + " · " + Setup());
            Say("[замер] кадры: " + n + ", в среднем " + (1000.0 / avg).ToString("0.0", Inv) + " к/с (" + avg.ToString("0.0", Inv) +
                " мс), обычный кадр " + median.ToString("0.0", Inv) + " мс, 99% кадров не дольше " + p99.ToString("0.0", Inv) +
                " мс, худший " + _worst.ToString("0", Inv) + " мс; дольше 33 мс: " + _slow + ", дольше 100 мс: " + _stalls + ", рывков: " + _spikes);
            Group(Mod, n, 8, true, seconds);
            Group(Flash, n, 6, true, seconds);
            Group(Ui, n, 0, true, seconds);
            Group(Detail, n, 14, false, seconds);
            Say("[замер] память: сборок мусора " + _collections + ", выделено " + (_exactBytes ? "основным потоком " : "примерно ") +
                Rate(_windowBytes, seconds) + ", " + Heap() +
                (_exactBytes || Noise <= 0L ? "" : "; шум холостой пробы " + Rate(Noise, seconds) + " — меньше этого по модулям не считается"));
            Takers(seconds);
            if (!_countersBroken)
            {
                string counters = "";
                try { counters = PerfCounters.Line(); }
                catch (Exception e) { CountersFailed(e); }
                if (counters.Length > 0) Say("[замер] счётчики Unity: " + counters);
            }
            Say("[замер] сообщения Unity: " + _unityTotal + " (" + (_unityTotal / seconds).ToString("0.0", Inv) + " в с)" + TopLines());
            Say("[замер] журналы модов: " + ModLinesText(seconds));
        }

        private static void Takers(double seconds)
        {
            long floor = Math.Max((long)(1024.0 * seconds), Noise * 2L);
            var top = Meters.Where(m => m.Group != Detail && m.Group != Quiet && m.Bytes > floor).OrderByDescending(m => m.Bytes).Take(8).ToList();
            if (top.Count == 0) return;
            long mine = Meters.Where(m => m.Group != Detail && m.Group != Quiet).Sum(m => m.Bytes);
            string rest = _exactBytes && _windowBytes > mine ? "; игра и всё остальное на основном потоке " + Rate(_windowBytes - mine, seconds) : "";
            Say("[замер] выделяют больше всех: " + string.Join(", ", top.Select(m => m.Name + " " + Rate(m.Bytes, seconds))) + rest);
        }

        private static string Rate(long bytes, double seconds)
        {
            double perSecond = seconds > 0.0 ? bytes / seconds : 0.0;
            return perSecond >= 1048576.0
                ? (perSecond / 1048576.0).ToString("0.0", Inv) + " МБ/с"
                : (perSecond / 1024.0).ToString("0", Inv) + " КБ/с";
        }

        private static void Group(string group, int frames, int take, bool sum, double seconds)
        {
            var list = Meters.Where(m => m.Group == group && m.Calls > 0).ToList();
            if (list.Count == 0) return;
            var line = new StringBuilder("[замер] ").Append(group).Append(": ");
            if (sum)
            {
                double total = list.Sum(m => (double)m.Total) * TickMs / frames;
                var worst = list.OrderByDescending(m => m.Peak).First();
                line.Append(total.ToString("0.00", Inv)).Append(" мс за кадр, пик ").Append((worst.Peak * TickMs).ToString("0.0", Inv)).Append(" мс")
                    .Append(worst.PeakGc ? " со сборкой мусора" : "");
            }
            if (take > 0)
            {
                var top = list.OrderByDescending(m => m.Total).Take(take).Select(m => Cost(m, frames, seconds));
                line.Append(sum ? "; дороже всех: " : "").Append(string.Join(", ", top));
            }
            Say(line.ToString());
        }

        private static string Cost(Meter m, int frames, double seconds)
        {
            var text = new StringBuilder(m.Name).Append(' ').Append((m.Total * TickMs / frames).ToString("0.00", Inv))
                .Append(" (пик ").Append((m.Peak * TickMs).ToString("0.0", Inv));
            if (m.PeakGc) text.Append(" со сборкой мусора");
            if (m.GcHits > 0) text.Append(", сборок внутри ").Append(m.GcHits);
            if (seconds > 0.0 && m.Bytes / seconds >= 1024.0 && m.Bytes > Noise * 2L) text.Append(", выделяет ").Append(Rate(m.Bytes, seconds));
            if (m.Calls * 2 >= frames * 3L) text.Append(", вызовов за кадр ").Append(((double)m.Calls / frames).ToString("0.#", Inv));
            return text.Append(')').ToString();
        }

        private static string Where()
        {
            double all = Places.Values.Sum();
            var shares = Places.Where(p => all <= 0.0 || p.Value * 100.0 / all >= 1.0).OrderByDescending(p => p.Value).ToList();
            string places = shares.Count <= 1
                ? string.Join(", ", shares.Select(p => p.Key))
                : string.Join(", ", shares.Select(p => p.Key + " " + (p.Value * 100.0 / all).ToString("0", Inv) + "%"));
            return places + (_windowScene.Length > 0 ? " (сцена " + _windowScene + ")" : "");
        }

        private static string SceneName()
        {
            try { return SceneManager.GetActiveScene().name ?? ""; }
            catch { return ""; }
        }

        private static string Setup()
        {
            return "мод " + (ModSwitch.On ? "вкл" : "выкл") + ", Flash-вид " + FlashState() + ", окно " + Screen.width + "x" + Screen.height +
                   ", цель " + Application.targetFrameRate + " к/с, vSync " + QualitySettings.vSyncCount;
        }

        private static string FlashState()
        {
            var entry = FlashLook.Entry("General", "Enabled");
            return entry == null ? "нет" : entry.Value ? "вкл" : "выкл";
        }

        private static string Machine()
        {
            string refresh = "";
            try { refresh = " " + Screen.currentResolution.refreshRateRatio.value.ToString("0", Inv) + " Гц"; }
            catch { }
            string quality = "?";
            try { quality = QualitySettings.names[QualitySettings.GetQualityLevel()]; }
            catch { }
            return "Видеокарта " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + "), процессор " + SystemInfo.processorType +
                   " (" + SystemInfo.processorCount + " потоков), память " + SystemInfo.systemMemorySize + " МБ, монитор " +
                   Screen.currentResolution.width + "x" + Screen.currentResolution.height + refresh + ", режим " + Screen.fullScreenMode +
                   ", качество " + quality + ", " + Setup();
        }

        private static string Heap()
        {
            try
            {
                long used = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                long heap = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
                long total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                return "куча C# занята " + Mb(used) + " из " + Mb(heap) + " МБ" + (total > 0 ? ", всего у Unity " + Mb(total) + " МБ" : "");
            }
            catch { return "размер кучи неизвестен"; }
        }

        private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0", Inv);

        private static string TopLines()
        {
            if (UnityLines.Count == 0) return "";
            var top = UnityLines.OrderByDescending(p => p.Value).Take(4).Select(p => "«" + p.Key + "» ×" + p.Value);
            return "; чаще всего: " + string.Join(", ", top) + (_unityRest > 0 ? "; ещё разных сверх учёта: " + _unityRest : "");
        }

        private static string ModLinesText(double seconds)
        {
            lock (Gate)
            {
                if (ModLines.Count == 0) return "ничего";
                return string.Join(", ", ModLines.OrderByDescending(p => p.Value)
                    .Select(p => p.Key + " " + p.Value + " (" + (p.Value / seconds).ToString("0.0", Inv) + " в с)"));
            }
        }

        private static void OnUnityLog(string text, string stack, LogType type)
        {
            if (!On) return;
            _unityFrame++;
            _unityTotal++;
            string key = Key(text);
            int n;
            if (UnityLines.TryGetValue(key, out n)) UnityLines[key] = n + 1;
            else if (UnityLines.Count < 200) UnityLines[key] = 1;
            else _unityRest++;
        }

        private static string Key(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int end = text.IndexOf('\n');
            if (end < 0) end = text.Length;
            if (end > 90) end = 90;
            var chars = new char[end];
            for (int i = 0; i < end; i++)
            {
                char c = text[i];
                chars[i] = c >= '0' && c <= '9' ? '#' : c;
            }
            return new string(chars);
        }

        private static void OnScene(Scene scene, LoadSceneMode mode)
        {
            if (On) _loaded = true;
        }

        private static void Say(string text) => Plugin.Log?.LogInfo(text);

        private static void Hook()
        {
            var harmony = new Harmony(Plugin.Guid + ".perf");
            _harmony = harmony;
            var before = new HarmonyMethod(AccessTools.Method(typeof(Perf), nameof(Before)));
            var after = new HarmonyMethod(AccessTools.Method(typeof(Perf), nameof(After)));
            int count = 0;
            var rebuild = AccessTools.Method(typeof(UnityEngine.UI.CanvasUpdateRegistry), "PerformUpdate");
            if (rebuild == null) Say("[замер] перестройка интерфейса не найдена, её время не считается");
            else if (Watch(harmony, rebuild, Ui, "перестройка uGUI", before, after)) count++;
            string own = typeof(Perf).Assembly.GetName().Name;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                string group = name == own ? Mod : name == "NewAge2D" ? Flash : null;
                if (group == null) continue;
                foreach (var type in Types(asm))
                {
                    if (type == null || type == typeof(Plugin) || type.ContainsGenericParameters || !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                    foreach (string loop in Loops)
                    {
                        var method = type.GetMethod(loop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (method == null || method.IsAbstract) continue;
                        if (Watch(harmony, method, group, type.Name + "." + loop, before, after)) count++;
                    }
                }
            }
            count += Details(harmony, before, after);
            Say("[замер] подключено методов: " + count);
        }

        private static int Details(Harmony harmony, HarmonyMethod before, HarmonyMethod after)
        {
            int count = 0;
            var missing = new List<string>();
            string raw = DetailBase + "," + (_detail != null ? _detail.Value ?? "" : "");
            foreach (var piece in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string spec = piece.Trim();
                int dot = spec.LastIndexOf('.');
                if (dot <= 0 || dot == spec.Length - 1) continue;
                string typeName = spec.Substring(0, dot);
                string methodName = spec.Substring(dot + 1);
                var type = AccessTools.TypeByName(typeof(Perf).Namespace + "." + typeName) ?? AccessTools.TypeByName(typeName);
                var methods = type == null || type.ContainsGenericParameters
                    ? new List<MethodInfo>()
                    : AccessTools.GetDeclaredMethods(type).Where(m => m.Name == methodName && !m.IsAbstract && !m.ContainsGenericParameters).ToList();
                if (methods.Count == 0)
                {
                    missing.Add(spec);
                    continue;
                }
                foreach (var method in methods)
                {
                    if (Hooked.ContainsKey(method.MethodHandle.Value)) continue;
                    string name = type.Name + "." + methodName + (methods.Count > 1 ? "(" + method.GetParameters().Length + ")" : "");
                    if (Watch(harmony, method, Detail, name, before, after)) count++;
                }
            }
            if (missing.Count > 0) Say("[замер] для подробного замера не нашлись: " + string.Join(", ", missing));
            return count;
        }

        private static IEnumerable<Type> Types(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        private static bool Watch(Harmony harmony, MethodBase method, string group, string name, HarmonyMethod before, HarmonyMethod after)
        {
            IntPtr key = method.MethodHandle.Value;
            try
            {
                Hooked[key] = Track(group, name);
                harmony.Patch(method, prefix: before, postfix: after);
                return true;
            }
            catch (Exception e)
            {
                Hooked.Remove(key);
                Say("[замер] не подключился " + name + ": " + e.Message);
                return false;
            }
        }

        private static void Before(out PerfMark __state)
        {
            __state = Mark();
        }

        private static void After(PerfMark __state, MethodBase __originalMethod)
        {
            if (__state.At == 0L || __originalMethod == null) return;
            Meter meter;
            if (!Hooked.TryGetValue(__originalMethod.MethodHandle.Value, out meter)) return;
            Add(meter, __state);
        }

        private sealed class Lines : ILogListener
        {
            public void LogEvent(object sender, LogEventArgs e)
            {
                if (!On || e == null || e.Source == null) return;
                string name = e.Source.SourceName ?? "";
                if (name.StartsWith("Unity", StringComparison.Ordinal)) return;
                lock (Gate)
                {
                    int n;
                    ModLines.TryGetValue(name, out n);
                    ModLines[name] = n + 1;
                    _modFrame++;
                }
            }

            public void Dispose()
            {
            }
        }
    }

    internal static class PerfCounters
    {
        private sealed class Counter
        {
            internal string Title;
            internal ProfilerRecorder Recorder;
            internal ProfilerMarkerDataUnit Unit;
            internal double Sum;
            internal long Peak;
            internal int Samples;
        }

        private static readonly string[,] Wanted =
        {
            { "CPU Main Thread Frame Time", "основной поток" },
            { "CPU Render Thread Frame Time", "поток отрисовки" },
            { "GPU Frame Time", "видеокарта" },
            { "Draw Calls Count", "вызовов отрисовки" },
            { "Batches Count", "пакетов" },
            { "SetPass Calls Count", "смен материала" },
            { "Triangles Count", "треугольников" },
            { "GC Allocated In Frame", "выделено памяти за кадр" },
            { "GC Allocation In Frame Count", "выделений за кадр" },
        };

        private const int SampleEvery = 10;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly List<Counter> Counters = new List<Counter>();
        private static int _skipped;

        internal static void Open()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var taken = new List<string>();
            foreach (var handle in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(handle);
                for (int i = 0; i < Wanted.GetLength(0); i++)
                {
                    if (d.Name != Wanted[i, 0]) continue;
                    var recorder = ProfilerRecorder.StartNew(d.Category, d.Name, 1);
                    Counters.Add(new Counter { Title = Wanted[i, 1], Recorder = recorder, Unit = d.UnitType });
                    taken.Add(d.Name);
                    break;
                }
            }
            Plugin.Log?.LogInfo("[замер] счётчиков Unity доступно " + handles.Count + ", взяты: " + (taken.Count == 0 ? "ни одного" : string.Join(", ", taken)));
            if (taken.Count > 0) return;
            var names = handles.Take(80).Select(h => ProfilerRecorderHandle.GetDescription(h).Name);
            Plugin.Log?.LogInfo("[замер] первые счётчики Unity: " + string.Join(", ", names));
        }

        internal static void Close()
        {
            foreach (var c in Counters)
            {
                try { c.Recorder.Dispose(); }
                catch { }
            }
            Counters.Clear();
            _skipped = 0;
        }

        internal static void Sample()
        {
            if (Counters.Count == 0) return;
            if (++_skipped < SampleEvery) return;
            _skipped = 0;
            FrameTimingManager.CaptureFrameTimings();
            foreach (var c in Counters)
            {
                if (!c.Recorder.Valid) continue;
                long v = c.Recorder.LastValue;
                c.Sum += v;
                c.Samples++;
                if (v > c.Peak) c.Peak = v;
            }
        }

        internal static void Restart()
        {
            _skipped = 0;
            foreach (var c in Counters)
            {
                c.Sum = 0.0;
                c.Peak = 0L;
                c.Samples = 0;
            }
        }

        internal static string Line()
        {
            var parts = new List<string>();
            foreach (var c in Counters)
            {
                if (c.Samples == 0 || c.Sum <= 0.0) continue;
                double avg = c.Sum / c.Samples;
                if (c.Unit == ProfilerMarkerDataUnit.TimeNanoseconds)
                    parts.Add(c.Title + " " + (avg / 1e6).ToString("0.0", Inv) + " мс (пик " + (c.Peak / 1e6).ToString("0", Inv) + ")");
                else if (c.Unit == ProfilerMarkerDataUnit.Bytes)
                    parts.Add(c.Title + " " + (avg / 1024.0).ToString("0", Inv) + " КБ (пик " + (c.Peak / 1024.0).ToString("0", Inv) + ")");
                else
                    parts.Add(c.Title + " " + avg.ToString("0", Inv) + " (пик " + c.Peak + ")");
            }
            return string.Join(", ", parts);
        }
    }
}
