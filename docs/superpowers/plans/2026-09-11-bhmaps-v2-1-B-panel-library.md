# BhMaps 2.1, part B: the map panel, the picture library, and the themed menus

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` to implement this
> plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Do not re-decide anything below.

**Goal:** Make a picture the unit the app works in. One Core library groups every custom picture by content hash;
the map panel becomes a two-segment (Background | Platforms) worksheet where every tile carries Apply and a menu;
the Backgrounds page becomes a library of pictures grouped by pack with a custom-pictures shelf on top; and Add
pictures becomes Add Custom Image with four "Then" outcomes. Every menu is a themed WPF ContextMenu reachable by
mouse, by a menu button and by the keyboard.

**Architecture:** `BhMaps.Core.Operations` gains one pure static, `CustomPictureLibrary`, and `ScanSnapshot` carries
its result so no page hashes anything of its own. `BhMaps.App` gains one shared tile object model
(`PictureTileViewModel` base, `MapPictureTileViewModel` and `CustomPictureTileViewModel` concrete) used by the map
panel, the Backgrounds page and its search results, so a tile's menu, tick and thumbnail behave the same wherever
it is drawn. Two multi-map write helpers (`MainViewModel.ApplyPictureAsync`, `MainViewModel.ApplySetAsync`) are the
only places a menu's Apply lands, and both go through `RunGameWriteAsync`. The theme gains ContextMenu and MenuItem
styles based on the Fluent implicit ones plus one shared data-driven `TileMenu` resource.

**Tech Stack:** .NET SDK 10, C# 14, WPF on `net10.0-windows`, `ThemeMode="Dark"` (Fluent), CommunityToolkit.Mvvm
8.4.2 with partial properties and `[RelayCommand]`, xunit 2.9.3. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-11-bhmaps-v2-1-design.md`. Section numbers below cite it ("spec 3.2").
Part B covers spec 3.2, 4, 7.1, the themed menus of section 13, and the section 11 copy for everything it touches.

**Branch:** `feature/bhmaps-v2.1`, on top of part A. Repo root `C:\Users\alexa\projects\bhmaps`.

---

## Global Constraints

Every task's requirements implicitly include this section. Copy the values verbatim.

**Safety. Hard rules, not preferences.**

- **Never run the app, `dotnet run`, or any test against the real game folder**
  `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt`, and never write anything under
  `C:\Users\alexa\files\bh`.
- Every manual run uses all three overrides together against the dev tree, where `<devtree>` is
  `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`:
  `--game <devtree>\game\mapArt --library <devtree>\lib --appdata <devtree>\appdata`.
- **Every write into the game folder goes through `MainViewModel.RunGameWriteAsync`.** No view model calls
  `BackgroundApplier`, `PlatformSetApplier`, `MapReset` or `File.*` against the game folder directly. Library
  writes (pack imports, removals) go through `RunBusyAsync` plus `SetLibraryDone`, never `RunGameWriteAsync`.

**Build and tooling.**

- Target framework **.NET 10** (`net10.0-windows`). Solution file is **`BhMaps.slnx`**, not `.sln`.
- All three projects set **`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`**: a warning fails the build.
- `<UseWPF>true</UseWPF>` removes `System.IO` from implicit usings; each `.csproj` carries
  `<Using Include="System.IO" />`. **Never add a per-file `using System.IO;`.**
- **`dotnet format BhMaps.slnx` is the only formatter.** Run it before every commit and
  `dotnet format BhMaps.slnx --verify-no-changes` to check. csharpier is not installed and the repo-root
  `.csharpierignore` disables the global hook. Do not install it, do not run it, do not delete `.csharpierignore`.
- `.editorconfig` enforces CRLF, 4-space C#, 2-space XAML.
- Test filter: `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~<ClassName>"`. Whole suite: `dotnet test`.

**Dependencies.** No new NuGet packages. `BhMaps.Core` has zero and keeps zero. `BhMaps.App` has exactly one:
**CommunityToolkit.Mvvm 8.4.2**.

**Copy.** **No em-dashes and no emoji** anywhere in the app, in code comments or in docs. Every user-visible string
below is quoted exactly as spec section 11 gives it. Do not improve the wording.

**MVVM style.** CommunityToolkit source generators with partial properties
(`[ObservableProperty] public partial bool IsBusy { get; set; }`) and `[RelayCommand]`. Every view model class is
`partial` and derives from `ObservableObject`. `partial void OnFooChanged(T value)` is the generated change hook.

**Design tokens, exact hex.** Bg `#161514`, Surface `#1C1B1A`, Surface2 `#242220`, Line `#2A2827`,
Line2 `#3A3734`, Text `#F1EFEA`, Text2 `#9C9891`, Text3 `#6B675F`, Tile `#2F2D2B`, MissingBg `#3A1A1C` with
MissingFg `#E57A7A`. Corner radius 6. Body type Geist 13. Panel width 360.

**Commit messages.** Conventional-commit subject, then these two trailer lines, each on its own line:

```
Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

Each task ends with its exact `git commit` command.

---

## Contracts part B consumes from part A

Part A lands first. B1 and B2 need none of it; **B3 onwards do**. Step 1 of B3 verifies them:

```powershell
Select-String -Path src\BhMaps.App\ViewModels\MainViewModel.cs -Pattern "IReadOnlyList<MapEntry> SelectedMaps","clearTicks","NotifySelectionChanged","LastOpenedMap"
Select-String -Path src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs -Pattern "class MapsViewModel"
```

- `MainViewModel.SelectedMaps : IReadOnlyList<MapEntry>`, `SelectedMapCount : int`, `NotifySelectionChanged()`,
  `ClearSelectionCommand`, `LastOpenedMap`.
- `MainViewModel.RunGameWriteAsync(string label, IReadOnlyList<string> undoPaths, Func<IProgress<string>, CancellationToken, Task> work, string doneText, bool clearTicks = false) : Task<bool>`.
  It appends the done sentence itself and clears ticks on success when `clearTicks`.
- Pages: `Maps` (`MapsViewModel`), `Backgrounds` (`BackgroundsViewModel`), `Packs`, `PackDetail`, `SettingsPage`.
  Platforms gone. Commands `NavigateMapsCommand` and friends.
- `MainViewModel.OpenAddPicturesAsync(AddPicturesTarget target)` with
  `record AddPicturesTarget(AddPicturesTargetKind Kind, MapEntry? Map, IReadOnlyList<MapEntry>? Maps)` and
  `enum AddPicturesTargetKind { None, Map, Ticked, All }` in `BhMaps.App.ViewModels`.
- `MainViewModel.OpenBackgroundEditorAsync(BackgroundEditorRequest request)` with
  `record BackgroundEditorRequest(string SourcePath, string? PackName, string? Slot)`. Part C implements the editor
  side; part B only calls it.
- `PackApplier.ApplyToMaps(...)` is part A's. Part B does not touch it.

**If a contract is missing when its task starts**, create exactly the declaration above in
`src/BhMaps.App/ViewModels/AddPicturesTarget.cs` or `src/BhMaps.App/ViewModels/BackgroundEditorRequest.cs` and carry
on. Do not invent a different shape and do not rename anything.

## Decisions this plan makes. Do not re-litigate.

| # | Question | Decision |
|---|---|---|
| BD1 | Where a tile's menu items come from | The tile view model exposes `MenuItems : IReadOnlyList<TileMenuCommand>`, rebuilt by `RebuildMenu(int tickedCount)`. The shared `TileMenu` ContextMenu resource binds `ItemsSource="{Binding MenuItems}"`; a ContextMenu set on an element inherits that element's DataContext, so no PlacementTarget plumbing is needed. |
| BD2 | "Apply to the N ticked maps" when nothing is ticked | **Omitted from the menu**, not disabled. `RebuildMenu` runs on every `SelectedMapCount` change for the live tiles (the panel's, the custom shelf's, the expanded sections'). Rebuilding five records per tile is cheaper than a converter per item and keeps the copy honest. |
| BD3 | One tile class or several | An abstract `PictureTileViewModel` carries the whole drawn surface (Title, Subtitle, Thumbnail, IsInGame, ApplyText, ShowChevron, MenuItems, ApplyCommand). `MapPictureTileViewModel` is a picture aimed at one map; `CustomPictureTileViewModel` is a picture with no map. One keyed DataTemplate per surface (page tile, panel tile) draws both. |
| BD4 | The in-game tick on a pack's picture | Read off `ScanSnapshot.MapStatuses` through the shared `InGameMatch` helper. **No new hashing and no dependency on which maps are ticked.** This deletes `BackgroundsViewModel._slotHashes`, `ComputeHashes`, `LibraryFiles`, `Hash` and `UpdateTicks`. |
| BD5 | Which search box the Backgrounds page uses | Its own `BackgroundsViewModel.SearchText`, not `MainViewModel.SearchText`. Two pages with different placeholders ("Search maps", "Search pictures") and different match rules must not share one string. |
| BD6 | A map with more than one background slot | Tiles are keyed on `map.BackgroundSlots[0]`; the apply writes the picture into **every** slot, as `BackgroundApplier` already does. That is what v2 shipped and 2.1 does not change it. |
| BD7 | "Show files" on a platform set tile | Sets the panel's `FilesExpanded = true`, revealing the map's platform file list under the Platforms segment. It is the list that already exists; nothing new opens. |
| BD8 | A custom picture's source path with no library copy | `Path.Combine(gamePath, "Backgrounds", DisplayName)`. `CustomPictureLibrary` guarantees such a picture's `DisplayName` is the name of a file in the game's `Backgrounds` folder. |
| BD9 | "Nothing to apply to" | The old page-level guard (it fired whenever no map was ticked) goes. One better-worded `Dialogs.Info("Nothing to apply", ...)` stays inside `ApplyPictureAsync` and `ApplySetAsync` for the genuinely impossible case, because a menu item that silently does nothing is worse than one extra dialog. |
| BD10 | Keyboard menu access | An explicit `PreviewKeyDown` handler (`TileMenus.OnPreviewKeyDown`) on each page root, reading `e.Key == Key.System ? e.SystemKey : e.Key` so Shift+F10 is caught whichever way Windows delivers it, and setting `e.Handled` so WPF's own handling cannot double-open. |
| BD11 | Themed menus | `TileContextMenu` and `TileMenuItem` are `BasedOn` the Fluent implicit styles and set tokens only; **neither is re-templated.** Verified 2026-09-11 with a probe app: a `BasedOn="{StaticResource {x:Type ContextMenu}}"` style inside a dictionary merged into `Application.Resources` under `ThemeMode="Dark"` resolves and the app loads. The hover fill stays Fluent's. If it reads wrong against the warm near-black that is a follow-up, not a re-template here. |
| BD12 | Where the panel's Add Custom Image button lives | In the custom-pictures strip header row of the Background segment, right-aligned, `PlainButton` with the Plus glyph. Spec 3.2 lists only Reset this map and Open folder in the actions row, and the button belongs beside the pictures it adds. |

---

## File structure

```
src/BhMaps.Core/Operations/CustomPictureLibrary.cs        NEW     (B1)
src/BhMaps.App/Services/AppServices.cs                    MODIFY  (B1)  ScanSnapshot.CustomPictures
src/BhMaps.App/Theme/Icons.xaml                           MODIFY  (B2)  Icon.Dots
src/BhMaps.App/Theme/Controls.xaml                        MODIFY  (B2)  TileContextMenu, TileMenuItem, TileMenu,
                                                                        SegmentControl, PanelTile, SectionHeaderRow
src/BhMaps.App/ViewModels/TileMenuCommand.cs              NEW     (B2)
src/BhMaps.App/Views/Controls/TileMenus.cs                NEW     (B2)  keyboard and menu-button opening
src/BhMaps.App/Services/ExplorerLauncher.cs               MODIFY  (B2)  Reveal
src/BhMaps.App/ViewModels/InGameMatch.cs                  NEW     (B3)
src/BhMaps.App/ViewModels/PictureTileViewModel.cs         NEW     (B3)  base + MapPictureTileViewModel
src/BhMaps.App/ViewModels/CustomPictureTileViewModel.cs   NEW     (B3)
src/BhMaps.App/ViewModels/PlatformSetTileViewModel.cs     NEW     (B3)
src/BhMaps.App/ViewModels/MainViewModel.cs                MODIFY  (B3, B8)
src/BhMaps.App/ViewModels/MapPanelViewModel.cs            REWRITE (B4, B5)
src/BhMaps.App/ViewModels/Pages/MapsViewModel.cs          MODIFY  (B4)  PanelShowsPlatforms, RebuildMenus
src/BhMaps.App/Views/Pages/MapsView.xaml(.cs)             MODIFY  (B4, B5)  the panel half only
src/BhMaps.App/ViewModels/Pages/BackgroundsViewModel.cs   REWRITE (B6, B7)
src/BhMaps.App/ViewModels/PictureSectionViewModel.cs      NEW     (B7)
src/BhMaps.App/Views/Pages/BackgroundsView.xaml(.cs)      REWRITE (B6, B7)
src/BhMaps.App/ViewModels/BackgroundTileViewModel.cs      DELETE  (B6)  replaced by PictureTileViewModel
src/BhMaps.App/ViewModels/AddPicturesViewModel.cs         MODIFY  (B8)  the four "Then" radios
src/BhMaps.App/Views/AddPicturesWindow.xaml(.cs)          MODIFY  (B8)
tests/BhMaps.Core.Tests/CustomPictureLibraryTests.cs      NEW     (B1)
docs/manual.md                                            NOT TOUCHED by part B. B9 records the lines for part C.
```

## Task order

B1 and B2 are independent of part A and of each other and may run in parallel. B3 needs B2. B4 needs B3. B5 needs
B4. B6 needs B3. B7 needs B6. B8 needs B3. B9 needs everything. **The app builds and runs after every task.**

---

## Task B1: Core, the custom picture library

**Files:**
- Create: `src/BhMaps.Core/Operations/CustomPictureLibrary.cs`
- Test: `tests/BhMaps.Core.Tests/CustomPictureLibraryTests.cs`
- Modify: `src/BhMaps.App/Services/AppServices.cs` (the `ScanSnapshot` record at line 12, the
  `return new ScanSnapshot(...)` at line 98)

**Interfaces:**
- Consumes: `Pack`, `GameTree`, `GameFile`, `GameFolder` (`BhMaps.Core.Model`); `MapCatalog`, `MapEntry`
  (`BhMaps.Core.Maps`); `HashCache.GetOrCompute(GameFile)` (`BhMaps.Core.Hashing`); `AssetPath.Background`
  (`BhMaps.Core.LevelData`).
- Produces:

```csharp
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName);

