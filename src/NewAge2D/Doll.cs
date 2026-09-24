using System.Runtime.InteropServices;
using NewAge.Swf;
using NewAge.Swf.Display;
using NewAge.Swf.Skia;
using SkiaSharp;

namespace NewAge2D;

public sealed class DollWear
{
    public int Slot;
    public int ThingId;
    public string Image;
}

public sealed class DollRequest
{
    public int Race;
    public int Gender;
    public float Scale = 3f;
    public int Smooth = 1;
    public string Label;
    public int Frame;
    public bool Left = true;
    public List<DollWear> Wear = new();

    public string Clip;

    public string Look => Clip != null
        ? $"{Clip}/{Smooth}/{Scale:0.##}/{Doll.Vivid:0.##}/{(Left ? "L" : "R")}|"
        : $"{Race}/{Gender}/{Scale:0.##}/{Smooth}/{Doll.Vivid:0.##}/{(Left ? "L" : "R")}|" + string.Join(",", Wear.OrderBy(w => w.Slot).Select(w => $"{w.Slot}:{w.Image}"));

    public string Signature => Label == null ? Look : $"{Look}#{Label}:{Frame}";
}

public readonly struct LabelRange
{
    public readonly int Start;
    public readonly int Count;

    public LabelRange(int start, int count)
    {
        Start = start;
        Count = count;
    }
}

public sealed class DollPicture
{
    public int Width;
    public int Height;
    public float PivotX;
    public float PivotY;
    public float BodyHeight;
    public int FrameIndex;
    public double FrameRate;
    public Dictionary<string, LabelRange> Labels;
    public byte[] Rgba;
    public byte[] Dxt;
    public int DxtSize;
    public bool Pooled;
    public bool Raw;
    public int Mips = 1;
    public float Scale = 1f;
    public List<string> Notes = new();
    public string Error;
}

public sealed class DollSequence
{
    public string Label;
    public float BodyHeight;
    public double FrameRate;
    public Dictionary<string, LabelRange> Labels;
    public List<DollPicture> Frames = new();
    public List<string> Notes = new();
    public string Error;
    public Action<DollPicture> Packer;
    public float HeadX = float.NaN;
    public float BarY = float.NaN;
    public float HitShare = float.NaN;
    public long AdvanceTicks;
    public long BlendTicks;
    public long RasterTicks;
    public long PackTicks;
}

public static class Doll
{
    public static string DumpDir;

    public static Action<DollPicture> Packer;

    private static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

    public static readonly int[] SlotOrder = { 1, 3, 5, 4, 13, 14, 11, 12 };

    public const double StageRate = 20.0;

    private static void Dump(DollPicture picture, string name)
    {
        if (string.IsNullOrEmpty(DumpDir) || picture?.Rgba == null) return;
        try
        {
            Directory.CreateDirectory(DumpDir);
            var info = new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            int stride = picture.Width * 4;
            var rows = new byte[stride * picture.Height];
            for (int row = 0; row < picture.Height; row++)
                Buffer.BlockCopy(picture.Rgba, (picture.Height - 1 - row) * stride, rows, row * stride, stride);
            Marshal.Copy(rows, 0, bitmap.GetPixels(), rows.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(DumpDir, name));
            data.SaveTo(file);
        }
        catch { }
    }

    private static readonly Dictionary<int, string[]> Parts = new()
    {
        [1] = new[] { "bp4" },
        [3] = new[] { "bp6", "bp7", "bp5", "bp2", "bp15", "bp17", "bp18" },
        [5] = new[] { "bp3", "bp16" },
        [4] = new[] { "bp1", "bp14" },
        [13] = new[] { "bp10", "bp13", "bp8", "bp11" },
        [14] = new[] { "bp9", "bp12" },
        [11] = new[] { "bp1", "bp14" },
        [12] = new[] { "bp14", "bp1" },
    };

    public static bool IsVisualSlot(int slot) => Parts.ContainsKey(slot);

    public static string RaceName(int race) => race switch
    {
        13 => "human",
        8 => "orc",
        12 => "elves",
        10 => "vampire",
        11 => "gnom",
        9 => "troll",
        _ => null,
    };

    private static char RaceLetter(int race) => race switch
    {
        8 => 'o',
        12 => 'e',
        10 => 'v',
        11 => 'g',
        9 => 't',
        _ => 'h',
    };

