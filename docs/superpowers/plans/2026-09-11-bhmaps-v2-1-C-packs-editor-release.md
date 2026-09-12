# BhMaps 2.1, part C: pack detail, the background editor, docs and release

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (or `superpowers:executing-plans`) to run this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Parts A and B of the 2.1 plan land before this one; every contract they hand over is listed under "What parts A and B already did" and must not be renamed.

**Goal:** Finish BhMaps 2.1 by rebuilding pack detail as one segmented grid with a map drawer (spec 5, items K1, K2, D4), rebuilding the background editor around the picture that was clicked, with map names instead of slot names and a live preview that keeps up with a drag (spec 7.2, items E1, E2, E3), then rewriting the manual and the README for the four-tab app, stamping version 2.1.0, and producing the release files (spec 12). The task list ends at "dist ready"; publishing the GitHub release is the orchestrator's job.

**Architecture:** `BhMaps.Core` gains two small imaging helpers: a working-source decode for the editor's preview and an image-dimension read for the drawer's file list. `BhMaps.App` gains `Services/Throttler.cs` beside `Services/Debouncer.cs` (a rate limit rather than a quiet period). `PackDetailViewModel` collapses its three tile collections into one list of one tile type chosen by a segment, adds zoom persisted as `packZoom`, and owns a `PackDrawerViewModel` built for one map. `BackgroundEditorViewModel` takes a `BackgroundEditorRequest` and a list of `MapSlotChoice`, decodes the source once into a 640x360 working bitmap and renders every preview from it on a 16 ms throttle. The theme gains one shared slider track template and an `EditorSlider` style. No new projects, no new NuGet packages.

**Tech Stack:** .NET 10 (`net10.0-windows`), C# 14, WPF with `ThemeMode="Dark"`, CommunityToolkit.Mvvm 8.4.2 (the only package in `BhMaps.App`), xunit 2.9.3 in `tests\BhMaps.Core.Tests`, `dotnet format` on `BhMaps.slnx`.

**Spec:** `docs\superpowers\specs\2026-09-11-bhmaps-v2-1-design.md`. Section numbers below ("spec 5", "spec 7.2", "spec 11") refer to it. Where this plan and the spec disagree, the spec wins, except for the decisions table below, which an implementer must not re-litigate.

---

## Global Constraints

Every task's requirements implicitly include this section.

**Safety, hard rules.**

- **Never run the app, a test, or a script against the real game folder** `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt`, and never write under `C:\Users\alexa\files\bh`.
- Every manual run uses the dev tree and all three overrides together. The dev tree for this session is
  `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`.
- **Every write into the game folder goes through `MainViewModel.RunGameWriteAsync`.** No view model calls `PackApplier`, `BackgroundApplier` or `File.Copy` against the game path on its own.

**Build and tooling.**

- Solution file is `BhMaps.slnx`, not `.sln`. Build: `dotnet build BhMaps.slnx -c Debug`. Tests: `dotnet test`. Filter: `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~<ClassName>"`.
- All projects set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. A warning fails the build.
- `<UseWPF>true</UseWPF>` removes `System.IO` from implicit usings; each `.csproj` carries `<Using Include="System.IO" />`. Never add a per-file `using System.IO;`.
- **`dotnet format BhMaps.slnx` is the only formatter this repo uses.** Run it before every commit and `dotnet format BhMaps.slnx --verify-no-changes` to check. csharpier must not be installed or run; the repo-root `.csharpierignore` exists only to switch off an external hook.
- `.editorconfig`: CRLF everywhere, four-space C#, two-space XAML, csproj, json and md.
- **No new NuGet packages.** CommunityToolkit.Mvvm 8.4.2 stays the only package in `BhMaps.App`.
- MVVM style: partial properties with `[ObservableProperty]`, `[RelayCommand]`, `partial void OnFooChanged(T value)` hooks, every view model `partial` and derived from `ObservableObject`.

**Copy.**

- **No em-dashes and no emoji** anywhere in the app, the docs or the commit messages.
- Copy strings come from spec section 11 verbatim. Where this plan quotes a string, that string is the one to use, character for character.

**Commits.** Conventional-commit subject, blank line, optional body, then these two trailer lines:

```
Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

Write the message to a file and use `git commit -F <file>` so the trailers stay on their own lines. Branch: `feature/bhmaps-v2.1`.

---

## What parts A and B already did (contracts, do not rename)

- `MainViewModel` pages: `Maps`, `Backgrounds`, `Packs`, `PackDetail`, `SettingsPage`. `Platforms` is gone as a page.
- `MainViewModel.RunGameWriteAsync(string label, IReadOnlyList<string> undoPaths, Func<IProgress<string>, CancellationToken, Task> work, string doneText, bool clearTicks = false) : Task<bool>`. **It appends the shared sentence itself**, so a `doneText` passed from part C never ends in a period and never mentions the match load.
- `MainViewModel.OpenBackgroundEditorAsync(BackgroundEditorRequest request)` and `record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot)` in namespace `BhMaps.App.ViewModels`. Part B calls it; part C implements it.
- `PackApplier.ApplyToMaps(Pack pack, IReadOnlyList<MapEntry> maps, string gamePath, IProgress<string>? progress, CancellationToken ct) : ApplyResult` and `static IReadOnlyList<string> ApplyToMapsPaths(Pack pack, IReadOnlyList<MapEntry> maps)`: the pack's files for each map's platform folder plus its background files for that map's slots, and nothing else.
- `AppSettings` has `MapsZoom`, `BackgroundsZoom`, `PackZoom` (keys `mapsZoom`, `backgroundsZoom`, `packZoom`), clamped by `SettingsStore`. `HomeZoom` and `WhileRunning` are gone.
- Theme style keys from part B: `SegmentControl`, `TileContextMenu`, `TileMenuItem`, `PanelTile`.
- `MainViewModel.SelectedMaps` and `SelectedMapCount` are still the ticked set.

---

## Decisions this plan makes where the spec left a choice open

An implementer must **not** re-litigate any of these.

| # | Question | Decision |
|---|---|---|
| C-D1 | Where the "already decoded source" API lives and what it is called | `BackgroundFitter.LoadWorkingSource(string sourcePath, int canvasWidth, int canvasHeight) : BitmapSource`. `Render(BitmapSource, FitOptions, int, int)` already exists and is the fit-from-a-decoded-source half; the new call is the decode half, scaled by the factor a Cover fit at that canvas would use, never above 1:1. |
| C-D2 | Where the throttler lives and how it is tested | `src\BhMaps.App\Services\Throttler.cs`, beside `Debouncer`. `tests\BhMaps.Core.Tests` gains a `ProjectReference` to `BhMaps.App` so the one class can be tested. That is the whole reason for the reference; no other App type gets a test. |
| C-D3 | One tile type or two in pack detail | One: `PackTileViewModel` carries an optional `MapEntry`, an optional `GameFile`, a caption and a preview. Three grids become three collections of that one type and one `DataTemplate`. |
| C-D4 | What the drawer opens on for a Backgrounds-segment tile | The map that owns the slot, resolved from the catalog; the first map in display-name order when several share it. A slot no map names opens a drawer with the file row and Open folder only, and no Apply. |
| C-D5 | Where the drawer's picture comes from | The Combined tile for the same map, bound through, so the composite is rendered once for the page and the drawer costs nothing. |
| C-D6 | What "remembered for the session" means for the segment | An instance property on `PackDetailViewModel`, which the shell constructs once and keeps for the life of the app. Nothing is written to settings. |
| C-D7 | Back button placement | `PageHeader` gains a `Leading` dependency property and a column before the title. Pack detail puts its back chevron there. |
| C-D8 | The editor's Map list when the catalog has no background slots | The list falls back to the game's own `Backgrounds` file names, each labelled with the file name. With neither, `SelectedMap` is null, Save is disabled and the Map row reads "No maps yet. Refresh the game data in Settings." |
| C-D9 | Disabled look of a slider | The shared track template carries no disabled trigger. `ZoomSliderStyle` gets `Opacity 0.4` as a style trigger; the editor's Pan rows carry `Opacity 0.45` on the whole row, so the two never multiply. |
| C-D10 | Editor fit property names | `Mode` stays `FitMode` (`Cover`, `Contain`, `Stretch`); the bindable booleans are `ModeFill`, `ModeFit`, `ModeStretch` and `IsFill`. Only the labels say Fill / Fit / Stretch. |

---

## File map

```
src\BhMaps.Core\Imaging\BackgroundFitter.cs             MODIFY  C1  LoadWorkingSource
src\BhMaps.Core\Imaging\ImageDimensions.cs              CREATE  C1  Read(path)
src\BhMaps.App\Services\Throttler.cs                    CREATE  C2
tests\BhMaps.Core.Tests\BhMaps.Core.Tests.csproj        MODIFY  C2  ProjectReference to BhMaps.App
src\BhMaps.App\Theme\Controls.xaml                      MODIFY  C3  SliderTrack, EditorSlider
src\BhMaps.App\ViewModels\BackgroundEditorViewModel.cs  MODIFY  C4  rewritten
src\BhMaps.App\ViewModels\BackgroundEditorRequest.cs    CREATE  C4  request record and MapSlotChoice
src\BhMaps.App\ViewModels\MainViewModel.cs              MODIFY  C4  OpenBackgroundEditorAsync
src\BhMaps.App\Views\BackgroundEditorWindow.xaml        MODIFY  C5  rewritten
src\BhMaps.App\Views\BackgroundEditorWindow.xaml.cs     MODIFY  C5  drag-completed handler
src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs  MODIFY  C6  rewritten
src\BhMaps.App\ViewModels\PackDrawerViewModel.cs        CREATE  C6
src\BhMaps.App\Views\Controls\PageHeader.xaml           MODIFY  C7  Leading slot
src\BhMaps.App\Views\Controls\PageHeader.xaml.cs        MODIFY  C7  Leading property
src\BhMaps.App\Views\Pages\PackDetailView.xaml          MODIFY  C7  rewritten
src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs       MODIFY  C7  tile click, Enter, Escape
docs\manual.md                                          MODIFY  C8
README.md                                               MODIFY  C8
src\BhMaps.App\BhMaps.App.csproj                        MODIFY  C8  Version 2.1.0
tests\BhMaps.Core.Tests\BackgroundFitterTests.cs        MODIFY  C1
tests\BhMaps.Core.Tests\ImageDimensionsTests.cs         CREATE  C1
tests\BhMaps.Core.Tests\ThrottlerTests.cs               CREATE  C2
```

---

## Task C1: a working source for the preview, and image dimensions

**Files:**
- Modify: `src\BhMaps.Core\Imaging\BackgroundFitter.cs` (insert after `LoadSource`, which is lines 24 to 31; the file is 103 lines today)
- Create: `src\BhMaps.Core\Imaging\ImageDimensions.cs`
- Test: `tests\BhMaps.Core.Tests\BackgroundFitterTests.cs` (append inside the class; it ends at line 154)
- Test: `tests\BhMaps.Core.Tests\ImageDimensionsTests.cs` (new)

**Interfaces:**

```csharp
// BhMaps.Core.Imaging.BackgroundFitter, additive. Render(BitmapSource, FitOptions, int, int) already exists.
public static BitmapSource LoadWorkingSource(string sourcePath, int canvasWidth, int canvasHeight);

