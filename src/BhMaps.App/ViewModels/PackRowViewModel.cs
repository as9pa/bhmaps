using BhMaps.Core.Model;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the Packs list (spec 7.5): the pack's name and what it holds. Rebuilt from every scan, so
/// nothing on it changes after construction and it needs no change notification.</summary>
public sealed class PackRowViewModel
{
    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    public PackRowViewModel(Pack pack)
    {
        Pack = pack;
        MapCount = pack.Folders.Count(f => !f.Name.Equals(BackgroundsFolder, StringComparison.OrdinalIgnoreCase));
        BackgroundCount = pack.FindFolder(BackgroundsFolder)?.Files.Count ?? 0;
        CountsText = $"{Plural(MapCount, "map")}, {Plural(BackgroundCount, "background")}";
    }

    public Pack Pack { get; }

    public string Name => Pack.Name;

    /// <summary>The pack's folders other than Backgrounds.</summary>
    public int MapCount { get; }

    /// <summary>Files in the pack's Backgrounds folder.</summary>
    public int BackgroundCount { get; }

    /// <summary>The line under the name, as in "1 map, 3 backgrounds".</summary>
    public string CountsText { get; }

    /// <summary>"1 map" but "0 maps" and "3 maps". Shared with the page's confirm text, which counts files.</summary>
    public static string Plural(int count, string noun) => count == 1 ? $"{count} {noun}" : $"{count} {noun}s";
}
