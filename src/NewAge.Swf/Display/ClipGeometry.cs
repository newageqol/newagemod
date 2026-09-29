namespace NewAge.Swf.Display;

public static class ClipGeometry
{
    public static SwfRect? Bounds(MovieClip clip, in SwfMatrix matrix, int depth = 0)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        Walk(clip, matrix, depth, ref minX, ref minY, ref maxX, ref maxY);
        if (minX > maxX || minY > maxY) return null;
        return new SwfRect((int)(minX * 20), (int)(maxX * 20), (int)(minY * 20), (int)(maxY * 20));
    }

    private static void Walk(MovieClip clip, in SwfMatrix matrix, int depth,
        ref double minX, ref double minY, ref double maxX, ref double maxY)
    {
        if (depth > 12) return;

        foreach (var layer in clip.Layers)
        {
            if (!layer.Visible) continue;
            var m = layer.Matrix.Concat(matrix);

            if (layer.Clip is not null)
            {
                Walk(layer.Clip, m, depth + 1, ref minX, ref minY, ref maxX, ref maxY);
                continue;
            }

            SwfRect? local = clip.Movie.KindOf(layer.CharacterId) switch
            {
                SwfCharacterKind.Shape => clip.Movie.GetShape(layer.CharacterId)?.Bounds,
                SwfCharacterKind.EditText => clip.Movie.GetEditText(layer.CharacterId)?.Bounds,
                SwfCharacterKind.Button => clip.Movie.GetButtonBounds(layer.CharacterId),
                SwfCharacterKind.MorphShape => clip.Movie.GetMorphShape(layer.CharacterId)?.Start?.Bounds,
                _ => null,
            };
            if (local is not { } b) continue;

            foreach (var (px, py) in new[]
            {
                (b.X, b.Y), (b.X + b.Width, b.Y), (b.X, b.Y + b.Height), (b.X + b.Width, b.Y + b.Height),
            })
            {
                double x = m.A * px + m.C * py + m.Tx;
                double y = m.B * px + m.D * py + m.Ty;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
    }

    public static bool Contains(SwfRect rect, double x, double y) =>
        x >= rect.X && x <= rect.X + rect.Width && y >= rect.Y && y <= rect.Y + rect.Height;
}
