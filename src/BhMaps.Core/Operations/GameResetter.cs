using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>Deletes image files from game folders so Brawlhalla regenerates the defaults. Never deletes folders.</summary>
public static class GameResetter
{
    public static ResetResult ResetFolder(string gamePath, string folderName)
    {
        var deleted = 0;
        var failures = new List<FileFailure>();
        foreach (var file in ImageFiles.ListImageFiles(Path.Combine(gamePath, folderName)))
        {
            DeleteOne(file.FullPath, ref deleted, failures);
        }

        return new ResetResult(deleted, failures);
    }

    public static ResetResult ResetFile(string gamePath, string folderName, string fileName)
    {
        var path = Path.Combine(gamePath, folderName, fileName);
        var deleted = 0;
        var failures = new List<FileFailure>();
        if (!ImageFiles.IsImage(fileName))
        {
            failures.Add(new FileFailure(path, "Only .png and .jpg files can be reset."));
        }
        else if (File.Exists(path))
        {
            DeleteOne(path, ref deleted, failures);
        }

        return new ResetResult(deleted, failures);
    }

    public static ResetResult ResetAll(string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var deleted = 0;
        var failures = new List<FileFailure>();
        foreach (var folder in GameTreeScanner.Scan(gamePath).Folders)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(folder.Name);
            var result = ResetFolder(gamePath, folder.Name);
            deleted += result.Deleted;
            failures.AddRange(result.Failures);
        }

        return new ResetResult(deleted, failures);
    }

    private static void DeleteOne(string path, ref int deleted, List<FileFailure> failures)
    {
        try
        {
            File.Delete(path);
            deleted++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(path, ex.Message));
        }
    }
}
