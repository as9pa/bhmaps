using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BhMaps.App.ViewModels;
using BhMaps.App.Views.Controls;
using BhMaps.Core.Imaging;

namespace BhMaps.App.Views;

public partial class PlatformEditorWindow : Window
{
    /// <summary>Where the drag was last seen, in the preview's own coordinates; null when nothing is dragging.</summary>
    private Point? _panFrom;

    /// <summary>G6: the closest the view goes, as a multiple of fit.</summary>
    private const double MaxViewZoom = 8;

    /// <summary>G6: one wheel notch with Ctrl held looks this much closer, or this much further back.</summary>
    private const double ViewZoomStep = 1.25;

    /// <summary>G6: the most bitmap pixels the zoomed preview is composed with per stage pixel. 3 is a 4K bitmap,
    /// and a render that size still keeps up with the sliders; past it the view scales the bitmap up, smoothly.
    /// </summary>
    private const double MaxPreviewScale = 3;

    /// <summary>G6: how long the zoom has to stay put before the preview is composed again at the new detail.</summary>
    private static readonly TimeSpan SharpenDelay = TimeSpan.FromMilliseconds(200);

    private readonly DispatcherTimer _sharpen;

    /// <summary>G6: the view's zoom, 1 at fit, and its offset in the stage's pixels; see <see
    /// cref="ViewTransform"/>.</summary>
    private double _viewZoom = 1;

    private Vector _viewOffset;

    /// <summary>G6: where a Space+drag pan was last seen, in the stage's coordinates; null when the view is not
    /// being panned.</summary>
    private Point? _viewPanFrom;

    private bool _spaceHeld;

    public PlatformEditorWindow()
    {
        InitializeComponent();
        _sharpen = new DispatcherTimer { Interval = SharpenDelay };
        _sharpen.Tick += (_, _) => Sharpen();
        Closed += (_, _) => _sharpen.Stop();
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
                ResetView();

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
        else if (e.PropertyName == nameof(PlatformEditorViewModel.CurrentIndex))
        {
            // G6: another layout is another picture, so looking closer starts again from fit.
            ResetView();
        }
    }

    private void StageBox_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateGuidesTransform();

    /// <summary>G1: level space to the guides' own pixels: the view model's stage transform for the camera on show,
    /// then wherever the 1280 by 720 picture really is on screen. That second part is read back from the visual
    /// tree, the Viewbox's fit and the view's zoom and pan included, so the guides cannot part from the art.
    /// </summary>
    private void UpdateGuidesTransform()
    {
        if (DataContext is not PlatformEditorViewModel vm)
        {
            return;
        }

        var levelToScreen = vm.LevelToStage;
        levelToScreen.Append(ArtToGuides());
        GuidesLayer.LevelToScreen = levelToScreen;
    }

    /// <summary>G6: the picture's 1280 by 720 to the guides' pixels as WPF draws it. Before the first layout, or
    /// were the two ever not in one tree, the same thing composed by hand.</summary>
    private Matrix ArtToGuides()
    {
        if (StageBox.ActualWidth > 0
            && StageArt.IsDescendantOf(Stage)
            && StageArt.TransformToVisual(GuidesLayer) is Transform drawn)
        {
            return drawn.Value;
        }

        var composed = StageToCard();
        composed.Append(ViewTransform());
        return composed;
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

        // The layout offsets alone: TranslatePoint would also carry the view's render transform, which comes after
        // this, in ViewTransform.
        var origin = (Point)(VisualTreeHelper.GetOffset(StageBox) + VisualTreeHelper.GetOffset(StageViewport)
            - VisualTreeHelper.GetOffset(GuidesLayer));
        var fit = Matrix.Identity;
        fit.Scale(scale, scale);
        fit.Translate(
            origin.X + ((StageBox.ActualWidth - (MapCompositor.PanelWidth * scale)) / 2),
            origin.Y + ((StageBox.ActualHeight - (MapCompositor.PanelHeight * scale)) / 2));
        return fit;
    }

    /// <summary>G6: the view's own zoom and pan, in the stage's pixels, applied last. It is the render transform
    /// of the viewport that holds the Viewbox; the viewport and the guides both fill the stage, so their pixels are
    /// the same pixels. Identity at fit.</summary>
    private Matrix ViewTransform() => new(_viewZoom, 0, 0, _viewZoom, _viewOffset.X, _viewOffset.Y);

    /// <summary>G6: the view back to fit, and the preview back to its own 1280 by 720 straight away.</summary>
    private void ResetView()
    {
        _viewZoom = 1;
        _viewOffset = default;
        EndViewPan();
        ApplyView();
        _sharpen.Stop();
        if (DataContext is PlatformEditorViewModel vm)
        {
            vm.PreviewScale = 1;
        }
    }

    /// <summary>G6: Ctrl+scroll looks closer about the point under the pointer, which stays where it is.</summary>
    private void ZoomViewAt(Point at, double factor)
    {
        var zoom = Math.Clamp(_viewZoom * factor, 1, MaxViewZoom);
        var under = new Point((at.X - _viewOffset.X) / _viewZoom, (at.Y - _viewOffset.Y) / _viewZoom);
        _viewOffset = new Vector(at.X - (zoom * under.X), at.Y - (zoom * under.Y));
        _viewZoom = zoom;
        ClampView();
    }

