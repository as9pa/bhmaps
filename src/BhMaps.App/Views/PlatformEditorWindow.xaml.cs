using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.ViewModels;
using BhMaps.App.Views.Controls;
using BhMaps.Core.Imaging;

namespace BhMaps.App.Views;

public partial class PlatformEditorWindow : Window
{
    /// <summary>Where the drag was last seen, in the preview's own coordinates; null when nothing is dragging.</summary>
    private Point? _panFrom;

    public PlatformEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PlatformEditorViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
                old.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is PlatformEditorViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                UpdateGuidesTransform();

                // The rows draw their tile colour until a file has decoded, so the list is up before the
                // thumbnails are and nothing waits on the disk.
                _ = vm.LoadThumbnailsAsync(CancellationToken.None);
            }
        };

        // Turned back on, the guides are laid out afresh, so they take the transform again with their new size.
        GuidesLayer.SizeChanged += (_, _) => UpdateGuidesTransform();
    }

    private void OnCloseRequested(bool ok) => DialogResult = ok;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlatformEditorViewModel.LevelToStage))
        {
            UpdateGuidesTransform();
        }
    }

    private void StageBox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGuidesTransform();

    /// <summary>G1: level space to the guides' own pixels, composed in this one place: the view model's stage
    /// transform for the camera on show, then the Viewbox's fit of the 1280 by 720 stage into the card, then the
    /// view's own zoom and pan.</summary>
    private void UpdateGuidesTransform()
    {
        if (DataContext is not PlatformEditorViewModel vm)
        {
            return;
        }

        var levelToScreen = vm.LevelToStage;
        levelToScreen.Append(StageToCard());
        levelToScreen.Append(ViewTransform());
        GuidesLayer.LevelToScreen = levelToScreen;
    }

    /// <summary>The Viewbox's uniform scale and its offset inside the guides' box, read from its layout. Before the
    /// first layout there is no scale, and identity draws nothing worse than an empty overlay.</summary>
    private Matrix StageToCard()
    {
        var scale = Math.Min(
            StageBox.ActualWidth / MapCompositor.PanelWidth, StageBox.ActualHeight / MapCompositor.PanelHeight);
        if (!(scale > 0))
        {
            return Matrix.Identity;
        }

        var origin = StageBox.TranslatePoint(new Point(0, 0), GuidesLayer);
        var fit = Matrix.Identity;
        fit.Scale(scale, scale);
        fit.Translate(
            origin.X + ((StageBox.ActualWidth - (MapCompositor.PanelWidth * scale)) / 2),
            origin.Y + ((StageBox.ActualHeight - (MapCompositor.PanelHeight * scale)) / 2));
        return fit;
    }

    /// <summary>G6: the view's own zoom and pan, in the guides' pixels, applied last. Identity until the stage can
    /// be zoomed.</summary>
    private static Matrix ViewTransform() => Matrix.Identity;

    /// <summary>G3: a click on the stage gives it the focus, so the G key reaches its binding. Nothing is handled
    /// here, so the drag and the double-click still get the press.</summary>
    private void Stage_PreviewMouseDown(object sender, MouseButtonEventArgs e) => Stage.Focus();

    /// <summary>G1: the piece under the pointer is the one whose box holds the point taken back into level space;
    /// where boxes stack, the one drawn last is on top.</summary>
    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (DataContext is not PlatformEditorViewModel { Guides: { } guides } || !GuidesLayer.IsVisible)
        {
            return;
        }

        var toLevel = GuidesLayer.LevelToScreen;
        if (!toLevel.HasInverse)
        {
            return;
        }

        toLevel.Invert();
        var at = toLevel.Transform(e.GetPosition(GuidesLayer));
        PieceOutline? top = null;
        foreach (var piece in guides.Pieces)
        {
            if ((top is null || piece.Order > top.Order) && Contains(piece.Corners, at))
            {
                top = piece;
            }
        }

        SetHovered(top);
    }

    private void Stage_MouseLeave(object sender, MouseEventArgs e) => SetHovered(null);

    /// <summary>G1: a file row names a file, and one file can be drawn more than once; the hover is one piece, so
    /// the row lights the use drawn last, the one on top on the stage.</summary>
    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is PlatformEditorViewModel { Guides: { } guides }
            && sender is FrameworkElement { DataContext: PlatformPieceViewModel row })
        {
            SetHovered(guides.Pieces
                .Where(p => string.Equals(p.RelativePath, row.RelativePath, StringComparison.OrdinalIgnoreCase))
                .MaxBy(p => p.Order));
        }
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e) => SetHovered(null);

    private void SetHovered(PieceOutline? piece) =>
        GuidesLayer.HoveredPiece = piece is null ? null : new GuidePieceKey(piece.RelativePath, piece.Order);

    /// <summary>Even-odd crossing test, so a flipped piece's mirrored corner order counts the same.</summary>
    private static bool Contains(IReadOnlyList<Point> polygon, Point at)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (a, b) = (polygon[i], polygon[j]);
            if ((a.Y > at.Y) != (b.Y > at.Y) && at.X < ((b.X - a.X) * (at.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>The throttle keeps drawing while the thumb moves; the drag ending is when the newest values are
    /// worth one more render with nothing queued behind it (spec 6).</summary>
    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is PlatformEditorViewModel vm)
        {
            vm.RenderFinal();
        }
    }

    /// <summary>Spec 6.2: the preview drags the picture, laid across the platforms or on each piece (3.10). The
    /// Viewbox draws the composed stage at whatever size the card leaves it, so a delta in the Image's own
    /// coordinates is scaled back to the stage's 1280 by 720 before the view model sees it. A double-click puts
    /// the picture back in the middle.</summary>
    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PlatformEditorViewModel { CanPan: true } vm || sender is not UIElement preview)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            vm.CentrePan();
            return;
        }

        _panFrom = e.GetPosition(preview);
        preview.CaptureMouse();
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_panFrom is not { } from
            || DataContext is not PlatformEditorViewModel vm
            || sender is not FrameworkElement preview)
        {
            return;
        }

        var now = e.GetPosition(preview);
        var scale = preview.ActualWidth > 0 ? MapCompositor.PanelWidth / preview.ActualWidth : 1.0;
        vm.DragPan((now.X - from.X) * scale, (now.Y - from.Y) * scale);
        _panFrom = now;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_panFrom is null || sender is not UIElement preview)
        {
            return;
        }

        _panFrom = null;
        preview.ReleaseMouseCapture();
    }

    /// <summary>3.10: the wheel zooms 10% a notch about the point under the cursor, in the stage's own pixels.</summary>
    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not PlatformEditorViewModel { CanPan: true } vm || sender is not FrameworkElement preview)
        {
            return;
        }

        var at = e.GetPosition(preview);
        var scale = preview.ActualWidth > 0 ? MapCompositor.PanelWidth / preview.ActualWidth : 1.0;
        vm.WheelZoom(at.X * scale, at.Y * scale, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine);
        e.Handled = true;
    }
}
