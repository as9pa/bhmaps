using System.Windows;
using System.Windows.Media;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>A piece about to be written faded: its path relative to the map art root, its pixel size, the straight
/// alpha of every pixel before fading (row by row), and the opacity it is written at (0..1).</summary>
public sealed record SeamPiece(string RelativePath, int Width, int Height, byte[] Alpha, double Opacity);

/// <summary>Spec 3.2 O1: faded pieces that overlap would otherwise draw twice at partial strength and show bright
/// seams. The mask makes each lower piece more transparent exactly where faded pieces above it cover it, so the
/// stack composites like one layer. Moving platform pieces join in at their rest position like any other piece:
/// while a platform moves away in game, the art it leaves uncovered shows the plain fade. Safe to call from any
/// thread.</summary>
public static class SeamMask
{
    /// <summary>A factor this close to the plain opacity is the plain opacity, well under one alpha level.</summary>
    private const float Agreement = 0.5f / 255;

    /// <summary>An error in the composited alpha this small does not show: under the seam audit's tolerance of 3
    /// levels with room for rounding the written alpha to a byte.</summary>
    private const double Unseen = 2.0 / 255;

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
    /// Every use of a piece in <paramref name="levels"/> wants its own factor on a pixel; where they differ the
    /// piece gets the value that leaves the fewest uses with a visible error (see <see cref="Merge"/>), and where
    /// one use draws the same file pixel on top of itself (a flipped copy meeting in the middle) the value is
    /// solved exactly. Only faded pieces are passed in: a piece at full strength above another changes nothing in
    /// the formula. A piece with nothing to correct is left out of the result.
    /// <para>With <paramref name="preferred"/>, the name of the layout open in the editor, that layout's uses win:
    /// wherever one of them sees a pixel, the value is merged over its uses alone, so they get it exactly where they
    /// agree, and the other layouts' uses only break ties among them or decide the pixels the preferred layout does
    /// not see. <paramref name="wrongElsewhere"/> then gets, per piece, how many pixels the result leaves visibly
    /// wrong under a use in another layout, counted once per use; a piece with none is left out.</para></summary>
    public static IReadOnlyDictionary<string, float[]> Compute(
        IEnumerable<LevelDesc> levels,
        IReadOnlyList<SeamPiece> pieces,
        string? preferred = null,
        IDictionary<string, long>? wrongElsewhere = null)
    {
        var byPath = new Dictionary<string, SeamPiece>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in pieces)
        {
            if (piece.Width > 0 && piece.Height > 0 && piece.Alpha.Length >= piece.Width * piece.Height)
            {
                byPath[piece.RelativePath] = piece;
            }
        }

        var uses = new Dictionary<SeamPiece, List<Want>>();
        if (byPath.Count == 0)
        {
            return new Dictionary<string, float[]>();
        }

        foreach (var level in levels)
        {
            var isPreferred = preferred != null && string.Equals(level.LevelName, preferred, StringComparison.OrdinalIgnoreCase);
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
                    if (other.Bounds.IntersectsWith(own.Bounds))
                    {
                        above.Add(other);
                    }
                }

                var selfBelow = new List<Placement>();
                for (var j = 0; j < k; j++)
                {
                    var other = placements[j];
                    if (other.Piece == own.Piece && other.Bounds.IntersectsWith(own.Bounds))
                    {
                        selfBelow.Add(other);
                    }
                }

                if (!uses.TryGetValue(own.Piece, out var list))
                {
                    uses[own.Piece] = list = [];
                }