// BhMaps.Core.Imaging
public static class ImageDimensions
{
    public static (int Width, int Height)? Read(string fullPath);
}
```

- [ ] **Step 1: write the failing tests.** Append these members to `BackgroundFitterTests`:

```csharp
    private static double MeanAbsoluteDifference(BitmapSource a, BitmapSource b)
    {
        Assert.Equal(a.PixelWidth, b.PixelWidth);
        Assert.Equal(a.PixelHeight, b.PixelHeight);
        var stride = a.PixelWidth * 4;
        var one = new byte[stride * a.PixelHeight];
        var two = new byte[stride * b.PixelHeight];
        new FormatConvertedBitmap(a, PixelFormats.Bgra32, null, 0).CopyPixels(one, stride, 0);
        new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0).CopyPixels(two, stride, 0);
        double sum = 0;
        var counted = 0;
        for (var i = 0; i < one.Length; i++)
        {
            if (i % 4 == 3)
            {
                continue;
            }

            sum += Math.Abs(one[i] - two[i]);
            counted++;
        }

        return sum / counted;
    }

    [Fact]
    public void LoadWorkingSource_ScalesDownToWhatTheCanvasNeedsAndNeverUp()
    {
        using var tmp = new TempDir();
        var big = SyntheticImage.SaveQuadrants(tmp.Sub("big.png"), 1600, 900);
        var small = SyntheticImage.SaveQuadrants(tmp.Sub("small.png"), 100, 50);

        var scaled = BackgroundFitter.LoadWorkingSource(big, 640, 360);
        var kept = BackgroundFitter.LoadWorkingSource(small, 640, 360);

        Assert.Equal(640, scaled.PixelWidth);
        Assert.Equal(360, scaled.PixelHeight);
        Assert.True(scaled.IsFrozen);
        Assert.Equal(100, kept.PixelWidth);
        Assert.Equal(50, kept.PixelHeight);
    }

    [Theory]
    [InlineData(FitMode.Cover, 0.25)]
    [InlineData(FitMode.Cover, 0.5)]
    [InlineData(FitMode.Contain, 0.5)]
    [InlineData(FitMode.Stretch, 0.5)]
    public void WorkingSourceFit_MatchesTheFullFitDownscaled(FitMode mode, double panX)
    {
        using var tmp = new TempDir();
        var src = SyntheticImage.SaveQuadrants(tmp.Sub("src.png"), 1600, 900);
        var options = new FitOptions(mode, PanX: panX, Darken: 0.2);

        var full = BackgroundFitter.Render(src, options, W, H);
        var reference = BackgroundFitter.Render(full, new FitOptions(FitMode.Stretch), 640, 360);
        var preview = BackgroundFitter.Render(BackgroundFitter.LoadWorkingSource(src, 640, 360), options, 640, 360);

        Assert.True(MeanAbsoluteDifference(reference, preview) < 6, "preview drifted from the full fit");
    }
```

Create `tests\BhMaps.Core.Tests\ImageDimensionsTests.cs`:

```csharp
using BhMaps.Core.Imaging;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class ImageDimensionsTests
{
    [Fact]
    public void Read_ReturnsThePixelSizeOfAnImage()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SaveQuadrants(tmp.Sub("shot.png"), 40, 20);

        Assert.Equal((40, 20), ImageDimensions.Read(path)!.Value);
    }

    [Fact]
    public void Read_ReturnsNullForAFileThatIsNotAnImage()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("notes.txt");
        File.WriteAllText(path, "not an image");

        Assert.Null(ImageDimensions.Read(path));
    }

    [Fact]
    public void Read_ReturnsNullForAMissingFile()
    {
        using var tmp = new TempDir();

        Assert.Null(ImageDimensions.Read(Path.Combine(tmp.Path, "gone.png")));
    }

    [Fact]
    public void Read_LeavesNoHandleOnTheFile()
    {
        using var tmp = new TempDir();
        var path = SyntheticImage.SaveQuadrants(tmp.Sub("shot.png"), 8, 8);

        ImageDimensions.Read(path);

        File.Delete(path);
        Assert.False(File.Exists(path));
    }
}
```

- [ ] **Step 2:** `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~ImageDimensionsTests"` -> compile error, `ImageDimensions` does not exist. Expected.
- [ ] **Step 3: implement.** In `BackgroundFitter.cs`, directly under `LoadSource`:

```csharp
    /// <summary>The source decoded no larger than a canvas of this size needs: the scale a Cover fit would use,
    /// which is the largest any mode asks for, and never above 1:1. The editor decodes once into this and renders
    /// every preview from it (spec 7.2), so a slider drag costs one draw instead of a decode plus a draw.</summary>
    public static BitmapSource LoadWorkingSource(string sourcePath, int canvasWidth, int canvasHeight)
    {
        var source = LoadSource(sourcePath);
        var scale = Math.Max((double)canvasWidth / source.PixelWidth, (double)canvasHeight / source.PixelHeight);
        if (scale >= 1.0)
        {
            return source;
        }

        // Drawn rather than transformed, so the downscale takes the same HighQuality path Render takes and the
        // preview cannot drift from the full-size fit by more than rounding.
        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var converted = new FormatConvertedBitmap(target, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }
```

Create `src\BhMaps.Core\Imaging\ImageDimensions.cs`:

```csharp
using System.Windows.Media.Imaging;

namespace BhMaps.Core.Imaging;

/// <summary>The pixel size of an image file, read from its header without decoding the pixels. Pack detail's
/// drawer shows it beside each file (spec 5).</summary>
public static class ImageDimensions
{
    /// <summary>Null for a missing, locked or undecodable file: a file row without a size is a fact about the
    /// file, never an error dialog.</summary>
    public static (int Width, int Height)? Read(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);

            // DelayCreation with no cache reads the header only; the frame is measured before the stream closes.
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 4: verify.** `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~ImageDimensionsTests"` -> 4 passed. `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~BackgroundFitterTests"` -> every earlier fitter test still passes, plus 5 new results (one fact and four theory cases).
- [ ] **Step 5:** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings, 0 errors. Then `dotnet format BhMaps.slnx` and `dotnet format BhMaps.slnx --verify-no-changes` -> no output.
- [ ] **Step 6: commit.**

```
git add src/BhMaps.Core/Imaging/BackgroundFitter.cs src/BhMaps.Core/Imaging/ImageDimensions.cs tests/BhMaps.Core.Tests/BackgroundFitterTests.cs tests/BhMaps.Core.Tests/ImageDimensionsTests.cs
git commit -F .git/COMMIT_C1.txt
```

with `.git/COMMIT_C1.txt` holding:

```
feat(core): decode a preview-sized working source and read image dimensions

LoadWorkingSource scales a source down to what a canvas needs, once, so the
editor's preview costs one draw per change instead of a decode plus a draw.
ImageDimensions reads a header for pack detail's file rows.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

---

## Task C2: the render throttle

**Files:**
- Create: `src\BhMaps.App\Services\Throttler.cs`
- Modify: `tests\BhMaps.Core.Tests\BhMaps.Core.Tests.csproj` (the `ItemGroup` at lines 20 to 22, which holds the `BhMaps.Core` reference)
- Test: `tests\BhMaps.Core.Tests\ThrottlerTests.cs` (new)

**Interfaces:**

```csharp
namespace BhMaps.App.Services;

public sealed class Throttler
{
    public static readonly TimeSpan DefaultInterval;   // 16 ms
    public Throttler();
    public Throttler(TimeSpan interval);
    public void Cancel();
    public async void Run(Func<CancellationToken, Task> work);
    public Task RunAsync(Func<CancellationToken, Task> work);
}
```

- [ ] **Step 1: write the failing tests.** Create `tests\BhMaps.Core.Tests\ThrottlerTests.cs`:

```csharp
using System.Diagnostics;
using BhMaps.App.Services;

namespace BhMaps.Core.Tests;

public class ThrottlerTests
{
    private static Throttler Fast() => new(TimeSpan.FromMilliseconds(10));

    [Fact]
    public async Task TheFirstCallRunsAtOnce()
    {
        var ran = false;

        await Fast().RunAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }

    [Fact]
    public async Task WhileOneIsInFlightOnlyTheNewestFollowerRuns()
    {
        var throttler = Fast();
        var log = new List<string>();
        var gate = new TaskCompletionSource();

        var first = throttler.RunAsync(async _ => { log.Add("a"); await gate.Task; });
        _ = throttler.RunAsync(_ => { log.Add("b"); return Task.CompletedTask; });
        _ = throttler.RunAsync(_ => { log.Add("c"); return Task.CompletedTask; });
        gate.SetResult();
        await first;

        Assert.Equal(new[] { "a", "c" }, log);
    }

    [Fact]
    public async Task CancelDropsWorkThatHasNotStartedAndCancelsTheTokenOfTheOneRunning()
    {
        var throttler = Fast();
        var log = new List<string>();
        var gate = new TaskCompletionSource();
        CancellationToken captured = default;

        var first = throttler.RunAsync(async ct => { captured = ct; log.Add("a"); await gate.Task; });
        _ = throttler.RunAsync(_ => { log.Add("b"); return Task.CompletedTask; });
        throttler.Cancel();
        gate.SetResult();
        await first;

        Assert.Equal(new[] { "a" }, log);
        Assert.True(captured.IsCancellationRequested);
    }

    [Fact]
    public async Task TwoCallsAreSpacedByTheInterval()
    {
        var throttler = new Throttler(TimeSpan.FromMilliseconds(60));
        var gate = new TaskCompletionSource();
        var watch = Stopwatch.StartNew();

        var first = throttler.RunAsync(async _ => await gate.Task);
        _ = throttler.RunAsync(_ => Task.CompletedTask);
        gate.SetResult();
        await first;

        Assert.True(watch.ElapsedMilliseconds >= 40, $"second render ran after {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task AFailureInTheWorkDoesNotWedgeTheThrottle()
    {
        var throttler = Fast();
        var ran = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => throttler.RunAsync(_ => throw new InvalidOperationException("boom")));
        await throttler.RunAsync(_ => { ran = true; return Task.CompletedTask; });

        Assert.True(ran);
    }
}
```

- [ ] **Step 2:** add the reference to `tests\BhMaps.Core.Tests\BhMaps.Core.Tests.csproj`, in the existing `ItemGroup` with the Core reference:

```xml
  <ItemGroup>
    <ProjectReference Include="..\..\src\BhMaps.Core\BhMaps.Core.csproj" />
    <!-- The WPF layer has no test project. Throttler is plain timing logic with no UI in it, and the editor's
         preview depends on it being right, so this one reference buys it a test (decision C-D2). -->
    <ProjectReference Include="..\..\src\BhMaps.App\BhMaps.App.csproj" />
  </ItemGroup>
```

- [ ] **Step 3:** `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~ThrottlerTests"` -> compile error, `Throttler` does not exist. Expected. If instead the *reference itself* fails to resolve (a WinExe reference the SDK refuses), stop and report: the fallback is a linked source file, `<Compile Include="..\..\src\BhMaps.App\Services\Throttler.cs" Link="Helpers\Throttler.cs" />` in the same csproj, with the same tests unchanged.

- [ ] **Step 4: implement** `src\BhMaps.App\Services\Throttler.cs`:

