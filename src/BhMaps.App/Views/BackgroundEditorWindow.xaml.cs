using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class BackgroundEditorWindow : Window
{
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
}
