using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What every picture tile has in common (spec 4): the picture itself, the two lines under it, the tick
/// that says the game is showing it, and the menu the hover button opens. What the tile can do is the subclass's,
/// because a picture aimed at one map has a one-click Apply and a picture aimed at none does not.</summary>
public abstract partial class PictureTileViewModel : ObservableObject
{
    protected PictureTileViewModel(
        MainViewModel shell, string title, string subtitle, string fullPath, string? packName, bool inGame)
    {
        Shell = shell;
        Title = title;
        Subtitle = subtitle;
        FullPath = fullPath;
        PackName = packName;
        IsInGame = inGame;
        MenuItems = [];
    }

    protected MainViewModel Shell { get; }

    /// <summary>The first line under the picture: what the picture is called.</summary>
    public string Title { get; }

    /// <summary>The second line: whatever the page that built the tile has to say about it, which is the pack it
    /// came from or the slot it fills. Empty when there is nothing to say.</summary>
    public string Subtitle { get; }

    public string FullPath { get; }

    /// <summary>The pack holding the picture, or null for a file that is in no pack.</summary>
    public string? PackName { get; }

    /// <summary>True when this picture is the one the game is showing for what the tile is about.</summary>
    public bool IsInGame { get; }

    /// <summary>The words on the hover button. A picture with no single map has no one-click Apply.</summary>
    public virtual string ApplyText => "Apply";

    /// <summary>True when the hover button opens the menu instead of applying (spec 4).</summary>
    public virtual bool ShowChevron => false;

    /// <summary>What the hover button runs when it applies rather than opening the menu. Null when there is
    /// nothing one click could mean.</summary>
    public virtual ICommand? ApplyCommand => null;

    /// <summary>Addendum B: a strip thumbnail's tooltip is its full caption, and the file name under it when the
    /// picture came from a pack, because two packs can caption the same slot with different files.</summary>
    public string ToolTipText =>
        PackName is null ? Title : $"{Title}\n{Path.GetFileName(FullPath)}";

    /// <summary>Null until the thumbnail is ready. Always frozen, because it is decoded off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    /// <summary>The rows of the tile's menu, as the last <see cref="RebuildMenu" /> left them.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }

    /// <summary>Rebuilt whenever the ticked count changes, because the ticked line names it and is dropped at zero.</summary>
    public abstract void RebuildMenu(int tickedCount);

    /// <summary>The ticked row's words, in one place, so no two tiles can word the same row differently.</summary>
    protected static string TickedText(int tickedCount) =>
        tickedCount == 1 ? "Apply to the 1 selected map" : $"Apply to the {tickedCount} selected maps";

    /// <summary>Every file touch is off the UI thread. A picture that cannot be read leaves the tile blank.</summary>
    public async Task LoadThumbnailAsync(AppServices services, CancellationToken ct)
    {
        var path = FullPath;
        try
        {
            var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(path).Ticks, ct);
            if (await services.Thumbnails.GetAsync(path, mtime, ct) is { } image && !ct.IsCancellationRequested)
            {
                Thumbnail = image;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A file that vanished between the scan and the decode. A tile is never worth an error dialog.
        }
    }

    /// <summary>The rows pages' load (addendum B): one decode per file across every row, through the shell's
    /// shared cache rather than a decode of this tile's own.</summary>
    public async Task LoadThumbnailAsync(ThumbnailCache cache, CancellationToken ct)
    {
        if (await cache.GetAsync(FullPath, ct) is { } image && !ct.IsCancellationRequested)
        {
            Thumbnail = image;
        }
    }
}

/// <summary>A picture aimed at one map: a pack's copy of that map's background slot, or a custom picture the
/// panel is offering for it. Used by the map panel and by the Backgrounds page's pack sections.</summary>
public sealed partial class MapPictureTileViewModel : PictureTileViewModel
{
    public MapPictureTileViewModel(
        MainViewModel shell, MapEntry map, string slot, string title, string subtitle,
        string fullPath, string? packName, bool inGame)
        : base(shell, title, subtitle, fullPath, packName, inGame)
    {
        Map = map;
        Slot = slot;
    }

    /// <summary>The map the tile is offering the picture for.</summary>
    public MapEntry Map { get; }

    /// <summary>The background slot this tile is about. The apply still writes every slot the map names (BD6).</summary>
    public string Slot { get; }

    public override ICommand? ApplyCommand => ApplyToMapCommand;

    public override void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand> { new($"Apply to {Map.DisplayName}", ApplyToMapCommand) };
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(TickedText(tickedCount), ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Apply to all maps", ApplyToAllCommand));
        items.Add(new TileMenuCommand("Edit", EditCommand));
        items.Add(new TileMenuCommand("Show in folder", ShowInFolderCommand));
        MenuItems = items;
    }

    // Title, not the file name: it is the caption the tile shows, so the done line names what was clicked.
    [RelayCommand]
    private Task ApplyToMapAsync() => Shell.ApplyPictureAsync(FullPath, [Map], clearTicks: false, Title, PackName);

    [RelayCommand]
    private Task ApplyToTickedAsync() =>
        Shell.ApplyPictureAsync(FullPath, Shell.SelectedMaps, clearTicks: true, Title, PackName);

    [RelayCommand]
    private Task ApplyToAllAsync() =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(FullPath, snapshot.Catalog.Maps, clearTicks: false, Title, PackName)
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() =>
        Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, Slot));

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ExplorerLauncher.Reveal(FullPath) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }
}
