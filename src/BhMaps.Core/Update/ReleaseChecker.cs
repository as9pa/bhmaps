using System.Globalization;
using System.Text.Json;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.1: the releases/latest JSON, and nothing else. No HTTP here, so the parse is testable on saved
/// strings and no test of it ever reaches the network.</summary>
public static class ReleaseChecker
{
    public const string ChecksumsName = "SHA256SUMS.txt";

    /// <summary>The self-contained exe publish.ps1 writes and the release carries, for the version given.</summary>
    public static string ExeName(Version version) =>
        $"bhmaps-v{version.Major}.{version.Minor}.{version.Build}-win-x64.exe";

    /// <summary>Null for a draft, a prerelease, a tag that is not vX.Y.Z, or anything that is not this JSON. A
    /// release missing the exe asset still parses: the UI falls back to the release page rather than to nothing.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object
            || Bool(root, "draft")
            || Bool(root, "prerelease")
            || ParseTag(Str(root, "tag_name")) is not { } version)
        {
            return null;
        }

        var exeName = ExeName(version);
        string? exeUrl = null;
        string? checksumsUrl = null;
        long exeSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Str(asset, "name");
                if (name.Equals(exeName, StringComparison.OrdinalIgnoreCase))
                {
                    exeUrl = Str(asset, "browser_download_url");
                    exeSize = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes)
                        ? bytes
                        : 0;
                }
                else if (name.Equals(ChecksumsName, StringComparison.OrdinalIgnoreCase))
                {
                    checksumsUrl = Str(asset, "browser_download_url");
                }
            }
        }

        return new ReleaseInfo(
            version,
            Str(root, "tag_name"),
            Time(root, "published_at"),
            Str(root, "html_url"),
            exeUrl is { Length: > 0 } ? exeUrl : null,
            exeSize,
            checksumsUrl is { Length: > 0 } ? checksumsUrl : null,
            Str(root, "body"));
    }

    /// <summary>Three-part compare. The running assembly version carries a fourth part that is always 0, so it is
    /// dropped before the compare rather than making every equal release look older.</summary>
    public static bool IsNewer(ReleaseInfo release, Version current)
    {
        var running = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return release.Version > running;
    }

    /// <summary>vX.Y.Z only. Anything else, including a bare X.Y.Z or a four-part tag, is not a release this app
    /// knows how to compare itself against.</summary>
    private static Version? ParseTag(string tag)
    {
        if (tag.Length < 6 || (tag[0] != 'v' && tag[0] != 'V'))
        {
            return null;
        }

        var parts = tag[1..].Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset Time(JsonElement element, string name) =>
        DateTimeOffset.TryParse(
            Str(element, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
            ? stamp
            : default;
}
