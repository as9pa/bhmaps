using System.Windows;
using System.Windows.Input;

namespace BhMaps.App.Views;

/// <summary>Spec 4.2: the chooser's chrome. The window closes with true when a row is picked and with false or
/// nothing otherwise; the caller reads the view model's Selected. Nothing here writes to the game.</summary>
public partial class ChooserWindow : Window
{
    public ChooserWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Search.Focus();
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
