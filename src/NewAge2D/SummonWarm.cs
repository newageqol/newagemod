using BepInEx;
using UnityEngine;

namespace NewAge2D;

internal static class SummonWarm
{
    private static readonly string[] First = { "stop", "prizuv" };
    private static readonly HashSet<int> Races = new();
    private static readonly HashSet<string> Looks = new();
    private static readonly HashSet<string> Empty = new();
    private static bool _loaded;
    private static int _generation = -1;
    private static float _checkAt;

    private static string Saved => Path.Combine(Paths.CachePath, "NewAge2D", "summons.txt");

    internal static IEnumerable<string> Kept => Looks;

    internal static void Summoned(int race)
    {
        if (Fighters.ClipFor(race) == null) return;
        Load();
        if (Races.Add(race)) Save();
        if (Plugin.FlashFight) Warm(race, 1);
    }

    internal static void Tick()
    {
        if (!Plugin.FlashFight || Time.unscaledTime < _checkAt) return;
        _checkAt = Time.unscaledTime + 1f;
        if (!(Fighters.Combat() is CombatData combat) || combat.MyCharacter == null) return;
        Load();
        if (Races.Count == 0) return;
        if (_generation != FrameCache.Generation)
        {
            _generation = FrameCache.Generation;
            Looks.Clear();
            Empty.Clear();
        }
        foreach (int race in Races) Warm(race, 2);
    }

    private static void Warm(int race, int priority)
    {
        string clip = Fighters.ClipFor(race);
        if (clip == null || Plugin.Store == null) return;
        if (!Plugin.Store.Ready(clip))
        {
            Plugin.Store.Prefetch(new[] { clip });
            return;
        }
        foreach (bool left in new[] { true, false })
        {
            var request = new DollRequest { Race = race, Clip = clip, Scale = Plugin.CombatScale, Smooth = Plugin.Smooth, Left = left };
            string look = request.Look;
            Looks.Add(look);
            foreach (string label in First) Order(request, look, label, priority);
            Order(request, look, "stop" + Fighters.Living, priority + 1);
        }
    }

    private static void Order(DollRequest request, string look, string label, int priority)
    {
        string sequence = FrameCache.SequenceKey(look, label);
        if (Empty.Contains(sequence)) return;
        if (!FrameCache.BeginSequence(sequence)) return;
        bool alive = label.EndsWith(Fighters.Living, StringComparison.Ordinal);
        string real = alive ? label.Substring(0, label.Length - Fighters.Living.Length) : label;
        if (!request.Left) real = Fighters.Mirror(real);
        float ppu = Field.PxPerUnit * request.Scale;
        int generation = FrameCache.Generation;
        string who = "призыв " + request.Clip;
        if (Trace.On) Trace.Write($"{who}: {label} облика {Trace.Look(look)} рисуется заранее, чтобы существо упало сразу после призыва");
        DollWorker.EnqueueSequence(request, real, 0, alive, result => MainThread.Post(() =>
        {
            if (FrameCache.Generation != generation) return;
            if (result.Error != null || result.Frames.Count == 0)
            {
                FrameCache.EndSequence(sequence, false);
                string why = result.Error ?? "нет кадров";
                bool never = why.Contains("нет метки");
                if (never) Empty.Add(sequence);
                if (Trace.On) Trace.Write($"{who}: {label} облика {Trace.Look(look)} не нарисован: {why}"
                    + (never ? ", больше не пробую" : ""));
                return;
            }
            FrameCache.Remember(look, result.Labels, result.FrameRate);
            FrameCache.SetHit(sequence, result.HitShare);
            if (label == "stop" || label == "stop" + Fighters.Living)
            {
                var idle = result.Frames[0];
                FrameCache.SetStop(look, float.IsNaN(result.BarY) ? idle.Height * (1f - idle.PivotY) : result.BarY);
                FrameCache.SetHead(look, result.HeadX);
            }
            FrameCache.Store(look, label, sequence, result.Frames, ppu, null, priority <= 1);
        }), priority, who, sequence);
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(Saved)) return;
            foreach (string line in File.ReadAllLines(Saved))
                if (int.TryParse(line.Trim(), out int race) && Fighters.ClipFor(race) != null) Races.Add(race);
            if (Trace.On && Races.Count > 0) Trace.Write($"призыв: прошлые призывы игрока, рисуются заранее: расы {string.Join(", ", Races)}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[призыв] список прошлых призывов: " + ex.Message); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Saved));
            File.WriteAllLines(Saved, Races.OrderBy(race => race).Select(race => race.ToString()));
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[призыв] список прошлых призывов: " + ex.Message); }
    }
}
