using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One tile on the Backgrounds grid (spec 7.3): the picture, its file name and the pack it came from,
/// and the tick that says a map ticked in the sidebar is showing it.</summary>
public partial class BackgroundTileViewModel : ObservableObject
{
    public BackgroundTileViewModel(LibraryBackground background)
    {
        PackName = background.PackName;
        FileName = background.FileName;
        FullPath = background.FullPath;
        FromGame = background.FromGame;
    }

    /// <summary>The pack holding the picture, or BackgroundLibrary.GameSourceName for the game's own copy.</summary>
    public string PackName { get; }

    /// <summary>The file name, which for the game's own copy is also the slot name.</summary>
    public string FileName { get; }

    public string FullPath { get; }

    public bool FromGame { get; }

    /// <summary>What the picture is, as against what it is called: the content hash the scan's cache holds for it,
    /// filled off the UI thread after every scan. Null until then, and for a file that has gone since; a tile with
    /// no hash never ticks.</summary>
    public string? Hash { get; set; }

    /// <summary>Null until the thumbnail is ready. Always frozen, because it is decoded off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    /// <summary>True while a map ticked in the sidebar shows this picture. False whenever nothing is ticked.</summary>
    [ObservableProperty]
    public partial bool IsInUse { get; set; }

    /// <summary>Called on the UI thread; every file touch happens off it. A picture that cannot be read leaves the
    /// tile blank rather than raising a dialog.</summary>
    public async Task LoadThumbnailAsync(AppServices services, CancellationToken ct)
    {
        var path = FullPath;
        try
        {
            // The thumbnail cache is keyed on the mtime, so reading it is file work like the decode behind it.
            var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(path).Ticks, ct);
            var image = await services.Thumbnails.GetAsync(path, mtime, ct);
            if (image is not null && !ct.IsCancellationRequested)
            {
                Thumbnail = image;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A file that vanished between the scan and the decode. A tile is never worth an error dialog.
        }
    }
}
