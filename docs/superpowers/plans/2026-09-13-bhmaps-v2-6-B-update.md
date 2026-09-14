# BhMaps 2.6 Part B Implementation Plan: auto-update

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BhMaps notices a newer GitHub release once a day, says so on one quiet top bar line, and on the Settings Version row offers a verified download of the new self-contained exe that swaps itself in when the app closes. No token, no download without the button, no restart on its own, no dialog on start.

**Architecture:** Three Core pieces with no UI and no app state. `ReleaseChecker` turns the GitHub `releases/latest` JSON into a `ReleaseInfo` and compares versions. `UpdateClient` wraps one `HttpClient` the App owns: check, and a streaming download verified against `SHA256SUMS.txt`. `UpdateInstaller` writes the `.cmd` that waits for the app to exit, swaps the exe and starts it, and decides whether swapping is possible at all. The App adds three settings keys, a delayed start-up check on the shell, a top bar line, and two Settings rows. `scripts\publish.ps1` gains the checksum file the download verifies against.

**Tech Stack:** .NET 10, WPF, `System.Net.Http` and `System.Text.Json` from the shared framework (no new package), CommunityToolkit.Mvvm, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-13-bhmaps-v2-6-design.md` section 7 (binding), with the global constraints of section 10. Sections 2 to 6 and 8 are other plans on the same branch.

**Branch:** `feature/bhmaps-v2.6`.

## Conflicts with spec

1. **The repo has to be public for this to work at all.** Spec 7.1 has `CheckAsync` GET `https://api.github.com/repos/as9pa/bhmaps/releases/latest` and spec 7.3 forbids a token in the app. A private repo answers that URL with 404 for an anonymous caller, so every check would silently return null and the feature would be dead code. **Resolution:** nothing in this plan changes, because `CheckAsync` returning null on any failure is already the correct behaviour for that case and the UI already has the `Not checked yet.` and `Could not reach GitHub. Try again later.` states. Task 7 adds the release checklist line "the repo must be public before 2.6 ships, or the update check can never succeed" to the manual's Development section, and the release controller confirms it before tagging. No secret is ever added.

2. **`UpdateClient(HttpClient http)` cannot know the app version for the `User-Agent`.** Spec 7.1 gives the one-argument constructor and also requires `User-Agent: BhMaps/{version}`. `BhMaps.Core` has no `<Version>`, so its own assembly version is `1.0.0` and a version read inside Core would be wrong. **Resolution:** the constructor stays exactly as the spec writes it, and the App sets `User-Agent` and `Accept` once as default headers on the `HttpClient` it creates (Task 5, `App.OnStartup`). The request still carries both headers, which is what the spec asks for.

3. **A 10 s `HttpClient.Timeout` would also cap the 135 MB download.** Spec 7.1 puts the 10 s on the check and a progress bar on the download. `HttpClient.Timeout` is per request, not per connect. **Resolution:** the App creates the client with `Timeout = Timeout.InfiniteTimeSpan` and `CheckAsync` applies its own 10 s through a linked `CancellationTokenSource`. `DownloadAsync` is bounded only by the caller's token and the Cancel button.

4. **`RuntimeEnvironment.GetRuntimeDirectory()` is untestable and may warn under `TreatWarningsAsErrors`.** **Resolution:** `CanSwap(string exePath)` stays the public shape spec 7.1 names and delegates to an `internal` three-argument overload that takes the runtime directory and the base directory, which the tests drive. `InternalsVisibleTo` for `BhMaps.Core.Tests` already exists in `src\BhMaps.Core\BhMaps.Core.csproj` :18.

5. **There is no "install page".** Spec 7.4 says "the manual's install page". `docs\manual.md` is one file with no install page; the closest homes are `## Where things live` (:145) and `## Development` (:448). **Resolution:** Task 7 puts the update paragraph and the new `%APPDATA%\BhMaps\updates\` table row in `## Where things live`, and the third release file in `## Development`.

6. **The Version row is the last grid row, so the Updates row needs a new row and a rule.** `SettingsPageView.xaml` declares rows 0 to 6 and Version is row 6 (:173-180) with no `SettingRule` above it. **Resolution:** Task 6 adds a seventh `RowDefinition`, a `SettingRule` on row 6 (above Version, matching every other row boundary) and one on row 7 (above Updates).

7. **`<Version>2.6.0</Version>` is not this plan's.** Spec 10 puts the version bump, README and manual version lines in the release task, which is another plan. This plan only makes `publish.ps1` produce a third file, whatever the version is.

## Global Constraints

- .NET 10, WPF, `TreatWarningsAsErrors` on. Format with `dotnet format BhMaps.slnx` only. Build and test with `--artifacts-path <ART>`.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1). No new test package: the `HttpMessageHandler` fake is hand-written.
- **No network in tests.** Every `UpdateClient` test goes through a fake `HttpMessageHandler`; no test resolves a host name. No test touches the real game folder, the real library or the real `%APPDATA%\BhMaps`; temp folders come from `tests\BhMaps.Core.Tests\Helpers\TempDir.cs`.
- New `.cs` and `.xaml` files CRLF. Docs CRLF, UTF-8 without BOM. No em-dashes, no emoji, anywhere, in code or copy. UI copy in sentence case.
- WPF: `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute on every focusable control added, including the dismiss `x` button and the new checkbox. No bare `x:Static` const int into a double.
- `[ObservableProperty]` setters run `OnXChanged` during construction, so the Settings page's `_refreshing` guard (`SettingsPageViewModel` :15, :20-28) covers every new observable row it assigns in the constructor and in `Refresh`.
- Never run the app or tests against the real game folder, the real library or `%APPDATA%\BhMaps`.
- **The app never contains a token.** No credential, no secret, no authenticated request, ever, in any of these tasks.
- **No download and no restart without the button.** The start-up check is a GET of the release JSON and nothing else. Only `UpdateCommand` downloads; only `CloseAndUpdateCommand` starts the script and closes the app.
- **No dialog on start.** The check's only visible result is the top bar line and the Settings row.
- One commit per task, message from a file with `git commit -F`, trailers as the dispatcher gives them.
- `AppSettings` is a positional record: the three new parameters go at the end, after `WriteGameThumbnails`. If another 2.6 part also adds a parameter, whichever lands second appends after the first; never reorder.

---

## Task 1: Core ReleaseInfo and ReleaseChecker

Spec 7.1, first three bullets.

**Files:**
- Create: `src\BhMaps.Core\Update\ReleaseInfo.cs`
- Create: `src\BhMaps.Core\Update\ReleaseChecker.cs`
- Test: `tests\BhMaps.Core.Tests\ReleaseCheckerTests.cs`

**Interfaces:**

Produces:

```csharp
namespace BhMaps.Core.Update;

/// <summary>One GitHub release, as much of it as the update flow needs. ExeUrl and ChecksumsUrl are null when the
/// release does not carry that asset, which is what makes a release undownloadable rather than unusable.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string TagName,
    DateTimeOffset PublishedAt,
    string HtmlUrl,
    string? ExeUrl,
    long ExeSize,
    string? ChecksumsUrl,
    string Body);

public static class ReleaseChecker
{
    /// <summary>The releases/latest JSON of the repo. Null for a draft, a prerelease, a tag that is not vX.Y.Z, or
    /// anything that does not parse as that JSON at all.</summary>
    public static ReleaseInfo? Parse(string json);

    /// <summary>Three-part compare: the release is newer when its major, minor or build is higher.</summary>
    public static bool IsNewer(ReleaseInfo release, Version current);

    /// <summary>The self-contained exe asset of a release, by name: bhmaps-v&lt;tag without v&gt;-win-x64.exe.</summary>
    public static string ExeName(Version version);

