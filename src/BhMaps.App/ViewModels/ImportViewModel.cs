using System.Collections.ObjectModel;
using BhMaps.App.Services;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>Any number of picked folders, each becoming its own pack: one plan and one pack name per folder, and
/// the table below shows the plan of the folder that is selected. The dialog only collects the answers; the shell
/// runs the copying, because that belongs to its busy boundary.</summary>
public partial class ImportViewModel : ObservableObject
{
    private const string PickHint =
        "Pick one or more folders to import. Every .png and .jpg inside a folder, at any depth, is routed to a game "
        + "folder, and each folder becomes its own pack.";

    private readonly GameTree _tree;
    private readonly IDialogs _dialogs;

    /// <summary>The packs already in the library, so a second pack cannot be given a name one of them holds.</summary>
    private readonly HashSet<string> _existingPacks;

    public ImportViewModel(GameTree tree, IReadOnlyList<string> existingPacks, IDialogs dialogs)
    {
        _tree = tree;
        _dialogs = dialogs;
        _existingPacks = new HashSet<string>(existingPacks, StringComparer.OrdinalIgnoreCase);
        AllFolders = tree.Folders.Select(f => f.Name).ToList();
        SourcePath = "";
        Summary = PickHint;
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> AllFolders { get; }

    /// <summary>The picked folders, in the order they were added.</summary>
    public ObservableCollection<ImportFolderViewModel> Folders { get; } = new();

    /// <summary>The selected folder's plan rows. Rebuilt on every selection, because the plan itself holds what
    /// the rows edit.</summary>
    public ObservableCollection<ImportRowViewModel> Rows { get; } = new();

    /// <summary>What the shell imports: one plan and one name per folder that scanned.</summary>
    public IReadOnlyList<ImportJob> Jobs =>
        Folders.Where(f => f.Plan is not null).Select(f => new ImportJob(f.Plan!, f.PackName.Trim())).ToList();

    [ObservableProperty]
    public partial ImportFolderViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    public partial string SourcePath { get; set; }

    /// <summary>How many of the selected folder's images will be copied, which is what the summary line counts.</summary>
    [ObservableProperty]
    public partial int IncludedCount { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    /// <summary>True while a source folder is being walked off the UI thread (spec 5.5). Set around a whole batch,
    /// so picking several folders disables the window once rather than once per folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand), nameof(ScanCommand), nameof(ImportCommand))]
    public partial bool IsScanning { get; set; }

    /// <summary>The button carries the count, because with several folders that is the number being checked.</summary>
    public string ImportButtonText => Folders.Count > 1 ? $"Import {Folders.Count} packs" : "Import";

    /// <summary>Every listed folder has to be able to become a pack of its own: a plan, a name nothing else uses,
    /// and between them at least one file to copy.</summary>
    public bool CanImport =>
        !IsScanning
        && Folders.Count > 0
        && Folders.All(f => f.Plan is not null && f.NameError.Length == 0)
        && Folders.Sum(f => f.Plan!.IncludedCount) > 0;

    /// <summary>The line under one entry's name field: the folder-name rules, then the one collision a pack cannot
    /// survive, which is two folders in this list writing into the same new pack.</summary>
    internal string NameErrorFor(ImportFolderViewModel entry)
    {
        if (!PackNameValidator.IsValid(entry.PackName, out var error))
        {
            return error;
        }

        return Folders.Any(f => f != entry && f.PackName.Equals(entry.PackName, StringComparison.OrdinalIgnoreCase))
            ? "Another folder already uses this name"
            : "";
    }

    /// <summary>The quiet line under a name a pack in the library already has: that import adds to the pack rather
    /// than making one, which is allowed and is what the confirm on Import asks about. Never shown beside an
    /// error, because the error is the line that has to be read.</summary>
    internal string NameNoteFor(ImportFolderViewModel entry) =>
        entry.NameError.Length == 0 && _existingPacks.Contains(entry.PackName) ? "Adds to the existing pack" : "";

    /// <summary>An entry was added, removed, or renamed: every entry's line under the name field is stale, and so
    /// are the Import button's label and its enabled state.</summary>
    internal void FolderListChanged()
    {
        foreach (var entry in Folders)
        {
            entry.RefreshNameLine();
        }

        OnPropertyChanged(nameof(ImportButtonText));
        OnPropertyChanged(nameof(CanImport));
        ImportCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Dropped paths: the folders in them are added and everything else is ignored, so a file dropped
    /// beside them does not refuse the drop. A drop that lands mid-scan is dropped, because the two would
    /// interleave; the Browse and Scan buttons are already off while one runs.</summary>
    public async Task AcceptDroppedFoldersAsync(IEnumerable<string> paths)
    {
        if (IsScanning)
        {
            return;
        }

        await AddFoldersAsync(paths.Where(Directory.Exists).ToList());
    }

    internal void SetInclude(ImportRow row, bool include)
    {
        Selected!.Plan!.SetInclude(row, include);
        SyncRows();
    }

    internal void AssignFolder(ImportRow row, string folder)
    {
        Selected!.Plan!.AssignFolder(row, folder);
        SyncRows();
    }

    partial void OnSelectedChanged(ImportFolderViewModel? value)
    {
        SourcePath = value?.SourcePath ?? "";
        ShowSelectedPlan();
    }

    private bool CanBrowse() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private async Task BrowseAsync()
    {
        if (_dialogs.PickFolders("Choose folders to import") is not { Count: > 0 } folders)
        {
            return;
        }

        await AddFoldersAsync(folders);
    }

    private bool CanScan() => !IsScanning && Directory.Exists(SourcePath);

    /// <summary>The Source folder box edits the selected folder, so Scan re-reads what it names. With nothing
    /// selected, which is an empty list, it adds the folder named there instead.</summary>
    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (Selected is not { } entry)
        {
            await AddFoldersAsync([SourcePath]);
            return;
        }

        entry.SourcePath = SourcePath;
        await ScanAllAsync([entry]);
    }

    /// <summary>Removing the selected folder leaves the selection on its neighbour, so the table keeps showing a
    /// plan while any folder is left.</summary>
    [RelayCommand]
    private void Remove(ImportFolderViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        // Both are read before the removal: afterwards the index belongs to the neighbour, and the list has
        // already cleared its own selection, which through the two-way binding has cleared this one.
        var index = Folders.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        var wasSelected = Selected == entry;
        Folders.RemoveAt(index);
        if (wasSelected)
        {
            Selected = Folders.Count == 0 ? null : Folders[Math.Min(index, Folders.Count - 1)];
        }

        FolderListChanged();
    }

    /// <summary>Spec 6: a folder can be imported into a pack that is already there, but not silently. The question
    /// is asked here rather than in the shell so that declining comes back to this window with the list intact.</summary>
    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var existing = Folders.Where(f => _existingPacks.Contains(f.PackName)).Select(f => f.PackName).ToList();
        if (existing.Count > 0
            && !_dialogs.Confirm(
                existing.Count == 1 ? "Add to existing pack" : "Add to existing packs",
                AddToExistingMessage(existing)))
        {
            return;
        }

        CloseRequested?.Invoke(true);
    }

    private static string AddToExistingMessage(IReadOnlyList<string> packs) =>
        (packs.Count == 1
            ? "This pack already exists. Add to it? Files with the same name are overwritten; other files stay."
            : $"These {packs.Count} packs already exist. Add to them? Files with the same name are overwritten; other files stay.")
        + "\n\n"
        + string.Join("\n", packs);

    /// <summary>Adds one entry per folder and scans them in turn. A folder already listed is skipped, so dropping
    /// the same folder twice does not ask for the same pack twice.</summary>
    private async Task AddFoldersAsync(IReadOnlyList<string> paths)
    {
        var added = new List<ImportFolderViewModel>();
        foreach (var path in paths)
        {
            if (Folders.Any(f => f.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // The pack is named after the folder, which is what the single-folder import has always filled in.
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var entry = new ImportFolderViewModel(this, path, name);
            Folders.Add(entry);
            Selected ??= entry;
            added.Add(entry);
        }

        if (added.Count == 0)
        {
            return;
        }

        FolderListChanged();
        await ScanAllAsync(added);
    }

    /// <summary>Spec 5.5: the dialog is modal, so walking a source folder must not run on the UI thread. A folder
    /// that cannot be read leaves its entry planless, which is what keeps Import off until it goes.</summary>
    private async Task ScanAllAsync(IReadOnlyList<ImportFolderViewModel> entries)
    {
        IsScanning = true;
        try
        {
            foreach (var entry in entries)
            {
                var source = entry.SourcePath;
                try
                {
                    entry.Plan = await Task.Run(() => ImportRouter.Plan(source, _tree));
                }
                catch (Exception ex)
                {
                    entry.Plan = null;
                    _dialogs.Error("Could not read that folder", ex.Message);
                }
            }
        }
        finally
        {
            IsScanning = false;
            ShowSelectedPlan();
        }
    }

    private void ShowSelectedPlan()
    {
        Rows.Clear();
        foreach (var row in Selected?.Plan?.Rows ?? [])
        {
            Rows.Add(new ImportRowViewModel(this, row));
        }

        RefreshCounts();
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
        IncludedCount = Selected?.Plan?.IncludedCount ?? 0;
        var ambiguous = Rows.Count(r => r.Row.Route == Route.Ambiguous);
        var unmatched = Rows.Count(r => r.Row.Route == Route.Unmatched);
        var conflicts = Rows.Count(r => r.Row.Conflict);
        Summary = Selected?.Plan is null
            ? PickHint
            : $"{Rows.Count} images found: {IncludedCount} will be imported, {ambiguous} ambiguous, {unmatched} unmatched, {conflicts} in conflict. "
                + "Pick a target folder for an ambiguous or unmatched row to include it. Only one of two rows with the same target can be included.";

        // Not through IncludedCount's own notifications: the button also turns on and off with the other folders'
        // plans, which this one's count says nothing about.
        OnPropertyChanged(nameof(CanImport));
        ImportCommand.NotifyCanExecuteChanged();
    }
}
