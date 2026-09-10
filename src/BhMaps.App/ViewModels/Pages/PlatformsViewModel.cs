using BhMaps.App.Services;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.4. Stub until Task 28 fills in the per-map and all-maps platform sets.</summary>
public partial class PlatformsViewModel : PageViewModel
{
    public PlatformsViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    public override string Title => "Platforms";

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
