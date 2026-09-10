using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>The pack that holds the game's own art. Every reset measures against it.</summary>
public static class DefaultPack
{
    public const string Name = "Default";

    /// <summary>Where a capture is built before it becomes the Default pack. A sibling of it, so the swap at the
    /// end is a rename on the same volume rather than a second copy.</summary>
    private const string CapturingName = Name + ".capturing";

    public static Pack? Find(IReadOnlyList<Pack> packs) =>
        packs.FirstOrDefault(p => p.Name.Equals(Name, StringComparison.OrdinalIgnoreCase));

    public static bool Exists(string libraryPath) =>
        Directory.Exists(Path.Combine(PackScanner.PacksRoot(libraryPath), Name));

    /// <summary>Imports the whole game folder into packs\Default.capturing and, only once that import has run to
    /// the end, replaces packs\Default with it. The caller confirms first. A capture that is cancelled or fails
    /// takes only its own temporary folder with it, so the Default pack that was there is still the one on disk.</summary>
    public static ApplyResult Capture(string gamePath, string libraryPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var packsRoot = PackScanner.PacksRoot(libraryPath);
        var packRoot = Path.Combine(packsRoot, Name);
        var capturingRoot = Path.Combine(packsRoot, CapturingName);

        // A temporary folder an earlier run left behind holds half a capture, so it is thrown away rather than
        // merged into.
        if (Directory.Exists(capturingRoot) && PackDeleter.Delete(libraryPath, CapturingName) is { } staleError)
        {
            return new ApplyResult(0, [new FileFailure(capturingRoot, staleError)]);
        }

        var plan = ImportRouter.Plan(gamePath, GameTreeScanner.Scan(gamePath));
        ApplyResult result;
        try
        {
            result = ImportRouter.Execute(plan, CapturingName, libraryPath, progress, ct);
        }
        catch
        {
            // Cancellation is rethrown for the busy boundary to read as a cancel; anything else is still the
            // caller's to report. Either way the half-copy goes and the old pack is untouched.
            PackDeleter.Delete(libraryPath, CapturingName);
            throw;
        }

        // Replace rather than merge: a stale folder left behind would read as game art forever. Deleted only
        // now, with the whole capture already on disk beside it.
        if (Directory.Exists(packRoot) && PackDeleter.Delete(libraryPath, Name) is { } deleteError)
        {
            PackDeleter.Delete(libraryPath, CapturingName);
            return new ApplyResult(0, [new FileFailure(packRoot, deleteError)]);
        }

        try
        {
            Directory.Move(capturingRoot, packRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old pack is already gone and the capture is the only copy left, so it stays where it is for
            // the next capture to find rather than being deleted with the failure.
            return new ApplyResult(0, [new FileFailure(packRoot, ex.Message)]);
        }

        return result;
    }
}
