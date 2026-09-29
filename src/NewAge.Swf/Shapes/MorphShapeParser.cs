namespace NewAge.Swf.Shapes;

public sealed class SwfMorphShape
{
    public int CharacterId { get; init; }
    public SwfShape Start { get; init; }
    public SwfShape End { get; init; }

    public bool CanInterpolate { get; init; }

    internal byte[] Body { get; init; }
    internal int StartEdges { get; init; }
    internal int EndEdges { get; init; }
    internal SwfFillStyle[] StartFills { get; init; }
    internal SwfFillStyle[] EndFills { get; init; }
    internal SwfLineStyle[] StartLines { get; init; }
    internal SwfLineStyle[] EndLines { get; init; }

    private struct Record
    {
        public bool IsEdge;
        public bool Curve;
        public bool MoveTo;
        public int ControlX;
        public int ControlY;
        public int X;
        public int Y;
        public int? Fill0;
        public int? Fill1;
        public int? Line;
    }

    public SwfShape Sample(double ratio)
    {
        if (ratio <= 0 || !CanInterpolate) return Start;
        if (Body == null) return ratio >= 1 ? End : Start;
        ratio = Math.Min(ratio, 1);

        var starts = ReadRecords(Body, StartEdges);
        var ends = ReadRecords(Body, EndEdges);
        var result = new SwfShape { CharacterId = CharacterId, Bounds = Start.Bounds };

        var fills = new SwfFillStyle[StartFills.Length];
        for (int k = 0; k < fills.Length; k++) fills[k] = LerpFill(StartFills[k], EndFills[k], ratio);
        var lines = new SwfLineStyle[StartLines.Length];
        for (int k = 0; k < lines.Length; k++) lines[k] = LerpStroke(StartLines[k], EndLines[k], ratio);

        var builder = new ShapeParser.ShapeBuilder(result, 3);
        builder.SetStyles(fills, lines);
        int Mix(int a, int b) => (int)Math.Round(a + (b - a) * ratio);

        int i = 0, j = 0, sx = 0, sy = 0, ex = 0, ey = 0;
        while (i < starts.Count)
        {
            var a = starts[i];
            if (!a.IsEdge)
            {
                if (a.MoveTo)
                {
                    sx = a.X;
                    sy = a.Y;
                    while (j < ends.Count && !ends[j].IsEdge)
                    {
                        bool moved = ends[j].MoveTo;
                        if (moved)
                        {
                            ex = ends[j].X;
                            ey = ends[j].Y;
                        }
                        j++;
                        if (moved) break;
                    }
                }
                builder.FeedStyle(a.MoveTo, Mix(sx, ex), Mix(sy, ey), a.Fill0, a.Fill1, a.Line);
                i++;
                continue;
            }

            while (j < ends.Count && !ends[j].IsEdge)
            {
                if (ends[j].MoveTo)
                {
                    ex = ends[j].X;
                    ey = ends[j].Y;
                    builder.FeedStyle(true, Mix(sx, ex), Mix(sy, ey), null, null, null);
                }
                j++;
            }
            if (j >= ends.Count) break;

            var b = ends[j];
            int acx = a.Curve ? a.ControlX : (sx + a.X) / 2;
            int acy = a.Curve ? a.ControlY : (sy + a.Y) / 2;
            int bcx = b.Curve ? b.ControlX : (ex + b.X) / 2;
            int bcy = b.Curve ? b.ControlY : (ey + b.Y) / 2;
            builder.FeedEdge(a.Curve || b.Curve, Mix(acx, bcx), Mix(acy, bcy), Mix(a.X, b.X), Mix(a.Y, b.Y));
            sx = a.X;
            sy = a.Y;
            ex = b.X;
            ey = b.Y;
            i++;
            j++;
        }

        builder.FeedEnd();
        builder.Flush();
        return result;
    }