public static class CustomPictureLibrary
{
    public static IReadOnlyList<CustomPicture> Build(
        IReadOnlyList<Pack> packs, GameTree tree, MapCatalog catalog, HashCache hashes);
}
```

  and on `ScanSnapshot`: `IReadOnlyList<CustomPicture> CustomPictures` as the **last** positional member.

The rules, exactly:

1. A pack's `Backgrounds\*.jpg` whose **file name is not any map's background slot name** is a custom picture.
2. A file in the game's `Backgrounds\*.jpg` whose **content hash matches no file in any pack's `Backgrounds`
   folder** is a custom picture. A game file whose hash matches a pack file that is itself custom (rule 1) joins
   that picture instead of starting a new one.
3. Pictures are grouped by content hash. `LibraryPaths` is every pack copy in pack then file order; `PackName` is
   the pack of the first; `InGameSlots` is every game `Backgrounds` file name of that hash that is also a slot name
   in the catalog; `DisplayName` is the file name of the first library path, or the first matching game file name
   when there is none.
4. A file that cannot be hashed (it went away between the scan and here) is skipped, never thrown over.
5. The result is sorted by `DisplayName` then `Hash`, ordinal ignore case.

- [ ] **Step 1: write the failing tests**

Create `tests/BhMaps.Core.Tests/CustomPictureLibraryTests.cs`:

```csharp
using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class CustomPictureLibraryTests
{
    private static (GameTree Tree, IReadOnlyList<Pack> Packs, HashCache Cache) Arrange(
        TempDir tmp, Action<string, string> build)
    {
        var game = Path.Combine(tmp.Path, "game");
        var library = Path.Combine(tmp.Path, "lib");
        build(game, library);
        return (GameTreeScanner.Scan(game), PackScanner.ScanAll(library), HashCache.Load(tmp.Sub("cache.json")));
    }

    private static FakeGameTree PackTree(string library, string packName) =>
        new(Path.Combine(library, "packs", packName));

    /// <summary>One map for <paramref name="folder"/> with the background slots given.</summary>
    private static MapCatalog Catalog(string folder, params string[] slots) =>
        MapCatalog.Build(new LevelDataModel(
            [
                new LevelDesc(
                    folder,
                    folder,
                    new CameraBounds(0, 0, 100, 50),
                    slots.Select(s => new LevelBackground(s, null, null)).ToList(),
                    []),
            ],
            [new LevelType(folder, folder, false, false)],
            [],
            DateTimeOffset.UtcNow));

    [Fact]
    public void Build_TakesAPackPictureWhoseNameIsNoMapsSlot()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "grove");
            PackTree(lib, "My Backgrounds").File("Backgrounds", "sunset.jpg", "sun");
        });

        var picture = Assert.Single(
            CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("My Backgrounds", picture.PackName);
        Assert.Empty(picture.InGameSlots);
        Assert.Single(picture.LibraryPaths);
    }

    [Fact]
    public void Build_LeavesAPackPictureThatIsAMapsSlotOut()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "grove");
            PackTree(lib, "flowermap").File("Backgrounds", "BG_Grove.jpg", "flowers");
        });

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));
    }

    [Fact]
    public void Build_TakesAGameBackgroundNoPackAccountsFor()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "mine");
            PackTree(lib, "Default").File("Backgrounds", "BG_Grove.jpg", "vanilla");
        });

        var picture = Assert.Single(
            CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));

        Assert.Equal("BG_Grove.jpg", picture.DisplayName);
        Assert.Null(picture.PackName);
        Assert.Empty(picture.LibraryPaths);
        Assert.Equal(["BG_Grove.jpg"], picture.InGameSlots);
    }

    [Fact]
    public void Build_LeavesAGameBackgroundThatMatchesAPackFileOut()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Backgrounds", "BG_Grove.jpg", "flowers");
            PackTree(lib, "flowermap").File("Backgrounds", "BG_Grove.jpg", "flowers");
        });

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove", "BG_Grove.jpg"), cache));
    }

    [Fact]
    public void Build_GroupsTheSameBytesUnderTwoNamesIntoOnePicture()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game)
                .File("Backgrounds", "BG_Grove.jpg", "sun")
                .File("Backgrounds", "BG_Sewer.jpg", "sun");
            PackTree(lib, "My Backgrounds").File("Backgrounds", "sunset.jpg", "sun");
        });

        var picture = Assert.Single(CustomPictureLibrary.Build(
            packs, tree, Catalog("Grove", "BG_Grove.jpg", "BG_Sewer.jpg"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("My Backgrounds", picture.PackName);
        Assert.Equal(["BG_Grove.jpg", "BG_Sewer.jpg"], picture.InGameSlots);
    }

    [Fact]
    public void Build_KeepsOneEntryPerPictureAcrossTwoPacksAndNamesItAfterTheFirst()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
        {
            PackTree(lib, "alpha").File("Backgrounds", "sunset.jpg", "sun");
            PackTree(lib, "bravo").File("Backgrounds", "copy.jpg", "sun");
        });

        var picture = Assert.Single(CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache));

        Assert.Equal("sunset.jpg", picture.DisplayName);
        Assert.Equal("alpha", picture.PackName);
        Assert.Equal(2, picture.LibraryPaths.Count);
    }

    [Fact]
    public void Build_IgnoresFilesThatAreNotJpg()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (game, lib) =>
        {
            new FakeGameTree(game).File("Grove", "A.png", "a");
            PackTree(lib, "My Backgrounds").File("Backgrounds", "notes.png", "n");
        });

        Assert.Empty(CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache));
    }

    [Fact]
    public void Build_SortsByDisplayName()
    {
        using var tmp = new TempDir();
        var (tree, packs, cache) = Arrange(tmp, (_, lib) =>
            PackTree(lib, "My Backgrounds")
                .File("Backgrounds", "zebra.jpg", "z")
                .File("Backgrounds", "apple.jpg", "a"));

        var pictures = CustomPictureLibrary.Build(packs, tree, Catalog("Grove"), cache);

        Assert.Equal(["apple.jpg", "zebra.jpg"], pictures.Select(p => p.DisplayName));
    }
}
```

- [ ] **Step 2: run to verify failure**

Run: `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~CustomPictureLibraryTests"`
Expected: compile errors, `CustomPictureLibrary` does not exist.

- [ ] **Step 3: implement**

Create `src/BhMaps.Core/Operations/CustomPictureLibrary.cs`:

```csharp
using BhMaps.Core.Hashing;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One picture that is nobody's map art: a pack file under a name no map's background slot uses, or a
/// background in the game that no pack accounts for. One picture is one entry however many copies of it exist,
/// because the user thinks in pictures and the app should not show the same one four times (spec 4).</summary>
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName);

/// <summary>Builds the custom picture list once per scan, off the UI thread, through the scan's own hash cache.</summary>
public static class CustomPictureLibrary
{
    private const string BackgroundsFolder = "Backgrounds";
    private const string JpgExtension = ".jpg";

    public static IReadOnlyList<CustomPicture> Build(
        IReadOnlyList<Pack> packs, GameTree tree, MapCatalog catalog, HashCache hashes)
    {
        var slotNames = SlotNames(catalog);
        var byHash = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Every pack hash, slot-named or not: a game file matching any of them is a pack's art, not a picture of
        // the user's. Collected in the same pass that picks the custom ones out.
        var packHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            foreach (var file in Jpgs(pack.FindFolder(BackgroundsFolder)))
            {
                if (Hash(hashes, file) is not { } hash)
                {
                    continue;
                }

                packHashes.Add(hash);
                if (!slotNames.Contains(file.Name))
                {
                    Of(byHash, hash).Library.Add((pack.Name, file.FullPath));
                }
            }
        }

        foreach (var file in Jpgs(tree.FindFolder(BackgroundsFolder)))
        {
            if (Hash(hashes, file) is not { } hash)
            {
                continue;
            }

            // A game file matching a picture already found joins it, which is how one tile learns every slot it
            // occupies. Anything else matching a pack is that pack's art for some map and belongs to no picture.
            if (!byHash.ContainsKey(hash) && packHashes.Contains(hash))
            {
                continue;
            }

            Of(byHash, hash).GameNames.Add(file.Name);
        }

        return byHash
            .Select(pair => Picture(pair.Key, pair.Value, slotNames))
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Hash, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every name a map's background slot resolves to, so a slot borrowed from a theme folder through
    /// "../" is compared on the file name the pack would actually hold.</summary>
    private static HashSet<string> SlotNames(MapCatalog catalog)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in catalog.Maps.SelectMany(m => m.BackgroundSlots))
        {
            names.Add(Path.GetFileName(AssetPath.Background(slot)));
        }

        return names;
    }

    private static CustomPicture Picture(string hash, Entry entry, HashSet<string> slotNames)
    {
        var displayName = entry.Library.Count > 0
            ? Path.GetFileName(entry.Library[0].Path)
            : entry.GameNames.Count > 0 ? entry.GameNames[0] : hash;

        return new CustomPicture(
            hash,
            displayName,
            entry.Library.Select(l => l.Path).ToList(),
            entry.GameNames.Where(slotNames.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            entry.Library.Count > 0 ? entry.Library[0].Pack : null);
    }

    private static Entry Of(Dictionary<string, Entry> byHash, string hash)
    {
        if (!byHash.TryGetValue(hash, out var entry))
        {
            entry = new Entry();
            byHash[hash] = entry;
        }

        return entry;
    }

    private static IEnumerable<GameFile> Jpgs(GameFolder? folder) =>
        folder is null
            ? Array.Empty<GameFile>()
            : folder.Files.Where(f => JpgExtension.Equals(Path.GetExtension(f.Name), StringComparison.OrdinalIgnoreCase));

    /// <summary>Null for a file that has gone since the scan, which simply never becomes a picture.</summary>
    private static string? Hash(HashCache hashes, GameFile file)
    {
        try
        {
            return hashes.GetOrCompute(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Entry
    {
        public List<(string Pack, string Path)> Library { get; } = [];

        public List<string> GameNames { get; } = [];
    }
}
```

- [ ] **Step 4: carry the result on the scan snapshot**

In `src/BhMaps.App/Services/AppServices.cs`, add the member to the record (line 12) as the last positional
parameter and fill it in `Scan` (line 98). Everything else in the file is unchanged.

```csharp
public sealed record ScanSnapshot(
    GameTree Tree,
    IReadOnlyList<Pack> Packs,
    StatusReport Status,
    MapCatalog Catalog,
    IReadOnlyDictionary<string, MapStatus> MapStatuses,
    IReadOnlyList<LibraryBackground> Backgrounds,
    Pack? DefaultPack,
    IReadOnlyList<CustomPicture> CustomPictures);
```

and the return at the end of `Scan`:

```csharp
        return new ScanSnapshot(
            tree,
            packs,
            status,
            catalog,
            mapStatuses,
            BackgroundLibrary.Build(packs, tree),
            DefaultPack.Find(packs),

            // Built here, inside the scan, because it hashes: every page reads the list rather than computing one.
            CustomPictureLibrary.Build(packs, tree, catalog, HashCache));
```

`HashCache.Save()` already runs above this line, so the hashes this adds are written on the next scan. That is the
existing behaviour for every other hash the scan takes and is not a bug to fix here.

- [ ] **Step 5: verify**

```
dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~CustomPictureLibraryTests"
dotnet build BhMaps.slnx -c Debug
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 8 tests pass, 0 warnings, 0 formatting changes.

- [ ] **Step 6: commit**

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(core): group custom pictures by content hash" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B2: the themed menus and the shared tile chrome

**Files:**
- Modify: `src/BhMaps.App/Theme/Icons.xaml` (append before `</ResourceDictionary>`; the last entry today is
  `Icon.ChevronLeft` at line 28)
- Modify: `src/BhMaps.App/Theme/Controls.xaml` (append after `MonoText`, line 437)
- Create: `src/BhMaps.App/ViewModels/TileMenuCommand.cs`
- Create: `src/BhMaps.App/Views/Controls/TileMenus.cs`
- Modify: `src/BhMaps.App/Services/ExplorerLauncher.cs` (add `Reveal` after `Open`, line 29)

**Interfaces:**
- Consumes: the Fluent implicit `ContextMenu` and `MenuItem` styles (`ThemeMode="Dark"` in `App.xaml` line 7); the
  tokens in `Theme/Tokens.xaml`; `Sans` from `Theme/Fonts.xaml`.
- Produces, as resource keys: `TileContextMenu` (ContextMenu), `TileMenuItem` (MenuItem), `TileMenu` (a
  `ContextMenu` instance, `x:Shared="False"`), `SegmentControl` (ToggleButton), `PanelTile` (Border),
  `SectionHeaderRow` (ToggleButton), `Icon.Dots` (Geometry). And in C#:

```csharp
public sealed record TileMenuCommand(string Text, System.Windows.Input.ICommand Command);

public static class TileMenus
{
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e);
    public static void OpenFor(object sender);
}

public static class ExplorerLauncher
{
    public static string? Reveal(string filePath);   // new, beside the existing Open
}
```

- [ ] **Step 1: the menu-button icon**

Append to `src/BhMaps.App/Theme/Icons.xaml`, after `Icon.ChevronLeft`:

```xml
  <!-- Three dots, drawn as three zero length strokes: the Icon control caps every stroke round, so each one comes
       out a 2 unit disc and no fill geometry is needed. -->
  <Geometry x:Key="Icon.Dots">M5 12h.01 M12 12h.01 M19 12h.01</Geometry>
```

- [ ] **Step 2: the menu styles and the shared menu resource**

Append to `src/BhMaps.App/Theme/Controls.xaml`, after the `MonoText` style and before `</ResourceDictionary>`:

```xml
  <!-- Spec section 13: the menus are themed rather than left as the platform's. BasedOn the Fluent implicit style,
       the lesson Views/AddPicturesWindow.xaml line 40 records: without it these fall off the Fluent theme
       (ThemeMode=Dark in App.xaml) onto the legacy template, which on this palette draws a light menu. Setters
       only, no re-template: the roles a MenuItem template carries (submenu, check column, icon column) are not
       worth re-implementing for a five line menu. -->
  <Style x:Key="TileContextMenu" TargetType="ContextMenu" BasedOn="{StaticResource {x:Type ContextMenu}}">
    <Setter Property="FontFamily" Value="{StaticResource Sans}" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
    <Setter Property="Background" Value="{StaticResource SurfaceBrush}" />
    <Setter Property="BorderBrush" Value="{StaticResource Line2Brush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="4" />
  </Style>

  <!-- Header and Command are setters rather than per item markup, so one ItemsSource of TileMenuCommand drives
       every tile's menu and a page never writes a MenuItem by hand. A literal MenuItem that sets its own Header
       still wins: a local value beats a style setter. -->
  <Style x:Key="TileMenuItem" TargetType="MenuItem" BasedOn="{StaticResource {x:Type MenuItem}}">
    <Setter Property="FontFamily" Value="{StaticResource Sans}" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
    <Setter Property="Padding" Value="10,6" />
    <Setter Property="Header" Value="{Binding Text}" />
    <Setter Property="Command" Value="{Binding Command}" />
    <Setter Property="AutomationProperties.Name" Value="{Binding Text}" />
  </Style>

  <!-- One menu for every tile in the app. x:Shared="False" so each StaticResource reference is a fresh instance,
       and no ItemsSource plumbing: a ContextMenu set on an element inherits that element's DataContext, which is
       the tile view model. -->
  <ContextMenu x:Key="TileMenu"
               x:Shared="False"
               ItemContainerStyle="{StaticResource TileMenuItem}"
               ItemsSource="{Binding MenuItems}"
               Style="{StaticResource TileContextMenu}" />
