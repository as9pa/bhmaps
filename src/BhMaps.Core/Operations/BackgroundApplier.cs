using System.Windows;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Writes one image into a map's background slots. The source file is never modified.</summary>
public static class BackgroundApplier
{
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>Writes the source into the game's Backgrounds folder under every slot name,
    /// fitting with Cover to 2048x1151 only when the source size differs.
    /// The fit runs once and is reused for every slot.</summary>
    public static ApplyResult Apply(string sourcePath, string gamePath, IReadOnlyList<string> slots,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (slots.Count == 0)
        {
            return new ApplyResult(0, []);
        }

        byte[] bytes;
        try
        {
            bytes = ReadFitted(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            // An unusable source is one failure for the source, not an aborted batch.
            return new ApplyResult(0, [new FileFailure(sourcePath, ex.Message)]);
        }

        var targetDir = Path.Combine(gamePath, BackgroundsFolder);
        var copied = 0;
        var failures = new List<FileFailure>();
        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{BackgroundsFolder}\\{slot}");
            WriteOne(bytes, targetDir, slot, ref copied, failures);
        }

        return new ApplyResult(copied, failures);
    }

    /// <summary>Relative paths this apply would write, for the undo snapshot.</summary>
    public static IReadOnlyList<string> TargetPaths(IReadOnlyList<string> slots) =>
        slots.Select(slot => Path.Combine(BackgroundsFolder, slot)).ToList();

    /// <summary>The bytes every slot gets: the source untouched when it already measures 2048x1151, otherwise one Cover fit.</summary>
    private static byte[] ReadFitted(string sourcePath)
    {
        var source = BackgroundFitter.LoadSource(sourcePath);
        return source.PixelWidth == BackgroundFitter.OutputWidth && source.PixelHeight == BackgroundFitter.OutputHeight
            ? File.ReadAllBytes(sourcePath)
            : BackgroundFitter.Fit(sourcePath, new FitOptions(FitMode.Cover));
    }

    private static void WriteOne(byte[] bytes, string targetDir, string slot, ref int copied, List<FileFailure> failures)
    {
        var target = Path.Combine(targetDir, slot);
        try
        {
            Directory.CreateDirectory(targetDir);
            File.WriteAllBytes(target, bytes);
            copied++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(target, ex.Message));
        }
    }
}