    private static bool Woman(int race, int gender) => gender == 2 && race != 9;

    public static string BodyFile(int race, int gender)
    {
        string name = RaceName(race);
        if (name == null) return null;
        string version = race == 12 || race == 9 ? "503" : "502";
        return $"{name}({(Woman(race, gender) ? "woman" : "man")}){version}.swf";
    }

    public static string Prefix(int race, int gender) => $"{RaceLetter(race)}{(Woman(race, gender) ? 'w' : 'm')}";

    public static string WearFile(string image)
    {
        if (string.IsNullOrEmpty(image)) return null;
        string name = image.Trim();
        if (name.StartsWith("image_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(6);
        return name.Length == 0 ? null : name + ".swf";
    }

    private static void HideBars(MovieClip root)
    {
        foreach (var layer in root.Layers)
            if (layer.Name == "barInfo" || layer.Name == "selectEmpty") layer.Visible = false;
    }

    public static Dictionary<string, LabelRange> LabelRanges(SwfTimeline timeline)
    {
        var result = new Dictionary<string, LabelRange>(StringComparer.Ordinal);
        var ordered = timeline.Labels.OrderBy(pair => pair.Value).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            int start = ordered[i].Value;
            int end = timeline.FrameCount;
            for (int j = i + 1; j < ordered.Count; j++)
            {
                if (ordered[j].Value <= start) continue;
                end = ordered[j].Value;
                break;
            }
            int count = Math.Max(1, end - start);
            string name = ordered[i].Key;
            if (count > 1 && name != "stop" && name != "die" && end - 1 < timeline.FrameCount && timeline.Frames[end - 1].Actions.Count > 0) count--;
            result[name] = new LabelRange(start, count);
        }
        return result;
    }

    private sealed class Outfit
    {
        public readonly List<(MovieClip Target, MovieClip Part, string Name, int Slot)> Attached = new();
        public readonly List<string> Notes = new();
    }

    private static bool SwapHands(DollRequest request, string label)
    {
        if (label != "die") return !request.Left;
        bool right = request.Wear.Any(w => w.Slot == 12);
        bool left = request.Wear.Any(w => w.Slot == 11);
        return right || !left;
    }

    private static Outfit Dress(MovieClip root, DollRequest request, SwfStore store, bool swapHands)
    {
        var outfit = new Outfit();
        var skins = new Dictionary<MovieClip, int>();
        var weapons = new Dictionary<MovieClip, int>();
        string prefix = Prefix(request.Race, request.Gender);
        foreach (int slot in SlotOrder)
        {
            var wear = request.Wear.FirstOrDefault(w => w.Slot == slot);
            if (wear == null) continue;

            string file = WearFile(wear.Image);
            if (file == null)
            {
                outfit.Notes.Add($"слот {slot}: у вещи {wear.ThingId} нет имени картинки");
                continue;
            }

            var movie = store.Get(file, out string reason);
            if (movie == null)
            {
                outfit.Notes.Add($"слот {slot}: {file} — {reason}");
                continue;
            }

            string[] points = Parts[slot];
            bool hand = slot == 11 || slot == 12;
            int worn = 0;
            for (int i = 0; i < points.Length; i++)
            {
                int partIndex = hand && swapHands ? (i == 0 ? 2 : 1) : i + 1;
                string symbol = $"part{partIndex}_{prefix}";
                var instance = movie.CreateInstance(symbol);
                if (instance == null) continue;
                var target = root.FindDescendant(points[i]);
                if (target == null)
                {
                    outfit.Notes.Add($"в теле нет точки {points[i]}");
                    continue;
                }
                string name = $"{slot}:{symbol}";
                int depth;
                if (slot == 11 || slot == 12)
                {
                    weapons.TryGetValue(target, out int used);
                    depth = 100000 + used;
                    weapons[target] = used + 1;
                }
                else
                {
                    skins.TryGetValue(target, out int used);
                    depth = 50000 + used;
                    skins[target] = used + 1;
                }
                target.AttachExternal(instance, name, depth);
                if (hand)
                {
                    bool down = (slot == 12) != swapHands;
                    foreach (var layer in instance.Layers)
                    {
                        if (layer.Name == "down") layer.Visible = down;
                        else if (layer.Name == "up") layer.Visible = !down;
                    }
                }
                outfit.Attached.Add((target, instance, name, slot));
                worn++;
            }
            outfit.Notes.Add($"слот {slot}: {file} — частей {worn}");
        }
        foreach (var (point, symbol) in Bare)
        {
            if (request.Wear.Any(w => Parts.TryGetValue(w.Slot, out var points) && points.Contains(point))) continue;
            var target = root.FindDescendant(point);
            var instance = target != null ? root.Movie.CreateInstance(symbol) : null;
            if (instance == null) continue;
            target.AttachExternal(instance, symbol, 50000);
            outfit.Attached.Add((target, instance, symbol, -1));
            outfit.Notes.Add($"{point} без вещей: {symbol}");
        }
        return outfit;
    }

    private static readonly (string Point, string Symbol)[] Bare = { ("bp4", "hear"), ("bp7", "und_cloth") };

    private static float Reach(SwfMovie body, MovieClip root, Outfit outfit, int kick, in SwfMatrix matrix)
    {
        float left = float.MaxValue;
        foreach (var (target, _, _, slot) in outfit.Attached)
        {
            if (slot != kick || !FindClip(root, target, matrix, out var world)) continue;
            var bounds = SwfBounds.Measure(body, target, world);
            if (!bounds.IsEmpty && bounds.Left < left) left = bounds.Left;
        }
        return left;
    }

    private static bool FindClip(MovieClip clip, MovieClip wanted, SwfMatrix parent, out SwfMatrix world)
    {
        foreach (var layer in clip.Layers)
        {
            if (layer.Clip == null) continue;
            var local = layer.Matrix.Concat(parent);
            if (ReferenceEquals(layer.Clip, wanted))
            {
                world = local;
                return true;
            }
            if (FindClip(layer.Clip, wanted, local, out world)) return true;
        }
        world = SwfMatrix.Identity;
        return false;
    }

    private static bool FindPart(MovieClip clip, string name, SwfMatrix parent, out SwfMatrix world)
    {
        foreach (var layer in clip.Layers)
        {
            if (layer.Clip == null) continue;
            var local = layer.Matrix.Concat(parent);
            if (layer.Name == name || layer.Clip.Name == name)
            {
                world = local;
                return true;
            }
            if (FindPart(layer.Clip, name, local, out world)) return true;
        }
        world = SwfMatrix.Identity;
        return false;
    }

    private static bool StillDressed(MovieClip root, Outfit outfit)
    {
        foreach (var (target, _, _, _) in outfit.Attached)
        {
            var current = root.FindDescendant(target.Name);
            if (!ReferenceEquals(current, target)) return false;
        }
        return true;
    }

    private struct Pose
    {
        public SwfMatrix Matrix;
        public double Ratio;
    }

    private static void Snapshot(MovieClip clip, Dictionary<DisplayObject, Pose> into)
    {
        foreach (var layer in clip.Layers)
        {
            into[layer] = new Pose { Matrix = layer.Matrix, Ratio = layer.Ratio };
            if (layer.Clip != null) Snapshot(layer.Clip, into);
        }
    }

    private static SwfMatrix Mix(in SwfMatrix from, in SwfMatrix to, double t) => new(
        from.A + (to.A - from.A) * t, from.B + (to.B - from.B) * t,
        from.C + (to.C - from.C) * t, from.D + (to.D - from.D) * t,
        from.Tx + (to.Tx - from.Tx) * t, from.Ty + (to.Ty - from.Ty) * t);

    private static void Blend(MovieClip clip, Dictionary<DisplayObject, Pose> from, Dictionary<DisplayObject, Pose> to, double t, List<(DisplayObject Layer, Pose Saved)> restore)
    {
        foreach (var layer in clip.Layers)
        {
            if (from.TryGetValue(layer, out var a) && to.TryGetValue(layer, out var b))
            {
                restore.Add((layer, new Pose { Matrix = layer.Matrix, Ratio = layer.Ratio }));
                layer.Matrix = Mix(a.Matrix, b.Matrix, t);
                layer.Ratio = a.Ratio + (b.Ratio - a.Ratio) * t;
            }
            if (layer.Clip != null) Blend(layer.Clip, from, to, t, restore);
        }
    }

    private static void Restore(List<(DisplayObject Layer, Pose Saved)> restore)
    {
        foreach (var (layer, saved) in restore)
        {
            layer.Matrix = saved.Matrix;
            layer.Ratio = saved.Ratio;
        }
        restore.Clear();
    }

    public static int SlotOf(string label) =>
        label.EndsWith("_right", StringComparison.Ordinal) ? 12 : label.EndsWith("_left", StringComparison.Ordinal) ? 11 : 0;

    private static void Kick(Outfit outfit, int slot)
    {
        if (slot == 0) return;
        foreach (var (_, part, _, partSlot) in outfit.Attached)
        {
            if (partSlot != slot || part.FrameCount < 2) continue;
            part.GotoFrame(0);
            part.Play();
            part.GotoFrame(1);
        }
    }

    private const int CycleCap = 180;
    private const int CycleLimit = 360;

    private static void Lengths(MovieClip clip, HashSet<int> lengths)
    {
        foreach (var layer in clip.Layers)
        {
            var child = layer.Clip;
            if (child == null || !layer.Visible) continue;
            if (child.Playing && child.FrameCount > 1) lengths.Add(child.FrameCount);
            Lengths(child, lengths);
        }
    }

    private static int Cycle(MovieClip clip)
    {
        var lengths = new HashSet<int>();
        Lengths(clip, lengths);
        if (lengths.Count == 0) return 1;
        long lcm = 1;
        int longest = 1;
        foreach (int length in lengths)
        {
            longest = Math.Max(longest, length);
            if (lcm <= CycleCap) lcm = lcm / Gcd(lcm, length) * length;
        }
        return lcm <= CycleCap ? (int)lcm : Math.Min(longest, CycleLimit);
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            long t = a % b;
            a = b;
            b = t;
        }
        return a;
    }