    private static List<Record> ReadRecords(byte[] body, int position)
    {
        var reader = new SwfReader(body, position);
        reader.Align();
        int fillBits = (int)reader.ReadUB(4);
        int lineBits = (int)reader.ReadUB(4);

        var records = new List<Record>();
        int x = 0, y = 0;
        while (true)
        {
            if (reader.ReadFlag())
            {
                if (reader.ReadFlag())
                {
                    int bits = (int)reader.ReadUB(4) + 2;
                    int dx = 0, dy = 0;
                    if (reader.ReadFlag())
                    {
                        dx = reader.ReadSB(bits);
                        dy = reader.ReadSB(bits);
                    }
                    else if (reader.ReadFlag())
                    {
                        dy = reader.ReadSB(bits);
                    }
                    else
                    {
                        dx = reader.ReadSB(bits);
                    }
                    x += dx;
                    y += dy;
                    records.Add(new Record { IsEdge = true, X = x, Y = y });
                }
                else
                {
                    int bits = (int)reader.ReadUB(4) + 2;
                    int cx = x + reader.ReadSB(bits);
                    int cy = y + reader.ReadSB(bits);
                    x = cx + reader.ReadSB(bits);
                    y = cy + reader.ReadSB(bits);
                    records.Add(new Record { IsEdge = true, Curve = true, ControlX = cx, ControlY = cy, X = x, Y = y });
                }
                continue;
            }

            bool newStyles = reader.ReadFlag();
            bool changeLine = reader.ReadFlag();
            bool changeFill1 = reader.ReadFlag();
            bool changeFill0 = reader.ReadFlag();
            bool moveTo = reader.ReadFlag();

            if (!newStyles && !changeLine && !changeFill1 && !changeFill0 && !moveTo)
                break;

            var record = new Record();
            if (moveTo)
            {
                int bits = (int)reader.ReadUB(5);
                x = reader.ReadSB(bits);
                y = reader.ReadSB(bits);
                record.MoveTo = true;
                record.X = x;
                record.Y = y;
            }
            if (changeFill0) record.Fill0 = (int)reader.ReadUB(fillBits);
            if (changeFill1) record.Fill1 = (int)reader.ReadUB(fillBits);
            if (changeLine) record.Line = (int)reader.ReadUB(lineBits);
            if (newStyles) break;
            records.Add(record);
        }
        return records;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static SwfColor Lerp(in SwfColor a, in SwfColor b, double t) => new(
        (byte)Math.Round(Lerp(a.R, b.R, t)),
        (byte)Math.Round(Lerp(a.G, b.G, t)),
        (byte)Math.Round(Lerp(a.B, b.B, t)),
        (byte)Math.Round(Lerp(a.A, b.A, t)));

    private static SwfMatrix Lerp(in SwfMatrix a, in SwfMatrix b, double t) => new(
        Lerp(a.A, b.A, t), Lerp(a.B, b.B, t), Lerp(a.C, b.C, t),
        Lerp(a.D, b.D, t), Lerp(a.Tx, b.Tx, t), Lerp(a.Ty, b.Ty, t));

    private static SwfFillStyle LerpFill(SwfFillStyle a, SwfFillStyle b, double t)
    {
        if (a is null || b is null) return a ?? b;
        switch (a)
        {
            case SwfSolidFill sa when b is SwfSolidFill sb:
                return new SwfSolidFill(Lerp(sa.Color, sb.Color, t));

            case SwfGradientFill ga when b is SwfGradientFill gb && ga.Stops.Length == gb.Stops.Length:
            {
                var stops = new SwfGradientStop[ga.Stops.Length];
                for (int i = 0; i < stops.Length; i++)
                    stops[i] = new SwfGradientStop(
                        Lerp(ga.Stops[i].Ratio, gb.Stops[i].Ratio, t),
                        Lerp(ga.Stops[i].Color, gb.Stops[i].Color, t));

                return new SwfGradientFill
                {
                    Radial = ga.Radial,
                    Matrix = Lerp(ga.Matrix, gb.Matrix, t),
                    Stops = stops,
                    Spread = ga.Spread,
                    Interpolation = ga.Interpolation,
                    FocalPoint = Lerp(ga.FocalPoint, gb.FocalPoint, t),
                };
            }

            case SwfBitmapFill ba when b is SwfBitmapFill bb:
                return new SwfBitmapFill
                {
                    BitmapId = ba.BitmapId,
                    Matrix = Lerp(ba.Matrix, bb.Matrix, t),
                    Repeat = ba.Repeat,
                    Smoothed = ba.Smoothed,
                };

            default:
                return a;
        }
    }

    private static SwfLineStyle LerpStroke(SwfLineStyle a, SwfLineStyle b, double t)
    {
        if (a is null || b is null) return a ?? b;
        return new SwfLineStyle
        {
            Width = Lerp(a.Width, b.Width, t),
            Color = Lerp(a.Color, b.Color, t),
            Fill = LerpFill(a.Fill, b.Fill, t),
            StartCap = a.StartCap,
            EndCap = a.EndCap,
            Join = a.Join,
            MiterLimit = a.MiterLimit,
            NoHScale = a.NoHScale,
            NoVScale = a.NoVScale,
            PixelHinting = a.PixelHinting,
        };
    }
}

public static class MorphShapeParser
{
    private const double Twip = 20.0;

