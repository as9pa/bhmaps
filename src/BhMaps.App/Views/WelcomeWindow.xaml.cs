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

    /// <summary>Neither the title bar nor Escape may leave while the capture runs: closing here would start the
    /// shell on top of a copy still going on a thread pool thread. The line's own Cancel is the way out (3.0),
    /// and it stops the copy before this window can go anywhere.</summary>
    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (DataContext is WelcomeViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }
    }
}
