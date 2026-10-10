using BhMaps.Core.Hashing;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>One map-select picture a pack carries for one map: the game file name it replaces and the jpg in the
/// pack's images\thumbnails folder.</summary>
public sealed record PackPicture(string MapFolder, string FileName, string SourcePath);

/// <summary>3.7: a pack's own map-select pictures. A pack may hold images\thumbnails\*.jpg at its content root or,
/// when it wraps its art in mapArt, beside that folder. Apply copies the jpg as-is over the game's picture for every
/// map the pack applied art to, in place of the composite BhMaps would render.</summary>
public static class PackPictures
{
    /// <summary>The pack's images\thumbnails folder: under the content root first, then under the pack folder that
    /// wraps a mapArt. Null when the pack has neither.</summary>
    public static string? Dir(Pack pack)
    {
        foreach (var root in new[] { pack.FullPath, pack.FolderPath }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = Path.Combine(root, PackScanner.ImagesFolderName, "thumbnails");
            if (Directory.Exists(dir))
            {
                return dir;
            }
        }

        return null;
    }

    /// <summary>Every jpg in the pack's pictures folder whose name is a map-select picture one of
    /// <paramref name="maps"/> owns, matched by file name in any case. A jpg no map owns is ignored, and so is one
    /// another map's folder also names, on ThumbnailWriter.Plan's Shared rule: writing it would change that map's
    /// picture too. <paramref name="allMaps"/> is the whole catalog the shared check reads.</summary>
    public static IReadOnlyList<PackPicture> Match(Pack pack, IReadOnlyList<MapEntry> maps, IReadOnlyList<MapEntry> allMaps)
    {
        if (Dir(pack) is not { } dir)
        {
            return [];
        }

        string[] jpgs;
        try
        {
            jpgs = Directory.GetFiles(dir, "*.jpg");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var pictures = new List<PackPicture>();
        foreach (var path in jpgs.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            var map = maps.FirstOrDefault(m => m.ThumbnailFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase));
            if (map is null)
            {
                continue;
            }

            var shared = allMaps.Any(o =>
                !o.FolderName.Equals(map.FolderName, StringComparison.OrdinalIgnoreCase)
                && o.Candidates.Contains(fileName, StringComparer.OrdinalIgnoreCase));
            if (!shared)
            {
                pictures.Add(new PackPicture(map.FolderName, fileName, path));
            }
        }

        return pictures;
    }

    /// <summary>The pictures an apply of the pack to <paramref name="maps"/> copies: only for the maps it applies
    /// art to (PackApplier.MapsTouched), so a picture of a map the pack has no art for never reaches the game.</summary>
    public static IReadOnlyList<PackPicture> ForApply(Pack pack, IReadOnlyList<MapEntry> maps, IReadOnlyList<MapEntry> allMaps) =>
        Match(pack, PackApplier.MapsTouched(pack, maps), allMaps);

    /// <summary>Import from pack: the source pack's pictures of this map copied into the target pack's own
    /// images\thumbnails (the one it has, else beside its mapArt, else at its root). Returns the failures.</summary>
    public static IReadOnlyList<FileFailure> CopyIntoPack(Pack source, Pack target, MapEntry map, IReadOnlyList<MapEntry> allMaps)
    {
        var failures = new List<FileFailure>();
        var pictures = Match(source, [map], allMaps);
        if (pictures.Count == 0)
        {
            return failures;
        }

        var dir = Dir(target) ?? Path.Combine(target.FolderPath, PackScanner.ImagesFolderName, "thumbnails");
        foreach (var picture in pictures)
        {
            var to = Path.Combine(dir, picture.FileName);
            try
            {
                Directory.CreateDirectory(dir);
                File.Copy(picture.SourcePath, to, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(to, ex.Message));
            }
        }

        return failures;
    }

    /// <summary>Copies the pack's jpg over the game's picture as-is, after keeping the game's own copy the way a
    /// rendered write does, and notes it in the thumbnails record so later saves leave it alone.</summary>
    public static void Copy(PackPicture picture, ThumbnailTarget target, string packName, string recordPath)
    {
        ThumbnailWriter.KeepOriginal(target);
        File.Copy(picture.SourcePath, target.TargetPath, overwrite: true);
        ThumbnailRecord.Note(recordPath, picture.MapFolder, packName, target.FileName, FileHasher.Hash(target.TargetPath));
    }
}
