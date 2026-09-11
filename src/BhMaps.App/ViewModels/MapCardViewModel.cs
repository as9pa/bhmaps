using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One card on the Maps grid (spec 7.2): the composed preview, the map's name and its state tag.</summary>
public partial class MapCardViewModel : ObservableObject
{
    /// <summary>The longest a custom picture's name is drawn at (spec 3.1).</summary>
    public const int TagMaxLength = 16;

    public MapCardViewModel(MapEntry map, MapStatus? status, IReadOnlyList<CustomPicture> customPictures)
    {
        Map = map;
        FolderName = map.FolderName;
        DisplayName = map.DisplayName;

        // Missing is the only coloured state (D8), which is exactly when the card wears the coloured tag.
        IsMissing = status?.State == MapState.Missing;
        TagText = status?.State switch
        {
            MapState.Missing => "Missing",
            MapState.Packs => status.Text,
            MapState.Custom => Ellipsise(CustomName(map, customPictures)),
            _ => "",
        };
        ToolTipText = TagText.Length == 0 ? DisplayName : $"{DisplayName} ({TagText})";
    }

    public string FolderName { get; }

    /// <summary>The map's in-game name, or its folder name in the no-level-data fallback (spec 3.6).</summary>
    public string DisplayName { get; }

    public bool IsMissing { get; }

    /// <summary>Spec 3.1's tag, drawn only when it says which art is on the map: the pack names, the custom
    /// picture's name ellipsised at <see cref="TagMaxLength"/>, or "Missing". Default draws no tag, and
    /// neither does a map the scan produced no status for.</summary>
    public string TagText { get; }

    /// <summary>The name and the tag in one line, for the two tightest zoom steps where the card draws neither
    /// (spec 3.1).</summary>
    public string ToolTipText { get; }

    public bool ShowTag => TagText.Length > 0;

    /// <summary>The catalog entry behind the card, so a page can filter on its sets without a second lookup.</summary>
    public MapEntry Map { get; }

    /// <summary>Null until the preview is ready. Always frozen, because it is decoded off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    /// <summary>Spec 3.3: whether this map is in the ticked set. The grid's ListBoxItem binds its own IsSelected
    /// to it two ways, so a click, a Ctrl click, a shift range, Space and the tick box all say the same thing.
    /// MapsViewModel watches it and tells the shell.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Composes the card preview, falling back to the v1 single-file thumbnail without level data or when
    /// the composite could not be drawn (spec 3.6). Called on the UI thread; every file touch happens off it.</summary>
    public async Task LoadPreviewAsync(AppServices services, bool hasLevelData, CancellationToken ct)
    {
        var image = hasLevelData ? await ComposedAsync(services, ct) : null;

        // A preview is never worth an error dialog, so a composite that could not be drawn becomes the file tile.
        image ??= await ThumbnailAsync(services, ct);
        if (image is not null && !ct.IsCancellationRequested)
        {
            Preview = image;
        }
    }

    private async Task<ImageSource?> ComposedAsync(AppServices services, CancellationToken ct)
    {
        var level = Map.BaseLevel;
        var sources = new AssetSources(services.GamePath);
        try
        {
            // The whole call goes on the pool: GetOrRenderAsync hashes every input file on its caller's thread
            // before it reaches the render queue, and the decode after it is as much file work again.
            return await Task.Run(
                async () =>
                {
                    var path = await services.Previews
                        .GetOrRenderAsync(level, MapCompositor.CardWidth, MapCompositor.CardHeight, sources, ct)
                        .ConfigureAwait(false);
                    return Load(path);
                },
                ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            // A file that vanished between the scan and the render, or a preview that could not be written.
            return null;
        }
    }

    private async Task<ImageSource?> ThumbnailAsync(AppServices services, CancellationToken ct)
    {
        var directory = Path.Combine(services.GamePath, FolderName);
        try
        {
            var folder = await Task.Run(() => ImageFiles.ScanFolder(directory), ct);
            if (ThumbnailProvider.PickRepresentative(folder) is not { } file)
            {
                return null;
            }

            return await services.Thumbnails.GetAsync(file.FullPath, file.MtimeTicks, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Spec 3.1: the picture's own name when the library knows it, the slot's file name when it does not.
    /// Read the way MapPanelViewModel.CustomPictureName reads it, so a card and its panel can never name the same
    /// picture differently (plan decision A-D5).</summary>
    private static string CustomName(MapEntry map, IReadOnlyList<CustomPicture> customPictures)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return "Custom";
        }

        var fileName = Path.GetFileName(AssetPath.Background(map.BackgroundSlots[0]));
        return customPictures
                   .FirstOrDefault(p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                   ?.DisplayName
               ?? fileName;
    }

    private static string Ellipsise(string name) =>
        name.Length <= TagMaxLength ? name : name[..(TagMaxLength - 1)] + "\u2026";

    /// <summary>Decoded whole and frozen off the UI thread, so nothing is read from disk while the card draws.</summary>
    private static BitmapImage Load(string path)
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