```csharp
namespace BhMaps.App.Services;

/// <summary>A rate limit rather than a quiet period (spec 7.2): the first call runs at once, calls that arrive
/// while one is in flight collapse into a single follow-up, and only the newest of those survives. One piece of
/// work runs at a time and two are never closer together than the interval. Debouncer is the other shape: it
/// waits for the typing to stop. Call from the UI thread; the work itself is free to go off it.</summary>
public sealed class Throttler
{
    /// <summary>One 60 Hz frame, the fastest a preview can usefully be redrawn.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(16);

    private readonly TimeSpan _interval;
    private Func<CancellationToken, Task>? _pending;
    private CancellationTokenSource? _cts;
    private bool _running;

    public Throttler()
        : this(DefaultInterval)
    {
    }

    public Throttler(TimeSpan interval) => _interval = interval;

    /// <summary>Drops the queued call and cancels the token the running one holds.</summary>
    public void Cancel()
    {
        _pending = null;
        _cts?.Cancel();
    }

    /// <summary>The fire-and-forget form, for a caller with no task to await. Deliberately async void, exactly as
    /// Debouncer.Run: a non-cancellation failure is posted back to the UI thread and reaches App's
    /// DispatcherUnhandledException handler instead of being swallowed as an unobserved task.</summary>
    public async void Run(Func<CancellationToken, Task> work) => await RunAsync(work);

    /// <summary>Completes when the queue this call joined has drained. Cancellation is swallowed; anything else
    /// the work throws comes back out to the caller, and the throttle is left usable.</summary>
    public async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _pending = work;
        if (_running)
        {
            // The loop below is still turning and will pick up whatever _pending holds by then.
            return;
        }

        _running = true;
        try
        {
            while (_pending is { } next)
            {
                _pending = null;
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                try
                {
                    await next(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer set of values.
                }

                // The floor between two runs, and the window a call arriving now is collapsed into.
                await Task.Delay(_interval);
            }
        }
        finally
        {
            _running = false;
        }
    }
}
```

- [ ] **Step 5: verify.** `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~ThrottlerTests"` -> 5 passed. `dotnet test` -> the whole suite passes. `dotnet build BhMaps.slnx -c Debug` -> 0 warnings. `dotnet format BhMaps.slnx --verify-no-changes` -> no output.
- [ ] **Step 6: commit.** `git add src/BhMaps.App/Services/Throttler.cs tests/BhMaps.Core.Tests/ThrottlerTests.cs tests/BhMaps.Core.Tests/BhMaps.Core.Tests.csproj` then `git commit -F .git/COMMIT_C2.txt` with subject `feat(app): a 16 ms render throttle beside the debouncer` and the two trailer lines.

---

## Task C3: one slider track, and the EditorSlider style

**Files:**
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (`ZoomSliderStyle` is lines 371 to 425 as part A left it; search for `x:Key="ZoomSliderStyle"`)

**Interfaces:** two new resource keys, `SliderTrack` (a `ControlTemplate` for `Slider`) and `EditorSlider` (a `Style` for `Slider`). `ZoomSliderStyle` keeps its key and its look.

- [ ] **Step 1: lift the template out.** Cut the whole `<ControlTemplate TargetType="Slider">` that is `ZoomSliderStyle`'s `Template` value and put it above the style as a keyed resource, dropping its `ControlTemplate.Triggers` block:

```xml
  <!-- The app's one slider: a 2 px Line2 groove with a 12 px Text thumb, no ticks drawn. Shared by the zoom
       sliders and by the editor's, so the two cannot drift. The disabled look is on each style rather than in
       here, because the editor dims a whole row at 45% and two opacities would multiply (decision C-D9). -->
  <ControlTemplate x:Key="SliderTrack" TargetType="Slider">
    <ControlTemplate.Resources>
      <!-- The two halves of the groove are invisible; the track line is drawn once, behind. -->
      <Style x:Key="GrooveHalf" TargetType="RepeatButton">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="RepeatButton">
              <Border Background="Transparent" />
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </ControlTemplate.Resources>
    <Grid Height="20" Background="Transparent">
      <Border Height="2" VerticalAlignment="Center" Background="{StaticResource Line2Brush}" CornerRadius="1" />
      <Track x:Name="PART_Track">
        <Track.DecreaseRepeatButton>
          <RepeatButton Command="Slider.DecreaseLarge" Style="{StaticResource GrooveHalf}" />
        </Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton>
          <RepeatButton Command="Slider.IncreaseLarge" Style="{StaticResource GrooveHalf}" />
        </Track.IncreaseRepeatButton>
        <Track.Thumb>
          <Thumb Width="12" Height="12" Focusable="False">
            <Thumb.Template>
              <ControlTemplate TargetType="Thumb">
                <Ellipse Fill="{StaticResource TextBrush}" />
              </ControlTemplate>
            </Thumb.Template>
          </Thumb>
        </Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
```

- [ ] **Step 2: point the zoom style at it** and give it back its disabled look as a style trigger:

```xml
  <Style x:Key="ZoomSliderStyle" TargetType="Slider">
    <Setter Property="Width" Value="96" />
    <Setter Property="Orientation" Value="Horizontal" />
    <Setter Property="IsSnapToTickEnabled" Value="True" />
    <Setter Property="TickFrequency" Value="1" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Template" Value="{StaticResource SliderTrack}" />
    <Style.Triggers>
      <Trigger Property="IsEnabled" Value="False">
        <Setter Property="Opacity" Value="0.4" />
      </Trigger>
    </Style.Triggers>
  </Style>
```

- [ ] **Step 3: add the editor style** directly after it. SmallChange and LargeChange are set on each slider, because pan is 0..1 and darken is 0..100 (spec 7.2):

```xml
  <!-- Spec 7.2: the editor's sliders. Move-to-point on, so a click on the groove jumps the thumb there instead
       of stepping; no snapping, because pan is continuous. The row around a disabled one carries the dimming. -->
  <Style x:Key="EditorSlider" TargetType="Slider">
    <Setter Property="Orientation" Value="Horizontal" />
    <Setter Property="IsMoveToPointEnabled" Value="True" />
    <Setter Property="IsSnapToTickEnabled" Value="False" />
    <Setter Property="VerticalAlignment" Value="Center" />
    <Setter Property="Template" Value="{StaticResource SliderTrack}" />
  </Style>
```

- [ ] **Step 4: verify.** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings. Launch the dev tree:

```
dotnet run --project src\BhMaps.App -- --game "<devtree>\game\mapArt" --library "<devtree>\lib" --appdata "<devtree>\appdata"
```

On Maps, the zoom slider still draws a 2 px groove with a round thumb and still snaps to whole columns; dragging it still changes the column count. Nothing else moved. Close the app.
- [ ] **Step 5: commit.** `git add src/BhMaps.App/Theme/Controls.xaml` then `git commit -F .git/COMMIT_C3.txt`, subject `refactor(theme): share one slider track and add the EditorSlider style`, with the two trailer lines.

---

## Task C4: the background editor's view model and the shell that opens it

The window markup still binds the old names after this task, so the dialog looks wrong until C5 lands. Do not stop between the two.

**Files:**
- Create: `src\BhMaps.App\ViewModels\BackgroundEditorRequest.cs` (only if part B did not already add the record; if it exists, add `MapSlotChoice` to it and leave the request alone)
- Modify: `src\BhMaps.App\ViewModels\BackgroundEditorViewModel.cs` (rewritten, 259 lines today)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`OpenBackgroundEditorAsync`, lines 471 to 510 today)

**Interfaces:**

```csharp
namespace BhMaps.App.ViewModels;

public sealed record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot);

/// <summary>One entry of the editor's Map combo: the slot the file is written as, and every map that uses it.</summary>
public sealed record MapSlotChoice(string Slot, string DisplayNames);

public sealed record BackgroundSave(string PackFile, string Slot, string MapNames, bool ApplyToGame);

public partial class BackgroundEditorViewModel : ObservableObject
{
    public BackgroundEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<MapSlotChoice> maps,
        IReadOnlyList<string> packNames,
        BackgroundEditorRequest request);

    public event Action<bool>? CloseRequested;
    public void AcceptDroppedFile(string path);
    public void RenderFinal();
}
```

- [ ] **Step 1: the records.** `MapSlotChoice` and `BackgroundEditorRequest` live in `src\BhMaps.App\ViewModels\BackgroundEditorRequest.cs`:

```csharp
namespace BhMaps.App.ViewModels;

/// <summary>What a tile's Edit hands the editor: the picture that was clicked, the pack it lives in when it is in
/// one, and the slot it fills when the tile knows it. Every field but the path may be absent, because a custom
/// picture belongs to no pack and no map (spec 7.2).</summary>
public sealed record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot);

/// <summary>One line of the editor's Map combo: the file name the game expects, and the maps that share it,
/// joined with ", ". The slot is never the label; the user picks a map (spec 7.2).</summary>
public sealed record MapSlotChoice(string Slot, string DisplayNames);
```

- [ ] **Step 2: rewrite the view model.** `src\BhMaps.App\ViewModels\BackgroundEditorViewModel.cs`, whole file:

