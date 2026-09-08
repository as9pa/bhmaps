using System.Windows;
using System.Windows.Controls;

namespace BhMaps.App.Views;

public partial class FolderDetailView : UserControl
{
    public FolderDetailView()
    {
        InitializeComponent();
    }

    private void ApplyFrom_Click(object sender, RoutedEventArgs e) => DropdownButton.Open(sender);
}
