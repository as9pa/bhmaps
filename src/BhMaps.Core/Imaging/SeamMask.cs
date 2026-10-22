using System.Windows;
using System.Windows.Media;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>A piece about to be written faded: its path relative to the map art root, its pixel size, the straight
/// alpha of every pixel before fading (row by row), and the opacity it is written at (0..1).</summary>
public sealed record SeamPiece(string RelativePath, int Width, int Height, byte[] Alpha, double Opacity);

/// <summary>How far a piece above shifts against a piece below while the game runs (see
/// <see cref="SeamMask.Shifts"/>). Without <see cref="Samples"/>, <see cref="Offsets"/> are the corners of the hull
/// the shift stays inside, and the piece above covers a point only where it does at every one. With them,
/// <see cref="Offsets"/> are the distinct shifts at evenly spaced moments of one animation cycle, most held first,
/// and <see cref="Samples"/> how many moments show each; the piece above covers a point where it does for more than
/// half of the moments. No offsets: the two keep their places.</summary>
public sealed record MoverShifts(IReadOnlyList<Vector> Offsets, IReadOnlyList<int>? Samples = null)
{
    public static MoverShifts None { get; } = new([]);

    public int Count => Offsets.Count;

    /// <summary>How many moments <see cref="Samples"/> adds up to.</summary>
    public int Total { get; } = Samples?.Sum() ?? 0;

    /// <summary>The cycle of moments <paramref name="samples"/>, equal shifts counted together.</summary>
    public static MoverShifts Of(IEnumerable<Vector> samples)
    {
        var held = samples.GroupBy(s => s).OrderByDescending(g => g.Count()).ToList();
        return new MoverShifts(held.ConvertAll(g => g.Key), held.ConvertAll(g => g.Count()));
    }
}

