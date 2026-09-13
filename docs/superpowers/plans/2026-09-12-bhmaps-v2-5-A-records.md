# BhMaps 2.5 Part A Implementation Plan: records, editor memory, reset, background memory

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The platform and background editors write a json record beside the pictures they save, load it when they open (choosing the pack by hash match), start from the original art, and lose the record when the map is reset to default, with undo.

**Architecture:** One shared reader and writer (`EditRecordFile`) in Core serves two record types (`PlatformEditRecord`, `BackgroundEditRecord`). The editors' view models read the record in their constructors and write it in Save. The undo session gains a library side so record removal inside Reset restores with the game files. `MainViewModel` picks the source pack from the current scan's hash matches and hands it to the editor on the request.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, System.Text.Json, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-12-bhmaps-v2-5-design.md` (binding; "spec N" below cites its sections). Part A covers spec sections 3, 4, 5, 7 and 8. Part B covers 6, 9 and 11; Part C covers 10 and the release.

**Branch:** `feature/bhmaps-v2.5` (already checked out from main 717a36e).

## Global Constraints

- Format with `dotnet format BhMaps.slnx` only. Build and test with `--artifacts-path <ART>` where `<ART>` is the path the dispatcher gives you. TreatWarningsAsErrors is on.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3). Every new Core class gets tests. No tests in the App project.
- New `.cs` and `.xaml` files use CRLF. No em-dashes and no emoji in code comments, UI text, docs or commit messages.
- WPF: every focusable control you add carries `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute. No bare `x:Static` const int into a double property. `[ObservableProperty]` setters run their `OnXChanged` partial during construction, so guard them.
- Never run the app or tests against the real game folder, the real library or the real `%APPDATA%\BhMaps`. Tests use `TempDir`. Manual checks use the dev tree only.
- The records are never required: every code path works when the file is absent or unreadable (spec 3.1).
- One commit per task. Commit message in a file, `git commit -F <file>`, ending with the trailer lines the dispatcher gives you.
- Record entry keys are relative paths with backslashes exactly as `MapEntry.PlatformFiles` and `AssetPath.Background` produce them; compare ordinal, ignoring case.

---

## Task 1: Core records: EditRecordFile, PlatformEditRecord, BackgroundEditRecord

Spec 3.1, 3.2.

**Files:**
- Create: `src\BhMaps.Core\Packs\EditRecordFile.cs`
- Create: `src\BhMaps.Core\Packs\PlatformEditRecord.cs`
- Create: `src\BhMaps.Core\Packs\BackgroundEditRecord.cs`
- Test: `tests\BhMaps.Core.Tests\EditRecordTests.cs`

**Interfaces:**
- Consumes: `BhMaps.Core.Hashing.FileHasher.Hash(string path)` (lowercase hex SHA-256).
- Produces (later tasks rely on these exact names):

