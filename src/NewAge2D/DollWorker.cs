namespace NewAge2D;

public static class DollWorker
{
    private static readonly Queue<(Func<object> Work, Action<object> Done)> Jobs = new();
    private static readonly Queue<(Func<object> Work, Action<object> Done)> Soon = new();
    private static readonly Queue<(Func<object> Work, Action<object> Done)> Idle = new();
    private static readonly Queue<(Func<object> Work, Action<object> Done)> Later = new();
    private static readonly List<Thread> Threads = new();
    private static int _running;

    internal static int WorkerCount => Math.Max(2, Math.Min(8, Environment.ProcessorCount - 2));

    public static SwfStore Store;

    public static bool Compress;

    internal static bool Busy
    {
        get
        {
            lock (Jobs) return _running > 0 || Jobs.Count > 0 || Soon.Count > 0 || Idle.Count > 0 || Later.Count > 0;
        }
    }

    internal static string Stats()
    {
        lock (Jobs) return $"срочно {Jobs.Count}, скоро {Soon.Count}, фон {Idle.Count}, последние {Later.Count}, рисуют {_running}/{Threads.Count}";
    }

    public static void Clear()
    {
        lock (Jobs)
        {
            Jobs.Clear();
            Soon.Clear();
            Idle.Clear();
            Later.Clear();
            Waiting.Clear();
        }
    }

    public static void Enqueue(DollRequest request, Action<DollPicture> done, bool urgent = true)
    {
        Run(() => Doll.Render(request, Store),
            result => done(result as DollPicture ?? new DollPicture { Error = Describe(result) }), urgent);
    }

    internal static DollSequence Packed(DollSequence sequence)
    {
        if (Compress || sequence?.Packer != null) Dxt.Pack(sequence);
        return sequence;
    }

    private static readonly object Skip = new();
    private static readonly Dictionary<string, Ticket> Waiting = new();

    private sealed class Ticket
    {
        public int Taken;
        public bool Promoted;
        public Func<object> Work;
        public Action<object> Done;
    }

    public static bool Promote(string key)
    {
        lock (Jobs)
        {
            if (key == null || !Waiting.TryGetValue(key, out var ticket) || ticket.Taken != 0 || ticket.Promoted) return false;
            ticket.Promoted = true;
            Jobs.Enqueue((ticket.Work, ticket.Done));
            Monitor.PulseAll(Jobs);
            return true;
        }
    }

    public static void EnqueueSequence(DollRequest request, string label, int kick, bool alive, Action<DollSequence> done, int priority, string who = null, string key = null)
    {
        long queued = System.Diagnostics.Stopwatch.GetTimestamp();
        var ticket = new Ticket();
        ticket.Done = result =>
        {
            if (ReferenceEquals(result, Skip)) return;
            done(result as DollSequence ?? new DollSequence { Label = label, Error = Describe(result) });
        };
        ticket.Work = () =>
        {
            if (Interlocked.CompareExchange(ref ticket.Taken, 1, 0) != 0) return Skip;
            if (key != null)
                lock (Jobs)
                    if (Waiting.TryGetValue(key, out var waiting) && ReferenceEquals(waiting, ticket)) Waiting.Remove(key);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            var sequence = Doll.RenderSequence(request, Store, label, kick, alive);
            long drawn = System.Diagnostics.Stopwatch.GetTimestamp();
            Packed(sequence);
            long packed = System.Diagnostics.Stopwatch.GetTimestamp();
            if (Trace.On)
                Trace.Write($"«{who}» нарисовано {label}{(alive ? "~" : "")}{(kick != 0 ? "/" + kick : "")} облик {Trace.Look(request.Look)}: кадров {sequence.Frames.Count}, ждало {Ms(queued, started)} мс, рисование {Ms(started, drawn)} мс (таймлайн {Ms(0, sequence.AdvanceTicks)}, позы {Ms(0, sequence.BlendTicks)}, растр {Ms(0, sequence.RasterTicks)}, сжатие по ходу {Ms(0, sequence.PackTicks)}), досжатие {Ms(drawn, packed)} мс{(sequence.Error != null ? ", ошибка: " + sequence.Error : "")}");
            return sequence;
        };
        if (key != null)
            lock (Jobs) Waiting[key] = ticket;
        Run(ticket.Work, ticket.Done, priority);
    }

    private static long Ms(long from, long to) => (to - from) * 1000 / System.Diagnostics.Stopwatch.Frequency;

    private static string Describe(object result) => result is Exception ex ? ex.ToString() : "пустой результат";

    public static void Run(Func<object> work, Action<object> done, bool urgent) => Run(work, done, urgent ? 0 : 2);

    public static void Run(Func<object> work, Action<object> done, int priority)
    {
        lock (Jobs)
        {
            (priority <= 0 ? Jobs : priority == 1 ? Soon : priority == 2 ? Idle : Later).Enqueue((work, done));
            Monitor.PulseAll(Jobs);
            Threads.RemoveAll(thread => !thread.IsAlive);
            while (Threads.Count < WorkerCount)
            {
                bool urgentOnly = Threads.Count == 0;
                var thread = new Thread(() => Loop(urgentOnly)) { IsBackground = true, Name = "NewAge2D.Doll" + Threads.Count };
                Threads.Add(thread);
                thread.Start();
            }
        }
    }

    private static int _soonRunning;
    private static int _idleRunning;

    private static int SharedCap => Jobs.Count == 0
        ? Math.Max(2, WorkerCount - 2)
        : Math.Max(2, Math.Min(3, WorkerCount - 1));

    private static int IdleCap => Math.Max(1, Math.Min(SharedCap - 1, SharedCap / 2 + 1));

    private static int Pick(bool urgentOnly)
    {
        bool shared = !urgentOnly && _soonRunning + _idleRunning < SharedCap;
        bool starved = shared && Soon.Count > 0 && _soonRunning == 0 && _running - _idleRunning >= WorkerCount - 2;
        if (Jobs.Count > 0 && !starved) return 0;
        if (!shared) return -1;
        if (Soon.Count > 0) return 1;
        if (Idle.Count > 0 && _idleRunning < IdleCap) return 2;
        if (Later.Count > 0 && _idleRunning < IdleCap) return 3;
        return -1;
    }

    private static void Loop(bool urgentOnly)
    {
        while (true)
        {
            (Func<object> Work, Action<object> Done) job;
            int kind;
            lock (Jobs)
            {
                while ((kind = Pick(urgentOnly)) < 0) Monitor.Wait(Jobs);
                job = kind == 0 ? Jobs.Dequeue() : kind == 1 ? Soon.Dequeue() : kind == 2 ? Idle.Dequeue() : Later.Dequeue();
                if (kind == 1) _soonRunning++;
                else if (kind >= 2) _idleRunning++;
                _running++;
            }
            object result;
            try { result = job.Work(); }
            catch (Exception ex) { result = ex; }
            lock (Jobs)
            {
                _running--;
                if (kind == 1) _soonRunning--;
                else if (kind >= 2) _idleRunning--;
                Monitor.PulseAll(Jobs);
            }
            try { job.Done(result); } catch { }
        }
    }
}