/// <summary>Spec 3.2 O1: faded pieces that overlap would otherwise draw twice at partial strength and show bright
/// seams. The mask makes each lower piece more transparent exactly where faded pieces above it cover it, so the
/// stack composites like one layer. A moving platform's pieces correct the art below where they cover it for more
/// than half of their animation cycle, which leaves the art wrong for the least time: uncorrected it doubles up while
/// covered, corrected it shows a lighter patch while uncovered. Where a platform's timing is unclear they correct
/// only where they cover the art at every keyframe, and a platform whose keyframes turn it corrects nothing (see
/// <see cref="Shifts"/>). A platform that never leaves its rest position corrects like a still piece. Safe to call
/// from any thread.</summary>
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

    /// <summary>The native alpha (0..1) a piece of <paramref name="width"/> by <paramref name="height"/> pixels
    /// shows at <paramref name="local"/> in its own pixel space when drawn stretched (spec S4). The game and the
    /// preview (WPF's linear scaling) both blend the four nearest pixels: pixel centres sit at +0.5, and past the
    /// outer centres the edge pixel is held. 0 outside the piece, which the drawn quad does not reach.</summary>
    public static double Blended(byte[] alpha, int width, int height, Point local)
    {
        if (local.X < 0 || local.Y < 0 || local.X >= width || local.Y >= height)
        {
            return 0;
        }

        var u = local.X - 0.5;
        var v = local.Y - 0.5;
        var x0 = (int)Math.Floor(u);
        var y0 = (int)Math.Floor(v);
        var fx = u - x0;
        var fy = v - y0;
        int left = Math.Max(x0, 0),
            right = Math.Min(x0 + 1, width - 1),
            top = Math.Max(y0, 0),
            bottom = Math.Min(y0 + 1, height - 1);
        var upper = (alpha[(top * width) + left] * (1 - fx)) + (alpha[(top * width) + right] * fx);
        var lower = (alpha[(bottom * width) + left] * (1 - fx)) + (alpha[(bottom * width) + right] * fx);
        return ((upper * (1 - fy)) + (lower * fy)) / 255.0;
    }

    /// <summary>How far an upper piece riding on <paramref name="upper"/> can shift against a lower one riding on
    /// <paramref name="lower"/> while the game runs (see <see cref="PlacedAsset.Movers"/> and
    /// <see cref="MoverShifts"/>): none where the two keep their places, null where a platform between them turns. A
    /// platform both ride on moves them alike and drops out. When only one platform is left and its timing is clear,
    /// the shifts are its cycle (see <see cref="Mover.Cycle"/>), turned around when it carries the lower piece.
    /// Otherwise they are the corners of the hull of every relative offset: platforms that are not tied to each other
    /// in time count every pairing of their offsets, which can only make the hull larger than the real one, and
    /// keyframe motion stays inside the hull, so a convex footprint that covers a point at every corner covers it
    /// throughout.</summary>
    public static MoverShifts? Shifts(IReadOnlyList<Mover>? upper, IReadOnlyList<Mover>? lower)
    {
        upper ??= [];
        lower ??= [];
        var shared = 0;
        while (shared < upper.Count && shared < lower.Count && upper[shared].Group == lower[shared].Group)
        {
            shared++;
        }

        var apart = upper.Skip(shared).Select(m => (Mover: m, Sign: 1.0))
            .Concat(lower.Skip(shared).Select(m => (Mover: m, Sign: -1.0)))
            .ToList();
        if (apart.Exists(a => a.Mover.Offsets is null))
        {
            return null;
        }

        if (apart is [var (only, side)] && only.Cycle is { } cycle)
        {
            return cycle.Offsets.All(o => o == new Vector())
                ? MoverShifts.None
                : new MoverShifts(cycle.Offsets.Select(o => side * o).ToList(), cycle.Samples);
        }

        List<Vector> corners = [new Vector()];
        foreach (var (mover, sign) in apart)
        {
            corners = Hull(corners.SelectMany(c => mover.Offsets!.Select(o => c + (sign * o))));
        }

        return corners.TrueForAll(c => c == new Vector()) ? MoverShifts.None : new MoverShifts(corners);
    }

    /// <summary>Whether a piece of <paramref name="width"/> by <paramref name="height"/> pixels, taken from level
    /// space to its pixels by <paramref name="fromLevel"/>, counts as lying over the level point
    /// <paramref name="level"/> under <paramref name="shifts"/> (see <see cref="MoverShifts"/>; null never does): at
    /// every hull corner, or for more than half of a sampled cycle. That half is where correcting the art below shows
    /// it wrong for less of the time than leaving it: uncorrected it doubles up while covered, corrected it shows a
    /// lighter patch while uncovered.</summary>
    public static bool Covers(Matrix fromLevel, int width, int height, Point level, MoverShifts? shifts)
    {
        if (shifts is null)
        {
            return false;
        }

        var samples = shifts.Samples;
        int covered = 0, missed = 0;
        for (var i = 0; i < shifts.Offsets.Count; i++)
        {
            var local = fromLevel.Transform(level - shifts.Offsets[i]);
            var inside = local.X >= 0 && local.Y >= 0 && local.X < width && local.Y < height;
            if (samples is null)
            {
                if (!inside)
                {
                    return false;
                }

                continue;
            }

            // Offsets come most held first, so the answer is usually settled after a few.
            if (inside)
            {
                covered += samples[i];
                if (covered * 2 > shifts.Total)
                {
                    return true;
                }
            }
            else
            {
                missed += samples[i];
                if (missed * 2 >= shifts.Total)
                {
                    return false;
                }
            }
        }

        return samples is null;
    }

    /// <summary>The corners of the convex hull of <paramref name="points"/> (Andrew's monotone chain): the lower
    /// chain left to right, then the upper chain back. One or two points where they do not span an area.</summary>
    private static List<Vector> Hull(IEnumerable<Vector> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        var hull = new List<Vector>();
        foreach (var chain in new[] { sorted, Enumerable.Reverse(sorted).ToList() })
        {
            var start = hull.Count;
            foreach (var p in chain)
            {
                while (hull.Count >= start + 2
                    && Vector.CrossProduct(hull[^1] - hull[^2], p - hull[^2]) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            // Each chain's last point starts the other chain.
            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    /// <summary>Per piece, the factor each pixel's alpha is multiplied by instead of the plain opacity, row by row.
    /// Every use of a piece in <paramref name="levels"/> wants its own factor on a pixel; where they differ the
    /// piece gets the value that leaves the fewest uses with a visible error (see <see cref="Merge"/>), and where
    /// one use draws the same file pixel on top of itself (a flipped copy meeting in the middle) the value is
    /// solved exactly. Where copies of one file overlap at different file pixels (a half and its mirror sharing a
    /// strip), each pixel's value depends on its partner's, so those pixels are refined together until they settle
    /// (see <see cref="Refine"/>). Only faded pieces are passed in: a piece at full strength above another changes
    /// nothing in the formula. A piece with nothing to correct is left out of the result.
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
                var above = new List<(Placement Upper, MoverShifts Shifts)>();
                for (var j = placements.Count - 1; j > k; j--)
                {
                    var other = placements[j];
                    if (other.Bounds.IntersectsWith(own.Bounds) && Shifts(other.Movers, own.Movers) is { } shifts)
                    {
                        above.Add((other, shifts));
                    }
                }

                var selfBelow = new List<Placement>();
                for (var j = 0; j < k; j++)
                {
                    var other = placements[j];
                    if (other.Piece == own.Piece
                        && other.Bounds.IntersectsWith(own.Bounds)
                        && Shifts(own.Movers, other.Movers) is { Count: 0 })
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
            var merged = Merge(piece, plain, list);
            Refine(piece, plain, list, merged);
            KeepSeparateWhereNoWorse(piece, plain, list, merged);
            if (merged.Any(v => Math.Abs(v - plain) >= Agreement))
            {
                result[piece.RelativePath] = merged;
            }

            if (preferred != null && wrongElsewhere != null && CountWrongElsewhere(piece, list, merged) is > 0 and var wrong)
            {
                wrongElsewhere[piece.RelativePath] = wrong;
            }
        }

        return result;
    }

    /// <summary>What one use of a piece wants of every pixel: the factor, and the light that reaches the piece
    /// there through the faded pieces above it (1 uncovered, 0 hidden, below 0 where another draw of the same
    /// file pixel already carries this one, see <see cref="SolveSelf"/>, or where this draw lies over another
    /// draw of the file and leaves the value to it, see <see cref="Refine"/>), and whether it is a use in the preferred
    /// layout. It keeps the placement and its neighbours so the pixels in <see cref="Coupled"/>, whose value depends
    /// on another pixel of the same file, can be wanted again against the merged values.</summary>
    private sealed record Want(
        float[] Factor,
        float[] Through,
        Placement Own,
        List<(Placement Upper, MoverShifts Shifts)> Above,
        List<Placement> SelfBelow,
        int[] Coupled,
        int[] Deferred,
        bool Preferred = false);

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
    /// uses they leave satisfied break a tie between candidates.</summary>
    private static float[] Merge(SeamPiece piece, float plain, List<Want> uses)
    {
        var merged = new float[piece.Width * piece.Height];
        var low = new double[uses.Count];
        var high = new double[uses.Count];
        for (var p = 0; p < merged.Length; p++)
        {
            merged[p] = MergeAt(piece, plain, uses, p, low, high);
        }

        return merged;
    }

    /// <summary>The merged factor of pixel <paramref name="p"/> (see <see cref="Merge"/>), with
    /// <paramref name="low"/> and <paramref name="high"/> as room for each use's accepted range.</summary>
    private static float MergeAt(SeamPiece piece, float plain, List<Want> uses, int p, double[] low, double[] high)
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
            return first ?? plain;
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
        return (float)Math.Clamp(Math.Clamp(value, floor, ceiling), 0, plain);
    }

    /// <summary>Settles the pixels whose wanted value depends on another pixel of the same file: where a copy of the
    /// file lies over another copy at a different file pixel (a half and its mirror sharing a strip), the lower draw
    /// wants its value against the upper pixel's merged value, which is wanted in turn against its own partner. Each
    /// pass merges those pixels, moves halfway there, and wants them again against <paramref name="merged"/>:
    /// stepping all the way swings between two values for ever, and halfway settles on the value both draws share
    /// within a few passes. A copy only shifted over another settles in one pass, as its partner is not coupled
    /// back. Stops once no pixel moves by a tenth of an alpha level, or after 12 passes.
    /// <para>Settling one use can leave another use of the file, in this layout or another, visibly wrong where it
    /// was right. So a pixel keeps its settled value only where it leaves fewer of the piece's uses visibly wrong
    /// than the value before, each use wanting against the values around it at the time; elsewhere it goes back.
    /// If the mix of the two leaves no fewer uses wrong over all those pixels, all of them go back.</para></summary>
    private static void Refine(SeamPiece piece, float plain, List<Want> uses, float[] merged)
    {
        var coupled = uses.SelectMany(u => u.Coupled).Distinct().ToArray();
        if (coupled.Length == 0)
        {
            return;
        }

        var low = new double[uses.Count];
        var high = new double[uses.Count];
        var covering = new List<Cover>();
        Rewant(uses, merged, covering);
        var start = new float[coupled.Length];
        var wrongAtStart = new int[coupled.Length];
        var startTotal = 0;
        for (var i = 0; i < coupled.Length; i++)
        {
            start[i] = merged[coupled[i]];
            wrongAtStart[i] = WrongUses(piece, uses, coupled[i], merged[coupled[i]]);
            startTotal += wrongAtStart[i];
        }

        if (startTotal == 0)
        {
            return;
        }

        for (var pass = 0; pass < 12; pass++)
        {
            var moved = 0.0;
            foreach (var p in coupled)
            {
                var next = (merged[p] + MergeAt(piece, plain, uses, p, low, high)) / 2;
                moved = Math.Max(moved, Math.Abs(next - merged[p]));
                merged[p] = next;
            }

            if (moved == 0)
            {
                break;
            }

            Rewant(uses, merged, covering);
            if (moved < 0.1 / 255)
            {
                break;
            }
        }

        var restored = false;
        for (var i = 0; i < coupled.Length; i++)
        {
            var p = coupled[i];
            if (merged[p] != start[i] && WrongUses(piece, uses, p, merged[p]) >= wrongAtStart[i])
            {
                merged[p] = start[i];
                restored = true;
            }
        }

        if (restored)
        {
            Rewant(uses, merged, covering);
        }

        var total = 0;
        foreach (var p in coupled)
        {
            total += WrongUses(piece, uses, p, merged[p]);
        }

        if (total >= startTotal)
        {
            for (var i = 0; i < coupled.Length; i++)
            {
                merged[coupled[i]] = start[i];
            }

            Rewant(uses, merged, covering);
        }
    }

    /// <summary>Settling draws of one file together (see <see cref="Refine"/>) takes the upper draw's say away where it
    /// lies over another pixel of the file, and that draw may be covered by other pieces the lower one cannot make up
    /// for, which leaves uses wrong that were right before. So wherever a use couples or defers a pixel, the pixel is
    /// also merged as it was before, with every draw of the file wanting on its own (see <see cref="WantAt"/>), and
    /// it keeps the settled value only where that leaves fewer of the piece's uses in every layout visibly wrong, each
    /// counted by what it wants in its own way; on a tie the value from before stays. A piece no use couples or
    /// defers skips this.</summary>
    private static void KeepSeparateWhereNoWorse(SeamPiece piece, float plain, List<Want> uses, float[] merged)
    {
        var pixels = uses.SelectMany(u => u.Coupled.Concat(u.Deferred)).Distinct().ToArray();
        if (pixels.Length == 0)
        {
            return;
        }

        var covering = new List<Cover>();
        var separate = new List<Want>(uses.Count);
        foreach (var use in uses)
        {
            var alone = use with { Factor = (float[])use.Factor.Clone(), Through = (float[])use.Through.Clone() };
            foreach (var index in use.Coupled.Concat(use.Deferred))
            {
                (alone.Factor[index], alone.Through[index]) =
                    WantAt(use.Own, use.Above, use.SelfBelow, index, merged, covering, true, out _, out _);
            }

            separate.Add(alone);
        }

        var low = new double[uses.Count];
        var high = new double[uses.Count];
        foreach (var p in pixels)
        {
            var before = MergeAt(piece, plain, separate, p, low, high);
            if (WrongUses(piece, uses, p, merged[p]) < WrongUses(piece, separate, p, before))
            {
                continue;
            }

            merged[p] = before;
            for (var u = 0; u < uses.Count; u++)
            {
                uses[u].Factor[p] = separate[u].Factor[p];
                uses[u].Through[p] = separate[u].Through[p];
            }
        }
    }

    /// <summary>Wants every use's coupled pixels again against <paramref name="merged"/> (see
    /// <see cref="WantAt"/>).</summary>
    private static void Rewant(List<Want> uses, float[] merged, List<Cover> covering)
    {
        foreach (var use in uses)
        {
            foreach (var index in use.Coupled)
            {
                (use.Factor[index], use.Through[index]) =
                    WantAt(use.Own, use.Above, use.SelfBelow, index, merged, covering, false, out _, out _);
            }
        }
    }

    /// <summary>How many of the piece's uses that see pixel <paramref name="p"/> the factor <paramref name="value"/>
    /// leaves visibly wrong there.</summary>
    private static int WrongUses(SeamPiece piece, List<Want> uses, int p, float value)
    {
        var s = piece.Alpha[p] / 255.0;
        var wrong = 0;
        foreach (var use in uses)
        {
            if (IsWrong(use, p, s, value))
            {
                wrong++;
            }
        }

        return wrong;
    }

    /// <summary>Whether the factor <paramref name="value"/> shows under a use that sees pixel <paramref name="p"/>
    /// (native alpha <paramref name="s"/>) with an error over <see cref="Unseen"/> (see <see cref="Merge"/>).</summary>
    private static bool IsWrong(Want use, int p, double s, float value)
    {
        var through = use.Through[p];
        if (through <= 0)
        {
            return false;
        }

        var slack = Unseen / (through * s);
        return !(use.Factor[p] - slack <= value && value <= use.Factor[p] + slack);
    }

    /// <summary>How many pixels <paramref name="merged"/> leaves visibly wrong (an error over <see cref="Unseen"/>,
    /// see <see cref="Merge"/>) under the uses outside the preferred layout, counted once per use.</summary>
    private static long CountWrongElsewhere(SeamPiece piece, List<Want> uses, float[] merged)
    {
        var wrong = 0L;
        for (var p = 0; p < merged.Length; p++)
        {
            var s = piece.Alpha[p] / 255.0;
            foreach (var use in uses)
            {
                if (!use.Preferred && IsWrong(use, p, s, merged[p]))
                {
                    wrong++;
                }
            }
        }

        return wrong;
    }

    /// <summary>A piece covering a pixel: its native alpha and opacity, and whether it is the same file pixel drawn
    /// again, whose written alpha is the one being solved for, or the written alpha it is taken at where it is
    /// another pixel of the same file (NaN for any other piece).</summary>
    private readonly record struct Cover(double S, double A, bool Self, double Fixed = double.NaN);

    /// <summary>One use of a piece: the matrix from its pixels to level space and back, its level-space bounds, and
    /// the moving platforms it rides on.</summary>
    private sealed record Placement(
        SeamPiece Piece, Matrix ToLevel, Matrix FromLevel, Rect Bounds, IReadOnlyList<Mover>? Movers)
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
            return new Placement(piece, toLevel, fromLevel, bounds, placed.Movers);
        }

        /// <summary>The native alpha (0..1) the piece shows at this level point, blended as the game draws it
        /// stretched (see <see cref="Blended"/>), 0 outside the piece.</summary>
        public double AlphaAt(Point level) =>
            ToLevel.HasInverse ? Blended(Piece.Alpha, Piece.Width, Piece.Height, FromLevel.Transform(level)) : 0;

        /// <summary>Whether the piece counts as lying over this level point under <paramref name="shifts"/> (see
        /// <see cref="SeamMask.Covers"/>).</summary>
        public bool Covers(Point level, MoverShifts shifts) =>
            ToLevel.HasInverse && SeamMask.Covers(FromLevel, Piece.Width, Piece.Height, level, shifts);

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
    /// own there: its share is in the solve. Where the use above draws a different pixel of the same file here (a
    /// mirrored or shifted copy), this draw wants its value against that pixel's merged value, which starts at the
    /// plain opacity and is settled by <see cref="Refine"/>; the upper draw again wants nothing of its own there. A
    /// piece above that shifts against this one (see <see cref="Shifts"/>) covers only where it counts as staying
    /// over the point (see <see cref="Covers"/>); elsewhere it is away in game most of the time and the art keeps
    /// the plain fade.</summary>
    private static Want Wanted(
        Placement own, List<(Placement Upper, MoverShifts Shifts)> above, List<Placement> selfBelow)
    {
        var piece = own.Piece;
        var plain = (float)Math.Clamp(piece.Opacity, 0, 1);
        var factor = new float[piece.Width * piece.Height];
        var light = new float[factor.Length];
        Array.Fill(factor, plain);
        Array.Fill(light, 1f);
        if (above.Count == 0 && selfBelow.Count == 0)
        {
            return new Want(factor, light, own, above, selfBelow, [], []);
        }

        var current = new float[factor.Length];
        Array.Fill(current, plain);
        var covering = new List<Cover>(above.Count);
        var coupled = new List<int>();
        var deferred = new List<int>();
        for (var index = 0; index < factor.Length; index++)
        {
            (factor[index], light[index]) =
                WantAt(own, above, selfBelow, index, current, covering, false, out var depends, out var defers);
            if (depends)
            {
                coupled.Add(index);
            }

            if (defers)
            {
                deferred.Add(index);
            }
        }

        return new Want(factor, light, own, above, selfBelow, [.. coupled], [.. deferred]);
    }

    /// <summary>What one use wants of pixel <paramref name="index"/> (see <see cref="Wanted"/>), taking another
    /// pixel of the same file drawn above it at its value in <paramref name="current"/>; <paramref name="coupled"/>
    /// tells whether it did, and <paramref name="deferred"/> whether it left the value to another pixel of the file
    /// below. With <paramref name="separate"/> it does neither and wants what it did before draws of one file were
    /// settled together: another pixel of the file covers like any other piece. <paramref name="covering"/> is
    /// scratch room.</summary>
    private static (float Factor, float Light) WantAt(
        Placement own,
        List<(Placement Upper, MoverShifts Shifts)> above,
        List<Placement> selfBelow,
        int index,
        float[] current,
        List<Cover> covering,
        bool separate,
        out bool coupled,
        out bool deferred)
    {
        coupled = false;
        deferred = false;
        var piece = own.Piece;
        var plain = Math.Clamp(piece.Opacity, 0, 1);
        var s = piece.Alpha[index] / 255.0;
        if (s == 0)
        {
            return ((float)plain, 1f);
        }

        var point = own.ToLevel.Transform(new Point((index % piece.Width) + 0.5, (index / piece.Width) + 0.5));
        if (selfBelow.Exists(b => b.Bounds.Contains(point)
            && b.IndexAt(point) is >= 0 and var j
            && (j == index || (!separate && piece.Alpha[j] > 0))))
        {
            // Another draw of this file below carries the value here: this same pixel (see SolveSelf) or the pixel
            // under it, which wants its value against this one's (see Refine).
            deferred = !selfBelow.Exists(b => b.Bounds.Contains(point) && b.IndexAt(point) == index);
            return ((float)plain, -1f);
        }

        covering.Clear();
        var self = false;
        foreach (var (other, shifts) in above)
        {
            if (other.Bounds.Contains(point)
                && other.AlphaAt(point) is > 0 and var blended
                && (shifts.Count == 0 || other.Covers(point, shifts)))
            {
                // The same file pixel drawn again is solved as this pixel's own value (see SolveSelf), so it covers
                // with that value, not a blend of its neighbours; another pixel of the file covers at its current
                // value. Only a draw that keeps its place stays on one file pixel.
                var j = shifts.Count == 0 && other.Piece == piece ? other.IndexAt(point) : -1;
                if (separate && j != index)
                {
                    j = -1;
                }

                var same = j == index;
                var opacity = Math.Clamp(other.Piece.Opacity, 0, 1);
                if (j >= 0 && !same)
                {
                    coupled = true;
                    covering.Add(new Cover(blended, opacity, false, blended * current[j]));
                    continue;
                }

                self |= same;
                covering.Add(new Cover(same ? s : blended, opacity, same));
            }
        }

        if (covering.Count == 0)
        {
            return ((float)plain, 1f);
        }

        var written = self ? SolveSelf(s, plain, covering) : Stack(s, plain, covering, 0).Written;
        return ((float)Math.Clamp(written / s, 0, plain), (float)Stack(s, plain, covering, written).Through);
    }

    /// <summary>The written alpha this piece wants under <paramref name="above"/> (top first), and the light that
    /// reaches it through them. Other pieces are taken as corrected to their own wanted weight, as in
    /// <see cref="Written"/>. A <see cref="Cover.Self"/> entry is written at <paramref name="x"/> and a
    /// <see cref="Cover.Fixed"/> one at that value, and whatever it gives this file's colour more or less than one
    /// layer would is taken off or added to this draw.</summary>
    private static (double Written, double Through) Stack(double s, double a, List<Cover> above, double x)
    {
        var native = 1.0;
        var through = 1.0;
        var owed = 0.0;
        foreach (var (sj, aj, self, fixedAt) in above)
        {
            var wanted = aj * sj * native;
            var wj = self ? x : double.IsNaN(fixedAt) ? wanted / through : fixedAt;
            if (self || !double.IsNaN(fixedAt))
            {
                owed += wanted - (wj * through);
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
