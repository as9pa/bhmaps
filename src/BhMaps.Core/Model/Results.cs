namespace BhMaps.Core.Model;

/// <summary>One file that could not be processed. Path is the file the operation was trying to write or delete.</summary>
public sealed record FileFailure(string Path, string Error);

public sealed record ApplyResult(int Copied, IReadOnlyList<FileFailure> Failures)
{
    public int Failed => Failures.Count;
}

public sealed record ResetResult(int Deleted, IReadOnlyList<FileFailure> Failures)
{
    public int Failed => Failures.Count;
}
