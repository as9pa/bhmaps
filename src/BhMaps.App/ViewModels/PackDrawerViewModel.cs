using BhMaps.App.Services;
using BhMaps.App.ViewModels.Pages;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the drawer's file list: what the pack holds for this map, with its size on disk and its
/// pixel size (spec 5). Dimensions arrive from LoadAsync, so the list appears at once.</summary>
public partial class PackFileRowViewModel : ObservableObject
{
    public PackFileRowViewModel(GameFile file)
    {
        File = file;
        Dimensions = "";
    }

    public GameFile File { get; }

    public string Name => File.Name;

    /// <summary>"842 KB". Whole units; a background is never small enough for a decimal to matter.</summary>
    public string SizeText => File.Size >= 1024 * 1024
        ? $"{File.Size / 1024.0 / 1024.0:0.0} MB"
        : File.Size >= 1024 ? $"{File.Size / 1024} KB" : $"{File.Size} B";

    /// <summary>"1920 x 1080", or "" for a file whose header would not read.</summary>
    [ObservableProperty]
    public partial string Dimensions { get; set; }
}

/// <summary>Spec 5: pack detail's drawer for one tile. The composite the grid already rendered, what the game is
/// showing for the map, and the pack's own files for it with their sizes. Built fresh for every open and after
/// every scan; <see cref="Cancel"/> stops the loads the drawer it replaces still had in flight. The pack-detail
/// twin of <see cref="MapPanelViewModel"/>.</summary>
public partial class PackDrawerViewModel : ObservableObject
{
    private const string BackgroundsFolder = "Backgrounds";

    private readonly MainViewModel _shell;
    private readonly PackDetailViewModel _page;
    private readonly Pack _pack;
    private readonly MapEntry? _map;

    private readonly CancellationTokenSource _loads = new();

    public PackDrawerViewModel(
        MainViewModel shell,
        PackDetailViewModel page,
        Pack pack,
        MapEntry? map,
        MapStatus? status,
        PackTileViewModel preview)
    {
        _shell = shell;
        _page = page;
        _pack = pack;
        _map = map;
        Preview = preview;
        DisplayName = map?.DisplayName ?? preview.Caption;
        SetsText = map is null ? "" : string.Join(", ", map.Sets.Select(MapCatalog.LabelFor));
        InGameText = map is null
            ? "In game: nothing uses this picture"
            : $"In game: {status?.Text ?? "Default"}";
        CanApply = map is not null;
        Files = [.. FilesFor(pack, map, preview.File).Select(file => new PackFileRowViewModel(file))];
    }

    /// <summary>The map's name, or the file name when no map owns the picture.</summary>
    public string DisplayName { get; }

    /// <summary>The level sets the map is in, labelled as the chips label them, or "".</summary>
    public string SetsText { get; }

    /// <summary>"In game: " and the map's status text, or "In game: nothing uses this picture" when no map owns
    /// the slot.</summary>
    public string InGameText { get; }

    /// <summary>The tile whose composite the drawer shows, bound through so nothing renders twice (C-D5).</summary>
    public PackTileViewModel Preview { get; }

    public IReadOnlyList<PackFileRowViewModel> Files { get; }

    /// <summary>False for a background tile no map claims: there is no map to apply it to (decision C-D4).</summary>
    public bool CanApply { get; }

    /// <summary>Reads each row's pixel size off the UI thread, in view order.</summary>
    public async Task LoadAsync()
    {
        var ct = _loads.Token;
        try
        {
            foreach (var row in Files)
            {
                ct.ThrowIfCancellationRequested();
                var size = await Task.Run(() => ImageDimensions.Read(row.File.FullPath), ct);
                row.Dimensions = size is { } s ? $"{s.Width} x {s.Height}" : "";
            }
        }
        catch (OperationCanceledException)
        {
            // The drawer was replaced or closed.
        }
    }

    /// <summary>Stops the loads still in flight. Pack detail calls it when the drawer is replaced or closed.</summary>
    public void Cancel() => _loads.Cancel();

    /// <summary>The pack's files for this map's folder, then its backgrounds for this map's slots. A tile no map
    /// owns has the one file the tile itself is.</summary>
    private static IReadOnlyList<GameFile> FilesFor(Pack pack, MapEntry? map, GameFile? file)
    {
        if (map is null)
        {
            return file is null ? Array.Empty<GameFile>() : [file];
        }

        var folder = pack.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>();
        var backgrounds = pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>();
        var slots = backgrounds.Where(f => map.BackgroundSlots.Any(s => s.Equals(f.Name, StringComparison.OrdinalIgnoreCase)));
        return [.. folder, .. slots];
    }

    /// <summary>Spec 5: this pack, this map, one click. One map, so no confirm; the write is the shell's, so the
    /// undo snapshot, the busy boundary and the done sentence come with it (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyToThisMapAsync()
    {
        if (_map is not { } map)
        {
            return;
        }

        var gamePath = _shell.Services.GamePath;
        var pack = _pack;
        ApplyResult? result = null;
        await _shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            PackApplier.ApplyToMapsPaths(pack, [map]),
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyToMaps(pack, [map], gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {map.DisplayName}");

        if (result is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>The pack's own folder for this map, so the drawer opens what it is describing.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var path = _map is { } map && _pack.FindFolder(map.FolderName) is { } folder ? folder.FullPath : _pack.FullPath;
        if (ExplorerLauncher.Open(path) is { } error)
        {
            _shell.Dialogs.Error("Could not open the folder", error);
        }
    }

    [RelayCommand]
    private void Close() => _page.CloseDrawer();
}
