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

/// <summary>Spec 3.2: the right panel for one map. The composed preview, the status sentence, the two per-map
/// actions, and one of two segments: the backgrounds on offer, or the platform sets with the file list under
/// them. Built fresh for every selection and after every scan; <see cref="Cancel"/> stops the loads the panel it
/// replaces still had in flight.</summary>
public partial class MapPanelViewModel : ObservableObject
{
    /// <summary>The composite behind a set tile, rendered at twice the 156 by 88 the tile draws it at, so the
    /// picture is still sharp on a high DPI screen.</summary>
    public const int SetWidth = 312;
    public const int SetHeight = 176;

    public const string NoDefaultPackText = "No Default pack yet. Capture defaults first.";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly MainViewModel _shell;
    private readonly MapsViewModel _page;
    private readonly MapEntry _map;
    private readonly MapStatus? _status;
    private readonly ScanSnapshot _snapshot;

    /// <summary>ObservableCollections behind the read-only surfaces: the tiles fill their own pictures in as the
    /// loads arrive, and a file row is an immutable record that a load replaces rather than mutates.</summary>
    private readonly ObservableCollection<MapPictureTileViewModel> _backgroundTiles = [];
    private readonly ObservableCollection<MapPictureTileViewModel> _customTiles = [];
    private readonly ObservableCollection<PlatformSetTileViewModel> _platformTiles = [];
    private readonly ObservableCollection<PlatformFileViewModel> _platformFiles = [];

    private readonly CancellationTokenSource _loads = new();

    /// <summary>True once the strip's thumbnails have been asked for, so opening and closing it reads the files
    /// once rather than once a click.</summary>
    private bool _customThumbnailsStarted;

    public MapPanelViewModel(MainViewModel shell, MapsViewModel page, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        _shell = shell;
        _page = page;
        _map = map;
        _status = status;
        _snapshot = snapshot;
        DisplayName = map.DisplayName;
        SetsText = string.Join(", ", map.Sets.Select(MapCatalog.LabelFor));
        StatusText = BuildStatusText();
        HasDefaultPack = snapshot.DefaultPack is not null;
        ResetHint = HasDefaultPack ? "" : NoDefaultPackText;
        ShowPlatforms = page.PanelShowsPlatforms;

        foreach (var tile in BuildBackgroundTiles())
        {
            _backgroundTiles.Add(tile);
        }

        // Spec 4: every picture in the custom library, offered for this map the way a pack's is. A map with no
        // background slot has nowhere to put one, so it gets no strip at all.
        if (map.BackgroundSlots.Count > 0)
        {
            var slot = map.BackgroundSlots[0];
            var fileName = Path.GetFileName(AssetPath.Background(slot));
            foreach (var picture in snapshot.CustomPictures)
            {
                // Resolved by the rule a custom tile's own menu uses, so the panel and the Backgrounds page apply
                // the same file. Empty only for the picture SourcePath calls impossible, and then every action on
                // the tile reports a file that is not there rather than throwing on a path nobody could resolve.
                var source = CustomPictureTileViewModel.SourcePath(picture, shell.Services.GamePath) ?? "";
                _customTiles.Add(new MapPictureTileViewModel(
                    shell, map, slot, picture.DisplayName, "", source, picture.PackName,
                    picture.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase)));
            }
        }

