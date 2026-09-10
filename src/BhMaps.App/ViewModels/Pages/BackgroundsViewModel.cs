using BhMaps.App.Services;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.3. Stub until Task 26 fills in the universal background library.</summary>
public partial class BackgroundsViewModel : PageViewModel
{
    public BackgroundsViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    public override string Title => "Backgrounds";

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
