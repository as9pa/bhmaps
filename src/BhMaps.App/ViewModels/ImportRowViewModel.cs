using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>Thin wrapper over one ImportRow. All mutation goes through the owner so the plan's rules hold.</summary>
public sealed class ImportRowViewModel(ImportViewModel owner, ImportRow row) : ObservableObject
{
    public ImportRow Row { get; } = row;

    public string FileName => Row.FileName;

    public string SourcePath => Row.SourcePath;

    public string RouteText => Row.Route.ToString();

    public string CandidatesText => string.Join(", ", Row.Candidates);

    public bool IsRouted => Row.Route == Route.Routed;

    public bool Conflict => Row.Conflict;

    public bool Include
    {
        get => Row.Include;
        set
        {
            if (value == Row.Include || (value && !IsRouted))
            {
                return;
            }

            owner.SetInclude(Row, value);
        }
    }

    public string? TargetFolder
    {
        get => Row.TargetFolder;
        set
        {
            if (value is null || string.Equals(value, Row.TargetFolder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            owner.AssignFolder(Row, value);
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Include));
        OnPropertyChanged(nameof(TargetFolder));
        OnPropertyChanged(nameof(RouteText));
        OnPropertyChanged(nameof(IsRouted));
        OnPropertyChanged(nameof(Conflict));
    }
}
