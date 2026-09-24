using System.Collections;
using BepInEx;
using Transport.Messages.Responses.Things.Thinginfo.Generalinfo;
using UnityEngine;

namespace NewAge2D;

internal static class ThingImages
{
    private static readonly Dictionary<int, string> Known = new();
    private static readonly Dictionary<int, float> Asked = new();
    private static readonly Queue<int> Waiting = new();
    private static bool _loaded;

    internal static void Forget()
    {
        Known.Clear();
        Asked.Clear();
        Waiting.Clear();
        _loaded = true;
    }
    private static bool _listening;
    private static bool _draining;
    private static bool _dirty;
    private static float _savedAt;

    public static event Action<int> Learned;

    private static string FilePath => Path.Combine(Paths.CachePath, "NewAge2D", "things.txt");

    public static string Get(int thingId)
    {
        Load();
        lock (Known) return Known.TryGetValue(thingId, out var image) ? image : null;
    }

    public static void Learn(int thingId, string image)
    {
        if (thingId <= 0 || string.IsNullOrEmpty(image)) return;
        Load();
        bool fresh;
        lock (Known)
        {
            fresh = !Known.TryGetValue(thingId, out var old) || old != image;
            Known[thingId] = image;
        }
        if (!fresh) return;
        _dirty = true;
        try { Learned?.Invoke(thingId); }
        catch (Exception ex) { Plugin.Log.LogError("[вещи] " + ex); }
    }

    public static void Ask(int thingId)
    {
        if (thingId <= 0 || Get(thingId) != null) return;
        lock (Known)
        {
            if (Asked.TryGetValue(thingId, out float at) && Time.unscaledTime - at < 30f) return;
            Asked[thingId] = Time.unscaledTime;
            Waiting.Enqueue(thingId);
        }
        if (_draining || Plugin.Instance == null) return;
        _draining = true;
        Plugin.Instance.StartCoroutine(Drain());
    }

    private static IEnumerator Drain()
    {
        try
        {
            while (true)
            {
                var batch = new List<int>();
                lock (Known)
                    while (Waiting.Count > 0 && batch.Count < 8) batch.Add(Waiting.Dequeue());
                if (batch.Count == 0) yield break;
                if (!Listen())
                {
                    lock (Known) foreach (int id in batch) Asked.Remove(id);
                    yield break;
                }
                foreach (int id in batch)
                {
                    try { NetworkConnection.Instance.SendRequest(new GeneralThingHintRequest(id)); }
                    catch (Exception ex)
                    {
                        lock (Known) Asked.Remove(id);
                        Plugin.Log.LogWarning($"[вещи] запрос {id}: {ex.Message}");
                    }
                }
                float waited = 0f;
                while (waited < 0.1f)
                {
                    yield return null;
                    waited += Time.unscaledDeltaTime;
                }
            }
        }
        finally { _draining = false; }
    }

    private static bool Listen()
    {
        try
        {
            var connection = NetworkConnection.Instance;
            if (connection == null || !connection.IsConnected()) return false;
            if (!_listening)
            {
                connection.AddMessageListener(385, OnInfo);
                _listening = true;
            }
            return true;
        }
        catch { return false; }
    }

    private static void OnInfo(object message)
    {
        if (message is GeneralThingInfoMessage info) Learn(info.ThingId, info.Image);
    }

    public static void Tick()
    {
        if (!_dirty || Time.unscaledTime - _savedAt < 5f) return;
        Save();
    }

    public static void Stop()
    {
        try { if (_listening) NetworkConnection.Instance?.RemoveMessageListener(385, OnInfo); }
        catch { }
        _listening = false;
        Save();
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath)) return;
            lock (Known)
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int space = line.IndexOf(' ');
                    if (space <= 0) continue;
                    if (int.TryParse(line.Substring(0, space), out int id) && id > 0) Known[id] = line.Substring(space + 1).Trim();
                }
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[вещи] кэш не прочитан: " + ex.Message); }
    }

    private static void Save()
    {
        _dirty = false;
        _savedAt = Time.unscaledTime;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            List<string> lines;
            lock (Known) lines = Known.OrderBy(pair => pair.Key).Select(pair => pair.Key + " " + pair.Value).ToList();
            File.WriteAllLines(FilePath, lines);
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[вещи] кэш не записан: " + ex.Message); }
    }
}
