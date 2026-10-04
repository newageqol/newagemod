namespace NewAge.Swf;

public sealed class SwfIndex
{
    public SwfFile File { get; private init; }

    public Dictionary<int, SwfTag> Characters { get; } = new();

    public Dictionary<int, List<SwfTag>> Sprites { get; } = new();

    public Dictionary<int, int> SpriteFrameCounts { get; } = new();

    public Dictionary<int, string> Exports { get; } = new();

    public Dictionary<string, int> ExportsByName { get; } = new(StringComparer.Ordinal);

    public Dictionary<int, List<(int Frame, string Name)>> FrameLabels { get; } = new();

    public Dictionary<SwfTagCode, int> TagHistogram { get; } = new();

    public int MainFrameCount { get; private set; }
    public int ShapeCount { get; private set; }
    public int BitmapCount { get; private set; }
    public int ActionByteCount { get; private set; }

    public IReadOnlyList<SwfTag> MainTimeline { get; private init; }

    public static SwfIndex Build(SwfFile file)
    {
        var index = new SwfIndex { File = file, MainTimeline = file.Tags };
        index.Walk(file.Tags, spriteId: 0);
        return index;
    }

    private void Walk(IReadOnlyList<SwfTag> tags, int spriteId)
    {
        int frame = 0;

        foreach (var tag in tags)
        {
            TagHistogram[tag.Code] = TagHistogram.GetValueOrDefault(tag.Code) + 1;

            switch (tag.Code)
            {
                case SwfTagCode.ShowFrame:
                    frame++;
                    break;

                case SwfTagCode.FrameLabel:
                {
                    var reader = File.ReaderAt(tag);
                    string name = reader.ReadString();
                    if (!FrameLabels.TryGetValue(spriteId, out var list))
                        FrameLabels[spriteId] = list = new List<(int, string)>();
                    list.Add((frame, name));
                    break;
                }

                case SwfTagCode.DefineSprite:
                {
                    var reader = File.ReaderAt(tag);
                    int id = reader.ReadUI16();
                    int declaredFrames = reader.ReadUI16();
                    var inner = SwfFile.ReadTags(File.Body, reader.Position, tag.BodyEnd);

                    Characters[id] = tag;
                    Sprites[id] = inner;
                    SpriteFrameCounts[id] = declaredFrames;
                    Walk(inner, id);
                    break;
                }

                case SwfTagCode.ExportAssets:
                {
                    var reader = File.ReaderAt(tag);
                    int count = reader.ReadUI16();
                    for (int i = 0; i < count; i++)
                    {
                        int id = reader.ReadUI16();
                        string name = reader.ReadString();
                        Exports[id] = name;
                        ExportsByName[name] = id;
                    }
                    break;
                }

                case SwfTagCode.DoAction:
                case SwfTagCode.DoInitAction:
                    ActionByteCount += tag.BodyLength;
                    break;

                default:
                    RegisterCharacter(tag);
                    break;
            }
        }

        if (spriteId == 0) MainFrameCount = frame;
    }

    private void RegisterCharacter(in SwfTag tag)
    {
        switch (tag.Code)
        {
            case SwfTagCode.DefineShape:
            case SwfTagCode.DefineShape2:
            case SwfTagCode.DefineShape3:
            case SwfTagCode.DefineShape4:
                ShapeCount++;
                goto register;

            case SwfTagCode.DefineBits:
            case SwfTagCode.DefineBitsJpeg2:
            case SwfTagCode.DefineBitsJpeg3:
            case SwfTagCode.DefineBitsJpeg4:
            case SwfTagCode.DefineBitsLossless:
            case SwfTagCode.DefineBitsLossless2:
                BitmapCount++;
                goto register;

            case SwfTagCode.DefineMorphShape:
            case SwfTagCode.DefineMorphShape2:
            case SwfTagCode.DefineButton:
            case SwfTagCode.DefineButton2:
            case SwfTagCode.DefineText:
            case SwfTagCode.DefineText2:
            case SwfTagCode.DefineEditText:
            case SwfTagCode.DefineFont:
            case SwfTagCode.DefineFont2:
            case SwfTagCode.DefineFont3:
            case SwfTagCode.DefineSound:
            case SwfTagCode.DefineVideoStream:
            register:
            {
                var reader = File.ReaderAt(tag);
                int id = reader.ReadUI16();
                Characters[id] = tag;
                break;
            }
        }
    }

}
