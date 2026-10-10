using System.Windows;

namespace BhMaps.Core.Imaging;

/// <summary>The Fill geometry every editor shares: the picture cover-fitted into a box, grown by a zoom of 1 to 4,
/// and placed by a pan of 0..1 along the part that hangs over the box (0.5 = centred). The drag and the wheel on a
/// preview go through here too, so a pan or zoom means the same thing in every window and on every save.</summary>
public static class PanZoom
{
    public const double MinZoom = 1.0;
    public const double MaxZoom = 4.0;

    /// <summary>One wheel notch grows or shrinks the picture by this much (10%).</summary>
    public const double WheelStep = 1.1;

    /// <summary>The zoom held to 1..4; anything that is not a number reads as 1.</summary>
    public static double ClampZoom(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, MinZoom, MaxZoom) : MinZoom;

    /// <summary>Where the picture lands in a boxWidth x boxHeight box, in the box's own units: the cover fit times
    /// the zoom, moved by the pan along whatever hangs over the box.</summary>
    public static Rect Cover(
        double sourceWidth, double sourceHeight, double boxWidth, double boxHeight, double zoom, double panX, double panY)
    {
        var scale = Math.Max(boxWidth / sourceWidth, boxHeight / sourceHeight) * ClampZoom(zoom);
        var w = sourceWidth * scale;
        var h = sourceHeight * scale;
        var x = -(w - boxWidth) * Math.Clamp(panX, 0, 1);
        var y = -(h - boxHeight) * Math.Clamp(panY, 0, 1);
        return new Rect(x, y, w, h);
    }

    /// <summary>How far the zoomed cover fit hangs over the box each way, which is the whole range the pan moves
    /// along. 0 on an axis means there is nothing to move that way.</summary>
    public static (double X, double Y) Overflow(
        double sourceWidth, double sourceHeight, double boxWidth, double boxHeight, double zoom)
    {
        var rect = Cover(sourceWidth, sourceHeight, boxWidth, boxHeight, zoom, 0.5, 0.5);
        return (Math.Max(0, rect.Width - boxWidth), Math.Max(0, rect.Height - boxHeight));
    }

    /// <summary>The pan after the picture is dragged by dx, dy box units: the picture follows the pointer, so a drag
    /// to the right lowers the pan. An axis with no overflow keeps its pan.</summary>
    public static (double PanX, double PanY) Drag(
        double sourceWidth,
        double sourceHeight,
        double boxWidth,
        double boxHeight,
        double zoom,
        double panX,
        double panY,
        double dx,
        double dy)
    {
        var (overflowX, overflowY) = Overflow(sourceWidth, sourceHeight, boxWidth, boxHeight, zoom);
        return (
            overflowX > 0 ? Math.Clamp(panX - (dx / overflowX), 0, 1) : panX,
            overflowY > 0 ? Math.Clamp(panY - (dy / overflowY), 0, 1) : panY);
    }

    /// <summary>The zoom and pan after zooming to newZoom about a point of the box (in box units), so the part of
    /// the picture under that point stays under it. The pan is clamped, so near an edge of the picture the point
    /// can slide a little rather than show the box behind the picture.</summary>
    public static (double Zoom, double PanX, double PanY) ZoomAbout(
        double sourceWidth,
        double sourceHeight,
        double boxWidth,
        double boxHeight,
        double zoom,
        double panX,
        double panY,
        double newZoom,
        double pointX,
        double pointY)
    {
        var before = Cover(sourceWidth, sourceHeight, boxWidth, boxHeight, zoom, panX, panY);
        var zoomed = ClampZoom(newZoom);
        var after = Cover(sourceWidth, sourceHeight, boxWidth, boxHeight, zoomed, 0.5, 0.5);

        // The share of the picture under the point, which has to land under the point again at the new size.
        var u = before.Width > 0 ? (pointX - before.X) / before.Width : 0.5;
        var v = before.Height > 0 ? (pointY - before.Y) / before.Height : 0.5;
        return (zoomed, PanFor(pointX - (u * after.Width), after.Width, boxWidth), PanFor(pointY - (v * after.Height), after.Height, boxHeight));
    }

    /// <summary>The pan that puts the picture's left (or top) edge at offset, for a picture that wide.</summary>
    private static double PanFor(double offset, double size, double box)
    {
        var overflow = size - box;
        return overflow > 0 ? Math.Clamp(-offset / overflow, 0, 1) : 0.5;
    }
}
