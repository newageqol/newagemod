namespace NewAge.Swf;

public readonly struct SwfRect(int xMin, int xMax, int yMin, int yMax)
{
    public int XMinTwips { get; } = xMin;
    public int XMaxTwips { get; } = xMax;
    public int YMinTwips { get; } = yMin;
    public int YMaxTwips { get; } = yMax;

    public double X => XMinTwips / 20.0;
    public double Y => YMinTwips / 20.0;
    public double Width => (XMaxTwips - XMinTwips) / 20.0;
    public double Height => (YMaxTwips - YMinTwips) / 20.0;

    public override string ToString() => $"[{X:0.##},{Y:0.##} {Width:0.##}x{Height:0.##}]";
}

public readonly struct SwfMatrix(double a, double b, double c, double d, double tx, double ty)
{
    public double A { get; } = a;
    public double B { get; } = b;
    public double C { get; } = c;
    public double D { get; } = d;
    public double Tx { get; } = tx;
    public double Ty { get; } = ty;

    public static readonly SwfMatrix Identity = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && Tx == 0 && Ty == 0;

    public SwfMatrix Concat(in SwfMatrix parent) => new(
        A * parent.A + B * parent.C,
        A * parent.B + B * parent.D,
        C * parent.A + D * parent.C,
        C * parent.B + D * parent.D,
        Tx * parent.A + Ty * parent.C + parent.Tx,
        Tx * parent.B + Ty * parent.D + parent.Ty);

    public override string ToString() => IsIdentity
        ? "identity"
        : $"({A:0.###},{B:0.###},{C:0.###},{D:0.###},{Tx:0.##},{Ty:0.##})";
}

public readonly struct SwfColor(byte r, byte g, byte b, byte a)
{
    public byte R { get; } = r;
    public byte G { get; } = g;
    public byte B { get; } = b;
    public byte A { get; } = a;

    public uint Argb => (uint)((A << 24) | (R << 16) | (G << 8) | B);

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

public readonly struct SwfColorTransform(
    double rMul, double gMul, double bMul, double aMul,
    double rAdd, double gAdd, double bAdd, double aAdd)
{
    public double RMul { get; } = rMul;
    public double GMul { get; } = gMul;
    public double BMul { get; } = bMul;
    public double AMul { get; } = aMul;
    public double RAdd { get; } = rAdd;
    public double GAdd { get; } = gAdd;
    public double BAdd { get; } = bAdd;
    public double AAdd { get; } = aAdd;

    public static readonly SwfColorTransform Identity = new(1, 1, 1, 1, 0, 0, 0, 0);

    public bool IsIdentity =>
        RMul == 1 && GMul == 1 && BMul == 1 && AMul == 1 &&
        RAdd == 0 && GAdd == 0 && BAdd == 0 && AAdd == 0;

    public SwfColorTransform Concat(in SwfColorTransform parent) => new(
        RMul * parent.RMul, GMul * parent.GMul, BMul * parent.BMul, AMul * parent.AMul,
        RAdd * parent.RMul + parent.RAdd,
        GAdd * parent.GMul + parent.GAdd,
        BAdd * parent.BMul + parent.BAdd,
        AAdd * parent.AMul + parent.AAdd);

    public SwfColor Apply(in SwfColor c)
    {
        static byte Clamp(double v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
        return new SwfColor(
            Clamp(c.R * RMul + RAdd),
            Clamp(c.G * GMul + GAdd),
            Clamp(c.B * BMul + BAdd),
            Clamp(c.A * AMul + AAdd));
    }

    public override string ToString() => IsIdentity ? "identity" : $"mul({RMul:0.##},{GMul:0.##},{BMul:0.##},{AMul:0.##})";
}
