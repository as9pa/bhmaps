using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class PlatformsView : UserControl
{
    public PlatformsView()
    {
        InitializeComponent();
    }

    /// <summary>Addendum C: the keyboard is Backgrounds', through the one handler both pages share.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => RowKeys.OnPreviewKeyDown(sender, e);
}
