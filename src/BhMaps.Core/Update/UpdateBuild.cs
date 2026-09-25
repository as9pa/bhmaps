namespace BhMaps.Core.Update;

/// <summary>Which of the two published builds is running, which decides the release asset the Update button
/// downloads. A development run (dotnet run, or a plain build output) is neither and never swaps itself.</summary>
public enum UpdateBuild
{
    Development,

    /// <summary>bhmaps-vX.Y.Z-win-x64.exe: single file, runtime bundled.</summary>
    SelfContained,

    /// <summary>bhmaps-vX.Y.Z-win-x64-dotnet.zip: one single-file BhMaps.exe that runs on the installed runtime.</summary>
    FrameworkDependent,
}
