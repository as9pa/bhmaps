using System.Windows;
using System.Windows.Media;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>A piece about to be written faded: its path relative to the map art root, its pixel size, the straight
/// alpha of every pixel before fading (row by row), and the opacity it is written at (0..1).</summary>
public sealed record SeamPiece(string RelativePath, int Width, int Height, byte[] Alpha, double Opacity);

/// <summary>Spec 3.2 O1: faded pieces that overlap would otherwise draw twice at partial strength and show bright
/// seams. The mask makes each lower piece more transparent exactly where faded pieces above it cover it, so the
/// stack composites like one layer. Safe to call from any thread.</summary>
public static class SeamMask
{
    /// <summary>Two uses of a piece agree on a pixel when their scales differ by less than this, well under one
    /// alpha level.</summary>
    private const float Agreement = 0.5f / 255;

    /// <summary>The written alpha (0..1) of a piece with native alpha <paramref name="s"/> at opacity
    /// <paramref name="a"/>, under the pieces <paramref name="above"/> listed top first:
    /// wanted = a * s * prod(1 - s_j), written = wanted / prod(1 - written_j).</summary>
    public static double Written(double s, double a, IReadOnlyList<(double S, double A)> above)
    {
        var native = 1.0;
        var written = 1.0;
        foreach (var (sj, aj) in above)
        {
            var wj = aj * sj * native / written;
            native *= 1 - sj;
            written *= 1 - wj;
            if (written <= 0)
            {
                // A solid piece at full strength above hides this one completely, so it keeps today's value.
                return a * s;
            }
        }

        return a * s * native / written;
    }

    /// <summary>Per piece, the factor each pixel's alpha is multiplied by instead of the plain opacity, row by row.
    /// A piece is only corrected where every use of it in <paramref name="levels"/> agrees, and pixels where the
    /// uses disagree keep the plain opacity. Only faded pieces are passed in: a piece at full strength above
    /// another changes nothing in the formula. A piece with nothing to correct is left out of the result.</summary>
    public static IReadOnlyDictionary<string, float[]> Compute(IEnumerable<LevelDesc> levels, IReadOnlyList<SeamPiece> pieces)
    {
        var byPath = new Dictionary<string, SeamPiece>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in pieces)
        {
            if (piece.Width > 0 && piece.Height > 0 && piece.Alpha.Length >= piece.Width * piece.Height)
            {
                byPath[piece.RelativePath] = piece;
            }
        }

        var uses = new Dictionary<SeamPiece, List<float[]>>();
        if (byPath.Count == 0)
        {
            return new Dictionary<string, float[]>();
        }

        foreach (var level in levels)
        {
            var placements = new List<Placement>();
            foreach (var placed in MapCompositor.Walk(level))
            {
                if (byPath.TryGetValue(placed.RelativePath, out var piece))
                {
                    placements.Add(Placement.Of(piece, placed));
                }
            }

            for (var k = 0; k < placements.Count; k++)
            {
                var own = placements[k];
                var above = new List<Placement>();
                for (var j = placements.Count - 1; j > k; j--)
                {
                    var other = placements[j];
                    if (other.Group == own.Group && other.Bounds.IntersectsWith(own.Bounds))
                    {
                        above.Add(other);
                    }
                }

                if (!uses.TryGetValue(own.Piece, out var list))
                {
                    uses[own.Piece] = list = [];
                }

                list.Add(Scale(own, above));
            }
        }

        var result = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (piece, list) in uses)
        {
            var plain = (float)Math.Clamp(piece.Opacity, 0, 1);
            var merged = list[0];
            for (var i = 1; i < list.Count; i++)
            {
                var next = list[i];
                for (var p = 0; p < merged.Length; p++)
                {
                    if (Math.Abs(merged[p] - next[p]) >= Agreement)
                    {
                        merged[p] = plain;
                    }
                }
            }

            if (merged.Any(v => Math.Abs(v - plain) >= Agreement))
            {
                result[piece.RelativePath] = merged;
            }
        }

        return result;
    }

    /// <summary>One use of a piece: the matrix from its pixels to level space and back, and its level-space
    /// bounds.</summary>
    private sealed record Placement(SeamPiece Piece, Matrix ToLevel, Matrix FromLevel, Rect Bounds, int Group)
    {
        public static Placement Of(SeamPiece piece, PlacedAsset placed)
        {
            var toLevel = MapCompositor.AssetTransform(placed.Asset, piece.Width, piece.Height);
            toLevel.Append(placed.Transform);
            var fromLevel = toLevel;
            if (fromLevel.HasInverse)
            {
                fromLevel.Invert();
            }

            var bounds = new Rect(0, 0, piece.Width, piece.Height);
            bounds.Transform(toLevel);
            return new Placement(piece, toLevel, fromLevel, bounds, placed.Group);
        }

        /// <summary>The native alpha (0..1) of the pixel this level point falls on, 0 outside the piece.</summary>
        public double AlphaAt(Point level)
        {
            if (!ToLevel.HasInverse)
            {
                return 0;
            }

            var local = FromLevel.Transform(level);
            var x = (int)Math.Floor(local.X);
            var y = (int)Math.Floor(local.Y);
            return x < 0 || y < 0 || x >= Piece.Width || y >= Piece.Height
                ? 0
                : Piece.Alpha[(y * Piece.Width) + x] / 255.0;
        }
    }

    /// <summary>The alpha factor of every pixel of one use: the plain opacity where nothing faded covers it, the
    /// corrected written alpha over the native alpha where something does.</summary>
    private static float[] Scale(Placement own, List<Placement> above)
    {
        var piece = own.Piece;
        var plain = Math.Clamp(piece.Opacity, 0, 1);
        var scale = new float[piece.Width * piece.Height];
        Array.Fill(scale, (float)plain);
        if (above.Count == 0)
        {
            return scale;
        }

        var covering = new List<(double S, double A)>(above.Count);
        for (var y = 0; y < piece.Height; y++)
        {
            for (var x = 0; x < piece.Width; x++)
            {
                var index = (y * piece.Width) + x;
                var s = piece.Alpha[index] / 255.0;
                if (s == 0)
                {
                    continue;
                }

                var point = own.ToLevel.Transform(new Point(x + 0.5, y + 0.5));
                covering.Clear();
                foreach (var other in above)
                {
                    if (other.Bounds.Contains(point) && other.AlphaAt(point) is > 0 and var sj)
                    {
                        covering.Add((sj, Math.Clamp(other.Piece.Opacity, 0, 1)));
                    }
                }

                if (covering.Count > 0)
                {
                    scale[index] = (float)Math.Clamp(Written(s, plain, covering) / s, 0, plain);
                }
            }
        }

        return scale;
    }
}