    private static void AdvanceChildren(MovieClip clip)
    {
        foreach (var layer in clip.Layers) layer.Clip?.Advance();
    }

    private static bool Same(DollPicture a, DollPicture b)
    {
        if (a == null || b == null) return false;
        if (a.Width != b.Width || a.Height != b.Height) return false;
        if (Math.Abs(a.PivotX - b.PivotX) > 0.0001f || Math.Abs(a.PivotY - b.PivotY) > 0.0001f) return false;
        int size = a.Width * a.Height * 4;
        if (a.Rgba == null || b.Rgba == null || a.Rgba.Length < size || b.Rgba.Length < size) return false;
        for (int i = 0; i < size; i++)
            if (a.Rgba[i] != b.Rgba[i]) return false;
        return true;
    }

    internal static void Release(DollPicture picture)
    {
        if (picture == null || !picture.Pooled) return;
        Pixels.Return(picture.Rgba);
        picture.Rgba = null;
    }

    private static void Push(DollSequence sequence, DollPicture picture)
    {
        var last = sequence.Frames.Count > 0 ? sequence.Frames[sequence.Frames.Count - 1] : null;
        if (Same(last, picture))
        {
            sequence.Frames.Add(last);
            Release(picture);
            return;
        }
        if (last != null && sequence.Packer != null && last.Dxt == null)
        {
            long mark = Now;
            sequence.Packer(last);
            sequence.PackTicks += Now - mark;
        }
        sequence.Frames.Add(picture);
    }

