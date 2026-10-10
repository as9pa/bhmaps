using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

/// <summary>3.7.3: the background editor's Add as new popup. Add closes with true through the view model, so
/// Enter on a bad name does nothing, exactly as the disabled button does.</summary>
public partial class AddAsNewWindow : Window
{
    public AddAsNewWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is AddAsNewViewModel vm)
            {
                vm.CloseRequested += ok => DialogResult = ok;
            }
        };
        Loaded += (_, _) =>
        {
            NameBox.SelectAll();
            NameBox.Focus();
        };
    }
}
