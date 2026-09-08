namespace BhMaps.Core.Model;

public enum FolderState
{
    Empty,
    Applied,
    Unmanaged,
}

public sealed record FolderStatus(string FolderName, FolderState State, IReadOnlyList<string> PackNames)
{
    public string Text => State switch
    {
        FolderState.Empty => "Reset, launch game to regenerate",
        FolderState.Applied => string.Join(", ", PackNames),
        _ => "Default or unmanaged",
    };
}

public sealed record FileStatus(string FolderName, string FileName, IReadOnlyList<string> PackNames)
{
    public string Text => PackNames.Count == 0 ? "Default or unmanaged" : string.Join(", ", PackNames);
}

public sealed record StatusReport(
    IReadOnlyDictionary<string, FolderStatus> Folders,
    IReadOnlyDictionary<string, FileStatus> Files)
{
    public static StatusReport Empty { get; } = new(
        new Dictionary<string, FolderStatus>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, FileStatus>(StringComparer.OrdinalIgnoreCase));

    public static string Key(string folderName, string fileName) => folderName + "\\" + fileName;

    public FolderStatus ForFolder(string folderName) =>
        Folders.TryGetValue(folderName, out var status)
            ? status
            : new FolderStatus(folderName, FolderState.Empty, Array.Empty<string>());

    public FileStatus ForFile(string folderName, string fileName) =>
        Files.TryGetValue(Key(folderName, fileName), out var status)
            ? status
            : new FileStatus(folderName, fileName, Array.Empty<string>());
}
