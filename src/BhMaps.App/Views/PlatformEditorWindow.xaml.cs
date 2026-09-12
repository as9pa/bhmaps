using System.Threading;
using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class PlatformEditorWindow : Window
{
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
}
