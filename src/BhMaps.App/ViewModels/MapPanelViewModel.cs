using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One file of the map's platform art: where the game's copy came from, and whether it is a fully
/// transparent PNG that changes nothing in game.</summary>
public sealed record PlatformFileViewModel(
    string RelativePath, string SourceText, bool ChangesNothing, ImageSource? Thumbnail)
{
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>Empty when the scan produced no status for the file, and then no tag is drawn.</summary>
    public bool ShowSource => SourceText.Length > 0;
}

/// <summary>Spec 3: one strip of any-map pictures in the map panel. The group is the one
/// <see cref="MapChoices.PictureGroups"/> built; the panel wraps it so every strip folds on its own, and so the
/// pictures under a strip are read the first time that strip is opened rather than with the panel.</summary>
public sealed partial class PictureGroupViewModel : ObservableObject
{
    private readonly Func<PictureGroupViewModel, Task> _load;

    /// <summary>True once the strip's thumbnails have been asked for, so opening and closing it reads the files
    /// once rather than once a click.</summary>
    private bool _thumbnailsStarted;

    public PictureGroupViewModel(PictureGroup group, Func<PictureGroupViewModel, Task> load)
    {
        _load = load;
        Header = group.Header;
        Tiles = group.Tiles;
    }

    /// <summary>The pack's name and how many of its pictures this map can take, or "In game only".</summary>
    public string Header { get; }

    public IReadOnlyList<CustomPictureTileViewModel> Tiles { get; }

    /// <summary>Whether the strip is open. Closed on every new panel (spec 7).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The strip's thumbnails are a file each, so they wait for the first time it is opened.</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (value && !_thumbnailsStarted)
        {
            _thumbnailsStarted = true;
            _ = _load(this);
        }
    }
}

/// <summary>Spec 3.2 and 7: the right panel for one map, as one scroll. The composed preview, the status
/// sentence, the two per-map actions, then the Background section over the Platforms section. Built fresh for
/// every selection and after every scan; <see cref="Cancel"/> stops the loads the panel it replaces still had in
/// flight.</summary>
public partial class MapPanelViewModel : ObservableObject
{
    /// <summary>The composite behind a set tile, rendered at twice the 156 by 88 the tile draws it at, so the
    /// picture is still sharp on a high DPI screen.</summary>
    public const int SetWidth = 312;
    public const int SetHeight = 176;

    public const string NoDefaultPackText = "No Default pack yet. Capture defaults first.";

    private readonly MainViewModel _shell;
    private readonly MapEntry _map;
    private readonly MapStatus? _status;
    private readonly ScanSnapshot _snapshot;

    /// <summary>ObservableCollections behind the read-only surfaces: the tiles fill their own pictures in as the
    /// loads arrive, and a file row is an immutable record that a load replaces rather than mutates.</summary>
    private readonly ObservableCollection<MapPictureTileViewModel> _backgroundTiles = [];
    private readonly ObservableCollection<CustomPictureTileViewModel> _customTiles = [];
    private readonly ObservableCollection<PlatformSetTileViewModel> _platformTiles = [];
    private readonly ObservableCollection<PlatformFileViewModel> _platformFiles = [];

    private readonly CancellationTokenSource _loads = new();

    public MapPanelViewModel(MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        _shell = shell;
        _map = map;
        _status = status;
        _snapshot = snapshot;
        DisplayName = map.DisplayName;
        SetsText = string.Join(", ", map.Sets.Select(MapCatalog.LabelFor));
        StatusText = BuildStatusText();
        HasDefaultPack = snapshot.DefaultPack is not null;
        ResetHint = HasDefaultPack ? "" : NoDefaultPackText;

        foreach (var tile in MapChoices.PackBackgrounds(shell, map, status, snapshot))
        {
            _backgroundTiles.Add(tile);
        }

        // Spec 3: every any-map picture, grouped by the pack it lives in and offered for this map the way a
        // pack's picture is. A map with no background slot has nowhere to put one, so it gets no strip at all.
        PictureGroups =
            [.. MapChoices.PictureGroups(shell, map, snapshot).Select(g => new PictureGroupViewModel(g, LoadGroupThumbnailsAsync))];
        foreach (var tile in PictureGroups.SelectMany(g => g.Tiles))
        {
            _customTiles.Add(tile);
        }

        foreach (var tile in MapChoices.Platforms(
                     shell, map, status, snapshot, SetWidth, SetHeight, () => FilesOpen = true))
        {
            _platformTiles.Add(tile);
        }

        // Built with no thumbnail and no transparency verdict: both are file work, and both arrive from LoadAsync.
        foreach (var relativePath in map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _platformFiles.Add(new PlatformFileViewModel(
                relativePath, InGameMatch.File(status, relativePath)?.Text ?? "", ChangesNothing: false, Thumbnail: null));
        }

        RebuildMenus(shell.SelectedMapCount);
    }