```

- [ ] **Step 3: the segment, the panel tile and the section header**

Append to `src/BhMaps.App/Theme/Controls.xaml`, after the menu styles:

```xml
  <!-- One segment of a two or three way control (spec 3.2 and 5). The host is a Surface2 Border of Padding 2 with
       these side by side inside it; the chosen one is the Surface pill. Part C's pack detail uses the same style
       for Combined / Backgrounds / Platforms, so nothing about it names a page. -->
  <Style x:Key="SegmentControl" TargetType="ToggleButton">
    <Setter Property="FontFamily" Value="{StaticResource Sans}" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Padding" Value="14,5" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{StaticResource DialogFocusRing}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToggleButton">
          <ControlTemplate.Resources>
            <!-- See PrimaryButton: a string ContentPresenter needs the Foreground handed to it as a local value. -->
            <DataTemplate DataType="{x:Type sys:String}">
              <TextBlock Text="{Binding}"
                         FontFamily="{Binding FontFamily, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"
                         FontSize="{Binding FontSize, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"
                         Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}" />
            </DataTemplate>
          </ControlTemplate.Resources>
          <Border x:Name="Chrome"
                  Background="{TemplateBinding Background}"
                  CornerRadius="4"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="{StaticResource SurfaceBrush}" />
              <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Chrome" Property="Opacity" Value="0.4" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- A tile in the 360 px panel: two across at 156 with an 8 px gutter is 320, which is the panel's content
       width. The in-game ring and tick are the DataTemplate's, not this style's. -->
  <Style x:Key="PanelTile" TargetType="Border" BasedOn="{StaticResource CardBorder}">
    <Setter Property="Width" Value="156" />
    <Setter Property="Margin" Value="0,0,8,8" />
    <Setter Property="Padding" Value="6" />
  </Style>
```

```xml
  <!-- A collapsible section's header: a leading chevron, the label and its count, the whole row clickable
       (spec 4). Two chevron geometries rather than one rotated, so there is no transform origin to get wrong.
       Foreground on the style, so the icon inherits the colour the label states. -->
  <Style x:Key="SectionHeaderRow" TargetType="ToggleButton">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
    <Setter Property="HorizontalAlignment" Value="Stretch" />
    <Setter Property="HorizontalContentAlignment" Value="Left" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ToggleButton">
          <Grid>
            <Border x:Name="Chrome"
                    Background="Transparent"
                    CornerRadius="{StaticResource Radius}"
                    Padding="8,7">
              <StackPanel Orientation="Horizontal">
                <controls:Icon x:Name="Chevron"
                               Margin="0,0,8,0"
                               VerticalAlignment="Center"
                               Geometry="{StaticResource Icon.ChevronRight}"
                               Size="14" />
                <TextBlock FontFamily="{StaticResource Sans}"
                           FontSize="13"
                           Foreground="{StaticResource Text2Brush}"
                           Text="{Binding Content, RelativeSource={RelativeSource TemplatedParent}}" />
              </StackPanel>
            </Border>
            <Border x:Name="Ring"
                    BorderBrush="{StaticResource TextBrush}"
                    BorderThickness="1"
                    CornerRadius="{StaticResource Radius}"
                    IsHitTestVisible="False"
                    Opacity="0" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Chevron" Property="Geometry" Value="{StaticResource Icon.ChevronDown}" />
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chrome" Property="Background" Value="{StaticResource Surface2Brush}" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Ring" Property="Opacity" Value="0.6" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

`Controls.xaml` already declares `xmlns:controls` (line 3) and `xmlns:sys` (line 4), so neither needs adding.

- [ ] **Step 4: the menu record and the two ways to open a menu**

Create `src/BhMaps.App/ViewModels/TileMenuCommand.cs`:

```csharp
using System.Windows.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One line of a tile's menu: the words spec section 11 gives it and the command it runs. A record, so a
/// tile rebuilding its menu when the ticked count changes costs five allocations and no bookkeeping.</summary>
public sealed record TileMenuCommand(string Text, ICommand Command);
```

Create `src/BhMaps.App/Views/Controls/TileMenus.cs`:

```csharp
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BhMaps.App.Views.Controls;

/// <summary>The two ways a tile's menu opens that are not a right click: the keyboard (spec section 13) and the
/// menu button drawn on the tile. Both find the ContextMenu on the element that carries it rather than binding a
/// popup to a view model, because opening a popup is a view concern.</summary>
public static class TileMenus
{
    /// <summary>Shift+F10 and the menu key, on whatever has focus. Windows delivers F10 as a system key, so the
    /// real key is read out of SystemKey when that is what arrived. Handled is set so WPF's own handling of the
    /// same two keys cannot open the menu a second time.</summary>
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var wanted = key == Key.Apps
            || (key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
        if (!wanted || Keyboard.FocusedElement is not FrameworkElement focused)
        {
            return;
        }

        if (Open(focused))
        {
            e.Handled = true;
        }
    }

    /// <summary>The tile's menu button. The button itself carries no menu, so the search walks up to the tile.</summary>
    public static void OpenFor(object sender)
    {
        if (sender is FrameworkElement element)
        {
            Open(element);
        }
    }

    /// <summary>False when nothing from here up owns a menu, which is not an error: a focused search box has none.</summary>
    private static bool Open(FrameworkElement start)
    {
        var element = start;
        while (element is not null && element.ContextMenu is null)
        {
            element = VisualTreeHelper.GetParent(element) as FrameworkElement;
        }

        if (element?.ContextMenu is not { } menu)
        {
            return false;
        }

        menu.PlacementTarget = element;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        return true;
    }
}
```

- [ ] **Step 5: Show in folder**

Add to `src/BhMaps.App/Services/ExplorerLauncher.cs`, after `Open`:

```csharp
    /// <summary>Opens the file's folder with the file selected, which is what "Show in folder" means. Null when it
    /// worked, otherwise why it did not. A missing file is caught first, because explorer handed a path that is
    /// not there opens Documents instead, which looks like the menu item doing nothing.</summary>
    public static string? Reveal(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return $"File does not exist: {filePath}";
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true })
                ?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
```

- [ ] **Step 6: verify**

```
dotnet build BhMaps.slnx -c Debug
dotnet format BhMaps.slnx --verify-no-changes
```

Then run the app against the dev tree and confirm it still starts, because a `BasedOn` that cannot find its base
style is a load-time failure, not a compile error:

```powershell
dotnet run --project src\BhMaps.App -- --game "<devtree>\game\mapArt" --library "<devtree>\lib" --appdata "<devtree>\appdata"
```

Expected: the window opens as it did before this task (nothing consumes the new styles yet). A
`XamlParseException` naming `{x:Type ContextMenu}` or `{x:Type MenuItem}` would mean the Fluent implicit style is
not in scope from `Controls.xaml`; the fix is to move those two styles, unchanged and under the same keys, into the
`<Application.Resources>` block of `src/BhMaps.App/App.xaml` beside the existing `FileContainer` style. That case
was probed on 2026-09-11 and did not occur; this is the remedy if the probe stops holding.

- [ ] **Step 7: commit**

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(theme): themed context menus, segments and tile chrome" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B3: the shared tile object model and the two multi-map writes

**Files:**
- Create: `src/BhMaps.App/ViewModels/InGameMatch.cs`
- Create: `src/BhMaps.App/ViewModels/PictureTileViewModel.cs`
- Create: `src/BhMaps.App/ViewModels/CustomPictureTileViewModel.cs`
- Create: `src/BhMaps.App/ViewModels/PlatformSetTileViewModel.cs`
- Modify: `src/BhMaps.App/ViewModels/MainViewModel.cs` (add `ApplyPictureAsync` and `ApplySetAsync` after
  `ApplyPicturesAsync`, around line 466)

Nothing consumes the new classes yet, so the app builds and runs exactly as it did.

**Interfaces:**
- Consumes: part A's `SelectedMaps`, `SelectedMapCount`, `RunGameWriteAsync(..., clearTicks)`,
  `OpenBackgroundEditorAsync(BackgroundEditorRequest)`; `TileMenuCommand` (B2); `ExplorerLauncher.Reveal` (B2);
  `CustomPicture` (B1); `BackgroundApplier`, `PlatformSetApplier`, `PictureImporter`, `DefaultPack`,
  `PackScanner.PacksRoot`; `MapStatus`, `MapFileStatus`, `MapFileState`.
- Produces: `InGameMatch` (three statics, given in step 2); the abstract `PictureTileViewModel` with
  `MapPictureTileViewModel` and `CustomPictureTileViewModel` under it; `PlatformSetTileViewModel`; and on
  `MainViewModel` the two public writes
  `ApplyPictureAsync(string sourcePath, IReadOnlyList<MapEntry> maps, bool clearTicks)` and
  `ApplySetAsync(Pack pack, IReadOnlyList<MapEntry> maps, bool clearTicks)`. Every signature is exact in the
  steps below; nothing here is a sketch.

- [ ] **Step 1: check part A's contracts are in place**

```powershell
Select-String -Path src\BhMaps.App\ViewModels\MainViewModel.cs -Pattern "IReadOnlyList<MapEntry> SelectedMaps","bool clearTicks","OpenBackgroundEditorAsync"
```

Expected: three hits. A missing `BackgroundEditorRequest` or `AddPicturesTarget` is created exactly as the
Contracts section gives it; anything else missing means part A is not merged and this task waits.

- [ ] **Step 2: one rule for what the game is showing**

Create `src/BhMaps.App/ViewModels/InGameMatch.cs`. This is `MapPanelViewModel`'s old private `FileStatus`,
`Matches` and `IsInGame`, and `PlatformsViewModel`'s copy of the same, lifted so the map panel and the Backgrounds
page can never disagree about which tile wears the tick.

```csharp
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;

namespace BhMaps.App.ViewModels;

/// <summary>What the game is showing, as the last scan measured it. One rule everywhere: a file matching both the
/// Default pack and another pack counts as the other pack's, because the pack is what the user chose to apply.</summary>
public static class InGameMatch
{
    public static MapFileStatus? File(MapStatus? status, string relativePath) =>
        status?.Files.FirstOrDefault(f => f.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

    public static bool Matches(MapStatus? status, string relativePath, string packName) =>
        File(status, relativePath) is { } file
        && (packName.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            ? file.State == MapFileState.Default
            : file.PackNames.Contains(packName, StringComparer.OrdinalIgnoreCase));

    /// <summary>The tick and the ring on a platform set: every file the set would write is already the file that
    /// is there. A pack with nothing for the folder is never "in game".</summary>
    public static bool SetInGame(Pack pack, string folderName, MapStatus? status)
    {
        var paths = PlatformSetApplier.TargetPaths(pack, folderName);
        return paths.Count > 0 && paths.All(path => Matches(status, path, pack.Name));
    }
}
```

- [ ] **Step 3: the two multi-map writes**

Add to `src/BhMaps.App/ViewModels/MainViewModel.cs`, after `ApplyPicturesAsync` (line 466). Both are public
because a tile's menu, the map panel and part A's selection bar all land here; neither duplicates the write.

```csharp
    /// <summary>Spec 3.2 and 4: one picture into the background slots of every map given, as one game write.
    /// More than one map confirms with the count first (spec 3.3) and clears the ticks on success.</summary>
    public async Task ApplyPictureAsync(string sourcePath, IReadOnlyList<MapEntry> maps, bool clearTicks)
    {
        // Without level data a map has no background slots at all, so there is nowhere to write (spec 3.6).
        var targets = maps.Where(m => m.BackgroundSlots.Count > 0).ToList();
        var name = Path.GetFileName(sourcePath);
        if (targets.Count == 0)
        {
            Dialogs.Info(
                "Nothing to apply",
                "These maps have no background slots. Slots come from the game's own level data.");
            return;
        }

        if (targets.Count > 1
            && !Dialogs.Confirm(
                "Apply picture",
                $"Apply {name} to these {targets.Count} maps?\n\n{string.Join(", ", targets.Select(m => m.DisplayName))}"))
        {
            return;
        }

        var gamePath = Services.GamePath;
        var undoPaths = targets
            .SelectMany(m => BackgroundApplier.TargetPaths(m.BackgroundSlots))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {name}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(
                            BackgroundApplier.Apply(sourcePath, gamePath, map.BackgroundSlots, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{name} applied to {targets[0].DisplayName}"
                : $"{name} applied to {targets.Count} maps",
            clearTicks);

        Dialogs.ShowFailures("Some backgrounds could not be applied", failures);
    }
```

```csharp
    /// <summary>Spec 3.2: one pack's platform art onto every map given, each map getting its own set from the
    /// same pack. A map the pack has nothing for is left out rather than cleared.</summary>
    public async Task ApplySetAsync(Pack pack, IReadOnlyList<MapEntry> maps, bool clearTicks)
    {
        var targets = maps.Where(m => pack.FindFolder(m.FolderName) is { Files.Count: > 0 }).ToList();
        if (targets.Count == 0)
        {
            Dialogs.Info("Nothing to apply", $"{pack.Name} has no platform art for these maps.");
            return;
        }

        if (targets.Count > 1
            && !Dialogs.Confirm(
                "Apply platform set",
                $"Apply {pack.Name} to these {targets.Count} maps?\n\n{string.Join(", ", targets.Select(m => m.DisplayName))}"))
        {
            return;
        }

        var gamePath = Services.GamePath;
        var undoPaths = targets
            .SelectMany(m => PlatformSetApplier.TargetPaths(pack, m.FolderName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var failures = new List<FileFailure>();
        await RunGameWriteAsync(
            $"Applying {pack.Name}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in targets)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(PlatformSetApplier.Apply(pack, map.FolderName, gamePath, null, ct).Failures);
                    }
                },
                ct),
            targets.Count == 1
                ? $"{pack.Name} applied to {targets[0].DisplayName}"
                : $"{pack.Name} applied to {targets.Count} maps",
            clearTicks);

        Dialogs.ShowFailures("Some files could not be applied", failures);
    }
```

