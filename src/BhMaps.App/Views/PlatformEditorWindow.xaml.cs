using System.Threading;
using System.Windows;
using System.Windows.Input;
using BhMaps.App.ViewModels;
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
            }

            if (e.NewValue is PlatformEditorViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;

                // The rows draw their tile colour until a file has decoded, so the list is up before the
                // thumbnails are and nothing waits on the disk.
                _ = vm.LoadThumbnailsAsync(CancellationToken.None);
            }
        };
    }

    private void OnCloseRequested(bool ok) => DialogResult = ok;

    /// <summary>The throttle keeps drawing while the thumb moves; the drag ending is when the newest values are
    /// worth one more render with nothing queued behind it (spec 6).</summary>
    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is PlatformEditorViewModel vm)
        {
            vm.RenderFinal();
        }
    }

    /// <summary>Spec 6.2: the preview drags the picture that is laid across the platforms. The Viewbox draws the
    /// composed stage at whatever size the card leaves it, so a delta in the Image's own coordinates is scaled
    /// back to the stage's 1280 by 720 before the view model sees it.</summary>
    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not PlatformEditorViewModel { CanPan: true } || sender is not UIElement preview)
        {
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
}
