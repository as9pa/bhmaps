using System.Windows;
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
}
