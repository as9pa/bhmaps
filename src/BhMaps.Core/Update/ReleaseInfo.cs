namespace BhMaps.Core.Update;

/// <summary>One GitHub release, as much of it as the update flow needs (spec 7.1). ExeUrl and ChecksumsUrl are
/// null when the release does not carry that asset, which is what makes a release undownloadable rather than
/// unusable: the Settings row still offers the release page.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    DateTimeOffset PublishedAt,
    string HtmlUrl,
    string? ExeUrl,
    long ExeSize,
    string? ChecksumsUrl,
    string Body)
{
    /// <summary>Whether this release can be downloaded and verified, rather than only opened in a browser.</summary>
    public bool CanDownload => ExeUrl is { Length: > 0 } && ChecksumsUrl is { Length: > 0 };
}
