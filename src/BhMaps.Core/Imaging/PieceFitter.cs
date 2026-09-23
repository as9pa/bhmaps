using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>A picture fitted to one platform piece: covered and centred like a background on the screen, then cut
/// to the piece's own shape by multiplying in the piece's alpha (spec 5). A platform is a shaped cut-out and a
/// rectangle dropped on it would draw a box where the game draws nothing, so the piece's alpha always wins.</summary>
public static class PieceFitter
{
    /// <summary>The picture, cover-fitted to the piece's rectangle and masked by the piece's alpha. Frozen Bgra32,
    /// the piece's size and DPI.</summary>
    public static BitmapSource Fit(BitmapSource source, BitmapSource piece) =>
        Fit(source, piece, new FitOptions());

    /// <summary>The same, fitted the way the caller asks rather than always covered (3.0 E).</summary>
    public static BitmapSource Fit(BitmapSource source, BitmapSource piece, FitOptions options) =>
        Fit(source, piece, options, piece);

    /// <summary>The same, with the art Fit and Center leave showing where the picture does not reach (3.2 F1).
    /// original is the piece's own art at the piece's size; the shape still comes from piece.</summary>
    public static BitmapSource Fit(BitmapSource source, BitmapSource piece, FitOptions options, BitmapSource original)
    {
        var width = piece.PixelWidth;
        var height = piece.PixelHeight;
        var dest = BackgroundFitter.DestinationRect(source.PixelWidth, source.PixelHeight, options, width, height);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, dest);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var stride = width * 4;
        var pixels = new byte[stride * height];
        new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);

        MaskBy(pixels, piece);
        if (KeepsOriginal(options))
        {
            FillUncovered(pixels, Coverage(width, height, dest, null), original, piece);
        }

        var result = new WriteableBitmap(width, height, piece.DpiX, piece.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    /// <summary>Multiplies the piece's alpha into <paramref name="pixels"/>, a Bgra32 buffer the piece's own size,
    /// and hands the same array back. SpanFitter cuts its own pixels and then masks them the same way.</summary>
    internal static byte[] MaskBy(byte[] pixels, BitmapSource piece)
    {
        var stride = piece.PixelWidth * 4;
        var mask = new byte[stride * piece.PixelHeight];
        var pieceBgra = piece.Format == PixelFormats.Bgra32
            ? piece
            : new FormatConvertedBitmap(piece, PixelFormats.Bgra32, null, 0);
        pieceBgra.CopyPixels(mask, stride, 0);

        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)Math.Round(mask[i] * pixels[i] / 255.0, MidpointRounding.AwayFromZero);
        }

        return pixels;
    }

    /// <summary>Fit and Center can leave part of the piece bare; Fill and Stretch always reach every pixel and stay
    /// exactly as they were (3.2 F1).</summary>
    internal static bool KeepsOriginal(FitOptions options) =>
        options.Mode is FitMode.Contain or FitMode.Center;

    /// <summary>How much of each pixel of a width x height canvas the picture's rectangle covers, 0 to 255, drawn
    /// the same way (and under the same transform) as the picture itself so the edges match.</summary>
    internal static byte[] Coverage(int width, int height, Rect rect, Transform? transform)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (transform is not null)
            {
                dc.PushTransform(transform);
            }

            dc.DrawRectangle(Brushes.White, null, rect);
            if (transform is not null)
            {
                dc.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        target.CopyPixels(pixels, stride, 0);

        var coverage = new byte[width * height];
        for (var i = 0; i < coverage.Length; i++)
        {
            coverage[i] = pixels[(i * 4) + 3];
        }

        return coverage;
    }

    /// <summary>Lays the piece's original art under the already masked picture wherever the picture does not
    /// cover the pixel, so the platform keeps its whole shape (3.2 F1). The picture stays on top at full strength
    /// and the original shows through only the uncovered share, which keeps a soft picture edge seamless. An
    /// original of another size cannot line up, so the piece's own art stands in for it, and the piece's alpha
    /// still bounds what the original may show.</summary>
    internal static void FillUncovered(byte[] pixels, byte[] coverage, BitmapSource original, BitmapSource piece)
    {
        if (original.PixelWidth != piece.PixelWidth || original.PixelHeight != piece.PixelHeight)
        {
            original = piece;
        }

        var art = Bgra(original);
        var shape = ReferenceEquals(original, piece) ? art : Bgra(piece);
        for (var p = 0; p < coverage.Length; p++)
        {
            var bare = (255 - coverage[p]) / 255.0;
            if (bare <= 0)
            {
                continue;
            }

            var i = p * 4;
            var pictureAlpha = pixels[i + 3] / 255.0;
            var artAlpha = Math.Min(art[i + 3], shape[i + 3]) / 255.0 * bare;
            var alpha = Math.Min(1.0, pictureAlpha + artAlpha);
            if (alpha <= 0)
            {
                continue;
            }

            for (var c = 0; c < 3; c++)
            {
                var premultiplied = (pixels[i + c] * pictureAlpha) + (art[i + c] * artAlpha);
                pixels[i + c] = (byte)Math.Clamp(Math.Round(premultiplied / alpha, MidpointRounding.AwayFromZero), 0, 255);
            }

            pixels[i + 3] = (byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>A bitmap's pixels as a Bgra32 buffer.</summary>
    private static byte[] Bgra(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        var bgra = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        bgra.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    /// <summary>Both decoded from disk (frozen Bgra32, stream closed) and fitted.</summary>
    public static BitmapSource Fit(string sourcePath, string piecePath) =>
        Fit(BackgroundFitter.LoadSource(sourcePath), BackgroundFitter.LoadSource(piecePath));
}
