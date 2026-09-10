using BhMaps.App.Services;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5. Stub until Task 29 fills in the pack list and its actions.</summary>
public partial class PacksViewModel : PageViewModel
{
    public PacksViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    public override string Title => "Packs";

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