    public static SwfMorphShape Parse(SwfFile file, in SwfTag tag)
    {
        bool version2 = tag.Code == SwfTagCode.DefineMorphShape2;
        var reader = file.ReaderAt(tag);

        int characterId = reader.ReadUI16();
        var startBounds = reader.ReadRect();
        reader.ReadRect();

        if (version2)
        {
            reader.ReadRect();
            reader.ReadRect();
            reader.ReadUI8();
        }

        uint offset = reader.ReadUI32();
        int endEdges = reader.Position + (int)offset;

        var (startFills, endFills) = ReadFillStyles(reader, version2);
        var (startLines, endLines) = ReadLineStyles(reader, version2);

        int startEdges = reader.Position;
        var start = ShapeParser.ParseRecords(reader, startFills, startLines, 3, characterId, startBounds);

        return new SwfMorphShape
        {
            CharacterId = characterId,
            Start = start,
            End = start,
            CanInterpolate = true,
            Body = reader.Data,
            StartEdges = startEdges,
            EndEdges = endEdges,
            StartFills = startFills,
            EndFills = endFills,
            StartLines = startLines,
            EndLines = endLines,
        };
    }

    private static (SwfFillStyle[] Start, SwfFillStyle[] End) ReadFillStyles(SwfReader reader, bool version2)
    {
        reader.Align();
        int count = reader.ReadUI8();
        if (count == 0xFF) count = reader.ReadUI16();

        var starts = new SwfFillStyle[count];
        var ends = new SwfFillStyle[count];
        for (int i = 0; i < count; i++) (starts[i], ends[i]) = ReadFillStyle(reader, version2);
        return (starts, ends);
    }

