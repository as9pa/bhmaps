namespace BhMaps.Core.Maps;

/// <summary>Spec 11: the order the map cards fill their previews in. A write leaves the maps it touched showing
/// the picture from before it until their card reloads, so those cards go to the front of the queue rather than
/// waiting their turn in the alphabet. Pure: it orders names and nothing else.</summary>
public static class LoadOrder
{
    /// <summary>The names in <paramref name="first"/>, in their given order, that occur in <paramref name="all"/>
    /// (ignoring case), followed by the rest of <paramref name="all"/> in its own order. A name in
    /// <paramref name="first"/> that <paramref name="all"/> does not have is dropped, and the casing is always the
    /// one <paramref name="all"/> gave.</summary>
    public static IReadOnlyList<string> Prioritise(IReadOnlyList<string> all, IReadOnlyList<string>? first)
    {
        if (first is not { Count: > 0 })
        {
            return all;
        }

        var lead = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in first)
        {
            // Taken rather than added blind, so a name given twice is placed once.
            if (all.FirstOrDefault(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } match
                && taken.Add(match))
            {
                lead.Add(match);
            }
        }

        return [.. lead, .. all.Where(n => !taken.Contains(n))];
    }
}
