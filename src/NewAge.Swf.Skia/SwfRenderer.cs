using NewAge.Swf.Buttons;
using NewAge.Swf.Display;
using NewAge.Swf.Shapes;
using SkiaSharp;

namespace NewAge.Swf.Skia;

public sealed class SwfRenderer(SwfMovie movie) : IDisposable
{
    private readonly Dictionary<(SwfMovie Movie, int Character), SKImage> _imageCache = new();
    private readonly Dictionary<(SwfMovie Movie, int Character), MovieClip> _buttonChildren = new();
    private readonly Dictionary<SwfShape, SKPath[]> _pathCache = new();
    private readonly Dictionary<SwfBitmapFill, SKShader> _bitmapShaderCache = new();
    private readonly Dictionary<SwfGradientFill, SKShader> _gradientShaderCache = new();
    private readonly Dictionary<(SwfGradientFill Gradient, (double, double, double, double, double, double, double, double) Tint), SKShader> _tintedGradientCache = new();
    private readonly Dictionary<(double, double, double, double, double, double, double, double), SKColorFilter> _filterCache = new();
    private readonly SKPaint _fillPaint = new() { Style = SKPaintStyle.Fill };
    private readonly SKPaint _strokePaint = new() { Style = SKPaintStyle.Stroke };
    private readonly SKPath _maskPath = new();

    public SwfMovie Movie { get; } = movie;

    public bool Antialias { get; set; } = true;
    public bool Strokes { get; set; } = true;
    public int StatMasks;
    public int StatBlendLayers;
    public int StatShapes;
    public int StatPathsDrawn;
    public long StatMaskMs;

    public void Draw(SKCanvas canvas, MovieClip clip, in SwfMatrix matrix)
        => DrawClip(canvas, clip, matrix, SwfColorTransform.Identity);

    private void DrawClip(SKCanvas canvas, MovieClip clip, in SwfMatrix matrix, in SwfColorTransform colorTransform)
    {
        var owner = clip.Movie;
        var layers = clip.Layers;

        for (int i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (!layer.Visible) continue;

            if (layer.IsMask)
            {
                int last = i;
                while (last + 1 < layers.Count && layers[last + 1].Depth <= layer.ClipDepth) last++;

                long maskStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _maskPath.Reset();
                AppendOutline(_maskPath, owner, layer, layer.Matrix.Concat(matrix));
                if (!_maskPath.IsEmpty)
                {
                    _maskPath.FillType = SKPathFillType.Winding;
                    StatMasks++;
                    canvas.Save();
                    canvas.ClipPath(_maskPath, SKClipOperation.Intersect, antialias: true);
                    for (int j = i + 1; j <= last; j++)
                        DrawLayer(canvas, owner, layers[j], matrix, colorTransform);
                    canvas.Restore();
                }
                StatMaskMs += (System.Diagnostics.Stopwatch.GetTimestamp() - maskStart) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                i = last;
                continue;
            }

            DrawLayer(canvas, owner, layer, matrix, colorTransform);
        }
    }

    private void DrawLayer(SKCanvas canvas, SwfMovie owner, DisplayObject layer, in SwfMatrix parentMatrix, in SwfColorTransform parentColor)
    {
        var matrix = layer.Matrix.Concat(parentMatrix);
        var colorTransform = layer.ColorTransform.Concat(parentColor);

        bool needsLayer = layer.BlendMode is not (SwfBlendMode.Normal or SwfBlendMode.Normal1 or SwfBlendMode.Layer);
        if (needsLayer)
        {
            StatBlendLayers++;
            using var blendPaint = new SKPaint { BlendMode = ToSkia(layer.BlendMode) };
            canvas.SaveLayer(blendPaint);
        }

        var made = layer.Filters is { Count: > 0 } ? new List<SKImageFilter>() : null;
        var effect = made is null ? null : Effects(layer.Filters, parentMatrix, made);
        if (effect is not null)
        {
            StatFilterLayers++;
            using var effectPaint = new SKPaint { ImageFilter = effect };
            canvas.SaveLayer(effectPaint);
        }

        if (layer.Clip is not null)
        {
            DrawClip(canvas, layer.Clip, matrix, colorTransform);
        }
        else
        {
            switch (owner.KindOf(layer.CharacterId))
            {
                case SwfCharacterKind.Shape:
                    DrawShape(canvas, owner, owner.GetShape(layer.CharacterId), matrix, colorTransform);
                    break;

                case SwfCharacterKind.MorphShape:
                    DrawShape(canvas, owner, owner.GetMorphShape(layer.CharacterId, layer.Ratio), matrix, colorTransform);
                    break;

                case SwfCharacterKind.Button:
                    DrawButton(canvas, owner, layer.CharacterId, matrix, colorTransform, layer.ButtonState);
                    break;
            }
        }

        if (effect is not null) canvas.Restore();
        if (made is not null)
            foreach (var filter in made)
                filter.Dispose();
        if (needsLayer) canvas.Restore();
    }