    /// <summary>G6: never further back than fit, and never panned past the stage's edges: an axis the zoomed stage
    /// overfills keeps both edges at or past the view's, and one it does not fill yet stays centred.</summary>
    private void ClampView()
    {
        var fit = StageToCard();
        if (_viewZoom <= 1.0001 || fit.IsIdentity)
        {
            _viewZoom = 1;
            _viewOffset = default;
        }
        else
        {
            _viewOffset = new Vector(
                ClampAxis(_viewOffset.X, fit.OffsetX, MapCompositor.PanelWidth * fit.M11, Stage.ActualWidth),
                ClampAxis(_viewOffset.Y, fit.OffsetY, MapCompositor.PanelHeight * fit.M22, Stage.ActualHeight));
        }

        ApplyView();
        _sharpen.Stop();
        _sharpen.Start();
    }

    private double ClampAxis(double offset, double start, double length, double view)
    {
        var zoomed = length * _viewZoom;
        if (zoomed <= view)
        {
            return ((view - zoomed) / 2) - (start * _viewZoom);
        }

        return Math.Clamp(offset, view - ((start + length) * _viewZoom), -start * _viewZoom);
    }

    /// <summary>G6: the one matrix to the Viewbox and the guides, and the chip and the cursor that go with it.</summary>
    private void ApplyView()
    {
        StageView.Matrix = ViewTransform();
        UpdateGuidesTransform();
        ZoomChip.Visibility = _viewZoom > 1 ? Visibility.Visible : Visibility.Collapsed;
        ZoomChipText.Text = $"{Math.Round(_viewZoom * 100)}%";
        UpdateViewCursor();
    }

    /// <summary>G6: once the zoom has settled, the stage is composed at about as many pixels as the zoomed view
    /// shows, so looking closer shows the art's own detail rather than the fit preview blown up. At fit it is the
    /// plain 1280 by 720.</summary>
    private void Sharpen()
    {
        _sharpen.Stop();
        if (DataContext is not PlatformEditorViewModel vm)
        {
            return;
        }

        var shown = _viewZoom * StageToCard().M11 * VisualTreeHelper.GetDpi(this).DpiScaleX;
        vm.PreviewScale = _viewZoom > 1 ? Math.Clamp(Math.Ceiling(shown * 2) / 2, 1, MaxPreviewScale) : 1;
    }

    /// <summary>G6: Space held over a zoomed stage, or a pan under way, shows the hand over everything on the
    /// stage, the picture's own move cursor included.</summary>
    private void UpdateViewCursor()
    {
        var grab = (_spaceHeld && _viewZoom > 1) || _viewPanFrom is not null;
        Stage.Cursor = grab ? Cursors.Hand : null;
        Stage.ForceCursor = grab;
    }

    private void EndViewPan()
    {
        _viewPanFrom = null;
        if (Stage.IsMouseCaptured)
        {
            Stage.ReleaseMouseCapture();
        }

        UpdateViewCursor();
    }

    private void Stage_SizeChanged(object sender, SizeChangedEventArgs e) => ClampView();

    /// <summary>G6: Ctrl+scroll anywhere on the stage is the view's. Handled here, it never reaches the picture's
    /// own wheel zoom under it.</summary>
    private void Stage_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || DataContext is not PlatformEditorViewModel { Preview: not null })
        {
            return;
        }

        ZoomViewAt(e.GetPosition(Stage), Math.Pow(ViewZoomStep, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine));
        e.Handled = true;
    }

    /// <summary>G6: Space is the pan key only while the stage has the focus, and it is handled, so it presses
    /// nothing else.</summary>
    private void Stage_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Stage.IsKeyboardFocused)
        {
            _spaceHeld = true;
            UpdateViewCursor();
            e.Handled = true;
        }
    }

    private void Stage_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            UpdateViewCursor();
            e.Handled = true;
        }
    }

    private void Stage_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _spaceHeld = false;
        UpdateViewCursor();
    }

    private void Stage_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewPanFrom is not null)
        {
            EndViewPan();
            e.Handled = true;
        }
    }

    /// <summary>G6: the capture can go mid-drag without a button up (Alt+Tab, a dialog), and the pan and its hand
    /// cursor go with it. The event bubbles, so only the stage's own capture, the pan's, ends it.</summary>
    private void Stage_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_viewPanFrom is not null && ReferenceEquals(e.OriginalSource, Stage))
        {
            EndViewPan();
        }
    }

    /// <summary>G6: a double-click on the zoom chip goes back to fit.</summary>
    private void ZoomChip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetView();
        }

        e.Handled = true;
    }

    /// <summary>G3: a click on the stage gives it the focus, so the G key reaches its binding. G6: with Space held
    /// on a zoomed stage the press pans the view, and Ctrl+double-click goes back to fit; both are handled here,
    /// so the picture under them is neither dragged nor centred. Any other press is left for the drag and the
    /// double-click.</summary>
    private void Stage_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        Stage.Focus();
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_spaceHeld && _viewZoom > 1)
        {
            _viewPanFrom = e.GetPosition(Stage);
            Stage.CaptureMouse();
            UpdateViewCursor();
            e.Handled = true;
        }
        else if (e.ClickCount == 2 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ResetView();
            e.Handled = true;
        }
    }

    /// <summary>G1: the piece under the pointer is the one whose box holds the point taken back into level space;
    /// where boxes stack, the one drawn last is on top. G6: the guides' transform carries the view's zoom, so its
    /// inverse takes the pointer back through it too. During a Space+drag the move pans the view instead.</summary>
    private void Stage_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewPanFrom is { } from)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndViewPan();
                return;
            }

            var now = e.GetPosition(Stage);
            _viewOffset += now - from;
            _viewPanFrom = now;
            ClampView();
            return;
        }

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
