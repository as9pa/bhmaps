using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.App.Views.Controls;

/// <summary>One drawn piece, the way <see cref="GuidesOverlay.HoveredPiece"/> names it: the file and its place in
/// <see cref="MapCompositor.Walk"/>, because one file can be drawn more than once.</summary>
public sealed record GuidePieceKey(string RelativePath, int Order)
{
    public bool Matches(PieceOutline piece) =>
        piece.Order == Order && string.Equals(piece.RelativePath, RelativePath, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Spec G1/G2/G5/G6: the platform editor's guides drawn over the stage, from a <see cref="PieceGuideSet"/>
/// in level space taken through <see cref="LevelToScreen"/>. Every line is drawn in screen pixels after the
/// transform, so none of them thickens with zoom, and each point is snapped to a device pixel centre so the axis
/// aligned ones stay sharp. Layers go bottom to top: hitboxes, overlaps, boxes, size tags. The geometry is built
/// once per guide set, transform, size and DPI; a hover change only redraws it.</summary>
public sealed class GuidesOverlay : FrameworkElement
{
    /// <summary>The gap between hatch lines across the overlap band, in screen pixels at any zoom.</summary>
    private const double HatchSpacing = 6;

    /// <summary>How far a size tag sits from the overlap it measures.</summary>
    private const double TagGap = 4;

    public static readonly DependencyProperty GuidesProperty =
        DependencyProperty.Register(
            nameof(Guides),
            typeof(PieceGuideSet),
            typeof(GuidesOverlay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));

    public static readonly DependencyProperty LevelToScreenProperty =
        DependencyProperty.Register(
            nameof(LevelToScreen),
            typeof(Matrix),
            typeof(GuidesOverlay),
            new FrameworkPropertyMetadata(
                Matrix.Identity, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryChanged));

    public static readonly DependencyProperty HoveredPieceProperty =
        DependencyProperty.Register(
            nameof(HoveredPiece),
            typeof(GuidePieceKey),
            typeof(GuidesOverlay),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowBoxesProperty = RegisterLayer(nameof(ShowBoxes));

    public static readonly DependencyProperty ShowOverlapsProperty = RegisterLayer(nameof(ShowOverlaps));

    public static readonly DependencyProperty ShowHitboxesProperty = RegisterLayer(nameof(ShowHitboxes));

    public static readonly DependencyProperty ShowSpecialHitboxesProperty = RegisterLayer(nameof(ShowSpecialHitboxes));

    private Cache? cache;
    private Palette? palette;

    public GuidesOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>What to draw, in level space. Null draws nothing.</summary>
    public PieceGuideSet? Guides
    {
        get => (PieceGuideSet?)GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>Level space to this control's own coordinates: the caller's stage transform, Viewbox scale and view
    /// zoom and pan, already composed.</summary>
    public Matrix LevelToScreen
    {
        get => (Matrix)GetValue(LevelToScreenProperty);
        set => SetValue(LevelToScreenProperty, value);
    }

    /// <summary>The piece under the pointer, or null: its box is drawn solid and the overlaps it is part of carry
    /// a size tag.</summary>
    public GuidePieceKey? HoveredPiece
    {
        get => (GuidePieceKey?)GetValue(HoveredPieceProperty);
        set => SetValue(HoveredPieceProperty, value);
    }

    /// <summary>G1, every piece's file rectangle.</summary>
    public bool ShowBoxes
    {
        get => (bool)GetValue(ShowBoxesProperty);
        set => SetValue(ShowBoxesProperty, value);
    }

    /// <summary>G2, the hatched overlap bands and their size tags.</summary>
    public bool ShowOverlaps
    {
        get => (bool)GetValue(ShowOverlapsProperty);
        set => SetValue(ShowOverlapsProperty, value);
    }

    /// <summary>G5, the Hard and Soft collision lines.</summary>
    public bool ShowHitboxes
    {
        get => (bool)GetValue(ShowHitboxesProperty);
        set => SetValue(ShowHitboxesProperty, value);
    }

    /// <summary>G5, every other collision kind (no-slide, bouncy, lava, pressure plates, game-mode hard, mud), on
    /// its own so the common two can be shown without them. Independent of <see cref="ShowHitboxes"/>.</summary>
    public bool ShowSpecialHitboxes
    {
        get => (bool)GetValue(ShowSpecialHitboxesProperty);
        set => SetValue(ShowSpecialHitboxesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Guides is not { } guides)
        {
            return;
        }

        palette ??= new Palette(this);
        cache ??= Build(guides, LevelToScreen, VisualTreeHelper.GetDpi(this).DpiScaleX, new Rect(RenderSize));

        foreach (var hitbox in cache.Hitboxes)
        {
            if (hitbox.Style is HitboxStyle.Hard or HitboxStyle.Soft ? ShowHitboxes : ShowSpecialHitboxes)
            {
                drawingContext.DrawGeometry(null, palette.Hitbox[(int)hitbox.Style], hitbox.Lines);
            }
        }

        if (ShowOverlaps)
        {
            foreach (var overlap in cache.Overlaps)
            {
                drawingContext.PushClip(overlap.Shape);
                drawingContext.DrawGeometry(palette.OverlapFill, null, overlap.Shape);
                drawingContext.DrawGeometry(null, palette.Hatch, overlap.Hatch);
                drawingContext.Pop();
                drawingContext.DrawGeometry(null, palette.OverlapEdge, overlap.Shape);
            }
        }

        var hovered = HoveredPiece;
        if (ShowBoxes)
        {
            // The hovered box last, so its solid line is never broken by a neighbour's dashes.
            Geometry? hoveredBox = null;
            foreach (var box in cache.Boxes)
            {
                if (hovered is not null && hovered.Matches(box.Piece))
                {
                    hoveredBox = box.Outline;
                    continue;
                }

                drawingContext.DrawGeometry(null, palette.Box, box.Outline);
            }

            if (hoveredBox is not null)
            {
                drawingContext.DrawGeometry(null, palette.HoveredBox, hoveredBox);
            }
        }

        if (ShowOverlaps && hovered is not null)
        {
            foreach (var overlap in cache.Overlaps)
            {
                if (hovered.Matches(overlap.Overlap.Lower) || hovered.Matches(overlap.Overlap.Upper))
                {
                    DrawTag(drawingContext, overlap);
                }
            }
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        // The hatch is cut to the visible area, so a new size needs new hatching.
        cache = null;
        base.OnRenderSizeChanged(sizeInfo);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        cache = null;
        InvalidateVisual();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    private static DependencyProperty RegisterLayer(string name) =>
        DependencyProperty.Register(
            name,
            typeof(bool),
            typeof(GuidesOverlay),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GuidesOverlay)d).cache = null;

    private static Cache Build(PieceGuideSet guides, Matrix toScreen, double dpiScale, Rect visible)
    {
        Point Snap(Point level)
        {
            var p = toScreen.Transform(level);
            return new Point(SnapToPixel(p.X, dpiScale), SnapToPixel(p.Y, dpiScale));
        }

        var boxes = guides.Pieces
            .Where(p => p.Corners.Count >= 3)
            .Select(p => new BoxGeometry(p, Polygon(p.Corners.Select(Snap), filled: false)))
            .ToList();

        // Hatch lines are x + y = c, about 45 degrees, counted from where level (0, 0) lands, so the hatch moves
        // with a pan instead of crawling across the band.
        var hatchOrigin = toScreen.OffsetX + toScreen.OffsetY;
        var overlaps = new List<OverlapGeometry>();
        foreach (var overlap in guides.Overlaps)
        {
            if (overlap.Polygon.Count < 3)
            {
                continue;
            }

            var shape = Polygon(overlap.Polygon.Select(Snap), filled: true);
            overlaps.Add(new OverlapGeometry(overlap, shape, Hatch(shape.Bounds, visible, hatchOrigin)));
        }

        var hitboxes = new List<HitboxGeometry>();
        foreach (var group in guides.Collisions.GroupBy(c => StyleOf(c.Kind)))
        {
            var lines = new StreamGeometry();
            using (var context = lines.Open())
            {
                foreach (var line in group)
                {
                    context.BeginFigure(Snap(new Point(line.X1, line.Y1)), isFilled: false, isClosed: false);
                    var end = Snap(new Point(line.X2, line.Y2));
                    if (line is { AnchorX: { } ax, AnchorY: { } ay })
                    {
                        // An affine transform keeps a Bezier a Bezier, so only its three points need mapping.
                        context.QuadraticBezierTo(
                            toScreen.Transform(new Point(ax, ay)), end, isStroked: true, isSmoothJoin: false);
                    }
                    else
                    {
                        context.LineTo(end, isStroked: true, isSmoothJoin: false);
                    }
                }
            }

            lines.Freeze();
            hitboxes.Add(new HitboxGeometry(group.Key, lines));
        }

        return new Cache(boxes, overlaps, hitboxes);
    }

    /// <summary>The centre of the device pixel <paramref name="value"/> falls in, in device independent units: a
    /// 1 px line drawn there covers exactly one pixel instead of smearing over two.</summary>
    private static double SnapToPixel(double value, double dpiScale) => (Math.Floor(value * dpiScale) + 0.5) / dpiScale;

    private static StreamGeometry Polygon(IEnumerable<Point> points, bool filled)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var first = true;
            foreach (var point in points)
            {
                if (first)
                {
                    context.BeginFigure(point, filled, isClosed: true);
                    first = false;
                }
                else
                {
                    context.LineTo(point, isStroked: true, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>The hatch lines across <paramref name="bounds"/>, cut to the visible area first so a band zoomed
    /// far past the window does not cost thousands of lines.</summary>
    private static StreamGeometry Hatch(Rect bounds, Rect visible, double origin)
    {
        var geometry = new StreamGeometry();
        var area = Rect.Intersect(bounds, visible);
        if (!area.IsEmpty)
        {
            using var context = geometry.Open();
            var first = Math.Ceiling((area.Left + area.Top - origin) / HatchSpacing);
            var last = Math.Floor((area.Right + area.Bottom - origin) / HatchSpacing);
            for (var k = first; k <= last; k++)
            {
                var c = origin + (k * HatchSpacing);
                context.BeginFigure(new Point(area.Left, c - area.Left), isFilled: false, isClosed: false);
                context.LineTo(new Point(area.Right, c - area.Right), isStroked: true, isSmoothJoin: false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static HitboxStyle StyleOf(CollisionKind kind) =>
        kind switch
        {
            CollisionKind.Hard => HitboxStyle.Hard,
            CollisionKind.Soft => HitboxStyle.Soft,
            CollisionKind.NoSlide => HitboxStyle.NoSlide,
            CollisionKind.BouncyHard or CollisionKind.BouncySoft or CollisionKind.BouncyNoSlide => HitboxStyle.Bouncy,
            CollisionKind.Lava => HitboxStyle.Lava,
            CollisionKind.PressurePlate or CollisionKind.SoftPressurePlate => HitboxStyle.PressurePlate,
            CollisionKind.GameModeHard => HitboxStyle.GameModeHard,
            CollisionKind.Mud => HitboxStyle.Mud,
            _ => HitboxStyle.Hard,
        };

    /// <summary>The overlap's width in level units on a small dark plate under the band, or above it when the
    /// band sits at the bottom edge; kept inside the control and on whole pixels so the text stays crisp.</summary>
    private void DrawTag(DrawingContext drawingContext, OverlapGeometry overlap)
    {
        var palette = this.palette!;
        var dpi = VisualTreeHelper.GetDpi(this);
        var text = new FormattedText(
            Math.Round(overlap.Overlap.Width).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            palette.TagTypeface,
            palette.TagSize,
            palette.TagText,
            dpi.PixelsPerDip);

        const double padX = 4;
        const double padY = 1;
        var width = text.Width + (2 * padX);
        var height = text.Height + (2 * padY);
        var bounds = overlap.Shape.Bounds;

        var x = bounds.Left + ((bounds.Width - width) / 2);
        var y = bounds.Bottom + TagGap;
        if (y + height > ActualHeight)
        {
            y = bounds.Top - TagGap - height;
        }

        x = Math.Clamp(x, 0, Math.Max(0, ActualWidth - width));
        y = Math.Clamp(y, 0, Math.Max(0, ActualHeight - height));
        x = Math.Round(x * dpi.DpiScaleX) / dpi.DpiScaleX;
        y = Math.Round(y * dpi.DpiScaleY) / dpi.DpiScaleY;

        drawingContext.DrawRoundedRectangle(palette.TagPlate, null, new Rect(x, y, width, height), 3, 3);
        drawingContext.DrawText(text, new Point(x + padX, y + padY));
    }

    private enum HitboxStyle
    {
        Hard,
        Soft,
        NoSlide,
        Bouncy,
        Lava,
        PressurePlate,
        GameModeHard,
        Mud,
    }

    private sealed record BoxGeometry(PieceOutline Piece, Geometry Outline);

    private sealed record OverlapGeometry(PieceOverlap Overlap, Geometry Shape, Geometry Hatch);

    private sealed record HitboxGeometry(HitboxStyle Style, Geometry Lines);

    private sealed record Cache(
        IReadOnlyList<BoxGeometry> Boxes, IReadOnlyList<OverlapGeometry> Overlaps, IReadOnlyList<HitboxGeometry> Hitboxes);

    /// <summary>Frozen pens and brushes from the theme, read once. The fallbacks are the theme's own values, for a
    /// control that renders before its resources are reachable.</summary>
    private sealed class Palette
    {
        public Palette(FrameworkElement owner)
        {
            var text = ColorOf(owner, "TextBrush", Color.FromRgb(0xF1, 0xEF, 0xEA));
            var warn = ColorOf(owner, "WarnBrush", Color.FromRgb(0xD9, 0xA6, 0x6A));
            var hitbox = ColorOf(owner, "HitboxBrush", Color.FromRgb(0x7F, 0xA3, 0xD6));
            var lava = ColorOf(owner, "HitboxLavaBrush", Color.FromRgb(0xC9, 0x8B, 0x95));

            // G1: the light text colour at 55 percent, dashed; the hovered piece's box solid and at full strength.
            Box = MakePen(WithAlpha(text, 0.55), 1, [4, 3]);
            HoveredBox = MakePen(text, 1, null);

            // G2: a faint wash under the hatch so the band reads as one shape, the hatch, and a 1 px edge.
            OverlapFill = Frozen(new SolidColorBrush(WithAlpha(warn, 0.12)));
            Hatch = MakePen(WithAlpha(warn, 0.6), 1, null);
            OverlapEdge = MakePen(warn, 1, null);

            // G5: Hard solid, Soft dashed, and each special kind its own quiet dash (lava by tint). Dash lengths
            // are in multiples of the 1.25 px thickness.
            const double t = 1.25;
            Hitbox = new Pen[Enum.GetValues<HitboxStyle>().Length];
            Hitbox[(int)HitboxStyle.Hard] = MakePen(hitbox, t, null);
            Hitbox[(int)HitboxStyle.Soft] = MakePen(hitbox, t, [3, 2]);
            Hitbox[(int)HitboxStyle.NoSlide] = MakePen(hitbox, t, [1, 2]);
            Hitbox[(int)HitboxStyle.Bouncy] = MakePen(hitbox, t, [5, 2, 1, 2]);
            Hitbox[(int)HitboxStyle.Lava] = MakePen(lava, t, null);
            Hitbox[(int)HitboxStyle.PressurePlate] = MakePen(hitbox, t, [8, 3]);
            Hitbox[(int)HitboxStyle.GameModeHard] = MakePen(hitbox, t, [8, 2, 1, 2, 1, 2]);
            Hitbox[(int)HitboxStyle.Mud] = MakePen(lava, t, [1, 2]);

            TagPlate = owner.TryFindResource("ScrimBrush") as Brush
                ?? Frozen(new SolidColorBrush(Color.FromArgb(0xD9, 0x16, 0x15, 0x14)));
            TagText = Frozen(new SolidColorBrush(text));
            TagTypeface = new Typeface(
                owner.TryFindResource("Sans") as FontFamily ?? new FontFamily("Segoe UI"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            TagSize = owner.TryFindResource("FontCaption") is double size ? size : 12;
        }

        public Pen Box { get; }

        public Pen HoveredBox { get; }

        public Brush OverlapFill { get; }

        public Pen Hatch { get; }

        public Pen OverlapEdge { get; }

        public Pen[] Hitbox { get; }

        public Brush TagPlate { get; }

        public Brush TagText { get; }

        public Typeface TagTypeface { get; }

        public double TagSize { get; }

        private static Color ColorOf(FrameworkElement owner, string key, Color fallback) =>
            owner.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

        private static Color WithAlpha(Color color, double opacity) =>
            Color.FromArgb((byte)Math.Round(255 * opacity), color.R, color.G, color.B);

        private static Pen MakePen(Color color, double thickness, double[]? dashes)
        {
            var pen = new Pen(Frozen(new SolidColorBrush(color)), thickness)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
                DashCap = PenLineCap.Flat,
                LineJoin = PenLineJoin.Miter,
            };
            if (dashes is not null)
            {
                pen.DashStyle = new DashStyle(dashes, 0);
            }

            pen.Freeze();
            return pen;
        }

        private static T Frozen<T>(T freezable)
            where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
