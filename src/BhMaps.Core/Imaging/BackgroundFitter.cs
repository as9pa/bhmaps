using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

public enum FitMode
{
    Cover,
    Contain,
    Stretch,
    Center,
}

/// <summary>PanX/PanY are 0..1 and only matter for Cover (0.5 = centered). Darken is 0..1 and multiplies every channel by (1 - Darken). NoUpscale only matters for Contain: a source smaller than the canvas stays at 1:1 instead of growing.</summary>
public sealed record FitOptions(FitMode Mode = FitMode.Cover, double PanX = 0.5, double PanY = 0.5, double Darken = 0.0, bool NoUpscale = false);

/// <summary>A picture ready to draw, plus the pixel size the picture really is. A preview's working copy is
/// downsampled to about the preview canvas, so its bitmap is no longer that size, and Center is the one mode that
/// has to know it (3.0 E).</summary>
public sealed record WorkingSource(BitmapSource Bitmap, int NaturalWidth, int NaturalHeight)
{
    /// <summary>A source that is already at its natural size, as everything but a working copy is.</summary>
    public static WorkingSource Of(BitmapSource bitmap) => new(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
}

/// <summary>Turns any image into a 2048x1151 background. Safe to call from any thread; every bitmap it returns is frozen.</summary>
public static class BackgroundFitter
{
    public const int OutputWidth = 2048;
    public const int OutputHeight = 1151;
    public const int JpegQuality = 90;

    public static BitmapSource LoadSource(string sourcePath)
    {
        using var stream = File.OpenRead(sourcePath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>The source decoded no larger than a canvas of this size needs: the scale a Cover fit would use,
    /// which is the largest any mode asks for, and never above 1:1. The editor decodes once into this and renders
    /// every preview from it (spec 7.2), so a slider drag costs one draw instead of a decode plus a draw.</summary>
    public static WorkingSource LoadWorkingSource(string sourcePath, int canvasWidth, int canvasHeight)
    {
        var source = LoadSource(sourcePath);
        var scale = Math.Max((double)canvasWidth / source.PixelWidth, (double)canvasHeight / source.PixelHeight);
        if (scale >= 1.0)
        {
            return WorkingSource.Of(source);
        }

        // Drawn rather than transformed, so the downscale takes the same HighQuality path Render takes and the
        // preview cannot drift from the full-size fit by more than rounding.
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return new WorkingSource(converted, source.PixelWidth, source.PixelHeight);
    }

    /// <summary>Where the scaled source lands inside a width x height canvas. Cover rects overflow the canvas; the overflow is clipped when drawn.</summary>
    public static Rect DestinationRect(int srcW, int srcH, FitOptions options, int width, int height) =>
        DestinationRect(srcW, srcH, options, width, height, srcW, srcH, width, height);

    /// <summary>The same, for a canvas that stands in for a larger output. Center is the one mode that does not
    /// scale to the canvas, so a preview has to draw the picture at the canvas's own share of the output, from the
    /// size the picture really is rather than the size a downsampled working copy happens to be. On the output
    /// itself the share is 1 and the picture keeps its own pixels (3.0 E).</summary>
    public static Rect DestinationRect(
        int srcW,
        int srcH,
        FitOptions options,
        int width,
        int height,
        int naturalWidth,
        int naturalHeight,
        int outputWidth,
        int outputHeight)
    {
        switch (options.Mode)
        {
            case FitMode.Stretch:
                return new Rect(0, 0, width, height);
            case FitMode.Center:
                {
                    // No scale of its own: the picture keeps its natural pixels in the middle of the canvas, and
                    // whatever falls outside is clipped when it is drawn (3.0 E).
                    var w = naturalWidth * ((double)width / outputWidth);
                    var h = naturalHeight * ((double)height / outputHeight);
                    return new Rect((width - w) / 2.0, (height - h) / 2.0, w, h);
                }

            case FitMode.Contain:
                {
                    var scale = Math.Min((double)width / srcW, (double)height / srcH);
                    if (options.NoUpscale)
                    {
                        scale = Math.Min(scale, 1.0);
                    }

                    var w = srcW * scale;
                    var h = srcH * scale;
                    return new Rect((width - w) / 2, (height - h) / 2, w, h);
                }

            default:
                {
                    var scale = Math.Max((double)width / srcW, (double)height / srcH);
                    var w = srcW * scale;
                    var h = srcH * scale;
                    var x = -(w - width) * Math.Clamp(options.PanX, 0, 1);
                    var y = -(h - height) * Math.Clamp(options.PanY, 0, 1);
                    return new Rect(x, y, w, h);
                }
        }
    }

    public static BitmapSource Render(string sourcePath, FitOptions options, int width, int height) =>
        Render(LoadSource(sourcePath), options, width, height);

    public static BitmapSource Render(BitmapSource source, FitOptions options, int width, int height) =>
        Render(WorkingSource.Of(source), options, width, height, width, height);

    /// <summary>A preview render: the working copy drawn into a canvas that stands in for the 2048x1151 the save
    /// writes, so Center crops in the preview exactly where it will crop in the file (3.0 E).</summary>
    public static BitmapSource Render(WorkingSource source, FitOptions options, int width, int height) =>
        Render(source, options, width, height, OutputWidth, OutputHeight);

    public static BitmapSource Render(WorkingSource source, FitOptions options, int width, int height, int outputWidth, int outputHeight)
    {
        var bitmap = source.Bitmap;
        var dest = DestinationRect(
            bitmap.PixelWidth,
            bitmap.PixelHeight,
            options,
            width,
            height,
            source.NaturalWidth,
            source.NaturalHeight,
            outputWidth,
            outputHeight);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawImage(bitmap, dest);
            var darken = Math.Clamp(options.Darken, 0, 1);
            if (darken > 0)
            {
                var shade = new SolidColorBrush(Color.FromArgb((byte)Math.Round(darken * 255), 0, 0, 0));
                shade.Freeze();
                dc.DrawRectangle(shade, null, new Rect(0, 0, width, height));
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var rgb = new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
        rgb.Freeze();
        return rgb;
    }

    /// <summary>Full-size render encoded as JPEG bytes at quality 90.</summary>
    public static byte[] Fit(string sourcePath, FitOptions options)
    {
        var rendered = Render(sourcePath, options, OutputWidth, OutputHeight);
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(rendered));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
