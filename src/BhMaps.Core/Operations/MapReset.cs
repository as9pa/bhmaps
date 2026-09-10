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
        if (defaultPack is null)
        {
            var reset = GameResetter.ResetFolder(gamePath, folderName);
            return new ResetOutcome(0, reset.Deleted, reset.Failures);
        }

        var restored = 0;
        var failures = new List<FileFailure>();

        // Anything the pack does not have has no default to restore, so it is skipped rather than reported as a failure.
        if (defaultPack.FindFolder(folderName) is not null)
        {
            var applied = PackApplier.ApplyFolder(defaultPack, folderName, gamePath);
            restored += applied.Copied;
            failures.AddRange(applied.Failures);
        }

        var backgrounds = defaultPack.FindFolder(BackgroundsFolder);
        foreach (var slot in backgroundSlots)
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
}
