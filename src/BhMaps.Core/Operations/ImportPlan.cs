namespace BhMaps.Core.Operations;

public enum Route
{
    Routed,
    Ambiguous,
    Unmatched,
}

/// <summary>One source image and where it will go. Mutated only through ImportPlan.</summary>
public sealed class ImportRow
{
    private ImportRow(string sourcePath, Route route, string? targetFolder, IReadOnlyList<string> candidates, bool include)
    {
        SourcePath = sourcePath;
        Route = route;
        TargetFolder = targetFolder;
        Candidates = candidates;
        Include = include;
    }

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    public Route Route { get; private set; }

    public string? TargetFolder { get; private set; }

    /// <summary>Game folders that contain a file with this name; only filled for Ambiguous rows.</summary>
    public IReadOnlyList<string> Candidates { get; }

    public bool Include { get; internal set; }

    /// <summary>True when another routed row has the same target path.</summary>
    public bool Conflict { get; internal set; }

    /// <summary>"Folder\file" relative to the pack root, or null when not routed.</summary>
    public string? TargetRelativePath => TargetFolder is null ? null : Path.Combine(TargetFolder, FileName);

    internal static ImportRow Routed(string sourcePath, string folder) =>
        new(sourcePath, Route.Routed, folder, Array.Empty<string>(), include: true);

    internal static ImportRow Ambiguous(string sourcePath, IReadOnlyList<string> candidates) =>
        new(sourcePath, Route.Ambiguous, null, candidates, include: false);

    internal static ImportRow Unmatched(string sourcePath) =>
        new(sourcePath, Route.Unmatched, null, Array.Empty<string>(), include: false);

    internal void SetTarget(string folder)
    {
        TargetFolder = folder;
        Route = Route.Routed;
    }
}

/// <summary>The routing table for one source folder. Owns the include/conflict rules.</summary>
public sealed class ImportPlan
{
    internal ImportPlan(string sourcePath, IReadOnlyList<ImportRow> rows)
    {
        SourcePath = sourcePath;
        Rows = rows;
        RefreshConflicts();
        ExcludeLaterDuplicates();
    }

    public string SourcePath { get; }

    public IReadOnlyList<ImportRow> Rows { get; }

    public IEnumerable<ImportRow> IncludedRows => Rows.Where(r => r.Include && r.Route == Route.Routed);

    public int IncludedCount => IncludedRows.Count();

    /// <summary>Including a row excludes every other row that targets the same path. Excluding never touches other rows.</summary>
    public void SetInclude(ImportRow row, bool include)
    {
        if (!include)
        {
            row.Include = false;
            return;
        }

        if (row.Route != Route.Routed)
        {
            throw new InvalidOperationException("Assign a target folder before including this row.");
        }

        foreach (var other in Rows.Where(o => !ReferenceEquals(o, row) && SameTarget(o, row)))
        {
            other.Include = false;
        }

        row.Include = true;
    }

    /// <summary>Routes the row to <paramref name="folderName"/>, recomputes conflicts, and includes it.</summary>
    /// <exception cref="ArgumentException">The name is not usable as one folder under the pack root.</exception>
    public void AssignFolder(ImportRow row, string folderName)
    {
        // The same rules a pack name obeys: one folder name, so the target can never escape the pack root.
        if (!PackNameValidator.IsValid(folderName, out var error))
        {
            throw new ArgumentException(error, nameof(folderName));
        }

        row.SetTarget(folderName);
        RefreshConflicts();
        SetInclude(row, true);
    }

    private void RefreshConflicts()
    {
        foreach (var row in Rows)
        {
            row.Conflict = false;
        }

        var groups = Rows
            .Where(r => r.Route == Route.Routed)
            .GroupBy(r => r.TargetRelativePath!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var row in groups.SelectMany(g => g))
        {
            row.Conflict = true;
        }
    }

    private void ExcludeLaterDuplicates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows.Where(r => r.Route == Route.Routed))
        {
            if (!seen.Add(row.TargetRelativePath!))
            {
                row.Include = false;
            }
        }
    }

    private static bool SameTarget(ImportRow a, ImportRow b) =>
        a.Route == Route.Routed && b.Route == Route.Routed &&
        string.Equals(a.TargetRelativePath, b.TargetRelativePath, StringComparison.OrdinalIgnoreCase);
}
