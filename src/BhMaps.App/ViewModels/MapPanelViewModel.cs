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

/// <summary>One file of the map's platform art: where the game's copy came from, whether it is a fully
/// transparent PNG that changes nothing in game, and the Edit that opens the editor on this file alone
/// (ruling 7). <paramref name="CanEdit" /> is false until the scan has seen the game's copy on disk, because
/// there is nothing to edit without it.</summary>
public sealed record PlatformFileViewModel(
    string RelativePath, string SourceText, bool ChangesNothing, ImageSource? Thumbnail, bool CanEdit,
    IRelayCommand EditCommand)
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

    public const string NoDefaultPackText = "Needs the Default pack. Capture it on the Packs page.";

    /// <summary>3.0: the same reason in the width a tooltip has, for the Reset controls that have no room for
    /// the sentence above.</summary>
    public const string NoDefaultPackTip = "Needs the Default pack.";

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
        SetsText = MapCatalog.SetsSentence(map.Sets);
        SetsToolTip = string.Join(", ", map.Sets);
        ThumbnailNote = shell.ThumbnailNotes.GetValueOrDefault(map.FolderName, "");
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

        // Built with no thumbnail, no transparency verdict and no Edit: all three are file work, and all three
        // arrive from LoadAsync. The Edit opens the editor on the game's file, so it hands it no pack (ruling 7).
        foreach (var relativePath in map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var file = relativePath;
            _platformFiles.Add(new PlatformFileViewModel(
                file, InGameMatch.File(status, file)?.Text ?? "", ChangesNothing: false, Thumbnail: null,
                CanEdit: false, EditCommand: new RelayCommand(() => _ = shell.OpenPlatformEditorAsync([map], pack: null, onlyFile: file))));
        }

        RebuildMenus();
    }

    public string DisplayName { get; }

    /// <summary>3.0: the sets the map is in, said in the words a player uses.</summary>
    public string SetsText { get; }

    /// <summary>Every set the game lists the map in, raw, so the codes the sentence drops are still one hover
    /// away for anyone who wants them.</summary>
    public string SetsToolTip { get; }

    /// <summary>Spec 10.4: why the last write left this map's map-select thumbnail alone, or empty when it wrote
    /// it or never aimed at it. Fixed for the life of the panel: a new scan builds a new panel.</summary>
    public string ThumbnailNote { get; }

    /// <summary>False hides the note rather than leaving an empty line under the sets.</summary>
    public bool HasThumbnailNote => ThumbnailNote.Length > 0;

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

    /// <summary>Why Reset is off, in a tooltip, or null while it is on and there is nothing to explain.</summary>
    public string? ResetToolTip => HasDefaultPack ? null : NoDefaultPackTip;

    /// <summary>Fills every tile's menu, which the panel does once, when it is built.</summary>
    public void RebuildMenus()
    {
        foreach (var tile in _backgroundTiles)
        {
            tile.RebuildMenu();
        }

        foreach (var tile in _customTiles)
        {
            tile.RebuildMenu();
        }

        foreach (var tile in _platformTiles)
        {
            tile.RebuildMenu();
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
        var map = _map;

        // Spec 7: the files go back to default, so the edits the packs the map matches remembered for it go too.
        _snapshot.MapStatuses.TryGetValue(folderName, out var status);
        var matched = RecordReset.MatchedPacks(map, status, _snapshot.Packs);
        ResetOutcome? outcome = null;
        var resetPaths = PackApplier.ResetMapPaths(_snapshot.Tree, _map, defaultPack);
        await _shell.RunGameWriteAsync(
            $"Resetting {DisplayName}",
            resetPaths,
            (_, ct) => Task.Run(
                () =>
                {
                    outcome = MapReset.ResetMap(gamePath, folderName, slots, defaultPack);
                    RecordReset.Clear(map, matched);
                },
                ct),
            $"Reset {DisplayName} to the Default pack.",
            libraryUndoPaths: RecordReset.UndoPaths(matched, _shell.Services.LibraryPath),
            artMaps: [map],
            resetThumbnails: true,
            sources: AppliedSources.FromPack(defaultPack, resetPaths));

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
        _shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.Map, _map));

    /// <summary>Spec 3.2's one sentence, said in words as of 3.0: "Missing 2 files.", "sunset, from My
    /// Backgrounds.", "From the Default pack.", "From dark." or "In game only.". No file name, no slot code and
    /// no pack the app cannot name: a person reads this line, not the folder.</summary>
    private string BuildStatusText()
    {
        var missing = _status?.Files.Count(f => f.State == MapFileState.Missing) ?? 0;
        if (missing > 0)
        {
            return missing == 1 ? "Missing 1 file." : $"Missing {missing} files.";
        }

        var slot = _map.BackgroundSlots.Count > 0 ? _map.BackgroundSlots[0] : null;
        var file = slot is null ? null : InGameMatch.File(_status, AssetPath.Background(slot));
        if (file is { State: MapFileState.Custom })
        {
            // The picture the game is showing names the pack it lives in, which is where the user would look for
            // it again. A file the library no longer holds has no name a person would know, and its slot code is
            // not one, so the line says only where the art is (spec 3).
            var fileName = Path.GetFileName(AssetPath.Background(slot!));
            var picture = _snapshot.CustomPictures.FirstOrDefault(
                p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase));
            return picture is { PackName: { } pack }
                ? $"{picture.Name}, from {pack}."
                : "In game only.";
        }

        // The background names the source when a pack put it there; otherwise the platforms are the only thing
        // left that a pack could have touched.
        var source = file is { State: MapFileState.Pack, PackNames.Count: > 0 }
            ? file.PackNames[0]
            : PlatformSource();
        if (source is null)
        {
            return "In game only.";
        }

        // Without a captured Default pack the game's own files match nothing the app holds, so "the Default pack"
        // would name something that is not there.
        return source.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            ? _snapshot.DefaultPack is null ? "In game only." : "From the Default pack."
            : $"From {source}.";
    }

    /// <summary>The game's own art beats a pack beats Default, over this map's own folder only. Null is the
    /// game's own art, which belongs to no pack this app can name.</summary>
    private string? PlatformSource()
    {
        var files = (_status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(_map.FolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return null;
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

    /// <summary>The scan did not measure these paths, so the row's own probe answers both questions the rest of
    /// the load asks: whether the file is there, and the mtime the thumbnail cache keys on. A missing file reads
    /// as 1601 rather than throwing, so it is the Exists answer that keeps it out of the decode.</summary>
    private async Task LoadPlatformFilesAsync(CancellationToken ct)
    {
        var gamePath = _shell.Services.GamePath;
        for (var i = 0; i < _platformFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var row = _platformFiles[i];
            var fullPath = Path.Combine(gamePath, row.RelativePath);
            var (exists, mtimeTicks) = await Task.Run(
                () => (File.Exists(fullPath), File.GetLastWriteTimeUtc(fullPath).Ticks), ct);
            if (!exists)
            {
                // Nothing is known about a file that is not there, so the row is only put back to nothing when it
                // is reading as something already.
                if (row.CanEdit || row.ChangesNothing || row.Thumbnail is not null)
                {
                    _platformFiles[i] = row with { ChangesNothing = false, Thumbnail = null, CanEdit = false };
                }

                continue;
            }

            var changesNothing = await ChangesNothingAsync(fullPath, ct);
            ImageSource? thumbnail;
            try
            {
                thumbnail = await _shell.Services.Thumbnails.GetAsync(fullPath, mtimeTicks, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The file went between the probe and the decode.
                thumbnail = null;
            }

            _platformFiles[i] = row with { ChangesNothing = changesNothing, Thumbnail = thumbnail, CanEdit = true };
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
