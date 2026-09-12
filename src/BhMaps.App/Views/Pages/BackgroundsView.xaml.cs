using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class BackgroundsView : UserControl
{
    public BackgroundsView()
    {
        InitializeComponent();
    }

    /// <summary>Addendum B's keyboard, shared with the Platforms page so the two cannot answer a key differently.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => RowKeys.OnPreviewKeyDown(sender, e);
}
