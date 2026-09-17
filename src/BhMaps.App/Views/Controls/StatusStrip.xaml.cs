using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>The status strip (3.0): one line at the foot of the content area saying what is running, what the
/// last operation did, or why it did nothing, with the links that line offers and a close button. It is bound to
/// the shell's StatusViewModel, which is the only thing that decides what it says.</summary>
public partial class StatusStrip : UserControl
{
    public StatusStrip()
    {
        InitializeComponent();
    }
}