```csharp
namespace BhMaps.Core.Packs;

/// <summary>Reads and writes the json records a pack keeps beside its pictures. Missing or unreadable files read
/// as empty; writes go to a temporary file and move over the old one.</summary>
public static class EditRecordFile
{
    public static readonly JsonSerializerOptions Options; // camelCase, indented, ignore null, JsonStringEnumConverter (camelCase)

    /// <summary>Null when the file is missing or does not parse.</summary>
    public static T? Read<T>(string path) where T : class;

    public static void Write<T>(string path, T record);
}

public enum PlatformArt { Own, EachPiece, Across, WorkingCopy }

public sealed class PlatformPieceEntry
{
    public int? Opacity { get; set; }
    public int? Hue { get; set; }
    public PlatformArt Art { get; set; }
    public string? Picture { get; set; }
    public double? PanX { get; set; }
    public double? PanY { get; set; }
    public string Hash { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PlatformMapEntry
{
    public DateTimeOffset SavedAt { get; set; }
    public Dictionary<string, PlatformPieceEntry> Pieces { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PlatformEditRecord
{
    public const string FileName = "platforms.bhmaps.json";
    public int Version { get; set; } = 1;
    public Dictionary<string, PlatformMapEntry> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string PathFor(string packRoot) => Path.Combine(packRoot, FileName);
    /// <summary>Empty record when the file is missing or unreadable.</summary>
    public static PlatformEditRecord Load(string packRoot);
    public void Save(string packRoot);
    public PlatformPieceEntry? Entry(string mapFolder, string relativePath);
    public PlatformMapEntry? Map(string mapFolder);
    /// <summary>Replaces the map's whole entry set (spec 4, last bullet).</summary>
    public void SetMap(string mapFolder, DateTimeOffset savedAt, IReadOnlyDictionary<string, PlatformPieceEntry> pieces);
    public bool RemoveMap(string mapFolder);
}

public enum BackgroundMode { Cover, Contain, Stretch }

public sealed class BackgroundSlotEntry
{
    public DateTimeOffset SavedAt { get; set; }
    public string Picture { get; set; } = "";
    public BackgroundMode Mode { get; set; }
    public double PanX { get; set; } = 0.5;
    public double PanY { get; set; } = 0.5;
    public double Darken { get; set; }
    public string Hash { get; set; } = "";
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class BackgroundEditRecord
{
    public const string FileName = "backgrounds.bhmaps.json";
    public int Version { get; set; } = 1;
    public Dictionary<string, BackgroundSlotEntry> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static string PathFor(string packRoot) => Path.Combine(packRoot, FileName);
    public static BackgroundEditRecord Load(string packRoot);
    public void Save(string packRoot);
    public BackgroundSlotEntry? Entry(string relativePath);
    public void Set(string relativePath, BackgroundSlotEntry entry);
    public bool Remove(string relativePath);
}
```

Notes: `System.Text.Json` deserialises into `Dictionary<string, T>` with the default comparer; after `Read`, `Load` must rebuild the dictionaries with `StringComparer.OrdinalIgnoreCase` (a small `Normalise()` on each record). Enum values serialise camelCase (`"across"`, `"workingCopy"`, `"cover"`) via `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. `Write` uses `File.WriteAllText(tmp, json, new UTF8Encoding(false))` then `File.Move(tmp, path, overwrite: true)`, creating the folder first.

- [ ] **Step 1: Write the failing tests** in `tests\BhMaps.Core.Tests\EditRecordTests.cs` (use the existing `TempDir` helper):

```csharp
[Fact] public void Platform_record_round_trips()
// Set one map with two pieces (one Across with picture and pan, one WorkingCopy with hash only), Save, Load,
// assert every field equal, assert Entry("bloodmoon", "BLOODMOON\\platform_bm1.png") finds the piece (case).

[Fact] public void Missing_file_loads_empty()
// Load on an empty temp folder: Maps.Count == 0, no throw. Same for BackgroundEditRecord.

[Fact] public void Unreadable_file_loads_empty()
// Write "{ not json" to platforms.bhmaps.json, Load: Maps.Count == 0, no throw.

[Fact] public void Unknown_fields_survive_round_trip()
// Write json by hand with "future": 1 at root, map and piece levels; Load; Save; read the text back and assert
// all three "future" properties are still present.

[Fact] public void Remove_map_drops_only_that_map()
// Two maps, RemoveMap("A") true, Maps has only "B"; RemoveMap("A") again false.

[Fact] public void Working_copy_entry_writes_only_art_and_hash()
// Save a WorkingCopy entry; the file text contains "\"art\": \"workingCopy\"" and does not contain "opacity".

[Fact] public void Background_record_round_trips()
// Set "Backgrounds\\BG_Sewer.jpg" with Contain, 0.2, 0.8, 35, Save, Load, assert; the text contains "\"mode\": \"contain\"".

