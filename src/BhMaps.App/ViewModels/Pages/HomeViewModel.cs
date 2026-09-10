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

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
