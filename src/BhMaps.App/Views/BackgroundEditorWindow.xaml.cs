using System.Windows;
using System.Windows.Input;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class BackgroundEditorWindow : Window
{
    /// <summary>Where the drag was last seen, in the preview's own coordinates; null when nothing is dragging.</summary>
    private Point? _panFrom;

    public BackgroundEditorWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is BackgroundEditorViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
            }

            if (e.NewValue is BackgroundEditorViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
            }
        };
    }

    private void OnCloseRequested(bool ok) => DialogResult = ok;

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is BackgroundEditorViewModel vm
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            vm.AcceptDroppedFile(files[0]);
            e.Handled = true;
        }
    }

    /// <summary>The throttle draws from the working bitmap while the thumb moves; the drag ending is when the
    /// newest values are worth one more draw with nothing queued behind it (spec 7.2).</summary>
    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is BackgroundEditorViewModel vm)
        {
            vm.RenderFinal();
        }
    }

    /// <summary>3.10: the preview drags the picture in Fill. The Viewbox draws the 640 by 360 preview at whatever
    /// size the card leaves it, so a delta in the Image's own coordinates is scaled back to the preview's pixels
    /// before the view model sees it. A double-click puts the picture back in the middle.</summary>
    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not BackgroundEditorViewModel { IsFill: true } vm || sender is not UIElement preview)
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
            || DataContext is not BackgroundEditorViewModel vm
            || sender is not FrameworkElement preview)
        {
            return;
        }

        var now = e.GetPosition(preview);
        var scale = preview.ActualWidth > 0 ? BackgroundEditorViewModel.PreviewWidth / preview.ActualWidth : 1.0;
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

    /// <summary>3.10: the wheel zooms 10% a notch about the point under the cursor, in the preview's own pixels.</summary>
    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not BackgroundEditorViewModel { IsFill: true } vm || sender is not FrameworkElement preview)
        {
            return;
        }

        var at = e.GetPosition(preview);
        var scale = preview.ActualWidth > 0 ? BackgroundEditorViewModel.PreviewWidth / preview.ActualWidth : 1.0;
        vm.WheelZoom(at.X * scale, at.Y * scale, e.Delta / (double)Mouse.MouseWheelDeltaForOneLine);
        e.Handled = true;
    }
}
