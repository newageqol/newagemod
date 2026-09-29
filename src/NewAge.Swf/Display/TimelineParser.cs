namespace NewAge.Swf.Display;

public static class TimelineParser
{
    public static SwfTimeline Parse(SwfFile file, IReadOnlyList<SwfTag> tags, int characterId = 0)
    {
        var frames = new List<SwfFrame>();
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        var current = new SwfFrame { Index = 0 };

        foreach (var tag in tags)
        {
            switch (tag.Code)
            {
                case SwfTagCode.ShowFrame:
                    frames.Add(current);
                    current = new SwfFrame { Index = frames.Count };
                    break;

                case SwfTagCode.FrameLabel:
                {
                    string name = file.ReaderAt(tag).ReadString();
                    current.Label = name;
                    labels.TryAdd(name, current.Index);
                    break;
                }

                case SwfTagCode.PlaceObject:
                    current.Commands.Add(ReadPlaceObject1(file, tag));
                    break;

                case SwfTagCode.PlaceObject2:
                    current.Commands.Add(ReadPlaceObject2(file, tag, version3: false));
                    break;

                case SwfTagCode.PlaceObject3:
                    current.Commands.Add(ReadPlaceObject2(file, tag, version3: true));
                    break;

                case SwfTagCode.RemoveObject:
                {
                    var reader = file.ReaderAt(tag);
                    reader.ReadUI16();
                    current.Commands.Add(new SwfRemoveCommand { Depth = reader.ReadUI16() });
                    break;
                }

                case SwfTagCode.RemoveObject2:
                    current.Commands.Add(new SwfRemoveCommand { Depth = file.ReaderAt(tag).ReadUI16() });
                    break;

                case SwfTagCode.DoAction:
                    current.HasStop |= HasTopLevelStop(file, tag);
                    current.Actions.Add(tag);
                    break;

                case SwfTagCode.End:
                    goto done;
            }
        }

    done:
        if (current.Commands.Count > 0 || current.Label is not null) frames.Add(current);
        if (frames.Count == 0) frames.Add(current);

        return new SwfTimeline { CharacterId = characterId, Frames = frames, Labels = labels };
    }

    private static bool HasTopLevelStop(SwfFile file, in SwfTag tag)
    {
        var body = file.Body;
        int i = tag.BodyStart;
        int end = tag.BodyEnd;

        while (i < end)
        {
            byte op = body[i];
            if (op == 0) return false;
            if (op < 0x80)
            {
                if (op == 0x07) return true;
                i++;
                continue;
            }

            if (i + 3 > end) return false;
            int length = body[i + 1] | (body[i + 2] << 8);
            int next = i + 3 + length;
            if (next > end) return false;

            if (op is 0x8E or 0x9B && length >= 2)
            {
                int codeSize = body[next - 2] | (body[next - 1] << 8);
                next += codeSize;
                if (next > end) return false;
            }

            i = next;
        }
        return false;
    }

    private static SwfPlaceCommand ReadPlaceObject1(SwfFile file, in SwfTag tag)
    {
        var reader = file.ReaderAt(tag);
        int characterId = reader.ReadUI16();
        int depth = reader.ReadUI16();
        var matrix = reader.ReadMatrix();

        SwfColorTransform? cx = null;
        if (reader.Position < tag.BodyEnd) cx = reader.ReadColorTransform();

        return new SwfPlaceCommand
        {
            Depth = depth,
            IsMove = false,
            CharacterId = characterId,
            Matrix = matrix,
            ColorTransform = cx,
        };
    }

    private static SwfPlaceCommand ReadPlaceObject2(SwfFile file, in SwfTag tag, bool version3)
    {
        var reader = file.ReaderAt(tag);

        int flags = version3 ? reader.ReadUI16() : reader.ReadUI8();

        bool move = (flags & 1 << 0) != 0;
        bool hasCharacter = (flags & 1 << 1) != 0;
        bool hasMatrix = (flags & 1 << 2) != 0;
        bool hasColorTransform = (flags & 1 << 3) != 0;
        bool hasRatio = (flags & 1 << 4) != 0;
        bool hasName = (flags & 1 << 5) != 0;
        bool hasClipDepth = (flags & 1 << 6) != 0;
        bool hasClipActions = (flags & 1 << 7) != 0;

        bool hasFilters = version3 && (flags & 1 << 8) != 0;
        bool hasBlendMode = version3 && (flags & 1 << 9) != 0;
        bool hasCacheAsBitmap = version3 && (flags & 1 << 10) != 0;
        bool hasClassName = version3 && (flags & 1 << 11) != 0;
        bool hasImage = version3 && (flags & 1 << 12) != 0;
        bool hasVisible = version3 && (flags & 1 << 13) != 0;
        bool hasOpaqueBackground = version3 && (flags & 1 << 14) != 0;

        int depth = reader.ReadUI16();

        if (hasClassName || (hasImage && hasCharacter)) reader.ReadString();

        int? characterId = hasCharacter ? reader.ReadUI16() : null;
        SwfMatrix? matrix = hasMatrix ? reader.ReadMatrix() : null;
        SwfColorTransform? colorTransform = hasColorTransform ? reader.ReadColorTransformWithAlpha() : null;
        double? ratio = hasRatio ? reader.ReadUI16() / 65535.0 : null;
        string name = hasName ? reader.ReadString() : null;
        int? clipDepth = hasClipDepth ? reader.ReadUI16() : null;

        IReadOnlyList<SwfFilter> filters = hasFilters ? ReadFilterList(reader) : null;
        SwfBlendMode? blendMode = hasBlendMode ? (SwfBlendMode)reader.ReadUI8() : null;
        if (hasCacheAsBitmap) reader.ReadUI8();
        bool? visible = hasVisible ? reader.ReadUI8() != 0 : null;
        if (hasOpaqueBackground) reader.ReadRgba();
        _ = hasClipActions;

        return new SwfPlaceCommand
        {
            Depth = depth,
            IsMove = move,
            CharacterId = characterId,
            Matrix = matrix,
            ColorTransform = colorTransform,
            Ratio = ratio,
            Name = name,
            ClipDepth = clipDepth,
            BlendMode = blendMode,
            Visible = visible,
            Filters = filters,
        };
    }