```csharp
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What one Save left in the library: the fitted picture, the slot it was fitted for, the maps that slot
/// belongs to, and whether the user asked for it to go into the game as well. The shell does that part, so the
/// editor never writes into the game folder.</summary>
public sealed record BackgroundSave(string PackFile, string Slot, string MapNames, bool ApplyToGame);

/// <summary>Spec 7.2: one picture, fitted to one map's background slot. The source is decoded once into a
/// 640x360 working bitmap and every preview is drawn from it on a 16 ms throttle, so a slider drag moves the
/// picture rather than queueing decodes.</summary>
public partial class BackgroundEditorViewModel : ObservableObject
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 360;
    public const string NewPackChoice = "New pack...";
    public const string DefaultPackName = "My Backgrounds";
    public const string NoSourceText = "No picture yet. Drop one here or browse.";
    public const string NoMapsText = "No maps yet. Refresh the game data in Settings.";
    public const string PreviewSizeText = "preview 640 x 360";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly BackgroundEditorRequest _request;
    private readonly Throttler _preview = new();

    private BitmapSource? _working;
    private string _workingPath = "";

    public BackgroundEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<MapSlotChoice> maps,
        IReadOnlyList<string> packNames,
        BackgroundEditorRequest request)
    {
        _services = services;
        _dialogs = dialogs;
        _request = request;
        Maps = maps;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        SelectedMap = maps.FirstOrDefault(m => m.Slot.Equals(request.Slot, StringComparison.OrdinalIgnoreCase))
            ?? maps.FirstOrDefault();
        Error = "";
        SourceDetail = "";
        PanX = 0.5;
        PanY = 0.5;
        ApplyNow = true;

        // The tile's own pack, so Save replaces the picture the user was looking at; anything else keeps it.
        var requested = packNames.FirstOrDefault(p => p.Equals(request.PackName, StringComparison.OrdinalIgnoreCase));
        var existingDefault = packNames.FirstOrDefault(p => p.Equals(DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = requested ?? existingDefault ?? NewPackChoice;
        NewPackName = TargetPack == NewPackChoice ? DefaultPackName : "";

        // Last: the change hook reads everything above it.
        SourcePath = request.SourcePath;
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MapSlotChoice> Maps { get; }

    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public BackgroundSave? Saved { get; private set; }
```

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint), nameof(MapLabel))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial MapSlotChoice? SelectedMap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasSource), nameof(Title), nameof(SourceFileName), nameof(EmptyText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial ImageSource? SourceThumbnail { get; set; }

    /// <summary>"flowermap, 1920 x 1080" under the source's file name, or "" while it is unknown.</summary>
    [ObservableProperty]
    public partial string SourceDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFill), nameof(ModeFill), nameof(ModeFit), nameof(ModeStretch))]
    public partial FitMode Mode { get; set; }

    [ObservableProperty]
    public partial double PanX { get; set; }

    [ObservableProperty]
    public partial double PanY { get; set; }

    [ObservableProperty]
    public partial double DarkenPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyNow { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    public string Title => SourceFileName.Length == 0 ? "Edit background" : $"Edit {SourceFileName}";

    public string SourceFileName => SourcePath.Length == 0 ? "" : Path.GetFileName(SourcePath);

    public bool HasSource => File.Exists(SourcePath);

    public bool HasMaps => Maps.Count > 0;

    /// <summary>The Map row's own note: empty while there are maps to pick from (decision C-D8).</summary>
    public string MapLabel => HasMaps ? "" : NoMapsText;

    /// <summary>What the preview says instead of a picture: nothing chosen yet, or a source that has gone.</summary>
    public string EmptyText => SourcePath.Length == 0
        ? NoSourceText
        : HasSource ? "" : $"{SourceFileName} is no longer in {_request.PackName ?? "the library"}. Choose another source.";

    public bool IsFill => Mode == FitMode.Cover;

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public string Slot => SelectedMap?.Slot ?? "";

    /// <summary>Spec 7.2, shown only when Save would replace a file that is already in the pack.</summary>
    public string OverwriteHint =>
        Slot.Length > 0 && !IsNewPack && File.Exists(PackFilePath())
            ? $"Replaces {Slot} in {TargetPack}. Pick another pack to keep the original."
            : "";

    public bool ModeFill
    {
        get => Mode == FitMode.Cover;
        set { if (value) { Mode = FitMode.Cover; } }
    }

    public bool ModeFit
    {
        get => Mode == FitMode.Contain;
        set { if (value) { Mode = FitMode.Contain; } }
    }

    public bool ModeStretch
    {
        get => Mode == FitMode.Stretch;
        set { if (value) { Mode = FitMode.Stretch; } }
    }

    public bool CanSave => HasSource && Slot.Length > 0 && PackNameValidator.IsValid(EffectivePackName, out _);

    private FitOptions Options => new(Mode, PanX, PanY, DarkenPercent / 100.0);

    public void AcceptDroppedFile(string path) => SourcePath = path;

    /// <summary>The render a drag ends with: the throttle can only have dropped values that are stale by now, so
    /// the newest ones are drawn with nothing queued behind them (spec 7.2).</summary>
    public void RenderFinal()
    {
        _preview.Cancel();
        SchedulePreview();
    }

    partial void OnSourcePathChanged(string value)
    {
        _working = null;
        _workingPath = "";
        LoadSourceInfo(value);
        SchedulePreview();
    }

    partial void OnModeChanged(FitMode value) => SchedulePreview();

    partial void OnPanXChanged(double value) => SchedulePreview();

    partial void OnPanYChanged(double value) => SchedulePreview();

    partial void OnDarkenPercentChanged(double value) => SchedulePreview();

    [RelayCommand]
    private void Replace()
    {
        if (_dialogs.PickImageFile("Choose a source image") is { } file)
        {
            SourcePath = file;
        }
    }

    [RelayCommand]
    private void ResetPanX() => PanX = 0.5;

    [RelayCommand]
    private void ResetPanY() => PanY = 0.5;

    [RelayCommand]
    private void ResetDarken() => DarkenPercent = 0;
```

```csharp
    /// <summary>Saves the fitted picture into the pack and nothing else; the "Apply to game now" box is the
    /// shell's business, because a game write needs the boundary, the snapshot and the undo (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packFile = PackFilePath();
        var slot = Slot;
        if (File.Exists(packFile)
            && !_dialogs.Confirm("Replace background?", $"{slot} already exists in pack {EffectivePackName}. Replace it?"))
        {
            return;
        }

        var path = SourcePath;
        var options = Options;
        try
        {
            // The original, not the working bitmap: Save fits at 2048x1151 (spec 7.2).
            var bytes = await Task.Run(() => BackgroundFitter.Fit(path, options));
            Directory.CreateDirectory(Path.GetDirectoryName(packFile)!);
            await File.WriteAllBytesAsync(packFile, bytes);
            Saved = new BackgroundSave(packFile, slot, SelectedMap?.DisplayNames ?? slot, ApplyNow);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the background", ex.Message);
        }
    }

    private string PackFilePath() =>
        Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName, BackgroundsFolder, Slot);

    /// <summary>The Source row: a thumbnail, and the pack and pixel size under the file name. Both are file work,
    /// so both arrive late and neither blocks the preview.</summary>
    private async void LoadSourceInfo(string path)
    {
        SourceThumbnail = null;
        SourceDetail = "";
        if (!File.Exists(path))
        {
            return;
        }

        var pack = PackOfPath(path);
        var thumbnail = await Task.Run(() => ThumbnailProvider.Decode(path));
        var size = await Task.Run(() => ImageDimensions.Read(path));
        if (!string.Equals(path, SourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SourceThumbnail = thumbnail;
        SourceDetail = size is { } s ? $"{pack}, {s.Width} x {s.Height}" : pack;
    }

    /// <summary>The pack a path sits in, when it sits under the library's packs root.</summary>
    private string PackOfPath(string path)
    {
        var root = PackScanner.PacksRoot(_services.LibraryPath);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return _request.PackName ?? "Not in a pack";
        }

        var rest = full[(Path.GetFullPath(root).Length + 1)..];
        var cut = rest.IndexOf(Path.DirectorySeparatorChar);
        return cut < 0 ? rest : rest[..cut];
    }

    /// <summary>Spec 7.2: 16 ms throttle, newest values win, rendered off the UI thread from a working bitmap that
    /// is decoded once per source. A source that is not there clears the preview at once.</summary>
    private void SchedulePreview()
    {
        var path = SourcePath;
        var options = Options;
        if (!File.Exists(path))
        {
            _preview.Cancel();
            Preview = null;
            return;
        }

        _preview.Run(ct => RenderAsync(path, options, ct));
    }

    private async Task RenderAsync(string path, FitOptions options, CancellationToken ct)
    {
        try
        {
            if (_working is null || !_workingPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                var loaded = await Task.Run(() => BackgroundFitter.LoadWorkingSource(path, PreviewWidth, PreviewHeight), ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _working = loaded;
                _workingPath = path;
            }

            var source = _working;
            var bitmap = await Task.Run(() => BackgroundFitter.Render(source, options, PreviewWidth, PreviewHeight), ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Preview = bitmap;
            Error = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                Preview = null;
                Error = "Could not read the image: " + ex.Message;
            }
        }
    }
}
```

- [ ] **Step 3: the shell.** Replace `MainViewModel.OpenBackgroundEditorAsync` (lines 471 to 510) with:

```csharp
    /// <summary>Spec 7.2: the editor fits a picture and saves it into a pack, which is a library write of its own.
    /// The "Apply to game now" it offers is a game write, so the pack file it left is copied into the slot here,
    /// with the boundary, the snapshot and the undo every other write gets.</summary>
    public async Task OpenBackgroundEditorAsync(BackgroundEditorRequest request)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var vm = new BackgroundEditorViewModel(
            Services, Dialogs, MapSlotChoices(snapshot), snapshot.Packs.Select(p => p.Name).ToList(), request);
        var window = new BackgroundEditorWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        if (vm.Saved is not { ApplyToGame: true } saved)
        {
            await RescanAsync();
            return;
        }

        var gamePath = Services.GamePath;
        var source = saved.PackFile;
        var slot = saved.Slot;
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {Path.GetFileName(source)}",
            BackgroundApplier.TargetPaths([slot]),
            (_, ct) => Task.Run(
                () => failures.AddRange(BackgroundApplier.Apply(source, gamePath, [slot], null, ct).Failures),
                ct),
            $"{Path.GetFileName(source)} applied to {saved.MapNames}");

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }

    /// <summary>One entry per background slot the maps name, labelled with every map that shares it (spec 7.2),
    /// then any slot the game folder has that no map names, labelled with its own file name (decision C-D8).</summary>
    private static IReadOnlyList<MapSlotChoice> MapSlotChoices(ScanSnapshot snapshot)
    {
        var bySlot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in snapshot.Catalog.Maps)
        {
            foreach (var slot in map.BackgroundSlots)
            {
                if (!bySlot.TryGetValue(slot, out var names))
                {
                    names = [];
                    bySlot[slot] = names;
                }

                if (!names.Contains(map.DisplayName))
                {
                    names.Add(map.DisplayName);
                }
            }
        }

        var choices = bySlot
            .Select(pair => new MapSlotChoice(pair.Key, string.Join(", ", pair.Value)))
            .OrderBy(c => c.DisplayNames, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in snapshot.Tree.FindFolder("Backgrounds")?.Files ?? Array.Empty<GameFile>())
        {
            if (!bySlot.ContainsKey(file.Name))
            {
                choices.Add(new MapSlotChoice(file.Name, file.Name));
            }
        }

        return choices;
    }
```

- [ ] **Step 4: verify.** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings, 0 errors. Every caller of the editor (part B's tile menus) still compiles because the signature is the contract they were written against. `dotnet test` -> green. `dotnet format BhMaps.slnx --verify-no-changes` -> no output.
- [ ] **Step 5: commit.** `git add src/BhMaps.App/ViewModels/BackgroundEditorRequest.cs src/BhMaps.App/ViewModels/BackgroundEditorViewModel.cs src/BhMaps.App/ViewModels/MainViewModel.cs` then `git commit -F .git/COMMIT_C4.txt`, subject `feat(app): the background editor works in maps, fill words and a working bitmap`, with the two trailer lines.

---

## Task C5: the background editor window

**Files:**
- Modify: `src\BhMaps.App\Views\BackgroundEditorWindow.xaml` (rewritten, 180 lines today)
- Modify: `src\BhMaps.App\Views\BackgroundEditorWindow.xaml.cs` (add one handler; the file is 42 lines today)

**Interfaces:** consumes `BackgroundEditorViewModel` exactly as C4 left it. Produces no new public API.

- [ ] **Step 1: the window shell and its resources.** Keep the class, the drag handlers and the `Window.Resources` styles `ErrorText` and `HintText` as they are; change the `Title` attribute and add three styles. The opening tag becomes:

```xml
<Window x:Class="BhMaps.App.Views.BackgroundEditorWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"
        xmlns:converters="clr-namespace:BhMaps.App.Converters"
        xmlns:vm="clr-namespace:BhMaps.App.ViewModels"
        Title="{Binding Title}" Width="1040" Height="720" MinWidth="860" MinHeight="600"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        AllowDrop="True" PreviewDragOver="Window_PreviewDragOver" PreviewDrop="Window_PreviewDrop"
        Background="{StaticResource BgBrush}">
```

New resources beside the existing ones (`FieldLabel` keeps its 0,18,0,6 margin):

```xml
    <!-- The Reset in a slider's label: a word, not a button, so the label reads as one line. -->
    <Style x:Key="ResetLink" TargetType="Button" BasedOn="{StaticResource PlainButton}">
      <Setter Property="Padding" Value="6,0" />
      <Setter Property="FontSize" Value="11" />
      <Setter Property="Foreground" Value="{StaticResource Text3Brush}" />
    </Style>

    <!-- Pan applies in Fill only; in the other two modes the whole row sits at 45% (spec 7.2). -->
    <Style x:Key="PanRow" TargetType="Grid">
      <Setter Property="Opacity" Value="1" />
      <Style.Triggers>
        <DataTrigger Binding="{Binding IsFill}" Value="False">
          <Setter Property="Opacity" Value="0.45" />
        </DataTrigger>
      </Style.Triggers>
    </Style>

    <Style x:Key="Readout" TargetType="TextBlock" BasedOn="{StaticResource MonoText}">
      <Setter Property="MinWidth" Value="40" />
      <Setter Property="TextAlignment" Value="Right" />
      <Setter Property="VerticalAlignment" Value="Center" />
    </Style>
