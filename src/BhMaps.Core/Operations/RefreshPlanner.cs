using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Why one file is being written again: its source changed on disk since the app applied it, or the
/// game folder lost a file the Default pack has.</summary>
public enum RefreshReason
{
    SourceChanged,
    Missing,
}

/// <summary>One file the refresh writes. Fit is true when the source has to go through the background fitter
/// rather than a raw copy, which is how it landed the first time.</summary>
public sealed record RefreshCopy(
    string GameRelativePath, string SourceFullPath, string? PackName, RefreshReason Reason, bool Fit);

/// <summary>One recorded file the refresh leaves alone, and the reason it did.</summary>
public sealed record RefreshSkip(string GameRelativePath, string Why);

/// <summary>What a refresh would do. Folders names the map folders the copies land in, for the rescan after.</summary>
public sealed record RefreshPlan(
    IReadOnlyList<RefreshCopy> Copies, IReadOnlyList<RefreshSkip> Skipped, IReadOnlyList<string> Folders);

/// <summary>Works out what it takes to make the game match what the app says is on: every file the app wrote
/// whose source has been edited since, and every file a map folder has lost. Reads nothing but hashes, writes
/// nothing at all, so the caller can decide there is nothing to do before it takes an undo snapshot.</summary>
public static class RefreshPlanner
{
    /// <summary><paramref name="hashOf"/> gives the SHA-256 of a file by full path, or null when it is not
    /// there. A file the game no longer has is not a refresh: undo and reset both leave the game without it on
    /// purpose, so only a file that is still there and still holds the bytes the app wrote is written again.</summary>
    public static RefreshPlan Plan(
        string gamePath,
        string libraryPath,
        GameTree tree,
        IReadOnlyList<Pack> packs,
        Pack? defaultPack,
        IReadOnlyDictionary<string, MapStatus> statuses,
        AppliedRecord record,
        Func<string, string?> hashOf)
    {
        var copies = new Dictionary<string, RefreshCopy>(StringComparer.OrdinalIgnoreCase);
        var skipped = new List<RefreshSkip>();

        foreach (var (gameRelativePath, entry) in record.Entries)
        {
            var copy = PlanRecorded(gamePath, libraryPath, tree, gameRelativePath, entry, hashOf, skipped);
            if (copy is not null)
            {
                copies[gameRelativePath] = copy;
            }
        }

        if (defaultPack is not null)
        {
            foreach (var status in statuses.Values)
            {
                foreach (var file in status.Files.Where(f => f.State == MapFileState.Missing))
                {
                    var copy = PlanMissing(packs, defaultPack, status, file.RelativePath);
                    if (copy is not null && !copies.ContainsKey(file.RelativePath))
                    {
                        copies[file.RelativePath] = copy;
                    }
                }
            }
        }

        var folders = copies.Values
            .Select(c => FirstSegment(c.GameRelativePath))
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RefreshPlan([.. copies.Values], skipped, folders);
    }

    /// <summary>One entry of the applied record. Null when there is nothing to write, whether because the game
    /// file has gone, because the source has not moved, or because the file is one of the skips.</summary>
    private static RefreshCopy? PlanRecorded(
        string gamePath,
        string libraryPath,
        GameTree tree,
        string gameRelativePath,
        AppliedEntry entry,
        Func<string, string?> hashOf,
        List<RefreshSkip> skipped)
    {
        var folderName = FirstSegment(gameRelativePath);
        if (folderName.Length == 0
            || tree.FindFolder(folderName)?.FindFile(Path.GetFileName(gameRelativePath)) is null)
        {
            // Nothing there to refresh, and putting it back is not what a refresh is for.
            return null;
        }

        var gameHash = hashOf(Path.Combine(gamePath, gameRelativePath));
        if (gameHash is null)
        {
            return null;
        }

        if (!gameHash.Equals(entry.Hash, StringComparison.OrdinalIgnoreCase))
        {
            // Someone wrote over it since, so what the record remembers is no longer what is on.
            skipped.Add(new RefreshSkip(gameRelativePath, "changed since"));
            return null;
        }

        var source = Path.IsPathRooted(entry.Source) ? entry.Source : Path.Combine(libraryPath, entry.Source);
        var sourceHash = hashOf(source);
        if (sourceHash is null)
        {
            skipped.Add(new RefreshSkip(gameRelativePath, "source missing"));
            return null;
        }

        // A record written before source hashes were kept compares against the bytes that landed, which is right
        // for every raw copy and wrong only for a fitted picture, where the worst of it is one write too many.
        if (sourceHash.Equals(entry.SourceHash ?? entry.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new RefreshCopy(
            gameRelativePath, source, entry.Pack, RefreshReason.SourceChanged, NeedsFit(source, gameRelativePath));
    }

    /// <summary>One file a map folder has lost. It comes from the pack the rest of the folder matches when that
    /// pack has it, and from Default otherwise; a file no pack has is not restored at all.</summary>
    private static RefreshCopy? PlanMissing(
        IReadOnlyList<Pack> packs, Pack defaultPack, MapStatus status, string gameRelativePath)
    {
        var folderName = FirstSegment(gameRelativePath);
        var fileName = Path.GetFileName(gameRelativePath);

        // Two packs in one folder say nothing about which one the lost file came from, so Default answers.
        var matched = status.PackNames.Count == 1
            ? packs.FirstOrDefault(p => p.Name.Equals(status.PackNames[0], StringComparison.OrdinalIgnoreCase))
            : null;

        var matchedFile = matched?.FindFolder(folderName)?.FindFile(fileName);
        var source = matchedFile ?? defaultPack.FindFolder(folderName)?.FindFile(fileName);
        if (source is null)
        {
            return null;
        }

        var packName = matchedFile is null ? defaultPack.Name : matched!.Name;
        return new RefreshCopy(gameRelativePath, source.FullPath, packName, RefreshReason.Missing, Fit: false);
    }

    /// <summary>A pack mirrors the game layout, so its files sit at the game-relative path inside the pack. A
    /// source that does not is a picture the app fitted into a slot, and it has to be fitted again rather than
    /// copied in at whatever size it is.</summary>
    private static bool NeedsFit(string sourceFullPath, string gameRelativePath)
    {
        if (!sourceFullPath.EndsWith(gameRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "My Backgrounds\BG_Grove.jpg" ends with "Backgrounds\BG_Grove.jpg" while mirroring nothing, so the tail
        // only counts as the game path when it starts where a folder does.
        var before = sourceFullPath.Length - gameRelativePath.Length - 1;
        return before >= 0 && sourceFullPath[before] is not ('\\' or '/');
    }

    /// <summary>The map folder a game-relative path lands in, or "" when it names no folder.</summary>
    private static string FirstSegment(string gameRelativePath)
    {
        var separator = gameRelativePath.IndexOfAny(['\\', '/']);
        return separator < 0 ? "" : gameRelativePath[..separator];
    }
}
