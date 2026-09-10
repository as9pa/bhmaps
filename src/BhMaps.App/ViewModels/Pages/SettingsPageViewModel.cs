using BhMaps.App.Services;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.6. Stub until Task 31 fills in one line per setting.</summary>
public partial class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    public override string Title => "Settings";

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