    public string DisplayName { get; }

    /// <summary>The sets the map is in, labelled the way the chip row labels them.</summary>
    public string SetsText { get; }

    /// <summary>Spec 3.2: what the game is showing for this map, in one sentence of words.</summary>
    public string StatusText { get; }

    /// <summary>Default first, then every pack with a picture for this map's first background slot.</summary>
    public IReadOnlyList<MapPictureTileViewModel> Backgrounds => _backgroundTiles;

    /// <summary>The any-map pictures in the strips the panel draws under the packs: one per pack, then the
    /// game's own (spec 3).</summary>
    public IReadOnlyList<PictureGroupViewModel> PictureGroups { get; }

    public IReadOnlyList<PlatformSetTileViewModel> Platforms => _platformTiles;

    public IReadOnlyList<PlatformFileViewModel> Files => _platformFiles;

    /// <summary>Null until the composite is ready, and then a 1280x720 source (spec 7.2).</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>False hides the headers altogether, so a library with no any-map picture shows nothing but the
    /// Add Image button.</summary>
    public bool HasCustomPictures => _customTiles.Count > 0;

    [ObservableProperty]
    public partial bool FilesOpen { get; set; }

    public string FilesHeader => $"Platform files, {Files.Count}";

    /// <summary>False disables Reset. Fixed for the life of the panel: a new scan builds a new panel.</summary>
    public bool HasDefaultPack { get; }

    /// <summary>Why Reset is off, or empty when it is on.</summary>
    public string ResetHint { get; }

    /// <summary>Every tile's menu names the ticked maps, so the count changing rewords every one of them.</summary>
    public void RebuildMenus(int tickedCount)
    {
        foreach (var tile in _backgroundTiles)
        {
            tile.RebuildMenu(tickedCount);
        }

        foreach (var tile in _customTiles)
        {
            tile.RebuildMenu(tickedCount);
        }

        foreach (var tile in _platformTiles)
        {
            tile.RebuildMenu(tickedCount);
        }
    }

    /// <summary>Fills the pictures, in the order the panel shows them: the preview, then the pack tiles, then the
    /// composites behind the sets. The picture strips are folded on a new panel, so nothing under them is read
    /// here. Fire and forget from Maps: every file failure is already a fallback rather than an error, so the
    /// only thing left to stop for is cancellation.</summary>
    public async Task LoadAsync()
    {
        var ct = _loads.Token;
        try
        {
            await LoadPreviewAsync(ct);
            await LoadTileThumbnailsAsync(_backgroundTiles, ct);
            await LoadPlatformPreviewsAsync(ct);
            await LoadPlatformFilesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // The panel was replaced, so what was still loading is for a map nothing shows any more.
        }
    }

    /// <summary>Stops the loads still in flight. Maps calls it when the panel is replaced or closed.</summary>
    public void Cancel() => _loads.Cancel();

    /// <summary>Spec 6.1, for this map alone: the Default pack's files for the folder and its copies of the map's
    /// background slots.</summary>
    [RelayCommand(CanExecute = nameof(HasDefaultPack))]
    private async Task ResetAsync()
    {
        if (_snapshot.DefaultPack is not { } defaultPack)
        {
            return;
        }

        var gamePath = _shell.Services.GamePath;
        var folderName = _map.FolderName;
        var slots = _map.BackgroundSlots;
        ResetOutcome? outcome = null;
        await _shell.RunGameWriteAsync(
            $"Resetting {DisplayName}",
            PackApplier.ResetMapPaths(_snapshot.Tree, _map, defaultPack),
            (_, ct) => Task.Run(() => { outcome = MapReset.ResetMap(gamePath, folderName, slots, defaultPack); }, ct),
            $"Reset {DisplayName} to default");

        if (outcome is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be reset", outcome.Failures);
        }
    }

