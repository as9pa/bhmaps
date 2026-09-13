# BhMaps 2.4 Part A Implementation Plan: preview focus in the platform editor

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Part A of 2.4 (spec 3.1 to 3.5): the platform editor preview gains two chips, All pieces and Ticked only; Ticked only ghosts the unticked pieces at 15 percent and frames the ticked ones with 12 percent padding; clicking a file's name in the Files list ticks only that file (Ctrl+click adds it); the map panel row's Edit opens the editor already in Ticked only; and the mode is remembered across sessions in one new settings key.

**Architecture:** All the drawing work is one optional parameter each on two existing Core statics. `MapCompositor.Render` gains a focus set of relative asset paths and a ghost opacity, and `DrawAsset` pushes that opacity for any asset outside the set; `PlatformBounds.For` gains an overload that unions only the focused assets, so the existing padding and aspect logic is reused untouched. Every current caller passes nothing and gets today's output byte for byte. The editor's view model decides the focus set on the UI thread just before each render: not isolating, no pieces, or every piece ticked all render as today (null focus, null viewport); isolating with nothing ticked renders an empty focus set, which ghosts everything, over the full camera plus a scrim line; anything else renders the ticked paths with the framed viewport. The setting is one more positional field on the `AppSettings` record and one more key in `SettingsStore`. The window adds a chip ListBox over the preview using the existing `ChipRow` container style and wraps the Files row thumbnail and name in a hoverable, focusable Border with mouse and key bindings. Tasks are ordered so the solution builds and tests green after every commit: Core, then settings, then the view model, then the window.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2, xUnit (tests\BhMaps.Core.Tests, net10.0-windows), `dotnet format BhMaps.slnx`.

**Spec:** `docs\superpowers\specs\2026-09-12-bhmaps-v2-4-design.md` (binding). Sections are cited as "spec N" below. Only Part A (spec 3.1 to 3.5) is in this plan; Part B (spec 4) is a separate plan.

**Branch:** `feature/bhmaps-v2.4`, already checked out. Version stays 2.3.1 here; the bump to 2.4.0 belongs to the release task of the Part B plan.

## Global Constraints

Copied from spec 5, plus the build rules this run needs.

- Game writes go only through `MainViewModel.RunGameWriteAsync` and the existing apply paths; no new writer. Part A writes nothing to the game folder at all.
- Never run the app or tests against the real game folder, the real library or the real `%APPDATA%\BhMaps`; dev tree only.
- `dotnet format BhMaps.slnx` is the only formatter. csharpier must not be added or run. Builds with 0 warnings in Debug and Release (TreatWarningsAsErrors is on).
- Tests: `tests\BhMaps.Core.Tests` only (the App has no test project); view models are verified by build and UIA captures on the dev tree.
- Copy: no em-dashes, no emoji. Wording constants are `public const string` on the view model that owns them.
- `FocusVisualStyle` must be a local attribute, never a style setter; never a bare `x:Static` of a `const int` into a double property.
- `docs\manual.md` stays CRLF, UTF-8 without BOM. Part A adds no manual text; the manual edit belongs to the Part B plan docs task.
- **Builds and tests must pass `--artifacts-path`.** The owner's Release exe locks `bin\Release`, so every `dotnet build` and `dotnet test` in this plan carries `--artifacts-path C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\art`, written `<ART>` below. Never build without it unless the dispatch says the owner's app is closed.
- Implementers never dispatch subagents. No whole-file Reads over about 400 lines: use offset/limit or grep first.
- One commit per task, imperative mood, message written to a file and committed with `git commit -F`. Check `git config user.email` is the as9pa identity before the first commit.

---

## Task 1: Core draws a focus set and ghosts the rest

Spec 3.2 (Core half).

**Files:**
- Modify: `src\BhMaps.Core\Imaging\MapCompositor.cs` (`PanelWidth` and `PanelHeight` consts line 84; `Render` line 121; `DrawNode` line 210; `DrawAsset` line 241)
- Modify: `src\BhMaps.Core\Imaging\PlatformBounds.cs` (`Pad` const line 14; `For` line 18; `Union` line 76)
- Modify: `tests\BhMaps.Core.Tests\MapCompositorTests.cs` (helpers `Level`, `Node`, `Solid`, `AssertColour` at the top)
- Modify: `tests\BhMaps.Core.Tests\PlatformBoundsTests.cs` (helpers `Level`, `Node`, `Asset` at lines 79 to 88)

