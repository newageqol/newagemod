using System.Collections.Concurrent;
using BepInEx;
using UnityEngine;

namespace NewAge2D;

internal static class Trace
{
    private static readonly ConcurrentQueue<string> Lines = new();
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static readonly Dictionary<string, int> LookIds = new();
    private static readonly object Gate = new();
    private static StreamWriter _writer;
    private static double _battleAt;
    private static float _flushAt;
    private static float _statsAt;
    private static int _frames;
    private static float _frameTime;

    internal static int MainThreadId;

    internal static bool On => Plugin.CfgTrace != null && Plugin.CfgTrace.Value;

    internal static string Folder => Path.Combine(Paths.CachePath, "NewAge2D", "trace");

    internal static void Write(string text)
    {
        if (!On) return;
        double seconds = Clock.Elapsed.TotalSeconds - _battleAt;
        string thread = Thread.CurrentThread.ManagedThreadId == MainThreadId ? "главный" : "поток" + Thread.CurrentThread.ManagedThreadId;
        Lines.Enqueue($"{DateTime.Now:HH:mm:ss.fff} +{seconds,8:0.000} {thread,-8} {text}");
    }

    internal static string Look(string look)
    {
        if (look == null) return "-";
        lock (Gate)
        {
            if (LookIds.TryGetValue(look, out int id)) return "L" + id;
            id = LookIds.Count + 1;
            LookIds[look] = id;
            Write($"облик L{id} = {look}");
            return "L" + id;
        }
    }

    private const int Kept = 20;
    private const long Room = 20000000;
    private const long Cap = 4000000;
    private const string Ending = "===== журнал боя обрезан по размеру, дальше этот бой не записывается =====";

    private static readonly long Tail = System.Text.Encoding.UTF8.GetByteCount(Ending) + 2;
    private static long _wrote;

    private static void Prune(long room)
    {
        try
        {
            var files = new DirectoryInfo(Folder).GetFiles("*.log").OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            long sum = 0;
            int gone = 0;
            for (int i = 0; i < files.Count; i++)
            {
                sum += files[i].Length;
                if (i < Kept - 1 && sum <= room) continue;
                files[i].Delete();
                gone++;
            }
            if (gone > 0) Plugin.Log.LogInfo($"[журнал боя] старых журналов удалено: {gone}, осталось {files.Count - gone}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[журнал боя] старые файлы не удалились: " + ex.Message);
        }
    }

    internal static void Battle(string title)
    {
        if (!On) return;
        Flush();
        lock (Gate)
        {
            try
            {
                _writer?.Dispose();
                _writer = null;
                Directory.CreateDirectory(Folder);
                Prune(Room - Cap);
                string stem = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Safe(title)}";
                string path = Path.Combine(Folder, stem + ".log");
                for (int copy = 2; File.Exists(path); copy++) path = Path.Combine(Folder, $"{stem}_{copy}.log");
                _writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
                _wrote = 0;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[журнал боя] файл не открылся: " + ex.Message);
            }
            LookIds.Clear();
        }
        _battleAt = Clock.Elapsed.TotalSeconds;
        Write($"===== бой: {title}, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, NewAge2D {Plugin.Version}, потоков рисования {DollWorker.WorkerCount}, кадров в секунду {20 * Plugin.Smooth}, масштаб кадра {Plugin.CombatScale:0.#}, память под кадры {Plugin.FrameMemory / 1048576L} МБ, FlashSpeed {Plugin.FlashSpeed}, поле {Plugin.FlashField} =====");
    }

    private static string Safe(string title)
    {
        string name = Path.GetFileNameWithoutExtension(title ?? "");
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray();
        string safe = new string(chars);
        if (safe.Length > 60) safe = safe.Substring(0, 60);
        return safe.Length == 0 ? "бой" : safe;
    }

    internal static void Tick()
    {
        if (!On) return;
        float delta = Time.unscaledDeltaTime;
        _frames++;
        _frameTime += delta;
        if (delta > 0.1f && Fighters.Count > 0) Write($"кадр игры длился {delta * 1000f:0} мс");
        if (Time.unscaledTime >= _statsAt && (Fighters.Count > 0 || DollWorker.Busy || FrameCache.Busy))
        {
            _statsAt = Time.unscaledTime + 1f;
            float fps = _frameTime > 0f ? _frames / _frameTime : 0f;
            _frames = 0;
            _frameTime = 0f;
            Write($"очереди: рисование [{DollWorker.Stats()}]; выгрузка [{FrameCache.Stats()}]; кукол {Fighters.Count}; fps {fps:0}");
        }
        if (Time.unscaledTime >= _flushAt)
        {
            _flushAt = Time.unscaledTime + 0.25f;
            Flush();
        }
    }

    private static void Flush()
    {
        lock (Gate)
        {
            if (_writer == null)
            {
                while (Lines.TryDequeue(out _)) { }
                return;
            }
            try
            {
                while (Lines.TryDequeue(out string line))
                {
                    long size = System.Text.Encoding.UTF8.GetByteCount(line) + 2;
                    if (_wrote + size <= Cap - Tail)
                    {
                        _writer.WriteLine(line);
                        _wrote += size;
                        continue;
                    }
                    _writer.WriteLine(Ending);
                    _writer.Flush();
                    _writer.Dispose();
                    _writer = null;
                    while (Lines.TryDequeue(out _)) { }
                    Plugin.Log.LogWarning($"[журнал боя] журнал дорос до {Cap / 1048576} МБ, запись этого боя остановлена");
                    return;
                }
                _writer.Flush();
            }
            catch { }
        }
    }

    internal static void Close()
    {
        Flush();
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
