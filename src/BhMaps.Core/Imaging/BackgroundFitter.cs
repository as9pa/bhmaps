using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

public enum FitMode
{
    Cover,
    Contain,
    Stretch,
}

/// <summary>PanX/PanY are 0..1 and only matter for Cover (0.5 = centered). Darken is 0..1 and multiplies every channel by (1 - Darken). NoUpscale only matters for Contain: a source smaller than the canvas stays at 1:1 instead of growing.</summary>
public sealed record FitOptions(FitMode Mode = FitMode.Cover, double PanX = 0.5, double PanY = 0.5, double Darken = 0.0, bool NoUpscale = false);

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

    /// <summary>Where the scaled source lands inside a width x height canvas. Cover rects overflow the canvas; the overflow is clipped when drawn.</summary>
    public static Rect DestinationRect(int srcW, int srcH, FitOptions options, int width, int height)
    {
        switch (options.Mode)
        {
            case FitMode.Stretch:
                return new Rect(0, 0, width, height);
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

    public static BitmapSource Render(BitmapSource source, FitOptions options, int width, int height)
    {
        var dest = DestinationRect(source.PixelWidth, source.PixelHeight, options, width, height);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawImage(source, dest);
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
