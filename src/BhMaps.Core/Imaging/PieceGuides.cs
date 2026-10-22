using System.Windows;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>The rectangle one drawn file covers, in level space. <see cref="Corners"/> are the file's own top-left,
/// top-right, bottom-right and bottom-left pixel corners taken through the asset and node transforms, so a rotated
/// piece gives a rotated quad and a flipped one keeps its corners mirrored. <see cref="Order"/> is the piece's place in
/// <see cref="MapCompositor.Walk"/>, which tells apart two uses of the same file.</summary>
public sealed record PieceOutline(string RelativePath, int Group, int Order, IReadOnlyList<Point> Corners);

/// <summary>Where two drawn pieces cover the same ground: <see cref="Polygon"/> in level space, the piece drawn
/// first as <see cref="Lower"/>, and <see cref="Width"/> and <see cref="Height"/> as the polygon's axis-aligned extent
/// in level units (Brawlhaven's two halves overlap 406 wide).</summary>
public sealed record PieceOverlap(
    PieceOutline Lower, PieceOutline Upper, IReadOnlyList<Point> Polygon, double Width, double Height);

/// <summary>Everything the editor's guides draw for one level, all in level space: map it to the stage with
/// <see cref="MapCompositor.StageTransform"/>.</summary>
public sealed record PieceGuideSet(
    IReadOnlyList<PieceOutline> Pieces, IReadOnlyList<PieceOverlap> Overlaps, IReadOnlyList<CollisionLine> Collisions);

/// <summary>Spec G1/G2/G5: the geometry behind the editor's piece boxes, overlap hatching and hitboxes. Pure geometry
/// of file rectangles: transparent pixels count as covered, and two uses of one file at the same spot overlap like
/// any other pair. Moving platforms are paired at their rest position, the position the stage shows, the same way
/// the seam mask pairs them. Safe to call from any thread.</summary>
public static class PieceGuides
{
    /// <summary>An intersection smaller than this, in square level units, is a shared edge or corner, not an
    /// overlap.</summary>
    private const double MinArea = 1e-6;

    /// <summary>The guides for <paramref name="level"/>. <paramref name="pixelSize"/> gives a file's pixel size by
    /// its path relative to mapArt, or null when the file is missing or unreadable; such a piece is not drawn, so it
    /// gets no box. A non-null <paramref name="only"/> keeps the boxes of the pieces it names and every overlap that
    /// involves one of them ("Selected only"); build it with <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// Collision lines pass through as the parser gave them, dynamic ones at rest, whatever the filter.</summary>
    public static PieceGuideSet Build(
        LevelDesc level, Func<string, (int Width, int Height)?> pixelSize, IReadOnlySet<string>? only = null)
    {
        var all = Outlines(level, pixelSize);
        var overlaps = Overlaps(all);
        if (only is not null)
        {
            all = [.. all.Where(p => only.Contains(p.RelativePath))];
            overlaps = [.. overlaps.Where(o => only.Contains(o.Lower.RelativePath) || only.Contains(o.Upper.RelativePath))];
        }

        return new PieceGuideSet(all, overlaps, level.Collisions);
    }

    /// <summary>The pixel sizes the compositor would draw with: each path resolved through
    /// <paramref name="sources"/> and read from the file's header.</summary>
    public static Func<string, (int Width, int Height)?> SizesFrom(AssetSources sources) =>
        relativePath => sources.ResolveAsset(relativePath) is { } path ? ImageDimensions.Read(path) : null;

    /// <summary>Every drawn piece's outline in draw order. Each file's size is asked for once.</summary>
    public static IReadOnlyList<PieceOutline> Outlines(LevelDesc level, Func<string, (int Width, int Height)?> pixelSize)
    {
        var sizes = new Dictionary<string, (int Width, int Height)?>(StringComparer.OrdinalIgnoreCase);
        var outlines = new List<PieceOutline>();
        var order = 0;
        foreach (var placed in MapCompositor.Walk(level))
        {
            var index = order++;
            if (!sizes.TryGetValue(placed.RelativePath, out var size))
            {
                size = pixelSize(placed.RelativePath);
                sizes[placed.RelativePath] = size;
            }

            if (size is not { Width: > 0, Height: > 0 } known)
            {
                continue;
            }

            var toLevel = MapCompositor.AssetTransform(placed.Asset, known.Width, known.Height);
            toLevel.Append(placed.Transform);
            Point[] corners =
            [
                toLevel.Transform(new Point(0, 0)),
                toLevel.Transform(new Point(known.Width, 0)),
                toLevel.Transform(new Point(known.Width, known.Height)),
                toLevel.Transform(new Point(0, known.Height)),
            ];
            outlines.Add(new PieceOutline(placed.RelativePath, placed.Group, index, corners));
        }

        return outlines;
    }