**Interfaces:**
- Consumes: `AssetPath.Resolve(string assetDir, string assetName)` (in `src\BhMaps.Core\LevelData\LevelModels.cs`), `AssetSources.ResolveAsset(string relativePath)`, `LevelDesc.AssetDir`.
- Produces:
  - `public const double MapCompositor.GhostOpacity = 0.15;`
  - `public static BitmapSource MapCompositor.Render(LevelDesc level, int width, int height, AssetSources sources, CameraBounds? viewport = null, IReadOnlySet<string>? focus = null, double ghostOpacity = GhostOpacity)`
  - `public static CameraBounds? PlatformBounds.For(LevelDesc level, double pad, double aspect, IReadOnlySet<string>? focus)`
  - Task 3 calls both.

- [ ] **Step 1: The focus parameter on Render.** In `MapCompositor.cs`, add the const next to `PanelWidth` and widen `Render`.

```csharp
    /// <summary>What a piece outside the focus set is drawn at (spec 3.2): visible enough to place the shape,
    /// faint enough that the focused pieces read as the subject.</summary>
    public const double GhostOpacity = 0.15;
```

```csharp
    /// <summary>Frozen Bgr24 bitmap. Safe on any thread. Never throws for a missing or bad asset; that asset is
    /// skipped. <paramref name="viewport" /> is the part of the level to draw, in the level's own coordinates,
    /// and null means the whole camera, which is what every caller but the Platforms page wants (addendum C).
    /// <paramref name="focus" /> is the set of asset paths relative to the map art root that draw at full
    /// strength; null means every asset does, which is what every 2.3 caller wants. An asset outside a non-null
    /// set draws at <paramref name="ghostOpacity" />. Pass a set built with
    /// <see cref="StringComparer.OrdinalIgnoreCase" />: asset paths come from a file system that ignores case.</summary>
    public static BitmapSource Render(
        LevelDesc level,
        int width,
        int height,
        AssetSources sources,
        CameraBounds? viewport = null,
        IReadOnlySet<string>? focus = null,
        double ghostOpacity = GhostOpacity)
```

- [ ] **Step 2: Thread focus through the draw walk.** Inside `Render`, the platform loop becomes:

```csharp
                foreach (var node in level.Platforms)
                {
                    DrawNode(dc, node, level.AssetDir, sources, decoded, focus, ghostOpacity);
                }
```

`DrawNode` gains the same two parameters and passes them on:

```csharp
    private static void DrawNode(
        DrawingContext dc,
        PlatformNode node,
        string assetDir,
        AssetSources sources,
        Dictionary<string, BitmapSource?> decoded,
        IReadOnlySet<string>? focus,
        double ghostOpacity)
```

```csharp
        foreach (var asset in node.Assets)
        {
            DrawAsset(dc, asset, assetDir, sources, decoded, focus, ghostOpacity);
        }

        foreach (var child in node.Children)
        {
            DrawNode(dc, child, assetDir, sources, decoded, focus, ghostOpacity);
        }
```

Do not touch `DrawBackground`: spec 3.2 says the background stays at full strength. Do not touch the `node.IsThemed` early return: seasonal nodes stay hidden.

- [ ] **Step 3: Ghost the assets outside the set.** Replace `DrawAsset` with this. The opacity push wraps the mirror push, so the two pops stay paired in the order they were pushed.

```csharp
    private static void DrawAsset(
        DrawingContext dc,
        LevelAsset asset,
        string assetDir,
        AssetSources sources,
        Dictionary<string, BitmapSource?> decoded,
        IReadOnlySet<string>? focus,
        double ghostOpacity)
    {
        var relativePath = AssetPath.Resolve(assetDir, asset.AssetName);
        var path = sources.ResolveAsset(relativePath);
        var image = path is null ? null : Decode(path, decoded);
        if (image is null)
        {
            return;
        }

        // Spec 3.2: a piece outside the focus set is a ghost. A null set is every 2.3 caller and ghosts nothing.
        var ghost = focus is not null && !focus.Contains(relativePath);
        if (ghost)
        {
            dc.PushOpacity(ghostOpacity);
        }

        // A missing W or H parses as 0 and means "the image's own size".
        var w = asset.W == 0 ? image.PixelWidth : Math.Abs(asset.W);
        var h = asset.H == 0 ? image.PixelHeight : Math.Abs(asset.H);
        var mirrored = asset.W < 0 || asset.H < 0;
        if (mirrored)
        {
            dc.PushTransform(new ScaleTransform(
                asset.W < 0 ? -1 : 1, asset.H < 0 ? -1 : 1, asset.X + (w / 2), asset.Y + (h / 2)));
        }

        dc.DrawImage(image, new Rect(asset.X, asset.Y, w, h));
        if (mirrored)
        {
            dc.Pop();
        }

        if (ghost)
        {
            dc.Pop();
        }
    }
```

- [ ] **Step 4: The PlatformBounds focus overload.** In `PlatformBounds.cs`, keep the existing three argument `For` as a one line forwarder so no caller changes, and give the real body the focus set.

