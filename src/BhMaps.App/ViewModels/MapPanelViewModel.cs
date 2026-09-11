using System.Collections.ObjectModel;
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

namespace BhMaps.App.ViewModels;

/// <summary>One background the panel offers: a pack's, or the game's own copy of a slot this map uses.
/// InUse marks the one the game is showing, as the scan measured it.</summary>
public sealed record BackgroundChoiceViewModel(
    LibraryBackground Background, bool InUse, IAsyncRelayCommand ApplyCommand, ImageSource? Thumbnail)
{
    public string FileName => Background.FileName;

    public string PackName => Background.PackName;

    /// <summary>"BG_Grove.jpg (dark)": the tile is 96 px wide, so the pack only fits in the tooltip.</summary>
    public string Label => $"{Background.FileName} ({Background.PackName})";
}

/// <summary>One platform set on offer for this map: the Default pack, or any pack with files for its folder.
/// The preview is a platform-only composite, so the tile shows what the set itself brings.</summary>
public sealed record PlatformSetViewModel(
    string PackName, bool InGame, ImageSource? Preview, IAsyncRelayCommand UseCommand);

/// <summary>One file of the map's platform art: where the game's copy came from, and whether it is a fully
/// transparent PNG that changes nothing in game.</summary>
public sealed record PlatformFileViewModel(
    string RelativePath, string SourceText, bool ChangesNothing, ImageSource? Thumbnail)
{
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>Empty when the scan produced no status for the file, and then no tag is drawn.</summary>
    public bool ShowSource => SourceText.Length > 0;
}

/// <summary>Spec 7.2: the right panel for one map. The composed preview, the background candidates, the platform
/// sets, the platform files, and the two per-map actions. Built fresh for every selection and after every scan;
/// <see cref="Cancel"/> stops the loads the panel it replaces still had in flight.</summary>
public partial class MapPanelViewModel : ObservableObject
{
    /// <summary>The platform-only composite behind a set tile, drawn at the same 16:9 as everything else.</summary>
    public const int SetWidth = 320;
    public const int SetHeight = 180;

    public const string NoBackgroundText = "None";
    public const string NoDefaultPackText = "No Default pack yet. Capture defaults first.";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly MainViewModel _shell;
    private readonly MapEntry _map;
    private readonly ScanSnapshot _snapshot;

    /// <summary>The three lists are ObservableCollections behind the read-only surface: the records are immutable,
    /// so a picture that lands replaces its row rather than mutating it.</summary>
    private readonly ObservableCollection<BackgroundChoiceViewModel> _backgroundChoices = [];
    private readonly ObservableCollection<PlatformSetViewModel> _platformSets = [];
    private readonly ObservableCollection<PlatformFileViewModel> _platformFiles = [];

    private readonly CancellationTokenSource _loads = new();

    public MapPanelViewModel(MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        _shell = shell;
        _map = map;
        _snapshot = snapshot;
        DisplayName = map.DisplayName;
        HasDefaultPack = snapshot.DefaultPack is not null;
        ResetHint = HasDefaultPack ? "" : NoDefaultPackText;

        // The slot the game draws first is the map's current background; a map without one can take no picture.
        CurrentBackgroundName = map.BackgroundSlots.Count > 0 ? map.BackgroundSlots[0] : NoBackgroundText;

        foreach (var choice in BuildChoices(status))
        {
            _backgroundChoices.Add(choice);
        }

        foreach (var pack in PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs))
        {
            _platformSets.Add(new PlatformSetViewModel(
                pack.Name,
                IsInGame(pack, map.FolderName, status),
                Preview: null,
                new AsyncRelayCommand(() => UseSetAsync(pack))));
        }

