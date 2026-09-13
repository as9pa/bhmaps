using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Storage;

namespace BhMaps.Core.Operations;

/// <summary>Why a map's map-select thumbnail is left alone. None means it can be written.</summary>
public enum ThumbnailSkip
{
    None,
    Shared,
    NoFile,
    Missing,
}

/// <summary>Where a map's thumbnail lives and where its original is kept.</summary>
public sealed record ThumbnailTarget(string FileName, string TargetPath, string OriginalPath);

/// <summary>Target when the map's thumbnail can be written; otherwise the reason, and for Shared the other map's
/// display name.</summary>
public sealed record ThumbnailPlan(ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap);

/// <summary>Spec 10.3: the map-select thumbnail of one map, rendered from the game folder and written over the
/// game's own jpg. The game's picture is kept first, under the app's data folder, so every write can be undone.</summary>
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

    /// <summary>Shared when another map's included levels name the same file (OtherMap is that map's DisplayName);
    /// NoFile when the map names none; Missing when the jpg is not in the thumbnails folder.</summary>
    public static ThumbnailPlan Plan(MapEntry map, IReadOnlyList<MapEntry> allMaps, string gameRoot, string appDataDir)
    {
        if (map.ThumbnailFile is not { } fileName)
        {
            // A file two maps name belongs to neither: writing it would change the other map's thumbnail too.
            var other = allMaps.FirstOrDefault(o =>
                o != map && o.Candidates.Intersect(map.Candidates, StringComparer.OrdinalIgnoreCase).Any());
            return other is null
                ? new ThumbnailPlan(null, ThumbnailSkip.NoFile, null)
                : new ThumbnailPlan(null, ThumbnailSkip.Shared, other.DisplayName);
        }

        var targetPath = Path.Combine(ThumbnailsDir(gameRoot), fileName);
        if (!File.Exists(targetPath))
        {
            return new ThumbnailPlan(null, ThumbnailSkip.Missing, null);
        }

        var target = new ThumbnailTarget(fileName, targetPath, Path.Combine(OriginalsDir(appDataDir), fileName));
        return new ThumbnailPlan(target, ThumbnailSkip.None, null);
    }

    /// <summary>Renders the map from the game folder at RenderWidth by RenderHeight and returns the frozen
    /// composite. Runs on any thread (MapCompositor.Render is thread free once the sources are read).</summary>
    public static BitmapSource Render(MapEntry map, string gamePath)
    {
        var composite = MapCompositor.Render(map.BaseLevel, RenderWidth, RenderHeight, new AssetSources(gamePath));
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
