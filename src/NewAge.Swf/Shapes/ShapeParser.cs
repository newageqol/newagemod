namespace NewAge.Swf.Shapes;

public static class ShapeParser
{
    private const double Twip = 20.0;

    public static SwfShape Parse(SwfFile file, in SwfTag tag)
    {
        var reader = file.ReaderAt(tag);
        int characterId = reader.ReadUI16();
        var bounds = reader.ReadRect();

        int shapeVersion = tag.Code switch
        {
            SwfTagCode.DefineShape => 1,
            SwfTagCode.DefineShape2 => 2,
            SwfTagCode.DefineShape3 => 3,
            SwfTagCode.DefineShape4 => 4,
            _ => throw new ArgumentException($"{tag.Code} — не фигура", nameof(tag)),
        };

        if (shapeVersion == 4)
        {
            reader.ReadRect();
            reader.ReadUI8();
        }

        var shape = new SwfShape { CharacterId = characterId, Bounds = bounds };
        var builder = new ShapeBuilder(shape, shapeVersion);
        builder.ReadShapeWithStyle(reader);
        builder.Flush();
        return shape;
    }

    internal static SwfShape ParseRecords(
        SwfReader reader, SwfFillStyle[] fills, SwfLineStyle[] lines,
        int shapeVersion, int characterId, SwfRect bounds)
    {
        var shape = new SwfShape { CharacterId = characterId, Bounds = bounds };
        var builder = new ShapeBuilder(shape, shapeVersion);
        builder.SetStyles(fills, lines);
        builder.ReadRecords(reader);
        builder.Flush();
        return shape;
    }

    internal sealed class ShapeBuilder(SwfShape shape, int shapeVersion)
    {
        private SwfFillStyle[] _fills = [];
        private SwfLineStyle[] _lines = [];

        private readonly Dictionary<int, List<Segment>> _fillSegments = new();
        private readonly Dictionary<int, List<Segment>> _lineSegments = new();

        private int _feedX;
        private int _feedY;
        private int _feedFill0;
        private int _feedFill1;
        private int _feedLine;
        private Segment _feedSegment;

        public void SetStyles(SwfFillStyle[] fills, SwfLineStyle[] lines)
        {
            _fills = fills ?? [];
            _lines = lines ?? [];
        }

        public void FeedStyle(bool moveTo, int x, int y, int? fill0, int? fill1, int? line)
        {
            Commit(_feedSegment, _feedFill0, _feedFill1, _feedLine);
            _feedSegment = null;
            if (moveTo)
            {
                _feedX = x;
                _feedY = y;
            }
            if (fill0 is int f0) _feedFill0 = f0;
            if (fill1 is int f1) _feedFill1 = f1;
            if (line is int l) _feedLine = l;
        }

        public void FeedEdge(bool curve, int controlX, int controlY, int x, int y)
        {
            _feedSegment ??= new Segment(_feedX, _feedY);
            _feedSegment.Add(new Edge(curve, controlX, controlY, x, y));
            _feedX = x;
            _feedY = y;
        }

        public void FeedEnd()
        {
            Commit(_feedSegment, _feedFill0, _feedFill1, _feedLine);
            _feedSegment = null;
        }

        public void ReadShapeWithStyle(SwfReader reader)
        {
            _fills = ReadFillStyles(reader);
            _lines = ReadLineStyles(reader);
            ReadRecords(reader);
        }

