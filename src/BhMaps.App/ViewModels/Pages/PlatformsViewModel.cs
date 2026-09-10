using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.4: the platform sets on offer for one map, drawn over that map's current background, or one
/// row per map across the whole catalog. Which map is "this map" comes from the sidebar.</summary>
public partial class PlatformsViewModel : PageViewModel
{
    /// <summary>The tile size for the selected map: half the card preview, so the camera bounds keep their shape.</summary>
    public const int TileWidth = 480;

    public const int TileHeight = 270;

    /// <summary>The same tile on an All maps row, half again as small so a row fits several sets.</summary>
    public const int RowTileWidth = 240;

    public const int RowTileHeight = 135;

    public const string NoMapsText = "Pick a map in the sidebar.";

    public const string NoSetsText = "No pack has platform art for this map.";

    public const string NoDefaultPackText = "No Default pack yet";

    private const string BackgroundsFolder = "Backgrounds";

    private ScanSnapshot? _snapshot;

    /// <summary>Cancels the loads for the selected map; replaced, never disposed, exactly as
    /// BackgroundEditorViewModel.ScheduleRender does, because the loads still hold the token.</summary>
    private CancellationTokenSource? _selectedCts;

    private CancellationTokenSource? _allMapsCts;

    /// <summary>False until the All maps rows have been asked for their previews for this snapshot. The rows are
    /// built with every scan; their previews wait until the segment is actually shown.</summary>
    private bool _allMapsLoadStarted;

    public PlatformsViewModel(MainViewModel shell)
        : base(shell)
    {
        Sets = [];
        Files = [];
        AllMaps = [];
        ResetHint = "";

        // The sidebar decides which map this page is about, and it can change while the page is up. The shell
        // outlives the page, so there is nothing to unsubscribe from.
        Shell.PropertyChanged += OnShellChanged;
    }

    public override string Title => "Platforms";

    /// <summary>The selected map's sets, largest tile size. Rebuilt whenever the map or the scan changes.</summary>
    public ObservableCollection<PlatformSetTileViewModel> Sets { get; }

    /// <summary>The selected map's platform files, with a thumbnail and where the file came from.</summary>
    public ObservableCollection<PlatformFileRowViewModel> Files { get; }

    /// <summary>One row per map in the catalog, each with the same set tiles at the smaller size.</summary>
    public ObservableCollection<PlatformMapRowViewModel> AllMaps { get; }

    /// <summary>Which half of the segmented control is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSelectedMap))]
    public partial bool ShowAllMaps { get; set; }