```csharp
    /// <summary>Null for a level with no camera, or one whose only platforms are seasonal, which the compositor
    /// does not draw either. Then the caller renders the whole level as before.</summary>
    public static CameraBounds? For(LevelDesc level, double pad, double aspect) =>
        For(level, pad, aspect, null);

    /// <summary>Spec 3.2: with a focus set, the box is the union of the focused assets alone, so Ticked only
    /// frames the ticked pieces. A set that matches no asset leaves the box empty and returns null, which is the
    /// same "render the whole level" answer a level with no platforms already gives.</summary>
    public static CameraBounds? For(LevelDesc level, double pad, double aspect, IReadOnlySet<string>? focus)
    {
        var camera = level.Camera;
        if (camera.W <= 0 || camera.H <= 0 || aspect <= 0)
        {
            return null;
        }

        var box = Rect.Empty;
        foreach (var node in level.Platforms)
        {
            Union(node, Matrix.Identity, level.AssetDir, focus, ref box);
        }
```

The rest of the method body is unchanged from `if (box.IsEmpty)` down. `Union` becomes:

```csharp
    private static void Union(
        PlatformNode node, Matrix parent, string assetDir, IReadOnlySet<string>? focus, ref Rect box)
    {
        if (node.IsThemed)
        {
            return;
        }

        var local = Matrix.Identity;
        local.Scale(node.EffectiveScaleX, node.EffectiveScaleY);
        local.Rotate(node.Rotation);
        local.Translate(node.X, node.Y);
        var matrix = Matrix.Multiply(local, parent);

        foreach (var asset in node.Assets)
        {
            // A node outside the set still carries its children's transform, so the walk goes on either way.
            if (focus is not null && !focus.Contains(AssetPath.Resolve(assetDir, asset.AssetName)))
            {
                continue;
            }

            // A missing W or H parses as 0 and means "the image's own size", which is not known without decoding
            // the file, so the asset contributes its position alone rather than a guessed rectangle.
            var rect = new Rect(asset.X, asset.Y, Math.Abs(asset.W), Math.Abs(asset.H));
            var transformed = Rect.Transform(rect, matrix);
            box.Union(transformed);
        }

        foreach (var child in node.Children)
        {
            Union(child, matrix, assetDir, focus, ref box);
        }
    }
```

`AssetPath` lives in `BhMaps.Core.LevelData`, which `PlatformBounds.cs` already imports.

- [ ] **Step 5: The ghost pixel test.** Append inside the class in `tests\BhMaps.Core.Tests\MapCompositorTests.cs`. A black background and a white piece make the arithmetic plain: a ghosted white pixel over black is about 0.15 times 255, which is 38.

```csharp
    [Fact]
    public void Render_DrawsAnAssetOutsideTheFocusSetAtGhostOpacity()
    {
        using var tmp = new TempDir();
        Solid(tmp, @"Backgrounds\BG_Grove.jpg", 0, 0, 0);
        Solid(tmp, @"Grove\a.png", 255, 255, 255);
        Solid(tmp, @"Grove\b.png", 255, 255, 255);
        var level = Level(
            new CameraBounds(0, 0, 100, 100),
            "BG_Grove.jpg",
            Node(0, 0, null, new LevelAsset("a.png", 0, 0, 40, 100)),
            Node(0, 0, null, new LevelAsset("b.png", 60, 0, 40, 100)));
        var focus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AssetPath.Resolve("Grove", "a.png"),
        };

        var bitmap = MapCompositor.Render(level, 100, 100, new AssetSources(tmp.Path), null, focus);

        var focused = SyntheticImage.PixelAt(bitmap, 20, 50);
        var ghosted = SyntheticImage.PixelAt(bitmap, 80, 50);
        Assert.True(focused.R > 240, $"the focused piece was dimmed: {focused}");
        Assert.InRange(ghosted.R, 25, 52);
    }
```

- [ ] **Step 6: The two bounds tests.** Append inside the class in `tests\BhMaps.Core.Tests\PlatformBoundsTests.cs`. The file's own helpers name every asset `a.png`, so add one local helper that takes a name rather than changing the shared ones.

```csharp
    [Fact]
    public void For_WithAFocusSetBoxesOnlyTheFocusedAssets()
    {
        var level = new LevelDesc(
            "Test", "Test", new CameraBounds(0, 0, 4000, 2000), [],
            [Named(1000, 1000, "a.png"), Named(3000, 1000, "b.png")]);
        var focus = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AssetPath.Resolve("Test", "a.png"),
        };

        var bounds = PlatformBounds.For(level, 0, Aspect, focus);

        // Only the first node is in the set, so the box is 1000..1100 rather than 1000..3100.
        Assert.NotNull(bounds);
        Assert.True(
            bounds!.X + bounds.W < 2000,
            $"the unfocused asset was boxed too: {bounds.X}..{bounds.X + bounds.W}");
        Assert.InRange(bounds.X + (bounds.W / 2), 1040, 1060);
    }

    [Fact]
    public void For_WithAFocusSetThatMatchesNothingFallsBackToTheFullCamera()
    {
        var level = new LevelDesc(
            "Test", "Test", new CameraBounds(0, 0, 4000, 2000), [], [Named(1000, 1000, "a.png")]);

        var bounds = PlatformBounds.For(level, 0, Aspect, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        // Null is the caller's signal to render the whole level, which is what nothing ticked has to do.
        Assert.Null(bounds);
    }

    private static PlatformNode Named(double x, double y, string assetName) =>
        new(x, y, 1, 1, 1, 0, null, [new LevelAsset(assetName, 0, 0, 100, 100)], []);
```

