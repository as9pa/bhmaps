using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

public enum PictureFit
{
    Stretch,
    Center,
    Fill,
    Fit,
}

/// <summary>What one import call did. Written holds the file names that landed in the pack, in source order and
/// with the suffix each one actually took, so a caller that goes on to use those pictures does not have to guess
/// which name a collision was given. A source that failed contributes a failure and no name.</summary>
public sealed record PictureImportResult(IReadOnlyList<string> Written, IReadOnlyList<FileFailure> Failures)
{
    public int Copied => Written.Count;

    public int Failed => Failures.Count;
}

/// <summary>Turns loose pictures into pack backgrounds. Never moves or deletes a source; overwrites same-named files.</summary>
public static class PictureImporter
{
    /// <summary>The folder every background lives in, inside the pack and inside the game tree.</summary>
    public const string BackgroundsFolder = "Backgrounds";

    /// <summary>Stretch to Stretch; Center to Contain with NoUpscale; Fill to Cover; Fit to Contain (decision D9).</summary>
    public static FitOptions ToFitOptions(PictureFit fit) => fit switch
    {
        PictureFit.Stretch => new FitOptions(FitMode.Stretch),
        PictureFit.Center => new FitOptions(FitMode.Contain, NoUpscale: true),
        PictureFit.Fill => new FitOptions(FitMode.Cover),
        _ => new FitOptions(FitMode.Contain),
    };

    /// <summary>The name a source image will take in the pack: its file name with a .jpg extension.</summary>
    public static string TargetFileName(string sourcePath) => Path.ChangeExtension(Path.GetFileName(sourcePath), ".jpg");

    /// <summary>The background file names a pack already holds, which is what the names of an import have to
    /// dodge. A pack that does not exist yet holds nothing.</summary>
    public static IReadOnlyList<string> ExistingNames(string libraryPath, string packName)
    {
        var folder = Path.Combine(PackScanner.PacksRoot(libraryPath), packName, BackgroundsFolder);
        try
        {
            return [.. Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Each image becomes &lt;library&gt;\packs\&lt;packName&gt;\Backgrounds\&lt;source name&gt;.jpg at 2048x1151. A source that will not decode is a failure, not an abort.
    /// <paramref name="names"/> gives the file name for each source in order, for a caller that has already settled
    /// what the pictures are called (3.0 C); without it each source keeps its own name.</summary>
    public static PictureImportResult Import(IReadOnlyList<string> sourcePaths, string libraryPath, string packName,
        PictureFit fit, IProgress<string>? progress = null, CancellationToken ct = default,
        IReadOnlyList<string>? names = null)
    {
        if (!PackNameValidator.IsValid(packName, out var error))
        {
            throw new ArgumentException(error, nameof(packName));
        }

        var options = ToFitOptions(fit);
        var targetDir = Path.Combine(PackScanner.PacksRoot(libraryPath), packName, BackgroundsFolder);
        var written = new List<string>();
        var failures = new List<FileFailure>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sourcePaths.Count; i++)
        {
            var source = sourcePaths[i];
            ct.ThrowIfCancellationRequested();
            var name = FreeName(
                names is not null && i < names.Count ? names[i] : TargetFileName(source), taken);
            var target = Path.Combine(targetDir, name);
            progress?.Report(Path.Combine(BackgroundsFolder, name));
            try
            {
                var bytes = BackgroundFitter.Fit(source, options);
                Directory.CreateDirectory(targetDir);
                File.WriteAllBytes(target, bytes);
                taken.Add(name);
                written.Add(name);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                failures.Add(new FileFailure(target, ex.Message));
            }
        }

        Directory.CreateDirectory(targetDir);
        return new PictureImportResult(written, failures);
    }

    /// <summary>Keeps every picture in one call: a name this call already wrote becomes "photo (2).jpg", then "photo (3).jpg". A file left by an earlier import is still overwritten.</summary>
    private static string FreeName(string name, HashSet<string> taken)
    {
        if (!taken.Contains(name))
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var next = 2;
        string candidate;
        do
        {
            candidate = $"{stem} ({next}){extension}";
            next++;
        }
        while (taken.Contains(candidate));

        return candidate;
    }
}
