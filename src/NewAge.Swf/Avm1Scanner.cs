namespace NewAge.Swf;

public readonly struct Avm1Value
{
    public Avm1Value(string text) { Text = text; Number = 0; IsNumber = false; }
    public Avm1Value(double number) { Text = null; Number = number; IsNumber = true; }

    public string Text { get; }
    public double Number { get; }
    public bool IsNumber { get; }

    public bool Is(string text) => !IsNumber && string.Equals(Text, text, StringComparison.Ordinal);

    public override string ToString() => IsNumber ? Number.ToString("0.###") : Text ?? "<null>";
}

public static class Avm1Scanner
{
    public static List<Avm1Value> Pushes(byte[] code)
    {
        var result = new List<Avm1Value>();
        var pool = new List<string>();
        int i = 0;

        while (i < code.Length)
        {
            byte op = code[i];
            if (op < 0x80) { i++; continue; }
            if (i + 3 > code.Length) break;

            int length = code[i + 1] | (code[i + 2] << 8);
            int start = i + 3;
            int end = start + length;
            if (end > code.Length) break;

            switch (op)
            {
                case 0x88:
                    ReadConstantPool(code, start, end, pool);
                    break;
                case 0x96:
                    ReadPush(code, start, end, pool, result);
                    break;
            }

            i = end;
        }

        return result;
    }

    public static List<Avm1Value> Pushes(SwfFile file, IEnumerable<SwfTag> tags)
    {
        var result = new List<Avm1Value>();
        foreach (var tag in tags)
        {
            if (tag.Code is not (SwfTagCode.DoAction or SwfTagCode.DoInitAction)) continue;
            int offset = tag.Code == SwfTagCode.DoInitAction ? 2 : 0;
            result.AddRange(Pushes(file.Body[(tag.BodyStart + offset)..tag.BodyEnd]));
        }
        return result;
    }

    private static void ReadConstantPool(byte[] code, int start, int end, List<string> pool)
    {
        pool.Clear();
        if (start + 2 > end) return;

        int count = code[start] | (code[start + 1] << 8);
        int p = start + 2;
        for (int n = 0; n < count && p < end; n++)
        {
            int stop = p;
            while (stop < end && code[stop] != 0) stop++;
            pool.Add(System.Text.Encoding.UTF8.GetString(code, p, stop - p));
            p = stop + 1;
        }
    }

    private static void ReadPush(byte[] code, int start, int end, List<string> pool, List<Avm1Value> result)
    {
        int p = start;
        while (p < end)
        {
            byte type = code[p++];
            switch (type)
            {
                case 0:
                {
                    int stop = p;
                    while (stop < end && code[stop] != 0) stop++;
                    result.Add(new Avm1Value(System.Text.Encoding.UTF8.GetString(code, p, stop - p)));
                    p = stop + 1;
                    break;
                }
                case 1:
                    if (p + 4 > end) return;
                    result.Add(new Avm1Value(BitConverter.ToSingle(code, p)));
                    p += 4;
                    break;
                case 2:
                case 3:
                    result.Add(new Avm1Value((string)null));
                    break;
                case 4:
                    if (p >= end) return;
                    result.Add(new Avm1Value($"r{code[p]}"));
                    p++;
                    break;
                case 5:
                    if (p >= end) return;
                    result.Add(new Avm1Value(code[p] != 0 ? "true" : "false"));
                    p++;
                    break;
                case 6:
                    if (p + 8 > end) return;
                    result.Add(new Avm1Value(BitConverter.ToDouble(code, p)));
                    p += 8;
                    break;
                case 7:
                    if (p + 4 > end) return;
                    result.Add(new Avm1Value(BitConverter.ToInt32(code, p)));
                    p += 4;
                    break;
                case 8:
                {
                    if (p >= end) return;
                    int index = code[p++];
                    result.Add(new Avm1Value(index < pool.Count ? pool[index] : null));
                    break;
                }
                case 9:
                {
                    if (p + 2 > end) return;
                    int index = code[p] | (code[p + 1] << 8);
                    p += 2;
                    result.Add(new Avm1Value(index < pool.Count ? pool[index] : null));
                    break;
                }
                default:
                    return;
            }
        }
    }
}
