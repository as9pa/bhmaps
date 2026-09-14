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

    /// <summary>Streams the exe to &lt;name&gt;.partial, verifies its SHA-256 against the line of SHA256SUMS.txt that
    /// names it, then renames. A mismatch, or no line at all, deletes the file and throws InvalidDataException; a
    /// cancel deletes it and rethrows. The caller decides what to say about either.</summary>
    public async Task<string> DownloadAsync(
        ReleaseInfo release,
        string updatesDir,
        IProgress<(long Done, long Total)>? progress,
        CancellationToken ct)
    {
        if (release.ExeUrl is not { Length: > 0 } exeUrl || release.ChecksumsUrl is not { Length: > 0 } sumsUrl)
        {
            throw new InvalidDataException("The release does not carry a Windows exe and a checksum file.");
        }

        Directory.CreateDirectory(updatesDir);
        var name = ReleaseChecker.ExeName(release.Version);
        var finalPath = Path.Combine(updatesDir, name);
        var partialPath = finalPath + ".partial";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        try
        {
            using var response = await http
                .GetAsync(exeUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.ExeSize;

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

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(partialPath, finalPath);
            return finalPath;
        }
        catch
        {
            // Nothing half-downloaded and nothing unverified is ever left behind for the swap to pick up.
            TryDelete(partialPath);
            throw;
        }
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