[Fact] public void Write_leaves_no_temp_file()
// After Save, the pack root holds exactly one file.
```

- [ ] **Step 2: Run the tests, expect compile failure** (types missing):

```
dotnet test tests\BhMaps.Core.Tests --artifacts-path <ART> --filter EditRecordTests
```

- [ ] **Step 3: Implement** the three files per the Interfaces block. `Load` = `EditRecordFile.Read<T>(PathFor(packRoot)) ?? new T()` then `Normalise()`. `Read` catches `IOException`, `UnauthorizedAccessException`, `JsonException` and returns null; a missing file returns null without opening it.

- [ ] **Step 4: Run the tests, expect pass.** Then `dotnet format BhMaps.slnx` and `dotnet build BhMaps.slnx --artifacts-path <ART>` clean.

- [ ] **Step 5: Commit** "Core: pack edit records with a shared json reader and writer".

---

## Task 2: Export carries the records; undo gains a library side

Spec 3.2 (export), spec 7 (undo library side).

**Files:**
- Modify: `src\BhMaps.Core\Operations\PackExporter.cs` (whole file, 11 lines)
- Modify: `src\BhMaps.Core\Operations\UndoStore.cs` (`UndoSession`: add `CaptureLibrary`; `UndoStore.Restore`: add the three-argument overload)
- Test: `tests\BhMaps.Core.Tests\PackExporterTests.cs` (create if absent), `tests\BhMaps.Core.Tests\UndoStoreTests.cs` (extend)

**Interfaces:**
- Consumes: `PlatformEditRecord.FileName`, `BackgroundEditRecord.FileName` (Task 1); `PackApplier.ApplyPack`, `ApplyResult`, `FileFailure`.
- Produces:

```csharp
// UndoSession
/// <summary>Copies library files (relative to libraryPath) under the session's "library" side; absent ones are
/// listed in _library_absent.txt so a restore deletes them. First capture of a path wins, as on the game side.</summary>
public void CaptureLibrary(string libraryPath, IEnumerable<string> relativePaths);

// UndoStore
/// <summary>Restores the game side under gamePath and, when the session has a library side, the library side
/// under libraryPath. Clears the session on zero failures.</summary>
public ApplyResult Restore(UndoSession session, string gamePath, string libraryPath);
// The existing Restore(session, gamePath) keeps working and restores the game side only.
```

Layout inside a session folder: game files as today at the root (unchanged), library files under `<session>\library\<relativePath>`, absent list `<session>\_library_absent.txt`. `Capture(gamePath, ...)` must not treat the `library` folder or the new list as game files: `CapturedFiles()` and the game restore already enumerate by relative path from the captured list, check that the `library` folder is skipped when the game restore walks the session root (read `CapturedFiles()` first and adjust if it enumerates the directory).

- [ ] **Step 1: Write failing tests.**

`PackExporterTests`:

```csharp
[Fact] public void Export_copies_records_when_present()
// Pack root with one folder "BloodMoon\\a.png", plus platforms.bhmaps.json and backgrounds.bhmaps.json text files.
// Build a Pack via PackScanner (or construct Pack with one GameFolder). Export to a temp destination.
// Assert both json files exist under destination\<pack name>\ with identical text, and a.png is copied.

[Fact] public void Export_without_records_copies_only_pictures()
// Same without json: destination has the folder and no json files; ApplyResult has zero failures.
```

`UndoStoreTests` additions:

```csharp
[Fact] public void Library_side_restores_and_deletes()
// libraryPath temp with packs\P\platforms.bhmaps.json = "old". Begin; CaptureLibrary(lib, ["packs\\P\\platforms.bhmaps.json", "packs\\P\\backgrounds.bhmaps.json"]) where the second is absent.
// Overwrite the first with "new", create the second. Restore(session, game, lib): first reads "old", second is gone, session cleared.

[Fact] public void Game_only_restore_ignores_library_side()
// Capture a game file and a library file; Restore(session, gamePath) (two args) restores the game file and
// leaves the changed library file as is.

