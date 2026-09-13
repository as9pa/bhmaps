# BhMaps 2.6 Part C Implementation Plan: thumbnails a map owns outright, manual, README, version

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A map owns every map-select thumbnail candidate that no other map's folder also names, so the ranked maps whose folder holds two or three levels naming different jpgs (Fortress, GreatHall, Seven and the rest of the owner's list) get their picture written over each of their own files instead of being skipped; then the manual, the README and the version move to 2.6.

**Architecture:** `MapCatalog` already collects, per mapArt folder, every `ThumbnailPNGFile` its included levels name (`Candidates`). Today it folds that list to a single `ThumbnailFile` only when the folder names exactly one file and no other folder names it, which is why a folder holding both `Mammoth.jpg` and `MammothSmall.jpg` gets null. The fix moves the decision from "one file per folder" to "one folder per file": a folder owns each candidate whose count across all folders is one. `MapEntry` carries the owned list; `ThumbnailWriter.Plan` returns one entry per candidate, a target for each owned file that exists and a skip reason for the rest; `MainViewModel` renders the map once and writes the same composite over every target, keeping one original per file under its own name in `thumbnails-original`. Undo, Reset and the switch-off restore already work per file name, so nothing below `UndoSession.CaptureThumbnails` changes.

**Tech Stack:** .NET 10, WPF imaging (`MapCompositor.Render`, `TransformedBitmap`, `JpegBitmapEncoder`), CommunityToolkit.Mvvm, xunit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-09-13-bhmaps-v2-6-design.md` section 8 (binding), plus the release line of section 10 and every feature in the section 1 table for the docs task.

**Branch:** `feature/bhmaps-v2.6`. Parts A and B (spec sections 2 to 7: tile menus, Copy and Move, Import from pack, Duplicate, auto-update) are merged on the branch before this plan runs. This plan documents their features but does not build them.

## Conflicts with spec

1. **The shared note's wording.** Spec section 8 says shared candidates "stay skipped with the existing `shares its thumbnail with ...` note". The note that actually exists, in `MainViewModel.SkipNote` :1279-1280, reads `Map-select thumbnail not written: {map} shares its picture with {other}.`, and `docs\manual.md` :441-442 documents that behaviour in those words. **Resolution:** keep the code's wording. "The existing note" is the binding half of the sentence; the quoted phrase is a paraphrase. Task 3 keeps that exact sentence for a map whose every candidate is shared, and adds a per-file sentence for a map that owns some files and shares others, which 2.5 had no case for.

2. **`ThumbnailSkip.NoFile` has nowhere left to live.** Spec section 8 says "the `NoFile` skip remains only for folders with zero candidates". A per-file plan cannot carry a per-file reason for a file that does not exist. **Resolution:** `NoFile` leaves the enum and becomes `ThumbnailPlan.NamesNoFile` (the plan has no entries at all), which is the same condition by a name that fits the shape. The panel sentence for it is unchanged.

3. **`ThumbnailWriter` lives in `BhMaps.Core\Operations\`, not `BhMaps.Core\Thumbnails\`.** No spec text names the folder; the file is not moved.

4. **`LevelDataCache.SchemaVersion` does not move.** Checked: `LevelDataCache` :46 serialises `new CacheFile(SchemaVersion, stamp, model)`, where `model` is the parsed `LevelDataModel`. Catalog output is not cached, and `LevelType.ThumbnailFile` is already in the model at version 3, so a stale cache feeds the new `MapCatalog.Build` correctly. **No bump.**

## Global Constraints

From spec section 10 and the 2.5 Part C plan, both still binding:

- .NET 10, WPF, `TreatWarningsAsErrors` on. `dotnet format BhMaps.slnx` is the only formatter. Build and test with `--artifacts-path <ART>`.
- Tests only in `tests\BhMaps.Core.Tests` (xunit 2.9.3). Every Core type touched gets tests.
- No network in tests. No test touches the real game folder, the real library or the real `%APPDATA%\BhMaps`.
- Copy in the UI: sentence case, no em-dashes, no emoji. New `.cs` and `.xaml` files CRLF. Docs CRLF, UTF-8 without BOM.
- Every library write outside a plain copy goes through an undo session.
- WPF: `FocusVisualStyle="{StaticResource DialogFocusRing}"` as a local attribute on every focusable control added; no bare `x:Static` const int into a double; `[ObservableProperty]` setters run `OnXChanged` during construction.
- The game's own files other than `.png` and `.jpg` are never written. The thumbnail write is the one write outside `mapArt` the app makes, and only into `<GameRoot>\images\thumbnails\`, only over a `.jpg` that already exists there.
- One commit per task, message from a file with `git commit -F`, trailers as the dispatcher gives them.

---

## Task 1: A map owns every candidate no other folder names

Spec section 8, the `MapEntry` half.

**Files:**
- Modify: `src\BhMaps.Core\Maps\MapCatalog.cs` (`MapEntry` :9-25, `Build` :87-118 the `Thumbnails`/`Entry` calls at :110-115, `FolderEntry` :129-141, `Thumbnails` :143-159, `Entry` :176-204)
- Test: `tests\BhMaps.Core.Tests\MapCatalogTests.cs` (:201-248 the three existing thumbnail tests; helpers `Level` :10, `Model` :21, `Type` :25)

**Interfaces:**

```csharp
// Consumes: LevelType.ThumbnailFile (string?), unchanged since 2.5.
// Produces:
public sealed record MapEntry(
    string FolderName,
    string DisplayName,
    LevelDesc BaseLevel,
    IReadOnlyList<LevelDesc> Levels,
    IReadOnlyList<string> Sets,
    IReadOnlyList<string> BackgroundSlots,
    IReadOnlyList<string> PlatformFiles,
    IReadOnlyList<string>? OwnedThumbnails = null,
    IReadOnlyList<string>? ThumbnailCandidates = null)
{
    public IReadOnlyList<string> ThumbnailFiles => OwnedThumbnails ?? [];
    public IReadOnlyList<string> Candidates => ThumbnailCandidates ?? [];
    public string? ThumbnailFile => ThumbnailFiles.Count > 0 ? ThumbnailFiles[0] : null;
}
```

The positional parameter is `OwnedThumbnails` so that `ThumbnailFiles` can be the never-null accessor, the way `Candidates` already stands beside `ThumbnailCandidates`. Arity does not change: the 2.5 `string? ThumbnailFile = null` slot becomes the list. The three construction sites are `MapCatalog.Entry` :187, `MapCatalog.FolderEntry` :132 and `ThumbnailWriterTests` :138 (Task 2 updates that one).

- [ ] **Step 1: Tests.** In `tests\BhMaps.Core.Tests\MapCatalogTests.cs`, keep `Build_NamesTheThumbnailFileWhenOneMapOwnsIt` :202 as it is, replace `Build_DropsAThumbnailFileWhenAMapsLevelsDisagree` :235 (its behaviour is what this task changes) and add the rest:

```csharp
    [Fact]
    public void Build_OwnsBothFilesWhenAFoldersTwoLevelsNameDifferentOnes()
    {
        var data = Model(
            [Level("Fortress", "Fortress"), Level("SmallFortress", "Fortress")],
            [
                new LevelType("Fortress", "Mammoth Fortress", false, false, "Mammoth.jpg"),
                new LevelType("SmallFortress", "Small Mammoth Fortress", false, false, "MammothSmall.jpg"),
            ],
            []);

        var fortress = MapCatalog.Build(data).ByFolder("Fortress")!;

        Assert.Equal(["Mammoth.jpg", "MammothSmall.jpg"], fortress.ThumbnailFiles);
        Assert.Equal("Mammoth.jpg", fortress.ThumbnailFile);
    }

    [Fact]
    public void Build_OwnsAllThreeFilesOfAThreeLevelFolderInLevelOrder()
    {
        var data = Model(
            [Level("BigGreatHall", "GreatHall"), Level("GreatHall", "GreatHall"), Level("SmallGreatHall", "GreatHall")],
            [
                new LevelType("BigGreatHall", "Big Great Hall", false, false, "biggreathall.jpg"),
                new LevelType("GreatHall", "Great Hall", false, false, "greathall.jpg"),
                new LevelType("SmallGreatHall", "Small Great Hall", false, false, "smallgreathall.jpg"),
            ],
            []);

        var hall = MapCatalog.Build(data).ByFolder("GreatHall")!;

        Assert.Equal(["biggreathall.jpg", "greathall.jpg", "smallgreathall.jpg"], hall.ThumbnailFiles);
    }

    [Fact]
    public void Build_LeavesOutOnlyTheFileAnotherFolderAlsoNames()
    {
        var data = Model(
            [Level("X", "X"), Level("SmallX", "X"), Level("Y", "Y")],
            [
                new LevelType("X", "X", false, false, "Own.jpg"),
                new LevelType("SmallX", "Small X", false, false, "Shared.jpg"),
                new LevelType("Y", "Y", false, false, "Shared.jpg"),
            ],
            []);

        var catalog = MapCatalog.Build(data);

        Assert.Equal(["Own.jpg"], catalog.ByFolder("X")!.ThumbnailFiles);
        Assert.Equal(["Own.jpg", "Shared.jpg"], catalog.ByFolder("X")!.Candidates);
        Assert.Empty(catalog.ByFolder("Y")!.ThumbnailFiles);
        Assert.Null(catalog.ByFolder("Y")!.ThumbnailFile);
    }

    [Fact]
    public void Build_OwnsNothingWhenAMapsLevelsNameNoFile()
    {
        var data = Model([Level("Grove", "Grove")], [Type("Grove", "Twilight Grove")], []);

        var grove = MapCatalog.Build(data).ByFolder("Grove")!;

        Assert.Empty(grove.ThumbnailFiles);
        Assert.Empty(grove.Candidates);
    }
