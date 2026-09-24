using System.Net;
using System.Text.RegularExpressions;
using NewAge.Swf.Display;

namespace NewAge2D;

public sealed class SwfStore
{
    public string LocalDir;
    public string BundleDir;
    public string CacheDir;
    public string Server = "http://files.nura.biz/";
    public Action<string> Log;

    private readonly Dictionary<string, SwfMovie> _movies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fetching = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pulled = new(StringComparer.OrdinalIgnoreCase);

    public void Forget()
    {
        lock (_movies)
        {
            _movies.Clear();
            _failed.Clear();
            _fetching.Clear();
            _pulled.Clear();
        }
    }

    public void Prefetch(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            if (string.IsNullOrEmpty(file)) continue;
            lock (_movies)
            {
                if (_movies.ContainsKey(file) || _failed.ContainsKey(file) || !_fetching.Add(file)) continue;
            }
            ThreadPool.QueueUserWorkItem(state =>
            {
                try { Get(file, out string ignored); }
                catch { }
            });
        }
    }

    public void Pull(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            if (string.IsNullOrEmpty(file)) continue;
            lock (_movies)
            {
                if (_movies.ContainsKey(file) || _failed.ContainsKey(file) || !_pulled.Add(file)) continue;
            }
            string one = file;
            ThreadPool.QueueUserWorkItem(state =>
            {
                try { Locate(one, out string ignored); }
                catch { }
            });
        }
    }

    public event Action<string> Arrived;

    public bool Ready(string file)
    {
        if (string.IsNullOrEmpty(file)) return true;
        lock (_movies) return _movies.ContainsKey(file) || _failed.ContainsKey(file);
    }

    public string Failure(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        lock (_movies) return _failed.TryGetValue(file, out string reason) ? reason : null;
    }

    public SwfMovie Get(string file, out string reason)
    {
        reason = null;
        object gate;
        lock (_movies)
        {
            if (_movies.TryGetValue(file, out var known)) return known;
            if (_failed.TryGetValue(file, out reason)) return null;
            if (!_gates.TryGetValue(file, out gate)) _gates[file] = gate = new object();
        }

        SwfMovie movie = null;
        lock (gate)
        {
            lock (_movies)
            {
                if (_movies.TryGetValue(file, out var known)) return known;
                if (_failed.TryGetValue(file, out reason)) return null;
            }

            string path = Locate(file, out reason);
            if (path == null)
            {
                lock (_movies) _failed[file] = reason;
            }
            else
            {
                try
                {
                    movie = SwfMovie.Load(path);
                    lock (_movies) _movies[file] = movie;
                }
                catch (Exception ex)
                {
                    reason = "не разбирается: " + ex.Message;
                    lock (_movies) _failed[file] = reason;
                }
            }
        }
        try { Arrived?.Invoke(file); }
        catch { }
        return movie;
    }

    private string Locate(string file, out string reason)
    {
        reason = null;
        foreach (string dir in new[] { BundleDir, LocalDir })
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string near = Path.Combine(dir, file);
            if (File.Exists(near)) return near;
        }

        Directory.CreateDirectory(CacheDir);
        string cached = Path.Combine(CacheDir, file);
        if (File.Exists(cached) && new FileInfo(cached).Length > 8) return cached;

        if (string.Equals(file, "client.swf", StringComparison.OrdinalIgnoreCase))
        {
            string real = MainName(out reason) ?? Newest();
            if (real == null) return null;
            string mine = Path.Combine(CacheDir, real);
            if (File.Exists(mine) && new FileInfo(mine).Length > 8) return mine;
            byte[] main = Fetch(real, out reason);
            return main == null ? null : Keep(mine, main, real, real, out reason);
        }

        byte[] data = Fetch(file, out reason);
        string source = file;
        if (data == null)
            foreach (string kin in Kin(file))
            {
                string near = Path.Combine(CacheDir, kin);
                data = File.Exists(near) && new FileInfo(near).Length > 8 ? File.ReadAllBytes(near) : Fetch(kin, out _);
                if (data == null) continue;
                source = kin;
                reason = null;
                break;
            }
        if (data == null) return null;

        return Keep(cached, data, file, source, out reason);
    }

    private string Keep(string cached, byte[] data, string file, string source, out string reason)
    {
        reason = null;
        try
        {
            string partial = cached + "." + Guid.NewGuid().ToString("N") + ".part";
            File.WriteAllBytes(partial, data);
            if (File.Exists(cached)) File.Delete(cached);
            File.Move(partial, cached);
            Log?.Invoke(source == file ? $"скачан {file} ({data.Length} байт)" : $"для {file} взят {source} ({data.Length} байт)");
            return cached;
        }
        catch (Exception ex)
        {
            reason = "не сохранился: " + ex.Message;
            return null;
        }
    }

    private string MainName(out string reason)
    {
        reason = null;
        try
        {
            using var web = new WebClient();
            string list = web.DownloadString(Server + "md5list");
            var hit = Regex.Match(list ?? "", @"NewAge\d+\.swf", RegexOptions.IgnoreCase);
            if (hit.Success) return hit.Value;
            reason = "в списке файлов игры нет главного ролика";
            return null;
        }
        catch (Exception ex)
        {
            reason = "список файлов игры не скачался: " + ex.Message;
            return null;
        }
    }

    private string Newest()
    {
        try
        {
            string best = null;
            foreach (string path in Directory.GetFiles(CacheDir, "NewAge*.swf"))
                if (new FileInfo(path).Length > 8 && (best == null || string.CompareOrdinal(Path.GetFileName(path), best) > 0))
                    best = Path.GetFileName(path);
            return best;
        }
        catch { return null; }
    }

    private byte[] Fetch(string file, out string reason)
    {
        reason = null;
        try
        {
            using var web = new WebClient();
            byte[] data = web.DownloadData(Server + file);
            if (data != null && data.Length >= 8 && (data[0] == 'F' || data[0] == 'C')) return data;
            reason = "сервер вернул не SWF";
            return null;
        }
        catch (Exception ex)
        {
            reason = "не скачался: " + ex.Message;
            return null;
        }
    }

    private static IEnumerable<string> Kin(string file)
    {
        if (!file.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)) yield break;
        string name = file.Substring(0, file.Length - 4);
        string slim = Regex.Replace(name, @"^([a-z]+?)big(?=_|\d)", "$1", RegexOptions.IgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
        foreach (string one in new[] { slim, Split(name), Split(slim) })
            if (seen.Add(one)) yield return one + ".swf";
    }

    private static string Split(string name) => Regex.Replace(name, @"^([a-z]+)(\d)", "$1_$2", RegexOptions.IgnoreCase);
}
