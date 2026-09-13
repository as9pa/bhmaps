# BhMaps 2.5 Part B Implementation Plan: spanning Replace, many maps, stale card fix

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace lays one picture across a map's platforms through the level data's placement of every piece, the platform editor edits a set of maps in one session, and a card never shows an old picture after a game write.

**Architecture:** A new Core `SpanFitter` walks the level's platform tree with the same matrix chain `PlatformBounds` uses, cover fits the picture to the platform box and cuts each piece's window back into the file's own pixels. The editor gets a Fit switch, a drag pan on the preview, and a map set with a strip; Save loops the maps into one pack. `MapsViewModel` reloads the written maps' cards first after a game write, through a small Core `LoadOrder` helper that carries the test.

**Tech Stack:** .NET 10, WPF (`DrawingVisual`, `RenderTargetBitmap`, `Matrix`), CommunityToolkit.Mvvm, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-12-bhmaps-v2-5-design.md` sections 6, 9 and 11 (binding). Part A (records) is merged on the branch before this plan runs; its interfaces are named below where consumed.

**Branch:** `feature/bhmaps-v2.5`.

## Global Constraints

- Format with `dotnet format BhMaps.slnx` only. Build and test with `--artifacts-path <ART>`. TreatWarningsAsErrors on.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3). No App project tests; view model order logic goes through the Core `LoadOrder` helper so its test can live there.
- New `.cs` and `.xaml` files CRLF. No em-dashes, no emoji.
- WPF: `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute on every focusable control added; no bare `x:Static` const int into a double; `[ObservableProperty]` setters run `OnXChanged` during construction.
- Never run the app or tests against the real game folder, library or `%APPDATA%\BhMaps`. The dev tree (`scripts\make-dev-tree.ps1 -RealArt`, launched with `--game <dev>\game\mapArt --library <dev>\lib --appdata <dev>\appdata`) is the only place the app runs. Never steal focus (`--quiet` where the app supports it; `App.Quiet` sets `ShowActivated` false).
- One commit per task, message from a file with `git commit -F`, trailers as the dispatcher gives them.
- Part A names consumed here: `PlatformArt { Own, EachPiece, Across, WorkingCopy }`, `PlatformPieceEntry { Opacity, Hue, Art, Picture, PanX, PanY, Hash }`, `PlatformEditRecord.Load/Save/Entry/Map/SetMap`, `PlatformEditorRequest(MapEntry Map, Pack? Pack, string? OnlyFile, Pack? SourcePack)`, `PlatformPieceViewModel.SetReplacement(BitmapSource fitted, string name, string sourcePath)`, `ReplacementPath`, `ArtKind`, `Note`, `PlatformEditorViewModel.EntriesFor(rows, packRoot)`.

---

## Task 1: Bug 8 repro in the dev tree

Spec 11, first bullet. This task changes no code; its deliverable is a note in the plan's ledger.

**Files:**
- Read: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`Refresh` :356-411, `LoadPreviewsAsync` :484-500), `src\BhMaps.App\ViewModels\MapCardViewModel.cs` (`LoadPreviewAsync` :77-87, `ComposedAsync`), `src\BhMaps.Core\Imaging\PreviewCache.cs` (the cache key), `src\BhMaps.App\ViewModels\MainViewModel.cs` (`OpenPlatformEditorAsync` :619-669, `RunWriteCoreAsync` :981-1048).
- Create: `<sdd workspace>\bug8-repro.md`.

- [ ] **Step 1: Build the dev tree** with `powershell -File scripts\make-dev-tree.ps1 -RealArt` (default destination `%TEMP%\bhmaps-dev`). Build the app with `--artifacts-path <ART>` and launch the built exe from the artifacts folder with `--game`, `--library`, `--appdata` pointing into the dev tree and `--quiet` if the app accepts it (check `App.xaml.cs` for the flag). Never launch `bin\Release\BhMaps.exe`.
- [ ] **Step 2: Case 1 (Apply to game now).** Through UIA (`System.Windows.Automation` from a small PowerShell script, or the routes the dispatcher gives you): select map BloodMoon, open the card menu, Edit platforms, set Opacity to 0, Save with Apply to game now ticked. Then click a different map card, wait five seconds, and capture the BloodMoon card's picture (PrintWindow of the main window region, never foreground). Then click BloodMoon again and capture. Note whether the card showed unedited platforms between the two clicks.
- [ ] **Step 3: Case 2 (pack only).** Same steps with Apply to game now unticked. Note what the card and the panel show.
- [ ] **Step 4: Read the code path** and write in `bug8-repro.md`: which case reproduces, the sequence of `RescanAsync` calls after Save (count them from `OpenPlatformEditorAsync` and `RunWriteCoreAsync`), how `PreviewCache` keys a composite (does the key change when a pack file is written to the game folder?), and the order cards load in `LoadPreviewsAsync`. End with one paragraph: the cause you believe, and what Task 5 must change. Close the app you launched (it is the only BhMaps process you may close; check its PID is the one you started).
- [ ] **Step 5: No commit.** Report the file path.

