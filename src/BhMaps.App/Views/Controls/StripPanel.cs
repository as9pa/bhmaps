using System.Windows;
using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>A row's strip of choices (addendum B): one line while the row is folded, with everything past the
/// right edge left undrawn and counted in <see cref="Overflow" /> for the row's "+N" tile, and as many lines as
/// it needs once the row is unfolded. A panel of its own rather than a WrapPanel under a clip, because only the
/// panel that measured the tiles can say how many did not fit, and the "+N" tile has to name that number.</summary>
public sealed class StripPanel : Panel
{
    /// <summary>Room kept at the right end of a folded line for the row's "+N" tile, so the tile never lands on
    /// top of the last thumbnail. 52 px is "+58" in the theme's 11 px action font plus the TileAction style's
    /// 8,3 padding and its 4 px left margin.</summary>
    public const double MoreWidth = 52;

    public static readonly DependencyProperty IsUnfoldedProperty =
        DependencyProperty.Register(
            nameof(IsUnfolded),
            typeof(bool),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OverflowProperty =
        DependencyProperty.Register(
            nameof(Overflow),
            typeof(int),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public bool IsUnfolded
    {
        get => (bool)GetValue(IsUnfoldedProperty);
        set => SetValue(IsUnfoldedProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many children the last measure left undrawn. Always zero while the row is unfolded, which is
    /// what takes the "+N" tile off the row.</summary>
    public int Overflow
    {
        get => (int)GetValue(OverflowProperty);
        set => SetValue(OverflowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        foreach (UIElement child in InternalChildren)
        {
            // Unconstrained, because every tile in a strip is a fixed size the zoom decided (addendum B, q1).
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        if (IsUnfolded)
        {
            SetCurrentValue(OverflowProperty, 0);
            return Wrap(width, arrange: false);
        }

        // Two passes: the first asks whether every tile fits, the second makes room for the "+N" tile only when
        // the answer was no. Reserving that room unconditionally would push a tile off a line it fits on.
        var shown = Fit(width);
        if (shown < InternalChildren.Count)
        {
            shown = Fit(Math.Max(0, width - MoreWidth - Spacing));
        }

        SetCurrentValue(OverflowProperty, InternalChildren.Count - shown);
        double used = 0;
        double height = 0;
        for (var i = 0; i < shown; i++)
        {
            var size = InternalChildren[i].DesiredSize;
            used += size.Width + (i > 0 ? Spacing : 0);
            height = Math.Max(height, size.Height);
        }

        return new Size(Math.Min(used, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsUnfolded)
        {
            Wrap(finalSize.Width, arrange: true);
            return finalSize;
        }

        var shown = InternalChildren.Count - Overflow;
        double x = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (i >= shown)
            {
                // Arranged at nothing rather than collapsed: changing Visibility during arrange starts another
                // layout pass, and a child of zero size is neither drawn nor hit-tested nor tabbed to.
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var size = child.DesiredSize;
            child.Arrange(new Rect(x, 0, size.Width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }

    /// <summary>How many children fit on one line of that width. Always at least one, so a window narrower than
    /// a single thumbnail still shows the choice the game is on.</summary>
    private int Fit(double width)
    {
        double used = 0;
        var count = 0;
        foreach (UIElement child in InternalChildren)
        {
            var next = used + (count > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (count > 0 && next > width)
            {
                break;
            }

            used = next;
            count++;
        }

        return count;
    }

    /// <summary>Lines of children, wrapping at the width. Measures when <paramref name="arrange" /> is false and
    /// places them when it is true, so the two passes cannot disagree about where a line breaks.</summary>
    private Size Wrap(double width, bool arrange)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double widest = 0;
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(Math.Min(widest, width), y + lineHeight);
    }
}