- [ ] **Step 7: Build, test, format.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: build 0 warnings 0 errors; every existing test still green plus the three new ones; format reports no changes. If an existing `MapCompositorTests` or `PlatformBoundsTests` case changed result, the default arguments are wrong: a null focus must ghost nothing and must not narrow the camera.

- [ ] **Step 8: Commit.** Write this message to a file and `git commit -F` it.

```
feat(core): draw a focus set and ghost the rest

Render takes an optional set of relative asset paths that draw at full
strength; anything else draws at GhostOpacity, 0.15. PlatformBounds.For
gains an overload that unions only the focused assets, so a caller can
frame them. Every existing caller passes nothing and gets the same
pixels as before. Spec 3.2.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

---

## Task 2: The remembered preview mode setting

Spec 3.5.

**Files:**
- Modify: `src\BhMaps.Core\Settings\AppSettings.cs` (the record header, lines 3 to 13)
- Modify: `src\BhMaps.Core\Settings\SettingsStore.cs` (`KnownKeys` line 18; the `new AppSettings(...)` at the end of `Load`, line 81; the `JsonObject` initialiser in `Save`, line 103)
- Modify: `tests\BhMaps.Core.Tests\SettingsStoreTests.cs`

**Interfaces:**
- Consumes: the private `Bool(JsonObject obj, string key)` helper at `SettingsStore.cs` line 216, which defaults to false; `AtomicFile.WriteAllText`.
- Produces: `AppSettings.PlatformPreviewIsolate` (bool, default false), persisted under the JSON key `platformPreviewIsolate`. Task 3 reads and writes it through `AppServices.Settings` and `AppServices.UpdateSettings`.

- [ ] **Step 1: The record field.** In `AppSettings.cs`, add the parameter last, after `BackgroundsShowPictures`, so no positional caller shifts.

```csharp
public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int MapsZoom = 6,
    int BackgroundsZoom = 2,
    int PackZoom = 5,
    bool WelcomeDone = false,
    int PlatformsZoom = 3,
    IReadOnlyDictionary<string, DateTimeOffset>? PackLastApplied = null,
    bool BackgroundsShowPictures = false,
    bool PlatformPreviewIsolate = false)
```

- [ ] **Step 2: Load and Save the key.** Add `"platformPreviewIsolate"` to the end of `KnownKeys`; an unlisted key would be carried into `AppSettings.Unknown` and written back a second time.

```csharp
    private static readonly string[] KnownKeys =
    [
        "gamePath", "libraryPath", "firstRunDone", "mapsZoom", "backgroundsRowZoom", "packZoom", "platformsZoom",
        "welcomeDone", "homeZoom", "whileRunning", "backgroundsZoom", "packLastApplied",
        "backgroundsShowPictures", "platformPreviewIsolate",
    ];
```

In `Load`, the constructor call now ends:

```csharp
            Bool(obj, "backgroundsShowPictures"),
            Bool(obj, "platformPreviewIsolate"))
```

In `Save`, straight after the `backgroundsShowPictures` line inside the `JsonObject` initialiser:

```csharp
            ["platformPreviewIsolate"] = settings.PlatformPreviewIsolate,
```

- [ ] **Step 3: The round trip test.** Append inside the class in `tests\BhMaps.Core.Tests\SettingsStoreTests.cs`.

```csharp
    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformPreviewMode()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        Assert.False(SettingsStore.Load(path).PlatformPreviewIsolate);

        SettingsStore.Save(path, AppSettings.Default with { PlatformPreviewIsolate = true });

        Assert.True(SettingsStore.Load(path).PlatformPreviewIsolate);
        Assert.Contains("\"platformPreviewIsolate\": true", File.ReadAllText(path));
    }
```

- [ ] **Step 4: Build, test, format, commit.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: green, including the existing `SaveThenLoad_RoundTripsWithCamelCaseJsonAndNoTempFile` case and any case that asserts on unknown keys, which must not start seeing the new key as unknown.

```
feat(settings): remember the editor preview mode

platformPreviewIsolate, bool, default false, read when the platform
editor opens without a single file and written when the chip changes.
Spec 3.5.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

