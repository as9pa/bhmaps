using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BhMaps.Core.Tests.Helpers;
using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class UpdateClientTests
{
    private const string ExeUrl =
        "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64.exe";

    private const string SumsUrl =
        "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/SHA256SUMS.txt";

    private static readonly byte[] Exe = Encoding.ASCII.GetBytes("MZ this stands in for a 135 MB exe");

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Sums(string exeHex) =>
        $"{exeHex}  bhmaps-v2.6.0-win-x64.exe\n"
        + "0000000000000000000000000000000000000000000000000000000000000000  bhmaps-v2.6.0-win-x64-dotnet.zip\n";

    private const string ZipUrl =
        "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64-dotnet.zip";

    private static string ZipSums(string zipHex) =>
        "0000000000000000000000000000000000000000000000000000000000000000  bhmaps-v2.6.0-win-x64.exe\n"
        + $"{zipHex}  bhmaps-v2.6.0-win-x64-dotnet.zip\n";

    private static byte[] Zip(params (string Name, byte[] Body)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(body);
            }
        }

        return memory.ToArray();
    }

    private static ReleaseInfo Release() => ReleaseChecker.Parse(UpdateSamples.LatestJson)!;

    [Fact]
    public async Task CheckAsync_ReturnsTheParsedRelease()
    {
        var fake = new FakeHttp().Text(UpdateClient.LatestUrl, UpdateSamples.LatestJson);
        using var http = new HttpClient(fake);

        var release = await new UpdateClient(http).CheckAsync(CancellationToken.None);

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 6, 0), release!.Version);
        Assert.Equal([UpdateClient.LatestUrl], fake.Requested);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullOnA404()
    {
        using var http = new HttpClient(new FakeHttp());

        Assert.Null(await new UpdateClient(http).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullWhenTheRequestThrows()
    {
        using var http = new HttpClient(new FakeHttp().Throws(UpdateClient.LatestUrl));

        Assert.Null(await new UpdateClient(http).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullOnATimeoutRatherThanThrowing()
    {
        using var http = new HttpClient(new FakeHttp().Hangs(UpdateClient.LatestUrl));
        using var cts = new CancellationTokenSource();

        var checking = new UpdateClient(http).CheckAsync(cts.Token);
        await cts.CancelAsync();

        Assert.Null(await checking);
    }

    [Fact]
    public async Task CheckAsync_ReturnsNullForJunkJson()
    {
        using var http = new HttpClient(new FakeHttp().Text(UpdateClient.LatestUrl, "<html>rate limited</html>"));

        Assert.Null(await new UpdateClient(http).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DownloadAsync_VerifiesTheChecksumAndRenamesThePartialFile()
    {
        using var tmp = new TempDir();
        var fake = new FakeHttp().Bytes(ExeUrl, Exe).Text(SumsUrl, Sums(Hex(Exe)));
        using var http = new HttpClient(fake);
        var seen = new List<(long Done, long Total)>();

        var path = await new UpdateClient(http).DownloadAsync(
            Release(), UpdateBuild.SelfContained, tmp.Path, new Progress<(long, long)>(seen.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(tmp.Path, "bhmaps-v2.6.0-win-x64.exe"), path);
        Assert.Equal(Exe, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.partial"));
        Assert.Contains(SumsUrl, fake.Requested);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsAndDeletesTheFileOnAMismatch()
    {
        using var tmp = new TempDir();
        var wrong = Hex(Encoding.ASCII.GetBytes("a different build"));
        using var http = new HttpClient(new FakeHttp().Bytes(ExeUrl, Exe).Text(SumsUrl, Sums(wrong)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(Release(), UpdateBuild.SelfContained, tmp.Path, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_ThrowsWhenNoLineNamesTheExe()
    {
        using var tmp = new TempDir();
        var sums = "0000000000000000000000000000000000000000000000000000000000000000  something-else.zip\n";
        using var http = new HttpClient(new FakeHttp().Bytes(ExeUrl, Exe).Text(SumsUrl, sums));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(Release(), UpdateBuild.SelfContained, tmp.Path, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_LeavesNoPartialFileWhenCancelled()
    {
        using var tmp = new TempDir();
        using var http = new HttpClient(new FakeHttp().Bytes(ExeUrl, Exe).Text(SumsUrl, Sums(Hex(Exe))));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new UpdateClient(http).DownloadAsync(Release(), UpdateBuild.SelfContained, tmp.Path, null, cts.Token));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_VerifiesTheZipAndExtractsTheExeForTheFrameworkDependentBuild()
    {
        using var tmp = new TempDir();
        var zip = Zip(("BhMaps.exe", Exe), ("readme.txt", Encoding.ASCII.GetBytes("hi")));
        var fake = new FakeHttp().Bytes(ZipUrl, zip).Text(SumsUrl, ZipSums(Hex(zip)));
        using var http = new HttpClient(fake);

        var path = await new UpdateClient(http).DownloadAsync(
            Release(), UpdateBuild.FrameworkDependent, tmp.Path, null, CancellationToken.None);

        Assert.Equal(Path.Combine(tmp.Path, "bhmaps-v2.6.0-win-x64-dotnet.exe"), path);
        Assert.Equal(Exe, await File.ReadAllBytesAsync(path));
        Assert.Equal(["bhmaps-v2.6.0-win-x64-dotnet.exe"], Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
        Assert.DoesNotContain(ExeUrl, fake.Requested);
    }

    [Fact]
    public async Task DownloadAsync_ThrowsWhenTheZipHasNoExe()
    {
        using var tmp = new TempDir();
        var zip = Zip(("readme.txt", Encoding.ASCII.GetBytes("hi")));
        using var http = new HttpClient(new FakeHttp().Bytes(ZipUrl, zip).Text(SumsUrl, ZipSums(Hex(zip))));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(
                Release(), UpdateBuild.FrameworkDependent, tmp.Path, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_ThrowsAndExtractsNothingWhenTheZipDoesNotMatch()
    {
        using var tmp = new TempDir();
        var zip = Zip(("BhMaps.exe", Exe));
        var wrong = Hex(Encoding.ASCII.GetBytes("a different zip"));
        using var http = new HttpClient(new FakeHttp().Bytes(ZipUrl, zip).Text(SumsUrl, ZipSums(wrong)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(
                Release(), UpdateBuild.FrameworkDependent, tmp.Path, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_RefusesADevelopmentBuild()
    {
        using var tmp = new TempDir();
        using var http = new HttpClient(new FakeHttp());

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(
                Release(), UpdateBuild.Development, tmp.Path, null, CancellationToken.None));
    }

    [Theory]
    [InlineData("abc123  bhmaps-v2.6.0-win-x64.exe", "abc123")]
    [InlineData("abc123 *bhmaps-v2.6.0-win-x64.exe", "abc123")]
    [InlineData("ABC123  BHMAPS-V2.6.0-WIN-X64.EXE", "abc123")]
    public void ChecksumFor_ReadsSha256sumStyleLines(string line, string expected) =>
        Assert.Equal(expected, UpdateClient.ChecksumFor(line, "bhmaps-v2.6.0-win-x64.exe"));

    [Fact]
    public void ChecksumFor_ReturnsNullWhenNoLineNamesTheFile() =>
        Assert.Null(UpdateClient.ChecksumFor("abc123  other.exe\n", "bhmaps-v2.6.0-win-x64.exe"));
}

/// <summary>The one copy of the releases/latest sample, shared by every test of the update classes.</summary>
internal static class UpdateSamples
{
    /// <summary>A cut-down copy of the shape api.github.com returns for releases/latest, with the three assets
    /// 2.6.0 ships. Saved as a string: no test of these classes ever reaches the network.</summary>
    public const string LatestJson = """
    {
      "url": "https://api.github.com/repos/as9pa/bhmaps/releases/1",
      "html_url": "https://github.com/as9pa/bhmaps/releases/tag/v2.6.0",
      "tag_name": "v2.6.0",
      "name": "BhMaps 2.6.0",
      "draft": false,
      "prerelease": false,
      "published_at": "2026-09-14T10:30:00Z",
      "body": "Pack tile menus, copy and move, auto-update.",
      "assets": [
        {
          "name": "bhmaps-v2.6.0-win-x64.exe",
          "size": 141557760,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64.exe"
        },
        {
          "name": "bhmaps-v2.6.0-win-x64-dotnet.zip",
          "size": 3145728,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64-dotnet.zip"
        },
        {
          "name": "SHA256SUMS.txt",
          "size": 189,
          "browser_download_url": "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/SHA256SUMS.txt"
        }
      ]
    }
    """;
}
