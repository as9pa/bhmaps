using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using BhMaps.Core.Scanning;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5: one pack, whole. One grid of every map the pack touches, put together with the game's art,
/// plus a tile for each background it ships that no map claims, and the files in it that change nothing in game.
/// A tile opens the drawer, which lists the halves of what the pack holds for that map (addendum F). Every row is
/// shown and nothing is truncated (spec section 2, Packs). The pack comes from
/// <see cref="MainViewModel.NavigateToPack" />, so the title is a pack name rather than a fixed word.</summary>
public partial class PackDetailViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinZoom;

    public const int MaxZoom = AppSettings.MaxZoom;

    public const string NoPackText = "No pack is open.";

    public const string NoMapsText = "This pack has no map folders.";

    /// <summary>Spec 2.6 4.3: the line the page shows for two seconds when Ctrl+V has nothing to paste.</summary>
    public const string NothingCopiedText = "Nothing copied yet";

    /// <summary>The line Ctrl+X shows on the Default pack, whose menu offers no Move line either (spec 7.5: the
    /// Default pack is the game's own files).</summary>
    public const string NoCutFromDefaultText = "Nothing moves out of the Default pack";

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>What a background is: the game ships every one of its background slots as a JPEG.</summary>
    private const string BackgroundExtension = ".jpg";

    private ScanSnapshot? _snapshot;

    /// <summary>The key of the tile the drawer is open on, so a rescan can reopen it on the new snapshot.</summary>
    private string? _openKey;

    /// <summary>Cancels this pack's loads; replaced, never disposed, exactly as PlatformsViewModel does, because
    /// the loads still hold the token.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>"Folder\file.png" for every fully transparent PNG in the pack, as the last look at it found them.
    /// Remove deletes exactly these.</summary>
    private IReadOnlyList<string> _transparentFiles = Array.Empty<string>();

    /// <summary>False while <see cref="Refresh" /> re-resolves the pack, so a rescan rebuilds the page once
    /// instead of twice.</summary>
    private bool _rebuildOnPackChange = true;

    /// <summary>Takes <see cref="ClipboardHint" /> away again two seconds after it was put up.</summary>
    private readonly DispatcherTimer _hintTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    public PackDetailViewModel(MainViewModel shell)
        : base(shell)
    {
        Items = [];
        TransparentText = "";
        ClipboardHint = "";
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            ClipboardHint = "";
        };

        // A stored zoom from another version, or a hand-edited one, is clamped rather than trusted.
        Zoom = Math.Clamp(shell.Services.Settings.PackZoom, MinZoom, MaxZoom);
    }

    /// <summary>The pack the page is showing. Null until the shell navigates to one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(HasPack))]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial Pack? Pack { get; set; }

    public override string Title => Pack?.Name ?? "Pack";

    public bool HasPack => Pack is not null;

    /// <summary>Columns in the grid, MinZoom to MaxZoom, persisted as packZoom.</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    /// <summary>Every map the pack touches, composed with the pack's own files over the background the pack ships
    /// for it, or the game's when it ships none (spec 5, addendum F).</summary>
    public ObservableCollection<PackTileViewModel> Items { get; }

    /// <summary>The tile the keyboard is on. Enter opens the drawer for it.</summary>
    [ObservableProperty]
    public partial PackTileViewModel? SelectedTile { get; set; }

    /// <summary>The open drawer, or null when none is open.</summary>
    [ObservableProperty]
    public partial PackDrawerViewModel? Drawer { get; set; }

    /// <summary>"N files change nothing in game" (spec 7.5), or "" while the pack has none. Written only after
    /// the scan of the pack's PNGs finishes, so nothing flashes on a pack that has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransparent))]
    public partial string TransparentText { get; set; }

    public bool HasTransparent => TransparentText.Length > 0;

    /// <summary>"Wharf cut", or "" while the page has nothing to say. Shown for two seconds beside the zoom
    /// slider, so Ctrl+C and Ctrl+X say out loud what they took (spec 2.6 4.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClipboardHint))]
    public partial string ClipboardHint { get; set; }

    public bool HasClipboardHint => ClipboardHint.Length > 0;

    /// <summary>The tile the pointer is over wins; the keyboard-focused tile is the fallback (spec 2.6 4.3). The
    /// view knows both, so it hands the page the one it wants before the command runs.</summary>
    public PackTileViewModel? KeyTarget { get; set; }

    /// <summary>The shell's own navigation command. PageViewModel.Shell is protected, so the markup cannot reach
    /// Shell.NavigatePacksCommand directly; this one-line property is the smallest way to bind it.</summary>
    public IRelayCommand BackToPacksCommand => Shell.NavigatePacksCommand;

    public string EmptyText => NoPackText;

    /// <summary>What the grid says when the pack has nothing to show.</summary>
    public string EmptyNote => NoMapsText;

    /// <summary>Re-resolves the pack by name against the new snapshot and rebuilds every section from it. A pack
    /// that is no longer in the library was removed underneath the page, so the page goes back to the list.</summary>
    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;
        var shown = Pack;
        var current = shown is null ? null : FindPack(snapshot, shown.Name);
        SetPack(current);
        Rebuild();
        if (shown is not null && current is null)
        {
            // Spec 6.5: Remove deletes the pack from the library. Its page has nothing left to show.
            Shell.NavigatePacksCommand.Execute(null);
        }
    }

    /// <summary>Spec 5: a click, or Enter on the focused tile, opens the drawer. A background tile whose slot no
    /// map names opens a drawer with the file and Open folder and no Apply (decision C-D4).</summary>
    public void Open(PackTileViewModel? tile)
    {
        if (tile is null || _snapshot is not { } snapshot || Pack is not { } pack)
        {
            return;
        }

        Drawer?.Cancel();
        SelectedTile = tile;
        _openKey = tile.Key;
        var preview = tile.Map is { } map
            ? Items.FirstOrDefault(t => t.Key.Equals(map.FolderName, StringComparison.OrdinalIgnoreCase)) ?? tile
            : tile;
        var status = tile.Map is { } m && snapshot.MapStatuses.TryGetValue(m.FolderName, out var found) ? found : null;
        var drawer = new PackDrawerViewModel(Shell, this, pack, tile.Map, status, preview);
        Drawer = drawer;
        _ = drawer.LoadAsync();
    }

    public void OpenSelected() => Open(SelectedTile);

    /// <summary>Spec 2.6 section 3: every tile has a menu. A map tile leads with the map, a file tile with the
    /// file name, and both end with the line that takes the tile out of the pack. Built when the menu opens, so
    /// the ticked count is the one the user can see.</summary>
    public void BuildTileMenu(PackTileViewModel tile)
    {
        if (Pack is not { } pack)
        {
            tile.MenuItems = [];
            return;
        }

        var isDefault = pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase);
        var items = new List<TileMenuCommand>();
        if (tile.Map is { } owner)
        {
            items.Add(TileMenuCommand.Header(owner.DisplayName));
            items.Add(new TileMenuCommand(
                $"Apply to {owner.DisplayName}",
                new AsyncRelayCommand(() => Shell.ApplySetAsync(pack, [owner], clearTicks: false)),
                IsEnabled: Shell.CanWrite));
        }
        else
        {
            items.Add(TileMenuCommand.Header(tile.Caption));
        }

        if (tile.PicturePath is { } path)
        {
            AddPictureLines(items, tile, pack, path);
        }

        items.Add(new TileMenuCommand(
            "Copy to pack...", new AsyncRelayCommand(() => CopyTileAsync(tile, cut: false)), Gesture: "Ctrl+C"));
        if (!isDefault)
        {
            items.Add(new TileMenuCommand(
                "Move to pack...", new AsyncRelayCommand(() => CopyTileAsync(tile, cut: true)), Gesture: "Ctrl+X"));
        }

        if (tile.PicturePath is not null || tile.FolderPath is not null)
        {
            items.Add(new TileMenuCommand(
                "Show in folder", new RelayCommand(() => ShowInFolder(tile.PicturePath ?? tile.FolderPath!))));
        }

        if (tile.Map is { } removable && !isDefault)
        {
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand(
                $"Remove from {pack.Name}", new AsyncRelayCommand(() => RemoveMapFromPackAsync(removable))));
        }
        else if (tile.Map is null && tile.PicturePath is { } file)
        {
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand(
                $"Remove from {pack.Name}",
                new AsyncRelayCommand(() => RemoveFromPackAsync(file, Path.GetFileName(file)))));
        }

        tile.MenuItems = items;
    }

    /// <summary>The 2.5 picture lines, in their order: the ticked apply, the chooser, every map, then Edit. The
    /// per-map apply of 2.5 is gone: on a map tile Apply to {map} above it already names that map.</summary>
    private void AddPictureLines(List<TileMenuCommand> items, PackTileViewModel tile, Pack pack, string path)
    {
        var name = Path.GetFileName(path);
        var ticked = Shell.SelectedMapCount;
        var slot = tile.Map?.BackgroundSlots.FirstOrDefault();
        if (ticked > 0)
        {
            var text = ticked == 1 ? "Apply to the 1 selected map" : $"Apply to the {ticked} selected maps";
            items.Add(new TileMenuCommand(
                text,
                new AsyncRelayCommand(
                    () => Shell.ApplyPictureAsync(path, Shell.SelectedMaps, true, name, pack.Name))));
        }

        items.Add(new TileMenuCommand(
            "Apply to a map...", new AsyncRelayCommand(() => ApplyToChosenMapAsync(path, name, pack.Name))));
        items.Add(new TileMenuCommand(
            "Apply to all maps", new AsyncRelayCommand(() => ApplyToAllMapsAsync(path, name, pack.Name))));
        items.Add(TileMenuCommand.Separator());
        items.Add(new TileMenuCommand(
            "Edit",
            new AsyncRelayCommand(
                () => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(path, pack.Name, slot)))));
    }

    /// <summary>Spec 2.6 4.3: pick the target, then copy or move the tile into it. A copy that clashes offers
    /// Replace once; a move is always inside an undo session, a copy only when it replaces something.</summary>
    private async Task CopyTileAsync(PackTileViewModel tile, bool cut)
    {
        if (Pack is not { } pack)
        {
            return;
        }

        var name = tile.Map?.DisplayName ?? tile.Caption;
        var title = cut ? $"Move {name} to" : $"Copy {name} to";
        if (await Shell.ChoosePackAsync(title, pack) is { } target)
        {
            await PasteIntoAsync(pack, tile, cut, target);
        }
    }

    /// <summary>The write itself, shared by the menu lines and by Ctrl+V (spec 2.6 4.3). The first attempt never
    /// replaces; a Skipped result asks once and repeats with replace on, and that second attempt is captured for
    /// undo because it overwrites what the target held.</summary>
    public async Task PasteIntoAsync(Pack source, PackTileViewModel tile, bool cut, Pack target)
    {
        if (_snapshot is not { } snapshot
            || source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var catalog = snapshot.Catalog;
        var name = tile.Map?.DisplayName ?? tile.Caption;
        var relative = tile.PicturePath is { } picture && tile.Map is null
            ? Path.Combine(PackCopier.BackgroundsFolder, Path.GetFileName(picture))
            : null;
        var files = tile.Map is { } map ? PackCopier.MapFiles(source, map, catalog) : [relative!];
        IReadOnlyList<string> undoPaths =
            [.. PackCopier.Touched(source, files), .. PackCopier.Touched(target, files)];

        var result = await RunPasteAsync(source, target, tile, catalog, cut, replace: false, name, undoPaths);
        if (result is { Skipped: true }
            && Shell.Dialogs.Confirm("Replace", $"{target.Name} already has {name}. Replace it?"))
        {
            result = await RunPasteAsync(source, target, tile, catalog, cut, replace: true, name, undoPaths);
        }

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be copied", result.Failures);
        }
    }

    /// <summary>One attempt. A plain copy that replaces nothing writes outside an undo session and offers the
    /// target instead of Undo (spec 4.2); everything else goes through the library write boundary.</summary>
    private async Task<PackCopyResult?> RunPasteAsync(
        Pack source,
        Pack target,
        PackTileViewModel tile,
        MapCatalog catalog,
        bool cut,
        bool replace,
        string name,
        IReadOnlyList<string> undoPaths)
    {
        PackCopyResult? result = null;
        var verb = cut ? "Moving" : "Copying";
        var done = cut ? $"{name} moved to {target.Name}" : $"{name} copied to {target.Name}";
        var relative = tile.Map is null && tile.PicturePath is { } picture
            ? Path.Combine(PackCopier.BackgroundsFolder, Path.GetFileName(picture))
            : null;

        Task Work(IProgress<string> progress, CancellationToken ct) => Task.Run(
            () =>
            {
                progress.Report(name);
                result = tile.Map is { } map
                    ? cut
                        ? PackCopier.MoveMap(source, target, map, catalog, replace)
                        : PackCopier.CopyMap(source, target, map, catalog, replace)
                    : cut
                        ? PackCopier.MoveFile(source, target, relative!, replace)
                        : PackCopier.CopyFile(source, target, relative!, replace);
            },
            ct);

        if (cut || replace)
        {
            await Shell.RunLibraryWriteAsync($"{verb} {name}", undoPaths, Work, done);
            return result;
        }

        var ok = await Shell.RunBusyAsync($"{verb} {name}", Work);
        if (ok && result is { Skipped: false })
        {
            var open = target;
            Shell.SetLibraryDone(done, $"Open {target.Name}", new RelayCommand(() => Shell.NavigateToPack(open)));
        }

        await Shell.RescanAsync();
        return result;
    }

    /// <summary>Ctrl+C and Ctrl+X: the tile is remembered on the shell and the page says so. The Default pack is
    /// the game's own, so nothing is ever cut out of it, exactly as its menu offers no Move line.</summary>
    public void CopyTileToClipboard(PackTileViewModel? tile, bool cut)
    {
        if (tile is null || Pack is not { } pack)
        {
            return;
        }

        if (cut && pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase))
        {
            ShowHint(NoCutFromDefaultText);
            return;
        }

        Shell.PackClipboard = new PackClipboardItem(pack, tile, cut);
        var name = tile.Map?.DisplayName ?? tile.Caption;
        ShowHint(cut ? $"{name} cut" : $"{name} copied");
    }

    /// <summary>Ctrl+V: what the clipboard holds, into this page's pack. Nothing happens when the clipboard is
    /// empty but the line, and nothing at all when the source is this pack.</summary>
    public async Task PasteAsync()
    {
        if (Pack is not { } pack)
        {
            return;
        }

        if (Shell.PackClipboard is not { } held)
        {
            ShowHint(NothingCopiedText);
            return;
        }

        if (held.Source.Name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await PasteIntoAsync(held.Source, held.Tile, held.Cut, pack);
        if (held.Cut)
        {
            Shell.PackClipboard = null;
        }
    }

    /// <summary>The line, and the timer that takes it away again. One timer, restarted, so two keys in a row do
    /// not leave the first line's tick to clear the second's words.</summary>
    private void ShowHint(string text)
    {
        ClipboardHint = text;
        _hintTimer.Stop();
        _hintTimer.Start();
    }

    [RelayCommand]
    private void CopyTile() => CopyTileToClipboard(KeyTarget ?? SelectedTile, cut: false);

    [RelayCommand]
    private void CutTile() => CopyTileToClipboard(KeyTarget ?? SelectedTile, cut: true);

    [RelayCommand]
    private Task Paste() => PasteAsync();

    /// <summary>Spec 2.6 section 3 item 7: the map's files in this pack, deleted together, inside a library undo
    /// session so the confirm is the only thing standing between the user and getting them back.</summary>
    private async Task RemoveMapFromPackAsync(MapEntry map)
    {
        if (Pack is not { } pack || _snapshot is not { } snapshot)
        {
            return;
        }

        var count = PackCopier.MapFiles(pack, map, snapshot.Catalog).Count;
        if (!Shell.Dialogs.Confirm(
                $"Remove from {pack.Name}?",
                $"{map.DisplayName} and its {PackRowViewModel.Plural(count, "file")} are removed from {pack.Name}. The game keeps whatever is applied until you apply something else."))
        {
            return;
        }

        var catalog = snapshot.Catalog;
        PackCopyResult? result = null;
        await Shell.RunLibraryWriteAsync(
            $"Removing {map.DisplayName}",
            PackCopier.Touched(pack, PackCopier.MapFiles(pack, map, catalog)),
            (_, ct) => Task.Run(() => { result = PackCopier.RemoveMap(pack, map, catalog); }, ct),
            $"{map.DisplayName} removed from {pack.Name}");
        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be removed", result.Failures);
        }
    }

    /// <summary>Spec 4.1 line 3: the chooser, then the shell's apply on the one map it returned. No confirm,
    /// because one map is one click (spec 4.2).</summary>
    private async Task ApplyToChosenMapAsync(string path, string name, string packName)
    {
        if (await Shell.ChooseMapAsync(path, name) is { } map)
        {
            await Shell.ApplyPictureAsync(path, [map], clearTicks: false, name, packName);
        }
    }

    /// <summary>Spec 4.1 line 4: the shell's own apply over every map, which brings the
    /// "Apply {name} to these {N} maps?" confirm, the undo snapshot and the done line with it.</summary>
    private Task ApplyToAllMapsAsync(string path, string name, string packName) =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(path, snapshot.Catalog.Maps, clearTicks: false, name, packName)
            : Task.CompletedTask;

    private void ShowInFolder(string path)
    {
        if (ExplorerLauncher.Reveal(path) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }

    /// <summary>Spec 4.1 line 8: the file leaves the pack the way Remove from library takes it out of My
    /// Backgrounds. A library write, so RunBusyAsync and SetLibraryDone, never RunGameWriteAsync, and the
    /// packs-root guard is why this is not one File.Delete.</summary>
    private async Task RemoveFromPackAsync(string path, string name)
    {
        if (Pack is not { } pack
            || !Shell.Dialogs.Confirm(
                $"Remove from {pack.Name}?",
                $"{name} is removed from {pack.Name}. The game keeps whatever is applied until you apply something else."))
        {
            return;
        }

        var packsRoot = PackScanner.PacksRoot(Shell.Services.LibraryPath);
        var failures = new List<FileFailure>();
        var ok = await Shell.RunBusyAsync(
            $"Removing {name}",
            (_, ct) => Task.Run(
                () =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Path.GetFullPath(path).StartsWith(
                            Path.GetFullPath(packsRoot) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(new FileFailure(path, "Not a file in the library."));
                        return;
                    }

                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new FileFailure(path, ex.Message));
                    }
                },
                ct));

        Shell.Dialogs.ShowFailures("Some files could not be removed", failures);
        if (ok && failures.Count == 0)
        {
            Shell.SetLibraryDone($"Removed {name} from {pack.Name}");
        }

        await Shell.RescanAsync();
    }

    /// <summary>A command as well as a method: C7 binds Escape to CloseDrawerCommand, and a KeyBinding whose
    /// Command resolves to null fails silently.</summary>
    [RelayCommand]
    public void CloseDrawer()
    {
        Drawer?.Cancel();
        Drawer = null;
        _openKey = null;
    }

    /// <summary>Spec 6.5, reusing the Packs page's own apply so there is one implementation of it and one
    /// game-write boundary behind it.</summary>
    [RelayCommand(CanExecute = nameof(HasPack))]
    private Task ApplyAllAsync() =>
        Pack is { } pack ? Shell.Packs.ApplyAllAsync(pack) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasPack))]
    private void OpenFolder()
    {
        if (Pack is { } pack)
        {
            OpenInExplorer(pack.FullPath);
        }
    }

    /// <summary>Spec 6.5: deletes the flagged PNGs from the pack after a confirm that names how many. A
    /// library-only write, so it takes no undo snapshot; the rescan afterwards is what rebuilds the page.</summary>
    [RelayCommand]
    private async Task RemoveTransparentAsync()
    {
        if (Pack is not { } pack || _transparentFiles.Count == 0)
        {
            return;
        }

        var files = _transparentFiles;
        if (!Shell.Dialogs.Confirm("Remove files", RemoveQuestion(files.Count, pack.Name)))
        {
            return;
        }

        var root = pack.FullPath;
        var failures = new List<FileFailure>();
        var ok = await Shell.RunBusyAsync(
            "Removing transparent files",
            (progress, ct) => Task.Run(() => Delete(root, files, failures, progress, ct), ct));
        if (failures.Count > 0)
        {
            Shell.Dialogs.ShowFailures("Some files could not be removed", failures);
        }

        if (ok)
        {
            Shell.SetLibraryDone($"Removed {PackRowViewModel.Plural(files.Count - failures.Count, "file")}");
        }

        await Shell.RescanAsync();
    }

    /// <summary>Deletes exactly the paths it is given, relative to the pack's own folder. A file that will not go
    /// is reported and the rest still go.</summary>
    private static void Delete(
        string root,
        IReadOnlyList<string> relativePaths,
        List<FileFailure> failures,
        IProgress<string> progress,
        CancellationToken ct)
    {
        foreach (var relativePath in relativePaths)
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(relativePath);
            var path = Path.Combine(root, relativePath);
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(path, ex.Message));
            }
        }
    }

    private static Pack? FindPack(ScanSnapshot snapshot, string name) =>
        snapshot.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sets the pack without rebuilding, for the one caller that rebuilds itself.</summary>
    private void SetPack(Pack? pack)
    {
        _rebuildOnPackChange = false;
        Pack = pack;
        _rebuildOnPackChange = true;
    }

    partial void OnPackChanged(Pack? value)
    {
        if (_rebuildOnPackChange)
        {
            Rebuild();
        }
    }

    partial void OnZoomChanged(int value)
    {
        if (Shell.Services.Settings.PackZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { PackZoom = value });
        }
    }

    /// <summary>Throws away the previous pack's loads and starts this one's. The grid is built from the current
    /// snapshot, so a rescan and a change of pack take the same path.</summary>
    private void Rebuild()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var reopen = _openKey;
        CloseDrawer();
        SelectedTile = null;

        // The pointer is over nothing the new grid holds: a tile left over from the previous pack would pair the
        // wrong pack with it on the next Ctrl+C, and MouseLeave never comes for a container that is gone.
        KeyTarget = null;
        Items.Clear();
        _transparentFiles = Array.Empty<string>();
        TransparentText = "";
        if (_snapshot is not { } snapshot || Pack is not { } pack)
        {
            return;
        }

        foreach (var map in MapsIn(snapshot.Catalog, pack))
        {
            Items.Add(new PackTileViewModel(
                map.FolderName, map.DisplayName, map, null, MapCompositor.CardWidth, MapCompositor.CardHeight)
            { FolderPath = pack.FindFolder(map.FolderName)?.FullPath });
        }

        // .jpg only: the game's backgrounds are all JPEGs, so a PNG in the folder fills no slot and belongs in
        // the transparent-files line instead. A background whose slot a map in the grid already covers is drawn
        // by that map's own tile, so only the orphans get a tile of their own, captioned by file name.
        foreach (var file in pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (!Path.GetExtension(file.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owner = PackCopier.MapForSlot(snapshot.Catalog, file.Name);
            if (owner is null)
            {
                Items.Add(new PackTileViewModel(
                    file.Name, file.Name, null, file, MapCompositor.CardWidth, MapCompositor.CardHeight)
                { PicturePath = file.FullPath });
            }
            else
            {
                var tile = Items.FirstOrDefault(
                    t => t.Key.Equals(owner.FolderName, StringComparison.OrdinalIgnoreCase));
                if (tile is null)
                {
                    tile = new PackTileViewModel(
                        owner.FolderName, owner.DisplayName, owner, null,
                        MapCompositor.CardWidth, MapCompositor.CardHeight)
                    { FolderPath = pack.FindFolder(owner.FolderName)?.FullPath };
                    Items.Add(tile);
                }

                tile.PicturePath = file.FullPath;
            }
        }

        OnPropertyChanged(nameof(Items));
        Load([.. Items], pack, _cts.Token);
        LoadTransparent(pack, _cts.Token);

        // A rescan follows every write, so the drawer that started the write comes back on the new snapshot.
        if (reopen is not null)
        {
            Open(Items.FirstOrDefault(t => t.Key.Equals(reopen, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>The maps the pack touches: the catalog maps it has at least one file for. A pack folder that is
    /// not a map, such as a theme folder other maps borrow from (spec 4), is not one of them.</summary>
    private static IEnumerable<MapEntry> MapsIn(MapCatalog catalog, Pack pack) =>
        catalog.Maps.Where(map => pack.FindFolder(map.FolderName) is { Files.Count: > 0 });

    /// <summary>Fills the tiles in view order, one at a time, so the single render thread works down the page.
    /// Fire and forget, like PlatformsViewModel.LoadSelected.</summary>
    private async void Load(
        IReadOnlyList<PackTileViewModel> tiles,
        Pack pack,
        CancellationToken ct)
    {
        try
        {
            foreach (var tile in tiles)
            {
                ct.ThrowIfCancellationRequested();
                if (tile.Map is not null)
                {
                    tile.Preview = await ComposeAsync(tile, pack, ct);
                }
                else if (tile.File is { } file)
                {
                    tile.Preview = await Shell.Services.Thumbnails.GetAsync(file.FullPath, file.MtimeTicks, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by another pack or by a rescan.
        }
    }

    /// <summary>Spec 7.5 and A6: the pack's fully transparent PNGs. Decoding every PNG in a pack is far too much
    /// work for the UI thread, so it runs on the pool and the line appears when it is done.</summary>
    private async void LoadTransparent(Pack pack, CancellationToken ct)
    {
        try
        {
            var files = await Task.Run(() => PlatformSetApplier.TransparentFiles(pack, ct), ct);
            ct.ThrowIfCancellationRequested();
            _transparentFiles = files;
            TransparentText = TransparentLine(files.Count);
        }
        catch (OperationCanceledException)
        {
            // Superseded by another pack or by a rescan.
        }
    }

    /// <summary>Spec 7.5's line, verbatim. The verb inflects with the noun, so this is not PackRowViewModel's
    /// plural with a suffix.</summary>
    private static string TransparentLine(int count) => count switch
    {
        0 => "",
        1 => "1 file changes nothing in game",
        _ => $"{count} files change nothing in game",
    };

    /// <summary>The confirm, which names the count. Written out twice rather than assembled, because the pronoun
    /// and both verbs inflect with it and "1 file ... They are" is what the running app showed before this.</summary>
    private static string RemoveQuestion(int count, string packName) => count == 1
        ? $"Remove 1 file from pack '{packName}'? It is fully transparent and changes nothing in game. This cannot be undone."
        : $"Remove {count} files from pack '{packName}'? They are fully transparent and change nothing in game. This cannot be undone.";

    /// <summary>The map drawn from the pack's own files through the preview cache, so the render is queued on the
    /// one STA thread and kept on disk. AssetSources takes the pack as its second source, which means the pack's
    /// background when it ships one and the game's otherwise: what the pack put together looks like in game. A
    /// preview that cannot be composed falls back to a plain file tile; it is a picture, never an error dialog.</summary>
    private async Task<ImageSource?> ComposeAsync(PackTileViewModel tile, Pack pack, CancellationToken ct)
    {
        // Spec 3.6: with no level data there are no camera bounds and no platform tree, so there is nothing to
        // compose, and the pack's own art is the only picture of the map there is.
        if (_snapshot?.Catalog.HasLevelData == true)
        {
            try
            {
                var sources = new AssetSources(Shell.Services.GamePath, pack.FullPath);
                var path = await Task.Run(
                    () => Shell.Services.Previews.GetOrRenderAsync(
                        tile.Map!.BaseLevel, tile.ComposeWidth, tile.ComposeHeight, sources, ct),
                    ct);
                if (await Task.Run(() => LoadPreview(path), ct) is { } preview)
                {
                    return preview;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
            {
                // A file that vanished between the scan and the render costs this tile its composite, nothing more.
            }
        }

        var folder = pack.FindFolder(tile.Map!.FolderName);
        var file = folder is null ? null : ThumbnailProvider.PickRepresentative(folder);
        return file is null ? null : await Shell.Services.Thumbnails.GetAsync(file.FullPath, file.MtimeTicks, ct);
    }

    /// <summary>Decodes a cached preview at its own size and freezes it, so it can cross to the UI thread.</summary>
    private static ImageSource? LoadPreview(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    private void OpenInExplorer(string path)
    {
        if (ExplorerLauncher.Open(path) is { } error)
        {
            Shell.Dialogs.Error("Could not open the folder", error);
        }
    }
}

/// <summary>One tile of pack detail's single grid: a map the pack touches, drawn put together or with the
/// background dropped, or one of the pack's own background pictures (decision C-D3).</summary>
public partial class PackTileViewModel : ObservableObject
{
    public PackTileViewModel(
        string key, string caption, MapEntry? map, GameFile? file, int composeWidth, int composeHeight)
    {
        Key = key;
        Caption = caption;
        Map = map;
        File = file;
        ComposeWidth = composeWidth;
        ComposeHeight = composeHeight;
    }

    /// <summary>The map's folder name, or the background's file name. What a reopen after a rescan matches on.</summary>
    public string Key { get; }

    public string Caption { get; }

    public MapEntry? Map { get; }

    public GameFile? File { get; }

    /// <summary>No map owns this picture, so the caption is a file name and the markup draws it in Geist Mono.</summary>
    public bool IsFileTile => Map is null;

    public int ComposeWidth { get; }

    public int ComposeHeight { get; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>The lines of this tile's menu (spec 4.1), filled by PackDetailViewModel.BuildTileMenu when the
    /// menu opens. Built then and not before, because the ticked line names a count that changes on another page
    /// and there is one of these per tile.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; } = [];

    /// <summary>The picture the tile is about: the pack's own file for an orphan, or the pack's copy of the map's
    /// background slot. Null on a tile whose map the pack has no background for.</summary>
    public string? PicturePath { get; set; }

    /// <summary>The map's folder inside the pack, so Show in folder on a map tile has something to open. Null on
    /// a file tile, whose picture is what Show in folder reveals.</summary>
    public string? FolderPath { get; set; }
}
