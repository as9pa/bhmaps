using BhMaps.App.Services;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5. Stub until Task 30 fills in the four sections. The pack comes from
/// <see cref="MainViewModel.NavigateToPack" />, so the title is a pack name rather than a fixed word.</summary>
public partial class PackDetailViewModel : PageViewModel
{
    public PackDetailViewModel(MainViewModel shell)
        : base(shell)
    {
    }

    /// <summary>The pack the page is showing. Null until the shell navigates to one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial Pack? Pack { get; set; }

    public override string Title => Pack?.Name ?? "Pack";

    public override void Refresh(ScanSnapshot snapshot)
    {
    }
}
