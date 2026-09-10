using BhMaps.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>One page hosted by the shell's ContentControl (decision D10). The implicit DataTemplate in App.xaml
/// maps each concrete page to its view.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(MainViewModel shell)
    {
        Shell = shell;
    }

    protected MainViewModel Shell { get; }

    public abstract string Title { get; }

    /// <summary>Called after every scan. Rebuild the page's collections here.</summary>
    public abstract void Refresh(ScanSnapshot snapshot);
}
