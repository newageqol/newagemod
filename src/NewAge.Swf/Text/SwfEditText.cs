namespace NewAge.Swf.Text;

public sealed class SwfEditText
{
    public int CharacterId { get; init; }
    public SwfRect Bounds { get; init; }
    public SwfColor Color { get; init; }
    public double FontHeight { get; init; }
    public bool Multiline { get; init; }
    public bool Password { get; init; }
    public bool AutoSize { get; init; }

    public override string ToString() =>
        $"#{CharacterId} {Bounds} h={FontHeight:0.#} {Color}{(Multiline ? " multiline" : "")}";
}

