using BhMaps.Core.Imaging;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>The platform sets on offer for one map folder, and the apply that puts one of them in the game.</summary>
public static class PlatformSetApplier
{
    /// <summary>The Default pack first, then every other pack with at least one file for that folder.
    /// A pack with no files for the folder is not listed at all (spec section 2, Platforms).</summary>
    public static IReadOnlyList<Pack> SetsFor(string folderName, IReadOnlyList<Pack> packs) =>
        packs
            .Where(p => p.FindFolder(folderName) is { Files.Count: > 0 })
            .OrderBy(p => p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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
    /// so the UI can mark them "changes nothing". Decoding a pack's worth of PNGs is slow, so the token is
    /// checked before each one rather than once at the start.</summary>
    public static IReadOnlyList<string> TransparentFiles(Pack pack, CancellationToken ct = default)
    {
        var transparent = new List<string>();
        foreach (var folder in pack.Folders)
        {
            foreach (var file in folder.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (Path.GetExtension(file.Name).Equals(".png", StringComparison.OrdinalIgnoreCase)
                    && TransparentPng.IsFullyTransparent(file.FullPath))
                {
                    transparent.Add(Path.Combine(folder.Name, file.Name));
                }
            }
        }

        return transparent;
    }
}