---

## Task 2: Core SpanFitter

Spec 6.1.

**Files:**
- Create: `src\BhMaps.Core\Imaging\SpanFitter.cs`
- Modify: `src\BhMaps.Core\Imaging\PlatformBounds.cs` (expose the matrix walk: make a `Visit` that Task 2's `Placements` reuses, or duplicate the twelve lines with a comment naming the twin)
- Test: `tests\BhMaps.Core.Tests\SpanFitterTests.cs`

**Interfaces:**
- Consumes: `LevelDesc`, `PlatformNode` (`EffectiveScaleX/Y`, `Rotation`, `X`, `Y`, `IsThemed`, `Assets`, `Children`), `LevelAsset(AssetName, X, Y, W, H)`, `AssetPath.Resolve`, `BackgroundFitter.DestinationRect(srcW, srcH, FitOptions, width, height)`, `FitOptions(Mode, PanX, PanY, ...)`, `PieceFitter.Fit` (for the alpha mask step, factor its mask loop into `internal static byte[] MaskBy(byte[] pixels, BitmapSource piece)` and reuse it).
- Produces:

```csharp
namespace BhMaps.Core.Imaging;

/// <summary>One drawn instance of a piece: the asset rectangle in the node's own space and the matrix that
/// takes it into camera space (scale, rotate, translate, child inside parent, as the compositor draws).</summary>
public sealed record Placement(Rect Local, Matrix Transform)
{
    /// <summary>Area in camera space, for picking the largest instance.</summary>
    public double Area { get; }
    /// <summary>The camera-space bounding box.</summary>
    public Rect Bounds { get; }
}

public static class SpanFitter
{
    /// <summary>Every unthemed placement of relativePath on the level, in draw order. Empty when the level never
    /// places the piece. An asset whose W or H is 0 uses the piece's own pixel size, which the caller passes.</summary>
    public static IReadOnlyList<Placement> Placements(LevelDesc level, string relativePath, int pieceWidth, int pieceHeight);

    /// <summary>The platform box: PlatformBounds.For(level, pad: 0, aspect: <box's own aspect>) collapsed to the
    /// plain union, so no padding and no aspect growth. Null when the level has no unthemed platform asset.</summary>
    public static Rect? Box(LevelDesc level);

    /// <summary>The largest placement by camera-space area, or null for an empty list.</summary>
    public static Placement? Largest(IReadOnlyList<Placement> placements);

    /// <summary>The picture cover fitted into box with the pan, then the part under the placement cut back into
    /// the piece's own pixels (undoing scale, flip and rotation) and masked by the piece's alpha. Frozen Bgra32 at
    /// the piece's size and DPI.</summary>
    public static BitmapSource Cut(BitmapSource picture, Rect box, FitOptions pan, Placement placement, BitmapSource piece);

    /// <summary>The whole stage for the preview: the picture cover fitted into box, drawn as one bitmap the size of
    /// the box at scale pixels per unit. Used by the editor's joined preview when it needs the layer itself.</summary>
    public static BitmapSource Fitted(BitmapSource picture, Rect box, FitOptions pan, double scale);
}
```

`Cut` algorithm: `dest = BackgroundFitter.DestinationRect(picture.PixelWidth, picture.PixelHeight, pan, box.Width, box.Height)` gives the picture rectangle in box space (offset by `box.X`, `box.Y` to camera space). Build `M = placement.Transform` (local to camera). For the piece's pixel `(px, py)` the local point is `(Local.X + px * Local.Width / pieceWidth, Local.Y + py * Local.Height / pieceHeight)`; camera point `= M.Transform(local)`; picture point `= (camera - dest.TopLeft) * (picture.PixelWidth / dest.Width, picture.PixelHeight / dest.Height)`. Implement with WPF drawing, not loops: a `DrawingVisual` at piece size with a transform group `[ScaleTransform(Local.Width / pieceWidth, Local.Height / pieceHeight) then TranslateTransform(Local.X, Local.Y)]` inverted composition: push `MatrixTransform(Inverse(pixelToLocal * M))` where `pixelToLocal` maps piece pixels to local space, then draw the picture at `dest` (camera space). Equivalent: `full = pixelToLocal * M` (piece pixel to camera); `dc.PushTransform(new MatrixTransform(full.Inverse))`; `dc.DrawImage(picture, dest translated by box origin)`. Render to `RenderTargetBitmap` Pbgra32 at piece size, convert to Bgra32, mask with the piece's alpha, `WriteableBitmap`, freeze. Use `RenderOptions.SetBitmapScalingMode(visual, HighQuality)`.

- [ ] **Step 1: Write the failing tests** (helpers: `LevelXml.Level(name, assetDir, body)`, `SyntheticImage.SavePng`, `SyntheticImage.PixelRgbaAt`, `TempDir`; look at `PlatformBoundsTests` for how a `LevelDesc` is built from xml):

```csharp
[Fact] public void Two_pieces_side_by_side_continue_the_picture()
// Level: camera 0,0,400,200; one platform node at 0,0 with assets A (0,0,200,200) and B (200,0,200,200).
// Picture: 400 x 200 horizontal gradient, red channel = x. Pieces: two opaque 100 x 100 PNGs.
// Cut A and Cut B; assert red at A's last column ~ 199 within 4 and red at B's first column ~ 201 within 4,
// and that the red across A runs from ~0 to ~199 (monotonic).

[Fact] public void Flipped_piece_comes_back_mirrored()
// Node ScaleX = -1 placed at X = 200 with asset A (0,0,200,200): the cut's red decreases left to right.

[Fact] public void Rotated_piece_comes_back_rotated()
// Node Rotation = 90 at X = 200, Y = 0 with asset A (0,0,200,200) and a vertical gradient picture (green = y):
// the cut's green varies along its x axis, not its y axis.

[Fact] public void Piece_drawn_twice_lists_two_placements_and_largest_wins()
// Two nodes place A, one with Scale 2: Placements has 2 entries; Largest is the scaled one.

[Fact] public void Unplaced_piece_has_no_placements()
// Placements(level, "Map\\Missing.png", 10, 10) is empty; Box is not null.

[Fact] public void Alpha_of_the_piece_masks_the_cut()
// Piece PNG with a transparent left half: cut's left half alpha 0, right half alpha 255.

[Fact] public void Box_is_the_plain_union_without_padding()
// Assets at (10,20,100,50) and (200,20,100,50): Box == Rect(10,20,290,50).
```

- [ ] **Step 2: Run, expect compile failure.**
- [ ] **Step 3: Implement `SpanFitter.cs`** per the Interfaces block; refactor `PlatformBounds.Union` so `Placements` shares the matrix chain (a private static `Walk(node, parent, Action<PlatformNode, Matrix> visit)` in `PlatformBounds` marked `internal`, used by both).
- [ ] **Step 4: Run tests, expect pass. Format. Build.** Existing `PlatformBoundsTests` must still pass.
- [ ] **Step 5: Commit** "Core: SpanFitter cuts each placed piece from a picture laid across the platforms".

---

## Task 3: Editor: Fit switch, drag pan, joined preview, row notes, Across in the record

Spec 6.2; record art `Across` per spec 3.1 and 5.2.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\PlatformEditorViewModel.cs` (`ReplaceAsync` :509-551; record load branch for `Across` from Part A Task 4; new `FitAcross`, `PanX`, `PanY`, `LoadedPicture`, `LoadedPicturePath`, `RecutAsync`, `BeginPan/DragPan`)
- Modify: `src\BhMaps.App\ViewModels\PlatformPieceViewModel.cs` (`Across` flag on the row, `ArtKind` returns `PlatformArt.Across` when `Art == Replacement && Across`)
- Modify: `src\BhMaps.App\Views\PlatformEditorWindow.xaml` (switch under Image block :247-277; preview `Image` :64-121 gets mouse handlers) and `PlatformEditorWindow.xaml.cs` (mouse down, move, up on the preview mapped to `DragPan(dx, dy)` in stage pixels)

**Interfaces:**
- Consumes: Task 2 `SpanFitter`; Part A record API.
- Produces:

```csharp
// PlatformEditorViewModel
[ObservableProperty] private bool _fitAcross = true;     // "Across the platforms"; false is "On each piece"
[ObservableProperty] private double _panX = 0.5, _panY = 0.5;
public bool CanUseFit { get; }                            // a picture is loaded
/// <summary>Moves the laid picture by a stage-pixel delta; converts to pan units from the box size and the
/// fitted picture's overflow, clamps to 0..1, re-cuts.</summary>
public void DragPan(double dxStagePixels, double dyStagePixels);
/// <summary>Re-cuts every ticked row from the loaded picture with the current switch and pan.</summary>
private Task RecutAsync();

// PlatformPieceViewModel
public bool Across { get; private set; }                  // set with SetReplacement(fitted, name, sourcePath, across)
public void SetReplacement(BitmapSource fitted, string name, string sourcePath, bool across);   // Part A's 3-arg form stays and calls this with false
```

- [ ] **Step 1: Keep the picture.** `ReplaceAsync` stores `LoadedPicture` (frozen `BitmapSource`) and `LoadedPicturePath`, then calls `RecutAsync()`. `RecutAsync` off the UI thread: `level = _request.Map.BaseLevel`; `box = SpanFitter.Box(level)`; for each ticked row: `piece = BackgroundFitter.LoadSource(row.SourcePath)`; if `FitAcross && box is not null`: `placements = SpanFitter.Placements(level, row.RelativePath, piece.PixelWidth, piece.PixelHeight)`; if empty: `fitted = PieceFitter.Fit(picture, piece)`, `row.Note = $"{row.FileName} not on this stage, fitted on its own."`, `across = false`; else `fitted = SpanFitter.Cut(picture, box, new FitOptions(PanX: PanX, PanY: PanY), SpanFitter.Largest(placements)!, piece)`, `across = true`, note `$"{row.FileName} drawn {placements.Count} times, cut from the largest."` when `placements.Count > 1`, else `""`. When `!FitAcross`: `PieceFitter.Fit`, `across = false`, note `""`. Then `row.SetReplacement(fitted, name, path, across)`, thumbnail, `OnImageChanged()`, `SchedulePreview()`. Notes from Part A (Picture gone, Original missing, Changed outside) take priority: only set the fit notes when `row.Note` is empty or is itself a fit note (track with a `_fitNoteRows` set).
- [ ] **Step 2: Switch and pan.** `OnFitAcrossChanged` and `OnPanXChanged`/`OnPanYChanged` call `RecutAsync()` when a picture is loaded (guard `_constructing`). `DragPan`: the stage is 1280 by 720 rendering the viewport `FocusFor(level)` returns (or the camera); box width in stage pixels `= box.Width * scale` where `scale = 1280 / viewportWidth`; the fitted picture overflows the box by `overflowX = dest.Width - box.Width` (from `BackgroundFitter.DestinationRect`); `PanX -= dx / scale / overflowX` when `overflowX > 0` (clamped 0..1), same for Y. Debounce re-cuts while dragging with the existing `SchedulePreview` style 60 ms timer.
- [ ] **Step 3: Record.** `ArtKind` returns `Across` for an across row. In `EntriesFor`, for `Across` set `Picture = row.ReplacementPath`, `PanX`, `PanY` from the editor (pass them in: `EntriesFor(rows, packRoot, panX, panY)`). Record load (Part A Task 4 `Across` branch): set `LoadedPicture` from `entry.Picture`, `PanX`/`PanY` from the entry, `FitAcross = true`, and run `RecutAsync` for the rows whose entry is `Across`; rows whose entry is `EachPiece` keep Part A's behaviour.
- [ ] **Step 4: XAML.** Under the Replace row in the Image block add a two-button segmented switch (two `RadioButton`s styled as the app's toggle buttons, `GroupName="Fit"`, `AutomationProperties.Name="Across the platforms"` and `"On each piece"`, bound to `FitAcross` through the existing bool converters or an `IsChecked="{Binding FitAcross}"` and `IsChecked="{Binding FitAcross, Converter={StaticResource InverseBool}}"`; check `App.xaml` for an inverse converter, add one to the window resources if none), `IsEnabled="{Binding CanUseFit}"`, focus ring on both. Preview `Image` gets `MouseLeftButtonDown/Move/Up` in code behind computing deltas in the `Image`'s pixel space scaled to 1280 by 720 (`ActualWidth` against 1280) and calling `DragPan`; `Cursor` becomes `SizeAll` when `CanUseFit && FitAcross`.
- [ ] **Step 5: Build, format, all tests. Commit** "Platform editor: Replace lays the picture across the platforms, drag pan, row notes".

---

## Task 4: Many maps

Spec 9.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\PlatformEditorViewModel.cs` (request with `Maps`; `MapSets`, `CurrentIndex`, `CurrentMap`, `MapStripText`, `Previous/NextMapCommand`; `Pieces` becomes the current map's rows; `SaveAsync` loop with `SaveProgressText` and `CancelSaveCommand`)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`OpenPlatformEditorAsync(IReadOnlyList<MapEntry> maps, Pack? pack, string? onlyFile = null)`; single-map callers pass `[map]`; apply loop over `saved.Maps`)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`BuildCardMenu` :670-674 Edit platforms row enabled for any count, command passes `target`)
- Modify: `src\BhMaps.App\Views\PlatformEditorWindow.xaml` (map strip above the preview; progress line and Cancel in the bottom bar :365-387)

**Interfaces:**
- Produces:

```csharp
public sealed record PlatformEditorRequest(IReadOnlyList<MapEntry> Maps, Pack? Pack, string? OnlyFile = null, Pack? SourcePack = null)
{
    public MapEntry Map => Maps[0];   // keeps Part A call sites compiling
}
public sealed record PlatformSave(string PackName, string SetFolder, bool ApplyToGame, IReadOnlyList<MapEntry> Maps);

// PlatformEditorViewModel
public IReadOnlyList<PlatformPieceViewModel> Pieces { get; }        // current map's rows (raise PropertyChanged on map change)
public string MapStripText { get; }                                   // "2 of 3 maps"
public string CurrentMapName { get; }
public bool HasManyMaps { get; }
public IRelayCommand PreviousMapCommand { get; } public IRelayCommand NextMapCommand { get; }
public string SaveProgressText { get; }                               // "Saving 3 of 59 maps into Default"
public bool IsSaving { get; }
public IRelayCommand CancelSaveCommand { get; }
```

- [ ] **Step 1: Map set.** Hold `IReadOnlyList<MapSet>` where `MapSet(MapEntry Map, IReadOnlyList<PlatformPieceViewModel> Rows, string? BackgroundPath, PlatformEditRecord? Record, string ValuesFromText, bool HasValuesFrom)`, built in the ctor with Part A's per-map logic (source pack per map through `SourcePackFinder.ForPlatforms` when `request.Pack` is null; `MainViewModel` passes the per-map status dictionary, so `PlatformEditorRequest` gains `IReadOnlyDictionary<string, MapStatus> Statuses` or the ctor takes `ScanSnapshot`; pick the ctor taking `snapshot.MapStatuses` and `snapshot.Packs`). `CurrentIndex` switches `Pieces`, `SourceText`, `ValuesFromText`, `_backgroundPath`, then `SchedulePreview()`. `IsMixed` and the Opacity/Hue setters iterate every set's ticked rows. `ReplaceAsync`/`RecutAsync` run per map with each map's own `Box` and placements. Ticks stay per map. `Start fresh` acts on the current map.
- [ ] **Step 2: Save loop.** `SaveAsync`: Confirm once when any destination folder already has non-working-copy files ("Replace platforms?" with "{pack} already has platforms for {N maps}. Replace them?" for many). Then `IsSaving = true`, a `CancellationTokenSource`, and for `i` in maps: `SaveProgressText = $"Saving {i + 1} of {N} maps into {pack}"`, write the rows, write the record entry set, check `ct` between maps. On cancel: stop, `Saved = new PlatformSave(pack, folderOfFirst, ApplyNow, writtenSoFar)`, close accepted (written maps stay). Error dialog as today.
- [ ] **Step 3: Shell.** `OpenPlatformEditorAsync(maps, pack, onlyFile)`: after accept and rescan, when `ApplyToGame` and the target pack is found: `ApplySetAsync(target, saved.Maps, clearTicks: false)` (it already takes a list and captures one undo over all target paths; done line "Default applied to 3 maps" already comes from its count branch). `MapsViewModel.BuildCardMenu`: Edit platforms row `IsEnabled: true`, command `Shell.OpenPlatformEditorAsync(target, null)`. Panel row `Edit` and other single-map callers pass `[map]`.
- [ ] **Step 4: XAML.** Above the preview `Viewbox` a strip `Grid` visible through `HasManyMaps`: `Button` "Previous map" (glyph, `AutomationProperties.Name="Previous map"`), `TextBlock` `CurrentMapName`, `TextBlock` `MapStripText` (hint style), `Button` "Next map". Bottom bar: a `TextBlock` bound to `SaveProgressText` and a `Button` "Cancel" (`CancelSaveCommand`, `AutomationProperties.Name="Cancel save"`) visible through `IsSaving`; the Save and Cancel buttons disabled while saving.
- [ ] **Step 5: Build, format, all tests. Commit** "Platform editor: edit a set of maps with a map strip and one save run".

---

## Task 5: Bug 8 fix: written maps' cards reload first

Spec 11.

**Files:**
- Create: `src\BhMaps.Core\Maps\LoadOrder.cs`, test `tests\BhMaps.Core.Tests\LoadOrderTests.cs`
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`RescanAsync(IReadOnlyList<string>? writtenFolders = null)`; `RunWriteCoreAsync` passes the folders the write touched: derive from `undoPaths` with `AssetPath.FolderOf` distinct, excluding "Backgrounds"; a background write passes the maps whose slots it wrote, from the caller: add `IReadOnlyList<string>? writtenFolders` to `RunGameWriteAsync`, default derived from undo paths)
- Modify: `src\BhMaps.App\ViewModels\Pages\PageViewModel.cs` (`Refresh(ScanSnapshot snapshot)` stays; add `virtual void Refresh(ScanSnapshot snapshot, IReadOnlyList<string>? writtenFolders) => Refresh(snapshot)`)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`Refresh` override with the folders; `LoadPreviewsAsync` order through `LoadOrder.Prioritise`; if Task 1 found a cache key problem, also clear the written maps' `Previews` entries: `Services.Previews` has an `Invalidate`/`Clear` per level or gets one)

**Interfaces:**

```csharp
namespace BhMaps.Core.Maps;
public static class LoadOrder
{
    /// <summary>The names in first, in their given order, that occur in all (ignoring case), followed by the rest
    /// of all in its own order. Names in first that are not in all are dropped.</summary>
    public static IReadOnlyList<string> Prioritise(IReadOnlyList<string> all, IReadOnlyList<string>? first);
}
```

- [ ] **Step 1: Test** `LoadOrderTests`: `Prioritise(["A","B","C","D"], ["c","A"])` is `["C","A","B","D"]`; null first returns all unchanged; unknown names dropped. Implement, pass.
- [ ] **Step 2: Wire.** `RunWriteCoreAsync` computes `written` and calls `await RescanAsync(written)`. `RescanAsync` loops `page.Refresh(snapshot, writtenFolders)`. `MapsViewModel.Refresh(snapshot, written)`: build cards as today (a new card has `Preview` null, so nothing from before the write is shown), then `LoadPreviewsAsync` over `_all` ordered by `LoadOrder.Prioritise(folderNames, written)`. Apply what Task 1's `bug8-repro.md` concluded about the cache: if the key did not change, invalidate the written levels in `Services.Previews` before loading.
- [ ] **Step 3: Verify** the repro from Task 1 no longer reproduces (same dev tree steps, case 1), and record the result in the ledger.
- [ ] **Step 4: Build, format, all tests. Commit** "Maps: cards of the maps a write touched reload first".

---

## Risks and open questions

- `SpanFitter.Cut` with an asset whose W or H is 0 uses the piece's own size; the compositor does the same. Mirrored assets (W or H negative in the level data) flip inside `Local` through the compositor's centre flip; `Placements` must fold that flip into the matrix (scale -1 about the rect centre) so the cut mirrors the same way `MapCompositor.DrawAsset` draws.
- The joined preview is the real composite from the temp set, so once the rows hold their cuts the stage shows the join for free. `Fitted` exists for a later overlay and may stay unused; drop it if nothing calls it by the end of Task 3.
- Many maps with one `_tempRoot`: rows of different maps have different relative paths (their own folders), so one temp root serves all; a piece shared between two maps by relative path (theme folders) would collide, and themed nodes are skipped anyway.
- Task 1 may find that the pack-only case is the one the owner saw; then Task 5 still lands (it is right on its own) and the wording fix is Part A's Values from line.
