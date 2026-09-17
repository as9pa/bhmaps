using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>What one reset changed. Restored counts files copied back from the Default pack, Deleted counts files removed for the game to regenerate.</summary>
public sealed record ResetOutcome(int Restored, int Deleted, IReadOnlyList<FileFailure> Failures)
{
    public int Failed => Failures.Count;

    public int Changed => Restored + Deleted;
}

/// <summary>Resets game art to default. The Default pack is the reference; delete-and-regenerate is the fallback when there is none.</summary>
public static class MapReset
{
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>With a Default pack: copy its files for this folder plus its copies of the map's background slots. Without one: delete the folder's png and jpg files so the game regenerates them (v1 behaviour).</summary>
    public static ResetOutcome ResetMap(string gamePath, string folderName, IReadOnlyList<string> backgroundSlots, Pack? defaultPack)
    {
        var platforms = ResetPlatforms(gamePath, folderName, defaultPack);
        if (defaultPack is null)
        {
            // Without a pack there is nothing to restore, only files to delete, and a whole-map reset has always
            // deleted the map folder's images alone. The slots are shared art, so taking them out is its own
            // decision, which is what ResetBackgrounds is for.
            return platforms;
        }

        return Combine(platforms, ResetBackgrounds(gamePath, backgroundSlots, defaultPack));
    }

    /// <summary>The platforms half of ResetMap: the Default pack's files for that map folder, or, with no pack,
    /// the folder's png and jpg files deleted for the game to regenerate.</summary>
    public static ResetOutcome ResetPlatforms(string gamePath, string folderName, Pack? defaultPack)
    {
        if (defaultPack is null)
        {
            var reset = GameResetter.ResetFolder(gamePath, folderName);
            return new ResetOutcome(0, reset.Deleted, reset.Failures);
        }

        // A folder the pack does not have has no default to restore, so it is skipped rather than reported as a failure.
        if (defaultPack.FindFolder(folderName) is null)
        {
            return new ResetOutcome(0, 0, []);
        }

        var applied = PackApplier.ApplyFolder(defaultPack, folderName, gamePath);
        return new ResetOutcome(applied.Copied, 0, applied.Failures);
    }

    /// <summary>The backgrounds half of ResetMap: the Default pack's copy of each slot, or, with no pack, each
    /// slot's jpg deleted for the game to regenerate. A slot the pack has not got is skipped, on the same rule.</summary>
    public static ResetOutcome ResetBackgrounds(string gamePath, IReadOnlyList<string> slots, Pack? defaultPack)
    {
        if (defaultPack is null)
        {
            var deleted = 0;
            var gone = new List<FileFailure>();
            foreach (var slot in slots)
            {
                var reset = GameResetter.ResetFile(gamePath, BackgroundsFolder, slot);
                deleted += reset.Deleted;
                gone.AddRange(reset.Failures);
            }

            return new ResetOutcome(0, deleted, gone);
        }

        var restored = 0;
        var failures = new List<FileFailure>();
        var backgrounds = defaultPack.FindFolder(BackgroundsFolder);
        foreach (var slot in slots)
        {
            if (backgrounds?.FindFile(slot) is null)
            {
                continue;
            }

            var applied = PackApplier.ApplyFile(defaultPack, BackgroundsFolder, slot, gamePath);
            restored += applied.Copied;
            failures.AddRange(applied.Failures);
        }

        return new ResetOutcome(restored, 0, failures);
    }

    /// <summary>Every folder in the scanned game tree, including the theme folders the catalog hides.</summary>
    public static ResetOutcome ResetAll(string gamePath, Pack? defaultPack, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (defaultPack is null)
        {
            var reset = GameResetter.ResetAll(gamePath, progress, ct);
            return new ResetOutcome(0, reset.Deleted, reset.Failures);
        }

        var applied = PackApplier.ApplyPack(defaultPack, gamePath, progress, ct);
        return new ResetOutcome(applied.Copied, 0, applied.Failures);
    }

    /// <summary>Two halves of one reset as a single outcome, the first half's failures first.</summary>
    private static ResetOutcome Combine(ResetOutcome first, ResetOutcome second) =>
        new(first.Restored + second.Restored, first.Deleted + second.Deleted, [.. first.Failures, .. second.Failures]);
}