[Fact] public void Session_without_library_side_restores_as_before()
// Existing behaviour: Restore(session, game, lib) on a game-only session works and clears.
```

- [ ] **Step 2: Run, expect failure** (`CaptureLibrary` missing, exporter does not copy json).

- [ ] **Step 3: Implement.** `PackExporter.Export`: call `PackApplier.ApplyPack` as today, then for each of the two file names, if `File.Exists(Path.Combine(pack.FullPath, name))` copy to `Path.Combine(destinationRoot, pack.Name, name)` inside a try that adds a `FileFailure` to the result on `IOException`/`UnauthorizedAccessException` (build a new `ApplyResult` merging failures; look at how `ApplyResult` is constructed in `PackApplier` and reuse that shape). `UndoSession.CaptureLibrary` mirrors `Capture` with the `library` sub-root and `_library_absent.txt`. `UndoStore.Restore(session, gamePath, libraryPath)`: run the game restore logic, then if `Directory.Exists(<session>\library)` or the absent list exists, copy back and delete; Clear only when both sides had zero failures.

- [ ] **Step 4: Run all Core tests, expect pass. Format. Build.**

- [ ] **Step 5: Commit** "Core: export carries the records, undo sessions gain a library side".

---

## Task 3: Platform editor Save writes the record

Spec 4.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\PlatformPieceViewModel.cs` (add `ReplacementPath`, `ArtKind` helper)
- Modify: `src\BhMaps.App\ViewModels\PlatformEditorViewModel.cs` (`ReplaceAsync` :509-551 keep the picked path; `SaveAsync` :903-933 write the record)

**Interfaces:**
- Consumes: `PlatformEditRecord`, `PlatformPieceEntry`, `PlatformArt` (Task 1); `FileHasher.Hash`.
- Produces:

```csharp
// PlatformPieceViewModel
/// <summary>Full path of the picture Replace loaded for this row; null for own art and working copies.</summary>
public string? ReplacementPath { get; private set; }
public void SetReplacement(BitmapSource fitted, string name, string sourcePath); // replaces the two-argument form
/// <summary>The record's art kind for this row's current state.</summary>
public PlatformArt ArtKind => Art switch
{
    PieceArt.WorkingCopy => PlatformArt.WorkingCopy,
    PieceArt.Replacement => PlatformArt.EachPiece,   // Part B adds Across
    _ => PlatformArt.Own,
};

// PlatformEditorViewModel
/// <summary>Builds the record entry set for the rows just written into packRoot (hash from the written file).</summary>
internal static Dictionary<string, PlatformPieceEntry> EntriesFor(IReadOnlyList<PlatformPieceViewModel> rows, string packRoot);
```

- [ ] **Step 1: `SetReplacement`** gains the `sourcePath` parameter and stores `ReplacementPath`; `ResetArt` clears it. Update the one caller in `ReplaceAsync` (`row.SetReplacement(fitted, name)` becomes `row.SetReplacement(fitted, name, path)`).

- [ ] **Step 2: `SaveAsync`**: after the `foreach (var row in rows) row.CopyOrWriteResult(...)` loop and still inside the `Task.Run`, build the entries and write the record:

```csharp
var record = PlatformEditRecord.Load(packRoot);
record.SetMap(_request.Map.FolderName, DateTimeOffset.Now, EntriesFor(rows, packRoot));
record.Save(packRoot);
```

`EntriesFor`: for each row, `var written = Path.Combine(packRoot, row.RelativePath)`; entry `Hash = FileHasher.Hash(written)`, `Art = row.ArtKind`; when `Art == WorkingCopy` nothing else; otherwise `Opacity = row.Opacity`, `Hue = row.Hue`, and for `EachPiece` `Picture = row.ReplacementPath`. Key: `row.RelativePath`. A row whose written file does not exist (copy failed silently) is skipped.

- [ ] **Step 3: Cancel path**: confirm nothing else writes (`Cleanup` deletes `_tempRoot` only). No change expected; state so in the report.

