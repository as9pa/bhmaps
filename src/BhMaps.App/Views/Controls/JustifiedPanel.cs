using System.Windows;
using System.Windows.Controls;
using BhMaps.Core.Layout;

namespace BhMaps.App.Views.Controls;

/// <summary>3.0: the panel behind a justified grid. A WrapPanel gives every tile the width the size asks for and
/// leaves whatever did not fit as a hole down the right edge, which is the gap the owner saw between the Maps
/// grid and the open panel. This one treats <see cref="TargetWidth"/> as a wish: it fits as many tiles across as
/// it can, then shares the leftover out over them, so a row always reaches the right edge. The last row keeps
/// that width rather than stretching to fill, so a row of two cards is two cards and not two half-page ones.
///
/// Like the WrapPanel it replaces it does not virtualise, which is the point: every container is realised, so
/// the selection binding on a card is exact (plan decision A-D10).</summary>
public class JustifiedPanel : Panel
{
    public static readonly DependencyProperty TargetWidthProperty = DependencyProperty.Register(
        nameof(TargetWidth),
        typeof(double),
        typeof(JustifiedPanel),
        new FrameworkPropertyMetadata(
            200d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap),
        typeof(double),
        typeof(JustifiedPanel),
        new FrameworkPropertyMetadata(
            TileSizes.Gap,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>About how wide a tile should be. The width a tile actually gets is this one rounded out to make
    /// the row come out even, so it is never far from what the page asked for.</summary>
    public double TargetWidth
    {
        get => (double)GetValue(TargetWidthProperty);
        set => SetValue(TargetWidthProperty, value);
    }

    /// <summary>The gutter, between tiles and between rows. The tiles themselves carry no margin, so this is the
    /// only number that decides how far apart they sit.</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (columns, itemWidth) = Columns(availableSize.Width);
        var width = LayoutWidth(availableSize.Width);
        var height = 0d;
        var rowHeight = 0d;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            var endOfRow = (i + 1) % columns == 0 || i == InternalChildren.Count - 1;
            if (endOfRow)
            {
                height += height > 0 ? Gap + rowHeight : rowHeight;
                rowHeight = 0;
            }
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Columns(finalSize.Width);
        var y = 0d;

        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);

            // A row is as tall as its tallest tile, and every tile in it is given that height, so the row reads
            // as a row rather than as a ragged line of cards.
            var rowHeight = 0d;
            for (var i = start; i < end; i++)
            {
                rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            }

            for (var i = start; i < end; i++)
            {
                InternalChildren[i].Arrange(new Rect((i - start) * (itemWidth + Gap), y, itemWidth, rowHeight));
            }

            y += rowHeight + Gap;
        }

        return finalSize;
    }

    /// <summary>The width to lay out in. A ListBox with its horizontal scroll bar off hands down a real width;
    /// an unbounded measure, which only a misconfigured host produces, falls back to one row of targets rather
    /// than to infinity, so the panel still has a size to report.</summary>
    private double LayoutWidth(double available) =>
        double.IsInfinity(available) || double.IsNaN(available)
            ? (TargetWidth + Gap) * Math.Max(1, InternalChildren.Count)
            : available;

    private (int Columns, double ItemWidth) Columns(double available) =>
        JustifiedLayout.Compute(LayoutWidth(available), TargetWidth, Gap);
}
