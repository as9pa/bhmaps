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

    /// <summary>Each image becomes &lt;library&gt;\packs\&lt;packName&gt;\Backgrounds\&lt;source name&gt;.jpg at 2048x1151. A source that will not decode is a failure, not an abort.</summary>
    public static ApplyResult Import(IReadOnlyList<string> sourcePaths, string libraryPath, string packName,
        PictureFit fit, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!PackNameValidator.IsValid(packName, out var error))
        {
            throw new ArgumentException(error, nameof(packName));
        }

        var options = ToFitOptions(fit);
        var targetDir = Path.Combine(PackScanner.PacksRoot(libraryPath), packName, BackgroundsFolder);
        var copied = 0;
        var failures = new List<FileFailure>();
        foreach (var source in sourcePaths)
        {
            ct.ThrowIfCancellationRequested();
            var name = TargetFileName(source);
            var target = Path.Combine(targetDir, name);
            progress?.Report(Path.Combine(BackgroundsFolder, name));
            try
            {
                var bytes = BackgroundFitter.Fit(source, options);
                Directory.CreateDirectory(targetDir);
                File.WriteAllBytes(target, bytes);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                failures.Add(new FileFailure(target, ex.Message));
            }
        }

        Directory.CreateDirectory(targetDir);
        return new ApplyResult(copied, failures);
    }
}
