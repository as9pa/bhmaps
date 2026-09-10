using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>The pack that holds the game's own art. Every reset measures against it.</summary>
public static class DefaultPack
{
    public const string Name = "Default";

    public static Pack? Find(IReadOnlyList<Pack> packs) =>
        packs.FirstOrDefault(p => p.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));

    public static bool Exists(string libraryPath) =>
        Directory.Exists(Path.Combine(PackScanner.PacksRoot(libraryPath), Name));

    /// <summary>Deletes packs\Default, then imports the whole game folder into it. The caller confirms first.</summary>
    public static ApplyResult Capture(string gamePath, string libraryPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var packRoot = Path.Combine(PackScanner.PacksRoot(libraryPath), Name);
        if (Directory.Exists(packRoot))
        {
            // Replace rather than merge: a stale folder left behind would read as game art forever.
            var error = PackDeleter.Delete(libraryPath, Name);
            if (error is not null)
            {
                return new ApplyResult(0, [new FileFailure(packRoot, error)]);
            }
        }

        var plan = ImportRouter.Plan(gamePath, GameTreeScanner.Scan(gamePath));
        return ImportRouter.Execute(plan, Name, libraryPath, progress, ct);
    }
}
