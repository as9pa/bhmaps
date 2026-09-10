using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One folder's import: the plan the dialog built for it and the pack it becomes. The shell runs these,
/// because the copying belongs to its busy boundary.</summary>
public sealed record ImportJob(ImportPlan Plan, string PackName);

/// <summary>One picked folder in the Folders list. The plan and the name error both come from the owner: a plan
/// is a walk of the tree, and a name is only good against the other entries and the packs already there.</summary>
public sealed partial class ImportFolderViewModel : ObservableObject
{
    private readonly ImportViewModel _owner;

    public ImportFolderViewModel(ImportViewModel owner, string sourcePath, string packName)
    {
        _owner = owner;
        SourcePath = sourcePath;
        PackName = packName;
    }

    /// <summary>Where this folder's images go. Null until the scan has run, and again if it failed.</summary>
    public ImportPlan? Plan { get; set; }

    [ObservableProperty]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    public partial string PackName { get; set; }

    public string NameError => _owner.NameErrorFor(this);

    /// <summary>Shown in place of the error when the name is a pack the library already has.</summary>
    public string NameNote => _owner.NameNoteFor(this);

    /// <summary>Called by the owner when any entry's name changed: a duplicate is an error on both entries, so
    /// renaming one of them clears the other's line too.</summary>
    public void RefreshNameLine()
    {
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(NameNote));
    }

    partial void OnPackNameChanged(string value) => _owner.FolderListChanged();
}