        foreach (var pack in PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs))
        {
            _platformTiles.Add(new PlatformSetTileViewModel(
                shell, map, pack, InGameMatch.SetInGame(pack, map.FolderName, status),
                SetWidth, SetHeight, () => FilesExpanded = true));
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
    public IReadOnlyList<MapPictureTileViewModel> BackgroundTiles => _backgroundTiles;

    /// <summary>Spec 4: the custom library, under the packs, for this map's first background slot.</summary>
    public IReadOnlyList<MapPictureTileViewModel> CustomTiles => _customTiles;

    public IReadOnlyList<PlatformSetTileViewModel> PlatformTiles => _platformTiles;

    public IReadOnlyList<PlatformFileViewModel> PlatformFiles => _platformFiles;

    /// <summary>Null until the composite is ready, and then a 1280x720 source (spec 7.2).</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>Which half of the segment is showing. Remembered on the page, so the next map opens on the same
    /// one (spec 3.2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBackground), nameof(ShowBackgroundSegment), nameof(ShowPlatformsSegment))]
    public partial bool ShowPlatforms { get; set; }

    public bool ShowBackground => !ShowPlatforms;

    /// <summary>The two segments bind these rather than ShowPlatforms directly, the same shape the Add Custom
    /// Image fit radios use: a click on the segment that is already on is ignored instead of turning both off.</summary>
    public bool ShowBackgroundSegment
    {
        get => !ShowPlatforms;
        set
        {
            if (value)
            {
                ShowPlatforms = false;
            }
        }
    }

    public bool ShowPlatformsSegment
    {
        get => ShowPlatforms;
        set
        {
            if (value)
            {
                ShowPlatforms = true;
            }
        }
    }

    /// <summary>Whether the custom pictures strip is open. Closed on every new panel: the pictures under it are
    /// read only once it is asked for.</summary>
    [ObservableProperty]
    public partial bool CustomExpanded { get; set; }

    public string CustomHeader => $"Custom pictures ({_customTiles.Count})";

    /// <summary>False hides the header altogether, so a library with no custom picture shows nothing but the
    /// Add Custom Image button.</summary>
    public bool HasCustomPictures => _customTiles.Count > 0;

    [ObservableProperty]
    public partial bool FilesExpanded { get; set; }

    public string FilesHeader => $"Platform files, {PlatformFiles.Count}";

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

    /// <summary>Fills the pictures, in the order the panel shows them: the preview, then the Background segment
    /// that opens, then the composites behind the sets. Fire and forget from Maps: every file failure is already
    /// a fallback rather than an error, so the only thing left to stop for is cancellation.</summary>
    public async Task LoadAsync()
    {
        var ct = _loads.Token;
        try
        {
            await LoadPreviewAsync(ct);
            await LoadTileThumbnailsAsync(_backgroundTiles, ct);

            // Only when the strip is already open, which it is not on a panel nobody has expanded yet.
            if (CustomExpanded)
            {
                await LoadCustomThumbnailsAsync(ct);
            }

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

    partial void OnShowPlatformsChanged(bool value) => _page.PanelShowsPlatforms = value;

    /// <summary>The strip's thumbnails are a file each, so they wait for the first time it is opened.</summary>
    partial void OnCustomExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadCustomThumbnailsAsync(_loads.Token);
        }
    }

    /// <summary>Default first, then every pack with a picture for this map's first slot (spec 3.2).</summary>
    private IEnumerable<MapPictureTileViewModel> BuildBackgroundTiles()
    {
        if (_map.BackgroundSlots.Count == 0)
        {
            yield break;
        }

        var slot = _map.BackgroundSlots[0];
        var relative = AssetPath.Background(slot);
        var fileName = Path.GetFileName(relative);
        var packs = _snapshot.Packs
            .OrderBy(p => p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            if (pack.FindFolder(BackgroundsFolder)?.FindFile(fileName) is not { } file)
            {
                continue;
            }

            yield return new MapPictureTileViewModel(
                _shell, _map, slot, pack.Name, "", file.FullPath, pack.Name,
                InGameMatch.Matches(_status, relative, pack.Name));
        }
    }

    /// <summary>Spec 3.2's one sentence: "Missing 2 files", "Custom picture: sunset.jpg", "Default", or
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
            return $"Custom picture: {CustomPictureName(slot!)}";
        }

        var background = file is { State: MapFileState.Pack, PackNames.Count: > 0 }
            ? file.PackNames[0]
            : DefaultPack.Name;
        var platforms = PlatformSource();
        return background == DefaultPack.Name && platforms == DefaultPack.Name
            ? "Default"
            : $"In game: {background} background, {platforms} platforms";
    }

    /// <summary>The name the user knows the picture by, from the custom library, or the slot's own file name.</summary>
    private string CustomPictureName(string slot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        return _snapshot.CustomPictures
                   .FirstOrDefault(p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                   ?.DisplayName
               ?? fileName;
    }

    /// <summary>Custom beats a pack beats Default, over this map's own folder only.</summary>
    private string PlatformSource()
    {
        var files = (_status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(_map.FolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return "Custom";
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

    /// <summary>The strip's pictures, once: a library of two hundred of them costs nothing until it is opened,
    /// and opening it a second time costs nothing again. Started from the expander as well as from LoadAsync, so
    /// it catches the cancellation LoadAsync catches for its own loads.</summary>
    private async Task LoadCustomThumbnailsAsync(CancellationToken ct)
    {
        if (_customThumbnailsStarted)
        {
            return;
        }

        _customThumbnailsStarted = true;
        try
        {
            await LoadTileThumbnailsAsync(_customTiles, ct);
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