    private static (SwfFillStyle, SwfFillStyle) ReadFillStyle(SwfReader reader, bool version2)
    {
        int type = reader.ReadUI8();
        switch (type)
        {
            case 0x00:
            {
                var a = reader.ReadRgba();
                var b = reader.ReadRgba();
                return (new SwfSolidFill(a), new SwfSolidFill(b));
            }

            case 0x10:
            case 0x12:
            case 0x13:
            {
                var startMatrix = reader.ReadMatrix();
                var endMatrix = reader.ReadMatrix();

                reader.Align();
                var spread = (SwfSpreadMode)reader.ReadUB(2);
                var interpolation = (SwfInterpolationMode)reader.ReadUB(2);
                int stopCount = (int)reader.ReadUB(4);
                var startStops = new SwfGradientStop[stopCount];
                var endStops = new SwfGradientStop[stopCount];
                for (int i = 0; i < stopCount; i++)
                {
                    double ra = reader.ReadUI8() / 255.0;
                    var ca = reader.ReadRgba();
                    double rb = reader.ReadUI8() / 255.0;
                    var cb = reader.ReadRgba();
                    startStops[i] = new SwfGradientStop(ra, ca);
                    endStops[i] = new SwfGradientStop(rb, cb);
                }

                double startFocal = 0, endFocal = 0;
                if (type == 0x13 && version2)
                {
                    startFocal = reader.ReadFixed8();
                    endFocal = reader.ReadFixed8();
                }

                bool radial = type != 0x10;
                return (
                    new SwfGradientFill { Radial = radial, Matrix = startMatrix, Stops = startStops, Spread = spread, Interpolation = interpolation, FocalPoint = startFocal },
                    new SwfGradientFill { Radial = radial, Matrix = endMatrix, Stops = endStops, Spread = spread, Interpolation = interpolation, FocalPoint = endFocal });
            }

            case 0x40:
            case 0x41:
            case 0x42:
            case 0x43:
            {
                int bitmapId = reader.ReadUI16();
                var ms = reader.ReadMatrix();
                var me = reader.ReadMatrix();
                bool repeat = type is 0x40 or 0x42;
                bool smooth = type is 0x40 or 0x41;

                static SwfMatrix ToBitmapSpace(in SwfMatrix m) =>
                    new(m.A / Twip, m.B / Twip, m.C / Twip, m.D / Twip, m.Tx, m.Ty);

                return (
                    new SwfBitmapFill { BitmapId = bitmapId, Matrix = ToBitmapSpace(ms), Repeat = repeat, Smoothed = smooth },
                    new SwfBitmapFill { BitmapId = bitmapId, Matrix = ToBitmapSpace(me), Repeat = repeat, Smoothed = smooth });
            }

            default:
                throw new InvalidDataException($"неизвестный тип морф-заливки 0x{type:X2}");
        }
    }

    private static (SwfLineStyle[] Start, SwfLineStyle[] End) ReadLineStyles(SwfReader reader, bool version2)
    {
        reader.Align();
        int count = reader.ReadUI8();
        if (count == 0xFF) count = reader.ReadUI16();

        var starts = new SwfLineStyle[count];
        var ends = new SwfLineStyle[count];

        for (int i = 0; i < count; i++)
        {
            double startWidth = reader.ReadUI16() / Twip;
            double endWidth = reader.ReadUI16() / Twip;

            if (!version2)
            {
                starts[i] = new SwfLineStyle { Width = startWidth, Color = reader.ReadRgba() };
                ends[i] = new SwfLineStyle { Width = endWidth, Color = reader.ReadRgba() };
                continue;
            }

            reader.Align();
            var startCap = (SwfCapStyle)reader.ReadUB(2);
            var join = (SwfJoinStyle)reader.ReadUB(2);
            bool hasFill = reader.ReadFlag();
            bool noHScale = reader.ReadFlag();
            bool noVScale = reader.ReadFlag();
            bool pixelHinting = reader.ReadFlag();
            reader.ReadUB(5);
            reader.ReadFlag();
            var endCap = (SwfCapStyle)reader.ReadUB(2);
            double miter = join == SwfJoinStyle.Miter ? reader.ReadFixed8() : 3;

            SwfFillStyle fillStart = null, fillEnd = null;
            SwfColor colorStart = default, colorEnd = default;
            if (hasFill) (fillStart, fillEnd) = ReadFillStyle(reader, version2);
            else { colorStart = reader.ReadRgba(); colorEnd = reader.ReadRgba(); }

            SwfLineStyle Make(double width, SwfColor color, SwfFillStyle fill) => new()
            {
                Width = width, Color = color, Fill = fill,
                StartCap = startCap, EndCap = endCap, Join = join, MiterLimit = miter,
                NoHScale = noHScale, NoVScale = noVScale, PixelHinting = pixelHinting,
            };

            starts[i] = Make(startWidth, colorStart, fillStart);
            ends[i] = Make(endWidth, colorEnd, fillEnd);
        }

        return (starts, ends);
    }
}