```

  `Build_DropsAThumbnailFileTwoMapsShare` :218 stays and gains `Assert.Empty(catalog.ByFolder("X")!.ThumbnailFiles);`.

- [ ] **Step 2: Run, expect failures. Implement.** In `src\BhMaps.Core\Maps\MapCatalog.cs`:

  Replace the `MapEntry` record head and its doc comment (:9-25) with:

```csharp
/// <summary>One map: a mapArt folder with the levels that point at it, ranked so one of them names the map.
/// <see cref="ThumbnailFiles"/> are the map-select pictures this map owns outright, in level order: a folder
/// owns every file its included levels name that no other folder's levels also name, so a folder whose levels
/// name two or three different pictures owns all of them. <see cref="Candidates"/> holds every file its levels
/// name, owned or not, and <see cref="ThumbnailFile"/> is the first owned one for callers that want a single
/// name.</summary>
public sealed record MapEntry(
    string FolderName,
    string DisplayName,
    LevelDesc BaseLevel,
    IReadOnlyList<LevelDesc> Levels,
    IReadOnlyList<string> Sets,
    IReadOnlyList<string> BackgroundSlots,
    IReadOnlyList<string> PlatformFiles,
    IReadOnlyList<string>? OwnedThumbnails = null,
    IReadOnlyList<string>? ThumbnailCandidates = null)
{
    /// <summary>The map-select pictures this map owns outright, in level order, empty when it owns none.</summary>
    public IReadOnlyList<string> ThumbnailFiles => OwnedThumbnails ?? [];

    /// <summary>Every map-select picture this map's included levels name, empty when they name none.</summary>
    public IReadOnlyList<string> Candidates => ThumbnailCandidates ?? [];

    /// <summary>The first picture this map owns, or null when it owns none.</summary>
    public string? ThumbnailFile => ThumbnailFiles.Count > 0 ? ThumbnailFiles[0] : null;
}
```

  Replace `Thumbnails` (:143-159) with `Owned`:

```csharp
    /// <summary>The map-select pictures each folder owns, keyed by folder name, in the order its levels name
    /// them. A folder owns a candidate when it is the only folder naming it: a file two folders name belongs to
    /// neither, because writing it would change the other map's thumbnail. A folder naming several files of its
    /// own owns all of them, which is the case the ranked "Small X" levels make.</summary>
    private static Dictionary<string, IReadOnlyList<string>> Owned(
        Dictionary<string, IReadOnlyList<string>> candidates)
    {
        // Candidates are already distinct within a folder, so a count above one always means a second folder.
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in candidates.Values.SelectMany(files => files))
        {
            owners[file] = owners.GetValueOrDefault(file) + 1;
        }

        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.Where(file => owners[file] == 1).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }
```

  In `Build` (:110-115), `var thumbnails = Thumbnails(candidates);` becomes `var owned = Owned(candidates);` and the `Select` passes `owned[g.Key]`:

```csharp
        var candidates = Candidates(folders, included);
        var owned = Owned(candidates);

        var maps = folders
            .Select(g => Entry(g.Key, g.ToList(), Display, data.Sets, owned[g.Key], candidates[g.Key]))
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
```

  In `Entry` (:176-204), the parameter `string? thumbnailFile` becomes `IReadOnlyList<string> owned` and the argument at :203 becomes `owned`. In `FolderEntry` (:140), `ThumbnailFile: null` becomes `OwnedThumbnails: null`.

- [ ] **Step 3: Run all Core tests, format, build. Commit** "Maps: a map owns every map-select picture no other folder names".

---

## Task 2: ThumbnailWriter plans every file a map owns

Spec section 8, the writer half.

**Files:**
- Modify: `src\BhMaps.Core\Operations\ThumbnailWriter.cs` (`ThumbnailSkip` :9-16, `ThumbnailPlan` :22, `Plan` :45-69; `Render` :72-82, `Write` :84-94, `KeepOriginal` :96-108, `RestoreOriginal` :110-122, `RestoreAll` :124-139 and `KeptOriginals` :141-151 are already per file and are not touched)
- Test: `tests\BhMaps.Core.Tests\ThumbnailWriterTests.cs` (helper `Map` :11-13, `Plan_ReturnsTheTargetWhenTheFileExists` :23-39, `Plan_SkipsMissingSharedAndNoFile` :41-64, `Render_ComposesTheMapFromTheGameFolder` :131-)

**Interfaces:**

```csharp
// Consumes: MapEntry.ThumbnailFiles, MapEntry.Candidates, MapEntry.DisplayName (Task 1).
// Produces:
public enum ThumbnailSkip
{
    None,
    Shared,
    Missing,
}

public sealed record ThumbnailTarget(string FileName, string TargetPath, string OriginalPath);   // unchanged

/// <summary>One of a map's candidates: a target when the map owns it and the jpg is there, otherwise the reason.</summary>
public sealed record ThumbnailFilePlan(string FileName, ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap);

/// <summary>One entry per candidate the map's levels name, in level order; empty when its levels name none.</summary>
public sealed record ThumbnailPlan(IReadOnlyList<ThumbnailFilePlan> Files)
{
    public IReadOnlyList<ThumbnailTarget> Targets { get; } =
        [.. Files.Select(f => f.Target).OfType<ThumbnailTarget>()];

    public bool NamesNoFile => Files.Count == 0;
}