---

## Task 3: The view model drives the focus

Spec 3.1 (view model half), 3.2 (caller half), 3.3, 3.4, 3.5.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\PlatformEditorViewModel.cs` (wording consts line 33; `_syncing` field line 75; ctor line 77; observable property block line 140; computed properties near `TickedCount` line 182; `OnOpacityChanged` line 325; `All` and `None` commands lines 413 to 430; `OnTickedChanged` line 758; `SchedulePreview` line 855; `RenderAsync` line 870)

**Interfaces:**
- Consumes: `MapCompositor.Render(..., CameraBounds? viewport, IReadOnlySet<string>? focus, double ghostOpacity)` and `PlatformBounds.For(level, pad, aspect, focus)` from Task 1; `AppSettings.PlatformPreviewIsolate` from Task 2; `AppServices.Settings` and `AppServices.UpdateSettings(AppSettings)`; `PlatformPieceViewModel.RelativePath` and `.IsTicked`; `PlatformEditorRequest.OnlyFile`.
- Produces, for Task 4 to bind: `bool IsolatePreview { get; set; }`, `bool ShowAllPieces { get; set; }`, `string? IsolateHint { get; }`, `SoloCommand` and `AddTickCommand` (both `IRelayCommand<PlatformPieceViewModel?>`), and `public const string IsolateHintText`.

- [ ] **Step 1: The wording constant.** Next to `TickHintText` at line 36:

```csharp
    /// <summary>Spec 3.2: Ticked only with nothing ticked ghosts the whole map, so the preview says what to do
    /// about it, in the same words TickHintText uses for the sliders.</summary>
    public const string IsolateHintText = "Tick a file to see it on its own.";
```

- [ ] **Step 2: The mode property and its pair.** Add to the observable property block near line 140.

```csharp
    /// <summary>Spec 3.1: true is Ticked only, false is All pieces. Changing it schedules a render and writes the
    /// setting; nothing else is written anywhere.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAllPieces))]
    [NotifyPropertyChangedFor(nameof(IsolateHint))]
    public partial bool IsolatePreview { get; set; }
```

With the computed properties near `TickedCount`:

```csharp
    /// <summary>The All pieces chip's side of the pair, so each chip binds IsSelected two way and the list's own
    /// single selection keeps exactly one of them on.</summary>
    public bool ShowAllPieces
    {
        get => !IsolatePreview;
        set => IsolatePreview = !value;
    }

    /// <summary>Spec 3.2: the scrim line under a fully ghosted preview, and null when there is nothing to say, so
    /// the window hides it with the same NullToVis converter the empty text already uses.</summary>
    public string? IsolateHint => IsolatePreview && Pieces.Count > 0 && TickedCount == 0 ? IsolateHintText : null;
```

- [ ] **Step 3: Start in the right mode without writing the setting.** Add the guard field next to `_syncing`:

```csharp
    /// <summary>True while the ctor is putting the remembered mode on, so opening the editor never writes the
    /// setting back and never schedules a second render (spec 3.5).</summary>
    private bool _restoringMode;
```

In the constructor, between `ApplyNow = true;` and the `existingDefault` line:

```csharp
        // Spec 3.4: a panel row's Edit names one file, and that editor opens on it. Spec 3.5: every other way in
        // opens in the mode the last chip click left behind.
        _restoringMode = true;
        IsolatePreview = request.OnlyFile is not null || services.Settings.PlatformPreviewIsolate;
        _restoringMode = false;
```

- [ ] **Step 4: React to the mode changing.** Add next to `OnOpacityChanged`:

```csharp
    partial void OnIsolatePreviewChanged(bool value)
    {
        if (_restoringMode)
        {
            return;
        }

        // Spec 3.1: the chip re-renders through the existing 60 ms throttle. Spec 3.5: and is remembered.
        SchedulePreview();
        _services.UpdateSettings(_services.Settings with { PlatformPreviewIsolate = value });
    }
```

- [ ] **Step 5: Ticking re-renders while isolating.** `OnTickedChanged` at line 758 ends by raising the readouts. Add the hint to the `OnPropertyChanged` calls it already makes, then the render as the last statement of the method:

```csharp
        OnPropertyChanged(nameof(IsolateHint));

        // In All pieces a tick only moves the sliders, as in 2.3. In Ticked only it changes the picture, so the
        // render is asked for; the 60 ms throttle collapses the run of ticks All, None and Solo produce.
        if (IsolatePreview)
        {
            SchedulePreview();
        }