    private static DollPicture Draw(SwfMovie body, SwfRenderer renderer, MovieClip root, in SwfMatrix matrix, DollSequence sequence, Dictionary<string, LabelRange> labels)
    {
        long mark = Now;
        var picture = Finish(Rasterize(body, renderer, root, matrix, sequence.Packer != null), sequence, labels);
        sequence.RasterTicks += Now - mark;
        return picture;
    }

    private static float HeadCenter(DollPicture picture, in SwfMatrix head, float scale, float fallback)
    {
        if (picture?.Rgba == null || picture.Width <= 0 || picture.Height <= 0) return fallback;
        float pivotX = picture.PivotX * picture.Width;
        float headColumn = (float)head.Tx + pivotX;
        float headRow = picture.PivotY * picture.Height - (float)head.Ty;
        int reach = (int)Math.Ceiling(24f * scale);
        int wide = (int)Math.Ceiling(40f * scale);
        int rowFrom = Math.Max(0, (int)headRow - reach);
        int rowTo = Math.Min(picture.Height - 1, (int)headRow + reach);
        int columnFrom = Math.Max(0, (int)headColumn - wide);
        int columnTo = Math.Min(picture.Width - 1, (int)headColumn + wide);
        double sum = 0;
        long count = 0;
        for (int row = rowFrom; row <= rowTo; row++)
        {
            int line = row * picture.Width * 4;
            for (int column = columnFrom; column <= columnTo; column++)
            {
                if (picture.Rgba[line + column * 4 + 3] <= 96) continue;
                sum += column;
                count++;
            }
        }
        return count < 16 ? fallback : (float)(sum / count) - pivotX;
    }