- [ ] **Step 4: the tile base and the map-bound tile**

Create `src/BhMaps.App/ViewModels/PictureTileViewModel.cs`. Write house-style `<summary>` comments on every public
member as the rest of the code base does; the bodies below are exact.

```csharp
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public abstract partial class PictureTileViewModel : ObservableObject
{
    protected PictureTileViewModel(
        MainViewModel shell, string title, string subtitle, string fullPath, string? packName, bool inGame)
    {
        Shell = shell;
        Title = title;
        Subtitle = subtitle;
        FullPath = fullPath;
        PackName = packName;
        IsInGame = inGame;
        MenuItems = [];
    }

    protected MainViewModel Shell { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string FullPath { get; }

    public string? PackName { get; }

    /// <summary>True when this picture is the one the game is showing for what the tile is about.</summary>
    public bool IsInGame { get; }

    /// <summary>The words on the hover button. A picture with no single map has no one-click Apply.</summary>
    public virtual string ApplyText => "Apply";

    /// <summary>True when the hover button opens the menu instead of applying (spec 4).</summary>
    public virtual bool ShowChevron => false;

    public virtual ICommand? ApplyCommand => null;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }

    /// <summary>Rebuilt whenever the ticked count changes, because the ticked line names it and is dropped at zero.</summary>
    public abstract void RebuildMenu(int tickedCount);

    protected static string TickedText(int tickedCount) =>
        tickedCount == 1 ? "Apply to the 1 ticked map" : $"Apply to the {tickedCount} ticked maps";

    /// <summary>Every file touch is off the UI thread. A picture that cannot be read leaves the tile blank.</summary>
    public async Task LoadThumbnailAsync(AppServices services, CancellationToken ct)
    {
        var path = FullPath;
        try
        {
            var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(path).Ticks, ct);
            if (await services.Thumbnails.GetAsync(path, mtime, ct) is { } image && !ct.IsCancellationRequested)
            {
                Thumbnail = image;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A file that vanished between the scan and the decode. A tile is never worth an error dialog.
        }
    }
}
```

In the same file, the map-bound tile:

```csharp
/// <summary>A picture aimed at one map: a pack's copy of that map's background slot, or a custom picture the
/// panel is offering for it. Used by the map panel and by the Backgrounds page's pack sections.</summary>
public sealed partial class MapPictureTileViewModel : PictureTileViewModel
{
    public MapPictureTileViewModel(
        MainViewModel shell, MapEntry map, string slot, string title, string subtitle,
        string fullPath, string? packName, bool inGame)
        : base(shell, title, subtitle, fullPath, packName, inGame)
    {
        Map = map;
        Slot = slot;
    }

    public MapEntry Map { get; }

    /// <summary>The background slot this tile is about. The apply still writes every slot the map names (BD6).</summary>
    public string Slot { get; }

    public override ICommand? ApplyCommand => ApplyToMapCommand;

    public override void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand> { new($"Apply to {Map.DisplayName}", ApplyToMapCommand) };
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(TickedText(tickedCount), ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Apply to all maps", ApplyToAllCommand));
        items.Add(new TileMenuCommand("Edit", EditCommand));
        items.Add(new TileMenuCommand("Show in folder", ShowInFolderCommand));
        MenuItems = items;
    }

    [RelayCommand]
    private Task ApplyToMapAsync() => Shell.ApplyPictureAsync(FullPath, [Map], clearTicks: false);

    [RelayCommand]
    private Task ApplyToTickedAsync() => Shell.ApplyPictureAsync(FullPath, Shell.SelectedMaps, clearTicks: true);

    [RelayCommand]
    private Task ApplyToAllAsync() =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(FullPath, snapshot.Catalog.Maps, clearTicks: false)
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() =>
        Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, Slot));

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ExplorerLauncher.Reveal(FullPath) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }
}
```

- [ ] **Step 5: the custom picture tile**

Create `src/BhMaps.App/ViewModels/CustomPictureTileViewModel.cs`. Usings as above plus `BhMaps.Core.Operations`.

```csharp
/// <summary>One custom picture, which belongs to no map: its Apply is always a choice (spec 4), so the hover
/// button opens the menu. Remove from library deletes every copy of it in the packs and is the one destructive
/// action on this page, so it confirms and names the count.</summary>
public sealed partial class CustomPictureTileViewModel : PictureTileViewModel
{
    private readonly CustomPicture _picture;

    public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle)
        : base(
            shell,
            picture.DisplayName,
            subtitle,
            picture.LibraryPaths.Count > 0
                ? picture.LibraryPaths[0]
                : Path.Combine(shell.Services.GamePath, "Backgrounds", picture.DisplayName),
            picture.PackName,
            picture.InGameSlots.Count > 0)
    {
        _picture = picture;
    }

    public override string ApplyText => "Apply to...";

    public override bool ShowChevron => true;

    private bool InLibrary => _picture.LibraryPaths.Count > 0;

    public override void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand>();
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(TickedText(tickedCount), ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Apply to all maps", ApplyToAllCommand));
        items.Add(new TileMenuCommand("Edit", EditCommand));
        items.Add(new TileMenuCommand("Show in folder", ShowInFolderCommand));
        items.Add(InLibrary
            ? new TileMenuCommand("Remove from library", RemoveFromLibraryCommand)
            : new TileMenuCommand("Save to library", SaveToLibraryCommand));
        MenuItems = items;
    }

    [RelayCommand]
    private Task ApplyToTickedAsync() => Shell.ApplyPictureAsync(FullPath, Shell.SelectedMaps, clearTicks: true);

    [RelayCommand]
    private Task ApplyToAllAsync() =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(FullPath, snapshot.Catalog.Maps, clearTicks: false)
            : Task.CompletedTask;

    [RelayCommand]
    private Task EditAsync() => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, null));

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ExplorerLauncher.Reveal(FullPath) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }

    [RelayCommand]
    private async Task RemoveFromLibraryAsync()
    {
        var paths = _picture.LibraryPaths;
        var count = paths.Count == 1 ? "1 copy" : $"{paths.Count} copies";
        if (!Shell.Dialogs.Confirm(
                "Remove from library",
                $"Remove {Title} from the library?\n\nThe {count} in your packs are deleted. Nothing in the game folder changes."))
        {
            return;
        }

        // A library write, so RunBusyAsync and SetLibraryDone, never RunGameWriteAsync. The guard is the reason
        // this is not one File.Delete: a path outside packs\ is refused rather than deleted.
        var packsRoot = PackScanner.PacksRoot(Shell.Services.LibraryPath);
        var failures = new List<FileFailure>();
        var ok = await Shell.RunBusyAsync(
            $"Removing {Title}",
            (_, ct) => Task.Run(
                () =>
                {
                    foreach (var path in paths)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!Path.GetFullPath(path).StartsWith(
                                Path.GetFullPath(packsRoot) + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            failures.Add(new FileFailure(path, "Not a file in the library."));
                            continue;
                        }

                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            failures.Add(new FileFailure(path, ex.Message));
                        }
                    }
                },
                ct));

        Shell.Dialogs.ShowFailures("Some files could not be removed", failures);
        if (ok)
        {
            Shell.SetLibraryDone($"Removed {Title} from the library");
        }

        await Shell.RescanAsync();
    }

    [RelayCommand]
    private async Task SaveToLibraryAsync()
    {
        var source = FullPath;
        var library = Shell.Services.LibraryPath;
        var pack = BackgroundEditorViewModel.DefaultPackName;
        PictureImportResult? result = null;
        var ok = await Shell.RunBusyAsync(
            $"Importing into {pack}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import([source], library, pack, PictureFit.Fill, progress, ct); },
                ct));

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("The picture could not be saved", result.Failures);
        }

        if (ok && result is { Copied: > 0 })
        {
            Shell.SetLibraryDone($"Saved {Title} into {pack}");
        }

        await Shell.RescanAsync();
    }
}
```

`RunBusyAsync` and `RescanAsync` are already public on `MainViewModel`; `SetLibraryDone` too.

- [ ] **Step 6: the platform set tile**

Create `src/BhMaps.App/ViewModels/PlatformSetTileViewModel.cs`. This replaces the class of the same name that
lived inside the Platforms page part A deleted; the preview it draws is the pack's platform art over the map's
current background, which is what that page did.

```csharp
public sealed partial class PlatformSetTileViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private readonly Action _showFiles;

    public PlatformSetTileViewModel(
        MainViewModel shell, MapEntry map, Pack pack, bool inGame, int width, int height, Action showFiles)
    {
        _shell = shell;
        _showFiles = showFiles;
        Map = map;
        Pack = pack;
        InGame = inGame;
        TileWidth = width;
        TileHeight = height;
        MenuItems = [];
    }

    public MapEntry Map { get; }

    public Pack Pack { get; }

    public string PackName => Pack.Name;

    public bool InGame { get; }

    public int TileWidth { get; }

    public int TileHeight { get; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }

    public void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand> { new($"Apply to {Map.DisplayName}", ApplyToMapCommand) };
        if (tickedCount > 0)
        {
            items.Add(new TileMenuCommand(
                tickedCount == 1 ? "Apply to the 1 ticked map" : $"Apply to the {tickedCount} ticked maps",
                ApplyToTickedCommand));
        }

        items.Add(new TileMenuCommand("Show files", ShowFilesCommand));
        items.Add(new TileMenuCommand("Open folder", OpenFolderCommand));
        MenuItems = items;
    }

    [RelayCommand]
    private Task ApplyToMapAsync() => _shell.ApplySetAsync(Pack, [Map], clearTicks: false);

    [RelayCommand]
    private Task ApplyToTickedAsync() => _shell.ApplySetAsync(Pack, _shell.SelectedMaps, clearTicks: true);

    [RelayCommand]
    private void ShowFiles() => _showFiles();

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Path.Combine(Pack.FullPath, Map.FolderName);
        if (ExplorerLauncher.Open(path) is { } error)
        {
            _shell.Dialogs.Error("Could not open the folder", error);
        }
    }
}
```

- [ ] **Step 7: verify and commit**

```
dotnet build BhMaps.slnx -c Debug
dotnet test
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings, every test passes, no formatting changes. The app is unchanged on screen.

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(app): shared tile view models and the two multi-map writes" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B4: the map panel, two segments

**Files:**
- Rewrite: `src/BhMaps.App/ViewModels/MapPanelViewModel.cs`
- Modify: `src/BhMaps.App/ViewModels/Pages/MapsViewModel.cs` (part A's renamed `HomeViewModel`)
- Modify: `src/BhMaps.App/Views/Pages/MapsView.xaml` and `.xaml.cs` (the panel `Border` only, today
  `HomeView.xaml` lines 574 to 735, plus the `BackgroundChoice` and `PlatformSet` templates at lines 232 to 342)

The view model and the markup change together: the old panel binds to names this task deletes, so splitting them
would leave a broken panel between two commits.

**Interfaces:**
- Consumes: B3's `MapPictureTileViewModel`, `PlatformSetTileViewModel`, `InGameMatch`; `ScanSnapshot.CustomPictures`
  (B1); B2's `TileMenu`, `SegmentControl`, `PanelTile`, `Icon.Dots`, `TileMenus`.
- Produces on `MapPanelViewModel`: `DisplayName`, `SetsText`, `Preview`, `StatusText`, `ShowPlatforms`,
  `ShowBackground`, `BackgroundTiles`, `PlatformTiles`, `PlatformFiles`, `FilesExpanded`, `FilesHeader`,
  `ShowBackgroundSegment`, `ShowPlatformsSegment`, `HasDefaultPack`, `ResetHint`, `RebuildMenus(int)`, `LoadAsync()`,
  `Cancel()`, and the commands
  `ResetCommand`, `OpenFolderCommand`, `CloseCommand`.
- Produces on `MapsViewModel`: `public bool PanelShowsPlatforms { get; set; }`.

Kept from the current `MapPanelViewModel` unchanged, by line number: the `PlatformFileViewModel` record (33 to 42),
`LoadPreviewAsync` (321 to 333), `LoadPlatformFilesAsync` (373 to 388), `ComposeAsync` (392 to 414),
`FolderThumbnailAsync` (418 to 426), `ThumbnailAsync` (429 to 440), `ChangesNothingAsync` (444 to 447),
`FindPack` (449 to 450), `Decode` (453 to 463), and `ResetAsync`, `ResetPaths`, `OpenFolder` and `Close` (159 to
255) with only the done text changed. Deleted: `BackgroundChoiceViewModel`, `PlatformSetViewModel`, `BuildChoices`,
`IsInUse`, `IsInGame`, `Matches`, `FileStatus`, `AddPictureAsync`, `ApplyBackgroundAsync`, `UseSetAsync`,
`CurrentBackgroundName`, `NoBackgroundText`, `LoadBackgroundThumbnailsAsync`.

- [ ] **Step 1: the panel view model**

Constructor signature changes to carry the page, so the segment can be remembered for the session:

```csharp
public MapPanelViewModel(MainViewModel shell, MapsViewModel page, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
```

New constants and fields:

```csharp
    public const int SetWidth = 312;
    public const int SetHeight = 176;
    public const string NoDefaultPackText = "No Default pack yet. Capture defaults first.";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly MapsViewModel _page;
    private readonly MapStatus? _status;
    private readonly ObservableCollection<MapPictureTileViewModel> _backgroundTiles = [];
    private readonly ObservableCollection<PlatformSetTileViewModel> _platformTiles = [];
```

Body of the constructor, after the existing assignments to `_shell`, `_map`, `_snapshot`, `DisplayName`,
`HasDefaultPack` and `ResetHint`:

```csharp
        _page = page;
        _status = status;
        SetsText = string.Join(", ", map.Sets.Select(MapCatalog.LabelFor));
        StatusText = BuildStatusText();
        ShowPlatforms = page.PanelShowsPlatforms;

        foreach (var tile in BuildBackgroundTiles())
        {
            _backgroundTiles.Add(tile);
        }

        foreach (var pack in PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs))
        {
            _platformTiles.Add(new PlatformSetTileViewModel(
                shell, map, pack, InGameMatch.SetInGame(pack, map.FolderName, status),
                SetWidth, SetHeight, () => FilesExpanded = true));
        }

        foreach (var relativePath in map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _platformFiles.Add(new PlatformFileViewModel(
                relativePath, InGameMatch.File(status, relativePath)?.Text ?? "", ChangesNothing: false, Thumbnail: null));
        }

        RebuildMenus(shell.SelectedMapCount);
