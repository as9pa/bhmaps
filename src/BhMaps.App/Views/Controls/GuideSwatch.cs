using System.Windows;
using System.Windows.Media;
using BhMaps.Core.Imaging;

namespace BhMaps.App.Views.Controls;

/// <summary>Spec G4: one legend line's sample, 24 by 10, drawn with the pen <see cref="GuidesOverlay"/> uses for
/// <see cref="Layer"/>: a short line for the boxes and every hitbox layer, and a small hatched band for the
/// overlaps. Snapped to device pixels like the stage's lines, so a dotted sample reads the same as the real
/// one.</summary>
public sealed class GuideSwatch : FrameworkElement
{
    private const double SwatchWidth = 24;
    private const double SwatchHeight = 10;

    public static readonly DependencyProperty LayerProperty =
        DependencyProperty.Register(
            nameof(Layer),
            typeof(GuideLayer),
            typeof(GuideSwatch),
            new FrameworkPropertyMetadata(GuideLayer.Hard, FrameworkPropertyMetadataOptions.AffectsRender));

    private GuidesOverlay.Palette? palette;

    public GuideSwatch()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    public GuideLayer Layer
    {
        get => (GuideLayer)GetValue(LayerProperty);
        set => SetValue(LayerProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(SwatchWidth, SwatchHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        palette ??= new GuidesOverlay.Palette(this);
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleY;

        if (Layer == GuideLayer.Overlaps)
        {
            // The band as the stage draws it: the wash, the hatch cut to it, and the 1 px edge.
            var band = new Rect(0.5, 0.5, SwatchWidth - 1, SwatchHeight - 1);
            drawingContext.PushClip(new RectangleGeometry(band));
            drawingContext.DrawRectangle(palette.OverlapFill, null, band);
            for (var c = 0.0; c < SwatchWidth + SwatchHeight; c += 6)
            {
                drawingContext.DrawLine(palette.Hatch, new Point(c, 0), new Point(c - SwatchHeight, SwatchHeight));
            }

            drawingContext.Pop();
            drawingContext.DrawRectangle(null, palette.OverlapEdge, band);
            return;
        }

        var y = (Math.Floor(SwatchHeight / 2 * dpi) + 0.5) / dpi;
        drawingContext.DrawLine(palette.LineOf(Layer), new Point(0, y), new Point(SwatchWidth, y));
    }
}
