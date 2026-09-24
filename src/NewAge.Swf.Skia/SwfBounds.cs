using NewAge.Swf.Display;
using SkiaSharp;

namespace NewAge.Swf.Skia;

public static class SwfBounds
{
    public static SKRect Measure(SwfMovie movie, MovieClip clip, in SwfMatrix matrix)
    {
        var rect = SKRect.Empty;
        bool any = false;
        Walk(movie, clip, matrix, ref rect, ref any);
        return any ? rect : SKRect.Empty;
    }

    public static SKRect MeasureAnimation(SwfMovie movie, MovieClip clip, in SwfMatrix matrix, int frames)
    {
        var rect = SKRect.Empty;
        bool any = false;
        for (int i = 0; i < frames; i++)
        {
            Walk(movie, clip, matrix, ref rect, ref any);
            clip.Advance();
        }
        return any ? rect : SKRect.Empty;
    }

    private static void Walk(SwfMovie movie, MovieClip clip, in SwfMatrix matrix, ref SKRect rect, ref bool any)
    {
        var owner = clip.Movie;
        foreach (var layer in clip.Layers)
        {
            if (!layer.Visible) continue;
            float pad = Pad(layer.Filters, matrix);
            if (pad <= 0f)
            {
                Layer(owner, layer, matrix, ref rect, ref any);
                continue;
            }
            var inner = SKRect.Empty;
            bool found = false;
            Layer(owner, layer, matrix, ref inner, ref found);
            if (!found) continue;
            Include(ref rect, ref any, new SKPoint(inner.Left - pad, inner.Top - pad));
            Include(ref rect, ref any, new SKPoint(inner.Right + pad, inner.Bottom + pad));
        }
    }

    private static void Layer(SwfMovie owner, DisplayObject layer, in SwfMatrix matrix, ref SKRect rect, ref bool any)
    {
        var m = layer.Matrix.Concat(matrix);

        if (layer.Clip is not null)
        {
            Walk(owner, layer.Clip, m, ref rect, ref any);
            return;
        }

        var shape = owner.KindOf(layer.CharacterId) switch
        {
            SwfCharacterKind.Shape => owner.GetShape(layer.CharacterId),
            SwfCharacterKind.MorphShape => owner.GetMorphShape(layer.CharacterId, layer.Ratio),
            _ => null,
        };
        if (shape is null) return;

        var skMatrix = SwfRenderer.ToSkia(m);
        foreach (var path in shape.Paths)
        {
            foreach (var command in path.Commands)
            {
                var p = skMatrix.MapPoint((float)command.X, (float)command.Y);
                Include(ref rect, ref any, p);
                if (command.Verb == Swf.Shapes.SwfPathVerb.QuadTo)
                    Include(ref rect, ref any, skMatrix.MapPoint((float)command.ControlX, (float)command.ControlY));
            }
        }
    }

    private static float Pad(IReadOnlyList<SwfFilter> filters, in SwfMatrix matrix)
    {
        if (filters is null || filters.Count == 0) return 0f;
        double scale = Math.Sqrt(Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C));
        double pad = 0;
        foreach (var filter in filters)
        {
            if (filter.Inner || filter.Kind is not (SwfFilterKind.Blur or SwfFilterKind.Glow or SwfFilterKind.DropShadow)) continue;
            double blur = Math.Max(filter.BlurX, filter.BlurY);
            int passes = Math.Max(1, filter.Passes);
            pad += Math.Max(blur * passes * 0.5, 3.0 * blur * Math.Sqrt(passes / 12.0));
            if (filter.Kind == SwfFilterKind.DropShadow) pad += filter.Distance;
        }
        return (float)(pad * scale);
    }

    private static void Include(ref SKRect rect, ref bool any, SKPoint p)
    {
        if (!any)
        {
            rect = new SKRect(p.X, p.Y, p.X, p.Y);
            any = true;
            return;
        }
        if (p.X < rect.Left) rect.Left = p.X;
        if (p.X > rect.Right) rect.Right = p.X;
        if (p.Y < rect.Top) rect.Top = p.Y;
        if (p.Y > rect.Bottom) rect.Bottom = p.Y;
    }
}