```

New members:

```csharp
    public string SetsText { get; }

    public string StatusText { get; }

    public IReadOnlyList<MapPictureTileViewModel> BackgroundTiles => _backgroundTiles;

    public IReadOnlyList<PlatformSetTileViewModel> PlatformTiles => _platformTiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBackground), nameof(ShowBackgroundSegment), nameof(ShowPlatformsSegment))]
    public partial bool ShowPlatforms { get; set; }

    public bool ShowBackground => !ShowPlatforms;

    partial void OnShowPlatformsChanged(bool value) => _page.PanelShowsPlatforms = value;

    /// <summary>The two segments bind these rather than ShowPlatforms directly, the same shape the Add Custom
    /// Image fit radios use: a click on the segment that is already on is ignored instead of turning both off.</summary>
    public bool ShowBackgroundSegment
    {
        get => !ShowPlatforms;
        set
        {
            if (value)
            {
                ShowPlatforms = false;
            }
        }
    }

    public bool ShowPlatformsSegment
    {
        get => ShowPlatforms;
        set
        {
            if (value)
            {
                ShowPlatforms = true;
            }
        }
    }

    public void RebuildMenus(int tickedCount)
    {
        foreach (var tile in _backgroundTiles)
        {
            tile.RebuildMenu(tickedCount);
        }

        foreach (var tile in _platformTiles)
        {
            tile.RebuildMenu(tickedCount);
        }
    }

    /// <summary>Default first, then every pack with a picture for this map's first slot (spec 3.2).</summary>
    private IEnumerable<MapPictureTileViewModel> BuildBackgroundTiles()
    {
        if (_map.BackgroundSlots.Count == 0)
        {
            yield break;
        }

        var slot = _map.BackgroundSlots[0];
        var relative = AssetPath.Background(slot);
        var fileName = Path.GetFileName(relative);
        var packs = _snapshot.Packs
            .OrderBy(p => p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            if (pack.FindFolder(BackgroundsFolder)?.FindFile(fileName) is not { } file)
            {
                continue;
            }

            yield return new MapPictureTileViewModel(
                _shell, _map, slot, pack.Name, "", file.FullPath, pack.Name,
                InGameMatch.Matches(_status, relative, pack.Name));
        }
    }
```

The status sentence, spec 3.2 ("In game: flowermap background, Default platforms" / "Default" /
"Custom picture: sunset.jpg" / "Missing 2 files"):

```csharp
    private string BuildStatusText()
    {
        var missing = _status?.Files.Count(f => f.State == MapFileState.Missing) ?? 0;
        if (missing > 0)
        {
            return missing == 1 ? "Missing 1 file" : $"Missing {missing} files";
        }

        var slot = _map.BackgroundSlots.Count > 0 ? _map.BackgroundSlots[0] : null;
        var file = slot is null ? null : InGameMatch.File(_status, AssetPath.Background(slot));
        if (file is { State: MapFileState.Custom })
        {
            return $"Custom picture: {CustomPictureName(slot!)}";
        }

        var background = file is { State: MapFileState.Pack, PackNames.Count: > 0 }
            ? file.PackNames[0]
            : DefaultPack.Name;
        var platforms = PlatformSource();
        return background == DefaultPack.Name && platforms == DefaultPack.Name
            ? "Default"
            : $"In game: {background} background, {platforms} platforms";
    }

    /// <summary>The name the user knows the picture by, from the custom library, or the slot's own file name.</summary>
    private string CustomPictureName(string slot)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        return _snapshot.CustomPictures
                   .FirstOrDefault(p => p.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                   ?.DisplayName
               ?? fileName;
    }

    /// <summary>Custom beats a pack beats Default, over this map's own folder only.</summary>
    private string PlatformSource()
    {
        var files = (_status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(_map.FolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return "Custom";
        }

        return files.SelectMany(f => f.PackNames).FirstOrDefault() ?? DefaultPack.Name;
    }
```

`LoadAsync` keeps its shape; the order becomes preview, background thumbnails, platform set previews, platform
files, because the Background segment is the one that opens and a composite is the slow item:

```csharp
            await LoadPreviewAsync(ct);
            await LoadTileThumbnailsAsync(_backgroundTiles, ct);
            await LoadPlatformPreviewsAsync(ct);
            await LoadPlatformFilesAsync(ct);
```

```csharp
    private async Task LoadTileThumbnailsAsync(IEnumerable<PictureTileViewModel> tiles, CancellationToken ct)
    {
        foreach (var tile in tiles)
        {
            ct.ThrowIfCancellationRequested();
            await tile.LoadThumbnailAsync(_shell.Services, ct);
        }
    }

    /// <summary>The pack's platform art over the map's current background, so the tile shows what would change.</summary>
    private async Task LoadPlatformPreviewsAsync(CancellationToken ct)
    {
        var gamePath = _shell.Services.GamePath;
        var background = _map.BaseLevel.Backgrounds.Count == 0
            ? null
            : Path.Combine(gamePath, AssetPath.Background(_map.BaseLevel.Backgrounds[0].AssetName));
        foreach (var tile in _platformTiles)
        {
            ct.ThrowIfCancellationRequested();
            var image = _snapshot.Catalog.HasLevelData
                ? await ComposeAsync(_map.BaseLevel, SetWidth, SetHeight, tile.Pack.FullPath, background, ct)
                : null;
            image ??= await FolderThumbnailAsync(tile.Pack.FindFolder(_map.FolderName), ct);
            if (image is not null)
            {
                tile.Preview = image;
            }
        }
    }
```

`ComposeAsync` gains one parameter: `string? backgroundPath`, passed straight into
`new AssetSources(_shell.Services.GamePath, packRoot, backgroundPath)`. Its call in `LoadPreviewAsync` passes null.

- [ ] **Step 2: the page remembers the segment and keeps the menus current**

In `src/BhMaps.App/ViewModels/Pages/MapsViewModel.cs`:

```csharp
    /// <summary>Which half of the map panel's segment is showing, for the life of the session (spec 3.2).</summary>
    public bool PanelShowsPlatforms { get; set; }
```

Pass the page when the panel is built, in `OnSelectedChanged`:

```csharp
        var panel = new MapPanelViewModel(Shell, this, value.Map, status, snapshot);
```

And in the page's `OnShellChanged` handler, beside whatever part A already does there:

```csharp
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            Panel?.RebuildMenus(Shell.SelectedMapCount);
        }
```

- [ ] **Step 3: the panel markup**

In `src/BhMaps.App/Views/Pages/MapsView.xaml`, delete the `BackgroundChoice` and `PlatformSet` `DataTemplate`
resources and the `ChoiceContainer`, `SetContainer`, `SetTileBorder` and `UseButton` styles, and add two templates.
Keep `PictureTile`, `TileCaption`, `QuietNote`, `FilesToggle`, `PlatformFile`, `IconButton` and `PanelTileButton`
as they are.

```xml
    <!-- One background on offer for this map. The whole tile carries the menu, so a right click anywhere on it and
         the menu button open the same five lines, and Shift+F10 finds it through TileMenus. -->
    <DataTemplate x:Key="PanelPictureTile">
      <Border x:Name="Tile"
              AutomationProperties.Name="{Binding Title}"
              ContextMenu="{StaticResource TileMenu}"
              Focusable="True"
              FocusVisualStyle="{x:Null}"
              Style="{StaticResource PanelTile}">
        <StackPanel>
          <Grid>
            <Border Height="80" Style="{StaticResource PictureTile}">
              <Border CornerRadius="{StaticResource Radius}" RenderOptions.BitmapScalingMode="HighQuality">
                <Border.Background>
                  <ImageBrush ImageSource="{Binding Thumbnail}" Stretch="UniformToFill" />
                </Border.Background>
              </Border>
            </Border>

            <Border x:Name="InGameTick"
                    Margin="0,4,4,0"
                    HorizontalAlignment="Right"
                    VerticalAlignment="Top"
                    Background="{StaticResource BgBrush}"
                    CornerRadius="4"
                    Padding="4,3"
                    Visibility="Collapsed">
              <Path Data="M0,3.4 L2.8,6.2 L8,0.6"
                    Stroke="{StaticResource TextBrush}"
                    StrokeEndLineCap="Round"
                    StrokeLineJoin="Round"
                    StrokeStartLineCap="Round"
                    StrokeThickness="1.6" />
            </Border>

            <StackPanel x:Name="Actions"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                        Orientation="Horizontal"
                        Opacity="0">
              <Button AutomationProperties.Name="{Binding ApplyText}"
                      Command="{Binding ApplyCommand}"
                      Content="{Binding ApplyText}"
                      IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                      Style="{StaticResource TileAction}" />
              <Button Margin="4,0,0,0"
                      AutomationProperties.Name="More actions"
                      Click="OnTileMenuButton"
                      Style="{StaticResource TileAction}">
                <controls:Icon Geometry="{StaticResource Icon.Dots}" Size="14" />
              </Button>
            </StackPanel>
          </Grid>

          <TextBlock Margin="0,6,0,0" Style="{StaticResource TileCaption}" Text="{Binding Title}" />
        </StackPanel>
      </Border>
      <DataTemplate.Triggers>
        <DataTrigger Binding="{Binding IsInGame}" Value="True">
          <Setter TargetName="Tile" Property="BorderBrush" Value="{StaticResource TextBrush}" />
          <Setter TargetName="InGameTick" Property="Visibility" Value="Visible" />
        </DataTrigger>
        <Trigger SourceName="Tile" Property="IsMouseOver" Value="True">
          <Setter TargetName="Actions" Property="Opacity" Value="1" />
        </Trigger>
        <Trigger SourceName="Tile" Property="IsKeyboardFocusWithin" Value="True">
          <Setter TargetName="Actions" Property="Opacity" Value="1" />
          <Setter TargetName="Tile" Property="Background" Value="{StaticResource Surface2Brush}" />
        </Trigger>
      </DataTemplate.Triggers>
    </DataTemplate>
```

`TileAction` moves from `BackgroundsView.xaml` into `Theme/Controls.xaml` unchanged, under the same key, so both
pages use it. `PanelSetTile` is the same template with `Height="88"`, `{Binding Preview}` instead of
`{Binding Thumbnail}`, `{Binding PackName}` as the caption, `{Binding InGame}` in the DataTrigger, and
`Content="Apply"` with `Command="{Binding ApplyToMapCommand}"` on the first button.

Replace the panel `Border`'s contents. Its outer `Border` attributes are unchanged; the `Grid` inside drops to two
rows, because Reset this map and Open folder move up under the status line where spec 3.2 puts them.

```xml
      <Grid>
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto" />
          <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <Grid Margin="16,12,8,4">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
          </Grid.ColumnDefinitions>
          <StackPanel VerticalAlignment="Center">
            <TextBlock FontSize="16" FontWeight="SemiBold" Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis" />
            <TextBlock Margin="0,2,0,0" Style="{StaticResource QuietNote}" Text="{Binding SetsText}" TextTrimming="CharacterEllipsis" />
          </StackPanel>
          <Button Grid.Column="1"
                  AutomationProperties.Name="Close"
                  Command="{Binding CloseCommand}"
                  Foreground="{StaticResource Text2Brush}"
                  Style="{StaticResource IconButton}"
                  ToolTip="Close">
            <controls:Icon Geometry="{StaticResource Icon.X}" Size="16" />
          </Button>
        </Grid>

        <ScrollViewer Grid.Row="1" Margin="16,4,4,0" HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
          <StackPanel Margin="0,0,12,16">
            <Border Height="185" Style="{StaticResource PictureTile}">
              <Border CornerRadius="{StaticResource Radius}" RenderOptions.BitmapScalingMode="HighQuality">
                <Border.Background>
                  <ImageBrush ImageSource="{Binding Preview}" Stretch="UniformToFill" />
                </Border.Background>
              </Border>
            </Border>

            <TextBlock Margin="0,12,0,0"
                       Foreground="{StaticResource Text2Brush}"
                       Text="{Binding StatusText}"
                       TextWrapping="Wrap" />

            <StackPanel Margin="0,12,0,0" Orientation="Horizontal">
              <Button Command="{Binding ResetCommand}"
                      Content="Reset this map"
                      controls:Icon.Glyph="{StaticResource Icon.Undo}"
                      IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                      Style="{StaticResource OutlineButton}" />
              <Button Margin="8,0,0,0"
                      Command="{Binding OpenFolderCommand}"
                      Content="Open folder"
                      controls:Icon.Glyph="{StaticResource Icon.FolderOpen}"
                      Style="{StaticResource PlainButton}" />
            </StackPanel>

            <TextBlock Margin="0,8,0,0" Text="{Binding ResetHint}" TextWrapping="Wrap">
              <TextBlock.Style>
                <Style TargetType="TextBlock" BasedOn="{StaticResource QuietNote}">
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding ResetHint}" Value="">
                      <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </TextBlock.Style>
            </TextBlock>

            <Border Margin="0,16,0,0"
                    HorizontalAlignment="Left"
                    Background="{StaticResource Surface2Brush}"
                    CornerRadius="{StaticResource Radius}"
                    Padding="2">
              <StackPanel Orientation="Horizontal">
                <ToggleButton Content="Background" IsChecked="{Binding ShowBackgroundSegment}" Style="{StaticResource SegmentControl}" />
                <ToggleButton Content="Platforms" IsChecked="{Binding ShowPlatformsSegment}" Style="{StaticResource SegmentControl}" />
              </StackPanel>
            </Border>

            <ItemsControl Margin="0,12,0,0"
                          Focusable="False"
                          ItemTemplate="{StaticResource PanelPictureTile}"
                          ItemsSource="{Binding BackgroundTiles}"
                          Visibility="{Binding ShowBackground, Converter={StaticResource BoolToVis}}">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                  <WrapPanel />
                </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
            </ItemsControl>

            <!-- B5 inserts the custom pictures strip here, between the two ItemsControls. -->

            <ItemsControl Margin="0,12,0,0"
                          Focusable="False"
                          ItemTemplate="{StaticResource PanelSetTile}"
                          ItemsSource="{Binding PlatformTiles}"
                          Visibility="{Binding ShowPlatforms, Converter={StaticResource BoolToVis}}">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                  <WrapPanel />
                </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
            </ItemsControl>

            <ToggleButton Margin="0,8,0,0"
                          Content="{Binding FilesHeader}"
                          IsChecked="{Binding FilesExpanded}"
                          Style="{StaticResource FilesToggle}"
                          Visibility="{Binding ShowPlatforms, Converter={StaticResource BoolToVis}}" />

            <ScrollViewer MaxHeight="240"
                          Margin="8,4,0,0"
                          HorizontalScrollBarVisibility="Disabled"
                          VerticalScrollBarVisibility="Auto">
              <ScrollViewer.Style>
                <Style TargetType="ScrollViewer">
                  <Setter Property="Visibility" Value="Collapsed" />
                  <Style.Triggers>
                    <MultiDataTrigger>
                      <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding ShowPlatforms}" Value="True" />
                        <Condition Binding="{Binding FilesExpanded}" Value="True" />
                      </MultiDataTrigger.Conditions>
                      <Setter Property="Visibility" Value="Visible" />
                    </MultiDataTrigger>
                  </Style.Triggers>
                </Style>
              </ScrollViewer.Style>
              <ItemsControl Focusable="False"
                            ItemContainerStyle="{StaticResource FileContainer}"
                            ItemTemplate="{StaticResource PlatformFile}"
                            ItemsSource="{Binding PlatformFiles}" />
            </ScrollViewer>
          </StackPanel>
        </ScrollViewer>
      </Grid>
