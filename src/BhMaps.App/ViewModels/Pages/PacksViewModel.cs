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

            // 2.8: the eye's state is read before the menu is built, because the menu's line names it.
            row.IsHidden = Shell.Services.Settings.IsHidden(pack.Name);
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
    /// its parameter, because a TileMenuCommand carries no parameter of its own. Delete pack is last: it is the
    /// destructive one, and the pointer should not have to pass over it to reach another line. The Default pack
    /// has no Delete line and no Hide from lists line: it is the game's own art, every Reset to default restores
    /// from it, and the lists fall back to it.</summary>
    private IReadOnlyList<TileMenuCommand> BuildMenu(PackRowViewModel row)
    {
        var items = new List<TileMenuCommand>
        {
            TileMenuCommand.Header(row.Name),
            new("Duplicate", new RelayCommand(() => DuplicateCommand.Execute(row))),
            new("Import from another pack...", new RelayCommand(() => ImportIntoCommand.Execute(row))),
            new("Export", new RelayCommand(() => ExportCommand.Execute(row))),
            new("Open folder", new RelayCommand(() => OpenFolderCommand.Execute(row))),
        };
        if (!row.IsDefault)
        {
            items.Add(new TileMenuCommand(
                row.IsHidden ? "Show in lists" : "Hide from lists",
                new RelayCommand(() => ToggleHiddenCommand.Execute(row))));
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand(
                "Delete pack",
                new RelayCommand(() => RemoveCommand.Execute(row)),
                IsDestructive: true));
        }

        return items;
    }

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
                MainViewModel.ConfirmBody(
                    $"Apply {pack.Name} to {MainViewModel.Count(maps.Count, "map")}?",
                    MainViewModel.WritesArt,
                    maps)))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;

        // Spec 10.4: the maps the pack lands in are the maps whose map-select thumbnails the write redraws.
        IReadOnlyList<MapEntry> artMaps = Shell.Snapshot is { } snapshot
            ? PackApplier.MapsTouched(pack, snapshot.Catalog.Maps)
            : [];
        // The strip names maps, not files (3.0), so the applier gets the catalog's name for every folder it writes.
        // Two catalog maps can share a folder, so the first name for a folder is the one it reports under. The keys
        // cover every map the confirm counted, since a map with files in the pack is one of artMaps too, and that is
        // what makes the strip's total the same number the confirm and the done line say.
        var mapNames = artMaps
            .GroupBy(map => map.FolderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(folder => folder.Key, folder => folder.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        ApplyResult? result = null;
        await Shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            pack.RelativePaths,
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyPack(pack, gamePath, progress, ct, mapNames); }, ct),
            $"{pack.Name} applied to {MainViewModel.Count(maps.Count, "map")}.",
            packName: pack.Name,
            artMaps: artMaps,
            sources: AppliedSources.FromPack(pack, pack.RelativePaths));
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

    /// <summary>The header's menu (3.0): the two actions that are not what the page is for. Capture writes into
    /// the game folder and carries the CanWrite its button carried; Open library is library-only and stops at the
    /// shell being busy, which is the gate the header's row of buttons put on it (spec 7.8).</summary>
    public override IReadOnlyList<TileMenuCommand>? PageMenu =>
    [
        new TileMenuCommand("Capture the Default pack", CaptureDefaultsCommand, IsEnabled: Shell.CanWrite),
        new TileMenuCommand("Open library", OpenLibraryCommand, IsEnabled: Shell.IsNotBusy),
    ];

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
            $"Delete {pack.Name}?",
            $"Its {PackRowViewModel.Plural(pack.FileCount, "file")} will be deleted. This cannot be undone."))
        {
            return;
        }

        var libraryPath = Shell.Services.LibraryPath;
        string? error = null;
        var ok = await Shell.RunBusyAsync(
            $"Deleting {pack.Name}",
            (_, _) => Task.Run(() => { error = PackDeleter.Delete(libraryPath, pack.Name); }));
        if (error is not null)
        {
            Shell.Dialogs.Error("Could not remove pack", error);
        }
        else if (ok)
        {
            Shell.Status.Done($"Deleted {pack.Name}.", undoable: false);
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
            Shell.Status.Done($"Exported {pack.Name}.", undoable: false);
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
            Shell.Status.Error($"Could not duplicate {pack.Name}.", retry: null);
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

    /// <summary>2.8: the eye on the row. Hiding a pack keeps it out of the Backgrounds and Platforms lists and
    /// nothing else: the files stay where they are, nothing is written into the game, and the Default pack is
    /// never hidden, because the lists fall back to it. The list itself is rebuilt from the name it holds, so a
    /// name hidden twice is stored once.</summary>
    [RelayCommand]
    private void ToggleHidden(PackRowViewModel? row)
    {
        if (row is null || row.IsDefault)
        {
            return;
        }

        var hide = !row.IsHidden;
        var names = Shell.Services.Settings.HiddenPacks
            .Where(name => !name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (hide)
        {
            names.Add(row.Name);
        }

        Shell.Services.UpdateSettings(Shell.Services.Settings with { HiddenPackNames = names });
        row.IsHidden = hide;
        row.RefreshCountsText();
        row.SetMenu(BuildMenu(row));

        // 2.8: the other pages read the setting when they build their rows, and nothing else tells them it moved.
        // This page is left alone, because the row above is already right and a rebuild would lose the scroll.
        Shell.RefreshPages(this);
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
