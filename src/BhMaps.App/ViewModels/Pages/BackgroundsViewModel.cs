using System.Collections.ObjectModel;
using System.ComponentModel;
using BhMaps.App.Services;
using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.3: the whole background library on one grid, the header on one line, and a bottom bar while the
/// sidebar has maps ticked. There is no New background action; the editor opens from a tile.</summary>
public partial class BackgroundsViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinZoom;
    public const int MaxZoom = AppSettings.MaxZoom;

    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>Every tile the last scan produced. Tiles is this list under the search box.</summary>
    private readonly List<BackgroundTileViewModel> _all = [];

    /// <summary>The hash of the game file behind each background slot, by its "Folder\file" path. Filled off the
    /// UI thread after every scan, so ticking a map is set arithmetic rather than file work.</summary>
    private readonly Dictionary<string, string> _slotHashes = new(StringComparer.OrdinalIgnoreCase);

    private ScanSnapshot? _snapshot;
    private CancellationTokenSource? _thumbnails;

    public BackgroundsViewModel(MainViewModel shell)
        : base(shell)
    {
        Tiles = [];

        // A stored zoom from another version, or a hand-edited one, is clamped rather than trusted.
        Zoom = Math.Clamp(shell.Services.Settings.BackgroundsZoom, MinZoom, MaxZoom);

        // The header's search box is the shell's one string (spec 7.1) and the bottom bar follows the sidebar's
        // ticks. The page lives as long as the shell, so there is nothing to unsubscribe from.
        shell.PropertyChanged += OnShellChanged;
    }

    public override string Title => "Backgrounds";

    /// <summary>The tiles the search box leaves visible, sorted by pack then file name as the library is.</summary>
    public ObservableCollection<BackgroundTileViewModel> Tiles { get; }

    /// <summary>Columns in the grid, MinZoom to MaxZoom, persisted as backgroundsZoom.</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    /// <summary>True while at least one map is ticked in the sidebar, which is when the bottom bar shows and a
    /// tile's Apply has somewhere to write.</summary>
    public bool HasSelection => Shell.SelectedMapCount > 0;

    /// <summary>The bottom bar's line: "Apply to 1 map" or "Apply to N maps".</summary>
    public string SelectionText =>
        Shell.SelectedMapCount == 1 ? "Apply to 1 map" : $"Apply to {Shell.SelectedMapCount} maps";

    /// <summary>True when the library holds nothing at all, as against being filtered to nothing by the search
    /// box. The two states say different things and offer different ways out (spec 7.8).</summary>
    public bool IsLibraryEmpty => _all.Count == 0;

    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;

        // Every tile is replaced, so the thumbnails still in flight are for objects nothing shows any more.
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        _thumbnails = new CancellationTokenSource();

        _all.Clear();
        _slotHashes.Clear();
        foreach (var background in snapshot.Backgrounds)
        {
            _all.Add(new BackgroundTileViewModel(background));
        }

        OnPropertyChanged(nameof(IsLibraryEmpty));
        ApplyFilter();
        _ = LoadAsync([.. _all], snapshot, _thumbnails.Token);
    }

    /// <summary>Spec 7.3's one header action, and the empty state's button. Task 27 fills the window in; null means
    /// the sidebar's ticks decide which maps the pictures also go to.</summary>
    [RelayCommand]
    private Task AddPicturesAsync() => Shell.OpenAddPicturesAsync(null);

    /// <summary>Spec 6.3: one write covering every ticked map, with a confirm that names them when there is more
    /// than one. The game-running policy belongs to the launcher inside RunGameWriteAsync, so there is no second
    /// prompt here.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync(BackgroundTileViewModel? tile)
    {
        if (tile is null || _snapshot is not { } snapshot)
        {
            return;
        }

        var maps = SelectedMaps(snapshot).Where(m => m.BackgroundSlots.Count > 0).ToList();
        if (maps.Count == 0)
        {
            // Without level data a map has no background slots at all (spec 3.6), so there is nothing to write.
            Shell.Dialogs.Info(
                "Nothing to apply to",
                "The ticked maps have no background slots. Slots come from the game's own level data.");
            return;
        }

        if (maps.Count > 1
            && !Shell.Dialogs.Confirm(
                "Apply background",
                $"Apply {tile.FileName} to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}"))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        var source = tile.FullPath;
        var undoPaths = maps
            .SelectMany(m => BackgroundApplier.TargetPaths(m.BackgroundSlots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var failures = new List<FileFailure>();
        await Shell.RunGameWriteAsync(
            $"Applying {tile.FileName}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in maps)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(BackgroundApplier.Apply(source, gamePath, map.BackgroundSlots, null, ct).Failures);
                    }
                },
                ct),
            maps.Count == 1
                ? $"{tile.FileName} applied to 1 map"
                : $"{tile.FileName} applied to {maps.Count} maps");

        Shell.Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }

    /// <summary>Spec 7.3's second tile action. The editor works on a game background slot rather than on a library
    /// file: it fits a picture the user picks and saves it into a pack under the slot's name, optionally writing it
    /// straight into the game. What it is handed is therefore a slot name.</summary>
    [RelayCommand]
    private Task EditAsync(BackgroundTileViewModel? tile) =>
        tile is null ? Task.CompletedTask : Shell.OpenBackgroundEditorAsync(EditSlot(tile));

    /// <summary>The no-results state's way back (spec 7.8).</summary>
    [RelayCommand]
    private void ClearSearch() => Shell.SearchText = "";

    partial void OnZoomChanged(int value)
    {
        if (Shell.Services.Settings.BackgroundsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsZoom = value });
        }
    }

    private bool CanApply(BackgroundTileViewModel? tile) => tile is not null && Shell.SelectedMapCount > 0;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SearchText))
        {
            ApplyFilter();
        }

        // SelectedMaps is raised alongside this one; reacting to the count alone does the work once.
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectionText));
            ApplyCommand.NotifyCanExecuteChanged();
            UpdateTicks();
        }
    }

    /// <summary>The ticked sidebar rows as catalog entries. A row whose folder the catalog does not know is left
    /// out rather than guessed at.</summary>
    private IEnumerable<MapEntry> SelectedMaps(ScanSnapshot snapshot) =>
        Shell.SelectedMaps
            .Select(m => snapshot.Catalog.ByFolder(m.FolderName))
            .OfType<MapEntry>();

    /// <summary>The slot the editor should open on: this tile's own name when the game has a background by that
    /// name, otherwise the first slot a ticked map uses. Null lets the editor pick its first slot.</summary>
    private string? EditSlot(BackgroundTileViewModel tile)
    {
        if (_snapshot is not { } snapshot)
        {
            return null;
        }

        if (tile.FromGame || snapshot.Tree.FindFolder(BackgroundsFolder)?.FindFile(tile.FileName) is not null)
        {
            return tile.FileName;
        }

        return SelectedMaps(snapshot).SelectMany(m => m.BackgroundSlots).FirstOrDefault();
    }

    /// <summary>In use means the picture, not the name. Applying writes the chosen picture into the map's slot
    /// under the slot's own name, so the tile that was clicked and the game's copy of that slot are one picture
    /// under two names; matching on the name alone would tick the wrong tile. The comparison is the content hash
    /// the scan already holds, taken off the UI thread beforehand.</summary>
    private void UpdateTicks()
    {
        var inUse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_snapshot is { } snapshot)
        {
            foreach (var slot in SelectedMaps(snapshot).SelectMany(m => m.BackgroundSlots))
            {
                // A slot with no entry is one the game has no file for: nothing is on screen, so nothing ticks.
                if (_slotHashes.TryGetValue(AssetPath.Background(slot), out var hash))
                {
                    inUse.Add(hash);
                }
            }
        }

        foreach (var tile in _all)
        {
            tile.IsInUse = tile.Hash is { } hash && inUse.Contains(hash);
        }
    }

    /// <summary>The hashes the ticks compare, taken through the scan's cache so almost every one is a dictionary
    /// lookup: the scan has just hashed both the packs' pictures and the game files behind the slots. Anything the
    /// cache has not seen is hashed once here, off the UI thread, and cached for the next scan.</summary>
    private static (Dictionary<string, string> Pictures, Dictionary<string, string> Slots) ComputeHashes(
        IReadOnlyList<BackgroundTileViewModel> tiles, ScanSnapshot snapshot, HashCache hashes, CancellationToken ct)
    {
        var files = LibraryFiles(snapshot);
        var pictures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in tiles)
        {
            ct.ThrowIfCancellationRequested();
            if (files.TryGetValue(tile.FullPath, out var file) && Hash(hashes, file) is { } hash)
            {
                pictures[tile.FullPath] = hash;
            }
        }

        var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in snapshot.Catalog.Maps.SelectMany(m => m.BackgroundSlots))
        {
            ct.ThrowIfCancellationRequested();

            // Backgrounds\<slot>, except for a slot borrowed from a theme folder through "../".
            var relative = AssetPath.Background(slot);
            if (slots.ContainsKey(relative))
            {
                continue;
            }

            var file = snapshot.Tree
                .FindFolder(AssetPath.FolderOf(relative))
                ?.FindFile(Path.GetFileName(relative));
            if (file is not null && Hash(hashes, file) is { } hash)
            {
                slots[relative] = hash;
            }
        }

        return (pictures, slots);
    }

    /// <summary>The scan's own record for every picture on the grid, by full path, so the hashes are asked for on
    /// the size and mtime the scan measured and come back without touching the disk.</summary>
    private static Dictionary<string, GameFile> LibraryFiles(ScanSnapshot snapshot)
    {
        var files = new Dictionary<string, GameFile>(StringComparer.OrdinalIgnoreCase);
        var folders = snapshot.Packs
            .Select(pack => pack.FindFolder(BackgroundsFolder))
            .Append(snapshot.Tree.FindFolder(BackgroundsFolder));

        foreach (var file in folders.SelectMany(folder => folder?.Files ?? Array.Empty<GameFile>()))
        {
            files[file.FullPath] = file;
        }

        return files;
    }

    /// <summary>Null for a file that has gone since the scan, which simply never ticks.</summary>
    private static string? Hash(HashCache hashes, GameFile file)
    {
        try
        {
            return hashes.GetOrCompute(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The two things a tile needs that the snapshot does not carry: the hash behind its tick and the
    /// picture itself. Hashes first, because they are nearly free and the ticks are the answer to a question the
    /// user has already asked by ticking a map.</summary>
    private async Task LoadAsync(
        IReadOnlyList<BackgroundTileViewModel> tiles, ScanSnapshot snapshot, CancellationToken ct)
    {
        var hashes = Shell.Services.HashCache;
        try
        {
            var (pictures, slots) = await Task.Run(() => ComputeHashes(tiles, snapshot, hashes, ct), ct);
            foreach (var tile in tiles)
            {
                tile.Hash = pictures.GetValueOrDefault(tile.FullPath);
            }

            foreach (var (relative, hash) in slots)
            {
                _slotHashes[relative] = hash;
            }

            UpdateTicks();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await LoadThumbnailsAsync(tiles, ct);
    }

    /// <summary>Fills the tiles one at a time, in grid order, so the ones on screen fill first. Fire and forget:
    /// the tile turns its own file failures into a blank picture, so the only thing left to stop for is
    /// cancellation.</summary>
    private async Task LoadThumbnailsAsync(IReadOnlyList<BackgroundTileViewModel> tiles, CancellationToken ct)
    {
        foreach (var tile in tiles)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await tile.LoadThumbnailAsync(Shell.Services, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void ApplyFilter()
    {
        Tiles.Clear();
        foreach (var tile in _all.Where(Matches))
        {
            Tiles.Add(tile);
        }
    }

    /// <summary>The header search box, on the file name or the pack it came from.</summary>
    private bool Matches(BackgroundTileViewModel tile)
    {
        var search = Shell.SearchText;
        return search.Length == 0
            || tile.FileName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || tile.PackName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
}