    /// <summary>Every pair of same-group outlines that share some area, lower piece first, in draw order of the
    /// lower piece then the upper.</summary>
    public static IReadOnlyList<PieceOverlap> Overlaps(IReadOnlyList<PieceOutline> pieces)
    {
        var bounds = pieces.Select(p => BoundsOf(p.Corners)).ToArray();
        var overlaps = new List<PieceOverlap>();
        for (var i = 0; i < pieces.Count; i++)
        {
            for (var j = i + 1; j < pieces.Count; j++)
            {
                if (!bounds[i].IntersectsWith(bounds[j]))
                {
                    continue;
                }

                var polygon = Intersect(pieces[i].Corners, pieces[j].Corners);
                if (polygon.Count < 3 || Math.Abs(SignedArea(polygon)) < MinArea)
                {
                    continue;
                }

                var extent = BoundsOf(polygon);
                overlaps.Add(new PieceOverlap(pieces[i], pieces[j], polygon, extent.Width, extent.Height));
            }
        }

        return overlaps;
    }

    /// <summary>The intersection of two convex polygons (Sutherland-Hodgman), either winding. Empty when they share
    /// no area or <paramref name="clip"/> has none.</summary>
    public static IReadOnlyList<Point> Intersect(IReadOnlyList<Point> subject, IReadOnlyList<Point> clip)
    {
        var area = SignedArea(clip);
        if (Math.Abs(area) < MinArea)
        {
            return [];
        }

        // Inside is the left of each edge for a positive (counter-clockwise in y-up terms) winding, the right otherwise.
        var sign = Math.Sign(area);
        var output = subject.ToList();
        for (var e = 0; e < clip.Count && output.Count > 0; e++)
        {
            var a = clip[e];
            var b = clip[(e + 1) % clip.Count];
            var input = output;
            output = [];
            for (var k = 0; k < input.Count; k++)
            {
                var current = input[k];
                var previous = input[(k + input.Count - 1) % input.Count];
                var currentSide = sign * Cross(a, b, current);
                var previousSide = sign * Cross(a, b, previous);
                if (currentSide >= 0)
                {
                    if (previousSide < 0)
                    {
                        output.Add(Crossing(a, b, previous, current));
                    }

                    output.Add(current);
                }
                else if (previousSide >= 0)
                {
                    output.Add(Crossing(a, b, previous, current));
                }
            }
        }

        // A subject corner that sits on a clip edge comes out twice, once as the crossing and once as itself.
        var distinct = new List<Point>(output.Count);
        foreach (var point in output)
        {
            if (distinct.Count == 0 || !Near(distinct[^1], point))
            {
                distinct.Add(point);
            }
        }

        if (distinct.Count > 1 && Near(distinct[0], distinct[^1]))
        {
            distinct.RemoveAt(distinct.Count - 1);
        }

        return distinct;
    }

    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;

    /// <summary>Positive when <paramref name="p"/> lies left of the line from <paramref name="a"/> to
    /// <paramref name="b"/>.</summary>
    private static double Cross(Point a, Point b, Point p) => ((b.X - a.X) * (p.Y - a.Y)) - ((b.Y - a.Y) * (p.X - a.X));

    /// <summary>Where the segment from <paramref name="p"/> to <paramref name="q"/> crosses the line through
    /// <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static Point Crossing(Point a, Point b, Point p, Point q)
    {
        var dp = Cross(a, b, p);
        var dq = Cross(a, b, q);
        var t = dp / (dp - dq);
        return new Point(p.X + ((q.X - p.X) * t), p.Y + ((q.Y - p.Y) * t));
    }

    private static double SignedArea(IReadOnlyList<Point> polygon)
    {
        var sum = 0.0;
        for (var k = 0; k < polygon.Count; k++)
        {
            var a = polygon[k];
            var b = polygon[(k + 1) % polygon.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2;
    }

    private static Rect BoundsOf(IReadOnlyList<Point> points)
    {
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        return new Rect(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
    }
}
