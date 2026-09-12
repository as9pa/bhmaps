namespace BhMaps.Core.Maps;

public enum MapFileState
{
    Default,
    Pack,
    Custom,
    Missing,
}

/// <summary>One file of a map, measured against every pack. PackNames is filled only in the Pack state.</summary>
public sealed record MapFileStatus(string RelativePath, MapFileState State, IReadOnlyList<string> PackNames)
{
    /// <summary>"Default", the joined pack names, "In game only" or "Missing".</summary>
    public string Text => State switch
    {
        MapFileState.Default => "Default",
        MapFileState.Pack => string.Join(", ", PackNames),
        MapFileState.Missing => "Missing",
        _ => "In game only",
    };
}

public enum MapState
{
    Default,
    Packs,
    Custom,
    Missing,
}

/// <summary>One map summarised from its files. PackNames holds every pack matched anywhere in the map, in the
/// order the files first named them, whatever the summary state turned out to be.</summary>
public sealed record MapStatus(
    string FolderName, MapState State, IReadOnlyList<string> PackNames, IReadOnlyList<MapFileStatus> Files)
{
    private const int ShownPackNames = 2;

    /// <summary>"Default", "dark", "dark, flowers", "dark, flowers +2", "In game only" or "Missing".</summary>
    public string Text => State switch
    {
        MapState.Default => "Default",
        MapState.Packs => PackNames.Count > ShownPackNames
            ? string.Join(", ", PackNames.Take(ShownPackNames)) + " +" + (PackNames.Count - ShownPackNames)
            : string.Join(", ", PackNames),
        MapState.Missing => "Missing",
        _ => "In game only",
    };

    /// <summary>Only Missing is coloured (spec 6.2).</summary>
    public bool IsColoured => State == MapState.Missing;
}
