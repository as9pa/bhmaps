using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One custom picture, which belongs to no map: its Apply is always a choice (spec 4), so the hover
/// button opens the menu. Remove from library deletes every copy of it in the packs and is the one destructive
/// action on this page, so it confirms and names the count.</summary>
public sealed partial class CustomPictureTileViewModel : PictureTileViewModel
{
    private readonly CustomPicture _picture;

    public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle)
        : base(
            shell,
            picture.DisplayName,
            subtitle,
            // Empty only for the picture SourcePath calls impossible, and then every action on the tile reports a
            // file that is not there rather than throwing on a path nobody could resolve.
            SourcePath(picture, shell.Services.GamePath) ?? "",
            picture.PackName,
            picture.InGameSlots.Count > 0)
    {
        _picture = picture;
    }

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

    public override string ApplyText => "Apply to...";

    public override bool ShowChevron => true;

    /// <summary>False for a picture that is only in the game folder, which is offered Save to library instead of
    /// Remove from library.</summary>
    private bool InLibrary => _picture.LibraryPaths.Count > 0;

    public override void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand>();
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(TickedText(tickedCount), ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Apply to all maps", ApplyToAllCommand));
        items.Add(new TileMenuCommand("Edit", EditCommand));
        items.Add(new TileMenuCommand("Show in folder", ShowInFolderCommand));
        items.Add(InLibrary
            ? new TileMenuCommand("Remove from library", RemoveFromLibraryCommand)
            : new TileMenuCommand("Save to library", SaveToLibraryCommand));
        MenuItems = items;
    }

    [RelayCommand]
    private Task ApplyToTickedAsync() => Shell.ApplyPictureAsync(FullPath, Shell.SelectedMaps, clearTicks: true);

    [RelayCommand]
    private Task ApplyToAllAsync() =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(FullPath, snapshot.Catalog.Maps, clearTicks: false)
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, null));

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
        var count = paths.Count == 1 ? "1 copy" : $"{paths.Count} copies";
        if (!Shell.Dialogs.Confirm(
                "Remove from library",
                $"Remove {Title} from the library?\n\nThe {count} in your packs are deleted. Nothing in the game folder changes."))
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
            Shell.SetLibraryDone($"Removed {Title} from the library");
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
