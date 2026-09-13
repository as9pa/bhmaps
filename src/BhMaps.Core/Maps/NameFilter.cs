namespace BhMaps.Core.Maps;

/// <summary>The one rule a name-typed search uses, so the Maps page and the chooser (spec 4.2) cannot filter the
/// same list differently. An empty or blank search matches everything, which is what an empty box means.</summary>
public static class NameFilter
{
    public static bool Matches(string name, string search) =>
        string.IsNullOrWhiteSpace(search)
        || name.Contains(search, StringComparison.OrdinalIgnoreCase);
}