    public static float Vivid = 1f;

    private sealed class Grade
    {
        public float Lift;
        public byte[] Lut;
    }

    private static Grade _grade;

    private static void Paint(byte[] rgba, int size)
    {
        float vivid = Vivid;
        if (vivid <= 1.001f) return;
        float lift = 1f + (vivid - 1f) * 0.4f;
        var lut = Curve(lift);
        for (int i = 0; i + 3 < size; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            float r = rgba[i];
            float g = rgba[i + 1];
            float b = rgba[i + 2];
            float grey = 0.299f * r + 0.587f * g + 0.114f * b;
            rgba[i] = lut[Byte(grey + (r - grey) * vivid)];
            rgba[i + 1] = lut[Byte(grey + (g - grey) * vivid)];
            rgba[i + 2] = lut[Byte(grey + (b - grey) * vivid)];
        }
    }

    private static int Byte(float value) => value <= 0f ? 0 : value >= 255f ? 255 : (int)(value + 0.5f);

    private const float Edge = 0.4f;
    [ThreadStatic] private static byte[] _copy;
    private static void Sharpen(byte[] rgba, int width, int height)
    {
        int size = width * height * 4;
        if (width < 3 || height < 3 || rgba.Length < size) return;
        var copy = _copy;
        if (copy == null || copy.Length < size) _copy = copy = new byte[size];
        Buffer.BlockCopy(rgba, 0, copy, 0, size);
        int stride = width * 4;
        for (int y = 1; y < height - 1; y++)
        {
            int row = y * stride;
            for (int x = 1; x < width - 1; x++)
            {
                int at = row + x * 4;
                if (copy[at + 3] < 8) continue;
                int red = 0, green = 0, blue = 0, count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int near = at + dy * stride;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int one = near + dx * 4;
                        if (copy[one + 3] < 8) continue;
                        red += copy[one];
                        green += copy[one + 1];
                        blue += copy[one + 2];
                        count++;
                    }
                }
                if (count < 5) continue;
                rgba[at] = (byte)Byte(copy[at] + (copy[at] - (float)red / count) * Edge);
                rgba[at + 1] = (byte)Byte(copy[at + 1] + (copy[at + 1] - (float)green / count) * Edge);
                rgba[at + 2] = (byte)Byte(copy[at + 2] + (copy[at + 2] - (float)blue / count) * Edge);
            }
        }
    }

    private static byte[] Curve(float lift)
    {
        var grade = _grade;
        if (grade != null && Math.Abs(grade.Lift - lift) < 0.001f) return grade.Lut;
        var lut = new byte[256];
        for (int i = 0; i < 256; i++) lut[i] = (byte)Byte((i - 128f) * lift + 128f);
        _grade = new Grade { Lift = lift, Lut = lut };
        return lut;
    }

    private static DollPicture Rasterize(SwfMovie body, SwfRenderer renderer, MovieClip root, in SwfMatrix matrix, bool pooled = false, bool sharp = true)
    {
        var picture = new DollPicture();
        var bounds = SwfBounds.Measure(body, root, matrix);
        if (bounds.IsEmpty) bounds = new SKRect(0, 0, 1, 1);
        bounds.Inflate(2, 2);

        int width = (Math.Max(1, (int)Math.Ceiling(bounds.Width)) + 3) / 4 * 4;
        int height = (Math.Max(1, (int)Math.Ceiling(bounds.Height)) + 3) / 4 * 4;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(0, height);
        canvas.Scale(1, -1);
        canvas.Translate(-bounds.Left, -bounds.Top);
        renderer.Draw(canvas, root, matrix);
        canvas.Flush();

        using var pixmap = surface.PeekPixels();
        int size = info.BytesSize;
        picture.Rgba = pooled ? Pixels.Rent(size) : new byte[size];
        picture.Pooled = pooled;
        Marshal.Copy(pixmap.GetPixels(), picture.Rgba, 0, size);
        Paint(picture.Rgba, size);
        if (sharp) Sharpen(picture.Rgba, width, height);

        picture.Width = width;
        picture.Height = height;
        picture.PivotX = -bounds.Left / width;
        picture.PivotY = (height + bounds.Top) / height;
        picture.FrameIndex = root.CurrentFrame;
        return picture;
    }

    private static SwfMovie Body(DollRequest request, SwfStore store, out string error)
    {
        error = null;
        string bodyName = request.Clip ?? BodyFile(request.Race, request.Gender);
        if (bodyName == null)
        {
            error = $"неизвестная раса {request.Race}";
            return null;
        }
        var body = store.Get(bodyName, out string why);
        if (body == null) error = $"нет ролика тела {bodyName}: {why}";
        return body;
    }

    public static DollPicture Render(DollRequest request, SwfStore store)
    {
        var body = Body(request, store, out string error);
        if (body == null) return new DollPicture { Error = error };

        var root = body.CreateRoot();
        var labels = LabelRanges(root.Timeline);
        int stopFrame = labels.TryGetValue("stop", out var stop) ? stop.Start : 0;
        root.GotoAndStop(stopFrame);
        HideBars(root);

        var matrix = new SwfMatrix(request.Scale, 0, 0, request.Scale, 0, 0);
        float bodyHeight = SwfBounds.Measure(body, root, matrix).Height;

        int frame = stopFrame;
        if (request.Label != null && labels.TryGetValue(request.Label, out var range))
            frame = range.Start + Math.Clamp(request.Frame, 0, Math.Max(0, range.Count - 1));
        if (frame != stopFrame)
        {
            root.GotoAndStop(frame);
            HideBars(root);
        }

        var outfit = Dress(root, request, store, SwapHands(request, request.Label));
        using var renderer = new SwfRenderer(body);
        var picture = Rasterize(body, renderer, root, matrix);
        picture.Labels = labels;
        picture.FrameRate = StageRate;
        picture.BodyHeight = bodyHeight;
        picture.Notes = outfit.Notes;
        Dump(picture, $"window-{request.Race}-{request.Gender}-{request.Wear.Count}-{frame}.png");
        return picture;
    }

    public static DollSequence RenderSequence(DollRequest request, SwfStore store, string label, int kick, bool alive)
    {
        var sequence = new DollSequence { Label = label };
        var body = Body(request, store, out string error);
        if (body == null)
        {
            sequence.Error = error;
            return sequence;
        }

        var root = body.CreateRoot();
        var labels = LabelRanges(root.Timeline);
        sequence.Labels = labels;
        sequence.FrameRate = StageRate;
        if (!labels.TryGetValue(label, out var range))
        {
            sequence.Error = $"в теле нет метки {label}";
            return sequence;
        }

        int stopFrame = labels.TryGetValue("stop", out var stop) ? stop.Start : 0;
        root.GotoAndStop(stopFrame);
        HideBars(root);
        var matrix = new SwfMatrix(request.Scale, 0, 0, request.Scale, 0, 0);
        sequence.BodyHeight = SwfBounds.Measure(body, root, matrix).Height;
        bool hasHead = FindPart(root, "bp4", matrix, out var head);
        if (hasHead) sequence.HeadX = (float)head.Tx;
        if (FindPart(root, "barInfo", matrix, out var bar)) sequence.BarY = (float)-bar.Ty;
        if (hasHead && label == "stop")
        {
            using var headRenderer = new SwfRenderer(body);
            var probe = Rasterize(body, headRenderer, root, matrix, true);
            sequence.HeadX = HeadCenter(probe, head, request.Scale, sequence.HeadX);
            Release(probe);
        }

        root.GotoAndStop(range.Start);
        HideBars(root);
        var outfit = Dress(root, request, store, SwapHands(request, label));
        sequence.Notes = outfit.Notes;
        Kick(outfit, kick);
        root.Play();
        if (label == "stop" && !string.IsNullOrEmpty(DumpDir))
        {
            using var probeRenderer = new SwfRenderer(body);
            Dump(Rasterize(body, probeRenderer, root, matrix), $"combat-{request.Race}-{request.Gender}-{request.Wear.Count}.png");
        }

        int smooth = Math.Clamp(request.Smooth, 1, 4);
        bool loops = label == "move";
        int count = range.Count;
        if (alive && count == 1)
        {
            int cycle = Cycle(root);
            if (cycle > 1)
            {
                count = cycle;
                loops = true;
                sequence.Notes.Add($"{label}: вещи живут {cycle} кадров");
            }
        }
        bool live = (alive && loops) || label == "prizuv";
        int step = live ? 1 : smooth;
        sequence.FrameRate *= smooth;
        var previous = new Dictionary<DisplayObject, Pose>();
        var current = new Dictionary<DisplayObject, Pose>();
        var first = new Dictionary<DisplayObject, Pose>();
        var restore = new List<(DisplayObject Layer, Pose Saved)>();

        using var renderer = new SwfRenderer(body);
        sequence.Packer = live ? null : Packer;
        float farthest = float.MaxValue;
        int hitIndex = -1;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                long mark = Now;
                previous.Clear();
                Snapshot(root, previous);
                if (i < range.Count)
                {
                    root.Advance();
                    HideBars(root);
                }
                else AdvanceChildren(root);
                if (!StillDressed(root, outfit))
                {
                    foreach (var (target, _, name, _) in outfit.Attached) target.RemoveAttached(name);
                    outfit = Dress(root, request, store, SwapHands(request, label));
                    Kick(outfit, kick);
                    sequence.Notes.Add($"кадр {range.Start + i}: точки крепления пересозданы, одет заново");
                }
                sequence.AdvanceTicks += Now - mark;
                if (step > 1)
                {
                    mark = Now;
                    current.Clear();
                    Snapshot(root, current);
                    sequence.BlendTicks += Now - mark;
                    for (int s = 1; s < smooth; s++)
                    {
                        mark = Now;
                        Blend(root, previous, current, (double)s / smooth, restore);
                        sequence.BlendTicks += Now - mark;
                        Push(sequence, Draw(body, renderer, root, matrix, sequence, labels));
                        mark = Now;
                        Restore(restore);
                        sequence.BlendTicks += Now - mark;
                    }
                }
            }
            else if (step > 1) Snapshot(root, first);

            if (kick != 0 && i < range.Count)
            {
                float reach = Reach(body, root, outfit, kick, matrix);
                if (reach < farthest)
                {
                    farthest = reach;
                    hitIndex = i;
                }
            }
            var drawn = Draw(body, renderer, root, matrix, sequence, labels);
            Push(sequence, drawn);
            if (live)
            {
                var shown = sequence.Frames[sequence.Frames.Count - 1];
                for (int s = 1; s < smooth; s++) sequence.Frames.Add(shown);
            }
        }
        if (hitIndex >= 0 && range.Count > 1) sequence.HitShare = (hitIndex + 0.5f) / range.Count;

        if (step > 1)
        {
            var last = sequence.Frames[sequence.Frames.Count - 1];
            if (loops && count > 1)
            {
                previous.Clear();
                Snapshot(root, previous);
                for (int s = 1; s < smooth; s++)
                {
                    Blend(root, previous, first, (double)s / smooth, restore);
                    Push(sequence, Draw(body, renderer, root, matrix, sequence, labels));
                    Restore(restore);
                }
            }
            else
            {
                for (int s = 1; s < smooth; s++) sequence.Frames.Add(last);
            }
        }
        if (live)
        {
            var bled = new HashSet<DollPicture>();
            foreach (var picture in sequence.Frames)
            {
                if (picture?.Rgba == null || !bled.Add(picture)) continue;
                Dxt.Bleed(picture.Rgba, picture.Width, picture.Height);
                picture.Raw = true;
            }
        }
        return sequence;
    }

    public static DollPicture RenderStage(SwfMovie movie, float mirror, Func<int, int, float> pick = null)
    {
        var root = movie.CreateRoot();
        root.GotoAndStop(0);
        var bounds = SwfBounds.Measure(movie, root, SwfMatrix.Identity);
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1) return new DollPicture { Error = "в ролике нечего рисовать" };

        int left = (int)Math.Round(bounds.Left);
        int top = (int)Math.Round(bounds.Top);
        int width = Math.Max(1, (int)Math.Round(bounds.Right) - left);
        int body = Math.Max(1, (int)Math.Round(bounds.Bottom) - top);
        int extra = Math.Min(body, Math.Max(0, (int)Math.Round(body * mirror)));
        float scale = pick != null ? Math.Max(1f, pick(width, body + extra)) : 1f;
        int flat = width;
        width = Math.Max(1, (int)Math.Round(flat * scale));
        body = Math.Max(1, (int)Math.Round(body * scale));
        extra = Math.Min(body, (int)Math.Round(extra * scale));
        int height = body + extra;
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var picture = new DollPicture { Width = width, Height = height, FrameRate = StageRate, Scale = scale };

        using (var surface = SKSurface.Create(info))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Black);
            canvas.Translate(0, height);
            canvas.Scale(1, -1);
            canvas.Scale(scale, scale);
            canvas.Translate(-left, -top);
            using (var renderer = new SwfRenderer(movie)) renderer.Draw(canvas, root, SwfMatrix.Identity);
            canvas.Flush();
            using var pixmap = surface.PeekPixels();
            picture.Rgba = new byte[info.BytesSize];
            Marshal.Copy(pixmap.GetPixels(), picture.Rgba, 0, picture.Rgba.Length);
        }

        int stride = width * 4;
        for (int row = 0; row < extra; row++)
            Buffer.BlockCopy(picture.Rgba, (2 * extra - 1 - row) * stride, picture.Rgba, row * stride, stride);
        picture.PivotX = -left * scale / width;
        picture.PivotY = (height + top * scale) / height;
        return picture;
    }

    public static DollSequence RenderClip(SwfMovie movie, string symbol, float scale, int smooth, bool sharp = true)
    {
        var root = movie.CreateInstance(symbol);
        if (root == null) return new DollSequence { Label = symbol, FrameRate = StageRate, Error = $"в ролике нет символа {symbol}" };
        root.GotoFrame(0);
        root.Play();
        return RenderClipInstance(movie, root, symbol, scale, smooth, sharp);
    }

    public static DollSequence RenderClipInstance(SwfMovie movie, MovieClip root, string symbol, float scale, int smooth, bool sharp = true)
    {
        var sequence = new DollSequence { Label = symbol, FrameRate = StageRate };
        var matrix = new SwfMatrix(scale, 0, 0, scale, 0, 0);
        smooth = Math.Clamp(smooth, 1, 4);
        sequence.FrameRate *= smooth;
        var labels = new Dictionary<string, LabelRange> { [symbol] = new LabelRange(0, root.FrameCount) };
        sequence.Labels = labels;
        var previous = new Dictionary<DisplayObject, Pose>();
        var current = new Dictionary<DisplayObject, Pose>();
        var restore = new List<(DisplayObject Layer, Pose Saved)>();

        using var renderer = new SwfRenderer(movie);
        for (int i = 0; i < root.FrameCount; i++)
        {
            if (i > 0)
            {
                previous.Clear();
                Snapshot(root, previous);
                root.Advance();
                if (smooth > 1)
                {
                    current.Clear();
                    Snapshot(root, current);
                    for (int s = 1; s < smooth; s++)
                    {
                        Blend(root, previous, current, (double)s / smooth, restore);
                        Push(sequence, Finish(Rasterize(movie, renderer, root, matrix, false, sharp), sequence, labels));
                        Restore(restore);
                    }
                }
            }
            Push(sequence, Finish(Rasterize(movie, renderer, root, matrix, false, sharp), sequence, labels));
        }
        if (smooth > 1)
        {
            var last = sequence.Frames[sequence.Frames.Count - 1];
            for (int s = 1; s < smooth; s++) sequence.Frames.Add(last);
        }
        if (sequence.Frames.Count > 0) sequence.BodyHeight = sequence.Frames[0].Height;
        return sequence;
    }

    private static DollPicture Finish(DollPicture picture, DollSequence sequence, Dictionary<string, LabelRange> labels)
    {
        picture.BodyHeight = sequence.BodyHeight;
        picture.FrameRate = sequence.FrameRate;
        picture.Labels = labels;
        return picture;
    }
}
