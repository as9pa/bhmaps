using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5: the library's packs, one row each, with the whole-pack operations of spec 6.5 beside them.</summary>
public partial class PacksViewModel : PageViewModel
{
    /// <summary>Addendum E: the empty library's line. "No packs yet." was the v2 wording and said less.</summary>
    public const string EmptyText = "No packs in the library.";

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>What a background is: the game ships every one of its background slots as a JPEG.</summary>
    private const string BackgroundExtension = ".jpg";

    /// <summary>Spec 8: how often "applied just now" is read again, which is as often as it can change.</summary>
    private static readonly TimeSpan AppliedInterval = TimeSpan.FromSeconds(60);

    /// <summary>The snapshot the rows were built from, which the composites are drawn against.</summary>
    private ScanSnapshot? _snapshot;

    /// <summary>Cancels the loads the last scan's rows started. Replaced, never disposed, exactly as
    /// PackDetailViewModel does, because those loads still hold the token.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>Re-reads the rows' apply times once a minute. It runs for as long as the window does, because a
    /// page is not told when it is shown; the tick itself does nothing unless Packs is the page on screen, so a
    /// window sitting on Maps pays a comparison a minute for it.</summary>
    private readonly DispatcherTimer _appliedTimer;

    public PacksViewModel(MainViewModel shell)
        : base(shell)
    {
        Rows = [];
        _appliedTimer = new DispatcherTimer { Interval = AppliedInterval };
        _appliedTimer.Tick += (_, _) => RefreshApplied();
        _appliedTimer.Start();
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
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _snapshot = snapshot;
        Rows.Clear();

        // Spec 8: the packs arrive sorted by the shell, so the row that was just applied is already near the top;
        // all the page adds is the time each one carries on its counts line.
        var stamps = Shell.Services.Settings.LastApplied;
        foreach (var pack in snapshot.Packs)
        {
            var row = new PackRowViewModel(pack, MapsIn(snapshot.Catalog, pack));
            row.SetMenu(BuildMenu(row));
            row.SetLastApplied(stamps.TryGetValue(pack.Name, out var stamp) ? stamp : null);
            Rows.Add(row);
        }

        IsEmpty = Rows.Count == 0;
    }

    /// <summary>The apply times, read again from the stamps the rows already hold.</summary>
    private void RefreshApplied()
    {
        if (!ReferenceEquals(Shell.CurrentPage, this))
        {
            return;
        }

        foreach (var row in Rows)
        {
            row.RefreshCountsText();
        }
    }

    /// <summary>Addendum E: a row reads its files when it comes on screen and not before, so a library of forty
    /// packs does not compose forty leads to show six. Called from the row template's Loaded through
    /// RowRealiser; the row itself refuses every call after the first, so scrolling back costs nothing.</summary>
    public void RealiseRow(PackRowViewModel row)
    {
        if (_cts is { } cts && row.BeginRealise())
        {
            Load(row, cts.Token);
        }
    }

