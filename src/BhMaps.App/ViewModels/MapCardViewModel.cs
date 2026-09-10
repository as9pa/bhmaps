using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One card on the Home grid (spec 7.2): the composed preview, the map's name and its state tag.</summary>
public partial class MapCardViewModel : ObservableObject
{
    public MapCardViewModel(MapEntry map, MapStatus? status)
    {
        Map = map;
        FolderName = map.FolderName;
        DisplayName = map.DisplayName;
        StateText = status?.Text ?? "";

        // Missing is the only coloured state (D8), which is exactly when the card wears the coloured tag.
        IsMissing = status?.IsColoured ?? false;
    }

    public string FolderName { get; }

    /// <summary>The map's in-game name, or its folder name in the no-level-data fallback (spec 3.6).</summary>
    public string DisplayName { get; }

    /// <summary>The tag's text: "Default", the pack names, "Custom" or "Missing". Empty when the scan produced
    /// no status for this map, and then no tag is drawn.</summary>
    public string StateText { get; }

    public bool IsMissing { get; }

    /// <summary>The catalog entry behind the card, so a page can filter on its sets without a second lookup.</summary>
    public MapEntry Map { get; }

    /// <summary>Null until the preview is ready. Always frozen, because it is decoded off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

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
