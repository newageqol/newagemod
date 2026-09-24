using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace NewAgeQoL
{
    internal static class DiskJournal
    {
        private const long Limit = 10000000;
        private const int CheckEvery = 200;
        private const int FlushEvery = 20;

        private static readonly object Gate = new object();
        private static StreamWriter _writer;
        private static ManualLogSource _source;
        private static int _lines;
        private static int _since;
        private static bool _told;
        private static bool _rotateBroken;

        internal static string Folder => Path.Combine(Paths.CachePath, "NewAgeQoL");

        private static string Current => Path.Combine(Folder, "qol.log");

        private static string Previous => Path.Combine(Folder, "qol.old.log");

        internal static void Attach(ManualLogSource source)
        {
            if (_writer != null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(Current);
                if (info.Exists && info.Length > Limit) Rotate();
                Open();
                _writer.WriteLine("===== запуск " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ", версия мода " + Plugin.Version + " =====");
                _writer.Flush();
                _source = source;
                source.LogEvent += OnLog;
            }
            catch (Exception e) { source.LogWarning("журнал на диске не открылся: " + e.Message); }
        }

        internal static void Detach(ManualLogSource source)
        {
            try { source.LogEvent -= OnLog; } catch { }
            lock (Gate)
            {
                _writer?.Dispose();
                _writer = null;
                _lines = 0;
                _since = 0;
            }
            _source = null;
        }

        private static void Open()
        {
            _writer = new StreamWriter(Current, true, new UTF8Encoding(false));
        }

        private static void Rotate()
        {
            if (File.Exists(Previous)) File.Delete(Previous);
            File.Move(Current, Previous);
        }

        internal static void Flush()
        {
            lock (Gate)
            {
                try { _writer?.Flush(); }
                catch { }
                _since = 0;
            }
        }

        private static bool Important(LogLevel level)
        {
            return (level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0;
        }

        private static void Roll()
        {
            try { _writer.Dispose(); } catch { }
            _writer = null;
            try { Rotate(); }
            catch (Exception e)
            {
                _rotateBroken = true;
                Blame("старый журнал не переименовался (" + e.Message + "), пишу дальше в тот же файл");
            }
            Open();
        }

        private static string _blame;

        private static void Blame(string why)
        {
            if (_told) return;
            _told = true;
            _blame = why;
        }

        private static void OnLog(object sender, LogEventArgs e)
        {
            lock (Gate)
            {
                if (_writer == null) return;
                try { Write(e); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ObjectDisposedException)
                {
                    Blame("запись в журнал не прошла: " + ex.Message);
                }
                catch { }
            }
            Said();
        }

        private static void Write(LogEventArgs e)
        {
            _writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + e.Level + "] " + e.Data);
            if (Important(e.Level) || ++_since >= FlushEvery) { _writer.Flush(); _since = 0; }
            if (++_lines < CheckEvery) return;
            _lines = 0;
            if (_rotateBroken || _writer.BaseStream.Length <= Limit) return;
            Roll();
        }

        private static void Said()
        {
            string why = _blame;
            if (why == null) return;
            _blame = null;
            _source?.LogWarning("[журнал] " + why);
        }
    }
}
