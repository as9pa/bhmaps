using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.5: one pack, whole. The maps it touches put together with the game's art, the backgrounds it
/// ships, the same maps with the background dropped, and the files in it that change nothing in game. Every row
/// is shown and nothing is truncated (spec section 2, Packs). The pack comes from
/// <see cref="MainViewModel.NavigateToPack" />, so the title is a pack name rather than a fixed word.</summary>
public partial class PackDetailViewModel : PageViewModel
{
    /// <summary>The platform-only composites carry no background, so they are rendered at the size the tile is
    /// shown at. Pixel counts, so int, unlike the tile size below.</summary>
    public const int PlatformWidth = 320;

    public const int PlatformHeight = 180;

    /// <summary>One tile size for all three grids, so the page reads as one grid rather than three, and three of
    /// them fit across the content area of the 1280 window (spec 7.1) with the 12 px gap between them. Exactly
    /// 16 by 9, so a card preview and a background both fill it. Device independent units, so double: the markup
    /// sets Width and Height from these.</summary>
    public const double TileWidth = 304;

    public const double TileHeight = 171;

    public const string NoPackText = "No pack is open.";

    public const string NoMapsText = "This pack has no map folders.";

    public const string NoBackgroundsText = "This pack has no backgrounds.";

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>What a background is: the game ships every one of its background slots as a JPEG.</summary>
    private const string BackgroundExtension = ".jpg";

    private ScanSnapshot? _snapshot;

    /// <summary>Cancels this pack's loads; replaced, never disposed, exactly as PlatformsViewModel does, because
    /// the loads still hold the token.</summary>
    private CancellationTokenSource? _cts;

    /// <summary>"Folder\file.png" for every fully transparent PNG in the pack, as the last look at it found them.
    /// Remove deletes exactly these.</summary>
    private IReadOnlyList<string> _transparentFiles = Array.Empty<string>();

    /// <summary>False while <see cref="Refresh" /> re-resolves the pack, so a rescan rebuilds the page once
    /// instead of twice.</summary>
    private bool _rebuildOnPackChange = true;

    public PackDetailViewModel(MainViewModel shell)
        : base(shell)
    {
        PutTogether = [];
        Backgrounds = [];
        Platforms = [];
        TransparentText = "";
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

    /// <summary>Every map the pack touches, composed with the pack's own files over the background the pack
    /// ships for it, or the game's when it ships none.</summary>
    public ObservableCollection<PackMapTileViewModel> PutTogether { get; }

    /// <summary>The images in the pack's own Backgrounds folder.</summary>
    public ObservableCollection<PackBackgroundTileViewModel> Backgrounds { get; }

    /// <summary>The same maps as <see cref="PutTogether" />, with the background dropped, so the pack's
    /// platform art is all that is left.</summary>
    public ObservableCollection<PackMapTileViewModel> Platforms { get; }

    /// <summary>"N files change nothing in game" (spec 7.5), or "" while the pack has none. Written only after
    /// the scan of the pack's PNGs finishes, so nothing flashes on a pack that has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTransparent))]
    public partial string TransparentText { get; set; }

    public bool HasTransparent => TransparentText.Length > 0;

    /// <summary>The shell's own navigation command. PageViewModel.Shell is protected, so the markup cannot reach
    /// Shell.NavigatePacksCommand directly; this one-line property is the smallest way to bind it.</summary>
    public IRelayCommand BackToPacksCommand => Shell.NavigatePacksCommand;

    public string EmptyText => NoPackText;

    public string MapsEmptyText => NoMapsText;

    public string BackgroundsEmptyText => NoBackgroundsText;

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

    /// <summary>Throws away the previous pack's loads and starts this one's. Every section is built from the
    /// current snapshot, so a rescan and a change of pack take the same path.</summary>
    private void Rebuild()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        PutTogether.Clear();
        Backgrounds.Clear();
        Platforms.Clear();
        _transparentFiles = Array.Empty<string>();
        TransparentText = "";
        if (_snapshot is not { } snapshot || Pack is not { } pack)
        {
            return;
        }

        foreach (var map in MapsIn(snapshot.Catalog, pack))
        {
            PutTogether.Add(new PackMapTileViewModel(map, MapCompositor.CardWidth, MapCompositor.CardHeight, dropBackground: false));
            Platforms.Add(new PackMapTileViewModel(map, PlatformWidth, PlatformHeight, dropBackground: true));
        }

        // .jpg only: the game's backgrounds are all JPEGs, so a PNG that found its way into the folder fills no
        // slot and belongs in the transparent-files line below rather than in this grid.
        foreach (var file in pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (Path.GetExtension(file.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase))
            {
                Backgrounds.Add(new PackBackgroundTileViewModel(file));
            }
        }

        Load([.. PutTogether], [.. Backgrounds], [.. Platforms], pack, _cts.Token);
        LoadTransparent(pack, _cts.Token);
    }