        public void ReadRecords(SwfReader reader)
        {
            reader.Align();
            int fillBits = (int)reader.ReadUB(4);
            int lineBits = (int)reader.ReadUB(4);

            int x = 0, y = 0;
            int fill0 = 0, fill1 = 0, line = 0;
            Segment current = null;

            while (true)
            {
                if (reader.ReadFlag())
                {
                    current ??= new Segment(x, y);
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
                        x += dx; y += dy;
                        current.Add(new Edge(false, 0, 0, x, y));
                    }
                    else
                    {
                        int bits = (int)reader.ReadUB(4) + 2;
                        int cx = x + reader.ReadSB(bits);
                        int cy = y + reader.ReadSB(bits);
                        x = cx + reader.ReadSB(bits);
                        y = cy + reader.ReadSB(bits);
                        current.Add(new Edge(true, cx, cy, x, y));
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

                Commit(current, fill0, fill1, line);
                current = null;

                if (moveTo)
                {
                    int bits = (int)reader.ReadUB(5);
                    x = reader.ReadSB(bits);
                    y = reader.ReadSB(bits);
                }
                if (changeFill0) fill0 = (int)reader.ReadUB(fillBits);
                if (changeFill1) fill1 = (int)reader.ReadUB(fillBits);
                if (changeLine) line = (int)reader.ReadUB(lineBits);

                if (newStyles)
                {
                    Flush();
                    _fills = ReadFillStyles(reader);
                    _lines = ReadLineStyles(reader);
                    reader.Align();
                    fillBits = (int)reader.ReadUB(4);
                    lineBits = (int)reader.ReadUB(4);
                    fill0 = fill1 = line = 0;
                }
            }

            Commit(current, fill0, fill1, line);
        }

        private void Commit(Segment segment, int fill0, int fill1, int line)
        {
            if (segment is null || segment.IsEmpty) return;

            if (fill1 > 0 && fill1 != fill0) Merge(_fillSegments, fill1, segment.Clone());
            if (fill0 > 0 && fill0 != fill1)
            {
                var flipped = segment.Clone();
                flipped.Flip();
                Merge(_fillSegments, fill0, flipped);
            }
            if (line > 0) Merge(_lineSegments, line, segment.Clone(), allowFlip: true);
        }

        private static void Merge(Dictionary<int, List<Segment>> store, int styleIndex, Segment segment, bool allowFlip = false)
        {
            if (!store.TryGetValue(styleIndex, out var list))
                store[styleIndex] = list = new List<Segment>();

            while (!segment.IsClosed)
            {
                int found = -1;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var s = list[i];
                    if (s.IsClosed) continue;
                    if (s.EndX == segment.StartX && s.EndY == segment.StartY) { s.Append(segment); segment = s; found = i; break; }
                    if (s.StartX == segment.EndX && s.StartY == segment.EndY) { segment.Append(s); found = i; break; }
                    if (!allowFlip) continue;
                    if (s.EndX == segment.EndX && s.EndY == segment.EndY) { segment.Flip(); s.Append(segment); segment = s; found = i; break; }
                    if (s.StartX == segment.StartX && s.StartY == segment.StartY) { s.Flip(); s.Append(segment); segment = s; found = i; break; }
                }
                if (found < 0) break;
                list.RemoveAt(found);
            }
            list.Add(segment);
        }

        public void Flush()
        {
            foreach (int index in _fillSegments.Keys.OrderBy(i => i))
            {
                if (index > _fills.Length) continue;
                var path = new SwfPath { Fill = _fills[index - 1] };
                foreach (var segment in _fillSegments[index]) segment.Emit(path.Commands);
                if (path.Commands.Count > 0) shape.Paths.Add(path);
            }
            foreach (int index in _lineSegments.Keys.OrderBy(i => i))
            {
                if (index > _lines.Length) continue;
                var path = new SwfPath { Stroke = _lines[index - 1] };
                foreach (var segment in _lineSegments[index]) segment.Emit(path.Commands);
                if (path.Commands.Count > 0) shape.Paths.Add(path);
            }
            _fillSegments.Clear();
            _lineSegments.Clear();
        }

        private SwfFillStyle[] ReadFillStyles(SwfReader reader)
        {
            reader.Align();
            int count = reader.ReadUI8();
            if (count == 0xFF && shapeVersion >= 2) count = reader.ReadUI16();

            var styles = new SwfFillStyle[count];
            for (int i = 0; i < count; i++) styles[i] = ReadFillStyle(reader);
            return styles;
        }