```

- [ ] **Step 6: Solo and Ctrl+click.** Add after the `None` command at line 430.

```csharp
    /// <summary>Spec 3.3: clicking a row's thumbnail or name ticks that row and unticks the rest. Each write goes
    /// through the row's own property, so the sliders, the Image line and the header follow exactly as they do
    /// for All and None.</summary>
    [RelayCommand]
    private void Solo(PlatformPieceViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        foreach (var other in Pieces)
        {
            other.IsTicked = ReferenceEquals(other, row);
        }
    }

    /// <summary>Spec 3.3: Ctrl+click adds a row to the ticks and clears nothing. Ticking a ticked row is a no-op
    /// rather than a toggle: the checkbox is where unticking lives.</summary>
    [RelayCommand]
    private void AddTick(PlatformPieceViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsTicked = true;
    }
```

- [ ] **Step 7: Hand the focus to the render.** Add this helper next to `SchedulePreview` at line 855:

```csharp
    /// <summary>Spec 3.2. Not isolating, a map with no pieces, and every piece ticked all render exactly as 2.3
    /// did: null focus, null viewport, pixel for pixel. Isolating with nothing ticked passes an empty set, which
    /// ghosts everything, over the full camera. Otherwise the ticked paths are the focus and the frame leans in;
    /// a frame that comes back null, which is focused pieces with no size, falls back to the full camera too.</summary>
    private (IReadOnlySet<string>? Focus, CameraBounds? Viewport) FocusFor(LevelDesc level)
    {
        if (!IsolatePreview || Pieces.Count == 0)
        {
            return (null, null);
        }

        var ticked = Pieces.Where(p => p.IsTicked).ToList();
        if (ticked.Count == Pieces.Count)
        {
            return (null, null);
        }

        var focus = new HashSet<string>(ticked.Select(p => p.RelativePath), StringComparer.OrdinalIgnoreCase);
        if (focus.Count == 0)
        {
            return (focus, null);
        }

        var aspect = (double)MapCompositor.PanelWidth / MapCompositor.PanelHeight;
        return (focus, PlatformBounds.For(level, PlatformBounds.Pad, aspect, focus));
    }
```

In `RenderAsync` at line 870, read it beside the other captured values, on the UI thread, before the `try`:

```csharp
        var rows = Pieces;
        var root = _tempRoot;
        var level = _request.Map.BaseLevel;
        var background = _backgroundPath;
        var (focus, viewport) = FocusFor(level);
        var reading = "";
```

and pass both to the compositor:

```csharp
            var bitmap = await _services.Renderer.RunAsync(
                () => MapCompositor.Render(
                    level, MapCompositor.PanelWidth, MapCompositor.PanelHeight, sources, viewport, focus),
                ct);
```

Nothing else in `RenderAsync` changes. The temp set is still written for every row, ticked or not, so Save, the sliders, Replace and Edit in another app are untouched and the ghost never reaches disk (spec 3.2).

- [ ] **Step 8: Build and test.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet build BhMaps.slnx -c Release --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings in both configurations, all tests green, no format changes. The App has no test project, so this task's proof is the build plus the Task 4 captures.

- [ ] **Step 9: Commit.**

```
feat(editor): follow the ticks in the preview

IsolatePreview picks the focus set the render gets: the ticked pieces
draw as now and the rest ghost, with the camera framed on the ticked
ones. Solo and AddTick give a row name something to run. Every piece
ticked, and every render in All pieces, is the 2.3 picture unchanged.
Spec 3.1 to 3.5.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

---

## Task 4: The chips, the clickable row and the scrim

Spec 3.1 (window half), 3.2 (scrim), 3.3 (row half).

**Files:**
- Modify: `src\BhMaps.App\Views\PlatformEditorWindow.xaml` (preview card `Border` line 64, its inner `Grid` line 65, the `Viewbox` lines 68 to 74, the empty text `TextBlock` line 77; the Files `ItemsControl` line 123 and its `DataTemplate` lines 125 to 166)

**Interfaces:**
- Consumes from Task 3: `ShowAllPieces`, `IsolatePreview`, `IsolateHint`, `SoloCommand`, `AddTickCommand`, alongside the existing `Preview`, `FileName`, `RelativePath`, `Thumbnail`, `Readout` and `IsTicked`.
- Consumes from the theme: `ChipRow` (`src\BhMaps.App\Theme\Controls.xaml` line 736, a `ListBoxItem` style that draws a `ChipToggle` and owns its focus ring), `Surface2Brush`, `Text2Brush`, `Radius`, `PictureTile`, `MonoText`, `Readout`, `DialogFocusRing`, `NullToVis`.

- [ ] **Step 1: The chips over the preview.** Inside the preview card's `Grid`, after the closing `</Viewbox>` and before the empty text `TextBlock`, add:

