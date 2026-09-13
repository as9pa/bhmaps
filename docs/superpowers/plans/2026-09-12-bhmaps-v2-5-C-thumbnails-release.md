# BhMaps 2.5 Part C Implementation Plan: map-select thumbnails, manual, version

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An opt-in switch makes every game write that changes a map's art also write that map's composed picture over its map-select thumbnail in the game's `images\thumbnails` folder, keeping the original and putting it back on Reset, Undo or when the switch goes off; then the manual and the version move to 2.5.

**Architecture:** The level data parser reads `ThumbnailPNGFile`, the catalog decides which maps own a thumbnail file outright, and a Core `ThumbnailWriter` plans, renders and writes the JPEG. The one game write path (`RunWriteCoreAsync`) gets an optional list of the maps whose art the write touches and a reset flag; the thumbnail step runs inside the same write, after the caller's work, so undo, the busy boundary and the rescan already cover it. Undo sessions gain a thumbnails side next to Part A's library side.

**Tech Stack:** .NET 10, WPF imaging (`MapCompositor.Render`, `TransformedBitmap`, `JpegBitmapEncoder`), CommunityToolkit.Mvvm, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-12-bhmaps-v2-5-design.md` section 10 (binding). Parts A and B are merged on the branch before this plan runs.

**Branch:** `feature/bhmaps-v2.5`.

## Global Constraints

- Format with `dotnet format BhMaps.slnx` only. Build and test with `--artifacts-path <ART>`. TreatWarningsAsErrors on.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3).
- New `.cs` and `.xaml` files CRLF. Docs CRLF, UTF-8 without BOM. No em-dashes, no emoji.
- WPF: `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute on every focusable control added; no bare `x:Static` const int into a double; `[ObservableProperty]` setters run `OnXChanged` during construction.
- Never run the app or tests against the real game folder, the real library or `%APPDATA%\BhMaps`. The game's own files other than `.png` and `.jpg` are never written. The thumbnail write is the one write outside `mapArt` the app makes, and only into `<GameRoot>\images\thumbnails\`, only over a `.jpg` that already exists there.
- One commit per task, message from a file with `git commit -F`, trailers as the dispatcher gives them.
- Names from Parts A and B consumed here: `UndoSession.CaptureLibrary(string libraryPath, IEnumerable<string> relativePaths)` and `UndoStore.Restore(UndoSession session, string gamePath, string libraryPath)` (Part A Task 2); `RunGameWriteAsync(label, undoPaths, work, doneText, clearTicks = false, packName = null, libraryUndoPaths = null)` (Part A Task 5); `RescanAsync(IReadOnlyList<string>? writtenFolders = null)` (Part B Task 5); `PlatformSave.Maps` (Part B Task 4).

---

## Task 1: Level data carries the thumbnail file

Spec 10.1.

**Files:**
- Modify: `src\BhMaps.Core\LevelData\LevelModels.cs` (`LevelType` :39)
- Modify: `src\BhMaps.Core\LevelData\LevelTypesParser.cs` (`ParseTypes` :10-18)
- Modify: `src\BhMaps.Core\LevelData\LevelDataCache.cs` (`SchemaVersion` :12)
- Modify: `src\BhMaps.Core\Maps\MapCatalog.cs` (`MapEntry` :10-17, `Build` :79-101, `FolderEntry` :118-129, `Entry` :131-)
- Test: `tests\BhMaps.Core.Tests\LevelTypesParserTests.cs`, `tests\BhMaps.Core.Tests\MapCatalogTests.cs`

**Interfaces:**

```csharp
public sealed record LevelType(string LevelName, string DisplayName, bool DevOnly, bool TestLevel, string? ThumbnailFile = null);
// MapEntry gains a trailing optional positional parameter, so every existing `new MapEntry(7 args)` compiles:
public sealed record MapEntry(..., IReadOnlyList<string> PlatformFiles, string? ThumbnailFile = null);
public const int SchemaVersion = 3;
```