```

- [ ] **Step 2: the preview cell.** Replace the `Border` at `Grid.Row="0" Grid.Column="0"` (lines 51 to 66 today) with:

```xml
    <Border Grid.Row="0" Grid.Column="0" Style="{StaticResource CardBorder}">
      <Grid>
        <Viewbox Margin="16" Stretch="Uniform">
          <Border Width="640" Height="360" Background="{StaticResource BgBrush}">
            <Image RenderOptions.BitmapScalingMode="HighQuality" Source="{Binding Preview}" Stretch="Fill" />
          </Border>
        </Viewbox>

        <!-- Spec 7.2: nothing chosen yet, or a source that has gone since the scan. Browse is in line with it,
             because with no picture there is nothing else on this half of the window to act on. -->
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
                    Visibility="{Binding Preview, Converter={StaticResource NullToVis}, ConverterParameter=Inverse}">
          <TextBlock HorizontalAlignment="Center" MaxWidth="420" Foreground="{StaticResource Text3Brush}"
                     Text="{Binding EmptyText}" TextAlignment="Center" TextWrapping="Wrap" />
          <Button Margin="0,12,0,0" HorizontalAlignment="Center" Command="{Binding ReplaceCommand}" Content="Browse"
                  FocusVisualStyle="{StaticResource DialogFocusRing}" Style="{StaticResource OutlineButton}" />
        </StackPanel>

        <TextBlock Margin="0,0,22,22" HorizontalAlignment="Right" VerticalAlignment="Bottom" FontSize="11"
                   Foreground="{StaticResource Text3Brush}"
                   Text="{x:Static vm:BackgroundEditorViewModel.PreviewSizeText}" />
      </Grid>
    </Border>
```

- [ ] **Step 3: the panel.** Replace the contents of the `StackPanel` inside the right `Border`'s `ScrollViewer` (lines 77 to 152 today) with the blocks below, in this order. The `Border`, its `Width="{StaticResource PanelWidth}"` and the `ScrollViewer` around it are unchanged.

```xml
          <TextBlock Style="{StaticResource FieldLabel}" Text="Source" />
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto" />
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Border Width="96" Height="54" Background="{StaticResource TileBrush}"
                    CornerRadius="{StaticResource Radius}">
              <Border CornerRadius="{StaticResource Radius}">
                <Border.Background>
                  <ImageBrush ImageSource="{Binding SourceThumbnail}" Stretch="UniformToFill" />
                </Border.Background>
              </Border>
            </Border>
            <StackPanel Grid.Column="1" Margin="10,0" VerticalAlignment="Center">
              <TextBlock Text="{Binding SourceFileName}" TextTrimming="CharacterEllipsis" />
              <TextBlock Margin="0,3,0,0" Style="{StaticResource HintText}" Text="{Binding SourceDetail}" />
            </StackPanel>
            <Button Grid.Column="2" VerticalAlignment="Center" Command="{Binding ReplaceCommand}" Content="Replace"
                    FocusVisualStyle="{StaticResource DialogFocusRing}" Style="{StaticResource OutlineButton}" />
          </Grid>
          <TextBlock Style="{StaticResource HintText}" Text="Or drop an image anywhere on this window." />

          <TextBlock Style="{StaticResource FieldLabel}" Text="Map" />
          <ComboBox AutomationProperties.Name="Map"
                    DisplayMemberPath="DisplayNames"
                    IsEnabled="{Binding HasMaps}"
                    ItemsSource="{Binding Maps}"
                    SelectedItem="{Binding SelectedMap}" />
          <TextBlock Style="{StaticResource ErrorText}" Text="{Binding MapLabel}" />

          <TextBlock Style="{StaticResource FieldLabel}" Text="Fit" />
          <StackPanel Orientation="Horizontal">
            <RadioButton MinWidth="0" Margin="0,0,20,0" Content="Fill" GroupName="Fit"
                         FocusVisualStyle="{StaticResource DialogFocusRing}" IsChecked="{Binding ModeFill}" />
            <RadioButton MinWidth="0" Margin="0,0,20,0" Content="Fit" GroupName="Fit"
                         FocusVisualStyle="{StaticResource DialogFocusRing}" IsChecked="{Binding ModeFit}" />
            <RadioButton MinWidth="0" Content="Stretch" GroupName="Fit"
                         FocusVisualStyle="{StaticResource DialogFocusRing}" IsChecked="{Binding ModeStretch}" />
          </StackPanel>
```

The Pan X block, then Pan Y and Darken as described under it:

```xml
          <Grid Style="{StaticResource PanRow}">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto" />
              <RowDefinition Height="Auto" />
            </Grid.RowDefinitions>
            <StackPanel Orientation="Horizontal">
              <TextBlock Style="{StaticResource FieldLabel}" Text="Pan X" />
              <Button Margin="0,18,0,6" Command="{Binding ResetPanXCommand}" Content="Reset"
                      Style="{StaticResource ResetLink}" />
            </StackPanel>
            <Slider Grid.Row="1" AutomationProperties.Name="Pan X"
                    IsEnabled="{Binding IsFill}"
                    LargeChange="0.1" Maximum="1" Minimum="0" SmallChange="0.01"
                    Style="{StaticResource EditorSlider}"
                    Thumb.DragCompleted="Slider_DragCompleted"
                    Value="{Binding PanX}" />
            <TextBlock Grid.Row="1" Grid.Column="1" Margin="10,0,0,0" Style="{StaticResource Readout}"
                       Text="{Binding PanX, StringFormat=N2}" />
          </Grid>
```

Pan Y is the same `Grid` with the label "Pan Y", `ResetPanYCommand` and `{Binding PanY}` on both the slider and the readout. Darken is the same `Grid` without `Style="{StaticResource PanRow}"` and without the slider's `IsEnabled`, with the label "Darken", `ResetDarkenCommand`, `LargeChange="10" Maximum="100" Minimum="0" SmallChange="1"`, `Value="{Binding DarkenPercent}"`, and a readout of `Text="{Binding DarkenPercent, StringFormat=F0}"` with a second `TextBlock` holding a literal `%` beside it, so no format string needs escaping.

```xml
          <TextBlock Style="{StaticResource FieldLabel}" Text="Save into pack" />
          <ComboBox AutomationProperties.Name="Save into pack"
                    ItemsSource="{Binding PackChoices}"
                    SelectedItem="{Binding TargetPack}" />
          <TextBox Margin="0,8,0,0" AutomationProperties.Name="New pack name"
                   Style="{StaticResource FieldTextBox}"
                   Text="{Binding NewPackName, UpdateSourceTrigger=PropertyChanged}"
                   Visibility="{Binding IsNewPack, Converter={StaticResource BoolToVis}}" />
          <TextBlock Style="{StaticResource HintText}" Text="{Binding OverwriteHint}" />

          <TextBlock Margin="0,18,0,0" Style="{StaticResource ErrorText}" Text="{Binding Error}" />
```

- [ ] **Step 4:** the footer (lines 156 to 178 today) is unchanged: the "Apply to game now" checkbox, Cancel (IsCancel), Save (IsDefault, bound to `SaveCommand`).
- [ ] **Step 5: the final render.** Add to `BackgroundEditorWindow.xaml.cs`:

```csharp
    /// <summary>The throttle draws from the working bitmap while the thumb moves; the drag ending is when the
    /// newest values are worth one more draw with nothing queued behind it (spec 7.2).</summary>
    private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (DataContext is BackgroundEditorViewModel vm)
        {
            vm.RenderFinal();
        }
    }
```

- [ ] **Step 6: verify.** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings. Launch the dev tree:

```
dotnet run --project src\BhMaps.App -- --game "<devtree>\game\mapArt" --library "<devtree>\lib" --appdata "<devtree>\appdata"
```

Open Backgrounds, a tile's menu, Edit. Check: the title reads `Edit <that file name>`; the Source row shows a thumbnail, the file name and "pack, W x H"; the Map combo lists map names and no `BG_*.jpg`; the fit words are Fill, Fit, Stretch; dragging Pan X moves the picture while the thumb moves (spec 12) and the readout counts with it; in Fit both pan rows are dimmed and take no input; Reset puts a slider back; "preview 640 x 360" sits at the bottom right of the preview; Save into pack starts on the tile's own pack and the hint names the file it would replace; Cancel closes with nothing written. Then open the editor with a path that does not exist (delete the picture from the library while the app is open, then Edit it) and confirm the preview says it is no longer there and offers Browse.
- [ ] **Step 7: commit.** `git add src/BhMaps.App/Views/BackgroundEditorWindow.xaml src/BhMaps.App/Views/BackgroundEditorWindow.xaml.cs` then `git commit -F .git/COMMIT_C5.txt`, subject `feat(app): the background editor window, source row, sliders and live preview`.

---

## Task C6: pack detail as one grid, and the drawer's view model

**Files:**
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs` (391 lines today)
- Create: `src\BhMaps.App\ViewModels\PackDrawerViewModel.cs`

**Kept verbatim from the file as it stands:** `ApplyAllAsync`, `OpenFolder`, `BackToPacksCommand`, `RemoveTransparentAsync`, `Delete`, `TransparentLine`, `RemoveQuestion`, `FindPack`, `SetPack`, `OnPackChanged`, `LoadTransparent`, `ComposeAsync`, `LoadPreview`, `OpenInExplorer`, `MapsIn`, `TransparentText`, `HasTransparent`, the `Pack` property with its attributes, `Title`, `HasPack`, `EmptyText`. Everything else in the file is replaced.

**Interfaces:**

```csharp
// ViewModels/Pages/PackDetailViewModel.cs
public const string CombinedSegment = "Combined";
public const string BackgroundsSegment = "Backgrounds";
public const string PlatformsSegment = "Platforms";
public const int MinZoom = 2;
public const int MaxZoom = 10;
public IReadOnlyList<string> Segments { get; }                       // the three above, in order
public partial string Segment { get; set; }                          // observable, defaults to Combined
public bool IsCombined { get; set; }                                 // and IsBackgrounds, IsPlatforms
public partial int Zoom { get; set; }                                // 2..10, persisted as packZoom
public ObservableCollection<PackTileViewModel> Combined { get; }      // was PutTogether
public ObservableCollection<PackTileViewModel> Backgrounds { get; }
public ObservableCollection<PackTileViewModel> Platforms { get; }
public IEnumerable<PackTileViewModel> Items { get; }                 // the collection the segment picks
public partial PackTileViewModel? SelectedTile { get; set; }
public partial PackDrawerViewModel? Drawer { get; set; }
public void Open(PackTileViewModel? tile);
public void OpenSelected();
public void CloseDrawer();

public partial class PackTileViewModel : ObservableObject
{
    public PackTileViewModel(string key, string caption, MapEntry? map, GameFile? file,
        int composeWidth, int composeHeight, bool dropBackground);
    public string Key { get; }            // map folder name, or the background file name
    public string Caption { get; }
    public MapEntry? Map { get; }
    public GameFile? File { get; }
    public bool IsFileTile { get; }       // Map is null: the caption is a file name, drawn in Geist Mono
    public int ComposeWidth { get; }
    public int ComposeHeight { get; }
    public bool DropBackground { get; }
    public partial ImageSource? Preview { get; set; }
}
```

- [ ] **Step 1: the segment, the zoom and the items.** In `PackDetailViewModel`, replace the three old collections and add:

