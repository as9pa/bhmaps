using System.Windows;
using System.Windows.Input;

namespace BhMaps.App.Views;

/// <summary>Spec 2.6 5.2: the import window's chrome. It closes with true when there is something to import and
/// the caller reads the view model's plan; nothing here writes to the library or the game.</summary>
public partial class ImportFromPackWindow : Window
{
    public ImportFromPackWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => From.Focus();
    }

    /// <summary>Enter fires IsDefault from anywhere in the window, so the guard is here as well as on the
    /// button's IsEnabled.</summary>
    private void OnImport(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ImportFromPackViewModel { CanImport: true })
        {
            DialogResult = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