    /// <summary>The maps the pack touches: the catalog maps it has at least one file for. A pack folder that is
    /// not a map, such as a theme folder other maps borrow from (spec 4), is not one of them.</summary>
    private static IEnumerable<MapEntry> MapsIn(MapCatalog catalog, Pack pack) =>
        catalog.Maps.Where(map => pack.FindFolder(map.FolderName) is { Files.Count: > 0 });

    /// <summary>Fills the tiles in view order: the composites, then the backgrounds, then the platform-only
    /// composites. One at a time, so the single render thread works down the page. Fire and forget, like
    /// PlatformsViewModel.LoadSelected.</summary>
    private async void Load(
        IReadOnlyList<PackMapTileViewModel> putTogether,
        IReadOnlyList<PackBackgroundTileViewModel> backgrounds,
        IReadOnlyList<PackMapTileViewModel> platforms,
        Pack pack,
        CancellationToken ct)
    {
        try
        {
            foreach (var tile in putTogether)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(tile, pack, ct);
            }

            foreach (var tile in backgrounds)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await Shell.Services.Thumbnails.GetAsync(tile.File.FullPath, tile.File.MtimeTicks, ct);
            }

            foreach (var tile in platforms)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(tile, pack, ct);
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
    private async Task<ImageSource?> ComposeAsync(PackMapTileViewModel tile, Pack pack, CancellationToken ct)
    {
        // Spec 3.6: with no level data there are no camera bounds and no platform tree, so there is nothing to
        // compose, and the pack's own art is the only picture of the map there is.
        if (_snapshot?.Catalog.HasLevelData == true)
        {
            try
            {
                var level = tile.DropBackground
                    ? tile.Map.BaseLevel with { Backgrounds = [] }
                    : tile.Map.BaseLevel;
                var sources = new AssetSources(Shell.Services.GamePath, pack.FullPath);
                var path = await Task.Run(
                    () => Shell.Services.Previews.GetOrRenderAsync(
                        level, tile.ComposeWidth, tile.ComposeHeight, sources, ct),
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

        var folder = pack.FindFolder(tile.Map.FolderName);
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

/// <summary>One map the pack touches, drawn either put together or with the background dropped. The two are the
/// same map at two sizes, so one type serves both grids.</summary>
public partial class PackMapTileViewModel : ObservableObject
{
    public PackMapTileViewModel(MapEntry map, int composeWidth, int composeHeight, bool dropBackground)
    {
        Map = map;
        ComposeWidth = composeWidth;
        ComposeHeight = composeHeight;
        DropBackground = dropBackground;
    }

    public MapEntry Map { get; }

    /// <summary>The caption: the map's in-game name, or its folder name without level data (spec 3.6).</summary>
    public string DisplayName => Map.DisplayName;

    /// <summary>The size the composite is rendered at, which is not the size the tile is shown at.</summary>
    public int ComposeWidth { get; }

    public int ComposeHeight { get; }

    /// <summary>True for the Platforms grid, where the level's backgrounds are dropped before the render.</summary>
    public bool DropBackground { get; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }
}

/// <summary>One image in the pack's own Backgrounds folder.</summary>
public partial class PackBackgroundTileViewModel : ObservableObject
{
    public PackBackgroundTileViewModel(GameFile file)
    {
        File = file;
    }

    public GameFile File { get; }

    /// <summary>The caption: the file's own name, which is the background slot it fills.</summary>
    public string Name => File.Name;

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }
}
