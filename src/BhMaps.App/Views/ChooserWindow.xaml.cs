using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BhMaps.App.Views;

/// <summary>Spec 4.2: the chooser's chrome. The window closes with true when a row is picked and with false or
/// nothing otherwise; the caller reads the view model's Selected. Nothing here writes to the game.</summary>
public partial class ChooserWindow : Window
{
    /// <summary>3.1: the grid is resizable, and a chooser opened a second time comes back the size it was left.
    /// For the session only: a window size is not worth a line in the settings file.</summary>
    private static Size _lastSize;

    public ChooserWindow()
    {
        InitializeComponent();
        if (_lastSize is { Width: > 0, Height: > 0 })
        {
            Width = _lastSize.Width;
            Height = _lastSize.Height;
        }

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The search box keeps the focus; the grid opens on its first row so Enter applies something (3.1).
        Search.Focus();
        if (DataContext is ViewModels.ChooserViewModel { Selected: null } vm && vm.Rows.Count > 0)
        {
            vm.Selected = vm.Rows[0];
        }
    }

    /// <summary>Down out of the search box walks into the grid, where the arrow keys are the ListBox's own.</summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down || Rows.SelectedItem is not { } selected)
        {
            return;
        }

        Rows.ScrollIntoView(selected);
        Rows.UpdateLayout();
        if (Rows.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem item)
        {
            item.Focus();
            e.Handled = true;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _lastSize = new Size(ActualWidth, ActualHeight);
    }

    private void OnApply(object sender, RoutedEventArgs e) => Accept();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    /// <summary>Enter in the search box would otherwise fire IsDefault with nothing picked, so the guard is here
    /// as well as on the button's IsEnabled.</summary>
    private void Accept()
    {
        if (DataContext is ViewModels.ChooserViewModel { CanApply: true })
        {
            DialogResult = true;
        }
    }
}
