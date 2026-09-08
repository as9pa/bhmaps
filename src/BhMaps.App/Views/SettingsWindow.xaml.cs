using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsViewModel old)
            {
                old.CloseRequested -= OnCloseRequested;
            }

            if (e.NewValue is SettingsViewModel vm)
            {
                vm.CloseRequested += OnCloseRequested;
            }
        };
    }

    private void OnCloseRequested(bool ok) => DialogResult = ok;
}
