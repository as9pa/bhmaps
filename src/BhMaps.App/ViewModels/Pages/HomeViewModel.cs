using BhMaps.App.Services;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.2. Stub until Task 24 fills in the chips, the zoom slider and the map cards.</summary>
public partial class HomeViewModel : PageViewModel
{
    public HomeViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    public override string Title => "Home";

    /// <summary>Opens one map's right panel. Empty until Task 24 fills the Home page in; the sidebar's
    /// autocomplete already calls it when Enter picks a name while Home is the current page.</summary>
    public void OpenMap(string folderName)
    {
    }

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
