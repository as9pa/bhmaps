using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>Spec 8: the order every list of packs is shown in. The shell sorts once after each scan and every
/// list downstream keeps the order it is given.</summary>
public static class PackOrder
{
    /// <summary>Default first, then the packs with a last-applied stamp, newest first, then the packs never
    /// applied by name. The pack used last is the one the user is working with, so it sits nearest the top.</summary>
    public static IReadOnlyList<Pack> Sort(IReadOnlyList<Pack> packs, IReadOnlyDictionary<string, DateTimeOffset> lastApplied)
    {
        // A pack name is a folder name, so the stamp a settings file holds may be spelled any way at all.
        var stamps = new Dictionary<string, DateTimeOffset>(lastApplied, StringComparer.OrdinalIgnoreCase);

        return packs
            .OrderBy(p => p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => stamps.ContainsKey(p.Name) ? 0 : 1)
            .ThenByDescending(p => stamps.TryGetValue(p.Name, out var stamp) ? stamp : DateTimeOffset.MinValue)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