    /// <summary>The lead, then the strip left to right, one at a time, so the single render thread works down
    /// the list in the order the eye does. Fire and forget, like PackDetailViewModel.Load.</summary>
    private async void Load(PackRowViewModel row, CancellationToken ct)
    {
        try
        {
            row.Lead = await LeadAsync(row, ct);
            foreach (var tile in row.Previews)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(row.Pack, tile.Map, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a rescan.
        }
    }

    /// <summary>Addendum E's lead: the pack's first map put together. A pack with no map folders has no
    /// composite to draw, so its first background stands in; a pack with neither leaves the tile colour showing,
    /// which is what an empty slot looks like everywhere else in the app.</summary>
    private async Task<ImageSource?> LeadAsync(PackRowViewModel row, CancellationToken ct)
    {
        if (row.Maps.Count > 0)
        {
            return await ComposeAsync(row.Pack, row.Maps[0], ct);
        }

        var file = (row.Pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
            .FirstOrDefault(f => Path.GetExtension(f.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase));
        return file is null ? null : await Shell.Services.RowThumbnails.GetAsync(file.FullPath, ct);
    }

    /// <summary>One map with the pack's own files over it, through the preview cache so the render is queued on
    /// the one STA thread and kept on disk, and then through the shared decode cache so the same file never
    /// decodes twice. AssetSources takes the pack second, which means the pack's background when it ships one
    /// and the game's otherwise: what the pack looks like in game (addendum E). A composite that cannot be drawn
    /// falls back to a plain file tile; it is a picture, never an error dialog.</summary>
    private async Task<ImageSource?> ComposeAsync(Pack pack, MapEntry map, CancellationToken ct)
    {
        var previews = Shell.Services.Previews;
        var thumbnails = Shell.Services.RowThumbnails;
        var gamePath = Shell.Services.GamePath;
        ImageSource? image = null;

        // Spec 3.6: with no level data there are no camera bounds and no platform tree, so there is nothing to
        // compose, and the pack's own art is the only picture of the map there is.
        if (_snapshot?.Catalog.HasLevelData == true)
        {
            try
            {
                var sources = new AssetSources(gamePath, pack.FullPath);
                image = await Task.Run(
                    async () =>
                    {
                        var path = await previews
                            .GetOrRenderAsync(
                                map.BaseLevel, PackRowViewModel.ComposeWidth, PackRowViewModel.ComposeHeight,
                                sources, ct)
                            .ConfigureAwait(false);
                        return await thumbnails.GetAsync(path, ct).ConfigureAwait(false);
                    },
                    ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or FileFormatException or ArgumentException)
            {
                image = null;
            }
        }

        if (image is null
            && pack.FindFolder(map.FolderName) is { } folder
            && ThumbnailProvider.PickRepresentative(folder) is { } file)
        {
            image = await thumbnails.GetAsync(file.FullPath, ct);
        }

        return image;
    }

    /// <summary>The row's dots menu (addendum E, q7). Each line wraps the page's own command with this row as
    /// its parameter, because a TileMenuCommand carries no parameter of its own. Remove is last: it is the
    /// destructive one, and the pointer should not have to pass over it to reach another line.</summary>
    private IReadOnlyList<TileMenuCommand> BuildMenu(PackRowViewModel row) =>
    [
        new TileMenuCommand("Duplicate", new RelayCommand(() => DuplicateCommand.Execute(row))),
        new TileMenuCommand("Import from another pack...", new RelayCommand(() => ImportIntoCommand.Execute(row))),
        new TileMenuCommand("Export", new RelayCommand(() => ExportCommand.Execute(row))),
        new TileMenuCommand("Open folder", new RelayCommand(() => OpenFolderCommand.Execute(row))),
        new TileMenuCommand("Remove", new RelayCommand(() => RemoveCommand.Execute(row))),
    ];

    /// <summary>The maps the pack touches: the catalog maps it has at least one file for, in catalog order. A
    /// pack folder that is not a map, such as a theme folder other maps borrow from (spec 4), is not one of
    /// them, and neither is Backgrounds. The same rule pack detail uses, so a row's strip and the pack's own
    /// page show the same maps in the same order.</summary>
    private static IReadOnlyList<MapEntry> MapsIn(MapCatalog catalog, Pack pack) =>
        [.. catalog.Maps.Where(map => pack.FindFolder(map.FolderName) is { Files.Count: > 0 })];

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

    [RelayCommand]
    private Task ApplyAllAsync(PackRowViewModel? row) =>
        row is null ? Task.CompletedTask : ApplyAllAsync(row.Pack);

    /// <summary>Spec 6.5: every folder and every background the pack has, in one write. Only the slots the pack
    /// holds are touched, so packs stack. The launcher inside RunGameWriteAsync owns the game-running policy.
    /// Public because the pack detail page's header offers the same action and there is one implementation of it.</summary>
    public async Task ApplyAllAsync(Pack pack)
    {
        // Spec 6.3: the maps are named before more than one of them is written. A pack with files for one map, or
        // with backgrounds and nothing else, writes without asking.
        var maps = MapsTouched(pack);
        if (maps.Count > 1
            && !Shell.Dialogs.Confirm(
                $"Apply {pack.Name}",
                $"Apply {pack.Name} to these {maps.Count} maps?\n\n{string.Join(", ", maps)}"))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;

        // Spec 10.4: the maps the pack lands in are the maps whose map-select thumbnails the write redraws.
        IReadOnlyList<MapEntry> artMaps = Shell.Snapshot is { } snapshot
            ? PackApplier.MapsTouched(pack, snapshot.Catalog.Maps)
            : [];
        ApplyResult? result = null;
        await Shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            pack.RelativePaths,
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyPack(pack, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied",
            packName: pack.Name,
            artMaps: artMaps);
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }
    }

    /// <summary>Display names of the maps the pack writes into, for the confirm that names them.</summary>
    private IReadOnlyList<string> MapsTouched(Pack pack) =>
        Shell.Snapshot is { } snapshot
            ? [.. MapsIn(snapshot.Catalog, pack).Select(map => map.DisplayName)]
            : Array.Empty<string>();

    /// <summary>Copies the game folder into the Default pack. The confirm text and the busy boundary live on the
    /// shell, so this page and Settings ask the same question (spec 6.1).</summary>
    [RelayCommand]
    private Task CaptureDefaultsAsync() => Shell.CaptureDefaultsAsync();

    /// <summary>Spec 13: makes an empty pack to fill later from Add Image or the editors. Cancel and an empty name
    /// do nothing; a taken or unusable name is reported and nothing is created. The new pack has never been
    /// applied, so PackOrder lands it with the rest by name.</summary>
    [RelayCommand]
    private Task NewPackAsync() => Shell.NewPackAsync();

    /// <summary>Spec 6.5: deletes the pack from the library after a confirm that names it. Inside RunBusyAsync so
    /// the sibling commands are disabled while a folder is going away underneath them (commit 375497e). A tile
    /// copied out of this pack has nowhere to be copied from once the folder is gone, so the clipboard drops it
    /// rather than hold a source that no longer exists.</summary>
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
        var ok = await Shell.RunBusyAsync(
            $"Removing {pack.Name}",
            (_, _) => Task.Run(() => { error = PackDeleter.Delete(libraryPath, pack.Name); }));
        if (error is not null)
        {
            Shell.Dialogs.Error("Could not remove pack", error);
        }
        else if (ok)
        {
            Shell.SetLibraryDone($"Removed {pack.Name}");
            if (Shell.PackClipboard is { } held && held.Source.Name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase))
            {
                Shell.PackClipboard = null;
            }
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
        var ok = await Shell.RunBusyAsync(
            $"Exporting {pack.Name}",
            (progress, ct) => Task.Run(() => { result = PackExporter.Export(pack, destination, progress, ct); }, ct));
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be exported", result.Failures);
        }

        if (ok)
        {
            Shell.SetLibraryDone($"Exported {pack.Name}");
        }
    }