    public const string ChecksumsName = "SHA256SUMS.txt";
}
```

Consumes: nothing outside the framework.

- [ ] **Step 1: Test file.** Create `tests\BhMaps.Core.Tests\ReleaseCheckerTests.cs`:

```csharp
using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class ReleaseCheckerTests
{
    /// <summary>A cut-down copy of the shape api.github.com returns for releases/latest, with the three assets
    /// 2.6.0 ships. Saved as a string: no test of this class ever reaches the network.</summary>
    private const string LatestJson = """
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

    [Fact]
    public void Parse_ReadsTagAssetsAndDate()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.Equal(new Version(2, 6, 0), release!.Version);
        Assert.Equal("v2.6.0", release.TagName);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("https://github.com/as9pa/bhmaps/releases/tag/v2.6.0", release.HtmlUrl);
        Assert.Equal(
            "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/bhmaps-v2.6.0-win-x64.exe",
            release.ExeUrl);
        Assert.Equal(141557760, release.ExeSize);
        Assert.Equal(
            "https://github.com/as9pa/bhmaps/releases/download/v2.6.0/SHA256SUMS.txt",
            release.ChecksumsUrl);
        Assert.Equal("Pack tile menus, copy and move, auto-update.", release.Body);
    }

    [Fact]
    public void Parse_TakesTheSelfContainedExeNotTheZip()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.EndsWith("bhmaps-v2.6.0-win-x64.exe", release!.ExeUrl);
        Assert.Equal("bhmaps-v2.6.0-win-x64.exe", ReleaseChecker.ExeName(release.Version));
    }

    [Fact]
    public void Parse_ReturnsNullForADraft() =>
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"draft\": false", "\"draft\": true")));

    [Fact]
    public void Parse_ReturnsNullForAPrerelease() =>
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"prerelease\": false", "\"prerelease\": true")));

    [Theory]
    [InlineData("\"tag_name\": \"nightly\"")]
    [InlineData("\"tag_name\": \"v2.6\"")]
    [InlineData("\"tag_name\": \"\"")]
    public void Parse_ReturnsNullForATagThatIsNotVMajorMinorBuild(string tag)
    {
        Assert.Null(ReleaseChecker.Parse(LatestJson.Replace("\"tag_name\": \"v2.6.0\"", tag)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Parse_ReturnsNullForJunk(string json) => Assert.Null(ReleaseChecker.Parse(json));

    [Fact]
    public void Parse_KeepsTheReleaseWhenTheExeAssetIsMissing()
    {
        var json = LatestJson.Replace("bhmaps-v2.6.0-win-x64.exe", "bhmaps-v2.6.0-win-arm64.exe");

        var release = ReleaseChecker.Parse(json);

        Assert.NotNull(release);
        Assert.Null(release!.ExeUrl);
        Assert.Equal(0, release.ExeSize);
    }

    [Theory]
    [InlineData("2.5.0", true)]
    [InlineData("2.5.9", true)]
    [InlineData("1.9.9", true)]
    [InlineData("2.6.0", false)]
    [InlineData("2.6.1", false)]
    [InlineData("3.0.0", false)]
    public void IsNewer_ComparesThreeParts(string current, bool expected)
    {
        var release = ReleaseChecker.Parse(LatestJson);

        Assert.NotNull(release);
        Assert.Equal(expected, ReleaseChecker.IsNewer(release!, Version.Parse(current)));
    }

    [Fact]
    public void IsNewer_IgnoresTheAssemblysFourthPart()
    {
        var release = ReleaseChecker.Parse(LatestJson);

        // Assembly versions carry a fourth part; 2.6.0.0 is not older than 2.6.0.
        Assert.False(ReleaseChecker.IsNewer(release!, new Version(2, 6, 0, 0)));
    }
}
```

- [ ] **Step 2: Run, expect failure.** `dotnet test tests\BhMaps.Core.Tests --artifacts-path <ART>` does not compile, because `BhMaps.Core.Update` does not exist.

- [ ] **Step 3: Implement.** `src\BhMaps.Core\Update\ReleaseInfo.cs`:

```csharp
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
```

`src\BhMaps.Core\Update\ReleaseChecker.cs`:

```csharp
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
```

- [ ] **Step 4: Run all Core tests.** Every `ReleaseCheckerTests` fact passes and nothing else moved.
- [ ] **Step 5: Format, build, commit.** `dotnet format BhMaps.slnx`, build with `--artifacts-path <ART>`, then `git commit -F` with "Core: read the GitHub releases/latest JSON into a ReleaseInfo".

---

## Task 2: Core UpdateClient, check and verified download

Spec 7.1, fourth bullet.

**Files:**
- Create: `src\BhMaps.Core\Update\UpdateClient.cs`
- Create: `tests\BhMaps.Core.Tests\Helpers\FakeHttp.cs`
- Test: `tests\BhMaps.Core.Tests\UpdateClientTests.cs`

**Interfaces:**

Consumes: `ReleaseChecker.Parse`, `ReleaseChecker.ExeName`, `ReleaseChecker.ChecksumsName`, `ReleaseInfo` (Task 1).

Produces:

```csharp
namespace BhMaps.Core.Update;

public sealed class UpdateClient(HttpClient http)
{
    public const string LatestUrl = "https://api.github.com/repos/as9pa/bhmaps/releases/latest";
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Null on any failure at all: no network, a 404, junk JSON, a draft, the 10 s timeout.</summary>
    public Task<ReleaseInfo?> CheckAsync(CancellationToken ct);

    /// <summary>The verified exe's path in updatesDir. Throws InvalidDataException on a checksum mismatch, and
    /// whatever HttpClient throws on a network failure.</summary>
    public Task<string> DownloadAsync(
        ReleaseInfo release, string updatesDir, IProgress<(long Done, long Total)>? progress, CancellationToken ct);

    /// <summary>The hex digest SHA256SUMS.txt gives for one file name, or null when no line names it.</summary>
    public static string? ChecksumFor(string sums, string fileName);
}
```

- [ ] **Step 1: Test helper.** Create `tests\BhMaps.Core.Tests\Helpers\FakeHttp.cs`. No network: every response is built in memory.

```csharp
using System.Net;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>An HttpMessageHandler that answers from a table of urls, so UpdateClient can be tested without a
/// network. A url with no entry answers 404; a url mapped to null throws, standing in for no connection.</summary>
public sealed class FakeHttp : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requested { get; } = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttp Text(string url, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[url] = () => new HttpResponseMessage(status) { Content = new StringContent(body) };
        return this;
    }

    public FakeHttp Bytes(string url, byte[] body)
    {
        _routes[url] = () =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentLength = body.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        return this;
    }

    public FakeHttp Throws(string url)
    {
        _routes[url] = () => throw new HttpRequestException("no connection");
        return this;
    }

    /// <summary>Never completes until the token is cancelled, so the 10 s timeout can be tested with a token the
    /// test cancels itself rather than by waiting ten seconds.</summary>
    public FakeHttp Hangs(string url)
    {
        _routes[url] = null!;
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requested.Add(url);
        Requests.Add(request);
        if (!_routes.TryGetValue(url, out var route))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }

        if (route is null)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return route!();
    }
}
```

- [ ] **Step 2: Tests.** Create `tests\BhMaps.Core.Tests\UpdateClientTests.cs`. `LatestJson` is the same sample as `ReleaseCheckerTests`, so lift it into an `internal static class UpdateSamples` in this file rather than copying it twice.

```csharp
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
            Release(), tmp.Path, new Progress<(long, long)>(seen.Add), CancellationToken.None);

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
            () => new UpdateClient(http).DownloadAsync(Release(), tmp.Path, null, CancellationToken.None));

        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task DownloadAsync_ThrowsWhenNoLineNamesTheExe()
    {
        using var tmp = new TempDir();
        var sums = "0000000000000000000000000000000000000000000000000000000000000000  something-else.zip\n";
        using var http = new HttpClient(new FakeHttp().Bytes(ExeUrl, Exe).Text(SumsUrl, sums));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new UpdateClient(http).DownloadAsync(Release(), tmp.Path, null, CancellationToken.None));

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
            () => new UpdateClient(http).DownloadAsync(Release(), tmp.Path, null, cts.Token));

        Assert.Empty(Directory.GetFiles(tmp.Path));
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

internal static class UpdateSamples
{
    public const string LatestJson = """ ... same sample as ReleaseCheckerTests ... """;
}
```

Move the `LatestJson` const out of `ReleaseCheckerTests` into `UpdateSamples` in this step and have `ReleaseCheckerTests` read `UpdateSamples.LatestJson`, so there is one copy of the sample.

- [ ] **Step 3: Run, expect failure.** Compile error: no `UpdateClient`.

- [ ] **Step 4: Implement.** `src\BhMaps.Core\Update\UpdateClient.cs`:

```csharp
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
```

- [ ] **Step 5: Run all Core tests.** Confirm no test resolved a host name: every url in `FakeHttp.Requested` was answered from the table.
- [ ] **Step 6: Format, build, commit.** "Core: UpdateClient checks GitHub and downloads a checksum-verified exe".

---

## Task 3: Core UpdateInstaller

Spec 7.1, last two bullets.

**Files:**
- Create: `src\BhMaps.Core\Update\UpdateInstaller.cs`
- Test: `tests\BhMaps.Core.Tests\UpdateInstallerTests.cs`

**Interfaces:**

Produces:

```csharp
namespace BhMaps.Core.Update;

public static class UpdateInstaller
{
    public const string ScriptName = "apply-update.cmd";
    public const string OldSuffix = ".old";

    /// <summary>Writes apply-update.cmd into updatesDir and returns its path.</summary>
    public static string WriteApplyScript(string updatesDir, string newExe, string runningExe, int pid);

    /// <summary>Whether this build can swap itself: a writable folder and a self-contained runtime.</summary>
    public static bool CanSwap(string exePath);

    internal static bool CanSwap(string exePath, string runtimeDir, string baseDir);
}
```

Consumes: nothing outside the framework.

- [ ] **Step 1: Tests.** Create `tests\BhMaps.Core.Tests\UpdateInstallerTests.cs`. Nothing here runs the script: the tests read its text and probe a temp folder.

```csharp
using BhMaps.Core.Tests.Helpers;
using BhMaps.Core.Update;

namespace BhMaps.Core.Tests;

public class UpdateInstallerTests
{
    [Fact]
    public void WriteApplyScript_QuotesEveryPathAndWaitsForThePid()
    {
        using var tmp = new TempDir();
        var updates = tmp.Sub("updates", "x")[..^2];
        var newExe = Path.Combine(updates, "bhmaps-v2.6.0-win-x64.exe");
        var running = @"C:\Program Files\Bh Maps\BhMaps.exe";

        var script = UpdateInstaller.WriteApplyScript(updates, newExe, running, 4321);
        var text = File.ReadAllText(script);

        Assert.Equal(Path.Combine(updates, "apply-update.cmd"), script);
        Assert.Contains("tasklist /FI \"PID eq 4321\"", text);
        Assert.Contains("timeout /t 1", text);
        Assert.Contains($"\"{running}\"", text);
        Assert.Contains($"\"{newExe}\"", text);
        Assert.Contains($"\"{running}.old\"", text);
        Assert.DoesNotContain("C:\\Program Files\\Bh Maps\\BhMaps.exe ", text.Replace($"\"{running}\"", ""));
    }

    [Fact]
    public void WriteApplyScript_MovesRenamesStartsAndDeletesItself()
    {
        using var tmp = new TempDir();
        var script = UpdateInstaller.WriteApplyScript(
            tmp.Path, Path.Combine(tmp.Path, "new.exe"), Path.Combine(tmp.Path, "BhMaps.exe"), 10);
        var text = File.ReadAllText(script);

        // The order matters: the running exe is out of the way before the new one takes its name, and the app is
        // started before anything is deleted, so a failed start still leaves the .old file to go back to.
        var rename = text.IndexOf("move /y", StringComparison.Ordinal);
        var start = text.IndexOf("start \"\"", StringComparison.Ordinal);
        var cleanup = text.IndexOf(".old\"", start, StringComparison.Ordinal);
        Assert.True(rename > 0 && start > rename && cleanup > start);
        Assert.Contains("del /f /q \"%~f0\"", text);
        Assert.StartsWith("@echo off", text);
    }

    [Fact]
    public void WriteApplyScript_OverwritesAnOlderScript()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "apply-update.cmd");
        File.WriteAllText(path, "stale");

        UpdateInstaller.WriteApplyScript(tmp.Path, Path.Combine(tmp.Path, "n.exe"), Path.Combine(tmp.Path, "o.exe"), 1);

        Assert.DoesNotContain("stale", File.ReadAllText(path));
    }

    [Fact]
    public void CanSwap_TrueForAWritableFolderAndASelfContainedRuntime()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        Assert.True(UpdateInstaller.CanSwap(exe, tmp.Path, tmp.Path));
    }

    [Fact]
    public void CanSwap_FalseWhenTheRuntimeLivesOutsideTheAppFolder()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        Assert.False(UpdateInstaller.CanSwap(exe, @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.0\", tmp.Path));
    }

    [Fact]
    public void CanSwap_FalseWhenTheFolderIsNotThere()
    {
        using var tmp = new TempDir();
        var missing = Path.Combine(tmp.Path, "gone", "BhMaps.exe");

        Assert.False(UpdateInstaller.CanSwap(missing, Path.Combine(tmp.Path, "gone"), Path.Combine(tmp.Path, "gone")));
    }

    [Fact]
    public void CanSwap_LeavesNoProbeFileBehind()
    {
        using var tmp = new TempDir();
        var exe = Path.Combine(tmp.Path, "BhMaps.exe");
        File.WriteAllText(exe, "");

        UpdateInstaller.CanSwap(exe, tmp.Path, tmp.Path);

        Assert.Equal(new[] { "BhMaps.exe" }, Directory.GetFiles(tmp.Path).Select(Path.GetFileName));
    }
}
```

- [ ] **Step 2: Run, expect failure. Implement.** `src\BhMaps.Core\Update\UpdateInstaller.cs`:

```csharp
using System.Globalization;
using System.Runtime.InteropServices;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.1: the swap runs after the app is gone, so it cannot run inside the app. A .cmd waits for the
/// process to exit, moves the new exe over the old one and starts it. Every path is quoted: the app is normally
/// under a Program Files path with a space in it.</summary>
public static class UpdateInstaller
{
    public const string ScriptName = "apply-update.cmd";

    public const string OldSuffix = ".old";

    /// <summary>Writes the script and returns its path. The script is plain ASCII cmd, written fresh every time,
    /// and deletes itself last.</summary>
    public static string WriteApplyScript(string updatesDir, string newExe, string runningExe, int pid)
    {
        Directory.CreateDirectory(updatesDir);
        var scriptPath = Path.Combine(updatesDir, ScriptName);
        var oldExe = runningExe + OldSuffix;
        var id = pid.ToString(CultureInfo.InvariantCulture);

        var text =
            "@echo off\r\n"
            + "setlocal\r\n"
            + "rem Written by BhMaps. Waits for the app to close, swaps the exe, starts it, removes itself.\r\n"
            + ":wait\r\n"
            + $"tasklist /FI \"PID eq {id}\" | find \"{id}\" >nul\r\n"
            + "if not errorlevel 1 (\r\n"
            + "  timeout /t 1 /nobreak >nul\r\n"
            + "  goto wait\r\n"
            + ")\r\n"
            + $"if exist \"{oldExe}\" del /f /q \"{oldExe}\" >nul 2>&1\r\n"
            + $"move /y \"{runningExe}\" \"{oldExe}\" >nul\r\n"
            + "if errorlevel 1 goto fail\r\n"
            + $"move /y \"{newExe}\" \"{runningExe}\" >nul\r\n"
            + "if errorlevel 1 goto restore\r\n"
            + $"start \"\" \"{runningExe}\"\r\n"
            + $"del /f /q \"{oldExe}\" >nul 2>&1\r\n"
            + "goto done\r\n"
            + ":restore\r\n"
            + $"move /y \"{oldExe}\" \"{runningExe}\" >nul 2>&1\r\n"
            + ":fail\r\n"
            + $"start \"\" \"{runningExe}\"\r\n"
            + ":done\r\n"
            + "del /f /q \"%~f0\"\r\n";

        File.WriteAllText(scriptPath, text);
        return scriptPath;
    }

    /// <summary>False means the button reads "Open release page" instead of offering a download (spec 7.3): the
    /// framework-dependent zip build cannot be swapped by one file, and a folder the user cannot write to cannot
    /// be swapped at all.</summary>
    public static bool CanSwap(string exePath) =>
        CanSwap(exePath, RuntimeEnvironment.GetRuntimeDirectory(), AppContext.BaseDirectory);

    /// <summary>The testable half. Self-contained means the runtime the app is running on lives inside the app's
    /// own folder; the framework-dependent build finds it under Program Files instead.</summary>
    internal static bool CanSwap(string exePath, string runtimeDir, string baseDir)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        if (!Normalize(runtimeDir).StartsWith(Normalize(baseDir), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var probe = Path.Combine(folder, $".bhmaps-write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
}
```

If the build reports an obsolete-API error for `RuntimeEnvironment.GetRuntimeDirectory()`, replace only that one call site with `Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } dir ? dir : AppContext.BaseDirectory` (a single-file self-contained build reports an empty `Location`, which is exactly the self-contained case). The `internal` overload and every test stay as they are.

- [ ] **Step 3: Run all Core tests. Format, build, commit.** "Core: UpdateInstaller writes the swap script and says when a swap is possible".

---

## Task 4: The three settings keys

Spec 7.2, and the `{when}` wording of 7.3.

**Files:**
- Modify: `src\BhMaps.Core\Settings\AppSettings.cs` (record parameters :3-15)
- Modify: `src\BhMaps.Core\Settings\SettingsStore.cs` (`KnownKeys` :18-23, `Load` :81-99, `Save` :105-118, helpers :220-225)
- Create: `src\BhMaps.Core\Update\UpdateText.cs`
- Test: `tests\BhMaps.Core.Tests\SettingsStoreTests.cs` (additions), `tests\BhMaps.Core.Tests\ReleaseCheckerTests.cs` (additions for `UpdateText`)

**Interfaces:**

Produces:

```csharp
// AppSettings gains three trailing parameters, so every existing positional construction still compiles:
public sealed record AppSettings(
    ..., bool WriteGameThumbnails = false,
    bool CheckForUpdates = true,
    DateTimeOffset? LastUpdateCheck = null,
    string? DismissedUpdate = null);

namespace BhMaps.Core.Update;

public static class UpdateText
{
    /// <summary>"today, 15:40", "yesterday, 15:40", "14 Sep 2026, 15:40", or "never".</summary>
    public static string CheckedWhen(DateTimeOffset? last, DateTimeOffset now);

    /// <summary>"14 Sep 2026", the release date the Version row names.</summary>
    public static string ReleaseDate(DateTimeOffset published);

    /// <summary>"2.6.0", never the assembly's fourth part.</summary>
    public static string Short(Version version);

    /// <summary>"41 of 135 MB".</summary>
    public static string Megabytes(long done, long total);
}
```

Consumes: nothing.

- [ ] **Step 1: Tests.** In `SettingsStoreTests.cs`:

```csharp
    [Fact]
    public void Load_DefaultsTheUpdateKeysToOnAndNeverChecked()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(path, "{ \"gamePath\": \"D:\\\\g\", \"libraryPath\": \"D:\\\\l\" }");

        var settings = SettingsStore.Load(path);

        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.LastUpdateCheck);
        Assert.Null(settings.DismissedUpdate);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheUpdateKeys()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        var checkedAt = new DateTimeOffset(2026, 9, 14, 15, 40, 0, TimeSpan.Zero);
        var settings = new AppSettings(@"D:\g", @"D:\l", true)
        {
            CheckForUpdates = false,
            LastUpdateCheck = checkedAt,
            DismissedUpdate = "v2.6.0",
        };

        SettingsStore.Save(path, settings);
        var read = SettingsStore.Load(path);

        Assert.False(read.CheckForUpdates);
        Assert.Equal(checkedAt, read.LastUpdateCheck);
        Assert.Equal("v2.6.0", read.DismissedUpdate);
        var json = File.ReadAllText(path);
        Assert.Contains("\"checkForUpdates\": false", json);
        Assert.Contains("\"lastUpdateCheck\"", json);
        Assert.Contains("\"dismissedUpdate\": \"v2.6.0\"", json);
    }

    [Fact]
    public void Save_WritesNullForANeverCheckedFileRatherThanDroppingTheKey()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");

        SettingsStore.Save(path, new AppSettings(@"D:\g", @"D:\l", true));

        var json = File.ReadAllText(path);
        Assert.Contains("\"lastUpdateCheck\": null", json);
        Assert.Contains("\"dismissedUpdate\": null", json);
        Assert.Contains("\"checkForUpdates\": true", json);
    }

    [Fact]
    public void Load_IgnoresAHandEditedUpdateStampRatherThanFailingTheFile()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Path, "settings.json");
        File.WriteAllText(path, "{ \"lastUpdateCheck\": \"soon\", \"checkForUpdates\": \"yes\" }");

        var settings = SettingsStore.Load(path);

        Assert.Null(settings.LastUpdateCheck);
        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.Unknown);
    }
```

The `Unknown` assertion is the point of the last one: the three keys must be in `KnownKeys`, or `Save` writes both spellings.

In `ReleaseCheckerTests.cs`:

```csharp
    [Fact]
    public void CheckedWhen_ReadsTodayYesterdayAndADate()
    {
        var now = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal("never", UpdateText.CheckedWhen(null, now));
        Assert.Equal("today, 15:40", UpdateText.CheckedWhen(now.AddHours(-2).AddMinutes(-20), now));
        Assert.Equal("yesterday, 15:40", UpdateText.CheckedWhen(now.AddDays(-1).AddHours(-2).AddMinutes(-20), now));
        Assert.Equal("8 Sep 2026", UpdateText.CheckedWhen(new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero), now));
    }

    [Fact]
    public void ReleaseDateAndShortAndMegabytes_MatchTheSpecsCopy()
    {
        Assert.Equal("14 Sep 2026", UpdateText.ReleaseDate(new DateTimeOffset(2026, 9, 14, 10, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2.6.0", UpdateText.Short(new Version(2, 6, 0, 0)));
        Assert.Equal("41 of 135 MB", UpdateText.Megabytes(43_000_000, 141_557_760));
        Assert.Equal("0 of 135 MB", UpdateText.Megabytes(0, 141_557_760));
    }
```

- [ ] **Step 2: Run, expect failure. Implement.**

`AppSettings`: append after `bool WriteGameThumbnails = false`:

```csharp
    bool CheckForUpdates = true,

    /// <summary>Spec 7.2: when the last start-up check ran, so the next one waits 24 h. Null means never.</summary>
    DateTimeOffset? LastUpdateCheck = null,

    /// <summary>Spec 7.3: the tag of a release the user waved away on the top bar. A later release has a
    /// different tag, so the line comes back on its own.</summary>
    string? DismissedUpdate = null)
```

`SettingsStore.KnownKeys`: add `"checkForUpdates", "lastUpdateCheck", "dismissedUpdate",`.

`SettingsStore.Load`: after `Bool(obj, "writeGameThumbnails")` add

```csharp
            Bool(obj, "checkForUpdates", fallback: true),
            Time(obj, "lastUpdateCheck"),
            Str(obj, "dismissedUpdate") is { Length: > 0 } tag ? tag : null)
```

`SettingsStore.Save`: after `["writeGameThumbnails"]` add

```csharp
            ["checkForUpdates"] = settings.CheckForUpdates,
            ["lastUpdateCheck"] = settings.LastUpdateCheck?.ToString("o", CultureInfo.InvariantCulture),
            ["dismissedUpdate"] = settings.DismissedUpdate,
```

A `JsonObject` indexer assigned a null string writes JSON null, which is what the never-checked test asserts.

New helpers beside the existing ones at :220:

```csharp
    /// <summary><paramref name="fallback"/> when the key is absent or holds anything other than a JSON boolean,
    /// so a setting that is on unless it was deliberately turned off stays on through a hand-edited file.</summary>
    private static bool Bool(JsonObject obj, string key, bool fallback) =>
        obj[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : fallback;

    /// <summary>A round-trip timestamp, or null when the key is absent, null, or not a date this version reads.</summary>
    private static DateTimeOffset? Time(JsonObject obj, string key) =>
        obj[key]?.GetValueKind() == JsonValueKind.String
        && DateTimeOffset.TryParse(
            obj[key]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var stamp)
            ? stamp
            : null;
```

`src\BhMaps.Core\Update\UpdateText.cs`:

```csharp
using System.Globalization;

namespace BhMaps.Core.Update;

/// <summary>Spec 7.3's wording, in one place, so the top bar, the Version row and the Updates row say the same
/// thing the same way. Every date is formatted invariant: the copy is English and reads the same everywhere.</summary>
public static class UpdateText
{
    public static string CheckedWhen(DateTimeOffset? last, DateTimeOffset now)
    {
        if (last is not { } then)
        {
            return "never";
        }

        var local = then.ToLocalTime();
        var today = now.ToLocalTime().Date;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today)
        {
            return $"today, {time}";
        }

        return local.Date == today.AddDays(-1)
            ? $"yesterday, {time}"
            : ReleaseDate(then);
    }

    public static string ReleaseDate(DateTimeOffset published) =>
        published.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Short(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";

    /// <summary>Whole megabytes both sides, because a byte count moving under a progress bar is noise.</summary>
    public static string Megabytes(long done, long total) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{done / 1_048_576} of {total / 1_048_576} MB");
}
```

- [ ] **Step 3: Run all Core tests.** `SettingsStoreTests.SaveThenLoad_RoundTripsWithCamelCaseJsonAndNoTempFile` compares whole records, so it must still pass unchanged: the three defaults round-trip.
- [ ] **Step 4: Format, build, commit.** "Settings: checkForUpdates, lastUpdateCheck and dismissedUpdate".

---

## Task 5: The start-up check and the top bar line

Spec 7.3, first two bullets. No dialog, nothing blocking, nothing downloaded.

**Files:**
- Modify: `src\BhMaps.App\App.xaml.cs` (`OnStartup` :34-36, `Exit` :35)
- Modify: `src\BhMaps.App\Services\AppServices.cs` (ctor :26, properties :47-73, `Dispose` :143)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (ctor :38-64, observable state near :93-145, `RescanAsync` :851-878, `Shutdown` :896-900, `NavigateSettings` :172)
- Modify: `src\BhMaps.App\Views\MainWindow.xaml` (top bar right cell, the `Grid` at :49-113)
- Test: none. This is shell wiring over Core that is already covered; `tests\BhMaps.Core.Tests` does not reference the App project.

**Interfaces:**

Consumes: `UpdateClient.CheckAsync`, `ReleaseChecker.IsNewer`, `ReleaseInfo`, `AppSettings.CheckForUpdates/LastUpdateCheck/DismissedUpdate`.

Produces:

```csharp
// AppServices
public UpdateClient Updates { get; }          // built from the HttpClient the App passes in
public string UpdatesDir { get; }             // Path.Combine(AppDataDir, "updates")
public Version AppVersion { get; }            // the entry assembly's version, 0.0.0 when there is none

// MainViewModel
public ReleaseInfo? AvailableUpdate { get; private set; }   // [ObservableProperty]
public bool ShowUpdateLine { get; }                          // AvailableUpdate is newer and not dismissed
public IRelayCommand DismissUpdateCommand { get; }
public IRelayCommand OpenUpdateCommand { get; }              // navigates to Settings
public Task CheckForUpdateAsync(bool force);                 // force: the Check now button
```

- [ ] **Step 1: One HttpClient, created by the App.** In `App.OnStartup`, immediately before `new AppServices(...)` at :34:

```csharp
        // Spec 7.1: one HttpClient for the life of the app, created here because only the App knows its own
        // version for the User-Agent. No timeout on the client itself: UpdateClient puts 10 s on the check, and a
        // 135 MB download must not be cut off by the check's limit. No credential of any kind is ever set.
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"BhMaps/{version.Major}.{version.Minor}.{version.Build}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var services = new AppServices(
            parsed.AppData ?? SettingsStore.DefaultAppDataDir, parsed.Game, parsed.Library, http);
```

`AppServices` takes `HttpClient http` as a fourth constructor parameter, keeps it in a field, and builds `Updates = new UpdateClient(http)`, `UpdatesDir = Path.Combine(AppDataDir, "updates")`, `AppVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0)`. `Dispose` (:143) becomes

```csharp
    public void Dispose()
    {
        Renderer.Dispose();
        _http.Dispose();
    }
```

Nothing creates `UpdatesDir` here: `UpdateClient.DownloadAsync` creates it on the first real download, so a user who never updates never gets the folder.

- [ ] **Step 2: Shell state.** In `MainViewModel`, beside the other observable properties:

```csharp
    /// <summary>Spec 7.3: the latest release the last check found, or null when nothing has been found yet. Set
    /// off the UI thread's work but assigned on it, because the top bar binds to it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowUpdateLine))]
    public partial ReleaseInfo? AvailableUpdate { get; set; }

    /// <summary>The top bar line shows only for a release newer than this build whose tag has not been waved
    /// away. A later release carries a different tag, so the line comes back on its own.</summary>
    public bool ShowUpdateLine =>
        AvailableUpdate is { } release
        && ReleaseChecker.IsNewer(release, Services.AppVersion)
        && !string.Equals(release.TagName, Services.Settings.DismissedUpdate, StringComparison.OrdinalIgnoreCase);
```

and the two commands:

```csharp
    /// <summary>Spec 7.3: the dismiss x remembers the tag, so this release never asks again and the next one
    /// does. Saving is best effort: a settings file that cannot be written is not worth a dialog here.</summary>
    [RelayCommand]
    private void DismissUpdate()
    {
        if (AvailableUpdate is not { } release)
        {
            return;
        }

        try
        {
            Services.UpdateSettings(Services.Settings with { DismissedUpdate = release.TagName });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine($"BhMaps: the dismissed update was not saved: {ex.Message}");
        }

        OnPropertyChanged(nameof(ShowUpdateLine));
    }

    /// <summary>The line itself is the way to the Settings row that offers the download. Nothing downloads here.</summary>
    [RelayCommand]
    private void OpenUpdate()
    {
        CurrentPage = SettingsPage;
        SettingsPage.Refresh(Snapshot!);
    }
```

If `Refresh(ScanSnapshot)` is not reachable with a null snapshot, call `SettingsPage.RefreshUpdateRow()` from Task 6 instead and leave navigation alone; the implementer picks whichever exists at that point and keeps only one.

- [ ] **Step 3: The check, 5 s after the first scan.** `MainViewModel` gains a field `private bool _updateCheckStarted;` and `RescanAsync` ends, after `CanUndo = ...`, with

```csharp
        // Spec 7.3: once a run, 5 s after the first scan finished, off the UI thread and blocking nothing. A
        // timer rather than an await, so the scan's caller is not held by it.
        if (!_updateCheckStarted)
        {
            _updateCheckStarted = true;
            _updateTimer.Start();
        }
```

with, in the constructor beside `_gameTimer`:

```csharp
        // One shot: the tick stops the timer and starts the check.
        _updateTimer = new DispatcherTimer { Interval = UpdateCheckDelay };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Stop();
            _ = CheckForUpdateAsync(force: false);
        };
```

and `private static readonly TimeSpan UpdateCheckDelay = TimeSpan.FromSeconds(5);` beside `GamePollInterval` (:22). `Shutdown` (:896) stops it: `_updateTimer.Stop();`.

```csharp
    /// <summary>Spec 7.3: the check itself. Off unless the setting is on; at most once a day unless the Settings
    /// page's Check now button forces it. Never throws, never shows a dialog, never downloads, never blocks: the
    /// only thing it can do is set AvailableUpdate and stamp lastUpdateCheck.</summary>
    public async Task CheckForUpdateAsync(bool force)
    {
        if (!force && (!Services.Settings.CheckForUpdates || !DueForCheck(Services.Settings.LastUpdateCheck)))
        {
            return;
        }

        UpdateCheckFailed = false;
        UpdateChecking = true;
        try
        {
            var release = await Task.Run(() => Services.Updates.CheckAsync(CancellationToken.None));
            AvailableUpdate = release;
            UpdateCheckFailed = release is null;
            if (release is not null || force)
            {
                try
                {
                    Services.UpdateSettings(Services.Settings with { LastUpdateCheck = DateTimeOffset.UtcNow });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Trace.WriteLine($"BhMaps: the update stamp was not saved: {ex.Message}");
                }
            }
        }
        finally
        {
            UpdateChecking = false;
            SettingsPage.RefreshUpdateRow();
        }
    }

    /// <summary>Null, or older than a day. A stamp in the future (a clock that moved) counts as due.</summary>
    private static bool DueForCheck(DateTimeOffset? last) =>
        last is not { } then || DateTimeOffset.UtcNow - then >= TimeSpan.FromHours(24) || then > DateTimeOffset.UtcNow;
```

`UpdateChecking` and `UpdateCheckFailed` are two more `[ObservableProperty] public partial bool` on the shell, read by the Settings row in Task 6.

- [ ] **Step 4: Top bar line.** In `MainWindow.xaml`, the right-hand cell at :49 becomes a horizontal `StackPanel` holding the update line first and the existing three-state `Grid` second, so the line sits before the Brawlhalla state as the spec says:

```xml
        <StackPanel Grid.Column="2" VerticalAlignment="Center" Orientation="Horizontal">

          <!-- Spec 7.3: one quiet line, only for a newer release that has not been waved away. It navigates to
               the Settings row that offers the download; nothing here downloads or restarts anything. -->
          <StackPanel Margin="0,0,16,0"
                      VerticalAlignment="Center"
                      Orientation="Horizontal"
                      Visibility="{Binding ShowUpdateLine, Converter={StaticResource BoolToVis}}">
            <Button Command="{Binding OpenUpdateCommand}"
                    Content="New update available"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Foreground="{StaticResource Text2Brush}"
                    Style="{StaticResource PlainButton}" />
            <Button Margin="2,0,0,0"
                    AutomationProperties.Name="Dismiss the update notice"
                    Command="{Binding DismissUpdateCommand}"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Style="{StaticResource PlainButton}"
                    ToolTip="Dismiss"
                    controls:Icon.Glyph="{StaticResource Icon.X}" />
          </StackPanel>

          <Grid VerticalAlignment="Center">
            ... the existing three states, unchanged ...
          </Grid>
        </StackPanel>
```

`MainWindow.xaml` has no `BooleanToVisibilityConverter` yet: add `<Window.Resources><BooleanToVisibilityConverter x:Key="BoolToVis" /></Window.Resources>` under the `InputBindings` block, matching the key `SettingsPageView.xaml` :6 uses.

- [ ] **Step 5: Run the app by hand.** Start it with no network reachable and confirm: no dialog, no delay, the top bar unchanged, and the Settings Version row reading `Could not reach GitHub. Try again later.` once Task 6 lands. Confirm nothing was written into `%APPDATA%\BhMaps\updates`.
- [ ] **Step 6: Format, build, commit.** "App: a once-a-day update check and one top bar line".

---

## Task 6: The Settings Version row and the new Updates row

Spec 7.3, bullets three to five.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\Pages\SettingsPageViewModel.cs` (ctor :17-33, `Refresh` :79-87, `Save` :192-)
- Modify: `src\BhMaps.App\Views\Pages\SettingsPageView.xaml` (`RowDefinitions` :64-71, Version row :172-180, new row 7)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (a `ThinProgress` style, appended near the other bar styles)
- Test: none (App project; the Core behind it is covered by Tasks 1 to 4).

**Interfaces:**

Consumes: `Shell.AvailableUpdate`, `Shell.UpdateChecking`, `Shell.UpdateCheckFailed`, `Shell.CheckForUpdateAsync`, `Services.Updates.DownloadAsync`, `UpdateInstaller.CanSwap/WriteApplyScript`, `UpdateText`, `Services.UpdatesDir`, `Services.AppVersion`.

Produces on `SettingsPageViewModel`:

```csharp
public string UpdateLine { get; }             // the Version row's second line
public bool HasUpdateLine { get; }
public string UpdateButtonText { get; }       // "Update to 2.6.0" or "Open release page" or "Close and update"
public bool CanUpdate { get; }
public bool ShowWhatChanged { get; }
public bool Downloading { get; }
public double DownloadProgress { get; }       // 0 to 1
public bool CheckForUpdates { get; set; }     // [ObservableProperty], the new checkbox
public string UpdatesLine { get; }            // the Updates row's explanatory line
public IAsyncRelayCommand UpdateCommand { get; }
public IRelayCommand CancelDownloadCommand { get; }
public IRelayCommand WhatChangedCommand { get; }
public IAsyncRelayCommand CheckNowCommand { get; }
public void RefreshUpdateRow();               // called by the shell after a check
```

- [ ] **Step 1: A thin progress bar.** `Controls.xaml` has no `ProgressBar` style, so add one, with the project's own tokens and nothing else:

```xml
  <!-- Spec 7.3: the one progress bar in the app, three pixels of it, under the download line. -->
  <Style x:Key="ThinProgress" TargetType="ProgressBar">
    <Setter Property="Height" Value="3" />
    <Setter Property="Background" Value="{StaticResource Line2Brush}" />
    <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="Maximum" Value="1" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ProgressBar">
          <Grid>
            <Border Background="{TemplateBinding Background}" CornerRadius="{StaticResource Radius}" />
            <Border x:Name="PART_Track" />
            <Border x:Name="PART_Indicator"
                    HorizontalAlignment="Left"
                    Background="{TemplateBinding Foreground}"
                    CornerRadius="{StaticResource Radius}" />
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

`PART_Track` and `PART_Indicator` are the names `ProgressBar` drives; both must be present or the indicator never moves.

- [ ] **Step 2: View model.** Add to `SettingsPageViewModel`. The constructor's `_refreshing` block (:20-28) gains `CheckForUpdates = shell.Services.Settings.CheckForUpdates;`, and `Refresh` (:79-87) gains the same line plus `RefreshUpdateRow();` after `_refreshing = false;`.

```csharp
    private CancellationTokenSource? _downloadCts;

    /// <summary>Set once the download has finished and been verified: the path of the exe the swap will move.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    public partial string? ReadyExe { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateLine))]
    public partial string UpdateLine { get; set; }

    [ObservableProperty]
    public partial bool Downloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    /// <summary>Spec 7.2: the Updates row's checkbox. Saves the moment it moves, like every other row here.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial string UpdatesLine { get; set; }

    public bool HasUpdateLine => UpdateLine.Length > 0;

    /// <summary>Spec 7.3: a build that cannot swap itself never offers a download at all.</summary>
    public bool CanSwap { get; } = UpdateInstaller.CanSwap(Environment.ProcessPath ?? "");

    public bool ShowWhatChanged => Newer is not null && !Downloading;

    public bool CanUpdate => Newer is not null && !Downloading;

    public string UpdateButtonText =>
        !CanSwap ? "Open release page"
        : ReadyExe is not null ? "Close and update"
        : Newer is { } release ? $"Update to {UpdateText.Short(release.Version)}"
        : "";

    /// <summary>The available release when it is actually newer than this build, otherwise null.</summary>
    private ReleaseInfo? Newer =>
        Shell.AvailableUpdate is { } release && ReleaseChecker.IsNewer(release, Services.AppVersion)
            ? release
            : null;

    /// <summary>Spec 7.3's four states of the Version row's second line, plus the two download states. Called
    /// from the constructor, from Refresh, and by the shell after every check.</summary>
    public void RefreshUpdateRow()
    {
        UpdatesLine =
            "Once a day, one small request to github.com. Nothing is sent about you or your library. Last checked "
            + UpdateText.CheckedWhen(Services.Settings.LastUpdateCheck, DateTimeOffset.UtcNow)
            + ".";

        if (Downloading)
        {
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(ShowWhatChanged));
            OnPropertyChanged(nameof(UpdateButtonText));
            return;
        }

        if (ReadyExe is not null && Newer is { } ready)
        {
            UpdateLine = $"{UpdateText.Short(ready.Version)} is ready. It installs when you close BhMaps.";
        }
        else if (Newer is { } release)
        {
            var line =
                $"{UpdateText.Short(release.Version)} is available. Released {UpdateText.ReleaseDate(release.PublishedAt)}. "
                + "Update downloads the new exe and swaps it in when you close BhMaps.";
            UpdateLine = CanSwap && release.CanDownload
                ? line
                : $"{UpdateText.Short(release.Version)} is available. Released {UpdateText.ReleaseDate(release.PublishedAt)}. "
                    + "Download the new version from the release page.";
        }
        else if (Shell.UpdateCheckFailed)
        {
            UpdateLine = "Could not reach GitHub. Try again later.";
        }
        else if (Services.Settings.LastUpdateCheck is null)
        {
            UpdateLine = "Not checked yet.";
        }
        else
        {
            UpdateLine =
                "You have the latest version. Checked "
                + UpdateText.CheckedWhen(Services.Settings.LastUpdateCheck, DateTimeOffset.UtcNow)
                + ".";
        }

        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(ShowWhatChanged));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_refreshing || Services.Settings.CheckForUpdates == value)
        {
            return;
        }

        if (!Save(Services.Settings with { CheckForUpdates = value }))
        {
            _refreshing = true;
            CheckForUpdates = Services.Settings.CheckForUpdates;
            _refreshing = false;
            return;
        }

        RefreshUpdateRow();
    }

    /// <summary>Spec 7.3: one button, three jobs. No swap possible, or no assets: the release page. Nothing
    /// downloaded yet: download it. Downloaded and verified: write the script, start it hidden and close.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (Newer is not { } release)
        {
            return;
        }

        if (!CanSwap || !release.CanDownload)
        {
            ExplorerLauncher.OpenUrl(release.HtmlUrl);
            return;
        }

        if (ReadyExe is { } ready)
        {
            CloseAndUpdate(ready);
            return;
        }

        _downloadCts = new CancellationTokenSource();
        Downloading = true;
        DownloadProgress = 0;
        UpdateLine = $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(0, release.ExeSize)}";
        RefreshUpdateRow();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                DownloadProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                UpdateLine =
                    $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(p.Done, p.Total)}";
            });

            ReadyExe = await Services.Updates.DownloadAsync(
                release, Services.UpdatesDir, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            ReadyExe = null;
        }
        catch (InvalidDataException)
        {
            ReadyExe = null;
            Shell.Dialogs.Error(
                "Update not installed",
                "The downloaded file did not match the checksum on the release, so it was deleted. Try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            ReadyExe = null;
            Shell.Dialogs.Error("Update not downloaded", ex.Message);
        }
        finally
        {
            Downloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            RefreshUpdateRow();
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private void WhatChanged()
    {
        if (Newer is { } release)
        {
            ExplorerLauncher.OpenUrl(release.HtmlUrl);
        }
    }

    /// <summary>The Check now button: the same check, past the once-a-day rule.</summary>
    [RelayCommand]
    private Task CheckNowAsync() => Shell.CheckForUpdateAsync(force: true);

    /// <summary>Spec 7.3: the script is started hidden and the window closed; the script waits for this process to
    /// be gone before it touches anything. Nothing restarts on its own: this runs only from the button.</summary>
    private void CloseAndUpdate(string newExe)
    {
        if (Environment.ProcessPath is not { Length: > 0 } running)
        {
            return;
        }

        try
        {
            var script = UpdateInstaller.WriteApplyScript(
                Services.UpdatesDir, newExe, running, Environment.ProcessId);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = Services.UpdatesDir,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            Shell.Dialogs.Error("Update not installed", ex.Message);
            return;
        }

        Application.Current.MainWindow?.Close();
    }
```

`ExplorerLauncher` (`src\BhMaps.App\Services\`) already opens a folder; add beside it

```csharp
    /// <summary>Opens a https url in whatever the machine's browser is. Returns the reason it could not, or null.</summary>
    public static string? OpenUrl(string url)
```

implemented as the existing `Open` is, with `UseShellExecute = true`, refusing anything that is not an absolute `https` uri.

- [ ] **Step 3: View.** `SettingsPageView.xaml`: add one more `<RowDefinition Height="Auto" MinHeight="56" />` after the seven already there, put a `<Border Grid.Row="6" Style="{StaticResource SettingRule}" />` above Version, and replace the Version row's single value `TextBlock` (:174-180) with a cell and an actions panel:

```xml
          <!-- Version -->
          <Border Grid.Row="6" Style="{StaticResource SettingRule}" />
          <TextBlock Grid.Row="6" Grid.Column="0" Style="{StaticResource SettingLabel}" Text="Version" />
          <StackPanel Grid.Row="6" Grid.Column="1" Style="{StaticResource SettingCell}">
            <TextBlock Style="{StaticResource SettingValue}" Text="{Binding Version}" />

            <!-- Spec 7.3: one second line for every state, available, up to date, never checked, failed, and the
                 two download states. The view model owns which one it is. -->
            <TextBlock Margin="0,6,0,0"
                       Foreground="{StaticResource Text2Brush}"
                       Text="{Binding UpdateLine}"
                       TextWrapping="Wrap"
                       Visibility="{Binding HasUpdateLine, Converter={StaticResource BoolToVis}}" />
            <ProgressBar Margin="0,8,0,0"
                         Style="{StaticResource ThinProgress}"
                         Value="{Binding DownloadProgress, Mode=OneWay}"
                         Visibility="{Binding Downloading, Converter={StaticResource BoolToVis}}" />
          </StackPanel>
          <StackPanel Grid.Row="6" Style="{StaticResource SettingActions}">
            <Button Command="{Binding CancelDownloadCommand}"
                    Content="Cancel"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Style="{StaticResource PlainButton}"
                    Visibility="{Binding Downloading, Converter={StaticResource BoolToVis}}" />
            <Button Margin="8,0,0,0"
                    Command="{Binding WhatChangedCommand}"
                    Content="What changed"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Style="{StaticResource PlainButton}"
                    Visibility="{Binding ShowWhatChanged, Converter={StaticResource BoolToVis}}" />
            <Button Margin="8,0,0,0"
                    Command="{Binding UpdateCommand}"
                    Content="{Binding UpdateButtonText}"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Style="{StaticResource PrimaryButton}"
                    Visibility="{Binding CanUpdate, Converter={StaticResource BoolToVis}}" />
          </StackPanel>

          <!-- Updates (spec 7.2, 7.3). Under Version, because it is about the row above it. -->
          <Border Grid.Row="7" Style="{StaticResource SettingRule}" />
          <TextBlock Grid.Row="7" Grid.Column="0" Style="{StaticResource SettingLabel}" Text="Updates" />
          <StackPanel Grid.Row="7" Grid.Column="1" Style="{StaticResource SettingCell}">
            <CheckBox AutomationProperties.Name="Check for updates when BhMaps starts"
                      Content="Check for updates when BhMaps starts"
                      FocusVisualStyle="{StaticResource DialogFocusRing}"
                      Foreground="{StaticResource TextBrush}"
                      IsChecked="{Binding CheckForUpdates}" />
            <TextBlock Margin="0,6,0,0"
                       Foreground="{StaticResource Text2Brush}"
                       Text="{Binding UpdatesLine}"
                       TextWrapping="Wrap" />
          </StackPanel>
          <StackPanel Grid.Row="7" Style="{StaticResource SettingActions}">
            <Button Command="{Binding CheckNowCommand}"
                    Content="Check now"
                    FocusVisualStyle="{StaticResource DialogFocusRing}"
                    Style="{StaticResource OutlineButton}" />
          </StackPanel>
```

The `Close and update` state reuses the same primary button: `UpdateButtonText` is what changes, not the button.

- [ ] **Step 4: Run the app by hand.** Check every state by hand: with no network (`Could not reach GitHub. Try again later.`), with `lastUpdateCheck` removed from a throwaway `--appdata` settings file (`Not checked yet.`), and with `Check now` pressed twice in a row (the line's `{when}` moves to `today, HH:mm` and nothing downloads). Use `--appdata` pointed at a temp folder for all of it; never the real `%APPDATA%\BhMaps`. Tab through the new row and confirm the focus ring shows on the checkbox, `Check now`, `What changed` and the primary button.
- [ ] **Step 5: Format, build, commit.** "Settings: the Version row offers the update and a new Updates row controls the check".

---

## Task 7: publish.ps1 writes SHA256SUMS.txt, and the manual

Spec 7.4.

**Files:**
- Modify: `scripts\publish.ps1` (the dist file list :16-20, the final report :46-49)
- Modify: `docs\manual.md` (`## Where things live` table :147-160 and the paragraph at :162-170; `## Development` publish paragraph :461-469)
- Test: none; the script's output is checked by running it.

**Interfaces:** produces `dist\SHA256SUMS.txt`, whose lines `UpdateClient.ChecksumFor` (Task 2) reads.

- [ ] **Step 1: The checksum file.** In `scripts\publish.ps1`, extend the cleanup list and add the write after the two publishes. Replace :16-20 with

```powershell
$exeOut = Join-Path $dist "bhmaps-v$version-win-x64.exe"
$zipOut = Join-Path $dist "bhmaps-v$version-win-x64-dotnet.zip"
$sumsOut = Join-Path $dist "SHA256SUMS.txt"
foreach ($f in $exeOut, $zipOut, $sumsOut) {
    if (Test-Path $f) { Remove-Item -Force $f }
}
```

and replace the final report at :46-49 with

```powershell
# The app verifies its download against this file, so it is a release asset like the other two. sha256sum's own
# format: lowercase hex, two spaces, the file name with no path.
$lines = foreach ($f in $exeOut, $zipOut) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $f).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($f))"
}
Set-Content -Path $sumsOut -Value $lines -Encoding ascii

foreach ($f in $exeOut, $zipOut, $sumsOut) {
    $mb = [math]::Round((Get-Item $f).Length / 1MB, 1)
    Write-Host "$f  $mb MB"
}

Write-Host ""
Write-Host "Upload all three files to the GitHub release. The update check needs SHA256SUMS.txt and a public repo."
```

`Set-Content -Encoding ascii` writes CRLF line endings, which `ChecksumFor` handles: it splits on `\n` and trims.

- [ ] **Step 2: Run it.** `pwsh -File scripts\publish.ps1` from the repo root. It must print three lines and `dist\SHA256SUMS.txt` must hold exactly two lines, each a 64-character lowercase hex digest, two spaces and a file name with no path. Verify one by hand: `(Get-FileHash -Algorithm SHA256 dist\bhmaps-v<version>-win-x64.exe).Hash.ToLower()` matches its line.
- [ ] **Step 3: Manual.** In `## Where things live`, add a table row after the undo row:

```
| Downloaded updates | `%APPDATA%\BhMaps\updates\` |
```

and add to the paragraph that lists what the settings file holds (:164-168) ", whether BhMaps checks for updates when it starts, when it last checked, and any update notice you dismissed". Then add a short paragraph after that one:

```
Once a day at most, a few seconds after the first scan, BhMaps asks GitHub whether there is a newer
release. It is one request to `github.com`; nothing about you, your machine or your library is sent,
and no account or token is involved. A newer release shows as one line in the top bar and on the
Settings Version row, and nothing is downloaded until you press the button. Turn the whole thing off
with the Updates row in Settings. An update you download is verified against the release's published
SHA-256 checksum, kept in `%APPDATA%\BhMaps\updates\`, and swapped in when you close BhMaps. Only the
self-contained exe can update itself; the framework-dependent zip build shows the release page
instead.
```

In `## Development`, change the publish paragraph at :465-469 to name three files: the self-contained exe, the dotnet zip, and `SHA256SUMS.txt`, "which the app's update check verifies a download against, so all three go on the release. The repository has to be public for the update check to reach the release at all."

- [ ] **Step 4: Format, build, run all Core tests. Commit.** "Release: publish.ps1 writes SHA256SUMS.txt, and the manual explains the update check".

---

## Plan self-review

### Spec coverage

| Spec 7 line | Task and step |
| --- | --- |
| `ReleaseInfo` record, eight members | 1.3 |
| `ReleaseChecker.Parse`, tag_name, published_at, html_url, body, assets | 1.1, 1.3 |
| Tag that does not parse returns null | 1.1 (theory), 1.3 `ParseTag` |
| Drafts and prereleases return null | 1.1, 1.3 |
| `ReleaseChecker.IsNewer`, three-part compare | 1.1, 1.3 |
| `UpdateClient(HttpClient http)` | 2.4 |
| `CheckAsync`, url, User-Agent, Accept, 10 s, null on any failure | 2.2, 2.4, 5.1 (headers on the client) |
| `DownloadAsync` streams to `.partial` | 2.4 |
| SHA-256 verified against `SHA256SUMS.txt` lines `<hex>  <name>` | 2.2, 2.4 `ChecksumFor` |
| Rename to the final name, return the path | 2.2, 2.4 |
| Mismatch deletes the file and throws `InvalidDataException` | 2.2, 2.4 |
| `UpdateInstaller.WriteApplyScript`, pid wait, rename, move, start, self-delete | 3.1, 3.2 |
| `UpdateInstaller.CanSwap`, writable folder and self-contained | 3.1, 3.2 |
| `checkForUpdates`, `lastUpdateCheck`, `dismissedUpdate` | 4.1, 4.2 |
| Start-up check, on, older than 24 h, off the UI thread, 5 s after the first scan, never blocking | 5.3 |
| `AvailableUpdate` on the shell, `lastUpdateCheck` written | 5.2, 5.3 |
| Top bar `New update available`, PlainButton, Text2, before the Brawlhalla state, navigates to Settings | 5.4 |
| Dismiss `x` sets `dismissedUpdate`; a later release shows again | 5.2 `DismissUpdate`, `ShowUpdateLine` |
| Version row first line, second line available state, `Update to X`, `What changed` | 6.2 `RefreshUpdateRow`, 6.3 |
| Up to date, never checked, failure lines | 6.2 |
| Updates row: checkbox, explanatory line, `Check now` | 6.2, 6.3 |
| Downloading line, thin progress bar, Cancel | 6.1, 6.2, 6.3 |
| Ready line and `Close and update`, script started hidden, app closed | 6.2 `CloseAndUpdate` |
| `CanSwap` false: `Open release page` from the start, line ends with the release-page sentence | 6.2 `UpdateButtonText`, `RefreshUpdateRow` |
| No download without the button, no restart on its own, no token, no dialog on start | Global Constraints; 5.3, 5.5, 6.2 |
| `publish.ps1` writes `dist\SHA256SUMS.txt`; the release uploads it | 7.1, 7.2 |
| The manual mentions the check and how to turn it off | 7.3 |

### Type consistency notes

- `ReleaseInfo.Version` is a three-part `Version`; the running version from `Assembly.GetName().Version` is four-part with a zero fourth. `IsNewer` drops the fourth part rather than comparing four against three, and `ReleaseCheckerTests.IsNewer_IgnoresTheAssemblysFourthPart` pins that.
- `IProgress<(long Done, long Total)>`: named tuple elements in the signature, but a `Progress<(long, long)>` in the test still binds, because tuple names are erased. Both spellings appear on purpose and both compile.
- `UpdateText.Megabytes` uses integer division, so `0 of 135 MB` is the first report, not `0.0`.
- `AppSettings` is a positional record with defaults, so `new AppSettings(game, library, true)` in the existing tests still compiles with three more parameters. `SettingsStore.Save` writes `lastUpdateCheck` and `dismissedUpdate` as JSON null rather than omitting them, and `Load` reads null back as null, so the record round-trips by value and the existing whole-record equality test keeps passing.
- `CanSwap` in the view model is a `get`-only property evaluated once at construction, because the answer cannot change while the process runs. `Environment.ProcessPath` is null only for a hosted runtime, and an empty string then makes `CanSwap` false, which is the safe direction.
- `UpdateClient` never throws from `CheckAsync` and always throws from `DownloadAsync` on failure. The Settings page catches exactly the three families it can report; anything else is a bug, not a state.
- `Task 5` touches `MainViewModel` and `Task 6` touches `SettingsPageViewModel`, and Task 5 calls `SettingsPage.RefreshUpdateRow()`. Task 5 therefore lands first with that call commented out or with a no-op `RefreshUpdateRow()` stub on the page, and Task 6 fills it in. Whichever the implementer chooses, the build must be green at the end of every task.
