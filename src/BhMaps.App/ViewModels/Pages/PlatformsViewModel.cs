using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Addendum C: the same rows, for the other half of a map's look. A platform set fits only its own map,
/// so a row lists exactly the packs that have a set for that map, each composed over the map's current
/// background and cropped to the platforms, because at row height a whole level shows them as slivers.</summary>
public partial class PlatformsViewModel : RowsPageViewModel
{
    /// <summary>The composite behind a set tile, rendered at twice the largest zoom step (128 px tall, 228 wide),
    /// so a thumbnail is still sharp at step 5 and on a high DPI screen. Exactly 16:9, which is the shape
    /// PlatformBounds crops to, so the render fills the tile with no letterbox.</summary>
    public const int SetWidth = 448;
    public const int SetHeight = 252;

    private const double Aspect = 16d / 9d;

    public PlatformsViewModel(MainViewModel shell)
        : base(shell, shell.Services.Settings.PlatformsZoom)
    {
    }

    public override string Title => "Platforms";

    public override string SearchPlaceholder => "Search maps and packs";

    protected override string NoResultsText => $"No map or pack matches '{SearchText}'.";

    protected override void SaveZoom(int value)
    {
        if (Shell.Services.Settings.PlatformsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { PlatformsZoom = value });
        }
    }

    /// <summary>Addendum C's order: the in-game set first with the check, then Default, then one tile per pack
    /// that has a set for this map. Nothing folds away: a map has as many sets as it has packs, and the "+N" tile
    /// is what handles a row too narrow for all of them.</summary>
    protected override MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot)
    {
        var map = card.Map;

        // Show files belongs to the map panel, which is where the file list lives; on a row the menu line would
        // have nothing to open, so the tile is handed null and its menu leaves the line out (spec 9).
        var sets = MapChoices.Platforms(Shell, map, status, snapshot, SetWidth, SetHeight, showFiles: null);
        List<object> alwaysShown = [.. sets.Where(t => t.InGame), .. sets.Where(t => !t.InGame)];
        var (tag, missing) = Tag(map, status);

        return new MapRowViewModel(
            map,
            tag,
            missing,
            Haystack(map, sets),
            alwaysShown,
            [],
            [],
            sets,
            ComposeSetAsync);
    }

    /// <summary>Addendum C's state tag, measured over this map's own folder only: Missing beats the game's own
    /// art beats the first pack that matched, and Default draws no tag at all.</summary>
    private static (string Tag, bool Missing) Tag(MapEntry map, MapStatus? status)
    {
        var prefix = map.FolderName + Path.DirectorySeparatorChar;
        var files = (status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Missing))
        {
            return ("Missing", true);
        }

        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return ("In game only", false);
        }

        return (files.SelectMany(f => f.PackNames).FirstOrDefault() ?? "", false);
    }

    /// <summary>Addendum C: the search matches map and pack names.</summary>
    private static string Haystack(MapEntry map, IReadOnlyList<PlatformSetTileViewModel> sets) =>
        string.Join('\n', [map.DisplayName, .. sets.Select(t => t.PackName)]);

    /// <summary>The pack's platform art over the map's current background, cropped to the platforms, through the
    /// preview cache and then the shared decode cache. Never throws: a file that vanished between the scan and
    /// the render leaves the tile grey, which is what every other preview path in the app does.</summary>
    private async Task ComposeSetAsync(PlatformSetTileViewModel tile, CancellationToken ct)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var map = tile.Map;
        var gamePath = Shell.Services.GamePath;
        var background = map.BaseLevel.Backgrounds.Count == 0
            ? null
            : Path.Combine(gamePath, AssetPath.Background(map.BaseLevel.Backgrounds[0].AssetName));
        var sources = new AssetSources(gamePath, tile.Pack.FullPath, background);
        var viewport = PlatformBounds.For(map.BaseLevel, PlatformBounds.Pad, Aspect);
        var previews = Shell.Services.Previews;
        var thumbnails = Shell.Services.RowThumbnails;

        ImageSource? image = null;
        if (snapshot.Catalog.HasLevelData)
        {
            try
            {
                // The whole call goes on the pool: GetOrRenderAsync hashes every input file on its caller's
                // thread before it reaches the render queue.
                image = await Task.Run(
                    async () =>
                    {
                        var path = await previews
                            .GetOrRenderAsync(map.BaseLevel, SetWidth, SetHeight, sources, ct, viewport)
                            .ConfigureAwait(false);
                        return await thumbnails.GetAsync(path, ct).ConfigureAwait(false);
                    },
                    ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or FileFormatException or ArgumentException)
            {
                image = null;
            }
        }

        // The v1 single-file tile, which stands in for a composite that could not be drawn and for the whole
        // no-level-data fallback (spec 3.6).
        if (image is null
            && tile.Pack.FindFolder(map.FolderName) is { } folder
            && ThumbnailProvider.PickRepresentative(folder) is { } file)
        {
            image = await thumbnails.GetAsync(file.FullPath, ct);
        }

        if (image is not null && !ct.IsCancellationRequested)
        {
            tile.Preview = image;
        }
    }
}