public static ThumbnailPlan Plan(MapEntry map, IReadOnlyList<MapEntry> allMaps, string gameRoot, string appDataDir);
```

`ThumbnailSkip.NoFile` is removed (see Conflicts 2); `ThumbnailPlan.NamesNoFile` replaces it. `ThumbnailPlan.Target`, `ThumbnailPlan.Skip` and `ThumbnailPlan.OtherMap` are gone; the one consumer is `MainViewModel`, which Task 3 rewrites.

- [ ] **Step 1: Tests.** In `tests\BhMaps.Core.Tests\ThumbnailWriterTests.cs`, change the helper and replace the two `Plan_` tests:

```csharp
    private static MapEntry Map(string folder, IReadOnlyList<string> owned, params string[] candidates) =>
        new(folder, folder, new LevelDesc(folder, folder, new CameraBounds(0, 0, 100, 100), [], []),
            [], [], [], [], owned, candidates);

    [Fact]
    public void Plan_ReturnsATargetForEveryFileTheMapOwns()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        var big = Thumbnail(tmp, "game", "Mammoth.jpg", "big picture");
        var small = Thumbnail(tmp, "game", "MammothSmall.jpg", "small picture");
        var map = Map("Fortress", ["Mammoth.jpg", "MammothSmall.jpg"], "Mammoth.jpg", "MammothSmall.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.False(plan.NamesNoFile);
        Assert.Equal(["Mammoth.jpg", "MammothSmall.jpg"], plan.Files.Select(f => f.FileName));
        Assert.All(plan.Files, f => Assert.Equal(ThumbnailSkip.None, f.Skip));
        Assert.Equal([big, small], plan.Targets.Select(t => t.TargetPath));
        Assert.Equal(
            [
                Path.Combine(appData, "thumbnails-original", "Mammoth.jpg"),
                Path.Combine(appData, "thumbnails-original", "MammothSmall.jpg"),
            ],
            plan.Targets.Select(t => t.OriginalPath));
    }

    [Fact]
    public void Plan_KeepsTheFilesThatExistAndNotesAMissingOne()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "wasteland.jpg", "game picture");
        var map = Map(
            "Seven",
            ["wasteland.jpg", "wastelandshowdown.jpg"],
            "wasteland.jpg",
            "wastelandshowdown.jpg");

        var plan = ThumbnailWriter.Plan(map, [map], gameRoot, appData);

        Assert.Single(plan.Targets);
        Assert.Equal("wasteland.jpg", plan.Targets[0].FileName);
        Assert.Equal(ThumbnailSkip.Missing, plan.Files[1].Skip);
        Assert.Null(plan.Files[1].Target);
        Assert.Null(plan.Files[1].OtherMap);
    }

    [Fact]
    public void Plan_SkipsOnlyTheCandidateAnotherMapAlsoNames()
    {
        using var tmp = new TempDir();
        var gameRoot = Path.Combine(tmp.Path, "game");
        var appData = Path.Combine(tmp.Path, "appdata");
        Thumbnail(tmp, "game", "Own.jpg", "own picture");
        Thumbnail(tmp, "game", "Both.jpg", "shared picture");
        var sewer = Map("Sewer", ["Own.jpg"], "Own.jpg", "Both.jpg");
        var swamp = Map("Swamp", [], "Both.jpg");

        var plan = ThumbnailWriter.Plan(sewer, [sewer, swamp], gameRoot, appData);

        Assert.Single(plan.Targets);
        Assert.Equal("Own.jpg", plan.Targets[0].FileName);
        Assert.Equal(ThumbnailSkip.Shared, plan.Files[1].Skip);
        Assert.Equal("Swamp", plan.Files[1].OtherMap);
    }

    [Fact]
    public void Plan_NamesNoFileWhenTheMapsLevelsNameNone()
    {
        using var tmp = new TempDir();
        var map = Map("Bombsketball", []);

        var plan = ThumbnailWriter.Plan(map, [map], Path.Combine(tmp.Path, "game"), Path.Combine(tmp.Path, "appdata"));

        Assert.True(plan.NamesNoFile);
        Assert.Empty(plan.Files);
        Assert.Empty(plan.Targets);
    }
```

  `Render_ComposesTheMapFromTheGameFolder` :138 builds a `MapEntry` with seven arguments and is unaffected. Add `using System.Linq;` only if the file's implicit usings do not already cover it (the project has `ImplicitUsings` on, so they do).

- [ ] **Step 2: Run, expect failures. Implement.** In `src\BhMaps.Core\Operations\ThumbnailWriter.cs`, replace the enum, the `ThumbnailPlan` record and `Plan`:

```csharp
/// <summary>Why one of a map's map-select pictures is left alone. None means it can be written.</summary>
public enum ThumbnailSkip
{
    None,
    Shared,
    Missing,
}

/// <summary>Where a map's thumbnail lives and where its original is kept.</summary>
public sealed record ThumbnailTarget(string FileName, string TargetPath, string OriginalPath);

/// <summary>One picture a map's levels name: a target when the map owns it and the game's jpg is there,
/// otherwise the reason it is left alone, and for Shared the display name of the map that also names it.</summary>
public sealed record ThumbnailFilePlan(string FileName, ThumbnailTarget? Target, ThumbnailSkip Skip, string? OtherMap);

/// <summary>Spec section 8: one entry per picture the map's included levels name, in level order. A map whose
/// levels name none plans no entries at all, which is <see cref="NamesNoFile"/>.</summary>
public sealed record ThumbnailPlan(IReadOnlyList<ThumbnailFilePlan> Files)
{
    /// <summary>Every file of the plan that can be written, in level order.</summary>
    public IReadOnlyList<ThumbnailTarget> Targets { get; } =
        [.. Files.Select(f => f.Target).OfType<ThumbnailTarget>()];

    /// <summary>True when the map's levels name no picture at all, so there was never anything to write.</summary>
    public bool NamesNoFile => Files.Count == 0;
}
```

  and

```csharp
    /// <summary>One plan entry per picture the map's levels name: a target when the map owns the file and the
    /// jpg is in the game's thumbnails folder, Shared when another map's folder also names it (OtherMap is that
    /// map's display name), Missing when the jpg is not there. A map that owns two files and is missing one
    /// plans the one that exists and reports the other.</summary>
    public static ThumbnailPlan Plan(MapEntry map, IReadOnlyList<MapEntry> allMaps, string gameRoot, string appDataDir)
    {
        var thumbnailsDir = ThumbnailsDir(gameRoot);
        var originalsDir = OriginalsDir(appDataDir);
        var owned = map.ThumbnailFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = new List<ThumbnailFilePlan>();

        foreach (var fileName in map.Candidates)
        {
            if (!owned.Contains(fileName))
            {
                // A file two folders name belongs to neither: writing it would change the other map's thumbnail.
                var other = allMaps.FirstOrDefault(o =>
                    o != map && o.Candidates.Contains(fileName, StringComparer.OrdinalIgnoreCase));
                files.Add(new ThumbnailFilePlan(fileName, null, ThumbnailSkip.Shared, other?.DisplayName));
                continue;
            }

            var targetPath = Path.Combine(thumbnailsDir, fileName);
            files.Add(File.Exists(targetPath)
                ? new ThumbnailFilePlan(
                    fileName,
                    new ThumbnailTarget(fileName, targetPath, Path.Combine(originalsDir, fileName)),
                    ThumbnailSkip.None,
                    null)
                : new ThumbnailFilePlan(fileName, null, ThumbnailSkip.Missing, null));
        }

        return new ThumbnailPlan(files);
    }
