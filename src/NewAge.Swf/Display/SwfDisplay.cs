namespace NewAge.Swf.Display;

public enum SwfBlendMode
{
    Normal = 0,
    Normal1 = 1,
    Layer = 2,
    Multiply = 3,
    Screen = 4,
    Lighten = 5,
    Darken = 6,
    Difference = 7,
    Add = 8,
    Subtract = 9,
    Invert = 10,
    Alpha = 11,
    Erase = 12,
    Overlay = 13,
    HardLight = 14,
}

public enum SwfFilterKind { DropShadow, Blur, Glow, Bevel, GradientGlow, Convolution, ColorMatrix, GradientBevel }

public sealed class SwfFilter
{
    public SwfFilterKind Kind { get; init; }
    public SwfColor Color { get; init; }
    public double BlurX { get; init; }
    public double BlurY { get; init; }
    public double Angle { get; init; }
    public double Distance { get; init; }
    public double Strength { get; init; }
    public int Passes { get; init; }
    public bool Inner { get; init; }
    public bool Knockout { get; init; }
    public bool CompositeSource { get; init; }
    public float[] Matrix { get; init; }

    public override string ToString() => $"{Kind}";
}

public sealed class SwfPlaceCommand
{
    public int Depth { get; init; }
    public bool IsMove { get; init; }
    public int? CharacterId { get; init; }
    public SwfMatrix? Matrix { get; init; }
    public SwfColorTransform? ColorTransform { get; init; }
    public double? Ratio { get; init; }
    public string Name { get; init; }
    public int? ClipDepth { get; init; }
    public SwfBlendMode? BlendMode { get; init; }
    public bool? Visible { get; init; }
    public IReadOnlyList<SwfFilter> Filters { get; init; }

    public override string ToString() =>
        $"{(IsMove ? "move" : "place")} depth={Depth}{(CharacterId is { } id ? $" char={id}" : "")}{(Name is null ? "" : $" '{Name}'")}";
}

public sealed class SwfRemoveCommand
{
    public int Depth { get; init; }
    public override string ToString() => $"remove depth={Depth}";
}

public sealed class SwfFrame
{
    public int Index { get; init; }
    public string Label { get; set; }
    public bool HasStop { get; set; }
    public List<SwfTag> Actions { get; } = new();
    public List<object> Commands { get; } = new();
}

public sealed class SwfTimeline
{
    public int CharacterId { get; init; }
    public IReadOnlyList<SwfFrame> Frames { get; init; }
    public IReadOnlyDictionary<string, int> Labels { get; init; }

    public int FrameCount => Frames.Count;

    public int FrameOf(string label) =>
        label is not null && Labels.TryGetValue(label, out int frame) ? frame : -1;
}
