namespace NewAge2D;

internal static class MainThread
{
    private static readonly Queue<Action> Pending = new();

    public static void Post(Action action)
    {
        lock (Pending) Pending.Enqueue(action);
    }

    public static void Clear()
    {
        lock (Pending) Pending.Clear();
    }

    public static void Drain()
    {
        for (int i = 0; i < 16; i++)
        {
            Action action;
            lock (Pending)
            {
                if (Pending.Count == 0) return;
                action = Pending.Dequeue();
            }
            try { action(); }
            catch (Exception ex) { Plugin.Log.LogError(ex.ToString()); }
        }
    }
}
