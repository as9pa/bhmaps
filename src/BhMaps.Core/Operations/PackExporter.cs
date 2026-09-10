using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Copies a pack out of the library to a folder the user picks. The library is untouched.</summary>
public static class PackExporter
{
    /// <summary>Copies the pack folder to destinationRoot\&lt;pack name&gt;\&lt;Folder&gt;\&lt;file&gt;.</summary>
    public static ApplyResult Export(Pack pack, string destinationRoot, IProgress<string>? progress = null, CancellationToken ct = default) =>
        PackApplier.ApplyPack(pack, Path.Combine(destinationRoot, pack.Name), progress, ct);
}
