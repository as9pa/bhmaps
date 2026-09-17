using System.Windows.Input;
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One custom picture, which belongs to no map: its Apply is always a choice (spec 4), so the hover
/// button opens the menu. Remove from the pack deletes every copy of it in the packs and is the one destructive
/// action on this page, so it confirms and names the pack.</summary>
public sealed partial class CustomPictureTileViewModel : PictureTileViewModel
{
    private readonly CustomPicture _picture;

    public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle)
        : this(shell, picture, subtitle, null, null, picture.InGameSlots.Count > 0)
    {
    }

    /// <summary>The rows page's and the map panel's form: the same picture, offered for one map's slot, so the
    /// hover button applies in a click and the menu is what the button cannot do.</summary>
    public CustomPictureTileViewModel(
        MainViewModel shell, CustomPicture picture, string subtitle, MapEntry? map, string? slot, bool inGame)
        : base(
            shell,
            Path.GetFileNameWithoutExtension(picture.DisplayName),
            subtitle,
            // Empty only for the picture SourcePath calls impossible, and then every action on the tile reports a
            // file that is not there rather than throwing on a path nobody could resolve.
            SourcePath(picture, shell.Services.GamePath) ?? "",
            picture.PackName,
            inGame)
    {
        _picture = picture;
        Map = map;
        Slot = slot;
    }

    /// <summary>The map this tile offers the picture for, or null on a tile that names no map.</summary>
    public MapEntry? Map { get; }

    /// <summary>The background slot the apply writes, or null with no map. The apply still writes every slot the
    /// map names; this is what the editor opens on.</summary>
    public string? Slot { get; }

    /// <summary>The file a custom picture is: its first copy in the library, or the game's own copy of the first
    /// slot it fills when the library has none. A slot resolves through <see cref="AssetPath" />, because a slot
    /// borrowed from a theme folder through "../" is not under Backgrounds at all. Null when the picture names
    /// neither, which a scan should not produce and which a write refuses rather than guessing at a path. Shared
    /// with the Maps page's Apply picture menu, so a tile and that menu cannot apply different files.</summary>
    public static string? SourcePath(CustomPicture picture, string gamePath) =>
        picture.LibraryPaths.Count > 0
            ? picture.LibraryPaths[0]
            : picture.InGameSlots.Count > 0
                ? Path.Combine(gamePath, AssetPath.Background(picture.InGameSlots[0]))
                : null;

    public override string ApplyText => Map is null ? "Apply to..." : "Apply";

    public override bool ShowChevron => Map is null;

    public override ICommand? ApplyCommand => Map is null ? null : ApplyToMapCommand;

    /// <summary>False for a picture that is only in the game folder, which is offered Save to My Backgrounds
    /// instead of Remove from its pack.</summary>
    private bool InLibrary => _picture.LibraryPaths.Count > 0;

    public override void RebuildMenu(int tickedCount)
    {
        // No "Apply to <map>": a tile that names a map carries the Apply button, and a menu holds only what the
        // tile cannot do on its own (spec 9).
        var items = new List<TileMenuCommand> { TileMenuCommand.Header(Title, MenuDetail()) };
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(TickedText(tickedCount), ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Apply to a map...", ApplyToChosenMapCommand));
        items.Add(new TileMenuCommand("Apply to all maps", ApplyToAllCommand));
        items.Add(new TileMenuCommand("Edit", EditCommand));
        items.Add(new TileMenuCommand("Show in folder", ShowInFolderCommand));
        items.Add(InLibrary
            ? new TileMenuCommand($"Remove from {PackName}", RemoveFromLibraryCommand)
            : new TileMenuCommand("Save to My Backgrounds", SaveToLibraryCommand));
        MenuItems = items;
    }

    /// <summary>The header's second line: the pack the picture lives in, and the map the game is showing it on.
    /// A picture that is only in the game folder is in no pack, so that is the whole line.</summary>
    private string? MenuDetail()
    {
        if (!InLibrary)
        {
            return "In game only";
        }

        var parts = new List<string>();
        if (PackName is { } pack)
        {
            parts.Add(pack);
        }

        if (_picture.InGameSlots.Count > 0 && OnMapsText(_picture.InGameSlots[0]) is { } on)
        {
            parts.Add(on);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    /// <summary>"on Brawlhaven", or "on Brawlhaven and 2 more" when the slot belongs to several maps. Null when
    /// no catalog map names the slot, which is what a scan that has not run yet looks like.</summary>
    private string? OnMapsText(string slotFile)
    {
        if (Shell.Snapshot is not { } snapshot)
        {
            return null;
        }

        var maps = snapshot.Catalog.Maps
            .Where(m => m.BackgroundSlots.Any(slot => Path.GetFileName(AssetPath.Background(slot))
                .Equals(slotFile, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return maps.Count switch
        {
            0 => null,
            1 => $"on {maps[0].DisplayName}",
            _ => $"on {maps[0].DisplayName} and {maps.Count - 1} more",
        };
    }

    // The picture's own name, not the file it happens to be stored as, is what the done line reports (spec 2.2).
    [RelayCommand]
    private Task ApplyToMapAsync() =>
        Map is { } map
            ? Shell.ApplyPictureAsync(FullPath, [map], clearTicks: false, _picture.DisplayName, _picture.PackName)
            : Task.CompletedTask;

    /// <summary>Spec 4.4: the chooser in map mode, then the shell's apply on the one map it returned.</summary>
    [RelayCommand]
    private async Task ApplyToChosenMapAsync()
    {
        if (await Shell.ChooseMapAsync(FullPath, _picture.DisplayName) is { } map)
        {
            await Shell.ApplyPictureAsync(
                FullPath, [map], clearTicks: false, _picture.DisplayName, _picture.PackName);
        }
    }

    [RelayCommand]
    private Task ApplyToTickedAsync() =>
        Shell.ApplyPictureAsync(FullPath, Shell.SelectedMaps, clearTicks: true, _picture.DisplayName, _picture.PackName);

    [RelayCommand]
    private Task ApplyToAllAsync() =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(FullPath, snapshot.Catalog.Maps, clearTicks: false, _picture.DisplayName, _picture.PackName)
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, Slot));

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ExplorerLauncher.Reveal(FullPath) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        var paths = _picture.LibraryPaths;
        if (!Shell.Dialogs.Confirm(
                $"Remove from {PackName}?",
                $"{Title} is removed from {PackName}. The game keeps whatever is applied until you apply something else."))
        {
            return;
        }

        // A library write, so RunBusyAsync and SetLibraryDone, never RunGameWriteAsync. The guard is the reason
        // this is not one File.Delete: a path outside packs\ is refused rather than deleted.
        var packsRoot = PackScanner.PacksRoot(Shell.Services.LibraryPath);
        var failures = new List<FileFailure>();
        var ok = await Shell.RunBusyAsync(
            $"Removing {Title}",
            (_, ct) => Task.Run(
                () =>
                {
                    foreach (var path in paths)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!Path.GetFullPath(path).StartsWith(
                                Path.GetFullPath(packsRoot) + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            failures.Add(new FileFailure(path, "Not a file in the library."));
                            continue;
                        }

                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            failures.Add(new FileFailure(path, ex.Message));
                        }
                    }
                },
                ct));

        Shell.Dialogs.ShowFailures("Some files could not be removed", failures);
        if (ok)
        {
            Shell.SetLibraryDone($"Removed {Title} from {PackName}");
        }

        await Shell.RescanAsync();
    }

    [RelayCommand]
    private async Task SaveToLibraryAsync()
    {
        var source = FullPath;
        var library = Shell.Services.LibraryPath;
        var pack = BackgroundEditorViewModel.DefaultPackName;
        PictureImportResult? result = null;
        var ok = await Shell.RunBusyAsync(
            $"Importing into {pack}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import([source], library, pack, PictureFit.Fill, progress, ct); },
                ct));

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("The picture could not be saved", result.Failures);
        }

        // Only a copy that landed changes what a scan would find, so a cancelled or failed import costs no rescan.
        if (ok && result is { Copied: > 0 })
        {
            Shell.SetLibraryDone($"Saved {Title} into {pack}");
            await Shell.RescanAsync();
        }
    }
}
