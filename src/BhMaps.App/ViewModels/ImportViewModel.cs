using System.Collections.ObjectModel;
using BhMaps.App.Services;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class ImportViewModel : ObservableObject
{
    private readonly GameTree _tree;
    private readonly IDialogs _dialogs;

    public ImportViewModel(GameTree tree, IDialogs dialogs)
    {
        _tree = tree;
        _dialogs = dialogs;
        AllFolders = tree.Folders.Select(f => f.Name).ToList();
        PackName = "";
        SourcePath = "";
        Summary = "Pick a folder to import. Every .png and .jpg inside it, at any depth, is routed to a game folder.";
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> AllFolders { get; }

    public ObservableCollection<ImportRowViewModel> Rows { get; } = new();

    public ImportPlan? Plan { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(CanImport))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial string PackName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial int IncludedCount { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    /// <summary>True while the source folder is being walked off the UI thread (spec 5.5).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand), nameof(ScanCommand), nameof(ImportCommand))]
    public partial bool IsScanning { get; set; }

    public string NameError => PackNameValidator.IsValid(PackName, out var error) ? "" : error;

    public bool CanImport => !IsScanning && Plan is not null && IncludedCount > 0 && NameError.Length == 0;

    private bool CanBrowse() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        var folder = _dialogs.PickFolder("Choose a folder to import");
        if (folder is null)
        {
            return;
        }

        SourcePath = folder;
        if (string.IsNullOrWhiteSpace(PackName))
        {
            PackName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        await ScanAsync();
    }

    private bool CanScan() => !IsScanning && Directory.Exists(SourcePath);

    /// <summary>Spec 5.5: the dialog is modal, so walking a large source folder must not run on the UI thread.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var source = SourcePath;
        IsScanning = true;
        try
        {
            var plan = await Task.Run(() => ImportRouter.Plan(source, _tree));
            Plan = plan;
            Rows.Clear();
            foreach (var row in plan.Rows)
            {
                Rows.Add(new ImportRowViewModel(this, row));
            }
        }
        catch (Exception ex)
        {
            Plan = null;
            Rows.Clear();
            _dialogs.Error("Could not read that folder", ex.Message);
        }
        finally
        {
            IsScanning = false;
            RefreshCounts();
        }
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import() => CloseRequested?.Invoke(true);

    internal void SetInclude(ImportRow row, bool include)
    {
        Plan!.SetInclude(row, include);
        SyncRows();
    }

    internal void AssignFolder(ImportRow row, string folder)
    {
        Plan!.AssignFolder(row, folder);
        SyncRows();
    }

    private void SyncRows()
    {
        foreach (var row in Rows)
        {
            row.Refresh();
        }

        RefreshCounts();
    }

    private void RefreshCounts()
    {
        IncludedCount = Plan?.IncludedCount ?? 0;
        var ambiguous = Rows.Count(r => r.Row.Route == Route.Ambiguous);
        var unmatched = Rows.Count(r => r.Row.Route == Route.Unmatched);
        var conflicts = Rows.Count(r => r.Row.Conflict);
        Summary = $"{Rows.Count} images found: {IncludedCount} will be imported, {ambiguous} ambiguous, {unmatched} unmatched, {conflicts} in conflict. "
            + "Pick a target folder for an ambiguous or unmatched row to include it. Only one of two rows with the same target can be included.";
    }
}