```

- [ ] **Step 4: the two view handlers**

On the root `UserControl` of `MapsView.xaml` add `PreviewKeyDown="OnPreviewKeyDown"`, and in
`MapsView.xaml.cs` add:

```csharp
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => TileMenus.OnPreviewKeyDown(sender, e);

    private void OnTileMenuButton(object sender, RoutedEventArgs e) => TileMenus.OpenFor(sender);
```

with `using System.Windows;`, `using System.Windows.Input;` and `using BhMaps.App.Views.Controls;`. Part A owns
Escape on this page; `TileMenus.OnPreviewKeyDown` only handles the menu key and Shift+F10 and leaves everything
else alone, so the two do not collide.

- [ ] **Step 5: verify**

```
dotnet build BhMaps.slnx -c Debug
dotnet test
dotnet format BhMaps.slnx --verify-no-changes
```

Then run against the dev tree:

```powershell
dotnet run --project src\BhMaps.App -- --game "<devtree>\game\mapArt" --library "<devtree>\lib" --appdata "<devtree>\appdata"
```

Acceptance, on Brawlhaven (the dev tree has `flowermap`, `b&w maps`, `black bgs`, `dark`, `demo`, `Default` and
`My Backgrounds`):

- The panel opens with Background chosen and shows tiles two across, Default first, one per pack that has
  `BG_Brawlhaven.jpg`, each captioned with its pack name.
- The status line is one sentence in words, not a file name, and says "Default" when the map is untouched.
- Hovering a tile shows Apply and a three-dot button. Right-clicking anywhere on the tile, clicking the three-dot
  button, and pressing Shift+F10 with the tile focused all open the same menu, on a Surface background with a
  Line2 hairline and Geist 13 items in Text, reading "Apply to Brawlhaven", "Apply to all maps", "Edit",
  "Show in folder". With three maps ticked on the grid it reads "Apply to the 3 ticked maps" as the second line.
- Clicking Platforms redraws the same area with platform sets over the map's current background, each with Apply
  and a menu whose lines are "Apply to Brawlhaven", "Show files", "Open folder". "Show files" opens the file list.
- Closing the panel and opening another map keeps the segment on Platforms.
- Apply on a tile writes and the header's done line reads "flowermap applied to Brawlhaven." plus the shared
  sentence. Undo is beside it.
- The word "Use" appears nowhere on the panel.

A capture pass, if a screenshot is wanted, is
`powershell -File <scratchpad>\cap\capture-all.ps1` with `<scratchpad>` the folder holding `cap` and `devtree`;
it launches the dev instance itself and writes into `<scratchpad>\shots`.

- [ ] **Step 6: commit**

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(maps): two-segment map panel with per-tile apply menus" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B5: the panel's custom pictures strip

**Files:** modify `src/BhMaps.App/ViewModels/MapPanelViewModel.cs` and the Background half of the panel markup in
`src/BhMaps.App/Views/Pages/MapsView.xaml`.

**Interfaces:** consumes `ScanSnapshot.CustomPictures` (B1), `MapPictureTileViewModel` (B3),
`AddPicturesTarget` (part A). Produces `CustomTiles`, `CustomExpanded`, `CustomHeader`, `HasCustomPictures` and
`AddPictureCommand` on `MapPanelViewModel`.

- [ ] **Step 1: the view model**

```csharp
    private readonly ObservableCollection<MapPictureTileViewModel> _customTiles = [];

    public IReadOnlyList<MapPictureTileViewModel> CustomTiles => _customTiles;

    [ObservableProperty]
    public partial bool CustomExpanded { get; set; }

    public string CustomHeader => $"Custom pictures ({_customTiles.Count})";

    public bool HasCustomPictures => _customTiles.Count > 0;

    /// <summary>Spec 7.1: the window opens with this map as its target, so "Add and apply to Brawlhaven" is the
    /// radio that starts on.</summary>
    [RelayCommand]
    private Task AddPictureAsync() =>
        _shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.Map, _map, null));
```

In the constructor, after the background tiles and only when the map has a slot:

```csharp
        if (_map.BackgroundSlots.Count > 0)
        {
            var slot = _map.BackgroundSlots[0];
            var fileName = Path.GetFileName(AssetPath.Background(slot));
            foreach (var picture in snapshot.CustomPictures)
            {
                var source = picture.LibraryPaths.Count > 0
                    ? picture.LibraryPaths[0]
                    : Path.Combine(shell.Services.GamePath, BackgroundsFolder, picture.DisplayName);
                _customTiles.Add(new MapPictureTileViewModel(
                    shell, map, slot, picture.DisplayName, "", source,
                    picture.PackName,
                    picture.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase)));
            }
        }
```

`RebuildMenus` gains a loop over `_customTiles`, and `LoadAsync` gains
`await LoadTileThumbnailsAsync(_customTiles, ct);` after the background thumbnails but only when `CustomExpanded`
is true; a `partial void OnCustomExpandedChanged(bool value)` starts the same load the first time it turns on,
guarded by a `_customThumbnailsStarted` bool, so a library of two hundred pictures costs nothing until it is
opened.

- [ ] **Step 2: the markup**

At the comment placeholder left in B4 step 3, inside the Background segment:

```xml
            <Grid Margin="0,4,0,0" Visibility="{Binding ShowBackground, Converter={StaticResource BoolToVis}}">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
              </Grid.ColumnDefinitions>
              <ToggleButton Content="{Binding CustomHeader}"
                            IsChecked="{Binding CustomExpanded}"
                            Style="{StaticResource SectionHeaderRow}"
                            Visibility="{Binding HasCustomPictures, Converter={StaticResource BoolToVis}}" />
              <Button Grid.Column="1"
                      VerticalAlignment="Center"
                      Command="{Binding AddPictureCommand}"
                      Content="Add Custom Image"
                      controls:Icon.Glyph="{StaticResource Icon.Plus}"
                      IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}"
                      Style="{StaticResource PlainButton}" />
            </Grid>

            <ItemsControl Focusable="False"
                          ItemTemplate="{StaticResource PanelPictureTile}"
                          ItemsSource="{Binding CustomTiles}"
                          Visibility="{Binding CustomExpanded, Converter={StaticResource BoolToVis}}">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                  <WrapPanel />
                </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
            </ItemsControl>
```

- [ ] **Step 3: verify and commit**

Build, test and format as in B4, then on the dev tree: the Background segment ends with a row reading
"Custom pictures (2)" and an Add Custom Image button; expanding it shows `v5yx6i293yy91.jpg` from My Backgrounds
with the same Apply and menu as a pack tile, and applying it puts it on the map.

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(maps): custom pictures strip in the map panel" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B6: the Backgrounds page, header and custom pictures

**Files:**
- Rewrite: `src/BhMaps.App/ViewModels/Pages/BackgroundsViewModel.cs`
- Rewrite: `src/BhMaps.App/Views/Pages/BackgroundsView.xaml`, add `BackgroundsView.xaml.cs` handlers
- Delete: `src/BhMaps.App/ViewModels/BackgroundTileViewModel.cs`
- Check: `src/BhMaps.Core/Settings/AppSettings.cs`

**Interfaces:** consumes `CustomPictureTileViewModel` (B3), `ScanSnapshot.CustomPictures` (B1), `TileMenu`,
`SectionHeaderRow`, `TileMenus` (B2). Produces on `BackgroundsViewModel`: `SearchText`, `Zoom`, `CustomTiles`,
`CustomHeader`, `HasCustomPictures`, `AddPicturesCommand`, `ClearSearchCommand`, `MinZoom = 2`, `MaxZoom = 10`.

Deleted with the old file: `HasSelection`, `SelectionText`, `IsLibraryEmpty`, `ApplyAsync`, `EditAsync`,
`CanApply`, `UpdateTicks`, `ComputeHashes`, `LibraryFiles`, `Hash`, `EditSlot`, `_slotHashes`, and the
"Nothing to apply to" dialog with them (BD9).

- [ ] **Step 1: the zoom default**

`src/BhMaps.Core/Settings/AppSettings.cs` must read `int BackgroundsZoom = 6` (spec 4). Part A owns the settings
keys; if it still says `4`, change that one default here and nothing else in the file.

- [ ] **Step 2: the view model**

```csharp
public partial class BackgroundsViewModel : PageViewModel
{
    public const int MinZoom = 2;
    public const int MaxZoom = 10;

    private ScanSnapshot? _snapshot;
    private CancellationTokenSource? _thumbnails;

    public BackgroundsViewModel(MainViewModel shell)
        : base(shell)
    {
        CustomTiles = [];
        SearchText = "";
        Zoom = Math.Clamp(shell.Services.Settings.BackgroundsZoom, MinZoom, MaxZoom);
        shell.PropertyChanged += OnShellChanged;
    }

    public override string Title => "Backgrounds";

    /// <summary>Spec 4's first section: one tile per picture, however many copies of it the library holds.</summary>
    public ObservableCollection<CustomPictureTileViewModel> CustomTiles { get; }

    /// <summary>This page's own box (BD5): file names, pack names and map names, not the Maps page's string.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial int Zoom { get; set; }

    public string CustomHeader => $"Custom pictures ({CustomTiles.Count})";

    public bool HasCustomPictures => CustomTiles.Count > 0;

    public bool IsSearching => SearchText.Length > 0;

    public string NoResultsText => $"No picture matches '{SearchText}'.";

    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        _thumbnails = new CancellationTokenSource();

        CustomTiles.Clear();
        foreach (var picture in snapshot.CustomPictures)
        {
            CustomTiles.Add(new CustomPictureTileViewModel(Shell, picture, Subtitle(picture, snapshot)));
        }

