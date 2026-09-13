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

    /// <summary>Spec 11: the same refresh, told which map folders the write that led to the scan touched. Only the
    /// page that shows those maps has anything to do with them, so every other page falls through.</summary>
    public virtual void Refresh(ScanSnapshot snapshot, IReadOnlyList<string>? writtenFolders) => Refresh(snapshot);
}
