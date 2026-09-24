using System.Runtime.InteropServices;
using NewAge.Swf.Bitmaps;
using SkiaSharp;

namespace NewAge.Swf.Skia;

public static class BitmapDecoder
{
    public static SKImage Decode(SwfBitmap bitmap)
    {
        if (bitmap is null) return null;

        if (bitmap.Format == SwfBitmapFormat.RawBgra)
            return FromBgra(bitmap.Data, bitmap.Width, bitmap.Height);

        using var data = SKData.CreateCopy(bitmap.Data);
        using var codecImage = SKBitmap.Decode(data);
        if (codecImage is null) return null;

        if (bitmap.Alpha is not null && bitmap.Alpha.Length >= codecImage.Width * codecImage.Height)
            return ApplyAlpha(codecImage, bitmap.Alpha);

        return SKImage.FromBitmap(codecImage);
    }

    private static SKImage FromBgra(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), width * 4);
            return SKImage.FromPixelCopy(pixmap);
        }
        finally
        {
            handle.Free();
        }
    }

    private static SKImage ApplyAlpha(SKBitmap source, byte[] alpha)
    {
        int width = source.Width, height = source.Height;
        var result = new byte[width * height * 4];

        using var pixmap = source.PeekPixels();
        for (int y = 0, i = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++, i++)
            {
                var c = pixmap is not null ? pixmap.GetPixelColor(x, y) : source.GetPixel(x, y);
                int o = i * 4;
                result[o] = c.Blue;
                result[o + 1] = c.Green;
                result[o + 2] = c.Red;
                result[o + 3] = alpha[i];
            }
        }

        return FromBgra(result, width, height);
    }
}
