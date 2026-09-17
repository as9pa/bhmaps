using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One pack's platform art for one map (spec 4): the preview the page composes into it, the tick that
/// says the game is showing that set already, and the menu its hover button opens. Show files belongs to the
/// page, not to the tile, so the page hands its own action in, and hands null where there is no file list to
/// open (spec 9).</summary>
public sealed partial class PlatformSetTileViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private readonly Action? _showFiles;
    private readonly Action _edit;

    public PlatformSetTileViewModel(
        MainViewModel shell, MapEntry map, Pack pack, bool inGame, int width, int height, Action? showFiles,
        Action edit)
    {
        _shell = shell;
        _showFiles = showFiles;
        _edit = edit;
        Map = map;
        Pack = pack;
        InGame = inGame;
        TileWidth = width;
        TileHeight = height;
        MenuItems = [];
    }

    /// <summary>The map the set is offered for. Its folder is the one the pack's files are copied into.</summary>
    public MapEntry Map { get; }

    public Pack Pack { get; }

    public string PackName => Pack.Name;

    /// <summary>The caption under the thumbnail, and the tile's automation name. The same member the picture
    /// tiles carry, so one row template draws both (addendum C).</summary>
    public string Title => Pack.Name;

    /// <summary>The picture tiles' name for <see cref="InGame" />, for the same reason.</summary>
    public bool IsInGame => InGame;

    /// <summary>The picture tiles' name for <see cref="Preview" />.</summary>
    public ImageSource? Thumbnail => Preview;

    /// <summary>What a click on the tile does: one set onto its own map, live (addendum C).</summary>
    public ICommand ApplyCommand => ApplyToMapCommand;

    /// <summary>Addendum C: a set tile's tooltip is the pack it came from, which is also its caption.</summary>
    public string ToolTipText => Pack.Name;

    /// <summary>True when every file the set would write is the file that is there, as the last scan measured it.</summary>
    public bool InGame { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    /// <summary>Null until the composite is ready. Always frozen, because it is drawn off the UI thread.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Thumbnail))]
    public partial ImageSource? Preview { get; set; }

    /// <summary>The rows of the tile's menu, as the last <see cref="RebuildMenu" /> left them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }

    /// <summary>Rebuilt whenever the ticked count changes, because the ticked line names it and is dropped at zero.</summary>
    public void RebuildMenu(int tickedCount)
    {
        // No "Apply to <map>": a click on the tile is that apply, and so is the panel's Apply button, and a menu
        // holds only what the tile cannot do on its own (spec 9).
        var items = new List<TileMenuCommand>
        {
            TileMenuCommand.Header(Pack.Name, $"Platforms for {Map.DisplayName}"),
            new("Edit", EditCommand),
        };
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(
                tickedCount == 1 ? "Apply to the 1 selected map" : $"Apply to the {tickedCount} selected maps",
                ApplyToTickedCommand));
        }

        // Only where there is a file list to open: on a row the line would do nothing, so it is not offered.
        if (_showFiles is not null)
        {
            items.Add(new TileMenuCommand("Show files", ShowFilesCommand));
        }

        items.Add(new TileMenuCommand("Open folder", OpenFolderCommand));

        // The Default pack is the art a reset restores, so there is nothing to delete it into.
        if (!Pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase))
        {
            items.Add(TileMenuCommand.Separator());
            items.Add(new TileMenuCommand("Delete platform set", DeleteCommand, IsDestructive: true));
        }

        MenuItems = items;
    }

    /// <summary>The pack's files for this map's folder, deleted, and the map's platforms put back to default
    /// when the game is showing this set. Its pictures are left alone: a platform delete is not a background
    /// delete.</summary>
    [RelayCommand]
    private Task DeleteAsync()
    {
        if (_shell.Snapshot is not { } snapshot)
        {
            return Task.CompletedTask;
        }

        var status = snapshot.MapStatuses.TryGetValue(Map.FolderName, out var found) ? found : null;
        var catalog = snapshot.Catalog;
        return _shell.DeleteFromLibraryAsync(
            $"{Pack.Name} platforms",
            Pack.Name,
            PackCopier.Touched(Pack, PackCopier.PlatformFiles(Pack, Map)),
            () => PackCopier.RemovePlatforms(Pack, Map, catalog),
            InGameMatch.SetInGame(Pack, Map.FolderName, status) ? [new PartReset(Map, true, [])] : []);
    }

    [RelayCommand]
    private Task ApplyToMapAsync() => _shell.ApplySetAsync(Pack, [Map], clearTicks: false);

    [RelayCommand]
    private Task ApplyToTickedAsync() => _shell.ApplySetAsync(Pack, _shell.SelectedMaps, clearTicks: true);

    [RelayCommand]
    private void Edit() => _edit();

    [RelayCommand]
    private void ShowFiles() => _showFiles?.Invoke();

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Path.Combine(Pack.FullPath, Map.FolderName);
        if (ExplorerLauncher.Open(path) is { } error)
        {
            _shell.Dialogs.Error("Could not open the folder", error);
        }
    }
}
