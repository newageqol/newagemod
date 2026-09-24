namespace NewAge2D;

internal static class Pixels
{
    private const int Step = 16384;
    private const long Limit = 96L * 1024 * 1024;

    private static readonly Dictionary<int, Stack<byte[]>> Free = new();
    private static long _held;

    internal static byte[] Rent(int size)
    {
        int capacity = (Math.Max(1, size) + Step - 1) / Step * Step;
        lock (Free)
        {
            if (Free.TryGetValue(capacity, out var stack) && stack.Count > 0)
            {
                var array = stack.Pop();
                _held -= array.Length;
                return array;
            }
        }
        return new byte[capacity];
    }

    internal static void Return(byte[] array)
    {
        if (array == null || array.Length % Step != 0) return;
        lock (Free)
        {
            if (_held + array.Length > Limit) return;
            if (!Free.TryGetValue(array.Length, out var stack)) Free[array.Length] = stack = new Stack<byte[]>();
            stack.Push(array);
            _held += array.Length;
        }
    }

    internal static void Clear()
    {
        lock (Free)
        {
            Free.Clear();
            _held = 0;
        }
    }
}