    /// <summary>Spec 2.6 3.1: the pack folder copied to "{name} copy", off the UI thread. The copy's paths are
    /// captured as absent before it is made, so Undo deletes exactly the files the copy laid down and prunes the
    /// folders they needed. The name is picked once and handed to the copy, and the paths come off the source tree
    /// the copy itself reads, so what Undo holds and what is written can never differ by a name or by a file.</summary>
    [RelayCommand]
    private async Task DuplicateAsync(PackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var pack = row.Pack;
        var libraryPath = Shell.Services.LibraryPath;
        var copyName = PackCopier.FreeCopyName(libraryPath, pack.Name);
        var undoPaths = PackCopier.DuplicatePaths(libraryPath, pack.Name, copyName);
        string? error = null;
        await Shell.RunLibraryWriteAsync(
            $"Duplicating {pack.Name}",
            undoPaths,
            (_, ct) => Task.Run(
                () =>
                {
                    try
                    {
                        PackCopier.DuplicatePack(libraryPath, pack.Name, copyName);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        error = ex.Message;
                    }
                },
                ct),
            $"{pack.Name} duplicated as {copyName}");
        if (error is not null)
        {
            // The copy took its own half-written folder with it, so there is nothing to undo and nothing to say
            // but what went wrong: the line the write boundary left is written over, as an import's is.
            Shell.SetLibraryDone($"Could not duplicate {pack.Name}");
            Shell.Dialogs.Error("Could not duplicate pack", $"Could not duplicate {pack.Name}: {error}");
        }
    }

    /// <summary>Spec 2.6 3.2: the same dialog the pack detail header opens, aimed at this row's pack.</summary>
    [RelayCommand]
    private Task ImportIntoAsync(PackRowViewModel? row) =>
        row is null ? Task.CompletedTask : Shell.ImportFromPackAsync(row.Pack);

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

    private void OpenInExplorer(string path)
    {
        if (ExplorerLauncher.Open(path) is { } error)
        {
            Shell.Dialogs.Error("Could not open the folder", error);
        }
    }
}