```csharp
    public PackDetailViewModel(MainViewModel shell)
        : base(shell)
    {
        Combined = [];
        Backgrounds = [];
        Platforms = [];
        Segments = [CombinedSegment, BackgroundsSegment, PlatformsSegment];
        Segment = CombinedSegment;
        TransparentText = "";
        Zoom = Math.Clamp(shell.Services.Settings.PackZoom, MinZoom, MaxZoom);
    }

    /// <summary>The three-way segment of spec 5. An instance property, and the shell builds this page once, so
    /// the choice lasts the session and nothing is written to settings (decision C-D6).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items), nameof(IsCombined), nameof(IsBackgrounds), nameof(IsPlatforms), nameof(EmptyNote))]
    public partial string Segment { get; set; }

    /// <summary>Columns in the grid, MinZoom to MaxZoom, persisted as packZoom.</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    [ObservableProperty]
    public partial PackTileViewModel? SelectedTile { get; set; }

    /// <summary>The drawer on the right, or null while none is open (spec 5).</summary>
    [ObservableProperty]
    public partial PackDrawerViewModel? Drawer { get; set; }

    public IReadOnlyList<string> Segments { get; }

    public IEnumerable<PackTileViewModel> Items => Segment switch
    {
        BackgroundsSegment => Backgrounds,
        PlatformsSegment => Platforms,
        _ => Combined,
    };

    /// <summary>What the grid says when the segment has nothing in it.</summary>
    public string EmptyNote => Segment == BackgroundsSegment ? NoBackgroundsText : NoMapsText;

    public bool IsCombined
    {
        get => Segment == CombinedSegment;
        set { if (value) { Segment = CombinedSegment; } }
    }
```

`IsBackgrounds` and `IsPlatforms` are the same three lines against `BackgroundsSegment` and `PlatformsSegment`. They are what the markup binds, because part B's `SegmentControl` is a `ToggleButton` style rather than a list. `Segments` stays as the list of the three names so the copy lives in one place.

- [ ] **Step 2: the change hooks, opening and closing.**

```csharp
    partial void OnZoomChanged(int value)
    {
        if (Shell.Services.Settings.PackZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { PackZoom = value });
        }
    }

    /// <summary>A tile that is gone from the grid cannot keep a drawer open behind it, so the segment closes it.</summary>
    partial void OnSegmentChanged(string value)
    {
        SelectedTile = null;
        CloseDrawer();
    }

    /// <summary>Spec 5: a click, or Enter on the focused tile, opens the drawer. A background tile whose slot no
    /// map names opens a drawer with the file and Open folder and no Apply (decision C-D4).</summary>
    public void Open(PackTileViewModel? tile)
    {
        if (tile is null || _snapshot is not { } snapshot || Pack is not { } pack)
        {
            return;
        }

        Drawer?.Cancel();
        SelectedTile = tile;
        _openKey = tile.Key;
        var preview = tile.Map is { } map
            ? Combined.FirstOrDefault(t => t.Key.Equals(map.FolderName, StringComparison.OrdinalIgnoreCase)) ?? tile
            : tile;
        var status = tile.Map is { } m && snapshot.MapStatuses.TryGetValue(m.FolderName, out var found) ? found : null;
        var drawer = new PackDrawerViewModel(Shell, this, pack, tile.Map, status, preview);
        Drawer = drawer;
        _ = drawer.LoadAsync();
    }

    public void OpenSelected() => Open(SelectedTile);

    public void CloseDrawer()
    {
        Drawer?.Cancel();
        Drawer = null;
        _openKey = null;
    }
```

`_openKey` is a `private string?` field beside `_snapshot`.

- [ ] **Step 3: one tile type, one rebuild.** Replace `Rebuild` and `Load`:

```csharp
    private void Rebuild()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var reopen = _openKey;
        CloseDrawer();
        SelectedTile = null;
        Combined.Clear();
        Backgrounds.Clear();
        Platforms.Clear();
        _transparentFiles = Array.Empty<string>();
        TransparentText = "";
        if (_snapshot is not { } snapshot || Pack is not { } pack)
        {
            return;
        }

        foreach (var map in MapsIn(snapshot.Catalog, pack))
        {
            Combined.Add(new PackTileViewModel(
                map.FolderName, map.DisplayName, map, null, MapCompositor.CardWidth, MapCompositor.CardHeight, false));
            Platforms.Add(new PackTileViewModel(
                map.FolderName, map.DisplayName, map, null, PlatformWidth, PlatformHeight, true));
        }

        // .jpg only: the game's backgrounds are all JPEGs, so a PNG in the folder fills no slot and belongs in the
        // transparent-files line instead. The caption is the map the slot belongs to, not the file name (spec 5).
        foreach (var file in pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (!Path.GetExtension(file.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owner = MapForSlot(snapshot.Catalog, file.Name);
            Backgrounds.Add(new PackTileViewModel(
                file.Name, owner?.DisplayName ?? file.Name, owner, file, MapCompositor.CardWidth, MapCompositor.CardHeight, false));
        }

        OnPropertyChanged(nameof(Items));
        Load([.. Combined], [.. Backgrounds], [.. Platforms], pack, _cts.Token);
        LoadTransparent(pack, _cts.Token);

        // A rescan follows every write, so the drawer that started the write comes back on the new snapshot.
        if (reopen is not null)
        {
            Open(Items.FirstOrDefault(t => t.Key.Equals(reopen, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>The first map, in display-name order, whose levels name this background slot (decision C-D4).</summary>
    private static MapEntry? MapForSlot(MapCatalog catalog, string slot) =>
        catalog.Maps.FirstOrDefault(m => m.BackgroundSlots.Any(s => s.Equals(slot, StringComparison.OrdinalIgnoreCase)));
```

`Load` keeps its shape and its `try`/`catch (OperationCanceledException)`; it now takes three `IReadOnlyList<PackTileViewModel>` and fills them in view order, using `ComposeAsync(tile, pack, ct)` for a tile whose `Map` is not null and `Shell.Services.Thumbnails.GetAsync(tile.File.FullPath, tile.File.MtimeTicks, ct)` for a tile whose `File` is not null. `ComposeAsync` changes only in its parameter type (`PackTileViewModel`) and reads `tile.Map!` behind the same null check.

- [ ] **Step 4: the tile.** Replace `PackMapTileViewModel` and `PackBackgroundTileViewModel` at the bottom of the file with one type:

```csharp
/// <summary>One tile of pack detail's single grid: a map the pack touches, drawn put together or with the
/// background dropped, or one of the pack's own background pictures (decision C-D3).</summary>
public partial class PackTileViewModel : ObservableObject
{
    public PackTileViewModel(
        string key, string caption, MapEntry? map, GameFile? file, int composeWidth, int composeHeight, bool dropBackground)
    {
        Key = key;
        Caption = caption;
        Map = map;
        File = file;
        ComposeWidth = composeWidth;
        ComposeHeight = composeHeight;
        DropBackground = dropBackground;
    }

    /// <summary>The map's folder name, or the background's file name. What a reopen after a rescan matches on.</summary>
    public string Key { get; }

    public string Caption { get; }

    public MapEntry? Map { get; }

    public GameFile? File { get; }

    /// <summary>No map owns this picture, so the caption is a file name and the markup draws it in Geist Mono.</summary>
    public bool IsFileTile => Map is null;

    public int ComposeWidth { get; }

    public int ComposeHeight { get; }

    public bool DropBackground { get; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }
}
```

- [ ] **Step 5: the drawer.** Create `src\BhMaps.App\ViewModels\PackDrawerViewModel.cs`. It is the pack-detail twin of `MapPanelViewModel` and follows it: an `ObservableObject`, a `CancellationTokenSource` for its loads, `LoadAsync` filling the file rows, `Cancel` stopping them.

```csharp
/// <summary>One row of the drawer's file list: what the pack holds for this map, with its size on disk and its
/// pixel size (spec 5). Dimensions arrive from LoadAsync, so the list appears at once.</summary>
public partial class PackFileRowViewModel : ObservableObject
{
    public PackFileRowViewModel(GameFile file) { File = file; Dimensions = ""; }

    public GameFile File { get; }

    public string Name => File.Name;

    /// <summary>"842 KB". Whole units; a background is never small enough for a decimal to matter.</summary>
    public string SizeText => File.Size >= 1024 * 1024
        ? $"{File.Size / 1024.0 / 1024.0:0.0} MB"
        : File.Size >= 1024 ? $"{File.Size / 1024} KB" : $"{File.Size} B";

    [ObservableProperty]
    public partial string Dimensions { get; set; }
}

public partial class PackDrawerViewModel : ObservableObject
{
    public PackDrawerViewModel(
        MainViewModel shell, PackDetailViewModel page, Pack pack, MapEntry? map, MapStatus? status, PackTileViewModel preview);

    /// <summary>The map's name, or the file name when no map owns the picture.</summary>
    public string DisplayName { get; }

    /// <summary>The level sets the map is in, labelled as the chips label them, or "".</summary>
    public string SetsText { get; }

    /// <summary>"In game: flowermap background, Default platforms" territory: "In game: " and the map's status
    /// text, or "In game: nothing uses this picture" when no map owns the slot.</summary>
    public string InGameText { get; }

    /// <summary>The tile whose composite the drawer shows, bound through so nothing renders twice (C-D5).</summary>
    public PackTileViewModel Preview { get; }

    public IReadOnlyList<PackFileRowViewModel> Files { get; }

    public bool CanApply { get; }          // false when Map is null
    public Task LoadAsync();
    public void Cancel();
}
```

The four method bodies that are not mechanical:

```csharp
    // In the constructor: the pack's files for this map's folder, then its backgrounds for this map's slots.
    private static IReadOnlyList<GameFile> FilesFor(Pack pack, MapEntry? map, GameFile? file)
    {
        if (map is null)
        {
            return file is null ? Array.Empty<GameFile>() : [file];
        }

        var folder = pack.FindFolder(map.FolderName)?.Files ?? Array.Empty<GameFile>();
        var backgrounds = pack.FindFolder("Backgrounds")?.Files ?? Array.Empty<GameFile>();
        var slots = backgrounds.Where(f => map.BackgroundSlots.Any(s => s.Equals(f.Name, StringComparison.OrdinalIgnoreCase)));
        return [.. folder, .. slots];
    }

    /// <summary>Reads each row's pixel size off the UI thread, in view order.</summary>
    public async Task LoadAsync()
    {
        var ct = _loads.Token;
        try
        {
            foreach (var row in Files)
            {
                ct.ThrowIfCancellationRequested();
                var size = await Task.Run(() => ImageDimensions.Read(row.File.FullPath), ct);
                row.Dimensions = size is { } s ? $"{s.Width} x {s.Height}" : "";
            }
        }
        catch (OperationCanceledException)
        {
            // The drawer was replaced or closed.
        }
    }

    /// <summary>Spec 5: this pack, this map, one click. One map, so no confirm; the write is the shell's, so the
    /// undo snapshot, the busy boundary and the done sentence come with it (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyToThisMapAsync()
    {
        if (_map is not { } map)
        {
            return;
        }

        var gamePath = _shell.Services.GamePath;
        var pack = _pack;
        ApplyResult? result = null;
        await _shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            PackApplier.ApplyToMapsPaths(pack, [map]),
            (progress, ct) => Task.Run(() => { result = PackApplier.ApplyToMaps(pack, [map], gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {map.DisplayName}");

        if (result is not null)
        {
            _shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>The pack's own folder for this map, so the drawer opens what it is describing.</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var path = _map is { } map && _pack.FindFolder(map.FolderName) is { } folder ? folder.FullPath : _pack.FullPath;
        if (ExplorerLauncher.Open(path) is { } error)
        {
            _shell.Dialogs.Error("Could not open the folder", error);
        }
    }

    [RelayCommand]
    private void Close() => _page.CloseDrawer();
```

`InGameText` is `$"In game: {status?.Text ?? "Default"}"` for a map, and `"In game: nothing uses this picture"` when `map` is null. `SetsText` is `string.Join(", ", map.Sets.Select(MapCatalog.LabelFor))`. `DisplayName` is `map?.DisplayName ?? preview.Caption`. `CanApply` is `map is not null`.

