using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>One drawn instance of a piece: the asset rectangle in the node's own space and the matrix that
/// takes it into camera space (scale, rotate, translate, child inside parent, as the compositor draws).</summary>
public sealed record Placement(Rect Local, Matrix Transform)
{
    /// <summary>Area in camera space, for picking the largest instance. The matrix's determinant is the factor it
    /// multiplies an area by, so a turned piece counts its own area rather than its wider bounding box.</summary>
    public double Area =>
        Math.Abs(Local.Width * Local.Height * ((Transform.M11 * Transform.M22) - (Transform.M12 * Transform.M21)));

    /// <summary>The camera-space bounding box.</summary>
    public Rect Bounds => Rect.Transform(Local, Transform);
}

/// <summary>One picture laid across every platform of a map and cut into per-piece bitmaps (spec 6.1). The
/// platforms' own bounding box is the stage, the picture is cover-fitted into it once, and each placed piece takes
/// the part of it that falls under the piece, carried back into the piece's own pixels and masked by the piece's
/// alpha. Two pieces side by side then carry on one picture instead of each repeating it.</summary>
public static class SpanFitter
{
    /// <summary>Every unthemed placement of relativePath on the level, in draw order. Empty when the level never
    /// places the piece. An asset whose W or H is 0 uses the piece's own pixel size, which the caller passes.</summary>
    public static IReadOnlyList<Placement> Placements(
        LevelDesc level, string relativePath, int pieceWidth, int pieceHeight)
    {
        var placements = new List<Placement>();
        foreach (var node in level.Platforms)
        {
            PlatformBounds.Walk(node, Matrix.Identity, (drawn, matrix) =>
            {
                foreach (var asset in drawn.Assets)
                {
                    var resolved = AssetPath.Resolve(level.AssetDir, asset.AssetName);
                    if (!string.Equals(resolved, relativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // A negative W or H is a flip, and its size is taken the way PlatformBounds.Union takes it, so
                    // the placements stay inside the box the same walk produced.
                    var w = asset.W != 0 ? Math.Abs(asset.W) : pieceWidth;
                    var h = asset.H != 0 ? Math.Abs(asset.H) : pieceHeight;
                    placements.Add(new Placement(new Rect(asset.X, asset.Y, w, h), matrix));
                }
            });
        }

        return placements;
    }

    /// <summary>The platform box: the plain union of the level's unthemed platform assets, so no padding and no
    /// aspect growth. Null when the level has no unthemed platform asset.</summary>
    public static Rect? Box(LevelDesc level)
    {
        var box = PlatformBounds.Union(level, null);
        return box.IsEmpty ? null : box;
    }

    /// <summary>The largest placement by camera-space area, or null for an empty list.</summary>
    public static Placement? Largest(IReadOnlyList<Placement> placements) =>
        placements.Count == 0 ? null : placements.MaxBy(placement => placement.Area);

    /// <summary>The picture cover fitted into box with the pan, then the part under the placement cut back into
    /// the piece's own pixels (undoing scale, flip and rotation) and masked by the piece's alpha. Frozen Bgra32 at
    /// the piece's size and DPI.</summary>
    public static BitmapSource Cut(
        BitmapSource picture, Rect box, FitOptions pan, Placement placement, BitmapSource piece) =>
        Cut(picture, box, pan, placement, piece, piece);

    /// <summary>The same, with the art Fit and Center leave showing on the part of the piece the picture does not
    /// reach (3.2 F1). original is the piece's own art at the piece's size; the shape still comes from piece.</summary>
    public static BitmapSource Cut(
        BitmapSource picture, Rect box, FitOptions pan, Placement placement, BitmapSource piece, BitmapSource original)
    {
        var width = piece.PixelWidth;
        var height = piece.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var pictureRect = PictureRect(picture, box, pan);
        Transform? toPiece = null;

        // Piece pixels to the node's own space, then the node's space to camera space. Drawing under its inverse
        // puts the camera-space picture back into the piece's pixels in one pass, with no per-pixel loop.
        var full = Matrix.Multiply(PixelToLocal(placement, width, height), placement.Transform);
        if (full.HasInverse)
        {
            full.Invert();
            toPiece = new MatrixTransform(full);

            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(toPiece);
                dc.DrawImage(picture, pictureRect);
                dc.Pop();
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, stride, 0);
        }

        // A placement scaled to nothing draws nothing, and leaves the transparent buffer as it is.
        PieceFitter.MaskBy(pixels, piece);
        if (PieceFitter.KeepsOriginal(pan))
        {
            // A placement scaled to nothing is covered nowhere, so it keeps its original art whole.
            var coverage = toPiece is null
                ? new byte[width * height]
                : PieceFitter.Coverage(width, height, pictureRect, toPiece);
            PieceFitter.FillUncovered(pixels, coverage, original, piece);
        }

        var result = new WriteableBitmap(width, height, piece.DpiX, piece.DpiY, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        result.Freeze();
        return result;
    }

    /// <summary>The whole stage for the preview: the picture cover fitted into box, drawn as one bitmap the size of
    /// the box at scale pixels per unit. Used by the editor's joined preview when it needs the layer itself.</summary>
    public static BitmapSource Fitted(BitmapSource picture, Rect box, FitOptions pan, double scale)
    {
        var width = Pixels(box.Width * scale);
        var height = Pixels(box.Height * scale);
        var dest = BackgroundFitter.DestinationRect(picture.PixelWidth, picture.PixelHeight, pan, width, height);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(picture, dest);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>Where the cover-fitted picture lands in camera space: the fit into the box's own size, moved to the
    /// box's corner. The fit is asked for in whole pixels, which on a box hundreds of units wide moves an edge by
    /// less than half a unit.</summary>
    private static Rect PictureRect(BitmapSource picture, Rect box, FitOptions pan)
    {
        var dest = BackgroundFitter.DestinationRect(
            picture.PixelWidth, picture.PixelHeight, pan, Pixels(box.Width), Pixels(box.Height));
        return new Rect(box.X + dest.X, box.Y + dest.Y, dest.Width, dest.Height);
    }

    /// <summary>The piece's pixel grid mapped onto its rectangle in the node's own space.</summary>
    private static Matrix PixelToLocal(Placement placement, int width, int height)
    {
        var matrix = Matrix.Identity;
        matrix.Scale(placement.Local.Width / width, placement.Local.Height / height);
        matrix.Translate(placement.Local.X, placement.Local.Y);
        return matrix;
    }

    /// <summary>A canvas is never zero pixels wide, however small the box it came from.</summary>
    private static int Pixels(double units) => Math.Max(1, (int)Math.Round(units, MidpointRounding.AwayFromZero));
}
