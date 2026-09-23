using System.ComponentModel;
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

        // A page menu bakes the shell's gates into its lines, so a change in either one has to make a new list
        // (3.0): the header binds PageMenu once and reads the enabled flags off the objects it was given.
        Shell.PropertyChanged += OnShellPropertyChanged;
    }

    protected MainViewModel Shell { get; }

    public abstract string Title { get; }

    /// <summary>The lines behind the page header's menu button, or null for a page with no menu (3.0).</summary>
    public virtual IReadOnlyList<TileMenuCommand>? PageMenu => null;

    /// <summary>3.2 P1 and P2: the switch's chips and its selection, handed through to the shell so the switch on
    /// every page is the one setting.</summary>
    public IReadOnlyList<string> PreviewModeChips => MainViewModel.PreviewModeChips;

    public string PreviewModeChip
    {
        get => Shell.PreviewModeChip;
        set => Shell.PreviewModeChip = value;
    }

    /// <summary>Called after every scan. Rebuild the page's collections here.</summary>
    public abstract void Refresh(ScanSnapshot snapshot);

    /// <summary>Spec 11: the same refresh, told which map folders the write that led to the scan touched. Only the
    /// page that shows those maps has anything to do with them, so every other page falls through.</summary>
    public virtual void Refresh(ScanSnapshot snapshot, IReadOnlyList<string>? writtenFolders) => Refresh(snapshot);

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CanWrite) or nameof(MainViewModel.IsBusy)
            or nameof(MainViewModel.IsNotBusy))
        {
            OnPropertyChanged(nameof(PageMenu));
        }
        else if (e.PropertyName == nameof(MainViewModel.PreviewModeChip))
        {
            OnPropertyChanged(nameof(PreviewModeChip));
            OnPreviewModeChanged();
        }
    }

    /// <summary>3.2: the switch changed, on this page or another. A page that draws previews redraws them here;
    /// the preview cache keys each mode apart, so a mode drawn before comes straight back from disk.</summary>
    protected virtual void OnPreviewModeChanged()
    {
    }
}