- [ ] **Step 4: Build, format.** No Core test covers view models; verify by a unit test on nothing new in Core. Manual check is deferred to the dispatcher's dev-tree pass.

- [ ] **Step 5: Commit** "Platform editor: Save writes the pack's platform record".

---

## Task 4: Platform editor open loads the record

Spec 5.1 to 5.4.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\PlatformEditorViewModel.cs` (`PlatformEditorRequest` :21, ctor :85-137, `BuildPieces` :394-411, new `StartFreshCommand`, `ValuesFromText`, `HasValuesFrom`)
- Modify: `src\BhMaps.App\ViewModels\PlatformPieceViewModel.cs` (add `Note` observable string, `LoadedFromRecord` flag)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`OpenPlatformEditorAsync` :619-669 resolves the source pack)
- Modify: `src\BhMaps.App\Views\PlatformEditorWindow.xaml` (Values from line above the Files header :137; row note under the readout inside the Pieces ItemsControl :158-245)
- Create: `src\BhMaps.Core\Packs\SourcePackFinder.cs` and `tests\BhMaps.Core.Tests\SourcePackFinderTests.cs`

**Interfaces:**
- Consumes: Task 1 records; `MapStatus`, `MapFileStatus`, `MapFileState.Pack`, `Pack`, `DefaultPack.Name`, `PackScanner.PacksRoot`, `AssetSources`.
- Produces:

```csharp
// Core
public static class SourcePackFinder
{
    /// <summary>Spec 5.1 step 2: the first pack, in the order the map's platform file statuses list them, whose
    /// platform record has an entry set for the map. Null when none.</summary>
    public static Pack? ForPlatforms(MapEntry map, MapStatus? status, IReadOnlyList<Pack> packs);

    /// <summary>Spec 8: the first pack listed for the slot's status whose background record has an entry for it.</summary>
    public static Pack? ForBackground(string slotRelativePath, MapStatus? status, IReadOnlyList<Pack> packs);
}

// App
public sealed record PlatformEditorRequest(MapEntry Map, Pack? Pack, string? OnlyFile = null, Pack? SourcePack = null);
// Pack: the pack the editor was opened with (2.4 meaning, rows resolve through it). SourcePack: the pack whose
// record is loaded; equals Pack when Pack is not null, else the hash-matched pack, else null.

// PlatformEditorViewModel
public string ValuesFromText { get; }      // "Values from Default, saved 12 Sep 22:01."
public bool HasValuesFrom { get; }         // line visible
public IRelayCommand StartFreshCommand { get; }

