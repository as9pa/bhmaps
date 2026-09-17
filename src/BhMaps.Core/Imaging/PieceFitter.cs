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
    public static BitmapSource Fit(BitmapSource source, BitmapSource piece, FitOptions options)
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

    /// <summary>Both decoded from disk (frozen Bgra32, stream closed) and fitted.</summary>
    public static BitmapSource Fit(string sourcePath, string piecePath) =>
        Fit(BackgroundFitter.LoadSource(sourcePath), BackgroundFitter.LoadSource(piecePath));
}
