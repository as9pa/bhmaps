namespace BhMaps.Core.Update;

/// <summary>One GitHub release, as much of it as the update flow needs (spec 7.1). ExeUrl, ZipUrl and ChecksumsUrl
/// are null when the release does not carry that asset, which is what makes a release undownloadable for one build
/// rather than unusable: the Settings row still offers the release page through Changelog.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    DateTimeOffset PublishedAt,
    string HtmlUrl,
    string? ExeUrl,
    long ExeSize,
    string? ChecksumsUrl,
    string Body,
    string? ZipUrl = null,
    long ZipSize = 0)
{
    /// <summary>The asset the given build updates from: the exe for the self-contained build, the zip for the
    /// framework-dependent one, nothing for a development run.</summary>
    public string? UrlFor(UpdateBuild build) =>
        build switch
        {
            UpdateBuild.SelfContained => ExeUrl,
            UpdateBuild.FrameworkDependent => ZipUrl,
            _ => null,
        };

    public long SizeFor(UpdateBuild build) =>
        build switch
        {
            UpdateBuild.SelfContained => ExeSize,
            UpdateBuild.FrameworkDependent => ZipSize,
            _ => 0,
        };

    /// <summary>Whether the running build can download and verify this release, rather than only open it in a
    /// browser.</summary>
    public bool CanDownload(UpdateBuild build) =>
        UrlFor(build) is { Length: > 0 } && ChecksumsUrl is { Length: > 0 };
}