`ThumbnailFile` on `LevelType`: `Read(e, "ThumbnailPNGFile")`, kept verbatim (the game names a `.jpg` in this field; the name is used as given), null when absent or empty. On `MapEntry`: in `Build`, collect for every map the set of `ThumbnailFile` values of its included levels (ignoring null, OrdinalIgnoreCase); the map's file is the single value when the set has exactly one entry and no other map's set contains it; otherwise null. `FolderEntry` passes null.

- [ ] **Step 1: Tests.** In `LevelTypesParserTests`: `ParseTypes_ReadsThumbnailFileFromAttributeOrChild` (one element with the attribute, one with a child element, one with neither: values `"Brawlhaven.jpg"`, `"Grove.jpg"`, null). In `MapCatalogTests`: `Build_NamesTheThumbnailFileWhenOneMapOwnsIt` (two levels of one folder both naming `A.jpg` gives `A.jpg`), `Build_DropsAThumbnailFileTwoMapsShare` (folders X and Y each with a level naming `Shared.jpg`: both null), `Build_DropsAThumbnailFileWhenAMapsLevelsDisagree` (one folder, levels naming `A.jpg` and `B.jpg`: null). Look at the existing tests in each file for how levels and types are built.
- [ ] **Step 2: Run, expect failure. Implement. Run all Core tests (LevelDataCacheTests has a version check; update the expectation it carries if it hardcodes 2).**
- [ ] **Step 3: Format, build. Commit** "Level data: level types carry the map-select thumbnail file".

---

## Task 2: Core ThumbnailWriter and the undo thumbnails side

Spec 10.3, and the undo half of 10.4.

**Files:**
- Create: `src\BhMaps.Core\Operations\ThumbnailWriter.cs`
- Modify: `src\BhMaps.Core\Operations\UndoStore.cs` (`UndoSession` :7-92, `UndoStore.Restore` :134-170; Part A Task 2 already added the library side, mirror it)
- Modify: `src\BhMaps.Core\Operations\PackApplier.cs` (add `MapsTouched`)
- Test: `tests\BhMaps.Core.Tests\ThumbnailWriterTests.cs`, additions to `UndoStoreTests.cs` and `PackApplierTests.cs`

**Interfaces:**

```csharp
namespace BhMaps.Core.Operations;

public enum ThumbnailSkip { None, Shared, NoFile, Missing }

/// <summary>Where a map's thumbnail lives and where its original is kept.</summary>
public sealed record ThumbnailTarget(string FileName, string TargetPath, string OriginalPath);

/// <summary>Target when the map's thumbnail can be written; otherwise the reason, and for Shared the other map's
/// display name.</summary>
public sealed record ThumbnailPlan(ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap);

public static class ThumbnailWriter
{
    public const int Width = 290;
    public const int Height = 164;
    public const int RenderWidth = 580;
    public const int RenderHeight = 328;
    public const int JpegQuality = 88;
    public const string OriginalsFolderName = "thumbnails-original";

    /// <summary>gameRoot\images\thumbnails.</summary>
    public static string ThumbnailsDir(string gameRoot);
    /// <summary>appDataDir\thumbnails-original.</summary>
    public static string OriginalsDir(string appDataDir);

    /// <summary>Shared when another map's included levels name the same file (OtherMap is that map's DisplayName);
    /// NoFile when the map names none; Missing when the jpg is not in the thumbnails folder.</summary>
    public static ThumbnailPlan Plan(MapEntry map, IReadOnlyList<MapEntry> allMaps, string gameRoot, string appDataDir);

    /// <summary>Renders the map from the game folder at RenderWidth by RenderHeight and returns the frozen
    /// composite. Runs on any thread (MapCompositor.Render is thread free once the sources are read).</summary>
    public static BitmapSource Render(MapEntry map, string gamePath);

    /// <summary>Scales the composite to Width by Height and writes JPEG at JpegQuality through AtomicFile.</summary>
    public static void Write(BitmapSource composite, string targetPath);

    /// <summary>Copies the jpg to originalPath only when no copy exists yet. Creates the folder.</summary>
    public static void KeepOriginal(ThumbnailTarget target);

    /// <summary>Copies the kept original back over the target when one exists. Returns true when it did.</summary>
    public static bool RestoreOriginal(ThumbnailTarget target);

    /// <summary>Every kept original copied back into thumbnailsDir, the copies deleted. Returns the file names
    /// written, so the caller can name them as undo paths first.</summary>
    public static IReadOnlyList<string> RestoreAll(string originalsDir, string thumbnailsDir);

    /// <summary>The file names RestoreAll would write, without writing: for the undo capture before it.</summary>
    public static IReadOnlyList<string> KeptOriginals(string originalsDir);
}

// UndoSession
public void CaptureThumbnails(string thumbnailsDir, IEnumerable<string> fileNames);   // under <session>\thumbnails\, absent list _thumbnails_absent.txt
// UndoStore
public ApplyResult Restore(UndoSession session, string gamePath, string libraryPath, string thumbnailsDir);  // the 3-arg overload calls this with thumbnailsDir null-equivalent: make the 4-arg the implementation and have the 3-arg pass "" meaning no thumbnails side

// PackApplier
/// <summary>The maps whose folder the pack has files for, or one of whose background slots the pack's Backgrounds
/// folder holds. In catalog order.</summary>
public static IReadOnlyList<MapEntry> MapsTouched(Pack pack, IReadOnlyList<MapEntry> maps);
```

