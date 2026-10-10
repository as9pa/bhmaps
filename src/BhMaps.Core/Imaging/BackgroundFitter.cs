using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

public enum FitMode
{
    Cover,
    Contain,
    Stretch,
    Center,
}

/// <summary>PanX/PanY are 0..1 and only matter for Cover (0.5 = centered). Darken is 0..1 and multiplies every channel by (1 - Darken). NoUpscale only matters for Contain: a source smaller than the canvas stays at 1:1 instead of growing.
/// Hue is a rotation in degrees; Saturation and Contrast are -1..1 with 0 leaving the picture alone; Blur is 0..1 (3.6 E4).
/// Zoom is 1..4 and, like the pan, only matters for Cover: it multiplies the cover fit (see <see cref="PanZoom"/>).</summary>
public sealed record FitOptions(
    FitMode Mode = FitMode.Cover,
    double PanX = 0.5,
    double PanY = 0.5,
    double Darken = 0.0,
    bool NoUpscale = false,
    double Hue = 0.0,
    double Saturation = 0.0,
    double Contrast = 0.0,
    double Blur = 0.0,
    double Zoom = 1.0);

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
                    return PanZoom.Cover(srcW, srcH, width, height, options.Zoom, options.PanX, options.PanY);
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
        var darken = Math.Clamp(options.Darken, 0, 1);
        var blur = Math.Clamp(options.Blur, 0, 1);
        var adjust = ColorMath.Wrap(options.Hue) != 0 || options.Saturation != 0 || options.Contrast != 0;

        // With no blur and no colour pass the shade goes in the same draw as the picture, exactly as before 3.6.
        var shadeInDraw = blur == 0 && !adjust;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawImage(bitmap, dest);
            if (shadeInDraw && darken > 0)
            {
                var shade = new SolidColorBrush(Color.FromArgb(ShadeAlpha(darken), 0, 0, 0));
                shade.Freeze();
                dc.DrawRectangle(shade, null, new Rect(0, 0, width, height));
            }
        }

        BitmapSource target = RenderVisual(visual, width, height);
        if (blur > 0)
        {
            target = Blurred(target, blur * 40 * ((double)width / outputWidth), width, height);
        }

        if (adjust || (!shadeInDraw && darken > 0))
        {
            target = Adjusted(target, options, shadeInDraw ? 0 : darken, width, height);
        }

        var rgb = new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
        rgb.Freeze();
        return rgb;
    }

    private static byte ShadeAlpha(double darken) => (byte)Math.Round(darken * 255);

    private static RenderTargetBitmap RenderVisual(Visual visual, int width, int height)
    {
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    /// <summary>The fitted frame under a Gaussian BlurEffect. The frame is drawn with mirrored copies around it, out
    /// to a little past the radius, so the blur near the canvas edge mixes in picture rather than fading to
    /// transparent (which the Bgr24 output would turn into a dark rim).</summary>
    internal static BitmapSource Blurred(BitmapSource frame, double radius, int width, int height)
    {
        var pad = Math.Ceiling(radius) + 2;
        var visual = new DrawingVisual { Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian } };
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.PushClip(new RectangleGeometry(new Rect(-pad, -pad, width + (2 * pad), height + (2 * pad))));
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    // dx = -1 flips the frame onto [-w, 0], +1 onto [w, 2w]; the same for dy.
                    var sx = dx == 0 ? 1 : -1;
                    var sy = dy == 0 ? 1 : -1;
                    var ox = dx == 1 ? 2.0 * width : 0;
                    var oy = dy == 1 ? 2.0 * height : 0;
                    dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, ox, oy));
                    dc.DrawImage(frame, new Rect(0, 0, width, height));
                    dc.Pop();
                }
            }

            dc.Pop();
        }

        return RenderVisual(visual, width, height);
    }

    /// <summary>Hue, saturation and contrast run over every pixel of the rendered frame, then the Darken shade. The
    /// frame is opaque, so its premultiplied bytes are its colour bytes.</summary>
    private static BitmapSource Adjusted(BitmapSource frame, FitOptions options, double darken, int width, int height)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        AdjustPixels(pixels, options.Hue, options.Saturation, options.Contrast, darken);

        var target = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        target.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        target.Freeze();
        return target;
    }

    /// <summary>The colour pass over a Bgra32 buffer of straight colour: hue and saturation in HSL, then contrast,
    /// then the Darken shade. Alpha is left as it is, so the platform editor runs the same pass over shaped pieces
    /// that the background editor runs over its opaque frame.</summary>
    internal static void AdjustPixels(byte[] pixels, double hueDegrees, double saturation, double contrast, double darken)
    {
        var shift = ColorMath.Wrap(hueDegrees);
        saturation = Math.Clamp(saturation, -1, 1);
        var hsl = shift != 0 || saturation != 0;
        contrast = Math.Clamp(contrast, -1, 1);
        var slope = ColorMath.ContrastSlope(contrast);
        var keep = 255 - ShadeAlpha(Math.Clamp(darken, 0, 1));
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            if (hsl)
            {
                var (h, s, l) = ColorMath.ToHsl(r, g, b);
                (r, g, b) = ColorMath.ToRgb((h + shift) % 360, ColorMath.Saturate(s, saturation), l);
            }

            if (contrast != 0)
            {
                r = ColorMath.Contrast(r, slope);
                g = ColorMath.Contrast(g, slope);
                b = ColorMath.Contrast(b, slope);
            }

            if (keep < 255)
            {
                r = (byte)Math.Round(r * keep / 255.0);
                g = (byte)Math.Round(g * keep / 255.0);
                b = (byte)Math.Round(b * keep / 255.0);
            }

            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
        }
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