    /// <summary>Opens the map's game folder, the way the Settings page opens its two.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var path = Path.Combine(_shell.Services.GamePath, _map.FolderName);
        if (ExplorerLauncher.Open(path) is { } error)
        {
            _shell.Dialogs.Error("Could not open the folder", error);
        }
    }

    /// <summary>Closes the panel by clearing the Maps page's selection, which is the one thing that opens it.</summary>
    [RelayCommand]
    private void Close() => _shell.Maps.Selected = null;

    /// <summary>Spec 7.1: the window opens with this map as its target, so "Add and apply to Brawlhaven" is the
    /// radio that starts on.</summary>
    [RelayCommand]
    private Task AddPictureAsync() =>
        _shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.Map, _map, null));

    /// <summary>Spec 3.2's one sentence: "Missing 2 files", "sunset, from My Backgrounds", "Default", or
    /// "In game: flowermap background, Default platforms".</summary>
    private string BuildStatusText()
    {
        var missing = _status?.Files.Count(f => f.State == MapFileState.Missing) ?? 0;
        if (missing > 0)
        {
            return missing == 1 ? "Missing 1 file" : $"Missing {missing} files";
        }

        var slot = _map.BackgroundSlots.Count > 0 ? _map.BackgroundSlots[0] : null;
        var file = slot is null ? null : InGameMatch.File(_status, AssetPath.Background(slot));
        if (file is { State: MapFileState.Custom })
        {
            // The picture the game is showing names the pack it lives in, which is where the user would look for
            // it again; a picture no pack holds is in the game and nowhere else (spec 3).
            var fileName = Path.GetFileName(AssetPath.Background(slot!));
            var picture = _snapshot.CustomPictures.FirstOrDefault(
                p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase));
            var name = Path.GetFileNameWithoutExtension(picture?.DisplayName ?? fileName);
            return picture?.PackName is { } pack ? $"{name}, from {pack}" : $"{name}, in game only";
        }

        var background = file is { State: MapFileState.Pack, PackNames.Count: > 0 }
            ? file.PackNames[0]
            : DefaultPack.Name;
        var platforms = PlatformSource();
        return background == DefaultPack.Name && platforms == DefaultPack.Name
            ? "Default"
            : $"In game: {background} background, {platforms} platforms";
    }

    /// <summary>The game's own art beats a pack beats Default, over this map's own folder only.</summary>
    private string PlatformSource()
    {
        var files = (_status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(_map.FolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return "in game only";
        }

        return files.SelectMany(f => f.PackNames).FirstOrDefault() ?? DefaultPack.Name;
    }

    private async Task LoadPreviewAsync(CancellationToken ct)
    {
        var image = _snapshot.Catalog.HasLevelData
            ? await ComposeAsync(_map.BaseLevel, MapCompositor.PanelWidth, MapCompositor.PanelHeight, null, null, ct)
            : null;

        // A preview is never worth an error dialog, so a composite that could not be drawn becomes the file tile.
        image ??= await FolderThumbnailAsync(_snapshot.Tree.FindFolder(_map.FolderName), ct);
        if (image is not null && !ct.IsCancellationRequested)
        {
            Preview = image;
        }
    }

    /// <summary>One strip's pictures, the first time it is opened: a library of two hundred of them costs nothing
    /// until the strip holding them is asked for. Started from the strip rather than from LoadAsync, so it
    /// catches the cancellation LoadAsync catches for its own loads.</summary>
    private async Task LoadGroupThumbnailsAsync(PictureGroupViewModel group)
    {
        try
        {
            await LoadTileThumbnailsAsync(group.Tiles, _loads.Token);
        }
        catch (OperationCanceledException)
        {
            // The panel was replaced, so what was still loading is for a map nothing shows any more.
        }
    }

    private async Task LoadTileThumbnailsAsync(IEnumerable<PictureTileViewModel> tiles, CancellationToken ct)
    {
        foreach (var tile in tiles)
        {
            ct.ThrowIfCancellationRequested();
            await tile.LoadThumbnailAsync(_shell.Services, ct);
        }
    }

    /// <summary>The pack's platform art over the map's current background, so the tile shows what would change.</summary>
    private async Task LoadPlatformPreviewsAsync(CancellationToken ct)
    {
        var gamePath = _shell.Services.GamePath;
        var background = _map.BaseLevel.Backgrounds.Count == 0
            ? null
            : Path.Combine(gamePath, AssetPath.Background(_map.BaseLevel.Backgrounds[0].AssetName));
        foreach (var tile in _platformTiles)
        {
            ct.ThrowIfCancellationRequested();
            var image = _snapshot.Catalog.HasLevelData
                ? await ComposeAsync(_map.BaseLevel, SetWidth, SetHeight, tile.Pack.FullPath, background, ct)
                : null;
            image ??= await FolderThumbnailAsync(tile.Pack.FindFolder(_map.FolderName), ct);
            if (image is not null)
            {
                tile.Preview = image;
            }
        }
    }

    private async Task LoadPlatformFilesAsync(CancellationToken ct)
    {
        var gamePath = _shell.Services.GamePath;
        for (var i = 0; i < _platformFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = _platformFiles[i];
            var fullPath = Path.Combine(gamePath, row.RelativePath);
            var changesNothing = await ChangesNothingAsync(fullPath, ct);
            var thumbnail = await ThumbnailAsync(fullPath, ct);
            if (changesNothing || thumbnail is not null)
            {
                _platformFiles[i] = row with { ChangesNothing = changesNothing, Thumbnail = thumbnail };
            }
        }
    }

    /// <summary>Every composite goes through the preview cache, never MapCompositor.Render: the cache queues the
    /// render on the one STA thread and keeps the result. Null when the picture could not be drawn.</summary>
    private async Task<ImageSource?> ComposeAsync(
        LevelDesc level, int width, int height, string? packRoot, string? backgroundPath, CancellationToken ct)
    {
        var previews = _shell.Services.Previews;
        var sources = new AssetSources(_shell.Services.GamePath, packRoot, backgroundPath);
        try
        {
            // The whole call goes on the pool: GetOrRenderAsync hashes every input file on its caller's thread
            // before it reaches the render queue, and the decode after it is as much file work again.
            return await Task.Run<ImageSource?>(
                async () =>
                {
                    var path = await previews.GetOrRenderAsync(level, width, height, sources, ct).ConfigureAwait(false);
                    return Decode(path);
                },
                ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            // A file that vanished between the scan and the render, or a preview that could not be written.
            return null;
        }
    }

    /// <summary>The v1 single-file tile: the folder's largest image, which stands in for a composite that could not
    /// be drawn and for the whole no-level-data fallback (spec 3.6).</summary>
    private async Task<ImageSource?> FolderThumbnailAsync(GameFolder? folder, CancellationToken ct)
    {
        if (folder is null || ThumbnailProvider.PickRepresentative(folder) is not { } file)
        {
            return null;
        }

        return await _shell.Services.Thumbnails.GetAsync(file.FullPath, file.MtimeTicks, ct);
    }

    /// <summary>A thumbnail for a path the scan did not measure, so its mtime is read off the UI thread first.</summary>
    private async Task<ImageSource?> ThumbnailAsync(string fullPath, CancellationToken ct)
    {
        try
        {
            var mtimeTicks = await Task.Run(() => File.GetLastWriteTimeUtc(fullPath).Ticks, ct);
            return await _shell.Services.Thumbnails.GetAsync(fullPath, mtimeTicks, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Only a PNG can be fully transparent, and the check decodes the whole file, so it stays off the UI
    /// thread and off every JPG.</summary>
    private static Task<bool> ChangesNothingAsync(string fullPath, CancellationToken ct) =>
        Path.GetExtension(fullPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? Task.Run(() => TransparentPng.IsFullyTransparent(fullPath), ct)
            : Task.FromResult(false);

    /// <summary>Decoded whole and frozen off the UI thread, so nothing is read from disk while the panel draws.</summary>
    private static ImageSource Decode(string path)
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
}