// PlatformPieceViewModel
[ObservableProperty] private string _note = "";   // "" hides the note
```

`ForPlatforms` reads `PlatformEditRecord.Load(pack.FullPath)` for candidates only; the candidate order is: for each `map.PlatformFiles` in order, `status.Files` entry with `State == MapFileState.Pack`, each of its `PackNames` in order, distinct ignoring case.

- [ ] **Step 1: Core test and finder.** `SourcePackFinderTests`: three packs in a temp library; status lists `["B", "A"]` for the first file; only `A` has a record for the map: `ForPlatforms` returns A. No record anywhere: null. Status null: null. `ForBackground` mirrors with a slot. Implement, run, pass.

- [ ] **Step 2: `MainViewModel.OpenPlatformEditorAsync`**: compute

```csharp
var status = snapshot.MapStatuses.GetValueOrDefault(map.FolderName);
var source = pack ?? SourcePackFinder.ForPlatforms(map, status, snapshot.Packs);
new PlatformEditorRequest(map, pack, onlyFile, source)
```

- [ ] **Step 3: `BuildPieces`** becomes an instance method taking the loaded record. Per row, with `entry = record?.Entry(map.FolderName, relativePath)`:
  - entry null or `Art == WorkingCopy`: 2.4 path (`AssetSources(GamePath, request.Pack?.FullPath).ResolveAsset`), sliders 100 and 0.
  - otherwise: `original = Path.Combine(PackScanner.PacksRoot(LibraryPath), DefaultPack.Name, relativePath)`; if missing, `original = Path.Combine(GamePath, relativePath)` and `Note = "The untouched art was not found, so the preview starts from the saved file."`; if that is missing too, skip the row as 2.4 does for unresolved files. Row `Opacity = entry.Opacity ?? 100`, `Hue = entry.Hue ?? 0`.
  - `Art == EachPiece`: if `File.Exists(entry.Picture)` load the picture and fit it (`PieceFitter.Fit(BackgroundFitter.LoadSource(entry.Picture), BackgroundFitter.LoadSource(original))`, off the UI thread: do it in a `LoadRecordArtAsync()` kicked from the ctor and awaited before the first preview; the ctor sets the row's `SetReplacement` when it finishes and calls `SchedulePreview()`). If the picture is missing: source = pack file (`Path.Combine(sourcePack.FullPath, relativePath)`), sliders 100 and 0, `Note = $"{Path.GetFileName(entry.Picture)}, missing. Showing the saved file."`.
  - `Art == Across`: Part A treats it as `EachPiece` (Part B replaces this branch). Leave a one-line comment naming Part B.
  - Changed outside: when the pack file exists and `FileHasher.Hash(packFile) != entry.Hash` and the row has no note yet: `Note = "The file changed since, so the preview may differ from the game."` Hashing runs in the same background load, not on the UI thread.
- [ ] **Step 4: Values from line.** In the ctor, when `request.SourcePack` is not null and its record has `Map(map.FolderName)`: `HasValuesFrom = true`, `ValuesFromText = $"Values from {SourcePack.Name}, saved {savedAt.ToLocalTime():d MMM HH:mm}."`, and `TargetPack = SourcePack.Name` when it is in `PackChoices`. `StartFreshCommand`: rebuild `Pieces` rows' state to the 2.4 resolution (reset each row: `ResetArt`, `Opacity = 100`, `Hue = 0`, `Note = ""`, and set `OriginalPath` back to the 2.4 resolution: add an internal `ResetOriginal(string path)` on the row), `HasValuesFrom = false`, `SchedulePreview()`. `Pieces` is an `IReadOnlyList` built once; keep the same row objects so bindings hold.

- [ ] **Step 5: XAML.** Above the Files header Grid (:137) add a `TextBlock`-plus-link row bound to `ValuesFromText` with visibility `HasValuesFrom` through `BoolToVis`, and a `Button` "Start fresh" styled `ResetLink` with `AutomationProperties.Name="Start fresh"` and the focus ring. Inside the row template, under the readout, a `TextBlock` bound to `Note` styled like `HintText`, collapsed when empty (use the existing string-empty converter if one exists in `App.xaml` resources; otherwise a `DataTrigger` on `Note` equal to "" setting `Visibility` Collapsed).

- [ ] **Step 6: Build, format, run all tests.** Commit "Platform editor: open loads the pack record, Values from line, Start fresh".

---

## Task 5: Reset to default clears the records, with undo

Spec 7.

**Files:**
- Create: `src\BhMaps.Core\Operations\RecordReset.cs`, test `tests\BhMaps.Core.Tests\RecordResetTests.cs`
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`RunGameWriteAsync` :969 and `RunWriteCoreAsync` :981-1048 gain `IReadOnlyList<string>? libraryUndoPaths`; `UndoAsync` :224 passes `Services.LibraryPath`)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`ResetAsync` :308-338)
- Modify: `src\BhMaps.App\ViewModels\MapPanelViewModel.cs` (`ResetAsync` :223)

**Interfaces:**
- Consumes: Task 1 records, Task 2 `CaptureLibrary` and `Restore(session, gamePath, libraryPath)`.
- Produces:

```csharp
public static class RecordReset
{
    /// <summary>The packs the map's files currently match (state Pack), distinct, in status order.</summary>
    public static IReadOnlyList<Pack> MatchedPacks(MapEntry map, MapStatus? status, IReadOnlyList<Pack> packs);