    public int StatFilterLayers;

    private static SKImageFilter Effects(IReadOnlyList<SwfFilter> filters, in SwfMatrix matrix, List<SKImageFilter> made)
    {
        float scale = (float)Math.Sqrt(Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C));
        SKImageFilter current = null;
        foreach (var filter in filters)
        {
            var next = filter.Kind switch
            {
                SwfFilterKind.Blur => Blur(filter, scale, current, made),
                SwfFilterKind.ColorMatrix => ColorMatrix(filter, current, made),
                SwfFilterKind.Glow => Glow(filter, scale, current, made, 0f, 0f),
                SwfFilterKind.DropShadow => Glow(filter, scale, current, made,
                    (float)(Math.Cos(filter.Angle) * filter.Distance * scale),
                    (float)(Math.Sin(filter.Angle) * filter.Distance * scale)),
                _ => null,
            };
            if (next is not null) current = next;
        }
        return current;
    }

    private static SKImageFilter Keep(List<SKImageFilter> made, SKImageFilter filter)
    {
        made.Add(filter);
        return filter;
    }

    private static float Sigma(double blur, int passes, float scale) =>
        (float)(Math.Max(0.0, blur) * scale * Math.Sqrt(Math.Max(1, passes) / 12.0));

    private static SKImageFilter Blur(SwfFilter filter, float scale, SKImageFilter input, List<SKImageFilter> made)
    {
        float sx = Sigma(filter.BlurX, filter.Passes, scale);
        float sy = Sigma(filter.BlurY, filter.Passes, scale);
        return sx < 0.05f && sy < 0.05f ? null : Keep(made, SKImageFilter.CreateBlur(sx, sy, input));
    }

    private static SKImageFilter ColorMatrix(SwfFilter filter, SKImageFilter input, List<SKImageFilter> made)
    {
        if (filter.Matrix is not { Length: 20 } source) return null;
        var m = (float[])source.Clone();
        m[4] /= 255f;
        m[9] /= 255f;
        m[14] /= 255f;
        m[19] /= 255f;
        using var colors = SKColorFilter.CreateColorMatrix(m);
        return Keep(made, SKImageFilter.CreateColorFilter(colors, input));
    }

    private static SKImageFilter Glow(SwfFilter filter, float scale, SKImageFilter input, List<SKImageFilter> made, float dx, float dy)
    {
        float strength = (float)filter.Strength;
        bool hidden = filter.Kind == SwfFilterKind.DropShadow && !filter.CompositeSource;
        if (strength <= 0f)
        {
            if (!filter.Knockout && !hidden) return null;
            using var clear = SKColorFilter.CreateColorMatrix(new float[20]);
            return Keep(made, SKImageFilter.CreateColorFilter(clear, input));
        }
        float sx = Sigma(filter.BlurX, filter.Passes, scale);
        float sy = Sigma(filter.BlurY, filter.Passes, scale);
        var shape = input;
        if (filter.Inner)
        {
            using var invert = SKColorFilter.CreateColorMatrix(new float[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 1 });
            shape = Keep(made, SKImageFilter.CreateColorFilter(invert, input));
        }
        var spread = sx < 0.05f && sy < 0.05f ? shape : Keep(made, SKImageFilter.CreateBlur(sx, sy, shape));
        if (dx != 0f || dy != 0f) spread = Keep(made, SKImageFilter.CreateOffset(dx, dy, spread));
        var color = filter.Color;
        using var tint = SKColorFilter.CreateColorMatrix(new float[]
        {
            0, 0, 0, 0, color.R / 255f,
            0, 0, 0, 0, color.G / 255f,
            0, 0, 0, 0, color.B / 255f,
            0, 0, 0, color.A / 255f * strength, 0,
        });
        var glow = Keep(made, SKImageFilter.CreateColorFilter(tint, spread));
        if (filter.Inner)
        {
            var inside = Keep(made, SKImageFilter.CreateBlendMode(SKBlendMode.SrcIn, input, glow));
            return filter.Knockout || hidden ? inside : Keep(made, SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, input, inside));
        }
        if (filter.Knockout) return Keep(made, SKImageFilter.CreateBlendMode(SKBlendMode.SrcOut, input, glow));
        return hidden ? glow : Keep(made, SKImageFilter.CreateBlendMode(SKBlendMode.SrcOver, glow, input));
    }

    private void DrawButton(SKCanvas canvas, SwfMovie owner, int characterId, in SwfMatrix matrix, in SwfColorTransform colorTransform, int state = 0)
    {
        var button = owner.GetButton(characterId);
        if (button is null) return;

        var records = state == 2 ? button.DownState : state == 1 ? button.OverState : button.UpState;
        if (!records.Any()) records = button.UpState;

        foreach (var record in records)
        {
            var m = record.Matrix.Concat(matrix);
            var cx = record.Color.Concat(colorTransform);

            switch (owner.KindOf(record.CharacterId))
            {
                case SwfCharacterKind.Shape:
                    DrawShape(canvas, owner, owner.GetShape(record.CharacterId), m, cx);
                    break;

                case SwfCharacterKind.MorphShape:
                    DrawShape(canvas, owner, owner.GetMorphShape(record.CharacterId, 0), m, cx);
                    break;

                case SwfCharacterKind.Sprite:
                {
                    var key = (owner, record.CharacterId);
                    if (!_buttonChildren.TryGetValue(key, out var clip))
                    {
                        var timeline = owner.GetTimeline(record.CharacterId);
                        clip = timeline is null ? null : new MovieClip(owner, timeline, record.CharacterId);
                        clip?.GotoAndStop(0);
                        _buttonChildren[key] = clip;
                    }
                    if (clip is not null) DrawClip(canvas, clip, m, cx);
                    break;
                }

                case SwfCharacterKind.Button:
                    DrawButton(canvas, owner, record.CharacterId, m, cx);
                    break;
            }
        }
    }

    private void DrawShape(SKCanvas canvas, SwfMovie owner, SwfShape shape, in SwfMatrix matrix, in SwfColorTransform colorTransform)
    {
        if (shape is null) return;
        StatShapes++;

        canvas.Save();
        var skMatrix = ToSkia(matrix);
        canvas.Concat(ref skMatrix);

        var paths = GetPaths(shape);
        for (int i = 0; i < paths.Length; i++)
        {
            var path = shape.Paths[i];
            var skPath = paths[i];
            if (skPath is null) continue;
            if (canvas.QuickReject(skPath.Bounds)) continue;
            StatPathsDrawn++;

            if (path.Fill is not null)
            {
                canvas.DrawPath(skPath, PrepareFill(owner, path.Fill, colorTransform));
            }
            else if (path.Stroke is not null && Strokes)
            {
                canvas.DrawPath(skPath, PrepareStroke(owner, path.Stroke, colorTransform, matrix));
            }
        }

        canvas.Restore();
    }

    private SKPath[] GetPaths(SwfShape shape)
    {
        if (_pathCache.TryGetValue(shape, out var cached)) return cached;

        var paths = new SKPath[shape.Paths.Count];
        for (int i = 0; i < paths.Length; i++)
        {
            var built = BuildPath(shape.Paths[i].Commands);
            if (built.IsEmpty) { built.Dispose(); continue; }
            if (shape.Paths[i].Fill is not null) built.FillType = SKPathFillType.Winding;
            paths[i] = built;
        }
        return _pathCache[shape] = paths;
    }

    private static SKPath BuildPath(List<SwfPathCommand> commands)
    {
        var path = new SKPath();
        foreach (var c in commands)
        {
            switch (c.Verb)
            {
                case SwfPathVerb.MoveTo: path.MoveTo((float)c.X, (float)c.Y); break;
                case SwfPathVerb.LineTo: path.LineTo((float)c.X, (float)c.Y); break;
                case SwfPathVerb.QuadTo: path.QuadTo((float)c.ControlX, (float)c.ControlY, (float)c.X, (float)c.Y); break;
            }
        }
        return path;
    }

    private void AppendOutline(SKPath target, SwfMovie owner, DisplayObject layer, in SwfMatrix matrix)
    {
        if (layer.Clip is not null)
        {
            foreach (var child in layer.Clip.Layers)
                if (child.Visible)
                    AppendOutline(target, layer.Clip.Movie, child, child.Matrix.Concat(matrix));
            return;
        }

        var shape = owner.KindOf(layer.CharacterId) switch
        {
            SwfCharacterKind.Shape => owner.GetShape(layer.CharacterId),
            SwfCharacterKind.MorphShape => owner.GetMorphShape(layer.CharacterId, layer.Ratio),
            _ => null,
        };
        if (shape is null) return;

        var skMatrix = ToSkia(matrix);
        var paths = GetPaths(shape);
        for (int i = 0; i < paths.Length; i++)
        {
            if (shape.Paths[i].Fill is null || paths[i] is null) continue;
            target.AddPath(paths[i], ref skMatrix);
        }
    }

    private static (double, double, double, double, double, double, double, double) TintOf(in SwfColorTransform t) =>
        (t.RMul, t.GMul, t.BMul, t.AMul, t.RAdd, t.GAdd, t.BAdd, t.AAdd);

    private SKShader GradientShader(SwfGradientFill gradient, in SwfColorTransform colorTransform)
    {
        if (colorTransform.IsIdentity)
        {
            if (!_gradientShaderCache.TryGetValue(gradient, out var shader))
                _gradientShaderCache[gradient] = shader = CreateGradientShader(gradient, colorTransform);
            return shader;
        }
        var key = (gradient, TintOf(colorTransform));
        if (!_tintedGradientCache.TryGetValue(key, out var tinted))
            _tintedGradientCache[key] = tinted = CreateGradientShader(gradient, colorTransform);
        return tinted;
    }

    private SKColorFilter ColorFilter(in SwfColorTransform colorTransform)
    {
        if (colorTransform.IsIdentity) return null;
        var key = TintOf(colorTransform);
        if (!_filterCache.TryGetValue(key, out var filter))
            _filterCache[key] = filter = CreateColorFilter(colorTransform);
        return filter;
    }

    private SKPaint PrepareFill(SwfMovie owner, SwfFillStyle fill, in SwfColorTransform colorTransform)
    {
        var paint = _fillPaint;
        paint.IsAntialias = Antialias;
        paint.Shader = null;
        paint.ColorFilter = null;
        paint.FilterQuality = SKFilterQuality.None;
        paint.Color = SKColors.Black;

        switch (fill)
        {
            case SwfSolidFill solid:
                paint.Color = ToSkia(colorTransform.Apply(solid.Color));
                break;

            case SwfGradientFill gradient:
                paint.Shader = GradientShader(gradient, colorTransform);
                break;

            case SwfBitmapFill bitmap:
                paint.Shader = CreateBitmapShader(owner, bitmap);
                paint.ColorFilter = ColorFilter(colorTransform);
                paint.FilterQuality = SKFilterQuality.High;
                break;
        }

        return paint;
    }

    private SKPaint PrepareStroke(SwfMovie owner, SwfLineStyle stroke, in SwfColorTransform colorTransform, in SwfMatrix matrix)
    {
        double width = stroke.Width;
        if (width <= 0)
        {
            double scale = Math.Sqrt(Math.Abs(matrix.A * matrix.D - matrix.B * matrix.C));
            width = 1.0 / Math.Max(0.0001, scale);
        }

        var paint = _strokePaint;
        paint.IsAntialias = true;
        paint.Shader = null;
        paint.ColorFilter = null;
        paint.Color = SKColors.Black;
        paint.StrokeWidth = (float)width;
        paint.StrokeCap = stroke.StartCap switch
        {
            SwfCapStyle.None => SKStrokeCap.Butt,
            SwfCapStyle.Square => SKStrokeCap.Square,
            _ => SKStrokeCap.Round,
        };
        paint.StrokeJoin = stroke.Join switch
        {
            SwfJoinStyle.Bevel => SKStrokeJoin.Bevel,
            SwfJoinStyle.Miter => SKStrokeJoin.Miter,
            _ => SKStrokeJoin.Round,
        };
        paint.StrokeMiter = (float)stroke.MiterLimit;

        if (stroke.Fill is SwfGradientFill gradient) paint.Shader = GradientShader(gradient, colorTransform);
        else if (stroke.Fill is SwfBitmapFill bitmap) paint.Shader = CreateBitmapShader(owner, bitmap);
        else paint.Color = ToSkia(colorTransform.Apply(stroke.Color));

        return paint;
    }

    private static SKShader CreateGradientShader(SwfGradientFill gradient, in SwfColorTransform colorTransform)
    {
        int n = gradient.Stops.Length;
        if (n == 0) return null;

        var colors = new SKColor[n];
        var positions = new float[n];
        for (int i = 0; i < n; i++)
        {
            colors[i] = ToSkia(colorTransform.Apply(gradient.Stops[i].Color));
            positions[i] = (float)gradient.Stops[i].Ratio;
        }

        var tile = gradient.Spread switch
        {
            SwfSpreadMode.Reflect => SKShaderTileMode.Mirror,
            SwfSpreadMode.Repeat => SKShaderTileMode.Repeat,
            _ => SKShaderTileMode.Clamp,
        };

        var local = ToSkia(gradient.Matrix);
        const float extent = (float)SwfGradientFill.GradientExtent;

        if (!gradient.Radial)
            return SKShader.CreateLinearGradient(new SKPoint(-extent, 0), new SKPoint(extent, 0), colors, positions, tile, local);

        if (Math.Abs(gradient.FocalPoint) < 1e-6)
            return SKShader.CreateRadialGradient(SKPoint.Empty, extent, colors, positions, tile, local);

        var focus = new SKPoint((float)(gradient.FocalPoint * extent), 0);
        return SKShader.CreateTwoPointConicalGradient(focus, 0, SKPoint.Empty, extent, colors, positions, tile, local);
    }

    private SKShader CreateBitmapShader(SwfMovie owner, SwfBitmapFill fill)
    {
        if (_bitmapShaderCache.TryGetValue(fill, out var cached)) return cached;

        var image = GetImage(owner, fill.BitmapId);
        SKShader shader = null;
        if (image is not null)
        {
            var tile = fill.Repeat ? SKShaderTileMode.Repeat : SKShaderTileMode.Clamp;
            shader = image.ToShader(tile, tile, ToSkia(fill.Matrix));
        }
        return _bitmapShaderCache[fill] = shader;
    }

    private static SKColorFilter CreateColorFilter(in SwfColorTransform t)
    {
        if (t.IsIdentity) return null;

        var m = new float[20]
        {
            (float)t.RMul, 0, 0, 0, (float)(t.RAdd / 255.0),
            0, (float)t.GMul, 0, 0, (float)(t.GAdd / 255.0),
            0, 0, (float)t.BMul, 0, (float)(t.BAdd / 255.0),
            0, 0, 0, (float)t.AMul, (float)(t.AAdd / 255.0),
        };
        return SKColorFilter.CreateColorMatrix(m);
    }

    private SKImage GetImage(SwfMovie owner, int characterId)
    {
        var key = (owner, characterId);
        if (_imageCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = owner.GetBitmap(characterId);
        SKImage image = null;

        if (bitmap is not null)
        {
            try { image = BitmapDecoder.Decode(bitmap); }
            catch (Exception) { image = null; }
        }

        return _imageCache[key] = image;
    }

    public static SKMatrix ToSkia(in SwfMatrix m) =>
        new((float)m.A, (float)m.C, (float)m.Tx,
            (float)m.B, (float)m.D, (float)m.Ty,
            0, 0, 1);

    public static SKColor ToSkia(in SwfColor c) => new(c.R, c.G, c.B, c.A);

    private static SKBlendMode ToSkia(SwfBlendMode mode) => mode switch
    {
        SwfBlendMode.Multiply => SKBlendMode.Multiply,
        SwfBlendMode.Screen => SKBlendMode.Screen,
        SwfBlendMode.Lighten => SKBlendMode.Lighten,
        SwfBlendMode.Darken => SKBlendMode.Darken,
        SwfBlendMode.Difference => SKBlendMode.Difference,
        SwfBlendMode.Add => SKBlendMode.Plus,
        SwfBlendMode.Overlay => SKBlendMode.Overlay,
        SwfBlendMode.HardLight => SKBlendMode.HardLight,
        SwfBlendMode.Erase => SKBlendMode.DstOut,
        SwfBlendMode.Alpha => SKBlendMode.DstIn,
        _ => SKBlendMode.SrcOver,
    };

    public void Dispose()
    {
        foreach (var image in _imageCache.Values) image?.Dispose();
        _imageCache.Clear();
        foreach (var paths in _pathCache.Values)
            foreach (var path in paths) path?.Dispose();
        _pathCache.Clear();
        foreach (var shader in _bitmapShaderCache.Values) shader?.Dispose();
        _bitmapShaderCache.Clear();
        foreach (var shader in _gradientShaderCache.Values) shader?.Dispose();
        _gradientShaderCache.Clear();
        foreach (var shader in _tintedGradientCache.Values) shader?.Dispose();
        _tintedGradientCache.Clear();
        foreach (var filter in _filterCache.Values) filter?.Dispose();
        _filterCache.Clear();
        _fillPaint.Dispose();
        _strokePaint.Dispose();
        _maskPath.Dispose();
    }
}
