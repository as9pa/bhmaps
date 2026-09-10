using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Copies pack files into the game folder, overwriting. Never deletes anything.</summary>
public static class PackApplier
{
    public static ApplyResult ApplyPack(Pack pack, string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var copied = 0;
        var failures = new List<FileFailure>();
        foreach (var folder in pack.Folders)
        {
            CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
        }

        return new ApplyResult(copied, failures);
    }

    public static ApplyResult ApplyFolder(Pack pack, string folderName, string gamePath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var folder = pack.FindFolder(folderName);
        if (folder is null)
        {
            return new ApplyResult(0, [new FileFailure(Path.Combine(gamePath, folderName), $"Pack '{pack.Name}' has no folder named '{folderName}'.")]);
        }

        var copied = 0;
        var failures = new List<FileFailure>();
        CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
        return new ApplyResult(copied, failures);
    }

    public static ApplyResult ApplyFile(Pack pack, string folderName, string fileName, string gamePath)
    {
        var folder = pack.FindFolder(folderName);
        var file = folder?.FindFile(fileName);
        if (folder is null || file is null)
        {
            return new ApplyResult(0, [new FileFailure(Path.Combine(gamePath, folderName, fileName), $"Pack '{pack.Name}' has no file '{folderName}\\{fileName}'.")]);
        }

        var copied = 0;
        var failures = new List<FileFailure>();
        CopyOne(file, Path.Combine(gamePath, folder.Name), ref copied, failures);
        return new ApplyResult(copied, failures);
    }

    private static void CopyFolder(GameFolder folder, string gamePath, IProgress<string>? progress, CancellationToken ct, ref int copied, List<FileFailure> failures)
    {
        var targetDir = Path.Combine(gamePath, folder.Name);
        foreach (var file in folder.Files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{folder.Name}\\{file.Name}");
            CopyOne(file, targetDir, ref copied, failures);
        }
    }

    private static void CopyOne(GameFile file, string targetDir, ref int copied, List<FileFailure> failures)
    {
        var target = Path.Combine(targetDir, file.Name);
        try
        {
            Directory.CreateDirectory(targetDir);
            File.Copy(file.FullPath, target, overwrite: true);
            copied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(target, ex.Message));
        }
    }
}