```

  Update the class doc comment above `ThumbnailWriter` to say "the map-select thumbnails of one map" and cite spec section 8 beside 10.3.

- [ ] **Step 3: Run all Core tests (`MainViewModel` will not compile yet; run the Core test project alone here and build the solution at the end of Task 3), format. Commit** "Core: a thumbnail plan carries every picture a map owns".

---

## Task 3: The write loop, the notes and the done line

Spec section 8, the app half. No public app signature changes: `RunGameWriteAsync` keeps `artMaps` and `resetThumbnails`, `MapPanelViewModel.ThumbnailNote` and `MapsView.xaml` :833-840 are untouched.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (the capture at :1161-1167, `WriteThumbnails` :1231-1275, `SkipNote` :1277-1284; `ThumbnailPlans` :1221-1226 and `WithThumbnailsDone` :1288- are unchanged)
- Not modified, checked: `src\BhMaps.App\ViewModels\MapPanelViewModel.cs` :103, :148, :151; `src\BhMaps.App\Views\Pages\MapsView.xaml` :833-840; `src\BhMaps.Core\Operations\UndoStore.cs` (`CaptureThumbnails` :77 already takes file names and the restore at :278-289 is per name); `scripts\make-dev-tree.ps1` :66-75 already copies the whole `images\thumbnails` folder, so a two-file map has both files in the dev tree.

**Interfaces:**

```csharp
// Consumes: ThumbnailPlan.Files, ThumbnailPlan.Targets, ThumbnailPlan.NamesNoFile, ThumbnailFilePlan (Task 2).
// Produces: no change to the public surface.
public Task<bool> RunGameWriteAsync(
    string label, IReadOnlyList<string> undoPaths, Func<IProgress<string>, CancellationToken, Task> work, string doneText,
    bool clearTicks = false, string? packName = null, IReadOnlyList<string>? libraryUndoPaths = null,
    IReadOnlyList<MapEntry>? artMaps = null, bool resetThumbnails = false);
public IReadOnlyDictionary<string, string> ThumbnailNotes { get; }
```

- [ ] **Step 1: The capture.** In `RunWriteCoreAsync` (:1161-1163) replace

```csharp
                                    session.CaptureThumbnails(
                                        thumbnailsDir,
                                        thumbnailPlans.Select(planned => planned.Plan.Target?.FileName).OfType<string>());
```

  with

```csharp
                                    session.CaptureThumbnails(
                                        thumbnailsDir,
                                        thumbnailPlans.SelectMany(planned => planned.Plan.Targets.Select(t => t.FileName)));
```

- [ ] **Step 2: The write loop.** Replace `WriteThumbnails` (:1231-1275) with:

```csharp
    /// <summary>Spec section 8 and 10.4: the thumbnail step, run after the caller's work succeeded. A map is
    /// rendered once and the same picture is written over every file it owns, so a folder whose levels name two
    /// or three pictures gets all of them. Each map stands on its own, so a keep or a write that fails for one
    /// becomes that map's panel note rather than a failure of a write that is already done. Returns how many
    /// files it wrote. Off the UI thread: the render is the composite the map page builds, and the rest is file
    /// copying.</summary>
    private int WriteThumbnails(
        IReadOnlyList<(MapEntry Map, ThumbnailPlan Plan)> plans,
        bool reset,
        string gamePath,
        IProgress<string> progress)
    {
        var written = 0;
        foreach (var (map, plan) in plans)
        {
            var note = SkipNote(map, plan);
            if (plan.Targets.Count == 0)
            {
                _thumbnailNotes[map.FolderName] = note;
                continue;
            }

            progress.Report("Map-select thumbnail " + map.DisplayName);
            try
            {
                // One render for the map, however many of its own files it is written over.
                var composite = reset ? null : ThumbnailWriter.Render(map, gamePath);
                foreach (var target in plan.Targets)
                {
                    // The game's own picture is kept before the first write over it, so every write can be
                    // undone even after the undo snapshot it was taken with has been replaced. One kept copy
                    // per file, under that file's own name.
                    ThumbnailWriter.KeepOriginal(target);
                    if (composite is null)
                    {
                        if (ThumbnailWriter.RestoreOriginal(target))
                        {
                            written++;
                        }
                    }
                    else
                    {
                        ThumbnailWriter.Write(composite, target.TargetPath);
                        written++;
                    }
                }

                if (note.Length == 0)
                {
                    _thumbnailNotes.Remove(map.FolderName);
                }
                else
                {
                    _thumbnailNotes[map.FolderName] = note;
                }
            }
            catch (Exception ex)
            {
                _thumbnailNotes[map.FolderName] = $"Map-select thumbnail not written: {ex.Message}";
            }
        }

        return written;
    }