        RebuildMenus();
        OnPropertyChanged(nameof(CustomHeader));
        OnPropertyChanged(nameof(HasCustomPictures));
        _ = LoadThumbnailsAsync([.. CustomTiles], _thumbnails.Token);
    }

    /// <summary>Spec 4: "in game on 12 maps", else the pack it lives in, else that it is only in the game.</summary>
    private static string Subtitle(CustomPicture picture, ScanSnapshot snapshot)
    {
        if (picture.InGameSlots.Count > 0)
        {
            var maps = snapshot.Catalog.Maps
                .Count(m => m.BackgroundSlots.Any(s =>
                    picture.InGameSlots.Contains(
                        Path.GetFileName(AssetPath.Background(s)), StringComparer.OrdinalIgnoreCase)));
            return maps == 1 ? "in game on 1 map" : $"in game on {maps} maps";
        }

        return picture.PackName ?? "In game";
    }

    [RelayCommand]
    private Task AddPicturesAsync() =>
        Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.None, null, null));

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(NoResultsText));
        ApplySearch();
    }

    partial void OnZoomChanged(int value)
    {
        if (Shell.Services.Settings.BackgroundsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsZoom = value });
        }
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            RebuildMenus();
        }
    }

    private void RebuildMenus()
    {
        foreach (var tile in CustomTiles)
        {
            tile.RebuildMenu(Shell.SelectedMapCount);
        }
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<PictureTileViewModel> tiles, CancellationToken ct)
    {
        foreach (var tile in tiles)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await tile.LoadThumbnailAsync(Shell.Services, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
```

`ApplySearch()` is added empty in this task (`private void ApplySearch() { }`) and filled in B7, so the page
compiles and runs with the custom section alone.

- [ ] **Step 3: the page markup**

`BackgroundsView.xaml` keeps its `DockPanel Margin="24,16,24,24"` and its `PageHeader`. Inside the header's
`Actions`, three changes: the search box binds this page rather than the window, its placeholder and automation
name become "Search pictures", the zoom slider's range comes from the new constants, and the button becomes the
page's one primary action.

```xml
            <TextBox Padding="33,3,10,0"
                     AutomationProperties.Name="Search pictures"
                     Style="{StaticResource FieldTextBox}"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
```

(the `Icon.Search` glyph and the placeholder `TextBlock` beside it are unchanged except for the two strings, and
the placeholder's `DataTrigger` binds `{Binding SearchText}`)

```xml
            <controls:ZoomSlider Margin="12,0,0,0"
                                 VerticalAlignment="Center"
                                 Maximum="{x:Static pages:BackgroundsViewModel.MaxZoom}"
                                 Minimum="{x:Static pages:BackgroundsViewModel.MinZoom}"
                                 Value="{Binding Zoom}" />

            <Button Margin="12,0,0,0"
                    Command="{Binding AddPicturesCommand}"
                    Content="Add Custom Image"
                    controls:Icon.Glyph="{StaticResource Icon.Plus}"
                    IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}"
                    Style="{StaticResource PrimaryButton}" />
```

The body becomes one vertical `ScrollViewer` over a `StackPanel`; the `SelectionBar` style and its `Border` go
(spec 4: the shell's ticks are reached through the menus alone). The `TileAction` style moved to the theme in B4,
so the page's copy goes too.

```xml
    <ScrollViewer HorizontalScrollBarVisibility="Disabled" VerticalScrollBarVisibility="Auto">
      <StackPanel Margin="0,8,0,0">
        <TextBlock Style="{StaticResource SectionHeaderText}"
                   Text="{Binding CustomHeader}"
                   Visibility="{Binding HasCustomPictures, Converter={StaticResource BoolToVis}}" />

        <ItemsControl Margin="0,8,0,0"
                      Focusable="False"
                      ItemTemplate="{StaticResource PictureTile}"
                      ItemsSource="{Binding CustomTiles}">
          <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
              <UniformGrid Columns="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=ItemsControl}}" />
            </ItemsPanelTemplate>
          </ItemsControl.ItemsPanel>
        </ItemsControl>

        <StackPanel Margin="0,4,0,12" Orientation="Horizontal">
          <StackPanel.Style>
            <Style TargetType="StackPanel">
              <Setter Property="Visibility" Value="Collapsed" />
              <Style.Triggers>
                <DataTrigger Binding="{Binding HasCustomPictures}" Value="False">
                  <Setter Property="Visibility" Value="Visible" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </StackPanel.Style>
          <TextBlock VerticalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="No custom pictures yet." />
          <Button Margin="12,0,0,0"
                  Command="{Binding AddPicturesCommand}"
                  Content="Add Custom Image"
                  controls:Icon.Glyph="{StaticResource Icon.Plus}"
                  Style="{StaticResource OutlineButton}" />
        </StackPanel>

        <!-- B7 inserts the pack sections and the flat search grid here. -->
      </StackPanel>
    </ScrollViewer>
```

The `PictureTile` `DataTemplate` is the page-sized tile: the old tile markup at lines 117 to 206 of
`BackgroundsView.xaml` with four changes. `Focusable="True"` root gains `ContextMenu="{StaticResource TileMenu}"`;
the two captions bind `{Binding Title}` and `{Binding Subtitle}` instead of `FileName` and `PackName`; the tick
binds `{Binding IsInGame}` with `ToolTip="In game"`; and the `Actions` panel becomes three buttons, of which the
`DataTemplate.Triggers` show two:

```xml
                        <StackPanel x:Name="Actions" Margin="6" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                    Orientation="Horizontal" Visibility="Collapsed">
                          <Button x:Name="Apply"
                                  Command="{Binding ApplyCommand}"
                                  Content="{Binding ApplyText}"
                                  IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                                  Style="{StaticResource TileAction}" />
                          <Button x:Name="ApplyTo"
                                  AutomationProperties.Name="Apply to..."
                                  Click="OnTileMenuButton"
                                  Content="{Binding ApplyText}"
                                  controls:Icon.Glyph="{StaticResource Icon.ChevronDown}"
                                  Style="{StaticResource TileAction}"
                                  Visibility="Collapsed" />
                          <Button x:Name="Menu"
                                  AutomationProperties.Name="More actions"
                                  Click="OnTileMenuButton"
                                  Style="{StaticResource TileAction}">
                            <controls:Icon Geometry="{StaticResource Icon.Dots}" Size="14" />
                          </Button>
                        </StackPanel>
```

with one more trigger beside the existing hover and focus ones:

```xml
                  <DataTrigger Binding="{Binding ShowChevron}" Value="True">
                    <Setter TargetName="Apply" Property="Visibility" Value="Collapsed" />
                    <Setter TargetName="Menu" Property="Visibility" Value="Collapsed" />
                    <Setter TargetName="ApplyTo" Property="Visibility" Value="Visible" />
                  </DataTrigger>
```

- [ ] **Step 4: the view handlers**

Root `UserControl` gains `PreviewKeyDown="OnPreviewKeyDown"`, and `BackgroundsView.xaml.cs` gains the same two
one-line handlers as `MapsView.xaml.cs` in B4 step 4.

- [ ] **Step 5: verify and commit**

Build, test, format. On the dev tree the page shows a header reading Backgrounds with a "Search pictures" box, a
zoom slider that reaches 10, and a filled Add Custom Image button; under it "Custom pictures (2)" and two tiles,
each with "Apply to..." and a chevron on hover whose menu reads "Apply to all maps", "Edit", "Show in folder",
"Remove from library". No selection bar appears when maps are ticked; the menu grows an "Apply to the N ticked
maps" line instead.

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(backgrounds): picture-first header and custom pictures shelf" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B7: the Backgrounds page, pack sections and search

**Files:**
- Create: `src/BhMaps.App/ViewModels/PictureSectionViewModel.cs`
- Modify: `src/BhMaps.App/ViewModels/Pages/BackgroundsViewModel.cs`, `src/BhMaps.App/Views/Pages/BackgroundsView.xaml`

**Interfaces:** produces `PictureSectionViewModel` and, on `BackgroundsViewModel`, `PackSections`, `SearchResults`,
`HasPacks`, `HasResults`, and a filled `ApplySearch()`.

- [ ] **Step 1: the section**

```csharp
namespace BhMaps.App.ViewModels;

/// <summary>One pack's pictures on the Backgrounds page, collapsed to its header and count until it is opened
/// (spec 4). Tiles are built with the section; their thumbnails wait for the first expand, because a library of
/// six packs is more pictures than any one screen shows.</summary>
public partial class PictureSectionViewModel : ObservableObject
{
    public PictureSectionViewModel(string packName, IReadOnlyList<PictureTileViewModel> tiles)
    {
        PackName = packName;
        Tiles = tiles;
    }

    public string PackName { get; }

    public IReadOnlyList<PictureTileViewModel> Tiles { get; }

    public string Header => Tiles.Count == 1
        ? $"{PackName}, 1 background"
        : $"{PackName}, {Tiles.Count} backgrounds";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>Set by the page the first time it starts this section's thumbnails, so a second expand is free.</summary>
    public bool ThumbnailsStarted { get; set; }
}
```

- [ ] **Step 2: building the sections**

In `BackgroundsViewModel`, add `public ObservableCollection<PictureSectionViewModel> PackSections { get; }` and
`public ObservableCollection<PictureTileViewModel> SearchResults { get; }`, both created in the constructor, plus
`public bool HasPacks => PackSections.Count > 0;` and `public bool HasResults => SearchResults.Count > 0;`.

In `Refresh`, after the custom tiles:

```csharp
        PackSections.Clear();
        var packs = snapshot.Packs
            .OrderBy(p => p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            var tiles = PackTiles(pack, snapshot);
            if (tiles.Count > 0)
            {
                var section = new PictureSectionViewModel(pack.Name, tiles);
                section.PropertyChanged += OnSectionChanged;
                PackSections.Add(section);
            }
        }

        OnPropertyChanged(nameof(HasPacks));
        ApplySearch();
```

```csharp
    /// <summary>One tile per map the pack has a background for, in the catalog's own display order, labelled by
    /// map name with the pack under it so a search result reads "Brawlhaven, flowermap" (spec 4).</summary>
    private IReadOnlyList<PictureTileViewModel> PackTiles(Pack pack, ScanSnapshot snapshot)
    {
        var backgrounds = pack.FindFolder("Backgrounds");
        var tiles = new List<PictureTileViewModel>();
        if (backgrounds is null)
        {
            return tiles;
        }

        foreach (var map in snapshot.Catalog.Maps)
        {
            foreach (var slot in map.BackgroundSlots)
            {
                var relative = AssetPath.Background(slot);
                if (backgrounds.FindFile(Path.GetFileName(relative)) is not { } file)
                {
                    continue;
                }

                snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);
                tiles.Add(new MapPictureTileViewModel(
                    Shell, map, slot, map.DisplayName, pack.Name, file.FullPath, pack.Name,
                    InGameMatch.Matches(status, relative, pack.Name)));

                // One tile per map: a map whose levels name two slots is still one picture to choose here (BD6).
                break;
            }
        }

        return tiles;
    }

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PictureSectionViewModel.IsExpanded)
            || sender is not PictureSectionViewModel { IsExpanded: true, ThumbnailsStarted: false } section)
        {
            return;
        }

        section.ThumbnailsStarted = true;
        foreach (var tile in section.Tiles)
        {
            tile.RebuildMenu(Shell.SelectedMapCount);
        }

        if (_thumbnails is { } cts)
        {
            _ = LoadThumbnailsAsync(section.Tiles, cts.Token);
        }
    }
```

`RebuildMenus` gains a loop over `PackSections.Where(s => s.ThumbnailsStarted).SelectMany(s => s.Tiles)` and over
`SearchResults`.

- [ ] **Step 3: search**

```csharp
    /// <summary>Spec 4: while the box has anything in it the page is one flat grid of every match, instead of
    /// expanding sections behind the user's back. Clearing it puts the sections back as they were.</summary>
    private void ApplySearch()
    {
        SearchResults.Clear();
        if (SearchText.Length > 0)
        {
            foreach (var tile in CustomTiles.Cast<PictureTileViewModel>().Concat(PackSections.SelectMany(s => s.Tiles)))
            {
                if (Matches(tile))
                {
                    tile.RebuildMenu(Shell.SelectedMapCount);
                    SearchResults.Add(tile);
                }
            }

            if (_thumbnails is { } cts)
            {
                _ = LoadThumbnailsAsync([.. SearchResults], cts.Token);
            }
        }

        OnPropertyChanged(nameof(HasResults));
    }

    private bool Matches(PictureTileViewModel tile) =>
        tile.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || tile.Subtitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(tile.FullPath).Contains(SearchText, StringComparison.OrdinalIgnoreCase);
```

A tile appears in the result grid and in its own section at once; that is fine, because only one of the two is
visible at a time.

- [ ] **Step 4: the markup**

At the placeholder comment B6 left in `BackgroundsView.xaml`, inside the same `StackPanel`. The whole sections
block hides while a search is running and the result grid takes its place.

```xml
        <StackPanel>
          <StackPanel.Style>
            <Style TargetType="StackPanel">
              <Style.Triggers>
                <DataTrigger Binding="{Binding IsSearching}" Value="True">
                  <Setter Property="Visibility" Value="Collapsed" />
                </DataTrigger>
              </Style.Triggers>
            </Style>
          </StackPanel.Style>
          <ItemsControl Focusable="False" ItemsSource="{Binding PackSections}">
            <ItemsControl.ItemTemplate>
              <DataTemplate>
                <StackPanel Margin="0,4,0,0">
                  <ToggleButton Content="{Binding Header}"
                                IsChecked="{Binding IsExpanded}"
                                Style="{StaticResource SectionHeaderRow}" />
                  <ItemsControl Margin="0,8,0,4"
                                Focusable="False"
                                ItemTemplate="{StaticResource PictureTile}"
                                ItemsSource="{Binding Tiles}"
                                Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVis}}">
                    <ItemsControl.ItemsPanel>
                      <ItemsPanelTemplate>
                        <UniformGrid Columns="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=UserControl}}" />
                      </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                  </ItemsControl>
                </StackPanel>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>

          <TextBlock Margin="0,12,0,0" Foreground="{StaticResource Text2Brush}" Text="No packs in the library. Import one from Packs.">
            <TextBlock.Style>
              <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding HasPacks}" Value="False">
                    <Setter Property="Visibility" Value="Visible" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </TextBlock.Style>
          </TextBlock>
        </StackPanel>

        <StackPanel Visibility="{Binding IsSearching, Converter={StaticResource BoolToVis}}">
          <ItemsControl Focusable="False" ItemTemplate="{StaticResource PictureTile}" ItemsSource="{Binding SearchResults}">
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                <UniformGrid Columns="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=UserControl}}" />
              </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
          </ItemsControl>

          <StackPanel Margin="0,12,0,0" HorizontalAlignment="Center">
            <StackPanel.Style>
              <Style TargetType="StackPanel">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding HasResults}" Value="False">
                    <Setter Property="Visibility" Value="Visible" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </StackPanel.Style>
            <TextBlock HorizontalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="{Binding NoResultsText}" />
            <Button Margin="0,8,0,0"
                    HorizontalAlignment="Center"
                    Command="{Binding ClearSearchCommand}"
                    Content="Clear search"
                    Style="{StaticResource PlainButton}" />
          </StackPanel>
        </StackPanel>
```

No inverse boolean converter is added: the sections panel hides through the `DataTrigger` above, which is the
shape the two empty states already use, and the result panel uses the existing `BoolToVis`.

- [ ] **Step 5: verify and commit**

Build, test, format. On the dev tree: under the custom shelf, six collapsed rows reading "b&w maps, 9 backgrounds",
"black bgs, N backgrounds", "dark, 1 background", "demo, 1 background", "flowermap, N backgrounds" and, last,
"Default, N backgrounds". Expanding flowermap shows tiles labelled by map name with the pack under them, a check
on the ones in game, and hover Apply plus a menu reading "Apply to Brawlhaven", "Apply to all maps", "Edit",
"Show in folder". Typing "brawl" replaces the sections with one flat grid labelled "Brawlhaven, flowermap";
clearing the box puts the sections back collapsed as they were. Typing "sewer" with nothing to find shows
"No picture matches 'sewer'." and Clear search.

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(backgrounds): collapsible pack sections and flat search" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B8: Add Custom Image

**Files:**
- Modify: `src/BhMaps.App/ViewModels/AddPicturesViewModel.cs` (the checkbox half: constructor line 38,
  `ApplyToMaps` line 87, `ApplyToMapsLabel` line 97, `CanApplyToMaps` line 105, `_oneNamedMap` line 36)
- Modify: `src/BhMaps.App/Views/AddPicturesWindow.xaml` (title line 6, the "Maps" block lines 219 to 226)
- Modify: `src/BhMaps.App/ViewModels/MainViewModel.cs` (`OpenAddPicturesAsync` lines 357 to 416,
  `AddPicturesTargets` lines 421 to 429)

**Interfaces:** produces

```csharp
public enum AddPicturesThen { Library, Map, Ticked, All }

// on AddPicturesViewModel
public AddPicturesViewModel(IDialogs dialogs, IReadOnlyList<string> packNames, AddPicturesTargetKind kind,
    string? mapName, int tickedCount, int allCount);
public AddPicturesThen Then { get; set; }
public bool ThenLibrary { get; set; }      // and ThenMap, ThenTicked, ThenAll
public bool ShowMapChoice { get; }         // and ShowTickedChoice
public string MapChoiceText { get; }       // and TickedChoiceText, AllChoiceText
```

Gone: `ApplyToMaps`, `ApplyToMapsLabel`, `CanApplyToMaps`, `TargetMapCount`, `_oneNamedMap`.

- [ ] **Step 1: the four states**

```csharp
    public AddPicturesViewModel(
        IDialogs dialogs, IReadOnlyList<string> packNames, AddPicturesTargetKind kind, string? mapName,
        int tickedCount, int allCount)
    {
        _dialogs = dialogs;
        _mapName = mapName;
        _tickedCount = tickedCount;
        _allCount = allCount;
        Files = [];
        Files.CollectionChanged += OnFilesChanged;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        Fit = PictureFit.Fill;
        Error = "";

        // Spec 7.1: the caller's target is the radio that starts on, and a target the window cannot offer falls
        // back to the library, which is the only outcome that is always available.
        Then = kind switch
        {
            AddPicturesTargetKind.Map when mapName is not null => AddPicturesThen.Map,
            AddPicturesTargetKind.Ticked when tickedCount > 0 => AddPicturesThen.Ticked,
            AddPicturesTargetKind.All when allCount > 0 => AddPicturesThen.All,
            _ => AddPicturesThen.Library,
        };

        var existingDefault = packNames.FirstOrDefault(p =>
            p.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? NewPackChoice;
        NewPackName = existingDefault is null ? BackgroundEditorViewModel.DefaultPackName : "";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThenLibrary), nameof(ThenMap), nameof(ThenTicked), nameof(ThenAll))]
    public partial AddPicturesThen Then { get; set; }

    public bool ShowMapChoice => _mapName is not null;

    public bool ShowTickedChoice => _tickedCount > 0;

    public string MapChoiceText => $"Add and apply to {_mapName}";

    public string TickedChoiceText => _tickedCount == 1
        ? "Add and apply to the 1 ticked map"
        : $"Add and apply to the {_tickedCount} ticked maps";

    public string AllChoiceText => $"Add and apply to all {_allCount} maps";