```xml
        <!-- Spec 3.1: top left inside the card, on the image's own 16 px margin. A ListBox rather than two
             ToggleButtons, so the list owns which chip is on; ChipRow draws each item with ChipToggle and carries
             the focus ring. A map with no platform art has no preview, so the chips go with it. -->
        <ListBox Margin="16,16,0,0"
                 HorizontalAlignment="Left"
                 VerticalAlignment="Top"
                 Background="Transparent"
                 BorderThickness="0"
                 ItemContainerStyle="{StaticResource ChipRow}"
                 Visibility="{Binding Preview, Converter={StaticResource NullToVis}}">
          <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
              <StackPanel Orientation="Horizontal" />
            </ItemsPanelTemplate>
          </ListBox.ItemsPanel>
          <ListBoxItem AutomationProperties.Name="All pieces"
                       Content="All pieces"
                       IsSelected="{Binding ShowAllPieces, Mode=TwoWay}" />
          <ListBoxItem AutomationProperties.Name="Ticked only"
                       Content="Ticked only"
                       IsSelected="{Binding IsolatePreview, Mode=TwoWay}" />
        </ListBox>
```

Tab order comes out right without a `TabIndex`: the preview card is `Grid.Column="0"`, so the chips are reached before Source and Files. Space and Enter on a `ListBoxItem` select it, which is the switch (spec 3.1).

- [ ] **Step 2: The scrim line.** Immediately after the chips, still inside the preview card's `Grid`:

```xml
        <!-- Spec 3.2: Ticked only with nothing ticked. The view model returns null when there is nothing to say,
             so the NullToVis the empty text already uses hides the line. -->
        <Border Margin="16"
                Padding="12,6"
                HorizontalAlignment="Center"
                VerticalAlignment="Bottom"
                Background="{StaticResource Surface2Brush}"
                CornerRadius="{StaticResource Radius}"
                Visibility="{Binding IsolateHint, Converter={StaticResource NullToVis}}">
          <TextBlock Foreground="{StaticResource Text2Brush}" Text="{Binding IsolateHint}" />
        </Border>
```

- [ ] **Step 3: The row click target.** In the Files `DataTemplate`, the `CheckBox` in column 0 is untouched. Replace the three elements that follow it (the thumbnail `Border` in column 1, the name `TextBlock` in column 2, the readout `TextBlock` in column 3) with one `Border` that spans columns 1 to 3 and holds them in a `Grid` of its own, their `Grid.Column` renumbered to 0, 1, 2. The outer `Grid` keeps all four `ColumnDefinition`s.

```xml
                  <!-- Spec 3.3: the thumbnail and the name are one click target. Background Transparent, not
                       unset: a Border with no Background is not hit testable. The Ctrl gesture is listed first so
                       it is matched before the plain click. -->
                  <Border Grid.Column="1"
                          Grid.ColumnSpan="3"
                          Padding="4,3"
                          AutomationProperties.Name="{Binding FileName}"
                          CornerRadius="{StaticResource Radius}"
                          Focusable="True"
                          FocusVisualStyle="{StaticResource DialogFocusRing}"
                          ToolTip="{Binding RelativePath}">
                    <Border.Style>
                      <Style TargetType="Border">
                        <Setter Property="Background" Value="Transparent" />
                        <Style.Triggers>
                          <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Background" Value="{StaticResource Surface2Brush}" />
                          </Trigger>
                        </Style.Triggers>
                      </Style>
                    </Border.Style>
                    <Border.InputBindings>
                      <MouseBinding Command="{Binding DataContext.AddTickCommand, RelativeSource={RelativeSource AncestorType={x:Type ItemsControl}}}"
                                    CommandParameter="{Binding}"
                                    Gesture="Ctrl+LeftClick" />
                      <MouseBinding Command="{Binding DataContext.SoloCommand, RelativeSource={RelativeSource AncestorType={x:Type ItemsControl}}}"
                                    CommandParameter="{Binding}"
                                    MouseAction="LeftClick" />
                      <KeyBinding Key="Enter"
                                  Command="{Binding DataContext.SoloCommand, RelativeSource={RelativeSource AncestorType={x:Type ItemsControl}}}"
                                  CommandParameter="{Binding}" />
                      <KeyBinding Key="Space"
                                  Command="{Binding DataContext.SoloCommand, RelativeSource={RelativeSource AncestorType={x:Type ItemsControl}}}"
                                  CommandParameter="{Binding}" />
                    </Border.InputBindings>
                    <Grid>
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                      </Grid.ColumnDefinitions>

                      <Border Width="48"
                              Height="27"
                              VerticalAlignment="Center"
                              Style="{StaticResource PictureTile}">
                        <Border CornerRadius="{StaticResource Radius}" RenderOptions.BitmapScalingMode="HighQuality">
                          <Border.Background>
                            <ImageBrush ImageSource="{Binding Thumbnail}" Stretch="UniformToFill" />
                          </Border.Background>
                        </Border>
                      </Border>

                      <TextBlock Grid.Column="1"
                                 Margin="8,0,0,0"
                                 VerticalAlignment="Center"
                                 Style="{StaticResource MonoText}"
                                 Text="{Binding FileName}"
                                 TextTrimming="CharacterEllipsis" />

                      <TextBlock Grid.Column="2"
                                 Margin="10,0,0,0"
                                 Style="{StaticResource Readout}"
                                 Text="{Binding Readout}" />
                    </Grid>
                  </Border>
```

