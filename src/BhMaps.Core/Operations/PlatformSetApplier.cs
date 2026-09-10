using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>The platform sets on offer for one map folder, and the apply that puts one of them in the game.</summary>
public static class PlatformSetApplier
{
    /// <summary>The reference pack, listed before the others.</summary>
    private const string DefaultPackName = "Default";

    /// <summary>The Default pack first, then every other pack with at least one file for that folder.
    /// A pack with no files for the folder is not listed at all (spec section 2, Platforms).</summary>
    public static IReadOnlyList<Pack> SetsFor(string folderName, IReadOnlyList<Pack> packs) =>
        packs
            .Where(p => p.FindFolder(folderName) is { Files.Count: > 0 })
            .OrderBy(p => p.Name.Equals(DefaultPackName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();

    /// <summary>Copies every file the pack has for that folder, transparent files included.</summary>
    public static ApplyResult Apply(Pack pack, string folderName, string gamePath, IProgress<string>? progress = null, CancellationToken ct = default) =>
        PackApplier.ApplyFolder(pack, folderName, gamePath, progress, ct);

    /// <summary>Relative paths of the pack's files for that folder, for the undo snapshot.</summary>
    public static IReadOnlyList<string> TargetPaths(Pack pack, string folderName)
    {
        var folder = pack.FindFolder(folderName);
        return folder is null
            ? Array.Empty<string>()
            : folder.Files.Select(f => Path.Combine(folder.Name, f.Name)).ToList();
    }

    /// <summary>Files in the pack that are fully transparent PNGs, as "Folder\file.png",
    /// so the UI can mark them "changes nothing".</summary>
    public static IReadOnlyList<string> TransparentFiles(Pack pack) =>
        pack.Folders
            .SelectMany(folder => folder.Files.Select(file => (Folder: folder, File: file)))
            .Where(x => Path.GetExtension(x.File.Name).Equals(".png", StringComparison.OrdinalIgnoreCase)
                && IsFullyTransparent(x.File.FullPath))
            .Select(x => Path.Combine(x.Folder.Name, x.File.Name))
            .ToList();

    /// <summary>True only for a decodable image whose every pixel has alpha 0. False for anything else,
    /// including unreadable files.</summary>
    private static bool IsFullyTransparent(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var data = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(data, stride, 0);
            for (var i = 3; i < data.Length; i += 4)
            {
                if (data[i] != 0)
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }
}
