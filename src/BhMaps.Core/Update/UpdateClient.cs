using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.1: the two requests the update flow makes, over the one HttpClient the App owns. The client
/// carries the User-Agent and Accept headers as defaults, because only the App knows its own version; the 10 s of
/// the check is applied here rather than on the client, so it never caps the download.</summary>
public sealed class UpdateClient(HttpClient http)
{
    public const string LatestUrl = "https://api.github.com/repos/as9pa/bhmaps/releases/latest";

    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private const int BufferSize = 64 * 1024;

    /// <summary>Null on any failure at all: no connection, a 404, a rate-limit page, junk JSON, a draft, a tag
    /// this version cannot read, or the 10 s running out. Never throws, because the start-up check has nowhere to
    /// report to and must not become a dialog (spec 7.3).</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CheckTimeout);
            using var response = await http.GetAsync(LatestUrl, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ReleaseChecker.Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>Streams the build's asset to &lt;name&gt;.partial, verifies its SHA-256 against the line of
    /// SHA256SUMS.txt that names it, then renames. The self-contained build gets the exe itself; the
    /// framework-dependent build gets the zip, and the one BhMaps.exe inside it is extracted beside it as
    /// bhmaps-dotnet.exe and the zip deleted. Returns the exe the swap moves. A mismatch, no line at all, or a zip with no BhMaps.exe deletes
    /// what was written and throws InvalidDataException; a cancel deletes it and rethrows. The caller decides what
    /// to say about either.</summary>
    public async Task<string> DownloadAsync(
        ReleaseInfo release,
        UpdateBuild build,
        string updatesDir,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        if (release.UrlFor(build) is not { Length: > 0 } assetUrl
            || release.ChecksumsUrl is not { Length: > 0 } sumsUrl)
        {
            throw new InvalidDataException("The release does not carry this build and a checksum file.");
        }

        Directory.CreateDirectory(updatesDir);
        var name = ReleaseChecker.AssetName(build);
        var assetPath = Path.Combine(updatesDir, name);
        var partialPath = assetPath + ".partial";
        var exePath = build == UpdateBuild.FrameworkDependent
            ? Path.Combine(updatesDir, Path.GetFileNameWithoutExtension(name) + ".exe")
            : assetPath;
        TryDelete(partialPath);

        try
        {
            using var response = await http
                .GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.SizeFor(build);

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var target = new FileStream(
                partialPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    progress?.Report((done, total));
                }
            }

            var sums = await http.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            if (ChecksumFor(sums, name) is not { } expected)
            {
                throw new InvalidDataException($"{ReleaseChecker.ChecksumsName} has no line for {name}.");
            }

            string actual;
            await using (var written = File.OpenRead(partialPath))
            {
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(written, ct).ConfigureAwait(false));
            }

            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The downloaded file does not match the checksum in {ReleaseChecker.ChecksumsName}.");
            }

            if (build == UpdateBuild.FrameworkDependent)
            {
                // Only a verified zip is opened, and only its BhMaps.exe comes out of it.
                await ExtractExeAsync(partialPath, exePath + ".partial", ct).ConfigureAwait(false);
                File.Delete(partialPath);
                partialPath = exePath + ".partial";
            }

            File.Move(partialPath, exePath, overwrite: true);
            return exePath;
        }
        catch
        {
            // Nothing half-downloaded and nothing unverified is ever left behind for the swap to pick up.
            TryDelete(assetPath + ".partial");
            TryDelete(exePath + ".partial");
            throw;
        }
    }

    /// <summary>The zip's one BhMaps.exe, wherever in the zip it sits.</summary>
    private static async Task ExtractExeAsync(string zipPath, string target, CancellationToken ct)
    {
        await using var zip = await ZipFile.OpenReadAsync(zipPath, ct).ConfigureAwait(false);
        var entry = zip.Entries.FirstOrDefault(
            e => e.Name.Equals(UpdateInstaller.ExeName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidDataException($"The zip does not contain {UpdateInstaller.ExeName}.");
        }

        await entry.ExtractToFileAsync(target, overwrite: true, ct).ConfigureAwait(false);
    }

    /// <summary>sha256sum's own format: the digest, two spaces (or a space and a star for binary mode), the file
    /// name. Null when no line names the file.</summary>
    public static string? ChecksumFor(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space <= 0)
            {
                continue;
            }

            var named = line[(space + 1)..].TrimStart(' ', '*');
            if (named.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return line[..space].ToLowerInvariant();
            }
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file the app cannot delete is not worth failing a failure over.
        }
    }
}
