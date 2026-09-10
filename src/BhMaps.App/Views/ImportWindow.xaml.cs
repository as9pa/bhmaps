using System.Windows;
using System.Windows.Input;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class ImportWindow : Window
{
    public ImportWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ImportViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
            }

            if (e.NewValue is ImportViewModel vm)
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

    /// <summary>Every dropped path goes to the view model, which keeps the folders and drops the rest: a file in
    /// the same drop is ignored rather than refusing the folders beside it. The scan it starts is awaited here,
    /// as the Browse button's is, so a folder that cannot be read still reports itself.</summary>
    private async void Window_PreviewDrop(object sender, DragEventArgs e)
    {
        if (DataContext is ImportViewModel vm
            && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
        {
            e.Handled = true;
            await vm.AcceptDroppedFoldersAsync(paths);
        }
    }

    /// <summary>Tabbing into a folder's name field or its Remove button selects that folder, so the table under
    /// the list always shows the plan of the entry being edited.</summary>
    private void FolderRow_GotFocus(object sender, RoutedEventArgs e) => SelectRow(sender);

    /// <summary>Clicking a row selects it. Its container is out of the tab order (Focusable="False") and a
    /// ListBoxItem that cannot take focus does not select itself on a click, so the click is taken here. Preview,
    /// and never handled, so the name field and the Remove button still get the same click.</summary>
    private void FolderRow_MouseDown(object sender, MouseButtonEventArgs e) => SelectRow(sender);

    private void SelectRow(object sender)
    {
        if (DataContext is ImportViewModel vm && sender is FrameworkElement { DataContext: ImportFolderViewModel entry })
        {
            vm.Selected = entry;
        }
    }
}