    /// <summary>Record paths relative to libraryPath for the undo capture: both record files of every matched pack.</summary>
    public static IReadOnlyList<string> UndoPaths(IReadOnlyList<Pack> matched, string libraryPath);

    /// <summary>Removes the map's platform entry set and its background slots from every matched pack's records.
    /// Saves only records that changed. Never throws for a missing record.</summary>
    public static void Clear(MapEntry map, IReadOnlyList<Pack> matched);
}

// MainViewModel
public Task<bool> RunGameWriteAsync(
    string label,
    IReadOnlyList<string> undoPaths,
    Func<IProgress<string>, CancellationToken, Task> work,
    string doneText,
    bool clearTicks = false,
    string? packName = null,
    IReadOnlyList<string>? libraryUndoPaths = null);
```

- [ ] **Step 1: Test `RecordResetTests`**: library with pack A (record with maps X and Y, background record with `Backgrounds\BG_X.jpg` and `Backgrounds\BG_Z.jpg`) and pack B (no records). Map X with slot `Backgrounds\BG_X.jpg`; status says its files match A and B. `MatchedPacks` returns [A, B]. `Clear`: A's platform record keeps only Y; A's background record keeps only BG_Z; B still has no record files (nothing created). `UndoPaths` returns four relative paths (`packs\A\platforms.bhmaps.json`, `packs\A\backgrounds.bhmaps.json`, `packs\B\...`). Implement, pass.

- [ ] **Step 2: `RunWriteCoreAsync`**: after `Services.Undo.Begin().Capture(gamePath, undoPaths)` capture the library side when `libraryUndoPaths` is not null and non-empty: `session.CaptureLibrary(libraryPath, libraryUndoPaths)` (keep the session in a local; `Begin()` returns it). `UndoAsync`: `Services.Undo.Restore(session, gamePath, Services.LibraryPath)`.

- [ ] **Step 3: Both `ResetAsync` methods**: compute `matched` per map from `snapshot.MapStatuses` and `snapshot.Packs`, pass `libraryUndoPaths: RecordReset.UndoPaths(allMatched, Shell.Services.LibraryPath)`, and inside the work, after each map's `MapReset.ResetMap`, call `RecordReset.Clear(map, matchedForMap)`. Applying a pack (`ApplySetAsync`, `ApplyPictureAsync`) is untouched.

- [ ] **Step 4: Build, format, tests. Commit** "Reset to default clears the pack records, undo restores them".

---

## Task 6: Background editor memory

Spec 8.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\BackgroundEditorViewModel.cs` (ctor :49; `SaveAsync` :242-272; new `ValuesFromText`, `HasValuesFrom`, `PictureGoneText`, `StartFreshCommand`)
- Modify: `src\BhMaps.App\ViewModels\BackgroundEditorRequest.cs` (add `Pack? SourcePack = null`)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`OpenBackgroundEditorAsync` :494 resolves the source pack; every caller that builds a `BackgroundEditorRequest` compiles unchanged thanks to the default)
- Modify: `src\BhMaps.App\Views\BackgroundEditorWindow.xaml` (line above the Fit radios :164)

**Interfaces:**
- Consumes: `BackgroundEditRecord`, `BackgroundSlotEntry`, `BackgroundMode` (Task 1); `SourcePackFinder.ForBackground` (Task 4); `FitMode`.
- Produces:

```csharp
public sealed record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot, Pack? SourcePack = null);

// BackgroundEditorViewModel
public string ValuesFromText { get; }   // "Values from Default, saved 12 Sep 22:03." or "sunset.jpg, missing. Showing the saved file."
public bool HasValuesFrom { get; }
public IRelayCommand StartFreshCommand { get; }
internal static BackgroundMode ToRecord(FitMode mode); internal static FitMode FromRecord(BackgroundMode mode);
```

