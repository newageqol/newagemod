namespace NewAge.Swf.Shapes;

public sealed class SwfShape
{
    public int CharacterId { get; init; }
    public SwfRect Bounds { get; init; }
    public List<SwfPath> Paths { get; } = new();
}

public sealed class SwfPath
{
    public SwfFillStyle Fill { get; init; }
    public SwfLineStyle Stroke { get; init; }
    public List<SwfPathCommand> Commands { get; } = new();
}

public enum SwfPathVerb
{
    MoveTo,
    LineTo,
    QuadTo,
}

public readonly struct SwfPathCommand(SwfPathVerb verb, double controlX, double controlY, double x, double y)
{
    public SwfPathVerb Verb { get; } = verb;
    public double ControlX { get; } = controlX;
    public double ControlY { get; } = controlY;
    public double X { get; } = x;
    public double Y { get; } = y;

    public static SwfPathCommand MoveTo(double x, double y) => new(SwfPathVerb.MoveTo, 0, 0, x, y);
    public static SwfPathCommand LineTo(double x, double y) => new(SwfPathVerb.LineTo, 0, 0, x, y);
    public static SwfPathCommand QuadTo(double cx, double cy, double x, double y) => new(SwfPathVerb.QuadTo, cx, cy, x, y);

    public override string ToString() => Verb switch
    {
        SwfPathVerb.MoveTo => $"M {X:0.##} {Y:0.##}",
        SwfPathVerb.LineTo => $"L {X:0.##} {Y:0.##}",
        _ => $"Q {ControlX:0.##} {ControlY:0.##} {X:0.##} {Y:0.##}",
    };
}

public abstract class SwfFillStyle;

public sealed class SwfSolidFill(SwfColor color) : SwfFillStyle
{
    public SwfColor Color { get; } = color;
    public override string ToString() => $"solid {Color}";
}

public enum SwfSpreadMode { Pad, Reflect, Repeat }
public enum SwfInterpolationMode { Rgb, LinearRgb }

public readonly struct SwfGradientStop(double ratio, SwfColor color)
{
    public double Ratio { get; } = ratio;
    public SwfColor Color { get; } = color;
}

public sealed class SwfGradientFill : SwfFillStyle
{
    public const double GradientExtent = 819.2;

    public bool Radial { get; init; }
    public SwfMatrix Matrix { get; init; }
    public SwfGradientStop[] Stops { get; init; }
    public SwfSpreadMode Spread { get; init; }
    public SwfInterpolationMode Interpolation { get; init; }
    public double FocalPoint { get; init; }

    public override string ToString() => $"{(Radial ? "radial" : "linear")} gradient, {Stops.Length} стопов";
}

public sealed class SwfBitmapFill : SwfFillStyle
{
    public int BitmapId { get; init; }
    public SwfMatrix Matrix { get; init; }
    public bool Repeat { get; init; }
    public bool Smoothed { get; init; }

    public override string ToString() => $"bitmap #{BitmapId}{(Repeat ? " repeat" : "")}";
}

public enum SwfCapStyle { Round, None, Square }
public enum SwfJoinStyle { Round, Bevel, Miter }

public sealed class SwfLineStyle
{
    public double Width { get; init; }
    public SwfColor Color { get; init; }
    public SwfFillStyle Fill { get; init; }

    public SwfCapStyle StartCap { get; init; } = SwfCapStyle.Round;
    public SwfCapStyle EndCap { get; init; } = SwfCapStyle.Round;
    public SwfJoinStyle Join { get; init; } = SwfJoinStyle.Round;
    public double MiterLimit { get; init; } = 3;

    public bool NoHScale { get; init; }
    public bool NoVScale { get; init; }
    public bool PixelHinting { get; init; }

    public override string ToString() => $"line {Width:0.##} {Color}";
}
