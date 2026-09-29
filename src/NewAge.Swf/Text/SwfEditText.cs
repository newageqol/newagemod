namespace NewAge.Swf.Text;

public enum SwfTextAlign { Left = 0, Right = 1, Center = 2, Justify = 3 }

public sealed class SwfEditText
{
    public int CharacterId { get; init; }
    public SwfRect Bounds { get; init; }
    public string InitialText { get; init; }
    public string VariableName { get; init; }
    public SwfColor Color { get; init; }
    public double FontHeight { get; init; }
    public SwfTextAlign Align { get; init; }
    public double LeftMargin { get; init; }
    public double RightMargin { get; init; }
    public double Indent { get; init; }
    public double Leading { get; init; }
    public bool Multiline { get; init; }
    public bool WordWrap { get; init; }
    public bool Html { get; init; }
    public bool Password { get; init; }
    public bool ReadOnly { get; init; }
    public bool Selectable { get; init; }
    public bool Border { get; init; }
    public bool AutoSize { get; init; }

    public override string ToString() =>
        $"#{CharacterId} {Bounds} h={FontHeight:0.#} {Color}{(Multiline ? " multiline" : "")}";
}

public static class EditTextParser
{
    public static SwfEditText Parse(SwfFile file, in SwfTag tag)
    {
        if (tag.Code != SwfTagCode.DefineEditText)
            throw new ArgumentException($"{tag.Code} — не текстовое поле", nameof(tag));

        var reader = file.ReaderAt(tag);
        int characterId = reader.ReadUI16();
        var bounds = reader.ReadRect();

        reader.Align();
        bool hasText = reader.ReadFlag();
        bool wordWrap = reader.ReadFlag();
        bool multiline = reader.ReadFlag();
        bool password = reader.ReadFlag();
        bool readOnly = reader.ReadFlag();
        bool hasColor = reader.ReadFlag();
        bool hasMaxLength = reader.ReadFlag();
        bool hasFont = reader.ReadFlag();
        bool hasFontClass = reader.ReadFlag();
        bool autoSize = reader.ReadFlag();
        bool hasLayout = reader.ReadFlag();
        bool noSelect = reader.ReadFlag();
        bool border = reader.ReadFlag();
        reader.ReadFlag();
        bool html = reader.ReadFlag();
        reader.ReadFlag();

        double fontHeight = 0;
        if (hasFont)
        {
            reader.ReadUI16();
            fontHeight = reader.ReadUI16() / 20.0;
        }
        if (hasFontClass)
        {
            reader.ReadString();
            fontHeight = reader.ReadUI16() / 20.0;
        }

        var color = new SwfColor(0, 0, 0, 255);
        if (hasColor) color = reader.ReadRgba();
        if (hasMaxLength) reader.ReadUI16();

        var align = SwfTextAlign.Left;
        double leftMargin = 0, rightMargin = 0, indent = 0, leading = 0;
        if (hasLayout)
        {
            align = (SwfTextAlign)reader.ReadUI8();
            leftMargin = reader.ReadUI16() / 20.0;
            rightMargin = reader.ReadUI16() / 20.0;
            indent = reader.ReadUI16() / 20.0;
            leading = reader.ReadSI16() / 20.0;
        }

        string variableName = reader.ReadString();
        string initialText = hasText ? reader.ReadString() : null;

        return new SwfEditText
        {
            CharacterId = characterId,
            Bounds = bounds,
            InitialText = initialText,
            VariableName = string.IsNullOrEmpty(variableName) ? null : variableName,
            Color = color,
            FontHeight = fontHeight,
            Align = align,
            LeftMargin = leftMargin,
            RightMargin = rightMargin,
            Indent = indent,
            Leading = leading,
            Multiline = multiline,
            WordWrap = wordWrap,
            Html = html,
            Password = password,
            ReadOnly = readOnly,
            Selectable = !noSelect,
            Border = border,
            AutoSize = autoSize,
        };
    }
}
