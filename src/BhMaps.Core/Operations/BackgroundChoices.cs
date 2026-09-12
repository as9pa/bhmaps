using BhMaps.Core.LevelData;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One pack's picture for one background slot: the pack it came from and the file itself.</summary>
public sealed record BackgroundChoice(Pack Pack, GameFile File);

/// <summary>Which packs can fill one map's background slot. The map panel (spec 3.2) and the Backgrounds rows
/// page (addendum B) both ask this, so the rule lives in Core and neither of them carries a copy of it.</summary>
public static class BackgroundChoices
{
    /// <summary>The folder a pack keeps its background pictures in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>Every pack holding a picture under the slot's own file name: the Default pack first, then the
    /// rest in the order they were given, which is the order the Packs page lists them in. A slot borrowed from
    /// a theme folder through "../" is still one file in the pack's Backgrounds folder, so the name is resolved
    /// through <see cref="AssetPath" /> rather than taken as typed.</summary>
    public static IReadOnlyList<BackgroundChoice> For(string slot, IReadOnlyList<Pack> packs)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var choices = new List<BackgroundChoice>();
        foreach (var pack in packs)
        {
            if (pack.FindFolder(BackgroundsFolder)?.FindFile(fileName) is { } file)
            {
                choices.Add(new BackgroundChoice(pack, file));
            }
        }

        // OrderBy is stable, so everything that is not Default keeps the order it arrived in.
        return choices
            .OrderBy(c => c.Pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }
}