```

- [ ] **Step 3: The notes.** Replace `SkipNote` (:1277-1284) with:

```csharp
    /// <summary>The panel note for a map the write could not aim every picture of, or "" when it wrote them all.
    /// A map that owns nothing keeps 2.5's sentence, which the manual quotes; a map that owns some files and not
    /// others names the ones that were left alone.</summary>
    private static string SkipNote(MapEntry map, ThumbnailPlan plan)
    {
        if (plan.NamesNoFile)
        {
            return $"Map-select thumbnail not written: no file is named for {map.DisplayName}.";
        }

        var shared = plan.Files.Where(f => f.Skip == ThumbnailSkip.Shared).ToList();
        var missing = plan.Files.Where(f => f.Skip == ThumbnailSkip.Missing).ToList();
        if (shared.Count == 0 && missing.Count == 0)
        {
            return "";
        }

        if (plan.Targets.Count == 0 && missing.Count == 0)
        {
            return $"Map-select thumbnail not written: {map.DisplayName} shares its picture with {shared[0].OtherMap}.";
        }

        var parts = new List<string>();
        if (shared.Count > 0)
        {
            parts.Add($"{Names(shared)} {(shared.Count == 1 ? "is" : "are")} shared with {shared[0].OtherMap}.");
        }

        if (missing.Count > 0)
        {
            parts.Add($"{Names(missing)} {(missing.Count == 1 ? "is" : "are")} missing from the game folder.");
        }

        var lead = plan.Targets.Count == 0 ? "Map-select thumbnail not written: " : "Map-select thumbnail: ";
        return lead + string.Join(" ", parts);
    }

    /// <summary>The file names of a group of skipped plan entries, in level order.</summary>
    private static string Names(IReadOnlyList<ThumbnailFilePlan> files) =>
        string.Join(", ", files.Select(f => f.FileName));
