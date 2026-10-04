using NewAge.Swf.Display;

namespace NewAge.Swf.Buttons;

public sealed class SwfButtonRecord
{
    public bool Up { get; init; }
    public bool Over { get; init; }
    public bool Down { get; init; }
    public bool HitTest { get; init; }
    public int CharacterId { get; init; }
    public int Depth { get; init; }
    public SwfMatrix Matrix { get; init; }
    public SwfColorTransform Color { get; init; }
}

public sealed class SwfButton
{
    public int CharacterId { get; init; }
    public IReadOnlyList<SwfButtonRecord> Records { get; init; }

    public IEnumerable<SwfButtonRecord> UpState =>
        Records.Where(r => r.Up).OrderBy(r => r.Depth);

    public IEnumerable<SwfButtonRecord> OverState =>
        Records.Where(r => r.Over).OrderBy(r => r.Depth);

    public IEnumerable<SwfButtonRecord> DownState =>
        Records.Where(r => r.Down).OrderBy(r => r.Depth);
}

public static class ButtonParser
{
    public static SwfButton Parse(SwfFile file, in SwfTag tag)
    {
        bool v2 = tag.Code == SwfTagCode.DefineButton2;
        if (!v2 && tag.Code != SwfTagCode.DefineButton)
            throw new ArgumentException($"{tag.Code} — не кнопка", nameof(tag));

        var reader = file.ReaderAt(tag);
        int id = reader.ReadUI16();
        if (v2)
        {
            reader.ReadUI8();
            reader.ReadUI16();
        }

        var records = new List<SwfButtonRecord>();
        while (reader.Position < tag.BodyEnd)
        {
            byte flags = reader.ReadUI8();
            if (flags == 0) break;

            int characterId = reader.ReadUI16();
            int depth = reader.ReadUI16();
            var matrix = reader.ReadMatrix();
            var color = v2 ? reader.ReadColorTransformWithAlpha() : SwfColorTransform.Identity;
            if (v2 && (flags & 0x10) != 0) TimelineParser.ReadFilterList(reader);
            if (v2 && (flags & 0x20) != 0) reader.ReadUI8();

            records.Add(new SwfButtonRecord
            {
                Up = (flags & 1) != 0,
                Over = (flags & 2) != 0,
                Down = (flags & 4) != 0,
                HitTest = (flags & 8) != 0,
                CharacterId = characterId,
                Depth = depth,
                Matrix = matrix,
                Color = color,
            });
        }

        return new SwfButton { CharacterId = id, Records = records };
    }
}
