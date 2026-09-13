using BhMaps.Core.Model;
using BhMaps.Core.Packs;

namespace BhMaps.Core.Operations;

/// <summary>Copies a pack out of the library to a folder the user picks. The library is untouched.</summary>
public static class PackExporter
{
    /// <summary>The edit records a pack keeps beside its pictures, copied out with it so the pack stays whole.</summary>
    private static readonly string[] RecordFileNames = [PlatformEditRecord.FileName, BackgroundEditRecord.FileName];

    /// <summary>Copies the pack folder to destinationRoot\&lt;pack name&gt;\&lt;Folder&gt;\&lt;file&gt;, then the pack's
    /// edit records to destinationRoot\&lt;pack name&gt;\ (spec 3.2). A pack with no records exports its pictures alone.</summary>
    public static ApplyResult Export(Pack pack, string destinationRoot, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var destination = Path.Combine(destinationRoot, pack.Name);
        var result = PackApplier.ApplyPack(pack, destination, progress, ct);
        var copied = result.Copied;
        var failures = new List<FileFailure>(result.Failures);
        foreach (var name in RecordFileNames)
        {
            ct.ThrowIfCancellationRequested();
            var source = Path.Combine(pack.FullPath, name);
            if (!File.Exists(source))
            {
                continue;
            }

            var target = Path.Combine(destination, name);
            progress?.Report(name);
            try
            {
                Directory.CreateDirectory(destination);
                File.Copy(source, target, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(target, ex.Message));
            }
        }

        return new ApplyResult(copied, failures);
    }
}