```

- [ ] **Step 4: Dev tree check.** Build with `--artifacts-path <ART>`, launch the built exe against the dev tree with `--quiet` (the launch rules are in the 2.5 Part B Task 1 notes; `scripts\make-dev-tree.ps1 -RealArt` already copies the whole thumbnails folder). Turn the Map-select thumbnails switch on, apply the demo pack to a map whose folder has two levels naming different jpgs, and confirm: both jpgs in `<dev>\images\thumbnails` changed, `<dev>\appdata\thumbnails-original\` holds both originals under their own names, the done line reads "Map-select thumbnails updated.", and the map panel shows no note. Undo, confirm both files are back byte for byte. Switch off, confirm the originals folder is empty and nothing outside `images\thumbnails` was written. Record it in the ledger. Close only the app you launched.
- [ ] **Step 5: Format, build, all tests. Commit** "Game writes update every map-select thumbnail a map owns".

---

## Task 4: Version 2.6.0, README, manual

Spec section 1 (every feature listed) and section 10's release line. Sections 2 to 7 are already on the branch; this task documents them from the spec text.

**Files:**
- Modify: `src\BhMaps.App\BhMaps.App.csproj` (:12 `<Version>2.5.0</Version>`)
- Modify: `README.md` (:13-14 asset file names; the "What it does" list :26-40)
- Modify: `docs\manual.md` (intro :1-11; new "## What is new in 2.6" above :13; "Where things live" table :147-160 and the paragraph :162-169; "Pages" Packs bullet :253-260, Pack detail bullet :261-271, Settings bullet :272-277, and the top bar paragraph at :173-175; "How it works" :379-446; "Known limitations" :527-549)

- [ ] **Step 1: Version.** `src\BhMaps.App\BhMaps.App.csproj` :12 becomes `<Version>2.6.0</Version>`.
- [ ] **Step 2: README.** :13-14 become

```
| `bhmaps-v2.6.0-win-x64.exe` | ~135 MB | No, the runtime is inside |
| `bhmaps-v2.6.0-win-x64-dotnet.zip` | ~0.7 MB | Yes, [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
```

  and add, after the "Take the `.exe`" paragraph at :16-17:

```
From 2.6 the app checks this page for a newer release once a day and offers to update itself. It is
one small request to github.com, nothing about you or your library is sent, and the check can be
turned off in Settings under Updates.
```

  Add three bullets to the end of "What it does" (after the Add Image bullet at :36-40):

```
- Every tile in a pack has a menu. **Copy to pack...** and **Move to pack...**, or Ctrl+C, Ctrl+X and
  Ctrl+V, take a map or a picture from one pack to another, and **Remove from** takes it out.
- **Import from pack** copies all of another pack's maps, or the ones you pick, into the pack you are
  looking at, and **Duplicate** in the Packs menu copies a whole pack under a new name.
- BhMaps checks for a new release once a day and can download it and swap itself over when you close
  it. The check is one line in Settings and can be turned off.
```

- [ ] **Step 3: Manual, the new section.** Insert above :13, keeping a blank line either side:

```
## What is new in 2.6

- **Every tile in a pack has a menu**, map tiles included. The dots button on a tile, or a right-click,
  opens it, headed with the map's name: **Apply to <map>**, **Copy to pack...**, **Move to pack...**,
  **Show in folder** and **Remove from <pack>**. A tile that also has a picture of its own keeps the
  picture lines it had in 2.5.
- **Copy to pack...** and **Move to pack...** move a map or a picture between packs. Both ask which
  pack with a small list that ends in **New pack...**, and both carry the map's folder, its pictures in
  the pack's Backgrounds folder and what the editors remembered for it. If the pack you picked already
  has that map, a confirm asks **"<pack> already has <name>. Replace it?"** A copy is not undoable and
  its done line offers **Open <pack>** instead; a move always is.
- **Ctrl+C**, **Ctrl+X** and **Ctrl+V** do the same on a pack page. Ctrl+C or Ctrl+X takes the tile
  under the pointer, or the one the keyboard is on, and Ctrl+V pastes it into whichever pack you are
  looking at. Pasting into the pack it came from does nothing; Ctrl+V with nothing held says
  **Nothing copied yet**.
- **Import from pack**, a button in the pack header and **Import from another pack...** in the Packs
  menu, copies another pack's maps in one go. Pick the pack to take from, then **All <n> maps** or
  **Choose maps** with a tick list, where a map the pack already has reads **already here** and starts
  unticked. **Replace maps <pack> already has** is off until you turn it on. The button counts what it
  will do, as in **Import 7 maps**, and the import can be cancelled; what was copied stays.
- **Duplicate** in the Packs menu copies a whole pack under the next free name, as in "Neon copy",
  with no name to type. Undo removes the copy.
- **BhMaps checks for a new release** once a day, when it starts, and says so with a **New update
  available** line in the top bar that goes to Settings, with an x beside it that dismisses that
  release. The Settings Version row offers **Update to 2.6.0** and **What changed**; the update
  downloads the new exe, checks it against the release's checksums and swaps it in when you close
  BhMaps. Nothing downloads or restarts on its own. The new **Updates** row turns the check off with
  **Check for updates when BhMaps starts** and has a **Check now** button. It is one request a day to
  github.com, and nothing about you or your library is sent.
- **Map-select thumbnails are written for far more maps.** A map whose mapArt folder holds several
  levels naming different pictures, which is most of the ranked "Small" maps, used to be skipped
  altogether. It now gets its picture written over every one of those files, so Small Mammoth
  Fortress, Small Great Hall, Small Wasteland and the rest change in the map select screen along with
  the maps that always worked. A file that two different maps' folders name is still left alone.
```

- [ ] **Step 4: Manual, the rest.**
  - "Where things live" table, after the `Undo of the last game write` row (:158): `| Downloaded updates | `%APPDATA%\BhMaps\updates\` |`.
  - The paragraph at :162-169, in the sentence listing what `settings.json` holds, after "whether the game's map-select thumbnails are updated," insert "whether BhMaps checks for updates when it starts, when it last checked and which release you dismissed,".
  - Top bar paragraph at :173-175: after the game line sentence add "When a newer release is out, a **New update available** line sits before the game line; clicking it opens Settings and the small x beside it hides it until the release after that."
  - Packs bullet :258-259: "a dots button beside it holds Export, Open folder and Remove" becomes "a dots button beside it holds **Duplicate**, **Import from another pack...**, Export, Open folder and Remove; `Default` has no Remove".
  - Pack detail bullet :261-271: after "Hovering a tile shows a dots button, and the dots or a right-click opens the" replace the rest of the menu sentence with "menu. A map's tile is headed with the map's name and holds **Apply to <map>**, **Copy to pack...** (Ctrl+C), **Move to pack...** (Ctrl+X), **Show in folder** and **Remove from <pack>**; a picture's tile is headed with its file name and keeps 2.5's lines, with Copy and Move added after **Edit**. Ctrl+V pastes whatever was copied or cut into the pack you are looking at. **Import from pack** in the header opens the import dialog." Keep the transparent-PNG sentence and the closing sentence as they are.
  - Settings bullet :272-277: before "and the version" insert "**Updates**, a checkbox reading **Check for updates when BhMaps starts** with a **Check now** button and a line saying when it last checked;", and after "and the version" add "which says whether a newer release is out and offers **Update to <version>** and **What changed**".
  - "How it works", replace the Map-select thumbnails paragraph :436-442 with:

```
- **Map-select thumbnails** are the one thing the app writes outside `mapArt`. With the switch in
  Settings on, a write that changes a map's art renders that map at 290 by 164 and writes the JPEG
  over every picture in the game's `images\thumbnails` folder that the map's own levels name and no
  other map's folder names. A map's folder often holds more than one level, the ranked "Small" one
  beside the casual one, and those levels usually name different pictures; all of them are that map's
  and all of them are written. The first time a file is written over, the original is copied into
  `%APPDATA%\BhMaps\thumbnails-original` under that file's own name, and those copies are what Reset
  to default, Undo and turning the switch off put back. A picture two different maps' folders both
  name is skipped, so one map's art is never written over another map's thumbnail, and the map panel
  says which file was left alone and why.
```

  - "How it works", add after that paragraph:

```
- **Updates** are read from the GitHub releases page of the project, once a day at most, when BhMaps
  starts and only when the check is on. The request carries nothing but the app's version; no account,
  no token and nothing about your library. When the newest release is newer than the running version,
  Settings offers to download its `.exe` into `%APPDATA%\BhMaps\updates`, checks it against the
  release's `SHA256SUMS.txt` and refuses it on a mismatch. Installing it is a small script that waits
  for BhMaps to close, swaps the new exe over the old one and starts it again, so nothing is replaced
  while the app is running. If the install folder cannot be written to, the button opens the release
  page instead.
```

  - "Known limitations", add at the end:

```
- A map-select picture that two different maps' mapArt folders both name is left alone, because
  writing it would change the other map's thumbnail too. The map panel says so.
- The update swap needs the folder BhMaps runs from to be writable and the build to be the
  self-contained `.exe`. The `-dotnet.zip` build, and an install under `Program Files`, get the
  release page instead of a download.
- Copying a map into another pack is not undoable; moving, importing, removing and duplicating are.
```

- [ ] **Step 5: Check the files are still CRLF, UTF-8 without BOM** (`python -c "d=open(r'docs\manual.md','rb').read(); print(d[:3], d.count(b'\r\n'), d.count(b'\n'))"`; the two counts must match and the first bytes must not be `\xef\xbb\xbf`). Convert with python if an editor changed them.
- [ ] **Step 6: Build with `--artifacts-path <ART>` and run all tests.** The Settings Version row reads the assembly's informational version, so 2.6.0 follows from the csproj.
- [ ] **Step 7: Commit** "BhMaps 2.6: manual, readme, version".

### Release notes text (scratch file, not committed)

Write this into the dispatcher's scratch directory as `release-notes-2.6.0.md` for the GitHub release body. Do **not** add it to the repo.

```
BhMaps 2.6

Packs
- Every tile in a pack detail page has a menu now, map tiles included: Apply, Copy to pack..., Move to pack..., Show in folder and Remove.
- Copy to pack... and Move to pack... take a map or a picture to another pack, with its pictures and what the editors remembered for it. Ctrl+C, Ctrl+X and Ctrl+V do the same from the keyboard.
- Import from pack copies another pack's maps into the one you are looking at: all of them or the ones you tick, with a Replace switch for the maps you already have.
- Duplicate copies a whole pack under the next free name.

Updates
- BhMaps checks for a new release once a day and says so in the top bar and in Settings. It can download the new exe, check it against the release checksums and swap itself over when you close it. Nothing downloads or restarts on its own, and the check can be turned off in Settings under Updates.

Map-select thumbnails
- Maps whose folder holds several levels naming different pictures, which is most of the ranked "Small" maps, were skipped. They are written now, over every picture that map owns: Small Mammoth Fortress, Small Great Hall, Small Wasteland and the rest change in the map select screen along with the maps that always worked. A picture two different maps share is still left alone.

Full manual: docs/manual.md
```

---

## Risks

- **A folder that owns several files now writes several files.** The done line counts files, not maps, so a single-map apply can read "Map-select thumbnails updated." That is correct and intended; the count is of writes.
- **One render, several writes.** `ThumbnailWriter.Write` scales the frozen composite per target, so the same picture is encoded once per file. Encoding a 290 by 164 JPEG three times is cheaper than rendering three times, and the composite is frozen before it leaves `Render`, so passing it across the loop is safe.
- **Kept originals grow.** A map with three files keeps three originals, one per file name, which is what `RestoreAll` and `KeptOriginals` already walk. No collision is possible: the keys are the game's own file names.
- **The 2.5 undo folder.** A session captured before this change holds one file name per map; restoring it still works, and the files this release writes that it never captured are put back by the switch-off restore instead.
- **A game update replaces the thumbnails**; a kept original then predates the update. Restore still copies it back. Carried over from 2.5 and accepted.

## Plan self-review

### Spec coverage

| Spec | Where |
| --- | --- |
| 8: a map owns every candidate no other folder names | Task 1, `MapCatalog.Owned` |
| 8: `MapEntry.ThumbnailFiles` in level order, `ThumbnailFile` the first or null | Task 1, `MapEntry` |
| 8: `Plan` and the write handle many files per map, one original per file | Task 2 `Plan`, Task 3 `WriteThumbnails` |
| 8: shared candidates skipped with the existing note | Task 3 `SkipNote`, Conflicts 1 |
| 8: `NoFile` only for folders with zero candidates | Task 2 `NamesNoFile`, Conflicts 2 |
| 8: two owned files, one missing, writes the one that exists and notes the other | Task 2 `Plan_KeepsTheFilesThatExistAndNotesAMissingOne`, Task 3 `SkipNote` |
| 10: version 2.6.0 in `BhMaps.App.csproj` | Task 4 Step 1 |
| 10: README and manual updated | Task 4 Steps 2 to 4 |
| 1.1 tile menus | Task 4 Step 3 bullet 1, Step 4 pack detail bullet |
| 1.2 Copy, Move, Ctrl+C/X/V | Task 4 Step 3 bullets 2 and 3 |
| 1.3, 2.1, 3.2 Import from pack | Task 4 Step 3 bullet 4, Step 4 Packs and pack detail bullets |
| 3.1 Duplicate | Task 4 Step 3 bullet 5, Step 4 Packs bullet |
| 4.1 auto-update and how to turn it off | Task 4 Step 2 README, Step 3 bullet 6, Step 4 Settings bullet, top bar, How it works, Known limitations |
| 5.1 thumbnails behaviour change | Task 4 Step 3 bullet 7, Step 4 How it works |

### Type consistency

- `MapEntry` arity is unchanged at nine, the eighth parameter changing type from `string?` to `IReadOnlyList<string>?`. The three construction sites (`MapCatalog.Entry` :187, `MapCatalog.FolderEntry` :132, `ThumbnailWriterTests` :138 through its `Map` helper) are all named in Tasks 1 and 2; nothing else in `src` or `tests` builds a `MapEntry`.
- `MapEntry.ThumbnailFile` keeps its type `string?` and every existing reader (`MapCatalogTests` :214, :230, :231, :247) keeps compiling; only `ThumbnailWriter.Plan` :51 stops reading it, and Task 2 rewrites that method.
- `ThumbnailPlan` changes shape, so `MainViewModel` :1161-1163, :1240 and :1277-1284 stop compiling at the end of Task 2 and compile again at the end of Task 3. Run the solution build only after Task 3; run `BhMaps.Core.Tests` alone in Task 2 Step 3.
- `UndoSession.CaptureThumbnails(string, IEnumerable<string>)` is unchanged: Task 3 passes more names through the same call.
- `ThumbnailWriter.Render`, `Write`, `KeepOriginal`, `RestoreOriginal`, `RestoreAll` and `KeptOriginals` keep their 2.5 signatures. `RestoreAll` walks the originals folder by file name, so a map with three kept originals restores all three with no change.
- `ThumbnailSkip.NoFile` is removed; the only reader was `MainViewModel.SkipNote`, rewritten in Task 3. `ThumbnailPlan.Targets` is an initialised property, not an expression body, so it is computed once per plan rather than per read.
