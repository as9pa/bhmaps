using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Pages;

public partial class PacksView : UserControl
{
    public PacksView()
    {
        InitializeComponent();
    }

    /// <summary>3.3 P1: F2 on a focused pack row renames its pack, the same command as the dots menu's line. The
    /// Default pack has no such line, and the command refuses it as well.</summary>
    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: PackRowViewModel row } && DataContext is PacksViewModel page
            && page.RenameCommand.CanExecute(row))
        {
            page.RenameCommand.Execute(row);
            e.Handled = true;
        }
    }
}