    public bool ShowSelectedMap => !ShowAllMaps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedMapName))]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    public partial MapEntry? SelectedMap { get; set; }

    /// <summary>The first segment's label.</summary>
    public string SelectedMapName => SelectedMap?.DisplayName ?? "This map";

    /// <summary>False when the catalog holds no maps at all, which is the page's empty state.</summary>
    [ObservableProperty]
    public partial bool HasMaps { get; set; }

    /// <summary>True when the selected map has no sets at all: no pack, not even Default, has a file for it.</summary>
    [ObservableProperty]
    public partial bool ShowNoSets { get; set; }

    /// <summary>Why the header's Reset is off, or "" when it is on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResetTooltip))]
    public partial string ResetHint { get; set; }

    public string ResetTooltip => ResetHint.Length > 0
        ? "There is no Default pack to reset to. Capture defaults first."
        : "Copies the Default pack's files for this map back into the game.";

    public string EmptyText => NoMapsText;

    public string SetsEmptyText => NoSetsText;

    public string FilesHeader => $"Platform files, {Files.Count}";

    private bool CanReset => SelectedMap is not null && _snapshot?.DefaultPack is not null;

    /// <summary>Rebuilds both halves of the page. The files behind every composite may have changed, so this
    /// reloads them; a mere change of selection does not (see <see cref="UpdateSelectedMap"/>).</summary>
    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;
        HasMaps = snapshot.Catalog.Maps.Count > 0;
        ResetHint = snapshot.DefaultPack is null ? NoDefaultPackText : "";
        ResetCommand.NotifyCanExecuteChanged();
        UpdateSelectedMap(force: true);
        BuildAllMaps();
    }

    [RelayCommand]
    private void ShowMap() => ShowAllMaps = false;

    [RelayCommand]
    private void ShowAll() => ShowAllMaps = true;

    /// <summary>Spec 6.1: the Default pack's files for this folder, plus its copies of the map's background slots.</summary>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAsync()
    {
        if (_snapshot is not { DefaultPack: { } defaultPack } snapshot || SelectedMap is not { } map)
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        var slots = map.BackgroundSlots;
        ResetOutcome? outcome = null;
        await Shell.RunGameWriteAsync(
            $"Resetting {map.DisplayName}",
            ResetPaths(snapshot, map, defaultPack),
            (_, ct) => Task.Run(() => { outcome = MapReset.ResetMap(gamePath, map.FolderName, slots, defaultPack); }, ct),
            $"Reset {map.DisplayName} to default");
        if (outcome is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be reset", outcome.Failures);
        }
    }

    /// <summary>Everything the reset may write: what the folder holds now, what the Default pack would put there,
    /// and the background slots the Default pack can restore.</summary>
    private static IReadOnlyList<string> ResetPaths(ScanSnapshot snapshot, MapEntry map, Pack defaultPack)
    {
        var backgrounds = defaultPack.FindFolder(BackgroundsFolder);
        var slots = map.BackgroundSlots.Where(slot => backgrounds?.FindFile(slot) is not null).ToList();
        return (snapshot.Tree.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>())
            .Select(file => Path.Combine(map.FolderName, file.Name))
            .Concat(PlatformSetApplier.TargetPaths(defaultPack, map.FolderName))
            .Concat(BackgroundApplier.TargetPaths(slots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedMapCount is raised in the same breath as SelectedMaps, so watching one of the two is enough.
        if (e.PropertyName is nameof(MainViewModel.SelectedMaps) or nameof(MainViewModel.LastOpenedMap))
        {
            UpdateSelectedMap(force: false);
        }
    }

    /// <summary>Spec 7.4: the first ticked map in the sidebar, else the map last opened on Home, else the first
    /// map in the catalog.</summary>
    private MapEntry? ResolveSelectedMap(ScanSnapshot snapshot)
    {
        if (Shell.SelectedMaps.FirstOrDefault() is { } ticked
            && snapshot.Catalog.ByFolder(ticked.FolderName) is { } tickedMap)
        {
            return tickedMap;
        }

        if (Shell.LastOpenedMap is { } folderName && snapshot.Catalog.ByFolder(folderName) is { } opened)
        {
            return opened;
        }

        return snapshot.Catalog.Maps.FirstOrDefault();
    }

    /// <summary>Follows the sidebar. A change that resolves to the map already shown rebuilds nothing, so ticking
    /// a second map never re-renders the first one's composites.</summary>
    private void UpdateSelectedMap(bool force)
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        var map = ResolveSelectedMap(snapshot);
        if (!force && string.Equals(map?.FolderName, SelectedMap?.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelectedMap = map;
        BuildSelected(snapshot, map);
    }

    private void BuildSelected(ScanSnapshot snapshot, MapEntry? map)
    {
        _selectedCts?.Cancel();
        _selectedCts = new CancellationTokenSource();
        Sets.Clear();
        Files.Clear();
        if (map is not null)
        {
            var status = snapshot.MapStatuses.GetValueOrDefault(map.FolderName);
            foreach (var tile in TilesFor(snapshot, map, status, TileWidth, TileHeight))
            {
                Sets.Add(tile);
            }

            foreach (var row in FileRows(snapshot, map, status))
            {
                Files.Add(row);
            }
        }

        ShowNoSets = map is not null && Sets.Count == 0;
        OnPropertyChanged(nameof(FilesHeader));
        LoadSelected([.. Sets], [.. Files], _selectedCts.Token);
    }

    private void BuildAllMaps()
    {
        _allMapsCts?.Cancel();
        _allMapsLoadStarted = false;
        AllMaps.Clear();
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        foreach (var map in snapshot.Catalog.Maps)
        {
            var status = snapshot.MapStatuses.GetValueOrDefault(map.FolderName);
            AllMaps.Add(new PlatformMapRowViewModel(map, TilesFor(snapshot, map, status, RowTileWidth, RowTileHeight)));
        }

        if (ShowAllMaps)
        {
            StartAllMapsLoad();
        }
    }

    partial void OnShowAllMapsChanged(bool value)
    {
        if (value)
        {
            StartAllMapsLoad();
        }
    }

    /// <summary>62 maps times a few sets is more composites than any one screen shows, so the rows only ask for
    /// their previews once the segment is on, and only once per scan.</summary>
    private void StartAllMapsLoad()
    {
        if (_allMapsLoadStarted || AllMaps.Count == 0)
        {
            return;
        }

        _allMapsLoadStarted = true;
        _allMapsCts = new CancellationTokenSource();
        LoadAllMaps([.. AllMaps], _allMapsCts.Token);
    }

    /// <summary>Spec 6.4: the Default pack first, then every other pack with at least one file for this folder.
    /// A pack with nothing for the map is not a set and is not listed.</summary>
    private IReadOnlyList<PlatformSetTileViewModel> TilesFor(
        ScanSnapshot snapshot, MapEntry map, MapStatus? status, int width, int height) =>
        PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs)
            .Select(pack => new PlatformSetTileViewModel(
                Shell, map, pack, IsInGame(pack, map.FolderName, status), width, height))
            .ToList();

    /// <summary>A set is the one in the game when every file it would write is already the file that is there.
    /// The measurement is MapStatusDetector's, so the tick agrees with the sidebar's state tag: a file matching
    /// both Default and another pack counts as the other pack's (spec 6.2).</summary>
    private static bool IsInGame(Pack pack, string folderName, MapStatus? status)
    {
        if (status is null)
        {
            return false;
        }

        var paths = PlatformSetApplier.TargetPaths(pack, folderName);
        if (paths.Count == 0)
        {
            return false;
        }

        var isDefault = pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var file = status.Files.FirstOrDefault(f => f.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (file is null)
            {
                return false;
            }

            var matches = isDefault
                ? file.State == MapFileState.Default
                : file.PackNames.Contains(pack.Name, StringComparer.OrdinalIgnoreCase);
            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The map's platform files (spec 4), each with the state the scan gave it. A file borrowed from
    /// another folder through "../" has no state of its own, so it shows without a tag.</summary>
    private static IReadOnlyList<PlatformFileRowViewModel> FileRows(
        ScanSnapshot snapshot, MapEntry map, MapStatus? status)
    {
        var rows = new List<PlatformFileRowViewModel>();
        foreach (var relativePath in map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var file = snapshot.Tree
                .FindFolder(AssetPath.FolderOf(relativePath))
                ?.FindFile(Path.GetFileName(relativePath));
            var fileStatus = status?.Files
                .FirstOrDefault(f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
            var missing = file is null;
            var sourceText = fileStatus?.Text ?? "";
            rows.Add(new PlatformFileRowViewModel(relativePath, sourceText, missing, file));
        }

        return rows;
    }

    /// <summary>Fills the selected map's tiles and then its file thumbnails, one at a time so the render queue
    /// works in page order. Fire and forget, like BackgroundEditorViewModel.ScheduleRender.</summary>
    private async void LoadSelected(
        IReadOnlyList<PlatformSetTileViewModel> tiles,
        IReadOnlyList<PlatformFileRowViewModel> rows,
        CancellationToken ct)
    {
        try
        {
            foreach (var tile in tiles)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(tile, ct);
            }

            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                row.Thumbnail = await ThumbnailAsync(row.File, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer selection or a rescan.
        }
    }

    private async void LoadAllMaps(IReadOnlyList<PlatformMapRowViewModel> rows, CancellationToken ct)
    {
        try
        {
            foreach (var tile in rows.SelectMany(row => row.Sets))
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(tile, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a rescan.
        }
    }

    /// <summary>The set drawn over the map's current background, through the preview cache so the render is
    /// queued on the one STA thread and kept on disk. A preview that cannot be composed falls back to a plain
    /// file tile: it is a picture, never an error dialog.</summary>
    private async Task<ImageSource?> ComposeAsync(PlatformSetTileViewModel tile, CancellationToken ct)
    {
        var gamePath = Shell.Services.GamePath;

        // Spec 3.6: with no level data there are no camera bounds and no platform tree, so there is nothing to
        // compose; the pack's own art is the only picture of the set there is.
        if (_snapshot?.Catalog.HasLevelData == true)
        {
            try
            {
                var sources = new AssetSources(gamePath, tile.Pack.FullPath, CurrentBackground(tile.Map, gamePath));
                var path = await Task.Run(
                    () => Shell.Services.Previews.GetOrRenderAsync(
                        tile.Map.BaseLevel, tile.TileWidth, tile.TileHeight, sources, ct),
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

        var folder = tile.Pack.FindFolder(tile.Map.FolderName);
        return await ThumbnailAsync(folder is null ? null : ThumbnailProvider.PickRepresentative(folder), ct);
    }

    /// <summary>The game's own copy of the map's first background slot, so a pack that carries backgrounds of its
    /// own still shows its platforms over the background the game is showing now (spec 7.4).</summary>
    private static string? CurrentBackground(MapEntry map, string gamePath) =>
        map.BaseLevel.Backgrounds.Count == 0
            ? null
            : Path.Combine(gamePath, AssetPath.Background(map.BaseLevel.Backgrounds[0].AssetName));

    private async Task<ImageSource?> ThumbnailAsync(GameFile? file, CancellationToken ct) =>
        file is null ? null : await Shell.Services.Thumbnails.GetAsync(file.FullPath, file.MtimeTicks, ct);

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
}

/// <summary>One platform set on offer for one map: a pack that has at least one file for that map's folder,
/// drawn over the map's current background, with the apply that puts it in the game.</summary>
public partial class PlatformSetTileViewModel : ObservableObject
{
    private readonly MainViewModel _shell;

    public PlatformSetTileViewModel(MainViewModel shell, MapEntry map, Pack pack, bool inGame, int width, int height)
    {
        _shell = shell;
        Map = map;
        Pack = pack;
        InGame = inGame;
        TileWidth = width;
        TileHeight = height;
    }

    public MapEntry Map { get; }

    public Pack Pack { get; }

    public string PackName => Pack.Name;

    /// <summary>True when this is the set the game is showing. It wears the tick and the ring.</summary>
    public bool InGame { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>Spec 6.4: copies every file the pack has for this folder, transparent files included.</summary>
    [RelayCommand]
    private async Task UseAsync()
    {
        var gamePath = _shell.Services.GamePath;
        var folderName = Map.FolderName;
        var pack = Pack;
        ApplyResult? result = null;
        await _shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            PlatformSetApplier.TargetPaths(pack, folderName),
            (progress, ct) => Task.Run(
                () => { result = PlatformSetApplier.Apply(pack, folderName, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {Map.DisplayName}");
        if (result is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }
}

/// <summary>One file of the selected map's platform art: where it lives, where it came from, and what it looks
/// like.</summary>
public partial class PlatformFileRowViewModel : ObservableObject
{
    public PlatformFileRowViewModel(string relativePath, string sourceText, bool isMissing, GameFile? file)
    {
        RelativePath = relativePath;
        SourceText = sourceText;
        IsMissing = isMissing;
        File = file;
    }

    /// <summary>"Folder\file.png", relative to mapArt.</summary>
    public string RelativePath { get; }

    /// <summary>"Default", the pack names, "Custom" or "Missing". Empty for a file no scan measured.</summary>
    public string SourceText { get; }

    /// <summary>The game folder has no such file. The only coloured state (spec 6.2).</summary>
    public bool IsMissing { get; }

    public bool ShowTag => !IsMissing && SourceText.Length > 0;

    /// <summary>The file as the scan saw it, or null when the game folder does not have it.</summary>
    public GameFile? File { get; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }
}

/// <summary>One row of the All maps list: a map and the same set tiles at the smaller size.</summary>
public sealed class PlatformMapRowViewModel
{
    public PlatformMapRowViewModel(MapEntry map, IReadOnlyList<PlatformSetTileViewModel> sets)
    {
        FolderName = map.FolderName;
        DisplayName = map.DisplayName;
        Sets = sets;
    }

    public string FolderName { get; }

    public string DisplayName { get; }

    public IReadOnlyList<PlatformSetTileViewModel> Sets { get; }

    public bool ShowNoSets => Sets.Count == 0;

    public string NoSetsText => PlatformsViewModel.NoSetsText;
}
