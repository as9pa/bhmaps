using System.Collections.ObjectModel;
using System.Diagnostics;
using BhMaps.App.Services;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5: the library's packs, one row each, with the whole-pack operations of spec 6.5 beside them.</summary>
public partial class PacksViewModel : PageViewModel
{
    public PacksViewModel(MainViewModel shell)
        : base(shell)
    {
        Rows = [];
    }

    public override string Title => "Packs";

    /// <summary>Every pack in the library, in scan order.</summary>
    public ObservableCollection<PackRowViewModel> Rows { get; }

    /// <summary>Whether to show the empty-library invitation instead of the list (spec 7.8). Set only by a scan,
    /// so it stays false while the first one is still running rather than flashing the empty state.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    /// <summary>The shell's own import flow (spec 6.9). The header reuses it rather than starting a second one;
    /// the rescan it ends with is what refreshes this list.</summary>
    public IAsyncRelayCommand ImportCommand => Shell.ImportCommand;

    public override void Refresh(ScanSnapshot snapshot)
    {
        Rows.Clear();
        foreach (var pack in snapshot.Packs)
        {
            Rows.Add(new PackRowViewModel(pack));
        }

        IsEmpty = Rows.Count == 0;
    }

    /// <summary>A click on the row body opens the pack's detail page.</summary>
    [RelayCommand]
    private void Open(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Shell.NavigateToPack(row.Pack);
    }

    /// <summary>Spec 6.5: every folder and every background the pack has, in one write. Only the slots the pack
    /// holds are touched, so packs stack. The launcher inside RunGameWriteAsync owns the game-running policy.</summary>
    [RelayCommand]
    private async Task ApplyAllAsync(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var pack = row.Pack;
        var gamePath = Shell.Services.GamePath;
        ApplyResult? result = null;
        await Shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            pack.RelativePaths,
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyPack(pack, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied");
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }
    }

    /// <summary>Copies the game folder into the Default pack. The confirm text and the busy boundary live on the
    /// shell, so this page and Settings ask the same question (spec 6.1).</summary>
    [RelayCommand]
    private Task CaptureDefaultsAsync() => Shell.CaptureDefaultsAsync();

    /// <summary>Spec 6.5: deletes the pack from the library after a confirm that names it. Inside RunBusyAsync so
    /// the sibling commands are disabled while a folder is going away underneath them (commit 375497e).</summary>
    [RelayCommand]
    private async Task RemoveAsync(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var pack = row.Pack;
        if (!Shell.Dialogs.Confirm(
            "Remove pack",
            $"Remove pack '{pack.Name}' and the {PackRowViewModel.Plural(pack.FileCount, "file")} in it? This cannot be undone."))
        {
            return;
        }

        var libraryPath = Shell.Services.LibraryPath;
        string? error = null;
        await Shell.RunBusyAsync(
            $"Removing {pack.Name}",
            (_, _) => Task.Run(() => { error = PackDeleter.Delete(libraryPath, pack.Name); }));
        if (error is not null)
        {
            Shell.Dialogs.Error("Could not remove pack", error);
        }

        await Shell.RescanAsync();
    }

    /// <summary>Spec 6.5: copies the pack folder to a folder the user picks. The library is untouched, so no rescan.</summary>
    [RelayCommand]
    private async Task ExportAsync(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var pack = row.Pack;
        var destination = Shell.Dialogs.PickFolder($"Export pack '{pack.Name}' to");
        if (string.IsNullOrEmpty(destination))
        {
            return;
        }

        ApplyResult? result = null;
        await Shell.RunBusyAsync(
            $"Exporting {pack.Name}",
            (progress, ct) => Task.Run(() => { result = PackExporter.Export(pack, destination, progress, ct); }, ct));
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be exported", result.Failures);
        }
    }

    [RelayCommand]
    private void OpenFolder(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        OpenInExplorer(row.Pack.FullPath);
    }

    [RelayCommand]
    private void OpenLibrary() => OpenInExplorer(Shell.Services.LibraryPath);

    /// <summary>The v1 idiom for showing a folder. A missing one is reported rather than handed to explorer, which
    /// would quietly open somewhere else instead.</summary>
    private void OpenInExplorer(string path)
    {
        if (!Directory.Exists(path))
        {
            Shell.Dialogs.Error("Folder not found", path);
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
    }
}