The name keeps `MonoText` and gains no underline (spec 3.3). The `ToolTip` moves off the name onto the target, so the whole row shows the relative path. `KeyboardNavigation.TabNavigation="Continue"` on the `ItemsControl` at line 123 stays, so Tab walks checkbox, target, checkbox, target; the checkbox keeps Space for toggling because focus is on one or the other, never both.

- [ ] **Step 4: Build both configurations.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet build BhMaps.slnx -c Release --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings, 0 errors, no format changes. A XAML syntax error shows up here; a missing `StaticResource` does not, which is what Step 5 is for.

- [ ] **Step 5: Prove it in the app (spec 7).** Launch on the dev tree only, with `--game`, `--library` and `--appdata` all pointing under the scratchpad dev tree, plus `--quiet`. Never launch, close or kill Brawlhalla, and never touch the owner's own running BhMaps.exe. Open the platform editor on a map with three or more pieces and capture, with the PrintWindow capture library the dispatch points at, into the dispatch's capture folder:
  1. `editor-all-pieces.png`: All pieces on, every piece at full strength, whole camera.
  2. `editor-ticked-only.png`: Ticked only with one file ticked. The ticked piece is sharp and framed; the rest are faint.
  3. `editor-ticked-none.png`: Ticked only after None. The whole map is ghosted and the scrim line reads "Tick a file to see it on its own."
  4. `editor-chip-focus.png`: Tab from the window's first control; the focus ring sits on a chip.
  5. `editor-row-hover.png`: pointer over a file row; the row carries the Surface2 hover surface.

Then check by eye: clicking a second row's name leaves exactly one tick; Ctrl+clicking a third leaves two; All followed by Ticked only looks identical to All pieces; closing the editor in Ticked only and reopening it comes back in Ticked only; opening from a map panel row's Edit comes up in Ticked only with that one file ticked; a map with no platform art of its own shows neither chips nor scrim. Stop the dev tree BhMaps.exe you started.

- [ ] **Step 6: Commit.**

```
feat(editor): chips over the preview and a clickable file row

Two chips, All pieces and Ticked only, top left inside the preview card,
drawn by the ChipRow container style. A file's thumbnail and name are one
focusable click target with a hover surface: click solos the row, Ctrl
click adds it, Enter and Space solo it from the keyboard. Nothing ticked
in Ticked only shows the scrim line. Spec 3.1 to 3.3.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

---

## Risks and open questions

- **"All ticked equals All pieces pixel for pixel" versus "the camera box becomes the union of the focused pieces" (both spec 3.2).** The two sentences conflict once every row is ticked, because the framed box is not the full camera. Resolved in favour of the pixel for pixel sentence: `FocusFor` returns a null focus and a null viewport when `ticked.Count == Pieces.Count`. Cost if wrong: one `if` in `FocusFor`.
- **Focus set keys.** The editor's rows carry `RelativePath` from `MapEntry.PlatformFiles`; the compositor keys on `AssetPath.Resolve(level.AssetDir, asset.AssetName)`. They agree today because the editor writes its temp set at `RelativePath` and the render resolves through the same helper, and both sets ignore case. A map whose `PlatformFiles` entry did not equal the resolved asset path would ghost everything in Ticked only; the Task 4 Step 5 capture on a real map is what catches that.
- **`OnTickedChanged` now renders.** 2.3 deliberately did not re-render on a tick. Solo writes up to `Pieces.Count` ticks, so `SchedulePreview` is called in a burst; the existing 60 ms `Throttler` plus the `_sequence` stamp collapses them to one render. If a map ever carries hundreds of pieces this is worth batching behind a single call.
- **Not verified:** that a plain `ListBox` inside `PlatformEditorWindow.xaml` picks up the theme's list chrome cleanly. `ChipRow` has only ever been used on the three pages, never in a dialog window. A missing `StaticResource` throws when the window opens, not at build, which is why Step 5 is a real launch.
- **Not verified:** that binding `ListBoxItem.IsSelected` two way to a pair of view model properties converges when the ListBox clears the other item. It should, because `ShowAllPieces` and `IsolatePreview` are the same bit written twice, but if a chip is ever seen with both off, replace the pair with `SelectedIndex` and a bool to index converter.
- **Not verified:** the exact `OnPropertyChanged` list inside `OnTickedChanged` (line 758 onwards was not read in full). Step 5 of Task 3 says to add to it, not to replace it.
