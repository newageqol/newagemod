using System.Collections.Concurrent;
using NewAge.Swf.Bitmaps;
using NewAge.Swf.Buttons;
using NewAge.Swf.Shapes;
using NewAge.Swf.Text;

namespace NewAge.Swf.Display;

public enum SwfCharacterKind { Unknown, Shape, Sprite, Bitmap, MorphShape, Text, EditText, Button, Font, Sound }

public sealed class SwfMovie
{
    private readonly ConcurrentDictionary<int, Lazy<SwfShape>> _shapes = new();
    private readonly ConcurrentDictionary<int, Lazy<SwfMorphShape>> _morphShapes = new();
    private readonly ConcurrentDictionary<(int Character, int Step), Lazy<SwfShape>> _morphSamples = new();
    private readonly ConcurrentDictionary<int, Lazy<SwfBitmap>> _bitmaps = new();
    private readonly ConcurrentDictionary<int, SwfButton> _buttons = new();
    private readonly ConcurrentDictionary<int, Lazy<SwfTimeline>> _timelines = new();
    private byte[] _jpegTables;

    public SwfFile File { get; }
    public SwfIndex Index { get; }
    public string Name { get; }

    public SwfMovie(SwfFile file)
    {
        File = file;
        Index = SwfIndex.Build(file);
        Name = file.SourcePath is null ? "<память>" : Path.GetFileNameWithoutExtension(file.SourcePath);

        foreach (var tag in file.Tags)
        {
            if (tag.Code == SwfTagCode.JpegTables)
            {
                _jpegTables = BitmapParser.ReadJpegTables(file, tag);
                break;
            }
        }
    }

    public static SwfMovie Load(string path) => new(SwfFile.Load(path));

    public double FrameRate => File.FrameRate;
    public SwfRect Stage => File.FrameSize;

    public SwfCharacterKind KindOf(int characterId)
    {
        if (Index.Sprites.ContainsKey(characterId)) return SwfCharacterKind.Sprite;
        if (!Index.Characters.TryGetValue(characterId, out var tag)) return SwfCharacterKind.Unknown;
        return tag.Code switch
        {
            SwfTagCode.DefineShape or SwfTagCode.DefineShape2
                or SwfTagCode.DefineShape3 or SwfTagCode.DefineShape4 => SwfCharacterKind.Shape,
            SwfTagCode.DefineBits or SwfTagCode.DefineBitsJpeg2 or SwfTagCode.DefineBitsJpeg3
                or SwfTagCode.DefineBitsJpeg4 or SwfTagCode.DefineBitsLossless
                or SwfTagCode.DefineBitsLossless2 => SwfCharacterKind.Bitmap,
            SwfTagCode.DefineMorphShape or SwfTagCode.DefineMorphShape2 => SwfCharacterKind.MorphShape,
            SwfTagCode.DefineText or SwfTagCode.DefineText2 => SwfCharacterKind.Text,
            SwfTagCode.DefineEditText => SwfCharacterKind.EditText,
            SwfTagCode.DefineButton or SwfTagCode.DefineButton2 => SwfCharacterKind.Button,
            SwfTagCode.DefineFont or SwfTagCode.DefineFont2 or SwfTagCode.DefineFont3 => SwfCharacterKind.Font,
            SwfTagCode.DefineSound => SwfCharacterKind.Sound,
            _ => SwfCharacterKind.Unknown,
        };
    }

    private readonly ConcurrentDictionary<int, string> _brokenShapes = new();

    public SwfShape GetShape(int characterId)
    {
        if (_brokenShapes.ContainsKey(characterId)) return null;
        try
        {
            return _shapes.GetOrAdd(characterId, id => new Lazy<SwfShape>(() => ParseShape(id))).Value;
        }
        catch (Exception ex)
        {
            _brokenShapes[characterId] = ex.Message;
            return null;
        }
    }

    private SwfShape ParseShape(int characterId)
    {
        if (!Index.Characters.TryGetValue(characterId, out var tag)) return null;
        return tag.Code is SwfTagCode.DefineShape or SwfTagCode.DefineShape2
                        or SwfTagCode.DefineShape3 or SwfTagCode.DefineShape4
            ? ShapeParser.Parse(File, tag)
            : null;
    }

    public SwfMorphShape GetMorphShape(int characterId)
    {
        if (_brokenShapes.ContainsKey(characterId)) return null;
        try
        {
            return _morphShapes.GetOrAdd(characterId, id => new Lazy<SwfMorphShape>(() => ParseMorphShape(id))).Value;
        }
        catch (Exception ex)
        {
            _brokenShapes[characterId] = ex.Message;
            return null;
        }
    }

    private SwfMorphShape ParseMorphShape(int characterId)
    {
        if (!Index.Characters.TryGetValue(characterId, out var tag)) return null;
        return tag.Code is SwfTagCode.DefineMorphShape or SwfTagCode.DefineMorphShape2
            ? MorphShapeParser.Parse(File, tag)
            : null;
    }

    public SwfShape GetMorphShape(int characterId, double ratio)
    {
        var morph = GetMorphShape(characterId);
        if (morph is null) return null;

        int step = (int)Math.Round(Math.Clamp(ratio, 0, 1) * 255);
        try
        {
            return _morphSamples.GetOrAdd((characterId, step), key => new Lazy<SwfShape>(() => morph.Sample(key.Step / 255.0))).Value;
        }
        catch (Exception ex)
        {
            _brokenShapes[characterId] = ex.Message;
            return null;
        }
    }

    public SwfButton GetButton(int characterId) => _buttons.GetOrAdd(characterId, ParseButton);

    private SwfButton ParseButton(int characterId)
    {
        if (!Index.Characters.TryGetValue(characterId, out var tag)) return null;
        if (tag.Code is not (SwfTagCode.DefineButton or SwfTagCode.DefineButton2)) return null;
        try { return ButtonParser.Parse(File, tag); }
        catch (Exception) { return null; }
    }

    public SwfBitmap GetBitmap(int characterId) =>
        _bitmaps.GetOrAdd(characterId, id => new Lazy<SwfBitmap>(() => ParseBitmap(id))).Value;

    private SwfBitmap ParseBitmap(int characterId)
    {
        if (!Index.Characters.TryGetValue(characterId, out var tag)) return null;
        if (KindOf(characterId) != SwfCharacterKind.Bitmap) return null;
        try { return BitmapParser.Parse(File, tag, _jpegTables); }
        catch (Exception) { return null; }
    }

    public SwfTimeline GetTimeline(int characterId) =>
        _timelines.GetOrAdd(characterId, id => new Lazy<SwfTimeline>(() => ParseTimeline(id))).Value;

    private SwfTimeline ParseTimeline(int characterId)
    {
        if (characterId == 0) return TimelineParser.Parse(File, File.Tags);
        return Index.Sprites.TryGetValue(characterId, out var tags) ? TimelineParser.Parse(File, tags, characterId) : null;
    }

    public int? FindExport(string name) =>
        Index.ExportsByName.TryGetValue(name, out int id) ? id : null;

    public MovieClip CreateRoot() => new(this, GetTimeline(0), characterId: 0);

    public MovieClip CreateInstance(string exportName)
    {
        int? id = FindExport(exportName);
        if (id is null) return null;
        var timeline = GetTimeline(id.Value);
        return timeline is null ? null : new MovieClip(this, timeline, id.Value);
    }
}
