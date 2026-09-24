using BepInEx;
using BepInEx.Logging;

namespace NewAge2D;

internal static class Journal
{
    private static StreamWriter _writer;
    private static readonly object Gate = new();
    private const long Limit = 5_000_000;
    private static int _lines;

    public static string Path => System.IO.Path.Combine(Paths.CachePath, "NewAge2D", "plugin.log");

    public static void Attach(ManualLogSource source)
    {
        try
        {
            string home = System.IO.Path.GetDirectoryName(Path);
            Directory.CreateDirectory(home);
            foreach (string stale in new[] { "trace.log", "trace.prev.log" })
            {
                try
                {
                    string path = System.IO.Path.Combine(home, stale);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }
            }
            var info = new FileInfo(Path);
            if (info.Exists && info.Length > Limit) Rotate();
            Open();
            _writer.WriteLine($"===== запуск {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
            source.LogEvent += OnLog;
        }
        catch (Exception ex)
        {
            source.LogWarning("журнал не открылся: " + ex.Message);
        }
    }

    public static void Detach(ManualLogSource source)
    {
        try { source.LogEvent -= OnLog; } catch { }
        lock (Gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void OnLog(object sender, LogEventArgs e)
    {
        lock (Gate)
        {
            if (_writer == null) return;
            try
            {
                _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{e.Level}] {e.Data}");
                if (++_lines < 200) return;
                _lines = 0;
                if (_writer.BaseStream.Length <= Limit) return;
                _writer.Dispose();
                _writer = null;
                Rotate();
                Open();
            }
            catch { }
        }
    }

    private static void Open()
    {
        _writer = new StreamWriter(Path, true, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
    }

    private static void Rotate()
    {
        string old = Path + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(Path, old);
    }
}