- [ ] **Step 1: `MainViewModel.OpenBackgroundEditorAsync`**: when `request.Slot` is not null, find the map whose `BackgroundSlots` contains the slot (`snapshot.Catalog.Maps`), its status, and `source = packByName(request.PackName) ?? SourcePackFinder.ForBackground(AssetPath.Background(slot), status, snapshot.Packs)`; pass `request with { SourcePack = source }`. Check how `Slot` is spelled in `MapSlotChoice` (slot file name or relative path) and convert consistently; the record key is the relative path `Backgrounds\<file>`.

- [ ] **Step 2: Ctor load.** After `SelectedMap` is chosen, if `request.SourcePack` is not null and `BackgroundEditRecord.Load(SourcePack.FullPath).Entry(AssetPath.Background(Slot))` is an entry: if `File.Exists(entry.Picture)` set `SourcePath = entry.Picture`, `Mode = FromRecord(entry.Mode)`, `PanX`, `PanY`, `DarkenPercent = entry.Darken`, `HasValuesFrom = true`, `ValuesFromText = $"Values from {pack}, saved {entry.SavedAt.ToLocalTime():d MMM HH:mm}."`, `TargetPack = pack name` when in the choices. If the picture is missing: load Mode, pan and darken, leave `SourcePath` empty, set `Preview` to the decoded pack file (`Path.Combine(pack.FullPath, AssetPath.Background(Slot))`) so the stage shows the saved file, `ValuesFromText = $"{Path.GetFileName(entry.Picture)}, missing. Showing the saved file."`, `HasValuesFrom = true`. Save stays disabled through the existing `CanSave` (no source). Guard: a request with an explicit `SourcePath` (opened from a picture tile) keeps its own `SourcePath` and loads only Mode, pan and darken.

- [ ] **Step 3: `StartFreshCommand`**: `SourcePath = ""` (unless the request gave one), `Mode = FitMode.Cover`, `PanX = PanY = 0.5`, `DarkenPercent = 0`, `HasValuesFrom = false`.

- [ ] **Step 4: `SaveAsync`**: after the bytes are written to `PackFilePath()`, write the entry:

```csharp
var packRoot = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName);
var relative = Path.GetRelativePath(packRoot, packFile);   // "Backgrounds\\BG_Sewer.jpg" or "Backgrounds\\name all maps.jpg"
var record = BackgroundEditRecord.Load(packRoot);
record.Set(relative, new BackgroundSlotEntry { SavedAt = DateTimeOffset.Now, Picture = SourcePath, Mode = ToRecord(Mode), PanX = PanX, PanY = PanY, Darken = DarkenPercent, Hash = FileHasher.Hash(packFile) });
record.Save(packRoot);
```

- [ ] **Step 5: XAML.** Above the Fit radio group (:164) a row like Task 4's: `TextBlock` bound to `ValuesFromText`, `Button` "Start fresh" (`ResetLink` style, `AutomationProperties.Name="Start fresh"`, focus ring), visible through `HasValuesFrom`.

- [ ] **Step 6: Build, format, all tests. Commit** "Background editor: Save writes the pack record, open loads it".

---

## Risks and open questions

- `System.Text.Json` and `Dictionary<string, T>` comparers: the `Normalise()` step after read is what makes case-insensitive lookups hold; the round-trip test covers it.
- The platform editor's record art load runs after construction; the first preview may render before it finishes. The ctor's `SchedulePreview()` plus a second `SchedulePreview()` when the load completes keeps the stage right. Keep the load cancellable through `Cleanup()`.
- `MapSlotChoice.Slot` spelling (file name against relative path) decides the record key in Task 6; the implementer must read `MapSlotChoice` and `PackFileName()` before writing the key.
- `UndoSession.CapturedFiles()` may enumerate the session directory; Task 2 must make sure the `library` folder is not restored into the game folder. The `Game_only_restore_ignores_library_side` test guards it.