    internal static List<SwfFilter> ReadFilterList(SwfReader reader)
    {
        int count = reader.ReadUI8();
        var filters = new List<SwfFilter>(count);
        for (int i = 0; i < count; i++) filters.Add(ReadFilter(reader));
        return filters;
    }

    private static SwfFilter ReadFilter(SwfReader reader)
    {
        int id = reader.ReadUI8();
        switch (id)
        {
            case 0:
            {
                var color = reader.ReadRgba();
                double bx = reader.ReadFixed(), by = reader.ReadFixed();
                double angle = reader.ReadFixed(), distance = reader.ReadFixed();
                double strength = reader.ReadFixed8();
                int f = reader.ReadUI8();
                return new SwfFilter
                {
                    Kind = SwfFilterKind.DropShadow, Color = color, BlurX = bx, BlurY = by,
                    Angle = angle, Distance = distance, Strength = strength,
                    Passes = f & 0x1F, CompositeSource = (f & 0x20) != 0,
                    Knockout = (f & 0x40) != 0, Inner = (f & 0x80) != 0,
                };
            }

            case 1:
            {
                double bx = reader.ReadFixed(), by = reader.ReadFixed();
                int f = reader.ReadUI8();
                return new SwfFilter { Kind = SwfFilterKind.Blur, BlurX = bx, BlurY = by, Passes = f >> 3 };
            }

            case 2:
            {
                var color = reader.ReadRgba();
                double bx = reader.ReadFixed(), by = reader.ReadFixed();
                double strength = reader.ReadFixed8();
                int f = reader.ReadUI8();
                return new SwfFilter
                {
                    Kind = SwfFilterKind.Glow, Color = color, BlurX = bx, BlurY = by, Strength = strength,
                    Passes = f & 0x1F, CompositeSource = (f & 0x20) != 0,
                    Knockout = (f & 0x40) != 0, Inner = (f & 0x80) != 0,
                };
            }

            case 3:
            {
                reader.ReadRgba(); reader.ReadRgba();
                double bx = reader.ReadFixed(), by = reader.ReadFixed();
                double angle = reader.ReadFixed(), distance = reader.ReadFixed();
                double strength = reader.ReadFixed8();
                reader.ReadUI8();
                return new SwfFilter { Kind = SwfFilterKind.Bevel, BlurX = bx, BlurY = by, Angle = angle, Distance = distance, Strength = strength };
            }

            case 4:
            case 7:
            {
                int colors = reader.ReadUI8();
                reader.Skip(colors * 4);
                reader.Skip(colors);
                double bx = reader.ReadFixed(), by = reader.ReadFixed();
                reader.ReadFixed(); reader.ReadFixed();
                reader.ReadFixed8();
                reader.ReadUI8();
                return new SwfFilter { Kind = id == 4 ? SwfFilterKind.GradientGlow : SwfFilterKind.GradientBevel, BlurX = bx, BlurY = by };
            }

            case 5:
            {
                int mx = reader.ReadUI8(), my = reader.ReadUI8();
                reader.ReadFloat(); reader.ReadFloat();
                reader.Skip(mx * my * 4);
                reader.ReadRgba();
                reader.ReadUI8();
                return new SwfFilter { Kind = SwfFilterKind.Convolution };
            }

            case 6:
            {
                var matrix = new float[20];
                for (int i = 0; i < 20; i++) matrix[i] = reader.ReadFloat();
                return new SwfFilter { Kind = SwfFilterKind.ColorMatrix, Matrix = matrix };
            }

            default:
                throw new InvalidDataException($"неизвестный фильтр {id}");
        }
    }
}
