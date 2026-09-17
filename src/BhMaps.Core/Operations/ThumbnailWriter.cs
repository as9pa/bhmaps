using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Operations;

/// <summary>Why one of a map's map-select pictures is left alone. None means it can be written.</summary>
public enum ThumbnailSkip
{
    None,
    Shared,
    Missing,
}

/// <summary>Where a map's thumbnail lives, where its original is kept, and the level it is the picture of: a
/// folder's files are one per level, so each is rendered from its own level and not from the map.</summary>
public sealed record ThumbnailTarget(string FileName, string TargetPath, string OriginalPath, LevelDesc Level);

/// <summary>One picture a map's levels name: a target when the map owns it and the game's jpg is there,
/// otherwise the reason it is left alone, and for Shared the display name of the map that also names it.</summary>
public sealed record ThumbnailFilePlan(string FileName, ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap);

/// <summary>Spec section 8: one entry per picture the map's included levels name, in level order. A map whose
/// levels name none plans no entries at all, which is <see cref="NamesNoFile"/>.</summary>
public sealed record ThumbnailPlan(IReadOnlyList<ThumbnailFilePlan> Files)
{
    /// <summary>Every file of the plan that can be written, in level order.</summary>
    public IReadOnlyList<ThumbnailTarget> Targets { get; } =
        [.. Files.Select(f => f.Target).OfType<ThumbnailTarget>()];

    /// <summary>True when the map's levels name no picture at all, so there was never anything to write.</summary>
    public bool NamesNoFile => Files.Count == 0;
}

/// <summary>Spec sections 8 and 10.3: the map-select thumbnails of one map, rendered from the game folder and
/// written over the game's own jpgs. The game's picture is kept first, under the app's data folder, so every
/// write can be undone.</summary>
public static class ThumbnailWriter
{
    public const int Width = 290;

    public const int Height = 164;

    public const int RenderWidth = 580;

    public const int RenderHeight = 328;

    public const int JpegQuality = 88;

    public const string OriginalsFolderName = "thumbnails-original";

    /// <summary>gameRoot\images\thumbnails.</summary>
    public static string ThumbnailsDir(string gameRoot) => Path.Combine(gameRoot, "images", "thumbnails");

    /// <summary>appDataDir\thumbnails-original.</summary>
    public static string OriginalsDir(string appDataDir) => Path.Combine(appDataDir, OriginalsFolderName);

    /// <summary>One plan entry per picture the map's levels name: a target when the map owns the file and the
    /// jpg is in the game's thumbnails folder, Shared when another map's folder also names it (OtherMap is that
    /// map's display name), Missing when the jpg is not there. A map that owns two files and is missing one
    /// plans the one that exists and reports the other.</summary>
    public static ThumbnailPlan Plan(MapEntry map, IReadOnlyList<MapEntry> allMaps, string gameRoot, string appDataDir)
    {
        var thumbnailsDir = ThumbnailsDir(gameRoot);
        var originalsDir = OriginalsDir(appDataDir);
        var owned = map.ThumbnailFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = new List<ThumbnailFilePlan>();

        foreach (var fileName in map.Candidates)
        {
            if (!owned.Contains(fileName))
            {
                // A file two folders name belongs to neither: writing it would change the other map's thumbnail.
                var other = allMaps.FirstOrDefault(o =>
                    o != map && o.Candidates.Contains(fileName, StringComparer.OrdinalIgnoreCase));
                files.Add(new ThumbnailFilePlan(fileName, null, ThumbnailSkip.Shared, other?.DisplayName));
                continue;
            }

            var targetPath = Path.Combine(thumbnailsDir, fileName);
            files.Add(File.Exists(targetPath)
                ? new ThumbnailFilePlan(
                    fileName,
                    new ThumbnailTarget(
                        fileName,
                        targetPath,
                        Path.Combine(originalsDir, fileName),
                        map.LevelFor(fileName)),
                    ThumbnailSkip.None,
                    null)
                : new ThumbnailFilePlan(fileName, null, ThumbnailSkip.Missing, null));
        }

        return new ThumbnailPlan(files);
    }

    /// <summary>Renders one level from the game folder at RenderWidth by RenderHeight and returns the frozen
    /// composite. Runs on any thread (MapCompositor.Render is thread free once the sources are read).</summary>
    public static BitmapSource Render(LevelDesc level, string gamePath)
    {
        var composite = MapCompositor.Render(level, RenderWidth, RenderHeight, new AssetSources(gamePath));
        if (composite.CanFreeze)
        {
            composite.Freeze();
        }

        return composite;
    }

    /// <summary>Scales the composite to Width by Height and writes JPEG at JpegQuality through AtomicFile.</summary>
    public static void Write(BitmapSource composite, string targetPath)
    {
        var scale = new ScaleTransform(Width / (double)composite.PixelWidth, Height / (double)composite.PixelHeight);
        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(new TransformedBitmap(composite, scale)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        AtomicFile.WriteAllBytes(targetPath, stream.ToArray());
    }

    /// <summary>Copies the jpg to originalPath only when no copy exists yet. Creates the folder.</summary>
    public static void KeepOriginal(ThumbnailTarget target)
    {
        if (File.Exists(target.OriginalPath))
        {
            // The kept copy is the game's own picture; a second keep would store a thumbnail we wrote ourselves.
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target.OriginalPath)!);
        File.Copy(target.TargetPath, target.OriginalPath, overwrite: false);
    }

    /// <summary>Copies the kept original back over the target when one exists. Returns true when it did.</summary>
    public static bool RestoreOriginal(ThumbnailTarget target)
    {
        if (!File.Exists(target.OriginalPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target.TargetPath)!);
        File.Copy(target.OriginalPath, target.TargetPath, overwrite: true);
        return true;
    }

    /// <summary>Every kept original copied back into thumbnailsDir, the copies deleted. Returns the file names
    /// written, so the caller can name them as undo paths first.</summary>
    public static IReadOnlyList<string> RestoreAll(string originalsDir, string thumbnailsDir)
    {
        var written = new List<string>();
        foreach (var fileName in KeptOriginals(originalsDir))
        {
            Directory.CreateDirectory(thumbnailsDir);
            var original = Path.Combine(originalsDir, fileName);
            File.Copy(original, Path.Combine(thumbnailsDir, fileName), overwrite: true);
            File.Delete(original);
            written.Add(fileName);
        }

        return written;
    }

    /// <summary>The file names RestoreAll would write, without writing: for the undo capture before it.</summary>
    public static IReadOnlyList<string> KeptOriginals(string originalsDir)
    {
        if (!Directory.Exists(originalsDir))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(originalsDir)
            .Select(f => Path.GetFileName(f))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