        // Built with no thumbnail and no transparency verdict: both are file work, and both arrive from LoadAsync.
        // Sorted the way the Platforms page sorts the same list, so one map reads the same on both pages.
        foreach (var relativePath in map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _platformFiles.Add(new PlatformFileViewModel(
                relativePath, SourceOf(relativePath, status), ChangesNothing: false, Thumbnail: null));
        }
    }

    public string DisplayName { get; }

    /// <summary>The first background slot the map's levels name, or "None".</summary>
    public string CurrentBackgroundName { get; }

    /// <summary>Every pack's backgrounds, plus the game's own copies of this map's slots. The one in game first.</summary>
    public IReadOnlyList<BackgroundChoiceViewModel> BackgroundChoices => _backgroundChoices;

    public IReadOnlyList<PlatformSetViewModel> PlatformSets => _platformSets;

    public IReadOnlyList<PlatformFileViewModel> PlatformFiles => _platformFiles;

    /// <summary>Null until the composite is ready, and then a 1280x720 source (spec 7.2).</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial bool FilesExpanded { get; set; }

    public string FilesHeader => $"Platform files, {PlatformFiles.Count}";

    /// <summary>False disables Reset. Fixed for the life of the panel: a new scan builds a new panel.</summary>
    public bool HasDefaultPack { get; }

    /// <summary>Why Reset is off, or empty when it is on.</summary>
    public string ResetHint { get; }

    /// <summary>Fills the pictures, in the order the panel shows them. Fire and forget from Home: every file
    /// failure is already a fallback rather than an error, so the only thing left to stop for is cancellation.</summary>
    public async Task LoadAsync()
    {
        var ct = _loads.Token;
        try
        {
            await LoadPreviewAsync(ct);
            await LoadPlatformSetsAsync(ct);
            await LoadBackgroundThumbnailsAsync(ct);
            await LoadPlatformFilesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // The panel was replaced, so what was still loading is for a map nothing shows any more.
        }
    }

    /// <summary>Stops the loads still in flight. Home calls it when the panel is replaced or closed.</summary>
    public void Cancel() => _loads.Cancel();

    /// <summary>Spec 6.8. The window is Task 27's; the panel only says which map the pictures are for.</summary>
    [RelayCommand]
    private Task AddPictureAsync() => _shell.OpenAddPicturesAsync(_map.FolderName);

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
            ResetPaths(defaultPack),
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

    /// <summary>Spec 6.3: one map's slots, so no confirm. Applying to more than one map is the Backgrounds page's
    /// job and does need one.</summary>
    private async Task ApplyBackgroundAsync(LibraryBackground background)
    {
        var gamePath = _shell.Services.GamePath;
        var sourcePath = background.FullPath;
        var slots = _map.BackgroundSlots;
        ApplyResult? result = null;
        await _shell.RunGameWriteAsync(
            $"Applying {background.FileName}",
            BackgroundApplier.TargetPaths(slots),
            (progress, ct) => Task.Run(
                () => { result = BackgroundApplier.Apply(sourcePath, gamePath, slots, progress, ct); }, ct),
            $"{background.FileName} applied to {DisplayName}");

        if (result is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>Spec 6.4: every file the pack has for this folder, transparent ones included.</summary>
    private async Task UseSetAsync(Pack pack)
    {
        var gamePath = _shell.Services.GamePath;
        var folderName = _map.FolderName;
        ApplyResult? result = null;
        await _shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            PlatformSetApplier.TargetPaths(pack, folderName),
            (progress, ct) => Task.Run(
                () => { result = PlatformSetApplier.Apply(pack, folderName, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {DisplayName}");

        if (result is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>The folder's current files, the Default pack's files for it, and the background slots ResetMap
    /// writes: everything the reset overwrites has to be in the undo set (spec 6.6).</summary>
    private IReadOnlyList<string> ResetPaths(Pack defaultPack)
    {
        var folderName = _map.FolderName;
        var backgrounds = defaultPack.FindFolder(BackgroundsFolder);

        // Only the slots the Default pack can actually restore: ResetMap skips the rest, so nothing else is written.
        var slots = _map.BackgroundSlots.Where(slot => backgrounds?.FindFile(slot) is not null).ToList();

        var current = _snapshot.Tree.FindFolder(folderName)?.Files.Select(f => Path.Combine(folderName, f.Name))
            ?? Array.Empty<string>();
        return current
            .Concat(PlatformSetApplier.TargetPaths(defaultPack, folderName))
            .Concat(BackgroundApplier.TargetPaths(slots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every pack's backgrounds, plus the game's own copy of a slot this map uses. The rest of the game's
    /// Backgrounds folder belongs to other maps, so it is not on offer here.</summary>
    private IReadOnlyList<BackgroundChoiceViewModel> BuildChoices(MapStatus? status)
    {
        var slots = _map.BackgroundSlots;

        // Without a slot there is nowhere to write, so the candidates are shown and disabled rather than hidden.
        var canApply = slots.Count > 0;
        return _snapshot.Backgrounds
            .Where(b => !b.FromGame || slots.Contains(b.FileName, StringComparer.OrdinalIgnoreCase))
            .Select(b => new BackgroundChoiceViewModel(
                b,
                IsInUse(b, slots, status),
                new AsyncRelayCommand(() => ApplyBackgroundAsync(b), () => canApply),
                Thumbnail: null))
            .OrderBy(c => c.InUse ? 0 : 1)
            .ToList();
    }

    /// <summary>What the game is showing now, as the scan measured it: the game's own copy of a slot, and any pack
    /// the scan found byte-identical to it. Never re-hashes anything.</summary>
    private static bool IsInUse(LibraryBackground background, IReadOnlyList<string> slots, MapStatus? status)
    {
        if (!slots.Contains(background.FileName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (background.FromGame)
        {
            return true;
        }

        var file = FileStatus(status, AssetPath.Background(background.FileName));
        return Matches(file, background.PackName);
    }

    /// <summary>The tick and the ring: every file the set would write is in game already, as the scan measured it.</summary>
    private static bool IsInGame(Pack pack, string folderName, MapStatus? status)
    {
        var paths = PlatformSetApplier.TargetPaths(pack, folderName);
        if (status is null || paths.Count == 0)
        {
            return false;
        }

        return paths.All(path => Matches(FileStatus(status, path), pack.Name));
    }

    /// <summary>A file matches the Default pack only in the Default state: MapStatusDetector counts a file matching
    /// both Default and another pack as the other pack's (spec 6.2).</summary>
    private static bool Matches(MapFileStatus? file, string packName) =>
        file is not null
        && (packName.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            ? file.State == MapFileState.Default
            : file.PackNames.Contains(packName, StringComparer.OrdinalIgnoreCase));

    private static MapFileStatus? FileStatus(MapStatus? status, string relativePath) =>
        status?.Files.FirstOrDefault(f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Default", the pack names, "Custom" or "Missing"; empty when the scan measured no such file.</summary>
    private static string SourceOf(string relativePath, MapStatus? status) =>
        FileStatus(status, relativePath)?.Text ?? "";

    private async Task LoadPreviewAsync(CancellationToken ct)
    {
        var image = _snapshot.Catalog.HasLevelData
            ? await ComposeAsync(_map.BaseLevel, MapCompositor.PanelWidth, MapCompositor.PanelHeight, null, ct)
            : null;

        // A preview is never worth an error dialog, so a composite that could not be drawn becomes the file tile.
        image ??= await FolderThumbnailAsync(_snapshot.Tree.FindFolder(_map.FolderName), ct);
        if (image is not null && !ct.IsCancellationRequested)
        {
            Preview = image;
        }
    }

    /// <summary>The brief's platform-only composite: the map's level with its backgrounds dropped, drawn out of the
    /// pack, so the tile shows the set and not the picture behind it.</summary>
    private async Task LoadPlatformSetsAsync(CancellationToken ct)
    {
        var level = _map.BaseLevel with { Backgrounds = [] };
        for (var i = 0; i < _platformSets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var set = _platformSets[i];
            if (FindPack(set.PackName) is not { } pack)
            {
                continue;
            }

            var image = _snapshot.Catalog.HasLevelData
                ? await ComposeAsync(level, SetWidth, SetHeight, pack.FullPath, ct)
                : null;
            image ??= await FolderThumbnailAsync(pack.FindFolder(_map.FolderName), ct);
            if (image is not null)
            {
                _platformSets[i] = set with { Preview = image };
            }
        }
    }

    private async Task LoadBackgroundThumbnailsAsync(CancellationToken ct)
    {
        for (var i = 0; i < _backgroundChoices.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var choice = _backgroundChoices[i];
            if (await ThumbnailAsync(choice.Background.FullPath, ct) is { } image)
            {
                _backgroundChoices[i] = choice with { Thumbnail = image };
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
        LevelDesc level, int width, int height, string? packRoot, CancellationToken ct)
    {
        var previews = _shell.Services.Previews;
        var sources = new AssetSources(_shell.Services.GamePath, packRoot);
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

    private Pack? FindPack(string name) =>
        _snapshot.Packs.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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