- [ ] **Step 6: verify.** `dotnet build BhMaps.slnx -c Debug` -> the only errors are in `PackDetailView.xaml`'s bindings, which are runtime, so the build is 0 errors and 0 warnings. `dotnet test` -> green. `dotnet format BhMaps.slnx --verify-no-changes` -> no output. The page still renders the old markup against the old names until C7, so a dev-tree run here is expected to show an empty pack detail page; do not stop between C6 and C7.
- [ ] **Step 7: commit.** `git add src/BhMaps.App/ViewModels/Pages/PackDetailViewModel.cs src/BhMaps.App/ViewModels/PackDrawerViewModel.cs` then `git commit -F .git/COMMIT_C6.txt`, subject `feat(app): pack detail as one segmented grid with a map drawer`.

---

## Task C7: pack detail's markup

**Files:**
- Modify: `src\BhMaps.App\Views\Controls\PageHeader.xaml` and `.xaml.cs` (add `Leading`)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml` (rewritten, 209 lines today)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs` (8 lines today)

- [ ] **Step 1: a leading slot in the header.** In `PageHeader.xaml.cs`, beside `TitleProperty`:

```csharp
    /// <summary>Anything the page wants before its title, such as pack detail's back button (spec 5).</summary>
    public static readonly DependencyProperty LeadingProperty =
        DependencyProperty.Register(nameof(Leading), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public object? Leading
    {
        get => GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }
```

In `PageHeader.xaml`, add a fourth `ColumnDefinition Width="Auto"` as the first one, put a presenter in it, and shift the title to column 1, the shell line to column 2 and the actions to column 3:

```xml
      <ColumnDefinition Width="Auto" />
      ...
    <ContentPresenter Grid.Column="0" Margin="0,0,4,0" VerticalAlignment="Center"
                      Content="{Binding Leading, ElementName=Root}" />
```

Every other page passes no `Leading`, so its presenter measures to zero and nothing moves.

- [ ] **Step 2: the page.** `PackDetailView.xaml`, whole file. `UserControl.Resources` keeps `BoolToVis` and adds `NullToVis` (`converters:NullToVisibilityConverter`) and the tile template; the three old `DataTemplate`s and the `SectionNote` style go.

```xml
  <UserControl.InputBindings>
    <KeyBinding Key="Escape" Command="{Binding CloseDrawerCommand}" />
  </UserControl.InputBindings>

  <DockPanel Margin="24,16,24,24">
    <controls:PageHeader DockPanel.Dock="Top" Title="{Binding Title}">
      <controls:PageHeader.Leading>
        <Button AutomationProperties.Name="Back" Command="{Binding BackToPacksCommand}"
                controls:Icon.Glyph="{StaticResource Icon.ChevronLeft}" Style="{StaticResource PlainButton}"
                ToolTip="Back to packs" />
      </controls:PageHeader.Leading>
      <controls:PageHeader.Actions>
        <StackPanel IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}"
                    Orientation="Horizontal">
          <Border Margin="0,0,12,0" Padding="2" Background="{StaticResource Surface2Brush}"
                  CornerRadius="{StaticResource Radius}">
            <StackPanel Orientation="Horizontal">
              <ToggleButton Content="Combined" IsChecked="{Binding IsCombined}"
                            Style="{StaticResource SegmentControl}" />
              <ToggleButton Content="Backgrounds" IsChecked="{Binding IsBackgrounds}"
                            Style="{StaticResource SegmentControl}" />
              <ToggleButton Content="Platforms" IsChecked="{Binding IsPlatforms}"
                            Style="{StaticResource SegmentControl}" />
            </StackPanel>
          </Border>
          <controls:ZoomSlider Margin="0,0,12,0" VerticalAlignment="Center"
                               Maximum="{x:Static pages:PackDetailViewModel.MaxZoom}"
                               Minimum="{x:Static pages:PackDetailViewModel.MinZoom}"
                               Value="{Binding Zoom}" />
          <Button Command="{Binding ApplyAllCommand}" Content="Apply all"
                  controls:Icon.Glyph="{StaticResource Icon.Download}"
                  IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                  Style="{StaticResource PrimaryButton}" />
          <Button Margin="8,0,0,0" Command="{Binding OpenFolderCommand}" Content="Open folder"
                  controls:Icon.Glyph="{StaticResource Icon.FolderOpen}" Style="{StaticResource OutlineButton}" />
        </StackPanel>
      </controls:PageHeader.Actions>
    </controls:PageHeader>
```

Part B's `SegmentControl` is a style for one `ToggleButton`, hosted in a Surface2 `Border` of padding 2 with the segments side by side, which is the shape written above and the same shape the map panel uses. Unchecking the active segment is a no-op: the setter ignores `false` and the property change puts the button back.

The body: the transparent-files note above the grid in every view (spec 5), the grid, then the drawer.

```xml
    <Grid IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}"
          Visibility="{Binding HasPack, Converter={StaticResource BoolToVis}}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
      </Grid.ColumnDefinitions>

      <DockPanel>
        <Border DockPanel.Dock="Top" Margin="0,4,0,12" Padding="14,10" Style="{StaticResource CardBorder}"
                Visibility="{Binding HasTransparent, Converter={StaticResource BoolToVis}}">
          <Grid>
            <TextBlock VerticalAlignment="Center" Text="{Binding TransparentText}" />
            <Button HorizontalAlignment="Right" Command="{Binding RemoveTransparentCommand}" Content="Remove"
                    controls:Icon.Glyph="{StaticResource Icon.Trash}" Style="{StaticResource OutlineButton}" />
          </Grid>
        </Border>

        <Grid>
          <ListBox x:Name="Tiles" ItemsSource="{Binding Items}" SelectedItem="{Binding SelectedTile}"
                   SelectionMode="Single" Style="{StaticResource TileListBox}"
                   PreviewKeyDown="Tiles_PreviewKeyDown"
                   ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                   ScrollViewer.VerticalScrollBarVisibility="Auto">
            <ListBox.ItemContainerStyle>
              <!-- The theme's ring, plus one click to open: a plain click selects and opens the drawer, and the
                   keyboard opens with Enter, which the code-behind handles (spec 5). -->
              <Style TargetType="ListBoxItem" BasedOn="{StaticResource TileListBoxItem}">
                <Setter Property="AutomationProperties.Name" Value="{Binding Caption}" />
                <Setter Property="Margin" Value="0,0,12,12" />
                <EventSetter Event="PreviewMouseLeftButtonUp" Handler="Tile_Click" />
              </Style>
            </ListBox.ItemContainerStyle>
            <ListBox.ItemsPanel>
              <ItemsPanelTemplate>
                <UniformGrid Columns="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=ListBox}}" />
              </ItemsPanelTemplate>
            </ListBox.ItemsPanel>
            <ListBox.ItemTemplate>
              <DataTemplate>
                <StackPanel>
                  <Viewbox Stretch="Uniform">
                    <Grid Width="{Binding Source={x:Static imaging:MapCompositor.CardWidth}, Mode=OneTime}"
                          Height="{Binding Source={x:Static imaging:MapCompositor.CardHeight}, Mode=OneTime}"
                          Background="{StaticResource TileBrush}">
                      <Image RenderOptions.BitmapScalingMode="HighQuality" Source="{Binding Preview}"
                             Stretch="UniformToFill" />
                    </Grid>
                  </Viewbox>
                  <TextBlock Margin="2,8,2,0" Text="{Binding Caption}" TextTrimming="CharacterEllipsis">
                    <TextBlock.Style>
                      <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                        <Style.Triggers>
                          <DataTrigger Binding="{Binding IsFileTile}" Value="True">
                            <Setter Property="FontFamily" Value="{StaticResource Mono}" />
                            <Setter Property="FontSize" Value="12" />
                            <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
                          </DataTrigger>
                        </Style.Triggers>
                      </Style>
                    </TextBlock.Style>
                  </TextBlock>
                </StackPanel>
              </DataTemplate>
            </ListBox.ItemTemplate>
          </ListBox>

          <TextBlock HorizontalAlignment="Center" VerticalAlignment="Center" Foreground="{StaticResource Text2Brush}"
                     Text="{Binding EmptyNote}">
            <TextBlock.Style>
              <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding Items.Count, ElementName=Tiles}" Value="0">
                    <Setter Property="Visibility" Value="Visible" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </TextBlock.Style>
          </TextBlock>
        </Grid>
      </DockPanel>
```

The empty-note trigger binds `Items.Count` on the `ListBox` itself, which follows whichever collection the segment is showing. The root `UserControl` needs three xmlns prefixes: `controls` and `pages` (already there), plus `converters="clr-namespace:BhMaps.App.Converters"` and `imaging="clr-namespace:BhMaps.Core.Imaging;assembly=BhMaps.Core"`.

The drawer, the same 360 px shape as the map panel (spec 5), declared after the grid so Tab reaches the tiles first:

```xml
      <Border Grid.Column="1" Width="{StaticResource PanelWidth}" Margin="16,0,0,0"
              AutomationProperties.Name="{Binding Drawer.DisplayName}" DataContext="{Binding Drawer}"
              Style="{StaticResource CardBorder}"
              Visibility="{Binding DataContext.Drawer, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullToVis}}">
        <Grid>
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
          </Grid.RowDefinitions>

          <Grid Margin="16,12,8,4">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*" />
              <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <StackPanel VerticalAlignment="Center">
              <TextBlock FontSize="16" FontWeight="SemiBold" Text="{Binding DisplayName}"
                         TextTrimming="CharacterEllipsis" />
              <TextBlock Margin="0,2,0,0" FontSize="12" Foreground="{StaticResource Text3Brush}"
                         Text="{Binding SetsText}" TextTrimming="CharacterEllipsis" />
            </StackPanel>
            <Button Grid.Column="1" VerticalAlignment="Top" AutomationProperties.Name="Close"
                    Command="{Binding CloseCommand}" Foreground="{StaticResource Text2Brush}" Padding="8"
                    Style="{StaticResource PlainButton}" ToolTip="Close">
              <controls:Icon Geometry="{StaticResource Icon.X}" Size="16" />
            </Button>
          </Grid>

          <ScrollViewer Grid.Row="1" Margin="16,4,4,0" HorizontalScrollBarVisibility="Disabled"
                        VerticalScrollBarVisibility="Auto">
            <StackPanel Margin="0,0,12,16">
              <Viewbox Stretch="Uniform">
                <Grid Width="{Binding Source={x:Static imaging:MapCompositor.CardWidth}, Mode=OneTime}"
                      Height="{Binding Source={x:Static imaging:MapCompositor.CardHeight}, Mode=OneTime}"
                      Background="{StaticResource TileBrush}">
                  <Image RenderOptions.BitmapScalingMode="HighQuality" Source="{Binding Preview.Preview}"
                         Stretch="UniformToFill" />
                </Grid>
              </Viewbox>
              <TextBlock Margin="0,10,0,0" Foreground="{StaticResource Text2Brush}" Text="{Binding InGameText}"
                         TextWrapping="Wrap" />
              <TextBlock Margin="0,16,0,6" Style="{StaticResource SectionHeaderText}" Text="Files in this pack" />
              <ItemsControl ItemsSource="{Binding Files}">
                <ItemsControl.ItemTemplate>
                  <DataTemplate>
                    <Grid Margin="0,0,0,6">
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                      </Grid.ColumnDefinitions>
                      <TextBlock Style="{StaticResource MonoText}" Text="{Binding Name}"
                                 TextTrimming="CharacterEllipsis" />
                      <StackPanel Grid.Column="1" Margin="10,0,0,0" Orientation="Horizontal">
                        <TextBlock FontSize="11" Foreground="{StaticResource Text3Brush}" Text="{Binding SizeText}" />
                        <TextBlock Margin="8,0,0,0" FontSize="11" Foreground="{StaticResource Text3Brush}"
                                   Text="{Binding Dimensions}" />
                      </StackPanel>
                    </Grid>
                  </DataTemplate>
                </ItemsControl.ItemTemplate>
              </ItemsControl>
            </StackPanel>
          </ScrollViewer>

          <StackPanel Grid.Row="2" Margin="16,8,16,14" Orientation="Horizontal">
            <Button Command="{Binding ApplyToThisMapCommand}" Content="Apply to this map"
                    controls:Icon.Glyph="{StaticResource Icon.Download}"
                    IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{StaticResource PrimaryButton}" />
            <Button Margin="8,0,0,0" Command="{Binding OpenFolderCommand}" Content="Open folder"
                    controls:Icon.Glyph="{StaticResource Icon.FolderOpen}" Style="{StaticResource PlainButton}" />
          </StackPanel>
        </Grid>
      </Border>
    </Grid>
```

