using System.ComponentModel;
using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is WelcomeViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
            }

            if (e.NewValue is WelcomeViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
            }
        };
    }

    private void OnCloseRequested(bool ok) => DialogResult = ok;

    /// <summary>The capture has no Cancel of its own, so neither the title bar nor Escape may leave while it
    /// runs: closing here would start the shell on top of a copy still going on a thread pool thread.</summary>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (DataContext is WelcomeViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }
    }
}