        private SwfFillStyle ReadFillStyle(SwfReader reader)
        {
            int type = reader.ReadUI8();
            switch (type)
            {
                case 0x00:
                    return new SwfSolidFill(shapeVersion >= 3 ? reader.ReadRgba() : reader.ReadRgb());

                case 0x10:
                case 0x12:
                case 0x13:
                {
                    var matrix = reader.ReadMatrix();
                    reader.Align();
                    var spread = (SwfSpreadMode)reader.ReadUB(2);
                    var interpolation = (SwfInterpolationMode)reader.ReadUB(2);
                    int stopCount = (int)reader.ReadUB(4);

                    var stops = new SwfGradientStop[stopCount];
                    for (int i = 0; i < stopCount; i++)
                    {
                        double ratio = reader.ReadUI8() / 255.0;
                        var color = shapeVersion >= 3 ? reader.ReadRgba() : reader.ReadRgb();
                        stops[i] = new SwfGradientStop(ratio, color);
                    }

                    double focal = type == 0x13 ? reader.ReadFixed8() : 0;
                    return new SwfGradientFill
                    {
                        Radial = type != 0x10,
                        Matrix = matrix,
                        Stops = stops,
                        Spread = spread,
                        Interpolation = interpolation,
                        FocalPoint = focal,
                    };
                }

                case 0x40:
                case 0x41:
                case 0x42:
                case 0x43:
                {
                    int bitmapId = reader.ReadUI16();
                    var m = reader.ReadMatrix();
                    var matrix = new SwfMatrix(m.A / Twip, m.B / Twip, m.C / Twip, m.D / Twip, m.Tx, m.Ty);
                    return new SwfBitmapFill
                    {
                        BitmapId = bitmapId,
                        Matrix = matrix,
                        Repeat = type is 0x40 or 0x42,
                        Smoothed = type is 0x40 or 0x41,
                    };
                }

                default:
                    throw new InvalidDataException($"неизвестный тип заливки 0x{type:X2}");
            }
        }

        private SwfLineStyle[] ReadLineStyles(SwfReader reader)
        {
            reader.Align();
            int count = reader.ReadUI8();
            if (count == 0xFF && shapeVersion >= 2) count = reader.ReadUI16();

            var styles = new SwfLineStyle[count];
            for (int i = 0; i < count; i++)
                styles[i] = shapeVersion >= 4 ? ReadLineStyle2(reader) : ReadLineStyle1(reader);
            return styles;
        }

        private SwfLineStyle ReadLineStyle1(SwfReader reader) => new()
        {
            Width = reader.ReadUI16() / Twip,
            Color = shapeVersion >= 3 ? reader.ReadRgba() : reader.ReadRgb(),
        };

        private SwfLineStyle ReadLineStyle2(SwfReader reader)
        {
            double width = reader.ReadUI16() / Twip;
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
            SwfFillStyle fill = null;
            var color = default(SwfColor);
            if (hasFill) fill = ReadFillStyle(reader);
            else color = reader.ReadRgba();

            return new SwfLineStyle
            {
                Width = width,
                Color = color,
                Fill = fill,
                StartCap = startCap,
                EndCap = endCap,
                Join = join,
                MiterLimit = miter,
                NoHScale = noHScale,
                NoVScale = noVScale,
                PixelHinting = pixelHinting,
            };
        }

        private readonly record struct Edge(bool Curve, int Cx, int Cy, int X, int Y);

        private sealed class Segment(int startX, int startY)
        {
            private List<Edge> _edges = new();

            public int StartX { get; private set; } = startX;
            public int StartY { get; private set; } = startY;
            public int EndX => _edges.Count > 0 ? _edges[^1].X : StartX;
            public int EndY => _edges.Count > 0 ? _edges[^1].Y : StartY;
            public bool IsEmpty => _edges.Count == 0;
            public bool IsClosed => _edges.Count > 0 && EndX == StartX && EndY == StartY;

            public void Add(in Edge edge) => _edges.Add(edge);

            public Segment Clone()
            {
                var copy = new Segment(StartX, StartY);
                copy._edges = new List<Edge>(_edges);
                return copy;
            }

            public void Append(Segment other)
            {
                _edges.AddRange(other._edges);
            }

            public void Flip()
            {
                var points = new List<(int X, int Y)>(_edges.Count + 1) { (StartX, StartY) };
                foreach (var e in _edges) points.Add((e.X, e.Y));

                var reversed = new List<Edge>(_edges.Count);
                for (int i = _edges.Count - 1; i >= 0; i--)
                {
                    var e = _edges[i];
                    reversed.Add(new Edge(e.Curve, e.Cx, e.Cy, points[i].X, points[i].Y));
                }

                StartX = points[^1].X;
                StartY = points[^1].Y;
                _edges = reversed;
            }

            public void Emit(List<SwfPathCommand> commands)
            {
                commands.Add(SwfPathCommand.MoveTo(StartX / Twip, StartY / Twip));
                foreach (var e in _edges)
                {
                    commands.Add(e.Curve
                        ? SwfPathCommand.QuadTo(e.Cx / Twip, e.Cy / Twip, e.X / Twip, e.Y / Twip)
                        : SwfPathCommand.LineTo(e.X / Twip, e.Y / Twip));
                }
            }
        }
    }
}