The "No pack is open." `TextBlock` that ends the file today stays exactly as it is, outside this `Grid`, bound to `EmptyText` and shown when `HasPack` is false.

- [ ] **Step 3: the code-behind.** `PackDetailView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Pages;

public partial class PackDetailView : UserControl
{
    public PackDetailView()
    {
        InitializeComponent();
    }

    private PackDetailViewModel? Page => DataContext as PackDetailViewModel;

    /// <summary>Spec 5: a click on a tile opens the drawer. Handled on the container rather than through the
    /// ListBox's selection, so arrowing through the grid moves focus without opening anything.</summary>
    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile })
        {
            Page?.Open(tile);
        }
    }

    private void Tiles_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Page?.OpenSelected();
            e.Handled = true;
        }
    }
}
```

Escape is the `KeyBinding` on the `UserControl` from step 2, so it closes the drawer wherever focus is inside the page.

- [ ] **Step 4: verify.** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings, 0 errors. Launch the dev tree and open Packs, then a pack. Check, against the spec:
  - the header reads back chevron, pack name, Combined | Backgrounds | Platforms, the zoom slider, Apply all, Open folder;
  - Combined is selected on arrival, the grid is five columns, and the zoom slider takes it to 2 and to 10;
  - switching to Backgrounds shows the pack's pictures captioned with map names, and to Platforms the same maps with the background dropped;
  - leaving the page and coming back keeps the segment, and restarting the app keeps the zoom;
  - a click on a tile opens a 360 px drawer with the picture, "In game: ...", the sets line, the pack's files for that map with a size and a W x H, Apply to this map, Open folder;
  - Tab into the grid, arrow to another tile, Enter opens it; Escape closes the drawer;
  - Apply to this map writes, the header's done line reads "<pack> applied to <map>. Shows ..." and Undo appears beside it; the drawer stays open and its "In game" line has changed;
  - the transparent-files note, on a pack that has one, sits above the grid in all three segments.
  - Capture the page with `. <scratchpad>\cap\CapLib.ps1` and `Save-Window` if a screenshot is wanted for the record.
- [ ] **Step 5: commit.** `git add src/BhMaps.App/Views/Controls/PageHeader.xaml src/BhMaps.App/Views/Controls/PageHeader.xaml.cs src/BhMaps.App/Views/Pages/PackDetailView.xaml src/BhMaps.App/Views/Pages/PackDetailView.xaml.cs` then `git commit -F .git/COMMIT_C7.txt`, subject `feat(app): pack detail header, segment, one grid and the drawer`.

---

## Task C8: the manual, the README and the version

**Files:**
- Modify: `docs\manual.md` (258 lines today)
- Modify: `README.md` (54 lines today)
- Modify: `src\BhMaps.App\BhMaps.App.csproj` (line 12)

- [ ] **Step 1: version.** `<Version>2.0.0</Version>` becomes `<Version>2.1.0</Version>`.

- [ ] **Step 2: `docs\manual.md`.** Keep the opening paragraph, "Where things live" (change the settings-file sentence to "the two paths, the three zoom levels, and whether the welcome window has been finished"), "How it works" and "Known limitations", minus the limitation about writing while the game runs. Replace the "Pages" section with one that describes the app as it now is:

  - The **top bar**: BhMaps, then Maps, Backgrounds, Packs, Settings; Ctrl+1 to Ctrl+4, Ctrl+K for the Maps search; the game line on the right with Launch; F5 rescans, Cancel in the header stops a long operation.
  - **Maps**: the grid, the chips (All, the set chips, Changed, Ticked), "Select all shown", zoom 2 to 10, the card tag rules, ticking with click, Ctrl+click, Shift+click, Space and Ctrl+A, the selection bar and what each of its buttons does, the map panel with its Background | Platforms segment and the tile menus, "Reset this map" and "Reset all to default".
  - **Backgrounds**: the custom-pictures section, one tile per picture by content, the per-pack sections, search across file, pack and map names, "Add Custom Image", the tile menus.
  - **Packs** and **pack detail**: the list, and the three-segment grid with its drawer.
  - **Settings**: the rows as spec 6 lists them, including the one line about applying, and no "while the game runs" row.
  - The windows: **Welcome**, **Add Custom Image** with its four "Then" choices, **Import folder**, and the **background editor** as spec 7.2 describes it.
  - Writes: every change is written straight into the game folder with the game open or closed and shows on the next match load; the done line and Undo; multi-map writes confirm with a count.

  The "Development" section keeps its commands and its dev-tree rules and gains one line: the tests now reference `BhMaps.App` for `Throttler`, so `BhMaps.Core` is still where the logic and the tests are, apart from that one class.

- [ ] **Step 3: `README.md`.** Update the download table to `bhmaps-v2.1.0-win-x64.exe` and `bhmaps-v2.1.0-win-x64-dotnet.zip`, drop the "Restart and apply" sentence from "First run" (changes are written straight in and show on the next match load), and rewrite "What it does" for the four tabs: Maps as the grid and the map panel, Backgrounds as the picture library with Add Custom Image, Packs as import, apply, export and capture defaults, the editor, ticks and the selection bar for group applies, and Undo.

- [ ] **Step 4: the old strings must be gone.** From the repo root, in PowerShell:

```powershell
$files = Get-ChildItem -Recurse -File -Include *.cs,*.xaml,*.md,*.ps1 `
  -Path src,docs,tests,scripts,README.md |
  Where-Object { $_.FullName -notmatch '\(bin|obj|\.claude)\' }
$files | Select-String -SimpleMatch -Pattern 'Put together','Add pictures','Restart and apply','Apply live','Use platform set'
$files | Select-String -Pattern 'HomeViewModel|HomeView\b|homeZoom|NavigateHome|PlatformsViewModel|PlatformsView\b|whileRunning'
$files | Select-String -SimpleMatch -Pattern 'Content="Use"','>Use<','"Home"'
```

Each of the three must print nothing. `docs\superpowers\` is excluded on purpose: the spec and the plans quote the old strings while explaining what replaced them, and rewriting history in them would be a lie. If a hit is in a part A or part B file, fix it there rather than working around it.

- [ ] **Step 5: verify.** `dotnet build BhMaps.slnx -c Debug` -> 0 warnings. Read `docs\manual.md` and `README.md` once for em-dashes and emoji: `Select-String -Pattern '[\u2014\u2013]' docs\manual.md,README.md` prints nothing.
- [ ] **Step 6: commit.** `git add docs/manual.md README.md src/BhMaps.App/BhMaps.App.csproj` then `git commit -F .git/COMMIT_C8.txt`, subject `docs: manual and README for the four-tab app, version 2.1.0`.

---

## Task C9: the release gate

**Files:** none changed, unless `dotnet format` finds something.

- [ ] **Step 1: build.** `dotnet build BhMaps.slnx -c Debug` -> `Build succeeded`, 0 warnings, 0 errors. Then `dotnet build BhMaps.slnx -c Release` -> the same.
- [ ] **Step 2: tests.** `dotnet test` -> every test passes, and the count is at least the 2.0.0 count plus the 9 added in C1 and C2. Record the number.
- [ ] **Step 3: format.** `dotnet format BhMaps.slnx --verify-no-changes` -> exit code 0 and no output. If it rewrites anything, run `dotnet format BhMaps.slnx`, re-run the build and the tests, and commit the result as `chore: formatting for 2.1.0` with the two trailer lines.
- [ ] **Step 4: the string gate.** Run the three `Select-String` commands from C8 step 4 again. All three print nothing.
- [ ] **Step 5: the owner's checks (spec 12), against the dev tree only.** Launch with the three overrides. Confirm a slider drag in the editor moves the preview, and that ten columns on Maps fit the screen. The live-write check with Brawlhalla open is the owner's to do on their own machine; do not start the game from here.
- [ ] **Step 6: publish.** `pwsh -File scripts\publish.ps1`. It reads `<Version>` from the csproj, so it must print `Publishing BhMaps 2.1.0` and end with two lines naming the files and their sizes. Expect:

```
dist\bhmaps-v2.1.0-win-x64.exe          about 135 MB
dist\bhmaps-v2.1.0-win-x64-dotnet.zip   about 0.7 MB
```

- [ ] **Step 7: report.** Record, for the orchestrator: the two dist file names and their exact sizes, the test count, and the commit range for part C (`git log --oneline main..HEAD`). **Stop here.** Tagging, `gh release create` and uploading the two files are the orchestrator's, not this plan's.

---

## Risks and the two windows where the app is half-built

- **C4 to C5, and C6 to C7.** In each pair the view model lands first and its markup second. The app builds and runs after either commit, but between them the editor window (C4) and the pack detail page (C6) bind to names that no longer exist and show empty controls. Neither pair may be left unfinished at the end of a session.
- **`SegmentControl` is a `ToggleButton` style**, read out of part B's plan rather than guessed, hosted in a Surface2 border. The three `Is...` properties on the view model are the binding surface whatever shape it turns out to have.
- **The test project's reference to `BhMaps.App`** is the one thing here that changes the repo's shape (the manual has said the WPF layer has no tests). C2 step 3 names the fallback if the SDK refuses a `WinExe` reference: link the single source file instead.
- **`PackApplier.ApplyToMaps` is part A's.** C6 consumes it and never reimplements it. If it is missing when C6 starts, stop and say so rather than writing a second copy of the rule about which files a map takes from a pack.
- **`RunGameWriteAsync` appends the sentence.** Every `doneText` in this part is a fragment with no trailing period. If a done line ever reads "... applied to Brawlhaven.. Shows on the next match load.", the fragment has a period in it.
- **Undo paths.** The drawer's apply passes `PackApplier.ApplyToMapsPaths`, which must cover the background slots as well as the platform folder. A snapshot that misses the slots makes Undo leave the new background in place, which is a silent partial restore, not an error.
- **Preview cost at zoom 10.** Tiles are composed at 640x360 whatever the column count, exactly as the Maps grid does. A pack that touches 67 maps renders 67 composites on first open, one at a time on the single render thread, cached on disk afterwards. If that is visibly slow on the dev tree, say so; do not change the render size on your own.
- **Not verified while planning:** that a `ProjectReference` from `BhMaps.Core.Tests` to the WinExe `BhMaps.App` resolves on this machine (C2 step 3 checks it), what part B actually ships under `PanelTile`, `TileContextMenu` and `TileMenuItem` (part C uses none of the three, by design), and that part A's `PackApplier.ApplyToMaps` lands with the signature its own plan states.