                list.Add(Wanted(own, above, selfBelow) with { Preferred = isPreferred });
            }
        }

        var result = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (piece, list) in uses)
        {
            var plain = (float)Math.Clamp(piece.Opacity, 0, 1);
            var merged = Merge(piece, plain, list, out var wrong);
            if (merged.Any(v => Math.Abs(v - plain) >= Agreement))
            {
                result[piece.RelativePath] = merged;
            }

            if (preferred != null && wrong > 0 && wrongElsewhere != null)
            {
                wrongElsewhere[piece.RelativePath] = wrong;
            }
        }

        return result;
    }

    /// <summary>What one use of a piece wants of every pixel: the factor, and the light that reaches the piece
    /// there through the faded pieces above it (1 uncovered, 0 hidden, below 0 where another draw of the same
    /// file pixel already carries this one, see <see cref="SolveSelf"/>), and whether it is a use in the preferred
    /// layout.</summary>
    private sealed record Want(float[] Factor, float[] Through, bool Preferred = false);

    /// <summary>The merged factor of every pixel over the uses of a piece. Moving this piece's written alpha at a
    /// pixel by d moves the composite under one use by d times the light T reaching the piece there, and the native
    /// alpha s is the same for every use, so a factor x shows under a use as an error of T * s * |x - wanted|. A
    /// least-squares mean of the wanted factors spreads one use's error over all of them, which in the seam audit
    /// left more pixels visibly wrong than the plain fade did; so the merge picks the value that leaves the fewest
    /// uses with an error over <see cref="Unseen"/>, ties going to the uses the more light reaches. Among the uses
    /// it satisfies it takes the least squared error, the mean weighted by T^2, kept inside the range every one of
    /// them accepts. Uses that agree get their value exactly, a slight disagreement gets a middle value no use
    /// shows, and a wide one gets the value the most uses want. Each use counts once. Where a use of the preferred
    /// layout sees the pixel (some light reaches it), only the preferred uses choose and are averaged, and the other
    /// uses they leave satisfied break a tie between candidates. <paramref name="wrongElsewhere"/> counts the pixels
    /// the result leaves visibly wrong under the uses outside the preferred layout, once per use.</summary>
    private static float[] Merge(SeamPiece piece, float plain, List<Want> uses, out long wrongElsewhere)
    {
        wrongElsewhere = 0;
        var merged = new float[piece.Width * piece.Height];
        var low = new double[uses.Count];
        var high = new double[uses.Count];
        for (var p = 0; p < merged.Length; p++)
        {
            var s = piece.Alpha[p] / 255.0;
            float? first = null;
            var agree = true;
            foreach (var use in uses)
            {
                if (use.Through[p] < 0)
                {
                    continue;
                }

                first ??= use.Factor[p];
                agree &= Math.Abs(use.Factor[p] - first.Value) < Agreement;
            }

            if (agree || s == 0)
            {
                merged[p] = first ?? plain;
                continue;
            }

            for (var u = 0; u < uses.Count; u++)
            {
                var through = uses[u].Through[p];
                var slack = through > 0 ? Unseen / (through * s) : double.PositiveInfinity;
                low[u] = through < 0 ? double.NaN : uses[u].Factor[p] - slack;
                high[u] = through < 0 ? double.NaN : uses[u].Factor[p] + slack;
            }

            // S3: the map open in the editor wins wherever one of its uses sees this pixel.
            var preferredOnly = uses.Exists(u => u.Preferred && u.Through[p] > 0);
            var best = (Count: -1, Weight: -1.0, Others: -1, At: 0.0);
            for (var c = 0; c < uses.Count; c++)
            {
                if (double.IsNaN(low[c]) || (preferredOnly && !uses[c].Preferred))
                {
                    continue;
                }

                var at = double.IsNegativeInfinity(low[c]) ? uses[c].Factor[p] : low[c];
                var count = 0;
                var weight = 0.0;
                var others = 0;
                for (var u = 0; u < uses.Count; u++)
                {
                    if (!(low[u] <= at && at <= high[u]))
                    {
                        continue;
                    }

                    if (preferredOnly && !uses[u].Preferred)
                    {
                        others++;
                        continue;
                    }

                    count++;
                    weight += uses[u].Through[p] * uses[u].Through[p];
                }

                if (count > best.Count
                    || (count == best.Count && weight > best.Weight)
                    || (count == best.Count && weight == best.Weight && others > best.Others))
                {
                    best = (count, weight, others, at);
                }
            }

            double sum = 0, total = 0, floor = double.NegativeInfinity, ceiling = double.PositiveInfinity;
            for (var u = 0; u < uses.Count; u++)
            {
                if (low[u] <= best.At && best.At <= high[u] && (uses[u].Preferred || !preferredOnly))
                {
                    var w = uses[u].Through[p] * uses[u].Through[p];
                    sum += w * uses[u].Factor[p];
                    total += w;
                    floor = Math.Max(floor, low[u]);
                    ceiling = Math.Min(ceiling, high[u]);
                }
            }

            var value = total > 0 ? sum / total : best.At;
            merged[p] = (float)Math.Clamp(Math.Clamp(value, floor, ceiling), 0, plain);
            for (var u = 0; u < uses.Count; u++)
            {
                if (!uses[u].Preferred && uses[u].Through[p] > 0 && !(low[u] <= merged[p] && merged[p] <= high[u]))
                {
                    wrongElsewhere++;
                }
            }
        }

        return merged;
    }

    /// <summary>A piece covering a pixel: its native alpha and opacity, and whether it is the same file pixel drawn
    /// again, whose written alpha is the one being solved for.</summary>
    private readonly record struct Cover(double S, double A, bool Self);

    /// <summary>One use of a piece: the matrix from its pixels to level space and back, and its level-space
    /// bounds.</summary>
    private sealed record Placement(SeamPiece Piece, Matrix ToLevel, Matrix FromLevel, Rect Bounds)
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
            return new Placement(piece, toLevel, fromLevel, bounds);
        }

        /// <summary>The native alpha (0..1) of the pixel this level point falls on, 0 outside the piece.</summary>
        public double AlphaAt(Point level) => IndexAt(level) is >= 0 and var index ? Piece.Alpha[index] / 255.0 : 0;

        /// <summary>The index of the pixel this level point falls on, -1 outside the piece.</summary>
        public int IndexAt(Point level)
        {
            if (!ToLevel.HasInverse)
            {
                return -1;
            }

            var local = FromLevel.Transform(level);
            var x = (int)Math.Floor(local.X);
            var y = (int)Math.Floor(local.Y);
            return x < 0 || y < 0 || x >= Piece.Width || y >= Piece.Height ? -1 : (y * Piece.Width) + x;
        }
    }

    /// <summary>What one use wants of every pixel: the plain opacity where nothing faded covers it, the corrected
    /// written alpha over the native alpha where something does. Where another use of the same piece above this
    /// one draws the same file pixel at this point, both draws carry this pixel's value, so what it wants depends on
    /// itself. That pixel is solved exactly (see <see cref="SolveSelf"/>), and the upper draw wants nothing of its
    /// own there: its share is in the solve.</summary>
    private static Want Wanted(Placement own, List<Placement> above, List<Placement> selfBelow)
    {
        var piece = own.Piece;
        var plain = Math.Clamp(piece.Opacity, 0, 1);
        var factor = new float[piece.Width * piece.Height];
        var light = new float[factor.Length];
        Array.Fill(factor, (float)plain);
        Array.Fill(light, 1f);
        if (above.Count == 0 && selfBelow.Count == 0)
        {
            return new Want(factor, light);
        }

        var covering = new List<Cover>(above.Count);
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
                if (selfBelow.Exists(b => b.Bounds.Contains(point) && b.IndexAt(point) == index))
                {
                    light[index] = -1;
                    continue;
                }

                covering.Clear();
                var self = false;
                foreach (var other in above)
                {
                    if (other.Bounds.Contains(point) && other.AlphaAt(point) is > 0 and var sj)
                    {
                        var same = other.Piece == piece && other.IndexAt(point) == index;
                        self |= same;
                        covering.Add(new Cover(sj, Math.Clamp(other.Piece.Opacity, 0, 1), same));
                    }
                }

                if (covering.Count == 0)
                {
                    continue;
                }

                var written = self ? SolveSelf(s, plain, covering) : Stack(s, plain, covering, 0).Written;
                factor[index] = (float)Math.Clamp(written / s, 0, plain);
                light[index] = (float)Stack(s, plain, covering, written).Through;
            }
        }

        return new Want(factor, light);
    }

    /// <summary>The written alpha this piece wants under <paramref name="above"/> (top first), and the light that
    /// reaches it through them. Other pieces are taken as corrected to their own wanted weight, as in
    /// <see cref="Written"/>. A <see cref="Cover.Self"/> entry is written at <paramref name="x"/>, and whatever it
    /// gives this file pixel's colour more or less than one layer would is taken off or added to this draw.</summary>
    private static (double Written, double Through) Stack(double s, double a, List<Cover> above, double x)
    {
        var native = 1.0;
        var through = 1.0;
        var owed = 0.0;
        foreach (var (sj, aj, self) in above)
        {
            var wanted = aj * sj * native;
            var wj = self ? x : wanted / through;
            if (self)
            {
                owed += wanted - (x * through);
            }

            native *= 1 - sj;
            through *= 1 - wj;
            if (through <= 0)
            {
                // A solid piece at full strength above hides this one completely, so it keeps today's value.
                return (a * s, 0);
            }
        }

        return (((a * s * native) + owed) / through, through);
    }

    /// <summary>The written alpha x of a file pixel drawn again above itself at the same point, such that all its
    /// draws together give its colour the weight one layer would: the fixed point x = Stack(x). Iterating does not
    /// settle there (for two draws the slope is -1), so x - Stack(x), which runs from below zero at 0 to at least
    /// zero at the plain a * s, is bisected; 40 halvings put x far under one alpha level.</summary>
    private static double SolveSelf(double s, double a, List<Cover> above)
    {
        double low = 0, high = a * s;
        if (high - Stack(s, a, above, high).Written <= 0)
        {
            return high;
        }

        for (var i = 0; i < 40; i++)
        {
            var mid = (low + high) / 2;
            if (mid - Stack(s, a, above, mid).Written < 0)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (low + high) / 2;
    }
}
