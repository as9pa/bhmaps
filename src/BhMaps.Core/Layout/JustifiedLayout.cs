namespace BhMaps.Core.Layout;

/// <summary>3.0: the row maths behind the justified grids. A wrapping row of fixed-width tiles leaves whatever
/// did not fit as a hole down the right edge, which is what the owner saw between the Maps grid and the open
/// panel. Here the tile width is the answer rather than the question: as many tiles as fit at about the target
/// width, then the leftover shared out over them, so a row always ends where the grid ends.</summary>
public static class JustifiedLayout
{
    /// <summary>Columns for <paramref name="available"/> px of width, and the width each tile takes to fill it.
    /// There is always at least one column, and a width is never below 1: a panel measured before it has a size
    /// must still hand its children something a layout pass can use.</summary>
    public static (int Columns, double ItemWidth) Compute(double available, double target, double gap)
    {
        var columns = Math.Max(1, (int)Math.Floor((available + gap) / (target + gap)));
        var itemWidth = (available - ((columns - 1) * gap)) / columns;
        return (columns, Math.Max(1, itemWidth));
    }
}
