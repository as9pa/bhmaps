using System.Windows;
using System.Windows.Input;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class DialogWindow : Window
{
    public DialogWindow(DialogViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Title = model.Title;

        // Nothing here is an input, so focus starts on the button the dialog is asking for.
        Loaded += (_, _) => OkButton.Focus();
    }

    /// <summary>The one-button kinds have no IsCancel button to give Escape its meaning, so there Escape is OK.
    /// Confirm keeps Escape as Cancel, which its own IsCancel button already handles.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is DialogViewModel { HasCancel: false })
        {
            DialogResult = true;
            e.Handled = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
