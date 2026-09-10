using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class AddPicturesWindow : Window
{
    public AddPicturesWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AddPicturesViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
            }

            if (e.NewValue is AddPicturesViewModel vm)
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

    /// <summary>Every dropped path goes to the view model, which keeps the images and drops the rest: a folder in
    /// the same drop is ignored rather than refusing the pictures beside it.</summary>
    private void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is AddPicturesViewModel vm
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            vm.AcceptDroppedFiles(files);
            e.Handled = true;
        }
    }
}