```

`ThenLibrary`, `ThenMap`, `ThenTicked` and `ThenAll` are the same getter and ignore-false setter shape the four
`Fit*` properties already use in this file (lines 117 to 163), each setting `Then` to its own value.

- [ ] **Step 2: the window**

`Title="Add Custom Image"` on line 6. The "Maps" label, checkbox and hint at lines 219 to 226 become:

```xml
          <TextBlock Style="{StaticResource FieldLabel}" Text="Then" />
          <RadioButton Content="Add to the library"
                       FocusVisualStyle="{StaticResource DialogFocusRing}"
                       GroupName="Then"
                       IsChecked="{Binding ThenLibrary}" />
          <RadioButton Margin="0,6,0,0"
                       Content="{Binding MapChoiceText}"
                       FocusVisualStyle="{StaticResource DialogFocusRing}"
                       GroupName="Then"
                       IsChecked="{Binding ThenMap}"
                       Visibility="{Binding ShowMapChoice, Converter={StaticResource BoolToVis}}" />
          <RadioButton Margin="0,6,0,0"
                       Content="{Binding TickedChoiceText}"
                       FocusVisualStyle="{StaticResource DialogFocusRing}"
                       GroupName="Then"
                       IsChecked="{Binding ThenTicked}"
                       Visibility="{Binding ShowTickedChoice, Converter={StaticResource BoolToVis}}" />
          <RadioButton Margin="0,6,0,0"
                       Content="{Binding AllChoiceText}"
                       FocusVisualStyle="{StaticResource DialogFocusRing}"
                       GroupName="Then"
                       IsChecked="{Binding ThenAll}" />
          <TextBlock Margin="0,8,0,0"
                     Style="{StaticResource HintText}"
                     Text="The pictures go to the maps in order, repeating from the first when there are more maps than pictures." />
```

These four radios take the Fluent implicit style by default, so no `Style` attribute and no `BasedOn` is needed;
`FitRadio` stays as it is for the four fit radios above them. The Add button is already `IsDefault`, so Enter runs
the selected radio and nothing more (spec 7.1).

- [ ] **Step 3: the shell side**

`MainViewModel.OpenAddPicturesAsync` takes part A's `AddPicturesTarget` and resolves the maps from the chosen
radio rather than from a checkbox. Replace lines 357 to 429 with:

```csharp
    /// <summary>Spec 7.1: the window collects pictures, a pack and one of four outcomes. The import and any apply
    /// belong here, because both belong to the busy boundary.</summary>
    public async Task OpenAddPicturesAsync(AddPicturesTarget target)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var mapName = target.Map?.DisplayName;
        var vm = new AddPicturesViewModel(
            Dialogs,
            snapshot.Packs.Select(p => p.Name).ToList(),
            target.Kind,
            mapName,
            SelectedMapCount,
            snapshot.Catalog.Maps.Count);
        var window = new AddPicturesWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true)
        {
            return;
        }

        var sources = vm.Files.Select(f => f.FullPath).ToList();
        var packName = vm.EffectivePackName;
        PictureImportResult? result = null;
        var ok = await RunBusyAsync(
            $"Importing into {packName}",
            (progress, ct) => Task.Run(
                () => { result = PictureImporter.Import(sources, Services.LibraryPath, packName, vm.Fit, progress, ct); },
                ct));
        if (result is not null)
        {
            Dialogs.ShowFailures("Some pictures could not be imported", result.Failures);
        }

        var maps = ThenMaps(vm.Then, target, snapshot);
        if (ok && maps.Count > 0 && result is { Written.Count: > 0 })
        {
            // The apply rescans on its way out, and its done line is the one that ends up in the header.
            await ApplyPicturesAsync(maps, packName, result.Written, vm.Then == AddPicturesThen.Ticked);
            return;
        }

        if (ok)
        {
            SetLibraryDone(result?.Copied == 1
                ? $"Imported 1 picture into {packName}"
                : $"Imported {result?.Copied ?? 0} pictures into {packName}");
        }

        await RescanAsync();
    }

    /// <summary>The maps the chosen radio names. A map with no background slots is left out: without level data
    /// there is nothing to write into (spec 3.6).</summary>
    private IReadOnlyList<MapEntry> ThenMaps(AddPicturesThen then, AddPicturesTarget target, ScanSnapshot snapshot)
    {
        IEnumerable<MapEntry> maps = then switch
        {
            AddPicturesThen.Map => target.Map is null ? [] : [target.Map],
            AddPicturesThen.Ticked => SelectedMaps,
            AddPicturesThen.All => snapshot.Catalog.Maps,
            _ => [],
        };

        return maps.Where(m => m.BackgroundSlots.Count > 0).ToList();
    }
```

`ApplyPicturesAsync` (lines 434 to 466) keeps its body and gains a `bool clearTicks` parameter passed straight
through to `RunGameWriteAsync`, plus the confirm the old code did at the call site:

```csharp
    private async Task ApplyPicturesAsync(
        IReadOnlyList<MapEntry> maps, string packName, IReadOnlyList<string> written, bool clearTicks)
    {
        if (maps.Count > 1
            && !Dialogs.Confirm(
                "Apply pictures",
                $"Apply these pictures to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}"))
        {
            await RescanAsync();
            return;
        }
        // ... the existing body, with clearTicks passed as the last argument of RunGameWriteAsync
    }
```

- [ ] **Step 4: verify and commit**

Build, test, format, then on the dev tree open the window three ways:

- Backgrounds header: title "Add Custom Image", "Then" shows "Add to the library" chosen, no map line, and
  "Add and apply to all 67 maps" below it.
- A map panel's Add Custom Image: "Add and apply to Brawlhaven" is present and chosen.
- With three maps ticked, part A's selection bar Apply picture then Add Custom Image: "Add and apply to the 3
  ticked maps" is present and chosen.

Drop one picture in, press Enter: it imports, applies to whatever the radio says, the confirm names the count when
there is more than one map, and the ticks clear after the ticked write.

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "feat(pictures): Add Custom Image with four outcomes" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Task B9: the rename sweep, the manual note and the whole-part check

**Files:** whatever the greps below turn up inside the files part B owns, plus a paragraph appended to this plan.
**Do not edit `docs/manual.md`.** Part C owns it; this task only records what has to change there.

- [ ] **Step 1: the sweep**

```powershell
Select-String -Path src\BhMaps.App\**\*.xaml,src\BhMaps.App\**\*.cs -Pattern "Add picture","Add pictures","\bUse\b","Reset to default \(this map\)","Search backgrounds","Nothing to apply to","No backgrounds"
```

Every hit inside part B's files must end as one of these, and nowhere else in the app may the old string survive:

| Old | New | Where |
|---|---|---|
| "Add pictures" (header button, empty state) | "Add Custom Image" | `BackgroundsView.xaml` |
| "Add picture" (panel button) | "Add Custom Image" | `MapsView.xaml` |
| "Add pictures" (window title) | "Add Custom Image" | `AddPicturesWindow.xaml` |
| "Use" (platform set tile) | "Apply" | `MapsView.xaml`, gone with `UseButton` |
| "Reset to default (this map)" | "Reset this map" | `MapsView.xaml` |
| "Search backgrounds" | "Search pictures" | `BackgroundsView.xaml`, twice |
| "No backgrounds yet." | "No custom pictures yet." | `BackgroundsView.xaml` |
| "No backgrounds match this search." | "No picture matches 'sewer'." (the live search text) | `BackgroundsView.xaml` |
| "Nothing to apply to" dialog | gone; see BD9 | `BackgroundsViewModel.cs` |

`AddPicturesViewModel`, `AddPicturesWindow` and `OpenAddPicturesAsync` keep their type and member names: renaming
them is churn across part A's call sites for no user-visible gain. Only the strings change.

- [ ] **Step 2: the note for part C**

Append to this file, under a heading "For part C: the manual". `docs/manual.md` needs, at these lines as of
b0e4181:

- line 40 to 49, the Home paragraph: rename to Maps, and replace the panel sentence (lines 45 to 49) with the
  two-segment panel, "Add Custom Image", "Apply" instead of "Use", "Reset this map" and the tile menus.
- line 50 to 55, the Backgrounds paragraph: replace wholesale. It describes one flat grid, the tick meaning
  "in use by a selected map", and a bottom selection bar; 2.1 has a custom pictures shelf, collapsible pack
  sections, a flat search grid and no bar on this page.
- line 56 to 61, the Platforms paragraph: delete it (part A removes the page).
- line 80 to 84, the Add pictures paragraph: rename to "Add Custom Image" and replace the "It can apply them to
  the ticked maps in the same step" sentence with the four Then outcomes.
- line 138 to 142, "Applying a background": still true, but the sentence naming the Backgrounds page's Apply
  should say the tile menu.

- [ ] **Step 3: the whole-part check**

```
dotnet build BhMaps.slnx -c Debug
dotnet test
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings, every test green including the 8 new `CustomPictureLibraryTests`, no formatting changes.

Then one pass over the dev tree, checking the spec's own claims:

1. Maps, open Brawlhaven, Background segment, apply `flowermap`: the done line reads
   "flowermap applied to Brawlhaven." and the shared sentence, Undo beside it, and the tile takes the check.
2. Undo: the picture goes back and the done line clears.
3. Tick three maps on the grid, open a panel tile's menu, "Apply to the 3 ticked maps": the confirm names three
   maps, the write lands, the ticks clear.
4. Platforms segment, apply a set, then "Show files": the file list opens under the tiles.
5. Backgrounds, expand flowermap, right-click a tile, Edit: the editor opens on that file (part C may still be a
   stub; the call must reach it with the file's own path, pack and slot).
6. A custom tile's "Remove from library": the confirm names the copies, the file goes, the page redraws without
   it, and nothing in the game folder moved.
7. Shift+F10 on a focused tile on both pages opens the menu; Escape closes it; Escape again closes the panel.
8. Nothing anywhere reads "Use", "Add picture" or "Add pictures".

- [ ] **Step 4: commit**

```powershell
dotnet format BhMaps.slnx
git add -A
git commit -m "chore(copy): part B rename sweep and manual note" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Risks and open questions

**Failure modes this plan takes on, and what guards each.**

- **Data loss: "Remove from library" deletes files.** It is the only destructive action part B adds. Guards: a
  confirm that names the count, a path check that refuses anything not under `<library>\packs\`, per-file failures
  collected into the usual summary rather than an abort, and no touch of the game folder. It is not undoable: the
  undo store only holds game writes. If that is judged too sharp, the smaller version is to move the files to the
  recycle bin instead, which needs a Shell API this code base does not use today.
- **Irreversible ordering: `Undo.Begin()` replaces the previous snapshot.** Every menu Apply goes through
  `RunGameWriteAsync`, so a second Apply discards the first one's undo. That is v2's behaviour and 2.1 keeps it;
  the plan adds no new undo-crossing path.
- **Silent failure: a menu item that does nothing.** `ApplyPictureAsync` and `ApplySetAsync` each end in a
  `Dialogs.Info` when the target list comes out empty (BD9), and the ticked line is dropped from the menu rather
  than shown disabled (BD2), so there is no menu line that can be clicked to no effect.
- **Hot path: menus rebuilt on every tick change.** Bounded by the tiles that exist: the panel's ten or so, the
  custom shelf, and the sections the user has actually expanded. Section tiles get their first `RebuildMenu` when
  the section expands, so a collapsed library costs nothing. If a user expands every pack and then rubber-bands a
  selection, each tick costs a few thousand small allocations; that is the shape to measure if the grid ever feels
  sticky, and the fix would be to defer the rebuild to the `ContextMenuOpening` event.
- **Hot path: thumbnails.** Sections load theirs on first expand, the panel's custom strip on first expand, the
  search grid on each search. A search over a large library starts a decode per match; the decodes are cached by
  path and mtime in `ThumbnailProvider`, so a second search over the same pictures is free.
- **Ordering constraint between parts.** B3 onwards cannot start before part A lands, because `SelectedMaps`
  changes type and `RunGameWriteAsync` changes shape. B1 and B2 can run alongside part A. B1 touches
  `AppServices.cs` and B8 touches `MainViewModel.cs`, both of which part A also edits; expect a merge, and resolve
  it by keeping both sides rather than by reverting either.

**What would change the recommendation.**

- If the Fluent implicit `MenuItem` style turns out to draw an unusable hover on this palette, BD11 is the
  decision to revisit: re-templating `MenuItem` is real work and belongs in its own task, not smuggled into B2.
- If `ScanSnapshot.CustomPictures` measurably slows the scan on a large library, `CustomPictureLibrary.Build`
  should move behind a lazy property on the snapshot. It hashes only `Backgrounds` folders and re-uses the cache
  the scan has just filled, so this is unlikely; measure before moving it.
- If part A gives the Backgrounds page a shared search string after all, BD5 is wrong and B6 step 3 changes to
  bind the shell. Nothing else in part B depends on it.

**Not verified.**

- The Fluent `ContextMenu` and `MenuItem` styles were probed for resolution and load only, not for appearance. The
  hover fill, the corner radius and the shadow are whatever the theme draws; the acceptance step in B4 is the first
  time anyone looks at them.
- The dev tree's level data was not read while writing this plan, so the exact per-pack background counts in B7's
  acceptance ("b&w maps, 9 backgrounds") are the file counts on disk and may differ once slot names are folded onto
  maps. Treat the pack names and the ordering as the assertion; treat the numbers as approximate.
- Part A's `MapsViewModel` does not exist yet, so the exact line numbers for its `OnShellChanged` handler and its
  `OnSelectedChanged` hook are from today's `HomeViewModel` (lines 179 to 210).