`Plan`: `NoFile` when `map.ThumbnailFile is null` and no other map's included levels name a file the map's levels also name; `Shared` when `map.ThumbnailFile is null` and another map in `allMaps` has a level whose `ThumbnailFile` equals one of this map's levels' `ThumbnailFile` (OrdinalIgnoreCase). Since `MapEntry.ThumbnailFile` already folds "shared" to null, detect Shared by scanning `map.Levels.Select(l => l.LevelName)` against `allMaps` level types: `MapEntry` does not carry `LevelType`, so `Plan` takes the map's own levels' thumbnail names through a new `MapEntry` member added in Task 1: extend Task 1's `MapEntry` with `IReadOnlyList<string> ThumbnailCandidates` (every distinct file its included levels name, possibly empty) as a second trailing optional parameter (`IReadOnlyList<string>? ThumbnailCandidates = null`, read through `Candidates => ThumbnailCandidates ?? []`). Then `Shared` is: `map.ThumbnailFile is null && map.Candidates.Count > 0 && allMaps.Any(o => o != map && o.Candidates.Intersect(map.Candidates, OrdinalIgnoreCase).Any())`, `OtherMap` the first such map's `DisplayName`. `Missing` when the target file does not exist.

- [ ] **Step 1: Tests** (`TempDir`, `SyntheticImage`, `LevelXml` helpers; a `MapEntry` built by hand):
  - `Plan_ReturnsTheTargetWhenTheFileExists` (creates `images\thumbnails\A.jpg` under a temp game root; asserts `TargetPath` and `OriginalPath = <appdata>\thumbnails-original\A.jpg`).
  - `Plan_SkipsMissingSharedAndNoFile` (three maps: file absent, two candidates shared, no candidates).
  - `Write_ProducesA290By164Jpeg` (render a 580 by 328 solid bitmap, Write, decode, assert size and `JpegBitmapDecoder` succeeds).
  - `KeepOriginal_CopiesOnceAndNeverOverwrites` (keep, change the target, keep again, original still the first bytes).
  - `RestoreAll_CopiesBackAndDeletesTheCopies` (two kept originals; after RestoreAll the thumbnails folder holds the originals' bytes and the originals folder is empty; returned names are the two).
  - `Render_ComposesTheMapFromTheGameFolder` (a one-platform level with a red piece PNG in a temp game folder: the composite has a red pixel; size 580 by 328).
  - `UndoStoreTests`: `Thumbnails_side_restores_and_deletes` (capture one existing and one absent name, change the folder, Restore 4-arg puts back and deletes), `Restore_without_thumbnails_dir_ignores_that_side`.
  - `PackApplierTests`: `MapsTouched_ListsFolderAndBackgroundMatches` (pack with folder X and `Backgrounds\BG_A.jpg`; maps X, Y (slot `Backgrounds\BG_A.jpg`), Z: result X, Y).
- [ ] **Step 2: Run, expect failures. Implement.** `Write`: `new TransformedBitmap(composite, new ScaleTransform(Width / (double)composite.PixelWidth, Height / (double)composite.PixelHeight))`, `RenderOptions` not needed; encode with `JpegBitmapEncoder { QualityLevel = JpegQuality }` into a `MemoryStream`, then `AtomicFile.WriteAllBytes(targetPath, bytes)` (add `WriteAllBytes` to `Storage.AtomicFile` beside `WriteAllText`, same temp-and-move). `Render`: `MapCompositor.Render(map.BaseLevel, RenderWidth, RenderHeight, new AssetSources(gamePath))`.
- [ ] **Step 3: Run all tests, format, build. Commit** "Core: ThumbnailWriter plans, renders and writes map-select thumbnails; undo gains a thumbnails side".

---

## Task 3: The setting and the Settings row

Spec 10.2, except the restore write, which Task 4 wires (it needs the game write changes).

**Files:**
- Modify: `src\BhMaps.Core\Settings\AppSettings.cs` (add `bool WriteGameThumbnails = false` as the last positional parameter)
- Modify: `src\BhMaps.Core\Settings\SettingsStore.cs` (known keys :20-22 add `"writeGameThumbnails"`; `Load` :95 add `Bool(obj, "writeGameThumbnails")`; `Save` :115 add `["writeGameThumbnails"] = settings.WriteGameThumbnails`)
- Modify: `src\BhMaps.App\ViewModels\Pages\SettingsPageViewModel.cs` (`WriteGameThumbnails` property, `OnWriteGameThumbnailsChanged` saves through `Save(Services.Settings with {...})` then raises `Shell.ThumbnailSwitchChangedAsync(value)`, a method Task 4 fills; add it now as `public Task ThumbnailSwitchChangedAsync(bool on) => Task.CompletedTask;` on `MainViewModel`)
- Modify: `src\BhMaps.App\Views\Pages\SettingsPageView.xaml` (new row between "Defaults" row 3 and "Applying" row 4: renumber rows 4 and 5 to 5 and 6 and add a `RowDefinition`)
- Test: `tests\BhMaps.Core.Tests\SettingsStoreTests.cs`

- [ ] **Step 1: Test** `WriteGameThumbnails_RoundTripsAndDefaultsOff`: Save with true, Load reads true; a file without the key loads false. Implement the Core half. Run.
- [ ] **Step 2: Settings row.** Row label `Text="Map-select thumbnails"`. Cell `StackPanel Style="{StaticResource SettingCell}"` holding a `CheckBox` `Content="Also update the game's map-select thumbnails"` `AutomationProperties.Name="Also update the game's map-select thumbnails"` `IsChecked="{Binding WriteGameThumbnails}"` `FocusVisualStyle="{StaticResource DialogFocusRing}"` (use the app's CheckBox style if one is keyed in `App.xaml`, grep `TargetType="CheckBox"`), and under it a `TextBlock Style="{StaticResource SettingValue}" TextWrapping="Wrap"` with the text: "When on, a game write that changes a map's art also writes the map's picture over its thumbnail in the game's images\thumbnails folder. The original is kept and comes back with Reset or when this is turned off." No actions column. Refresh sets `WriteGameThumbnails = Services.Settings.WriteGameThumbnails` under a `_refreshing` guard so the setter does not re-save.
- [ ] **Step 3: Build, format, all tests. Commit** "Settings: Map-select thumbnails switch, off by default".

---

## Task 4: Thumbnails inside the game write

Spec 10.4 and the restore-on-off write of 10.2.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`RunGameWriteAsync` :969, `RunWriteCoreAsync` :981-1048, `UndoAsync` :224, `ThumbnailSwitchChangedAsync`, new `ThumbnailNotes` dictionary, callers :355, :416, :465, :533)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (:234 Reset all, :257 Apply pack to maps, :318 Reset)
- Modify: `src\BhMaps.App\ViewModels\Pages\PacksViewModel.cs` (:243 whole pack apply)
- Modify: `src\BhMaps.App\ViewModels\PackDrawerViewModel.cs` (:141 apply to this map)
- Modify: `src\BhMaps.App\ViewModels\MapPanelViewModel.cs` (:234 Reset; `ThumbnailNote` read from the shell in the ctor :95)
- Modify: `src\BhMaps.App\Views\Pages\MapsView.xaml` (:833-834 panel header: a `TextBlock Style="{StaticResource QuietNote}" Text="{Binding ThumbnailNote}"` visible when non-empty, under `SetsText`)

**Interfaces:**

```csharp
// MainViewModel
public Task<bool> RunGameWriteAsync(
    string label, IReadOnlyList<string> undoPaths, Func<IProgress<string>, CancellationToken, Task> work, string doneText,
    bool clearTicks = false, string? packName = null, IReadOnlyList<string>? libraryUndoPaths = null,
    IReadOnlyList<MapEntry>? artMaps = null, bool resetThumbnails = false);

/// <summary>Panel notes by map folder, OrdinalIgnoreCase: "Map-select thumbnail not written: ...". Set by a write,
/// cleared for a map by its next write.</summary>
public IReadOnlyDictionary<string, string> ThumbnailNotes { get; }
public Task ThumbnailSwitchChangedAsync(bool on);
```

- [ ] **Step 1: The step.** In `RunWriteCoreAsync`, when `Services.Settings.WriteGameThumbnails && artMaps is { Count: > 0 }`: before the caller's work and after the game capture, `plans = artMaps.Select(m => (m, ThumbnailWriter.Plan(m, snapshot.Catalog.Maps, Services.GameRoot, Services.AppDataDir)))`; `session.CaptureThumbnails(ThumbnailsDir, plans with a Target -> FileName)`. After `await work(progress, ct)` succeeds, inside the same launcher boundary and `Task.Run`: for each planned target: `progress.Report("Map-select thumbnail " + map.DisplayName)`; `KeepOriginal(target)`; if `resetThumbnails`: `RestoreOriginal(target)`; else `Write(Render(map, gamePath), target.TargetPath)`. Count the writes. For every map in `artMaps`, replace its `ThumbnailNotes` entry: removed when written, set to the skip text when skipped (`Shared`: $"Map-select thumbnail not written: {map.DisplayName} shares its picture with {other}."; `NoFile`: $"Map-select thumbnail not written: no file is named for {map.DisplayName}."; `Missing`: "Map-select thumbnail not written: the file is missing from the game folder."). Done line: `doneText + " Map-select thumbnail updated."` for one write, `" Map-select thumbnails updated."` for more (append before `DoneLine`). Render happens on the worker thread; `MapCompositor.Render` already runs off the UI thread in `RenderQueue`, so call it directly inside the `Task.Run`.
- [ ] **Step 2: Undo.** `UndoAsync` calls the 4-arg `Restore(session, gamePath, Services.LibraryPath, ThumbnailWriter.ThumbnailsDir(Services.GameRoot))`.
- [ ] **Step 3: Callers.** Pass `artMaps`: MainViewModel :355 `maps`, :416 `targets`, :465 `targets`, :533 the maps whose `BackgroundSlots` contain `saved.Slot` (from the current snapshot's catalog); MapsViewModel :257 `maps`, :318 `maps` with `resetThumbnails: true`, :234 `snapshot.Catalog.Maps` with `resetThumbnails: true`; PacksViewModel :243 `PackApplier.MapsTouched(pack, snapshot.Catalog.Maps)`; PackDrawerViewModel :141 `[map]`; MapPanelViewModel :234 `[_map]` with `resetThumbnails: true`. The platform editor's Apply to game now goes through `ApplySetAsync`, so it is covered.
- [ ] **Step 4: Switch off.** `ThumbnailSwitchChangedAsync(false)`: `names = ThumbnailWriter.KeptOriginals(OriginalsDir)`; when empty return; else `RunGameWriteAsync("Restoring map-select thumbnails", [], (progress, ct) => Task.Run(() => ThumbnailWriter.RestoreAll(OriginalsDir, ThumbnailsDir), ct), "Map-select thumbnails restored")` where the write's capture must include the thumbnails side even though the setting is now off: add a private `IReadOnlyList<string>? thumbnailUndoNames` path through `RunWriteCoreAsync` (capture these names on the thumbnails side when given), and pass `names`. `ThumbnailSwitchChangedAsync(true)` does nothing (spec: never on its own).
- [ ] **Step 5: Panel.** `MapPanelViewModel` ctor: `ThumbnailNote = shell.ThumbnailNotes.GetValueOrDefault(map.FolderName, "")`, `HasThumbnailNote`. XAML under `SetsText`.
- [ ] **Step 6: Dev tree check.** Build, launch the built exe against the dev tree with `--quiet` (see Part B Task 1 for the launch rules; the dev tree needs an `images\thumbnails` folder with a jpg named as the LevelTypes name for BloodMoon; `scripts\make-dev-tree.ps1` gains a step copying `images\thumbnails\<names of the copied maps>.jpg` from the real install when `-RealArt`, read only). Turn the switch on through UIA, apply the demo pack to BloodMoon, confirm the jpg in the dev tree changed and `<dev>\appdata\thumbnails-original\` holds the original; Undo, confirm the bytes are back; switch off, confirm nothing else written. Record in the ledger. Close only the app you launched.
- [ ] **Step 7: Format, build, all tests. Commit** "Game writes update the map-select thumbnails when the switch is on, with undo and restore".

---

## Task 5: Manual, README, version 2.5.0

**Files:**
- Modify: `docs\manual.md` (new "## What is new in 2.5" above "## What is new in 2.4" :11; "Where things live" table :112-120 add `| Kept map-select thumbnails | %APPDATA%\BhMaps\thumbnails-original |`; Settings bullet :236-240 add the switch; Platform editor paragraphs :279 area add the Fit switch, drag pan, Values from line, Start fresh, map strip; Background editor paragraph add Values from; "How it works" :329 add one paragraph on the records `platforms.bhmaps.json` and `backgrounds.bhmaps.json` inside a pack and on the thumbnail write being the one write outside mapArt)
- Modify: `README.md` (:13-14 file names to `bhmaps-v2.5.0-win-x64.exe` and `bhmaps-v2.5.0-win-x64-dotnet.zip`; "What it does" gets one bullet for the editors remembering their values and one for the thumbnail switch)
- Modify: `src\BhMaps.App\BhMaps.App.csproj` (`<Version>2.5.0</Version>` :12)

"What is new in 2.5" bullets, in this order, plain prose (write them out fully; the items are: the platform and background editors remember what was saved into a pack and reopen with those values, with a "Values from" line and Start fresh; Replace lays one picture across the platforms by default, with a drag to move it and an "On each piece" switch; Edit platforms works on a selection of maps with a map strip and one Save that writes them all, with a progress line and Cancel; Reset to default also clears the editors' memory for that map, and Undo brings it back; Settings has an off-by-default "Map-select thumbnails" switch that writes each changed map's picture over its thumbnail in the game's map select, keeps the original, and restores it with Reset, Undo or when switched off; a card no longer shows an old picture right after a write).

- [ ] **Step 1: Edit the three files.** Keep CRLF and no BOM (check with `file` or a python read; convert with python if the editor changed them).
- [ ] **Step 2: Build with `--artifacts-path <ART>`; confirm the Settings page Version row would read 2.5.0 (the `Version` property reads the assembly's informational version).**
- [ ] **Step 3: Commit** "BhMaps 2.5: manual, readme, version".

---

## Risks

- `MapCompositor.Render` on a worker thread: it builds `DrawingVisual` and `RenderTargetBitmap`, which WPF allows on any STA or MTA thread as long as the objects stay on that thread and the result is frozen; `RenderQueue` already does this. Task 2's `Render` freezes the result.
- The thumbnails folder is outside `mapArt`; the undo thumbnails side keeps it out of the game-relative capture, so `..` never enters an undo path.
- A game update replaces the thumbnails; a kept original then predates the update. Restore still copies it back; the owner accepts this for 2.5 (spec 13).
