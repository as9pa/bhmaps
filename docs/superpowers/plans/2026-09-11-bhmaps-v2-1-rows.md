# BhMaps 2.1 rows pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn Backgrounds and Platforms into one-row-per-map choosing pages, bring the Platforms tab back as the fifth tab, give the Packs list a composed lead thumbnail and a preview strip, fix the Maps card's empty tag line, and amend the five tasks of the main plan that the addendum supersedes.

**Architecture:** The shell keeps its no-DI composition root and its one write wrapper. The two new pages share one abstract `RowsPageViewModel` (chips, search, zoom, rows, keyboard) so Backgrounds and Platforms cannot drift. Ticking stays on Maps alone (addendum B, q4): a rows page reads the ticked count only to word its tile menus. The choices a row offers come from one builder, `MapChoices`, which the map panel also calls, so a row and the panel can never disagree about what a map may show. `BhMaps.Core` gains two pure rules (`BackgroundChoices`, `PlatformBounds`), a cropped render, and two settings keys; nothing else in Core changes. Tasks are ordered so the app builds and runs after every commit: the shared model first, then the Backgrounds page, then the Platforms page and the tab, then the Packs list, then the amendments.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3 (tests\BhMaps.Core.Tests, net10.0-windows), `dotnet format BhMaps.slnx`.

**Spec:** `docs/superpowers/specs/2026-09-11-bhmaps-v2-1-rows-addendum.md` (binding for this plan; its sections A to I supersede the base spec where they conflict, and its preamble records the owner's answers to q1 to q7) and `docs/superpowers/specs/2026-09-11-bhmaps-v2-1-design.md` (the base spec; sections 2.1, 2.2, 3.1, 3.2, 3.3, 5, 8 and 11 are the ones these tasks read). Review page: https://claude.ai/code/artifact/eed15cec-4447-461c-9ca6-48f9894027c9.

This plan continues `docs/superpowers/plans/2026-09-11-bhmaps-v2-1.md`, whose Tasks 1 to 28 it does not renumber. Task 29 starts where that plan stops. Task 33 is a list of edits to that plan's own briefs.

## Global Constraints

- Build: `dotnet build BhMaps.slnx -c Debug` must produce 0 warnings (TreatWarningsAsErrors is on). Tests: `dotnet test BhMaps.slnx`. Formatter: `dotnet format BhMaps.slnx` is the only formatter; csharpier must not be added. CI runs build Release, test, and `dotnet format --verify-no-changes`.
- Never run the app or tests against the real game folder (C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla), the real library (C:\Users\alexa\files\bh) or the real %APPDATA%\BhMaps. Launch only with all three of `--game`, `--library`, `--appdata` pointing at the dev tree: `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree\{game\mapArt,lib,appdata}`. Never launch, close or kill Brawlhalla.
- Every write to the game folder goes through MainViewModel.RunGameWriteAsync (busy boundary, undo snapshot once per action, failure window). Undo goes through the same wrapper. No restart flow: GameLauncher.RunWriteAsync becomes the write itself; AppSettings loses whileRunning; SettingsStore reads an old value and drops it with a log line.
- Every done line ends with the shared sentence: "Shows on the next match load." when Brawlhalla is running, "Shows when Brawlhalla starts." otherwise. Multi-map writes keep the confirm that names the count; single-map writes are one click.
- Copy (spec section 11, as amended by the addendum): tabs Maps, Backgrounds, Platforms, Packs, Settings; game line "Brawlhalla running" / "Brawlhalla not running" + Launch; chip row "Select all" (owner change O7; the tooltip "Ticks the maps the chips and search show"); selection bar "3 of 67 maps selected", "Apply pack", "Apply picture", "Reset to default", "Select all", "Clear"; tile menu "Apply to Brawlhaven", "Apply to the 3 selected maps", "Apply to all maps", "Apply to...", "Edit", "Show in folder", "Show files", "Remove from library", "Save to library"; buttons "Add Custom Image", "Reset this map", "Reset all to default", "Apply all", "Open folder"; first-run line "No packs yet. Import a folder of map art on the Packs page, or add a custom image on Backgrounds."
- No em-dashes and no emoji anywhere in the app, docs, commit messages or plan text.
- Theme: reuse the tokens in src/BhMaps.App/Theme/Tokens.xaml and the style keys in Controls.xaml; no new colours outside Tokens.xaml; Geist / Geist Mono only. `FocusVisualStyle` must be set as a local attribute (never through a style setter); a bare `x:Static` of a `const int` into a double property crashes at startup (use Binding Source with Mode=OneTime).
- Not changing (spec section 10): composed previews from level data, hashing and status, the packs list, Welcome, Import folder, Undo as one snapshot of the last write, the theme, DialogWindow.
- Version becomes 2.1.0 in src/BhMaps.App/BhMaps.App.csproj in the release task only.
- Commits: one commit per task minimum, message in imperative mood, ending with the trailers `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg`. Git identity must be as9pa / alexander.palladino@gmail.com.
- Implementers never dispatch subagents.

**Added by the addendum, and binding on Tasks 29 to 33:**

- The tab row is five: Maps, Backgrounds, Platforms, Packs, Settings, on Ctrl+1 to Ctrl+5. Ctrl+K still focuses the Maps search.
- Copy this plan adds to spec section 11: tab "Platforms"; "Search maps and pictures"; "Search maps and packs"; "Custom (11)"; "+7"; "No map or picture matches 'sewer'."; "No map or pack matches 'sewer'."; "&lt;pack&gt; platforms applied to &lt;map&gt;." for a set.
- Ticking belongs to Maps alone (addendum B, q4). A rows page has no tick box, no "Select all", no Selected chip, no selection bar and no Ctrl+A. It reads `MainViewModel.SelectedMapCount` only so that a tile menu can say "Apply to the 3 selected maps".
- Nothing in Tasks 29 to 33 writes to the game folder itself. Every apply goes through the existing `MainViewModel.ApplyPictureAsync` or `MainViewModel.ApplySetAsync`, both of which already go through `RunGameWriteAsync`.
- `<SHOTS>` is the controller's capture folder. Where a step names a capture, write it to `<SHOTS>\<name>.png`.

---

## Decision points

The addendum marks seven decisions `[q1]` to `[q7]`. Its preamble records that the owner answered all seven in chat at 18:40, and this plan is written to those answers, not to the recommendations that preceded them. The table is kept so the controller can patch exactly the right steps if an answer is revised. Each affected step carries its mark in the heading.

| Mark | Question | The owner's answer, which this plan builds | Steps to patch if it is revised |
|---|---|---|---|
| q1 | Zoom steps on a rows page | Dense: thumbnail height 48, 56, 72, 96, 128 px, width at 16:9; Backgrounds defaults to 2 (56 px), Platforms to 3 (72 px) | Task 29 Step 7; Task 30 Step 2; Task 31 Step 5 |
| q2 | What a strip does when the choices do not fit | Show what fits plus a "+N" tile; clicking it unfolds the row | Task 29 Step 14; Task 30 Steps 3, 6, 7 |
| q3 | Where custom pictures sit in a Backgrounds row | Folded behind one "Custom (N)" tile at the end; an in-game custom picture is unfolded and first | Task 30 Steps 2, 6 |
| q4 | Ticks on a rows page | None. No tick box, no selection bar; ticks are made on Maps and only reword the tile menus | Task 29 Step 13; Task 30 Steps 2, 6, 7; Task 31 Step 5 |
| q5 | The map panel | Keep it as built: the Background \| Platforms segment, the custom strip, reset, files | Task 29 Steps 11, 12 |
| q6 | Pack detail's Combined \| Backgrounds \| Platforms segment | Dropped; Combined is the only view and the drawer lists the halves | Task 33 amendments 2 and 3 |
| q7 | The Packs row's actions | "Apply all" stays a button; Export, Open folder and Remove move into a dots menu, in that order (Remove last, as a destructive item) | Task 32 Steps 3, 5 |

**One contradiction inside the addendum, and how this plan reads it.** Section B's header paragraph and section C's both still say the chip row carries "Select all shown" at its right, which was written before q4 was answered. Section B's later paragraph ("Ticks are made on Maps only... Ctrl+A and the selection bar belong to Maps") is the answer itself and wins: a rows page's chip row ends at the last chip, with no button after it. If the owner wants the button back, it goes in Task 30 Step 6 and Task 31 Step 5 bound to `MainViewModel.Maps.SelectAllShownCommand`, which would tick every map the *Maps* page shows, not the rows page.

---

### Task 29: the row model, the shared choice builder, and the row zoom keys

**Files:**
- Create: `src\BhMaps.Core\Operations\BackgroundChoices.cs`
- Create: `tests\BhMaps.Core.Tests\BackgroundChoicesTests.cs`
- Modify: `src\BhMaps.Core\Settings\AppSettings.cs`
- Modify: `src\BhMaps.Core\Settings\SettingsStore.cs`
- Modify: `tests\BhMaps.Core.Tests\SettingsStoreTests.cs`
- Create: `src\BhMaps.App\Services\ThumbnailCache.cs`
- Create: `src\BhMaps.App\ViewModels\MapChoices.cs`
- Create: `src\BhMaps.App\ViewModels\MapRowViewModel.cs`
- Create: `src\BhMaps.App\Views\Controls\StripPanel.cs`
- Modify: `src\BhMaps.App\ViewModels\PictureTileViewModel.cs`
- Modify: `src\BhMaps.App\ViewModels\CustomPictureTileViewModel.cs`
- Modify: `src\BhMaps.App\ViewModels\PlatformSetTileViewModel.cs`
- Modify: `src\BhMaps.App\ViewModels\MapPanelViewModel.cs`
- Modify: `src\BhMaps.App\Services\AppServices.cs`
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs`

**Interfaces:**

Consumes, all of which exist at HEAD:

```csharp
// BhMaps.Core
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders);
public sealed record GameFolder(string Name, string FullPath, IReadOnlyList<GameFile> Files)
{
    public GameFile? FindFile(string name);
}
public sealed record GameFile(string Name, string FullPath, long Size, long MtimeTicks);
public sealed record MapEntry(
    string FolderName, string DisplayName, LevelDesc BaseLevel, IReadOnlyList<LevelDesc> Levels,
    IReadOnlyList<string> Sets, IReadOnlyList<string> BackgroundSlots, IReadOnlyList<string> PlatformFiles);
public sealed record MapStatus(
    string FolderName, MapState State, IReadOnlyList<string> PackNames, IReadOnlyList<MapFileStatus> Files);
public sealed record MapFileStatus(string RelativePath, MapFileState State, IReadOnlyList<string> PackNames);
public enum MapFileState { Default, Pack, Custom, Missing }
public enum MapState { Default, Packs, Custom, Missing }
public sealed record CustomPicture(
    string Hash, string DisplayName, IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots, string? PackName);
public static class AssetPath { public static string Background(string assetName); }
public static class DefaultPack { public const string Name = "Default"; }
public static class PlatformSetApplier { public static IReadOnlyList<Pack> SetsFor(string folderName, IReadOnlyList<Pack> packs); }
public sealed class ThumbnailProvider { public Task<BitmapSource?> GetAsync(string fullPath, long mtimeTicks, CancellationToken ct = default); }

// BhMaps.App
public sealed record ScanSnapshot(
    GameTree Tree, IReadOnlyList<Pack> Packs, StatusReport Status, MapCatalog Catalog,
    IReadOnlyDictionary<string, MapStatus> MapStatuses, IReadOnlyList<LibraryBackground> Backgrounds,
    Pack? DefaultPack, IReadOnlyList<CustomPicture> CustomPictures);
public abstract partial class PictureTileViewModel : ObservableObject
{
    public string Title { get; }
    public string Subtitle { get; }
    public string FullPath { get; }
    public string? PackName { get; }
    public bool IsInGame { get; }
    public virtual string ApplyText { get; }
    public virtual bool ShowChevron { get; }
    public virtual ICommand? ApplyCommand { get; }
    public partial ImageSource? Thumbnail { get; set; }
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }
    public abstract void RebuildMenu(int tickedCount);
    public Task LoadThumbnailAsync(AppServices services, CancellationToken ct);
}
public sealed class MapPictureTileViewModel : PictureTileViewModel
{
    public MapPictureTileViewModel(MainViewModel shell, MapEntry map, string slot, string title, string subtitle,
        string fullPath, string? packName, bool inGame);
    public MapEntry Map { get; }
    public string Slot { get; }
}
public sealed partial class PlatformSetTileViewModel : ObservableObject
{
    public PlatformSetTileViewModel(MainViewModel shell, MapEntry map, Pack pack, bool inGame,
        int width, int height, Action showFiles);
    public MapEntry Map { get; }
    public Pack Pack { get; }
    public string PackName { get; }
    public bool InGame { get; }
    public partial ImageSource? Preview { get; set; }
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }
    public void RebuildMenu(int tickedCount);
}
public sealed record TileMenuCommand(string Text, ICommand Command);
public static class InGameMatch
{
    public static MapFileStatus? File(MapStatus? status, string relativePath);
    public static bool Matches(MapStatus? status, string relativePath, string packName);
    public static bool SetInGame(Pack pack, string folderName, MapStatus? status);
}
```

Produces, which Tasks 30, 31 and 32 rely on:

```csharp
// BhMaps.Core\Operations\BackgroundChoices.cs
public sealed record BackgroundChoice(Pack Pack, GameFile File);
public static class BackgroundChoices
{
    public static IReadOnlyList<BackgroundChoice> For(string slot, IReadOnlyList<Pack> packs);
}

// BhMaps.Core\Settings\AppSettings.cs
public const int MinRowZoom = 1;
public const int MaxRowZoom = 5;
// record parameters: ... int BackgroundsZoom = 2, int PackZoom = 5, bool WelcomeDone = false, int PlatformsZoom = 3

// BhMaps.App\Services\ThumbnailCache.cs
public sealed class ThumbnailCache
{
    public ThumbnailCache(ThumbnailProvider provider);
    public Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct);
    public void Clear();
}

// BhMaps.App\Services\AppServices.cs
public ThumbnailCache RowThumbnails { get; }

// BhMaps.App\ViewModels\PictureTileViewModel.cs
public string ToolTipText { get; }
public Task LoadThumbnailAsync(ThumbnailCache cache, CancellationToken ct);

// BhMaps.App\ViewModels\PlatformSetTileViewModel.cs
public string ToolTipText { get; }

// BhMaps.App\ViewModels\CustomPictureTileViewModel.cs
public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle);
public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle,
    MapEntry? map, string? slot, bool inGame);
public MapEntry? Map { get; }
public string? Slot { get; }

// BhMaps.App\ViewModels\MapChoices.cs
public static class MapChoices
{
    public static IReadOnlyList<MapPictureTileViewModel> PackBackgrounds(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot);
    public static IReadOnlyList<CustomPictureTileViewModel> CustomBackgrounds(
        MainViewModel shell, MapEntry map, ScanSnapshot snapshot);
    public static IReadOnlyList<PlatformSetTileViewModel> Platforms(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot,
        int width, int height, Action showFiles);
}

// BhMaps.App\ViewModels\MapRowViewModel.cs
public sealed partial class RowMoreViewModel : ObservableObject
{
    public RowMoreViewModel(string text, MapRowViewModel row);
    public string Text { get; }
    public MapRowViewModel Row { get; }
    public IRelayCommand UnfoldCommand { get; }
}
public sealed partial class MapRowViewModel : ObservableObject
{
    public MapRowViewModel(
        MapEntry map, string tagText, bool isMissing, bool isChanged, bool showsCustomPicture, string haystack,
        IReadOnlyList<object> alwaysShown, IReadOnlyList<object> foldedAway, string foldedLabel,
        IReadOnlyList<PictureTileViewModel> pictures, IReadOnlyList<PlatformSetTileViewModel> sets,
        Func<PlatformSetTileViewModel, CancellationToken, Task>? composeSet);
    public MapEntry Map { get; }
    public string FolderName { get; }
    public string DisplayName { get; }
    public string TagText { get; }
    public bool IsMissing { get; }
    public bool ShowTag { get; }
    public bool IsChanged { get; }
    public bool ShowsCustomPicture { get; }
    public string Haystack { get; }
    public string FoldedLabel { get; }
    public IReadOnlyList<PictureTileViewModel> Pictures { get; }
    public IReadOnlyList<PlatformSetTileViewModel> Sets { get; }
    public ObservableCollection<object> Choices { get; }
    public partial bool IsUnfolded { get; set; }
    public IRelayCommand ToggleFoldCommand { get; }
    public void Fold();
    public void RebuildMenus(int tickedCount);
    public Task LoadAsync(AppServices services, CancellationToken ct);
}

// BhMaps.App\Views\Controls\StripPanel.cs
public sealed class StripPanel : Panel
{
    public const double MoreWidth = 52;
    public bool IsUnfolded { get; set; }
    public double Spacing { get; set; }
    public int Overflow { get; }
}
```

- [ ] **Step 1: write the failing test for the background choice rule**

Create `tests\BhMaps.Core.Tests\BackgroundChoicesTests.cs`:

```csharp
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

public class BackgroundChoicesTests
{
    [Fact]
    public void For_ListsOnlyThePacksHoldingTheSlotFileAndPutsDefaultFirst()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "zeta", "BG_Grove.jpg");
        WritePack(tmp, "alpha", "BG_Sewer.jpg");
        WritePack(tmp, "Default", "BG_Grove.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        var choices = BackgroundChoices.For("BG_Grove.jpg", packs);

        Assert.Equal(new[] { "Default", "zeta" }, choices.Select(c => c.Pack.Name));
        Assert.Equal("BG_Grove.jpg", choices[1].File.Name);
        Assert.Equal(
            Path.Combine(tmp.Path, "packs", "zeta", "Backgrounds", "BG_Grove.jpg"),
            choices[1].File.FullPath);
    }

    [Fact]
    public void For_KeepsTheGivenOrderForEverythingButDefault()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "aaa", "BG_Grove.jpg");
        WritePack(tmp, "bbb", "BG_Grove.jpg");
        WritePack(tmp, "Default", "BG_Grove.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        // The Packs page shows the scan order, so a rows page must show that order too (addendum B).
        Assert.Equal(new[] { "aaa", "bbb", "Default" }, packs.Select(p => p.Name));
        Assert.Equal(
            new[] { "Default", "aaa", "bbb" },
            BackgroundChoices.For("BG_Grove.jpg", packs).Select(c => c.Pack.Name));
    }

    [Fact]
    public void For_ResolvesASlotBorrowedFromAnotherFolderToItsFileName()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "snowy", "Snow1.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        // A slot written "../Snow/Snow1.jpg" in the level data is still one file in the pack's Backgrounds folder.
        var choices = BackgroundChoices.For("../Snow/Snow1.jpg", packs);

        Assert.Equal(new[] { "snowy" }, choices.Select(c => c.Pack.Name));
    }

    [Fact]
    public void For_ReturnsNothingWhenNoPackHasThePicture()
    {
        using var tmp = new TempDir();
        WritePack(tmp, "alpha", "BG_Sewer.jpg");
        var packs = PackScanner.ScanAll(tmp.Path);

        Assert.Empty(BackgroundChoices.For("BG_Grove.jpg", packs));
        Assert.Empty(BackgroundChoices.For("BG_Grove.jpg", []));
    }

    private static void WritePack(TempDir tmp, string packName, params string[] fileNames)
    {
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(tmp.Sub("packs", packName, "Backgrounds", fileName), packName + fileName);
        }
    }
}
```

- [ ] **Step 2: run it and watch it fail**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~BackgroundChoicesTests
```

Expected: the build fails with CS0103, "The name 'BackgroundChoices' does not exist in the current context".

- [ ] **Step 3: write the rule**

Create `src\BhMaps.Core\Operations\BackgroundChoices.cs`:

```csharp
using BhMaps.Core.LevelData;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One pack's picture for one background slot: the pack it came from and the file itself.</summary>
public sealed record BackgroundChoice(Pack Pack, GameFile File);

/// <summary>Which packs can fill one map's background slot. The map panel (spec 3.2) and the Backgrounds rows
/// page (addendum B) both ask this, so the rule lives in Core and neither of them carries a copy of it.</summary>
public static class BackgroundChoices
{
    /// <summary>The folder a pack keeps its background pictures in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>Every pack holding a picture under the slot's own file name: the Default pack first, then the
    /// rest in the order they were given, which is the order the Packs page lists them in. A slot borrowed from
    /// a theme folder through "../" is still one file in the pack's Backgrounds folder, so the name is resolved
    /// through <see cref="AssetPath" /> rather than taken as typed.</summary>
    public static IReadOnlyList<BackgroundChoice> For(string slot, IReadOnlyList<Pack> packs)
    {
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var choices = new List<BackgroundChoice>();
        foreach (var pack in packs)
        {
            if (pack.FindFolder(BackgroundsFolder)?.FindFile(fileName) is { } file)
            {
                choices.Add(new BackgroundChoice(pack, file));
            }
        }

        // OrderBy is stable, so everything that is not Default keeps the order it arrived in.
        return choices
            .OrderBy(c => c.Pack.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ToList();
    }
}
```

- [ ] **Step 4: run it and watch it pass**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~BackgroundChoicesTests
```

Expected: 4 passed.

- [ ] **Step 5: write the failing settings tests [q1]**

In `tests\BhMaps.Core.Tests\SettingsStoreTests.cs`, replace the whole of `Load_ClampsEveryZoomToTwoThroughTen` with these two tests, and add the third:

```csharp
    [Fact]
    public void Load_ClampsTheGridZoomsToTwoThroughTenAndTheRowZoomsToOneThroughFive()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"mapsZoom":99,"backgroundsRowZoom":0,"packZoom":-4,"platformsZoom":9}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(10, loaded.MapsZoom);
        Assert.Equal(1, loaded.BackgroundsZoom);
        Assert.Equal(2, loaded.PackZoom);
        Assert.Equal(5, loaded.PlatformsZoom);
    }

    [Fact]
    public void Load_DropsAnOldBackgroundsZoomSoAnUpgradeStartsDense()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        // backgroundsZoom was a 2.0 tile size (3 to 8) and then a 2.1 column count (2 to 10). The rows page keeps
        // its thumbnail height under backgroundsRowZoom, so an old value is dropped rather than read as a height;
        // every upgrader starts at the dense default the owner chose (q1).
        File.WriteAllText(path, """{"backgroundsZoom":6}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(2, loaded.BackgroundsZoom);
        Assert.Null(loaded.Unknown);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsThePlatformsZoom()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");

        SettingsStore.Save(path, AppSettings.Default with { PlatformsZoom = 4, BackgroundsZoom = 1 });
        var loaded = SettingsStore.Load(path);

        Assert.Equal(4, loaded.PlatformsZoom);
        Assert.Equal(1, loaded.BackgroundsZoom);
        Assert.Contains("\"platformsZoom\": 4", File.ReadAllText(path));
        Assert.Contains("\"backgroundsRowZoom\": 1", File.ReadAllText(path));
        Assert.DoesNotContain("\"backgroundsZoom\"", File.ReadAllText(path));
    }
```

In the same file, change the assertions in `Load_MissingV21Fields_UsesDefaults` that name the zooms:

```csharp
        Assert.Equal(6, loaded.MapsZoom);
        Assert.Equal(2, loaded.BackgroundsZoom);
        Assert.Equal(3, loaded.PlatformsZoom);
        Assert.Equal(5, loaded.PackZoom);
        Assert.False(loaded.WelcomeDone);
```

- [ ] **Step 6: run them and watch them fail**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~SettingsStoreTests
```

Expected: the build fails with CS0117, "'AppSettings' does not contain a definition for 'PlatformsZoom'".

- [ ] **Step 7: the two zoom keys [q1]**

In `src\BhMaps.Core\Settings\AppSettings.cs`, the record header becomes:

```csharp
public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int MapsZoom = 6,
    int BackgroundsZoom = 2,
    int PackZoom = 5,
    bool WelcomeDone = false,
    int PlatformsZoom = 3)
{
```

and, beside `MinZoom` and `MaxZoom`, add:

```csharp
    /// <summary>Steps a rows page can be zoomed to (addendum B, q1 answered dense). A row's zoom is a thumbnail
    /// height, not a column count: 1 is 48 px and 5 is 128 px. Backgrounds starts at 2 (56 px, about twelve rows
    /// on a 1080p window) and Platforms at 3 (72 px, because platform shapes need more height than a picture
    /// does). The grid pages keep MinZoom to MaxZoom. The height is stored under backgroundsRowZoom; the old
    /// backgroundsZoom (a 2.0 tile size, then a 2.1 column count) is dropped on load, never read as a height.</summary>
    public const int MinRowZoom = 1;
    public const int MaxRowZoom = 5;
```

`PlatformsZoom` is last, after `WelcomeDone`, so every positional call that exists keeps compiling.

In `src\BhMaps.Core\Settings\SettingsStore.cs`, the keys become (the old `backgroundsZoom` stays known so it is
not carried over as an unknown key; it is dropped below):

```csharp
    private static readonly string[] KnownKeys =
    [
        "gamePath", "libraryPath", "firstRunDone", "mapsZoom", "backgroundsRowZoom", "packZoom", "platformsZoom",
        "welcomeDone", "homeZoom", "whileRunning", "backgroundsZoom",
    ];
```

directly under the `whileRunning` drop in `Load`, drop the old key the same way (one log line, same logger call):

```csharp
        // Addendum B, q1: Backgrounds is a rows page whose zoom is a thumbnail height under backgroundsRowZoom.
        // The old backgroundsZoom was a 2.0 tile size and then a 2.1 column count; read as a height it would put
        // every upgrader on the largest rows, so it is reported once and dropped.
        if (obj["backgroundsZoom"] is not null)
        {
            System.Diagnostics.Trace.WriteLine(
                "BhMaps settings: backgroundsZoom is no longer used and was dropped; the Backgrounds rows start dense.");
        }
```

and change the returned record so the two rows pages clamp into their own range:

```csharp
        return new AppSettings(
            Str(obj, "gamePath") is { Length: > 0 } g ? g : AppSettings.DefaultGamePath,
            Str(obj, "libraryPath") is { Length: > 0 } l ? l : AppSettings.DefaultLibraryPath,
            Bool(obj, "firstRunDone"),
            Math.Clamp(mapsZoom, AppSettings.MinZoom, AppSettings.MaxZoom),

            // Addendum B: Backgrounds is a rows page now, so its stored value is a thumbnail height under its own
            // key; the old backgroundsZoom was dropped above.
            Math.Clamp(Int(obj, "backgroundsRowZoom", 2), AppSettings.MinRowZoom, AppSettings.MaxRowZoom),
            Math.Clamp(Int(obj, "packZoom", 5), AppSettings.MinZoom, AppSettings.MaxZoom),
            Bool(obj, "welcomeDone"),
            Math.Clamp(Int(obj, "platformsZoom", 3), AppSettings.MinRowZoom, AppSettings.MaxRowZoom))
        {
            Unknown = unknown.Count == 0 ? null : unknown,
        };
```

and in `Save` replace the `["backgroundsZoom"] = settings.BackgroundsZoom,` entry with the new key and add the
platforms key after `packZoom`:

```csharp
            ["backgroundsRowZoom"] = settings.BackgroundsZoom,
            ["packZoom"] = settings.PackZoom,
            ["platformsZoom"] = settings.PlatformsZoom,
            ["welcomeDone"] = settings.WelcomeDone,
```

- [ ] **Step 8: run them and watch them pass**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~SettingsStoreTests
```

Expected: every test in the class passes, including the three new ones.

- [ ] **Step 9: the shared thumbnail cache**

Create `src\BhMaps.App\Services\ThumbnailCache.cs`:

```csharp
using System.Windows.Media;
using BhMaps.Core.Imaging;

namespace BhMaps.App.Services;

/// <summary>One decode per file for a whole page of rows (addendum B, Performance). ThumbnailProvider caches a
/// finished decode, but sixty-seven rows offering the same custom picture ask for it before the first decode has
/// finished, and each of those calls would start its own. The task is shared instead, so the file is read once
/// and every row waits on the same result. Cleared by the shell after each scan, which is the only moment a file
/// on disk may have become a different picture.</summary>
public sealed class ThumbnailCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<ImageSource?>> _loads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ThumbnailProvider _provider;

    public ThumbnailCache(ThumbnailProvider provider)
    {
        _provider = provider;
    }

    /// <summary>The picture at that path, or null when it could not be read. The token belongs to the caller
    /// alone: it is awaited through WaitAsync, so a row that scrolls away stops waiting without cancelling the
    /// decode every other row is waiting on.</summary>
    public async Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct)
    {
        if (fullPath.Length == 0)
        {
            return null;
        }

        Task<ImageSource?> load;
        lock (_gate)
        {
            if (_loads.TryGetValue(fullPath, out var existing))
            {
                load = existing;
            }
            else
            {
                load = LoadAsync(fullPath);
                _loads[fullPath] = load;
            }
        }

        return await load.WaitAsync(ct);
    }

    /// <summary>Forgets every decode. The shell calls it before each scan's page refresh.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _loads.Clear();
        }
    }

    /// <summary>Never throws: a file that vanished between the scan and the decode leaves the tile blank, which
    /// is what every other thumbnail path in the app does with the same failure.</summary>
    private async Task<ImageSource?> LoadAsync(string fullPath)
    {
        try
        {
            var mtimeTicks = await Task.Run(() => File.GetLastWriteTimeUtc(fullPath).Ticks);
            return await _provider.GetAsync(fullPath, mtimeTicks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
```

In `src\BhMaps.App\Services\AppServices.cs`, add the property beside `Thumbnails`:

```csharp
    public ThumbnailProvider Thumbnails { get; } = new();

    /// <summary>The rows pages' loader over <see cref="Thumbnails" /> (addendum B). One instance for the app, so
    /// a picture decoded for a Backgrounds row is already decoded when a Platforms row asks for it.</summary>
    public ThumbnailCache RowThumbnails { get; }
```

and set it at the end of the constructor, after `Undo`:

```csharp
        Undo = new UndoStore(appDataDir);
        RowThumbnails = new ThumbnailCache(Thumbnails);
```

In `src\BhMaps.App\ViewModels\MainViewModel.cs`, inside `RescanAsync`, clear it just before the pages are refreshed:

```csharp
        Snapshot = snapshot;

        // A file may be a different picture now, so the rows pages' decodes are forgotten before they rebuild.
        Services.RowThumbnails.Clear();

        // Maps rebuilds its cards first, because SelectedMaps reads them and a page's Refresh may ask for it.
        foreach (var page in _pages)
        {
            page.Refresh(snapshot);
        }
```

- [ ] **Step 10: the two tile additions**

In `src\BhMaps.App\ViewModels\PictureTileViewModel.cs`, add to `PictureTileViewModel`, after `ApplyCommand`:

```csharp
    /// <summary>Addendum B: a strip thumbnail's tooltip is its full caption, and the file name under it when the
    /// picture came from a pack, because two packs can caption the same slot with different files.</summary>
    public string ToolTipText =>
        PackName is null ? Title : $"{Title}\n{Path.GetFileName(FullPath)}";
```

and, beside the existing `LoadThumbnailAsync`, the rows pages' overload:

```csharp
    /// <summary>The rows pages' load (addendum B): one decode per file across every row, through the shell's
    /// shared cache rather than a decode of this tile's own.</summary>
    public async Task LoadThumbnailAsync(ThumbnailCache cache, CancellationToken ct)
    {
        if (await cache.GetAsync(FullPath, ct) is { } image && !ct.IsCancellationRequested)
        {
            Thumbnail = image;
        }
    }
```

In `src\BhMaps.App\ViewModels\PlatformSetTileViewModel.cs`, add after `PackName`:

```csharp
    /// <summary>Addendum C: a set tile's tooltip is the pack it came from, which is also its caption.</summary>
    public string ToolTipText => Pack.Name;
```

- [ ] **Step 11: the custom tile learns which map it is for [q5]**

A custom picture on a rows page is offered *for one map*, so it applies in one click like any other tile; the same
picture on the map panel's strip is the same thing. Both now use this one class, which is what gives the panel's
custom tiles the Remove and Save lines they never had. In
`src\BhMaps.App\ViewModels\CustomPictureTileViewModel.cs`, replace the constructor with two, and add the three
overrides under them:

```csharp
    public CustomPictureTileViewModel(MainViewModel shell, CustomPicture picture, string subtitle)
        : this(shell, picture, subtitle, null, null, picture.InGameSlots.Count > 0)
    {
    }

    /// <summary>The rows page's and the map panel's form: the same picture, offered for one map's slot, so the
    /// hover button applies in a click and the menu opens with "Apply to Brawlhaven".</summary>
    public CustomPictureTileViewModel(
        MainViewModel shell, CustomPicture picture, string subtitle, MapEntry? map, string? slot, bool inGame)
        : base(
            shell,
            picture.DisplayName,
            subtitle,
            // Empty only for the picture SourcePath calls impossible, and then every action on the tile reports a
            // file that is not there rather than throwing on a path nobody could resolve.
            SourcePath(picture, shell.Services.GamePath) ?? "",
            picture.PackName,
            inGame)
    {
        _picture = picture;
        Map = map;
        Slot = slot;
    }

    /// <summary>The map this tile offers the picture for, or null on a tile that names no map.</summary>
    public MapEntry? Map { get; }

    /// <summary>The background slot the apply writes, or null with no map. The apply still writes every slot the
    /// map names; this is what the editor opens on.</summary>
    public string? Slot { get; }

    public override string ApplyText => Map is null ? "Apply to..." : "Apply";

    public override bool ShowChevron => Map is null;

    public override ICommand? ApplyCommand => Map is null ? null : ApplyToMapCommand;
```

The file needs two more usings at the top, `System.Windows.Input` and `BhMaps.Core.Maps`. Add the map's own apply
beside `ApplyToTickedAsync`:

```csharp
    // The picture's own name, not the file it happens to be stored as, is what the done line reports (spec 2.2).
    [RelayCommand]
    private Task ApplyToMapAsync() =>
        Map is { } map
            ? Shell.ApplyPictureAsync(FullPath, [map], clearTicks: false, _picture.DisplayName)
            : Task.CompletedTask;
```

`RebuildMenu` opens with the map's line when there is a map, and `EditAsync` passes the slot:

```csharp
    public override void RebuildMenu(int tickedCount)
    {
        var items = new List<TileMenuCommand>();
        if (Map is { } map)
        {
            items.Add(new TileMenuCommand($"Apply to {map.DisplayName}", ApplyToMapCommand));
        }

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
    private Task EditAsync() => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(FullPath, PackName, Slot));
```

- [ ] **Step 12: the one choice builder, and the map panel built through it [q5]**

Create `src\BhMaps.App\ViewModels\MapChoices.cs`:

```csharp
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;

namespace BhMaps.App.ViewModels;

/// <summary>The two lists of choices one map has: the pictures its background slot can take, and the platform
/// sets its folder can take. Built here and nowhere else, so the map panel (spec 3.2) and a rows page (addendum
/// B and C) cannot offer a different set of choices for the same map. The order is the panel's: Default first,
/// then the packs in the Packs page order. A rows page reorders what it gets, it does not rebuild it.</summary>
public static class MapChoices
{
    /// <summary>One tile per pack holding a picture for the map's first background slot. Empty for a map with no
    /// slot, which is what a map looks like without level data (spec 3.6).</summary>
    public static IReadOnlyList<MapPictureTileViewModel> PackBackgrounds(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return [];
        }

        var slot = map.BackgroundSlots[0];
        var relative = AssetPath.Background(slot);
        var tiles = new List<MapPictureTileViewModel>();
        foreach (var choice in BackgroundChoices.For(slot, snapshot.Packs))
        {
            tiles.Add(new MapPictureTileViewModel(
                shell, map, slot, choice.Pack.Name, "", choice.File.FullPath, choice.Pack.Name,
                InGameMatch.Matches(status, relative, choice.Pack.Name)));
        }

        return tiles;
    }

    /// <summary>One tile per custom picture in the library, offered for this map's first slot. The tick is the
    /// picture's own list of in-game slots, so a picture the game is showing on this map carries the check.</summary>
    public static IReadOnlyList<CustomPictureTileViewModel> CustomBackgrounds(
        MainViewModel shell, MapEntry map, ScanSnapshot snapshot)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return [];
        }

        var slot = map.BackgroundSlots[0];
        var fileName = Path.GetFileName(AssetPath.Background(slot));
        var tiles = new List<CustomPictureTileViewModel>();
        foreach (var picture in snapshot.CustomPictures)
        {
            tiles.Add(new CustomPictureTileViewModel(
                shell, picture, "", map, slot,
                picture.InGameSlots.Contains(fileName, StringComparer.OrdinalIgnoreCase)));
        }

        return tiles;
    }

    /// <summary>One tile per pack with at least one file for this map's folder, the Default pack first. A pack
    /// that has nothing for the map is not a choice at all (addendum C).</summary>
    public static IReadOnlyList<PlatformSetTileViewModel> Platforms(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot,
        int width, int height, Action showFiles)
    {
        var tiles = new List<PlatformSetTileViewModel>();
        foreach (var pack in PlatformSetApplier.SetsFor(map.FolderName, snapshot.Packs))
        {
            tiles.Add(new PlatformSetTileViewModel(
                shell, map, pack, InGameMatch.SetInGame(pack, map.FolderName, status), width, height, showFiles));
        }

        return tiles;
    }
}
```

The panel keeps every part of spec 3.2; only where its three lists come from changes. In
`src\BhMaps.App\ViewModels\MapPanelViewModel.cs`, change the custom field and its surface:

```csharp
    private readonly ObservableCollection<MapPictureTileViewModel> _backgroundTiles = [];
    private readonly ObservableCollection<CustomPictureTileViewModel> _customTiles = [];
```

```csharp
    /// <summary>Spec 4: the custom library, under the packs, for this map's first background slot.</summary>
    public IReadOnlyList<CustomPictureTileViewModel> CustomTiles => _customTiles;
```

Replace the three fill loops in the constructor (everything from `foreach (var tile in BuildBackgroundTiles())`
down to the end of the `PlatformSetApplier.SetsFor` loop) with:

```csharp
        foreach (var tile in MapChoices.PackBackgrounds(shell, map, status, snapshot))
        {
            _backgroundTiles.Add(tile);
        }

        // Spec 4: every picture in the custom library, offered for this map the way a pack's is. A map with no
        // background slot has nowhere to put one, so it gets no strip at all.
        foreach (var tile in MapChoices.CustomBackgrounds(shell, map, snapshot))
        {
            _customTiles.Add(tile);
        }

        foreach (var tile in MapChoices.Platforms(
                     shell, map, status, snapshot, SetWidth, SetHeight, () => FilesExpanded = true))
        {
            _platformTiles.Add(tile);
        }
```

Delete the private method `BuildBackgroundTiles` entirely and the `BackgroundsFolder` constant above it: nothing
else in the file reads either. `MapsView.xaml` binds the custom strip through `PanelPictureTile`, whose every
binding (`Title`, `Thumbnail`, `IsInGame`, `ApplyText`, `ApplyCommand`, `MenuItems`) is on the shared base class,
so the markup is untouched. Leave every other member of the class alone.

- [ ] **Step 13: the row [q4]**

There is no tick on a row and no card behind it: ticking is the Maps page's (addendum B, q4), so a row holds the
map and the words the page worked out for it, and nothing that has to stay in step with another object.

Create `src\BhMaps.App\ViewModels\MapRowViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>The last tile of a folded strip: "Custom (11)" for the pictures the row keeps folded (addendum B,
/// q3). The "+N" tile for choices the row had no width for is drawn by the view from StripPanel.Overflow,
/// because only the panel that measured them knows the number.</summary>
public sealed partial class RowMoreViewModel : ObservableObject
{
    public RowMoreViewModel(string text, MapRowViewModel row)
    {
        Text = text;
        Row = row;
    }

    public string Text { get; }

    public MapRowViewModel Row { get; }

    [RelayCommand]
    private void Unfold() => Row.IsUnfolded = true;
}

/// <summary>One row of the Backgrounds or Platforms page (addendum B and C): one map, the tag that says what it
/// is showing, and every choice that map has on that page. No tick box: a row is for clicking the choice you
/// want, and the ticked set the tile menus write to is made on Maps (q4).</summary>
public sealed partial class MapRowViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _alwaysShown;
    private readonly IReadOnlyList<object> _foldedAway;
    private readonly Func<PlatformSetTileViewModel, CancellationToken, Task>? _composeSet;

    /// <summary>True once the row has been on screen and asked for its pictures, so scrolling back to it costs
    /// nothing (addendum B, Performance).</summary>
    private bool _loadStarted;

    public MapRowViewModel(
        MapEntry map,
        string tagText,
        bool isMissing,
        bool isChanged,
        bool showsCustomPicture,
        string haystack,
        IReadOnlyList<object> alwaysShown,
        IReadOnlyList<object> foldedAway,
        string foldedLabel,
        IReadOnlyList<PictureTileViewModel> pictures,
        IReadOnlyList<PlatformSetTileViewModel> sets,
        Func<PlatformSetTileViewModel, CancellationToken, Task>? composeSet)
    {
        Map = map;
        TagText = tagText;
        IsMissing = isMissing;
        IsChanged = isChanged;
        ShowsCustomPicture = showsCustomPicture;
        Haystack = haystack;
        _alwaysShown = alwaysShown;
        _foldedAway = foldedAway;
        FoldedLabel = foldedLabel;
        Pictures = pictures;
        Sets = sets;
        _composeSet = composeSet;
        Choices = [];
        Rebuild();
    }

    public MapEntry Map { get; }

    public string FolderName => Map.FolderName;

    public string DisplayName => Map.DisplayName;

    /// <summary>The pack name, the custom picture's name, "Missing", or empty for Default. The page decides it:
    /// Backgrounds takes the Maps card's own tag, Platforms measures the map's folder instead.</summary>
    public string TagText { get; }

    public bool IsMissing { get; }

    public bool ShowTag => TagText.Length > 0;

    /// <summary>Whether the Changed chip keeps this row.</summary>
    public bool IsChanged { get; }

    /// <summary>Whether the Backgrounds page's Custom chip keeps this row. Always false on Platforms, which has
    /// no such chip (addendum C).</summary>
    public bool ShowsCustomPicture { get; }

    /// <summary>Everything the search box matches against: the map's name, then every choice's caption and file
    /// name, one per line (addendum B).</summary>
    public string Haystack { get; }

    /// <summary>"Custom (11)", or empty when the row folds nothing away.</summary>
    public string FoldedLabel { get; }

    /// <summary>Every picture tile in the row, folded or not, so one load reaches all of them.</summary>
    public IReadOnlyList<PictureTileViewModel> Pictures { get; }

    public IReadOnlyList<PlatformSetTileViewModel> Sets { get; }

    /// <summary>What the strip draws right now: the tiles, plus the "Custom (N)" tile while the row is folded.
    /// Typed as object because a Backgrounds strip holds two tile types and that last one; the view picks a
    /// template by type, which is what an implicit DataTemplate in the page's resources is for.</summary>
    public ObservableCollection<object> Choices { get; }

    /// <summary>Whether the row is showing every choice it has, on as many lines as that needs (addendum B, q2
    /// and q3). The page folds every row when the page is left.</summary>
    [ObservableProperty]
    public partial bool IsUnfolded { get; set; }

    /// <summary>Every tile's menu names the ticked maps, so the count changing rewords every one of them.</summary>
    public void RebuildMenus(int tickedCount)
    {
        foreach (var tile in Pictures)
        {
            tile.RebuildMenu(tickedCount);
        }

        foreach (var tile in Sets)
        {
            tile.RebuildMenu(tickedCount);
        }
    }

    /// <summary>Puts the row back to one line (addendum B: unfolded rows fold back when the page is left).</summary>
    public void Fold() => IsUnfolded = false;

    /// <summary>What a click on the row body does: an unfolded row folds again, and a folded one stays folded,
    /// because on a folded row the click was aimed at a thumbnail.</summary>
    [RelayCommand]
    private void ToggleFold()
    {
        if (IsUnfolded)
        {
            IsUnfolded = false;
        }
    }

    /// <summary>Fills the row's pictures, once, when the row is first realised. Fire and forget from the page:
    /// every file failure is already a blank tile rather than an error, so the only thing left to stop for is
    /// cancellation.</summary>
    public async Task LoadAsync(AppServices services, CancellationToken ct)
    {
        if (_loadStarted)
        {
            return;
        }

        _loadStarted = true;
        try
        {
            foreach (var tile in Pictures)
            {
                ct.ThrowIfCancellationRequested();
                await tile.LoadThumbnailAsync(services.RowThumbnails, ct);
            }

            if (_composeSet is { } compose)
            {
                foreach (var tile in Sets)
                {
                    ct.ThrowIfCancellationRequested();
                    await compose(tile, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The page was rebuilt by a scan, so what was still loading is for a row nothing shows any more.
        }
    }

    partial void OnIsUnfoldedChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        Choices.Clear();
        foreach (var choice in _alwaysShown)
        {
            Choices.Add(choice);
        }

        if (IsUnfolded)
        {
            foreach (var choice in _foldedAway)
            {
                Choices.Add(choice);
            }
        }
        else if (_foldedAway.Count > 0)
        {
            Choices.Add(new RowMoreViewModel(FoldedLabel, this));
        }
    }
}
```

- [ ] **Step 14: the strip panel [q2]**

Create `src\BhMaps.App\Views\Controls\StripPanel.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>A row's strip of choices (addendum B): one line while the row is folded, with everything past the
/// right edge left undrawn and counted in <see cref="Overflow" /> for the row's "+N" tile, and as many lines as
/// it needs once the row is unfolded. A panel of its own rather than a WrapPanel under a clip, because only the
/// panel that measured the tiles can say how many did not fit, and the "+N" tile has to name that number.</summary>
public sealed class StripPanel : Panel
{
    /// <summary>Room kept at the right end of a folded line for the row's "+N" tile, so the tile never lands on
    /// top of the last thumbnail. 52 px is "+58" in the theme's 11 px action font plus the TileAction style's
    /// 8,3 padding and its 4 px left margin.</summary>
    public const double MoreWidth = 52;

    public static readonly DependencyProperty IsUnfoldedProperty =
        DependencyProperty.Register(
            nameof(IsUnfolded),
            typeof(bool),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty OverflowProperty =
        DependencyProperty.Register(nameof(Overflow), typeof(int), typeof(StripPanel), new PropertyMetadata(0));

    public bool IsUnfolded
    {
        get => (bool)GetValue(IsUnfoldedProperty);
        set => SetValue(IsUnfoldedProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>How many children the last measure left undrawn. Always zero while the row is unfolded, which is
    /// what takes the "+N" tile off the row.</summary>
    public int Overflow
    {
        get => (int)GetValue(OverflowProperty);
        private set => SetValue(OverflowProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        foreach (UIElement child in InternalChildren)
        {
            // Unconstrained, because every tile in a strip is a fixed size the zoom decided (addendum B, q1).
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        if (IsUnfolded)
        {
            Overflow = 0;
            return Wrap(width, arrange: false);
        }

        // Two passes: the first asks whether every tile fits, the second makes room for the "+N" tile only when
        // the answer was no. Reserving that room unconditionally would push a tile off a line it fits on.
        var shown = Fit(width);
        if (shown < InternalChildren.Count)
        {
            shown = Fit(Math.Max(0, width - MoreWidth - Spacing));
        }

        Overflow = InternalChildren.Count - shown;
        double used = 0;
        double height = 0;
        for (var i = 0; i < shown; i++)
        {
            var size = InternalChildren[i].DesiredSize;
            used += size.Width + (i > 0 ? Spacing : 0);
            height = Math.Max(height, size.Height);
        }

        return new Size(Math.Min(used, width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (IsUnfolded)
        {
            Wrap(finalSize.Width, arrange: true);
            return finalSize;
        }

        var shown = InternalChildren.Count - Overflow;
        double x = 0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            if (i >= shown)
            {
                // Arranged at nothing rather than collapsed: changing Visibility during arrange starts another
                // layout pass, and a child of zero size is neither drawn nor hit-tested nor tabbed to.
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var size = child.DesiredSize;
            child.Arrange(new Rect(x, 0, size.Width, size.Height));
            x += size.Width + Spacing;
        }

        return finalSize;
    }

    /// <summary>How many children fit on one line of that width. Always at least one, so a window narrower than
    /// a single thumbnail still shows the choice the game is on.</summary>
    private int Fit(double width)
    {
        double used = 0;
        var count = 0;
        foreach (UIElement child in InternalChildren)
        {
            var next = used + (count > 0 ? Spacing : 0) + child.DesiredSize.Width;
            if (count > 0 && next > width)
            {
                break;
            }

            used = next;
            count++;
        }

        return count;
    }

    /// <summary>Lines of children, wrapping at the width. Measures when <paramref name="arrange" /> is false and
    /// places them when it is true, so the two passes cannot disagree about where a line breaks.</summary>
    private Size Wrap(double width, bool arrange)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double widest = 0;
        foreach (UIElement child in InternalChildren)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
            }

            if (arrange)
            {
                child.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + Spacing;
            widest = Math.Max(widest, x - Spacing);
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(Math.Min(widest, width), y + lineHeight);
    }
}
```

- [ ] **Step 15: build, test, format**

```powershell
dotnet build BhMaps.slnx -c Debug
dotnet test BhMaps.slnx
dotnet format BhMaps.slnx
```

Expected: 0 warnings, 0 errors; every test passes, the count is the previous count plus 6 (4 in
`BackgroundChoicesTests`, 2 net new in `SettingsStoreTests`); the formatter rewrites nothing, or rerun the build
and the tests after it does.

Then launch the dev tree once and check the map panel, which is the only page this task changes behaviour on:

```powershell
dotnet run --project src\BhMaps.App -- --game "<DEV>\game\mapArt" --library "<DEV>\lib" --appdata "<DEV>\appdata"
```

where `<DEV>` is
`C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`.
Open a map's panel: the Background tiles and the custom strip draw as before, and a custom tile's menu now reads
"Apply to Brawlhaven", "Apply to all maps", "Edit", "Show in folder", "Remove from library". Capture it to
`<SHOTS>\t29-panel-custom-menu.png`.

- [ ] **Step 16: commit**

```powershell
git add src/BhMaps.Core/Operations/BackgroundChoices.cs src/BhMaps.Core/Settings/AppSettings.cs src/BhMaps.Core/Settings/SettingsStore.cs tests/BhMaps.Core.Tests/BackgroundChoicesTests.cs tests/BhMaps.Core.Tests/SettingsStoreTests.cs src/BhMaps.App/Services/ThumbnailCache.cs src/BhMaps.App/Services/AppServices.cs src/BhMaps.App/ViewModels/MainViewModel.cs src/BhMaps.App/ViewModels/MapChoices.cs src/BhMaps.App/ViewModels/MapRowViewModel.cs src/BhMaps.App/ViewModels/PictureTileViewModel.cs src/BhMaps.App/ViewModels/CustomPictureTileViewModel.cs src/BhMaps.App/ViewModels/PlatformSetTileViewModel.cs src/BhMaps.App/ViewModels/MapPanelViewModel.cs src/BhMaps.App/Views/Controls/StripPanel.cs
git commit -m "feat(rows): one choice builder, the row model and the row zoom keys" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

### Task 30: the Backgrounds rows page, and the Maps card that no longer grows for its tag

**Files:**
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (add `ChipRow`, `RowBorder`, `RowChoiceTileButton`, `RowMoreTileButton`, the three tile templates and the `MapRow` template)
- Modify: `src\BhMaps.App\Views\Controls\TileMenus.cs` (the `OpensMenu` attached property)
- Create: `src\BhMaps.App\Views\Controls\RowRealiser.cs`
- Create: `src\BhMaps.App\Views\Controls\RowKeys.cs`
- Modify: `src\BhMaps.App\Views\Controls\ZoomSlider.xaml` and `.xaml.cs` (the `Label` property)
- Create: `src\BhMaps.App\ViewModels\Pages\RowsPageViewModel.cs`
- Rewrite: `src\BhMaps.App\ViewModels\Pages\BackgroundsViewModel.cs`
- Rewrite: `src\BhMaps.App\Views\Pages\BackgroundsView.xaml` and `.xaml.cs`
- Modify: `src\BhMaps.App\Views\Pages\MapsView.xaml` (delete the local `ChipRow` copy only; the card's tag fix is owner change O6, already on the branch)

**Interfaces:**

Consumes, from Task 29 and from HEAD:

```csharp
// Task 29
public sealed partial class MapRowViewModel : ObservableObject
{
    public MapRowViewModel(
        MapEntry map, string tagText, bool isMissing, bool isChanged, bool showsCustomPicture, string haystack,
        IReadOnlyList<object> alwaysShown, IReadOnlyList<object> foldedAway, string foldedLabel,
        IReadOnlyList<PictureTileViewModel> pictures, IReadOnlyList<PlatformSetTileViewModel> sets,
        Func<PlatformSetTileViewModel, CancellationToken, Task>? composeSet);
    public MapEntry Map { get; }
    public string FolderName { get; }
    public string DisplayName { get; }
    public string TagText { get; }
    public bool IsMissing { get; }
    public bool ShowTag { get; }
    public bool IsChanged { get; }
    public bool ShowsCustomPicture { get; }
    public string Haystack { get; }
    public IReadOnlyList<PictureTileViewModel> Pictures { get; }
    public IReadOnlyList<PlatformSetTileViewModel> Sets { get; }
    public ObservableCollection<object> Choices { get; }
    public partial bool IsUnfolded { get; set; }
    public IRelayCommand ToggleFoldCommand { get; }
    public void Fold();
    public void RebuildMenus(int tickedCount);
    public Task LoadAsync(AppServices services, CancellationToken ct);
}
public sealed partial class RowMoreViewModel : ObservableObject
{
    public string Text { get; }
    public IRelayCommand UnfoldCommand { get; }
}
public sealed class StripPanel : Panel { public bool IsUnfolded { get; set; } public double Spacing { get; set; } public int Overflow { get; } }
public static class MapChoices
{
    public static IReadOnlyList<MapPictureTileViewModel> PackBackgrounds(MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot);
    public static IReadOnlyList<CustomPictureTileViewModel> CustomBackgrounds(MainViewModel shell, MapEntry map, ScanSnapshot snapshot);
}
public sealed class ThumbnailCache { public Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct); public void Clear(); }
// AppServices.RowThumbnails, AppSettings.MinRowZoom = 1, AppSettings.MaxRowZoom = 5, AppSettings.BackgroundsZoom default 2

// HEAD
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(MainViewModel shell);
    protected MainViewModel Shell { get; }
    public abstract string Title { get; }
    public abstract void Refresh(ScanSnapshot snapshot);
}
public abstract partial class PictureTileViewModel : ObservableObject
{
    public string Title { get; }
    public string FullPath { get; }
    public string? PackName { get; }
    public bool IsInGame { get; }
    public string ToolTipText { get; }
    public virtual ICommand? ApplyCommand { get; }
    public partial ImageSource? Thumbnail { get; set; }
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; }
    public void RebuildMenu(int tickedCount);
}
public partial class MainViewModel : ObservableObject
{
    public AppServices Services { get; }
    public ScanSnapshot? Snapshot { get; }
    public MapsViewModel Maps { get; }
    public int SelectedMapCount { get; }
    public bool CanWrite { get; }
    public Task OpenAddPicturesAsync(AddPicturesTarget target);
}
public partial class MapsViewModel : PageViewModel { public IReadOnlyList<MapCardViewModel> AllCards { get; } }
public partial class MapCardViewModel : ObservableObject
{
    public string FolderName { get; }
    public string TagText { get; }
    public bool IsMissing { get; }
}
public static class TileMenus
{
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e);
    public static void OpenFor(object sender);
}
public sealed record AddPicturesTarget(AddPicturesTargetKind Kind, MapEntry? Map, IReadOnlyList<MapEntry>? Maps);
public enum AddPicturesTargetKind { None, Map, Ticked }
```

Produces, which Task 31 relies on:

```csharp
// ViewModels\Pages\RowsPageViewModel.cs
public abstract partial class RowsPageViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinRowZoom;
    public const int MaxZoom = AppSettings.MaxRowZoom;
    protected const string AllChip = "All";
    protected const string ChangedChip = "Changed";
    protected const string CustomChip = "Custom";
    protected RowsPageViewModel(MainViewModel shell, int storedZoom);
    protected ScanSnapshot? Snapshot { get; }
    public ObservableCollection<MapRowViewModel> Rows { get; }
    public IReadOnlyList<string> Chips { get; }
    public partial string SearchText { get; set; }
    public partial string SelectedChip { get; set; }
    public partial int Zoom { get; set; }
    public double ThumbHeight { get; }
    public double ThumbWidth { get; }
    public bool ShowTileApply { get; }
    public bool ShowClearSearch { get; }
    public bool ShowFirstRunLine { get; }
    public string EmptyText { get; }
    public abstract string SearchPlaceholder { get; }
    public abstract string ZoomLabel { get; }
    protected abstract bool HasCustomChip { get; }
    protected abstract void SaveZoom(int value);
    protected abstract MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot);
    public void RealiseRow(MapRowViewModel row);
    public void FoldAll();
    [RelayCommand] private void ClearSearch();
    public IRelayCommand ClearSearchCommand { get; }
}

// Views\Controls\TileMenus.cs
public static void SetOpensMenu(DependencyObject element, bool value);
public static bool GetOpensMenu(DependencyObject element);

// Views\Controls\RowRealiser.cs
public static void SetRealise(DependencyObject element, bool value);
public static bool GetRealise(DependencyObject element);

// Views\Controls\RowKeys.cs
public static void OnPreviewKeyDown(object sender, KeyEventArgs e);

// Views\Controls\ZoomSlider.xaml.cs
public string Label { get; set; }   // default "Columns"

// Theme\Controls.xaml resource keys
// ChipRow (ListBoxItem), RowBorder (Border), RowChoiceTileButton (Button), RowMoreTileButton (Button),
// RowChoiceTile (DataTemplate), MapRow (DataTemplate),
// implicit DataTemplates for MapPictureTileViewModel, CustomPictureTileViewModel and RowMoreViewModel
```

- [ ] **Step 1: the two attached behaviours and the shared key handler**

Add to the end of `src\BhMaps.App\Views\Controls\TileMenus.cs`, inside the class, so a tile's menu button can live
in a theme template that has no code-behind:

```csharp
    /// <summary>Set on a button inside a tile template: clicking it opens the menu of the first element above it
    /// that owns one. An attached property rather than a Click handler, because the row templates live in
    /// Controls.xaml, which is a resource dictionary and has no code-behind to hold one.</summary>
    public static readonly DependencyProperty OpensMenuProperty =
        DependencyProperty.RegisterAttached(
            "OpensMenu", typeof(bool), typeof(TileMenus), new PropertyMetadata(false, OnOpensMenuChanged));

    public static void SetOpensMenu(DependencyObject element, bool value) =>
        element.SetValue(OpensMenuProperty, value);

    public static bool GetOpensMenu(DependencyObject element) => (bool)element.GetValue(OpensMenuProperty);

    private static void OnOpensMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.ButtonBase button)
        {
            return;
        }

        button.Click -= OnMenuButtonClick;
        if (e.NewValue is true)
        {
            button.Click += OnMenuButtonClick;
        }
    }

    /// <summary>Handled, so the click does not also reach the tile button under it and apply the picture.</summary>
    private static void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        OpenFor(sender);
        e.Handled = true;
    }
```

Create `src\BhMaps.App\Views\Controls\RowRealiser.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Controls;

/// <summary>Tells a rows page that one of its rows has come on screen, so the row reads its pictures then and
/// not before (addendum B, Performance). An attached property rather than a Loaded handler in each page's
/// code-behind, because the row template is shared by both rows pages and lives in the theme.</summary>
public static class RowRealiser
{
    public static readonly DependencyProperty RealiseProperty =
        DependencyProperty.RegisterAttached(
            "Realise", typeof(bool), typeof(RowRealiser), new PropertyMetadata(false, OnRealiseChanged));

    public static void SetRealise(DependencyObject element, bool value) => element.SetValue(RealiseProperty, value);

    public static bool GetRealise(DependencyObject element) => (bool)element.GetValue(RealiseProperty);

    private static void OnRealiseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;
        if (e.NewValue is true)
        {
            element.Loaded += OnLoaded;
        }
    }

    /// <summary>Loaded fires again every time a recycled container comes back on screen; the row itself ignores
    /// every call after the first, so scrolling up and down costs one read of each file and no more.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MapRowViewModel row } element && FindPage(element) is { } page)
        {
            page.RealiseRow(row);
        }
    }

    private static RowsPageViewModel? FindPage(DependencyObject start)
    {
        for (DependencyObject? d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { DataContext: RowsPageViewModel page })
            {
                return page;
            }
        }

        return null;
    }
}
```

Create `src\BhMaps.App\Views\Controls\RowKeys.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views.Controls;

/// <summary>The keyboard a rows page answers (addendum B): the menu key and Shift+F10 on a focused tile, and
/// Escape in the search box, which clears what was typed and stops there. Up, Down, Left and Right are WPF's own
/// directional navigation, which the page turns on with KeyboardNavigation.DirectionalNavigation, and Enter on a
/// focused tile is the Button's own. Shared by both rows pages, so neither can answer a key differently.</summary>
public static class RowKeys
{
    public static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        TileMenus.OnPreviewKeyDown(sender, e);
        if (e.Handled || e.Key != Key.Escape)
        {
            return;
        }

        // By Tag, not by name: the box sits inside the PageHeader user control's own name scope.
        if (sender is FrameworkElement { DataContext: RowsPageViewModel page }
            && e.OriginalSource is TextBox box
            && Equals(box.Tag, "SearchBox")
            && page.SearchText.Length > 0)
        {
            page.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
```

- [ ] **Step 2: the shared rows page view model [q1] [q4]**

Create `src\BhMaps.App\ViewModels\Pages\RowsPageViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>What the Backgrounds and Platforms pages have in common (addendum B and C): one row per map, a chip
/// row, a search box, a zoom that sets the thumbnail height, and the rule that a row reads its pictures when it
/// comes on screen. No ticks: ticking is the Maps page's (q4), and this page reads the ticked count only so a
/// tile menu can say "Apply to the 3 selected maps".</summary>
public abstract partial class RowsPageViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinRowZoom;
    public const int MaxZoom = AppSettings.MaxRowZoom;

    protected const string AllChip = "All";
    protected const string ChangedChip = "Changed";
    protected const string CustomChip = "Custom";

    /// <summary>Every row the last scan produced. Rows is this list under the chip and the search.</summary>
    private readonly List<MapRowViewModel> _all = [];

    private readonly ObservableCollection<string> _chips = [];

    /// <summary>The sets behind the set chips, so a chip label maps back to the set name a map is measured against.</summary>
    private readonly List<UiSet> _uiSets = [];

    private CancellationTokenSource? _loads;

    protected RowsPageViewModel(MainViewModel shell, int storedZoom)
        : base(shell)
    {
        Rows = [];
        _chips.Add(AllChip);
        _chips.Add(ChangedChip);

        // After the collections, because setting them runs the change hooks that filter them.
        SearchText = "";
        SelectedChip = AllChip;

        // A stored zoom from another version, or a hand-edited one, is clamped rather than trusted. The value is
        // handed in rather than read here, so the constructor calls nothing the subclass has overridden.
        Zoom = Math.Clamp(storedZoom, MinZoom, MaxZoom);

        // A tile's menu names the ticked count, which is the shell's. The page lives as long as the shell, so
        // there is nothing to unsubscribe from.
        shell.PropertyChanged += OnShellChanged;
    }

    /// <summary>The last scan, or null before the first one.</summary>
    protected ScanSnapshot? Snapshot { get; private set; }

    /// <summary>The rows the chip and the search leave visible, in map order.</summary>
    public ObservableCollection<MapRowViewModel> Rows { get; }

    /// <summary>"All", the UI set labels, "Changed", and "Custom" on the page that has it. Without level data the
    /// set chips are gone entirely (spec 3.6).</summary>
    public IReadOnlyList<string> Chips => _chips;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedChip { get; set; }

    /// <summary>MinZoom to MaxZoom, a thumbnail height rather than a column count (addendum B, q1).</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    /// <summary>Addendum B, q1 answered dense: 48, 56, 72, 96, 128 px.</summary>
    public double ThumbHeight => Zoom switch
    {
        1 => 48,
        2 => 56,
        3 => 72,
        4 => 96,
        _ => 128,
    };

    /// <summary>16:9, rounded to a whole pixel so a strip of them measures the same on every row.</summary>
    public double ThumbWidth => Math.Round(ThumbHeight * 16 / 9);

    /// <summary>False at the two smallest steps, where a thumbnail is 85 or 100 px wide and the hover row has
    /// room for the dots button alone. A click on the tile still applies, so nothing is lost but the label.</summary>
    public bool ShowTileApply => Zoom >= 3;

    /// <summary>The words in the search box while it is empty, and its automation name.</summary>
    public abstract string SearchPlaceholder { get; }

    /// <summary>The zoom slider's automation name: a rows page sizes thumbnails, it does not count columns.</summary>
    public virtual string ZoomLabel => "Thumbnail size";

    /// <summary>Whether the chip row carries "Custom" (addendum B; Platforms has no such chip).</summary>
    protected abstract bool HasCustomChip { get; }

    public bool ShowClearSearch => SearchText.Length > 0;

    /// <summary>Spec 3.1's first-run line, on this page too: nothing in the library but the Default pack, and no
    /// custom picture either.</summary>
    public bool ShowFirstRunLine =>
        Snapshot is { } s
        && !s.Packs.Any(p => !p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase))
        && s.CustomPictures.Count == 0;

    /// <summary>Why the list is empty, in one line (addendum B and C).</summary>
    public string EmptyText
    {
        get
        {
            if (SearchText.Length > 0)
            {
                return NoResultsText;
            }

            // RebuildChips can leave the chip ListBox between a removal and an insertion, and the ListBox pushes
            // its lost selection back through this two-way binding, so the getter can run with nothing chosen.
            return SelectedChip switch
            {
                ChangedChip => "No map is changed. Every map matches the Default pack.",
                CustomChip => "No map is showing a custom picture.",
                null or "" or AllChip => "No map to show.",
                _ => $"No {SelectedChip.ToLowerInvariant()} map to show.",
            };
        }
    }

    /// <summary>The line the search's own empty state uses, which quotes what was typed.</summary>
    protected abstract string NoResultsText { get; }

    /// <summary>Writes the page's own zoom key. Called only from the change hook, never from the constructor.</summary>
    protected abstract void SaveZoom(int value);

    /// <summary>One row for one map. The page decides the tag, the chips' answers, the search haystack and the
    /// order of the strip; <see cref="MapChoices" /> decides what is in it.</summary>
    protected abstract MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot);

    public override void Refresh(ScanSnapshot snapshot)
    {
        Snapshot = snapshot;

        // Every row is replaced, so the loads still in flight are for objects nothing shows any more.
        _loads?.Cancel();
        _loads?.Dispose();
        _loads = new CancellationTokenSource();

        _all.Clear();

        // The Maps page rebuilt its cards first (it is first in the shell's page list), so its tag for each map
        // is the one this page shows rather than a second copy of the rule that works it out.
        foreach (var card in Shell.Maps.AllCards)
        {
            snapshot.MapStatuses.TryGetValue(card.FolderName, out var status);
            _all.Add(BuildRow(card, status, snapshot));
        }

        RebuildChips(snapshot.Catalog);
        ApplyFilter();
        RebuildMenus();
        OnPropertyChanged(nameof(ShowFirstRunLine));
    }

    /// <summary>A row has come on screen. Fire and forget: the row turns every file failure into a blank tile, so
    /// the only thing left to stop for is the cancellation a new scan brings.</summary>
    public void RealiseRow(MapRowViewModel row)
    {
        if (_loads is { } loads)
        {
            _ = row.LoadAsync(Shell.Services, loads.Token);
        }
    }

    /// <summary>Addendum B: unfolded rows fold back when the page is left. The shell calls it on the page it is
    /// leaving.</summary>
    public void FoldAll()
    {
        foreach (var row in _all)
        {
            row.Fold();
        }
    }

    /// <summary>The no-results state's way back (addendum B). RowKeys runs it for Escape in the box.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowClearSearch));
        ApplyFilter();
    }

    partial void OnSelectedChipChanged(string value) => ApplyFilter();

    partial void OnZoomChanged(int value)
    {
        SaveZoom(value);
        OnPropertyChanged(nameof(ThumbHeight));
        OnPropertyChanged(nameof(ThumbWidth));
        OnPropertyChanged(nameof(ShowTileApply));
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Every open tile menu names the ticked maps, so the count is what rewords them (addendum B, q4).
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            RebuildMenus();
        }
    }

    private void RebuildMenus()
    {
        foreach (var row in _all)
        {
            row.RebuildMenus(Shell.SelectedMapCount);
        }
    }

    /// <summary>Synced in place rather than cleared and refilled: clearing the chip ListBox's items pushes a null
    /// SelectedChip back through the two-way binding, and the filter answering it runs against no chip at all.</summary>
    private void RebuildChips(MapCatalog catalog)
    {
        List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label), ChangedChip];
        if (HasCustomChip)
        {
            wanted.Add(CustomChip);
        }

        if (_chips.SequenceEqual(wanted))
        {
            return;
        }

        _uiSets.Clear();
        _uiSets.AddRange(catalog.UiSets);
        var chosen = SelectedChip;

        for (var i = _chips.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(_chips[i]))
            {
                _chips.RemoveAt(i);
            }
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= _chips.Count || _chips[i] != wanted[i])
            {
                _chips.Insert(i, wanted[i]);
            }
        }

        // A set chip that has just gone, because the level data went with it, would otherwise leave the list
        // filtered by a chip the row no longer offers.
        SelectedChip = wanted.Contains(chosen) ? chosen : AllChip;
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        foreach (var row in _all.Where(Matches))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>The chip filter, then the search box on top of it. The search reads the row's haystack, which is
    /// the map's name and every choice's caption and file name (addendum B).</summary>
    private bool Matches(MapRowViewModel row)
    {
        var search = SearchText;
        if (search.Length > 0 && !row.Haystack.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedChip switch
        {
            AllChip => true,
            ChangedChip => row.IsChanged,
            CustomChip => row.ShowsCustomPicture,
            null or "" => true,
            _ => _uiSets.FirstOrDefault(s => s.Label == SelectedChip) is { } set
                && row.Map.Sets.Contains(set.Name, StringComparer.OrdinalIgnoreCase),
        };
    }
}
```

- [ ] **Step 3: the Backgrounds page's view model [q2] [q3]**

Replace the whole of `src\BhMaps.App\ViewModels\Pages\BackgroundsViewModel.cs` with:

```csharp
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Addendum B: one row per map, and the row is that map's answer to "what can my background be, and
/// which is it now". The pack sections and the custom shelf of spec 4 are gone: a pack's contents are the Packs
/// page's business.</summary>
public partial class BackgroundsViewModel : RowsPageViewModel
{
    public BackgroundsViewModel(MainViewModel shell)
        : base(shell, shell.Services.Settings.BackgroundsZoom)
    {
    }

    public override string Title => "Backgrounds";

    public override string SearchPlaceholder => "Search maps and pictures";

    protected override bool HasCustomChip => true;

    protected override string NoResultsText => $"No map or picture matches '{SearchText}'.";

    /// <summary>Spec 4's one header action. The page adds to the library and names no maps, which is what the
    /// None kind says (spec 7.1).</summary>
    [RelayCommand]
    private Task AddPicturesAsync() =>
        Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.None, null, null));

    protected override void SaveZoom(int value)
    {
        if (Shell.Services.Settings.BackgroundsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsZoom = value });
        }
    }

    /// <summary>Addendum B's order: the in-game choice first with the check, then Default, then each pack that
    /// has a picture for this map's slot in the Packs page order, then the custom pictures, which are folded
    /// behind one "Custom (N)" tile (q3). A custom picture the game is showing is unfolded and first, because it
    /// is the in-game choice.</summary>
    protected override MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot)
    {
        var map = card.Map;
        var packs = MapChoices.PackBackgrounds(Shell, map, status, snapshot);
        var customs = MapChoices.CustomBackgrounds(Shell, map, snapshot);

        List<PictureTileViewModel> pictures = [.. packs, .. customs];
        List<object> alwaysShown = [.. pictures.Where(t => t.IsInGame), .. packs.Where(t => !t.IsInGame)];
        List<object> foldedAway = [.. customs.Where(t => !t.IsInGame)];
        var foldedLabel = foldedAway.Count == 0 ? "" : $"Custom ({foldedAway.Count})";

        return new MapRowViewModel(
            map,
            card.TagText,
            card.IsMissing,
            IsChanged(status),
            ShowsCustomPicture(map, status),
            Haystack(map, pictures),
            alwaysShown,
            foldedAway,
            foldedLabel,
            pictures,
            [],
            composeSet: null);
    }

    /// <summary>Spec 3.1's Changed chip, on rows: everything that has a tag other than Missing.</summary>
    private static bool IsChanged(MapStatus? status) => status?.State is MapState.Packs or MapState.Custom;

    /// <summary>Addendum B's Custom chip: the game is showing a custom picture in this map's first slot. The
    /// slot, not the whole map, because a pack's platform art must not put a map on this chip.</summary>
    private static bool ShowsCustomPicture(MapEntry map, MapStatus? status) =>
        map.BackgroundSlots.Count > 0
        && InGameMatch.File(status, AssetPath.Background(map.BackgroundSlots[0])) is { State: MapFileState.Custom };

    /// <summary>Addendum B: the map's name, then every thumbnail's caption and file name, one per line, so a
    /// search for a pack name or a file name keeps the rows that offer it.</summary>
    private static string Haystack(MapEntry map, IReadOnlyList<PictureTileViewModel> pictures) =>
        string.Join(
            '\n',
            [map.DisplayName, .. pictures.Select(t => t.Title), .. pictures.Select(t => Path.GetFileName(t.FullPath))]);
}
```

- [ ] **Step 4: the zoom slider says what it sizes**

In `src\BhMaps.App\Views\Controls\ZoomSlider.xaml.cs`, beside `MaximumProperty`:

```csharp
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(ZoomSlider), new PropertyMetadata("Columns"));
```

```csharp
    /// <summary>What the slider sizes, for the automation tree: a grid counts columns, a rows page sizes
    /// thumbnails, and a screen reader must not read the second as the first.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
```

In `src\BhMaps.App\Views\Controls\ZoomSlider.xaml`, the inner slider's automation name becomes the property:

```xml
    <Slider AutomationProperties.Name="{Binding Label, ElementName=Root}"
            Maximum="{Binding Maximum, ElementName=Root}"
            Minimum="{Binding Minimum, ElementName=Root}"
            Style="{StaticResource ZoomSliderStyle}"
            Value="{Binding Value, ElementName=Root, Mode=TwoWay}" />
```

- [ ] **Step 5: the theme's row styles and templates [q1] [q2]**

In `src\BhMaps.App\Theme\Controls.xaml`, add `xmlns:vm="clr-namespace:BhMaps.App.ViewModels"` to the
`ResourceDictionary` element, then add these at the end, before the closing tag.

The chip row, moved here from `MapsView.xaml` word for word so three pages draw one chip:

```xml
  <!-- A chip is a ListBoxItem so the list owns which one is on, and ChipToggle above draws it. The toggle is
       chrome only: hit testing and focus belong to the row, which is what Tab and the arrow keys walk. Moved out
       of MapsView.xaml when the two rows pages grew chip rows of their own. -->
  <Style x:Key="ChipRow" TargetType="ListBoxItem">
    <Setter Property="Margin" Value="0,0,8,0" />

    <!-- Owner change O2: the hand the top bar's tabs show. It belongs on the row rather than on ChipToggle,
         which carries one already but is IsHitTestVisible=False, so the cursor under the pointer is the row's. -->
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <!-- Transparent, not unset: a Grid with no Background is not hit-testable, and both children below are
               IsHitTestVisible=False, so without this the ListBoxItem never sees a mouse click (spec 9). -->
          <Grid Background="Transparent">
            <ToggleButton x:Name="Chip"
                          Content="{TemplateBinding Content}"
                          Focusable="False"
                          IsChecked="{Binding IsSelected, RelativeSource={RelativeSource TemplatedParent}}"
                          IsHitTestVisible="False"
                          Style="{StaticResource ChipToggle}" />
            <Border x:Name="Ring"
                    BorderBrush="{StaticResource TextBrush}"
                    BorderThickness="1"
                    CornerRadius="14"
                    IsHitTestVisible="False"
                    Opacity="0" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Chip" Property="Background" Value="{StaticResource Surface2Brush}" />
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Ring" Property="Opacity" Value="0.6" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Addendum B: Surface, hairline, Radius 6, padding 6 12. Tighter than a card, because a page of them is
       meant to be scanned. -->
  <Style x:Key="RowBorder" TargetType="Border" BasedOn="{StaticResource CardBorder}">
    <Setter Property="Padding" Value="12,6" />
    <Setter Property="Margin" Value="0,0,0,8" />
  </Style>

  <!-- One choice in a row's strip: the picture, the check and the 2 px border when the game is on it, the hover
       actions, and the caption. A Button, so a click applies in one click (addendum B), Enter and Space do the
       same from the keyboard, and the focus ring is the control's own. The size is the page's zoom, read off the
       page rather than set here, so one template serves every step. -->
  <Style x:Key="RowChoiceTileButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Command" Value="{Binding ApplyCommand}" />
    <Setter Property="ContextMenu" Value="{StaticResource TileMenu}" />
    <Setter Property="ToolTip" Value="{Binding ToolTipText}" />
    <Setter Property="AutomationProperties.Name" Value="{Binding Title}" />
    <Setter Property="VerticalAlignment" Value="Top" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <StackPanel>
            <Grid Width="{Binding DataContext.ThumbWidth, RelativeSource={RelativeSource AncestorType=UserControl}}"
                  Height="{Binding DataContext.ThumbHeight, RelativeSource={RelativeSource AncestorType=UserControl}}">
              <Border Background="{StaticResource TileBrush}" ClipToBounds="True" CornerRadius="{StaticResource Radius}">
                <Image RenderOptions.BitmapScalingMode="HighQuality" Source="{Binding Thumbnail}" Stretch="UniformToFill" />
              </Border>

              <!-- The in-game ring, 2 px in Text, over the picture rather than around the tile, so a strip of
                   tiles stays evenly spaced whichever one the game is on. -->
              <Border x:Name="Ring"
                      BorderBrush="{StaticResource TextBrush}"
                      BorderThickness="2"
                      CornerRadius="{StaticResource Radius}"
                      IsHitTestVisible="False"
                      Opacity="0" />

              <!-- Drawn rather than typed: a glyph would depend on the font having one. -->
              <Border x:Name="Check"
                      Margin="4"
                      HorizontalAlignment="Right"
                      VerticalAlignment="Top"
                      Background="{StaticResource BgBrush}"
                      CornerRadius="4"
                      Padding="4,3"
                      ToolTip="In game"
                      Visibility="Collapsed">
                <Path Data="M0,3.4 L2.8,6.2 L8,0.6"
                      Stroke="{StaticResource TextBrush}"
                      StrokeEndLineCap="Round"
                      StrokeLineJoin="Round"
                      StrokeStartLineCap="Round"
                      StrokeThickness="1.6" />
              </Border>

              <!-- Opacity rather than Visibility, the map panel's lesson: a collapsed button cannot be tabbed to,
                   so it would be mouse-only. Revealed by the pointer, by keyboard focus, or by the tile's own
                   menu being open, so the button that opened the menu is still drawn under it. -->
              <StackPanel x:Name="Actions"
                          HorizontalAlignment="Center"
                          VerticalAlignment="Center"
                          Opacity="0"
                          Orientation="Horizontal">
                <TextBlock x:Name="ApplyLabel"
                           Padding="8,3"
                           VerticalAlignment="Center"
                           Background="{StaticResource SurfaceBrush}"
                           FontSize="11"
                           Text="Apply"
                           Visibility="{Binding DataContext.ShowTileApply, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource BoolToVis}}" />
                <Button AutomationProperties.Name="More actions"
                        controls:TileMenus.OpensMenu="True"
                        Style="{StaticResource TileAction}">
                  <controls:Icon Geometry="{StaticResource Icon.Dots}" Size="14" />
                </Button>
              </StackPanel>
            </Grid>

            <TextBlock Margin="0,4,0,0"
                       MaxWidth="{Binding DataContext.ThumbWidth, RelativeSource={RelativeSource AncestorType=UserControl}}"
                       HorizontalAlignment="Left"
                       FontSize="11"
                       Foreground="{StaticResource Text2Brush}"
                       Text="{Binding Title}"
                       TextTrimming="CharacterEllipsis" />
          </StackPanel>
          <ControlTemplate.Triggers>
            <DataTrigger Binding="{Binding IsInGame}" Value="True">
              <Setter TargetName="Ring" Property="Opacity" Value="1" />
              <Setter TargetName="Check" Property="Visibility" Value="Visible" />
            </DataTrigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Actions" Property="Opacity" Value="1" />
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Actions" Property="Opacity" Value="1" />
              <Setter TargetName="Ring" Property="Opacity" Value="0.6" />
            </Trigger>
            <DataTrigger Binding="{Binding ContextMenu.IsOpen, RelativeSource={RelativeSource Self}}" Value="True">
              <Setter TargetName="Actions" Property="Opacity" Value="1" />
            </DataTrigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="Actions" Property="Opacity" Value="0" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- "Custom (11)" at the end of a folded strip, and "+7" beside one. The same height as a thumbnail, so the
       line of them is level. -->
  <Style x:Key="RowMoreTileButton" TargetType="Button" BasedOn="{StaticResource OutlineButton}">
    <Setter Property="FontSize" Value="11" />
    <Setter Property="Padding" Value="8,3" />
    <Setter Property="VerticalAlignment" Value="Top" />
    <Setter Property="Height" Value="{Binding DataContext.ThumbHeight, RelativeSource={RelativeSource AncestorType=UserControl}}" />
  </Style>

  <!-- One tile type per template, each deferring to the one above, because an implicit DataTemplate is matched on
       the item's own type. -->
  <DataTemplate DataType="{x:Type vm:MapPictureTileViewModel}">
    <Button Style="{StaticResource RowChoiceTileButton}" />
  </DataTemplate>

  <DataTemplate DataType="{x:Type vm:CustomPictureTileViewModel}">
    <Button Style="{StaticResource RowChoiceTileButton}" />
  </DataTemplate>

  <DataTemplate DataType="{x:Type vm:RowMoreViewModel}">
    <Button Command="{Binding UnfoldCommand}"
            Content="{Binding Text}"
            AutomationProperties.Name="{Binding Text}"
            Style="{StaticResource RowMoreTileButton}" />
  </DataTemplate>

  <!-- Addendum B and C: one row, shared by both rows pages. Name column 168 px, then the strip, with the "+N"
       tile floating at the strip's right end rather than in a column of its own: a column would take width from
       the strip, which would change the overflow, which would change the column, which is a layout loop.
       StripPanel keeps MoreWidth clear for it instead. -->
  <DataTemplate x:Key="MapRow">
    <Border controls:RowRealiser.Realise="True" Style="{StaticResource RowBorder}">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="168" />
          <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>

        <StackPanel Margin="0,0,12,0" VerticalAlignment="Center">
          <TextBlock FontSize="13"
                     FontWeight="Medium"
                     Text="{Binding DisplayName}"
                     TextTrimming="CharacterEllipsis"
                     ToolTip="{Binding DisplayName}" />

          <!-- Two tags, because Missing is a style of its own rather than a colour on the ordinary one. Each sits
               in a wrapper that carries the visibility, so neither theme style is derived from. -->
          <Grid MaxWidth="156"
                Margin="0,3,0,0"
                HorizontalAlignment="Left"
                Visibility="{Binding ShowTag, Converter={StaticResource BoolToVis}}">
            <Grid>
              <Grid.Style>
                <Style TargetType="Grid">
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding IsMissing}" Value="True">
                      <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </Grid.Style>
              <Border Style="{StaticResource StateTagBorder}">
                <TextBlock Text="{Binding TagText}" TextTrimming="CharacterEllipsis" />
              </Border>
            </Grid>
            <Grid>
              <Grid.Style>
                <Style TargetType="Grid">
                  <Setter Property="Visibility" Value="Collapsed" />
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding IsMissing}" Value="True">
                      <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </Grid.Style>
              <Border Style="{StaticResource MissingTagBorder}">
                <TextBlock Text="{Binding TagText}" TextTrimming="CharacterEllipsis" />
              </Border>
            </Grid>
          </Grid>
        </StackPanel>

        <Grid Grid.Column="1">
          <ItemsControl Focusable="False" ItemsSource="{Binding Choices}">
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                <!-- SetCurrentValue writes Overflow from the measure, so this OneWayToSource binding survives it
                     and the row learns the number without the view reaching into the panel's name scope. -->
                <controls:StripPanel IsUnfolded="{Binding IsUnfolded}" Overflow="{Binding Overflow, Mode=OneWayToSource}" />
              </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
          </ItemsControl>

          <Button HorizontalAlignment="Right"
                  VerticalAlignment="Top"
                  Command="{Binding UnfoldCommand}"
                  Content="{Binding MoreText}"
                  AutomationProperties.Name="{Binding MoreText}"
                  Style="{StaticResource RowMoreTileButton}"
                  Visibility="{Binding ShowMore, Converter={StaticResource BoolToVis}}" />
        </Grid>
      </Grid>
    </Border>
  </DataTemplate>
```

`Controls.xaml` has no `BoolToVis` of its own. Add one at the top of the file, beside the existing converter
instances:

```xml
  <BooleanToVisibilityConverter x:Key="BoolToVis" />
```

The template binds three members Task 29 did not put on the row, so add them to `MapRowViewModel` now, beside
`IsUnfolded`:

```csharp
    /// <summary>How many choices the strip could not fit on one line. Written by StripPanel through a
    /// OneWayToSource binding, because the panel is the only thing that measured them (addendum B, q2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText), nameof(ShowMore))]
    public partial int Overflow { get; set; }

    /// <summary>"+7". Empty at zero, which is also when the tile is hidden.</summary>
    public string MoreText => Overflow > 0 ? $"+{Overflow}" : "";

    public bool ShowMore => Overflow > 0 && !IsUnfolded;
```

and add `ShowMore` to the notify list of `IsUnfolded`:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMore))]
    public partial bool IsUnfolded { get; set; }
```

and, in `StripPanel`, write `Overflow` through `SetCurrentValue` in both places it is assigned, so the binding is
not replaced by a local value:

```csharp
            SetCurrentValue(OverflowProperty, 0);
```

```csharp
        SetCurrentValue(OverflowProperty, InternalChildren.Count - shown);
```

with the property registered two-way so the source sees it:

```csharp
    public static readonly DependencyProperty OverflowProperty =
        DependencyProperty.Register(
            nameof(Overflow),
            typeof(int),
            typeof(StripPanel),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
```

and its setter becomes public, since a binding target may not be write-protected:

```csharp
    public int Overflow
    {
        get => (int)GetValue(OverflowProperty);
        set => SetValue(OverflowProperty, value);
    }
```

- [ ] **Step 6: the page's markup [q2] [q3] [q4]**

Replace the whole of `src\BhMaps.App\Views\Pages\BackgroundsView.xaml` with:

```xml
<UserControl x:Class="BhMaps.App.Views.Pages.BackgroundsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"
             xmlns:pages="clr-namespace:BhMaps.App.ViewModels.Pages"
             PreviewKeyDown="OnPreviewKeyDown">
  <UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
  </UserControl.Resources>

  <DockPanel Margin="24,16,24,24">
    <!-- Addendum B: title, search, zoom and Add Custom Image all on the header's one line. -->
    <controls:PageHeader DockPanel.Dock="Top" Title="Backgrounds">
      <controls:PageHeader.Actions>
        <StackPanel Orientation="Horizontal">
          <Grid Width="200" Height="{StaticResource ActionHeight}" VerticalAlignment="Center">
            <!-- Its own padding, not the style's: a TextBox reserves the whole of Geist's line box, which at this
                 size is taller than an action, so the box keeps 6 at the top and 0 at the bottom. 6 is Task 4's
                 measurement, not a typo; do not put 3 back. Tagged, not named: an element inside PageHeader's
                 content cannot take an x:Name, and RowKeys finds the box by this marker. -->
            <TextBox Padding="33,6,10,0"
                     AutomationProperties.Name="{Binding SearchPlaceholder}"
                     Style="{StaticResource FieldTextBox}"
                     Tag="SearchBox"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />

            <controls:Icon Margin="11,0,0,0"
                           HorizontalAlignment="Left"
                           VerticalAlignment="Center"
                           Foreground="{StaticResource Text3Brush}"
                           Geometry="{StaticResource Icon.Search}"
                           Size="15" />

            <TextBlock Margin="34,0,11,0"
                       VerticalAlignment="Center"
                       IsHitTestVisible="False"
                       Text="{Binding SearchPlaceholder}">
              <TextBlock.Style>
                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                  <Setter Property="Foreground" Value="{StaticResource Text3Brush}" />
                  <Setter Property="Visibility" Value="Collapsed" />
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding SearchText}" Value="">
                      <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </TextBlock.Style>
            </TextBlock>
          </Grid>

          <controls:ZoomSlider Margin="12,0,0,0"
                               VerticalAlignment="Center"
                               Label="{Binding ZoomLabel}"
                               Maximum="{x:Static pages:RowsPageViewModel.MaxZoom}"
                               Minimum="{x:Static pages:RowsPageViewModel.MinZoom}"
                               Value="{Binding Zoom}" />

          <!-- Solid: adding a picture is this page's one primary action (spec 4). -->
          <Button Margin="12,0,0,0"
                  Command="{Binding AddPicturesCommand}"
                  Content="Add Custom Image"
                  controls:Icon.Glyph="{StaticResource Icon.Plus}"
                  IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}"
                  Style="{StaticResource PrimaryButton}" />
        </StackPanel>
      </controls:PageHeader.Actions>
    </controls:PageHeader>

    <!-- Addendum B, q4: the chip row ends at the last chip. Ticking is the Maps page's, so there is no "Select
         all shown" here and no bar at the foot of the page. -->
    <ListBox DockPanel.Dock="Top"
             Margin="0,4,0,8"
             HorizontalAlignment="Left"
             ItemContainerStyle="{StaticResource ChipRow}"
             ItemsSource="{Binding Chips}"
             SelectedItem="{Binding SelectedChip}"
             Style="{StaticResource TileListBox}">
      <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
          <StackPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
      </ListBox.ItemsPanel>
    </ListBox>

    <!-- Spec 3.1's first-run line, on this page too, and it goes when a pack arrives. -->
    <TextBlock DockPanel.Dock="Top"
               Margin="0,0,0,12"
               Foreground="{StaticResource Text2Brush}"
               TextWrapping="Wrap"
               Visibility="{Binding ShowFirstRunLine, Converter={StaticResource BoolToVis}}">
      <Run Text="No packs yet. Import a folder of map art on the " /><Hyperlink Command="{Binding DataContext.NavigatePacksCommand, RelativeSource={RelativeSource AncestorType=Window}}" Foreground="{StaticResource TextBrush}"><Run Text="Packs" /></Hyperlink><Run Text=" page, or add a custom image on " /><Hyperlink Command="{Binding AddPicturesCommand}" Foreground="{StaticResource TextBrush}"><Run Text="Backgrounds" /></Hyperlink><Run Text="." />
    </TextBlock>

    <Grid>
      <!-- The rows virtualise (addendum B). Recycling is safe here because nothing in a row binds a container
           property back into the view model: without ticks there is no IsSelected to be written by a container
           that has just been handed to a different row. -->
      <ItemsControl ItemTemplate="{StaticResource MapRow}"
                    ItemsSource="{Binding Rows}"
                    KeyboardNavigation.DirectionalNavigation="Continue"
                    KeyboardNavigation.TabNavigation="Continue"
                    ScrollViewer.CanContentScroll="True"
                    ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                    ScrollViewer.VerticalScrollBarVisibility="Auto"
                    VirtualizingPanel.IsVirtualizing="True"
                    VirtualizingPanel.VirtualizationMode="Recycling">
        <ItemsControl.Template>
          <!-- An ItemsControl has no ScrollViewer of its own, and without one the VirtualizingStackPanel is
               handed infinite height and realises every row. -->
          <ControlTemplate TargetType="ItemsControl">
            <ScrollViewer CanContentScroll="True" Focusable="False" Padding="{TemplateBinding Padding}">
              <ItemsPresenter />
            </ScrollViewer>
          </ControlTemplate>
        </ItemsControl.Template>
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate>
            <VirtualizingStackPanel />
          </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
      </ItemsControl>

      <!-- Addendum B: an empty list says why in one line, and offers the way back when a search caused it. -->
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel.Style>
          <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding Rows.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </StackPanel.Style>
        <TextBlock HorizontalAlignment="Center"
                   Foreground="{StaticResource Text2Brush}"
                   Text="{Binding EmptyText}"
                   TextWrapping="Wrap" />
        <Button Margin="0,8,0,0"
                HorizontalAlignment="Center"
                Command="{Binding ClearSearchCommand}"
                Content="Clear"
                Style="{StaticResource PlainButton}"
                Visibility="{Binding ShowClearSearch, Converter={StaticResource BoolToVis}}" />
      </StackPanel>
    </Grid>
  </DockPanel>
</UserControl>
```

Replace the whole of `src\BhMaps.App\Views\Pages\BackgroundsView.xaml.cs` with:

```csharp
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class BackgroundsView : UserControl
{
    public BackgroundsView()
    {
        InitializeComponent();
    }

    /// <summary>Addendum B's keyboard, shared with the Platforms page so the two cannot answer a key differently.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => RowKeys.OnPreviewKeyDown(sender, e);
}
```

- [ ] **Step 7: the page folds its rows when it is left**

In `src\BhMaps.App\ViewModels\MainViewModel.cs`, add the hook beside the other `partial void On...Changed`
methods, so leaving the page puts every unfolded row back to one line (addendum B):

```csharp
    /// <summary>Addendum B: unfolded rows fold back when the page is left, so coming back to a rows page shows
    /// the same thing it shows on a first visit.</summary>
    partial void OnCurrentPageChanging(PageViewModel? oldValue, PageViewModel? newValue)
    {
        if (oldValue is RowsPageViewModel rows)
        {
            rows.FoldAll();
        }
    }
```

- [x] **Step 8: the Maps card stops growing for its tag (owner change O6): already landed**

Owner change O6 landed on the branch at 1b07ffb (2026-09-11, 20:10) before this plan ran: one `CardTagPill`
DataTemplate drawn in one of two places (`NameRowTag` on the name row when the card is 200 px or wider,
`PictureTag` over the picture's bottom-left corner when narrower), so no card grows for its tag. Do not redo it;
this task's only edit to `MapsView.xaml` is deleting the local `ChipRow` copy in Step 5. If the template you find
differs from this description, follow what is on the branch.

- [ ] **Step 9: build, test, format, and look at the page**

```powershell
dotnet build BhMaps.slnx -c Debug
dotnet test BhMaps.slnx
dotnet format BhMaps.slnx
dotnet run --project src\BhMaps.App -- --game "<DEV>\game\mapArt" --library "<DEV>\lib" --appdata "<DEV>\appdata"
```

Expected: 0 warnings, every test still passing (this task adds none), and on Backgrounds:

1. One row per map, name on the left with its tag under it, thumbnails to the right, captions under them.
2. The choice the game is on is first, ringed in Text with a check at its top right.
3. Custom pictures are behind one "Custom (11)" tile at the end; clicking it wraps the row open and the tile
   goes; clicking the row's body again folds it.
4. Narrowing the window to 1000 px puts a "+N" tile at the right end of the crowded rows; clicking it unfolds.
5. The zoom slider runs 1 to 5, starts at 2, and the rows grow and shrink with it; the value survives a restart.
6. Typing "sewer" keeps the rows whose map name or whose thumbnails' pack or file names match, and an empty
   result reads "No map or picture matches 'sewer'." with Clear. Escape in the box clears it.
7. The chips narrow the rows; there is no "Select all shown" and no bar at the foot of the page.
8. Clicking a thumbnail applies it: the done line reads "flowermap applied to Brawlhaven." plus the shared
   sentence, with Undo beside it, and the ring moves to the tile that was clicked after the rescan.
9. Right-click and the dots button open the same menu; a pack tile's reads "Apply to Brawlhaven", "Apply to all
   maps", "Edit", "Show in folder", and grows "Apply to the 3 selected maps" once three maps are ticked on Maps.
10. On Maps at zoom 6 with the panel open, no card shows an empty line under its name, and a card narrower than
    200 px carries its pill over the picture's bottom-left corner.

Captures: `<SHOTS>\t30-backgrounds-rows.png`, `<SHOTS>\t30-backgrounds-unfolded.png`,
`<SHOTS>\t30-backgrounds-search.png`, `<SHOTS>\t30-maps-card-no-empty-line.png`.

- [ ] **Step 10: commit**

```powershell
git add src/BhMaps.App/Theme/Controls.xaml src/BhMaps.App/Views/Controls/TileMenus.cs src/BhMaps.App/Views/Controls/RowRealiser.cs src/BhMaps.App/Views/Controls/RowKeys.cs src/BhMaps.App/Views/Controls/ZoomSlider.xaml src/BhMaps.App/Views/Controls/ZoomSlider.xaml.cs src/BhMaps.App/Views/Controls/StripPanel.cs src/BhMaps.App/ViewModels/MapRowViewModel.cs src/BhMaps.App/ViewModels/MainViewModel.cs src/BhMaps.App/ViewModels/Pages/RowsPageViewModel.cs src/BhMaps.App/ViewModels/Pages/BackgroundsViewModel.cs src/BhMaps.App/Views/Pages/BackgroundsView.xaml src/BhMaps.App/Views/Pages/BackgroundsView.xaml.cs
git commit -m "feat(backgrounds): one row per map with its background choices" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
git add src/BhMaps.App/Views/Pages/MapsView.xaml
git commit -m "fix(maps): a card never grows a row for its tag" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

### Task 31: the Platforms rows page, its cropped thumbnails, and the fifth tab

**Files:**
- Create: `src\BhMaps.Core\Imaging\PlatformBounds.cs`
- Create: `tests\BhMaps.Core.Tests\PlatformBoundsTests.cs`
- Modify: `src\BhMaps.Core\Imaging\MapCompositor.cs` (`Render` takes a viewport)
- Modify: `src\BhMaps.Core\Imaging\PreviewCache.cs` (`KeyFor` and `GetOrRenderAsync` take the viewport)
- Modify: `src\BhMaps.App\ViewModels\PlatformSetTileViewModel.cs` (the four members the shared tile template binds)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (the implicit template for a set tile)
- Create: `src\BhMaps.App\ViewModels\Pages\PlatformsViewModel.cs`
- Create: `src\BhMaps.App\Views\Pages\PlatformsView.xaml` and `.xaml.cs`
- Modify: `src\BhMaps.App\App.xaml` (the page template)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (the page, the command, the page list)
- Modify: `src\BhMaps.App\Views\MainWindow.xaml` (the tab and Ctrl+5)

**Interfaces:**

Consumes, from Tasks 29 and 30 and from HEAD:

```csharp
// Task 29
public static class MapChoices
{
    public static IReadOnlyList<PlatformSetTileViewModel> Platforms(
        MainViewModel shell, MapEntry map, MapStatus? status, ScanSnapshot snapshot,
        int width, int height, Action showFiles);
}
public sealed partial class MapRowViewModel : ObservableObject
{
    public MapRowViewModel(
        MapEntry map, string tagText, bool isMissing, bool isChanged, bool showsCustomPicture, string haystack,
        IReadOnlyList<object> alwaysShown, IReadOnlyList<object> foldedAway, string foldedLabel,
        IReadOnlyList<PictureTileViewModel> pictures, IReadOnlyList<PlatformSetTileViewModel> sets,
        Func<PlatformSetTileViewModel, CancellationToken, Task>? composeSet);
}
public sealed class ThumbnailCache { public Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct); }
// AppServices.RowThumbnails, AppSettings.MinRowZoom = 1, AppSettings.MaxRowZoom = 5, AppSettings.PlatformsZoom default 3

// Task 30
public abstract partial class RowsPageViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinRowZoom;
    public const int MaxZoom = AppSettings.MaxRowZoom;
    protected RowsPageViewModel(MainViewModel shell, int storedZoom);
    protected ScanSnapshot? Snapshot { get; }
    public ObservableCollection<MapRowViewModel> Rows { get; }
    public IReadOnlyList<string> Chips { get; }
    public partial string SearchText { get; set; }
    public partial string SelectedChip { get; set; }
    public partial int Zoom { get; set; }
    public double ThumbHeight { get; }
    public double ThumbWidth { get; }
    public bool ShowTileApply { get; }
    public bool ShowClearSearch { get; }
    public bool ShowFirstRunLine { get; }
    public string EmptyText { get; }
    public abstract string SearchPlaceholder { get; }
    public virtual string ZoomLabel { get; }
    protected abstract bool HasCustomChip { get; }
    protected abstract string NoResultsText { get; }
    protected abstract void SaveZoom(int value);
    protected abstract MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot);
    public void RealiseRow(MapRowViewModel row);
    public void FoldAll();
    public IRelayCommand ClearSearchCommand { get; }
}
public static class RowKeys { public static void OnPreviewKeyDown(object sender, KeyEventArgs e); }
// Controls.xaml keys: ChipRow, RowChoiceTileButton, RowMoreTileButton, MapRow

// HEAD
public sealed record CameraBounds(double X, double Y, double W, double H);
public sealed record LevelDesc(string LevelName, string AssetDir, CameraBounds Camera,
    IReadOnlyList<LevelBackground> Backgrounds, IReadOnlyList<PlatformNode> Platforms);
public sealed record PlatformNode(double X, double Y, double Scale, double ScaleX, double ScaleY, double Rotation,
    string? Theme, IReadOnlyList<LevelAsset> Assets, IReadOnlyList<PlatformNode> Children)
{
    public bool IsThemed { get; }
    public double EffectiveScaleX { get; }
    public double EffectiveScaleY { get; }
}
public sealed record LevelAsset(string AssetName, double X, double Y, double W, double H);
public sealed class AssetSources { public AssetSources(string mapArtPath, string? packRoot = null, string? backgroundOverride = null); }
public sealed class PreviewCache
{
    public string KeyFor(string levelName, int width, int height, IReadOnlyList<string> inputs);
    public Task<string> GetOrRenderAsync(LevelDesc level, int width, int height, AssetSources sources, CancellationToken ct = default);
}
public sealed class ThumbnailProvider { public static GameFile? PickRepresentative(GameFolder folder); }
public sealed partial class PlatformSetTileViewModel : ObservableObject
{
    public MapEntry Map { get; }
    public Pack Pack { get; }
    public string PackName { get; }
    public bool InGame { get; }
    public string ToolTipText { get; }
    public partial ImageSource? Preview { get; set; }
    public IAsyncRelayCommand ApplyToMapCommand { get; }
}
```

Produces:

```csharp
// BhMaps.Core\Imaging\PlatformBounds.cs
public static class PlatformBounds
{
    public const double Pad = 0.12;
    public static CameraBounds? For(LevelDesc level, double pad, double aspect);
}

// BhMaps.Core\Imaging\MapCompositor.cs
public static BitmapSource Render(LevelDesc level, int width, int height, AssetSources sources, CameraBounds? viewport = null);

// BhMaps.Core\Imaging\PreviewCache.cs
public string KeyFor(string levelName, int width, int height, IReadOnlyList<string> inputs, CameraBounds? viewport = null);
public Task<string> GetOrRenderAsync(LevelDesc level, int width, int height, AssetSources sources,
    CancellationToken ct = default, CameraBounds? viewport = null);

// BhMaps.App\ViewModels\PlatformSetTileViewModel.cs
public string Title { get; }
public bool IsInGame { get; }
public ImageSource? Thumbnail { get; }
public ICommand ApplyCommand { get; }

// BhMaps.App\ViewModels\Pages\PlatformsViewModel.cs
public partial class PlatformsViewModel : RowsPageViewModel
{
    public const int SetWidth = 448;
    public const int SetHeight = 252;
    public PlatformsViewModel(MainViewModel shell);
}

// BhMaps.App\ViewModels\MainViewModel.cs
public PlatformsViewModel Platforms { get; }
public IRelayCommand NavigatePlatformsCommand { get; }
```

- [ ] **Step 1: write the failing test for the platform crop**

Create `tests\BhMaps.Core.Tests\PlatformBoundsTests.cs`:

```csharp
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Tests;

public class PlatformBoundsTests
{
    private const double Aspect = 16d / 9d;

    [Fact]
    public void For_BoxesThePlatformsPadsThemAndKeepsTheAspect()
    {
        // One 200 x 100 platform in the middle of a 2000 x 1000 level.
        var level = Level(new CameraBounds(0, 0, 2000, 1000), Node(900, 450, Asset(0, 0, 200, 100)));

        var bounds = PlatformBounds.For(level, 0.12, Aspect);

        Assert.NotNull(bounds);

        // 200 wide plus 12 percent each side is 248; 100 tall plus 12 percent each side is 124. 248 / 124 is
        // exactly 2, which is wider than 16:9, so the height grows to 248 / (16/9) = 139.5.
        Assert.Equal(248, bounds!.W, 3);
        Assert.Equal(139.5, bounds.H, 3);

        // Centred on the platform, which sits at 900..1100 by 450..550, so the centre is 1000, 500.
        Assert.Equal(1000, bounds.X + (bounds.W / 2), 3);
        Assert.Equal(500, bounds.Y + (bounds.H / 2), 3);
    }

    [Fact]
    public void For_UnionsEveryPlatformIncludingNestedAndScaledOnes()
    {
        var level = Level(
            new CameraBounds(0, 0, 4000, 2000),
            Node(1000, 1000, Asset(0, 0, 100, 100)),
            Node(2000, 1000, scale: 2, child: Node(0, 0, Asset(0, 0, 100, 100))));

        var bounds = PlatformBounds.For(level, 0, Aspect);

        // 1000..1100 from the first, and 2000..2200 from the second (100 wide at scale 2).
        Assert.NotNull(bounds);
        Assert.True(bounds!.X <= 1000);
        Assert.True(bounds.X + bounds.W >= 2200);
    }

    [Fact]
    public void For_ClampsToTheCameraAndNeverLeavesIt()
    {
        // A platform hard against the left edge, with padding that would push the box off the level.
        var level = Level(new CameraBounds(0, 0, 1000, 600), Node(0, 300, Asset(0, 0, 40, 20)));

        var bounds = PlatformBounds.For(level, 0.12, Aspect);

        Assert.NotNull(bounds);
        Assert.True(bounds!.X >= 0);
        Assert.True(bounds.Y >= 0);
        Assert.True(bounds.X + bounds.W <= 1000.001);
        Assert.True(bounds.Y + bounds.H <= 600.001);
    }

    [Fact]
    public void For_SkipsSeasonalNodesAndReturnsNullWhenNothingIsLeft()
    {
        var level = Level(
            new CameraBounds(0, 0, 1000, 600),
            Node(100, 100, Asset(0, 0, 50, 50)) with { Theme = "Snow" });

        Assert.Null(PlatformBounds.For(level, 0.12, Aspect));
    }

    [Fact]
    public void For_ReturnsNullForALevelWithNoUsableCamera()
    {
        var level = Level(new CameraBounds(0, 0, 0, 0), Node(10, 10, Asset(0, 0, 50, 50)));

        Assert.Null(PlatformBounds.For(level, 0.12, Aspect));
    }

    private static LevelDesc Level(CameraBounds camera, params PlatformNode[] platforms) =>
        new("Test", "Test", camera, [], platforms);

    private static PlatformNode Node(double x, double y, LevelAsset asset) =>
        new(x, y, 1, 1, 1, 0, null, [asset], []);

    private static PlatformNode Node(double x, double y, double scale, PlatformNode child) =>
        new(x, y, scale, 1, 1, 0, null, [], [child]);

    private static LevelAsset Asset(double x, double y, double w, double h) => new("a.png", x, y, w, h);
}
```

- [ ] **Step 2: run it and watch it fail**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~PlatformBoundsTests
```

Expected: the build fails with CS0103, "The name 'PlatformBounds' does not exist in the current context".

- [ ] **Step 3: the platform bounding box**

Create `src\BhMaps.Core\Imaging\PlatformBounds.cs`:

```csharp
using System.Windows;
using System.Windows.Media;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>The part of a level a platform thumbnail shows (addendum C). A whole-level composite at row height
/// draws the platforms as slivers, so a rows page renders the platforms' own bounding box instead: padded,
/// widened to the thumbnail's aspect, and clamped to the level, so every set of one map is cropped the same way
/// and the row compares like with like. The map panel and the pack drawer keep the whole level.</summary>
public static class PlatformBounds
{
    /// <summary>How much of the box's own width and height is added on each side, so the platforms are not
    /// pressed against the edge of the thumbnail.</summary>
    public const double Pad = 0.12;

    /// <summary>Null for a level with no camera, or one whose only platforms are seasonal, which the compositor
    /// does not draw either. Then the caller renders the whole level as before.</summary>
    public static CameraBounds? For(LevelDesc level, double pad, double aspect)
    {
        var camera = level.Camera;
        if (camera.W <= 0 || camera.H <= 0 || aspect <= 0)
        {
            return null;
        }

        var box = Rect.Empty;
        foreach (var node in level.Platforms)
        {
            Union(node, Matrix.Identity, ref box);
        }

        if (box.IsEmpty)
        {
            return null;
        }

        // A level whose assets all declare no size collapses to a point, which would scale to nothing.
        var w = box.Width > 0 ? box.Width : camera.W * 0.1;
        var h = box.Height > 0 ? box.Height : camera.H * 0.1;
        var cx = box.X + (box.Width / 2);
        var cy = box.Y + (box.Height / 2);

        w += w * pad * 2;
        h += h * pad * 2;

        // Grow the short axis to the thumbnail's shape, so the render fills it with no letterbox.
        if (w / h < aspect)
        {
            w = h * aspect;
        }
        else
        {
            h = w / aspect;
        }

        // Nothing outside the level, and the shape kept after the clamp.
        w = Math.Min(w, camera.W);
        h = Math.Min(h, camera.H);
        if (w / h > aspect)
        {
            w = h * aspect;
        }
        else
        {
            h = w / aspect;
        }

        var x = Math.Clamp(cx - (w / 2), camera.X, camera.X + camera.W - w);
        var y = Math.Clamp(cy - (h / 2), camera.Y, camera.Y + camera.H - h);
        return new CameraBounds(x, y, w, h);
    }

    /// <summary>The same transform order the compositor draws with: scale, then rotate, then translate, each node
    /// inside its parent's. A seasonal node is skipped here because it is skipped there.</summary>
    private static void Union(PlatformNode node, Matrix parent, ref Rect box)
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
            // A missing W or H parses as 0 and means "the image's own size", which is not known without decoding
            // the file, so the asset contributes its position alone rather than a guessed rectangle.
            var rect = new Rect(asset.X, asset.Y, Math.Abs(asset.W), Math.Abs(asset.H));
            var transformed = Rect.Transform(rect, matrix);
            box.Union(transformed);
        }

        foreach (var child in node.Children)
        {
            Union(child, matrix, ref box);
        }
    }
}
```

- [ ] **Step 4: run it and watch it pass**

```powershell
dotnet test BhMaps.slnx --filter FullyQualifiedName~PlatformBoundsTests
```

Expected: 5 passed.

- [ ] **Step 5: the compositor and the cache learn a viewport**

In `src\BhMaps.Core\Imaging\MapCompositor.cs`, `Render` takes the window it draws:

```csharp
    /// <summary>Frozen Bgr24 bitmap. Safe on any thread. Never throws for a missing or bad asset; that asset is
    /// skipped. <paramref name="viewport" /> is the part of the level to draw, in the level's own coordinates,
    /// and null means the whole camera, which is what every caller but the Platforms page wants (addendum C).</summary>
    public static BitmapSource Render(
        LevelDesc level, int width, int height, AssetSources sources, CameraBounds? viewport = null)
    {
        var camera = viewport ?? level.Camera;
```

Nothing else in the method changes: `DrawBackground` reads `level.Camera` itself, because the background is
stretched over the whole camera however little of it the viewport shows.

In `src\BhMaps.Core\Imaging\PreviewCache.cs`, the key and the render carry it too. `KeyFor` gains a last
parameter and one branch:

```csharp
    /// <summary>sha256 of level name, size, the viewport when there is one, and every input file hash, lowercase
    /// hex. A cropped render and a whole one read the same files, so without the viewport in the key the two
    /// would collide on one cache file.</summary>
    public string KeyFor(
        string levelName, int width, int height, IReadOnlyList<string> inputs, CameraBounds? viewport = null)
    {
        var text = new StringBuilder(levelName).Append('|').Append(width).Append('x').Append(height);
        if (viewport is { } v)
        {
            text.Append("|v")
                .Append(v.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(v.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(v.W.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(v.H.ToString("R", CultureInfo.InvariantCulture));
        }

        foreach (var input in inputs)
        {
```

with `using System.Globalization;` added at the top of the file. `GetOrRenderAsync` takes the viewport after the
token, so every existing call that passes a token positionally still compiles:

```csharp
    public async Task<string> GetOrRenderAsync(
        LevelDesc level, int width, int height, AssetSources sources, CancellationToken ct = default,
        CameraBounds? viewport = null)
    {
        var inputs = MapCompositor.CollectInputs(level, sources);
        var key = KeyFor(level.LevelName, width, height, inputs, viewport);
```

and the render itself:

```csharp
        var jpeg = await _queue
            .RunAsync(() => Encode(MapCompositor.Render(level, width, height, sources, viewport)), ct)
            .ConfigureAwait(false);
```

- [ ] **Step 6: the set tile fits the shared row template**

The row templates bind one set of names. In `src\BhMaps.App\ViewModels\PlatformSetTileViewModel.cs`, add the four
the set tile is missing, so `RowChoiceTileButton` draws a set exactly as it draws a picture:

```csharp
    /// <summary>The caption under the thumbnail, and the tile's automation name. The same member the picture
    /// tiles carry, so one row template draws both (addendum C).</summary>
    public string Title => Pack.Name;

    /// <summary>The picture tiles' name for <see cref="InGame" />, for the same reason.</summary>
    public bool IsInGame => InGame;

    /// <summary>The picture tiles' name for <see cref="Preview" />.</summary>
    public ImageSource? Thumbnail => Preview;

    /// <summary>What a click on the tile does: one set onto its own map, live (addendum C).</summary>
    public ICommand ApplyCommand => ApplyToMapCommand;
```

with `using System.Windows.Input;` added at the top, and the alias told when the composite lands:

```csharp
    /// <summary>Null until the composite is ready. Always frozen, because it is drawn off the UI thread.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Thumbnail))]
    public partial ImageSource? Preview { get; set; }
```

In `src\BhMaps.App\Theme\Controls.xaml`, beside the other two tile templates:

```xml
  <DataTemplate DataType="{x:Type vm:PlatformSetTileViewModel}">
    <Button Style="{StaticResource RowChoiceTileButton}" />
  </DataTemplate>
```

- [ ] **Step 7: the Platforms page's view model [q1] [q4]**

Create `src\BhMaps.App\ViewModels\Pages\PlatformsViewModel.cs`:

```csharp
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Addendum C: the same rows, for the other half of a map's look. A platform set fits only its own map,
/// so a row lists exactly the packs that have a set for that map, each composed over the map's current
/// background and cropped to the platforms, because at row height a whole level shows them as slivers.</summary>
public partial class PlatformsViewModel : RowsPageViewModel
{
    /// <summary>The composite behind a set tile, rendered at twice the largest zoom step (128 px tall, 228 wide),
    /// so a thumbnail is still sharp at step 5 and on a high DPI screen. Exactly 16:9, which is the shape
    /// PlatformBounds crops to, so the render fills the tile with no letterbox.</summary>
    public const int SetWidth = 448;
    public const int SetHeight = 252;

    private const double Aspect = 16d / 9d;

    public PlatformsViewModel(MainViewModel shell)
        : base(shell, shell.Services.Settings.PlatformsZoom)
    {
    }

    public override string Title => "Platforms";

    public override string SearchPlaceholder => "Search maps and packs";

    /// <summary>Addendum C: the Maps chip row without "Custom". A platform set is never a custom picture.</summary>
    protected override bool HasCustomChip => false;

    protected override string NoResultsText => $"No map or pack matches '{SearchText}'.";

    protected override void SaveZoom(int value)
    {
        if (Shell.Services.Settings.PlatformsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { PlatformsZoom = value });
        }
    }

    /// <summary>Addendum C's order: the in-game set first with the check, then Default, then one tile per pack
    /// that has a set for this map. Nothing folds away: a map has as many sets as it has packs, and the "+N" tile
    /// is what handles a row too narrow for all of them.</summary>
    protected override MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot)
    {
        var map = card.Map;

        // Show files belongs to the map panel, which is where the file list lives; on a row the menu line would
        // have nothing to open, so the tile is handed a no-op and its own menu keeps Open folder.
        var sets = MapChoices.Platforms(Shell, map, status, snapshot, SetWidth, SetHeight, () => { });
        List<object> alwaysShown = [.. sets.Where(t => t.InGame), .. sets.Where(t => !t.InGame)];
        var (tag, missing) = Tag(map, status);

        return new MapRowViewModel(
            map,
            tag,
            missing,
            isChanged: tag.Length > 0 && !missing,
            showsCustomPicture: false,
            Haystack(map, sets),
            alwaysShown,
            [],
            foldedLabel: "",
            [],
            sets,
            ComposeSetAsync);
    }

    /// <summary>Addendum C's state tag, measured over this map's own folder only: Missing beats Custom beats the
    /// first pack that matched, and Default draws no tag at all.</summary>
    private static (string Tag, bool Missing) Tag(MapEntry map, MapStatus? status)
    {
        var prefix = map.FolderName + Path.DirectorySeparatorChar;
        var files = (status?.Files ?? Array.Empty<MapFileStatus>())
            .Where(f => f.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (files.Any(f => f.State == MapFileState.Missing))
        {
            return ("Missing", true);
        }

        if (files.Any(f => f.State == MapFileState.Custom))
        {
            return ("Custom", false);
        }

        return (files.SelectMany(f => f.PackNames).FirstOrDefault() ?? "", false);
    }

    /// <summary>Addendum C: the search matches map and pack names.</summary>
    private static string Haystack(MapEntry map, IReadOnlyList<PlatformSetTileViewModel> sets) =>
        string.Join('\n', [map.DisplayName, .. sets.Select(t => t.PackName)]);

    /// <summary>The pack's platform art over the map's current background, cropped to the platforms, through the
    /// preview cache and then the shared decode cache. Never throws: a file that vanished between the scan and
    /// the render leaves the tile grey, which is what every other preview path in the app does.</summary>
    private async Task ComposeSetAsync(PlatformSetTileViewModel tile, CancellationToken ct)
    {
        if (Snapshot is not { } snapshot)
        {
            return;
        }

        var map = tile.Map;
        var gamePath = Shell.Services.GamePath;
        var background = map.BaseLevel.Backgrounds.Count == 0
            ? null
            : Path.Combine(gamePath, AssetPath.Background(map.BaseLevel.Backgrounds[0].AssetName));
        var sources = new AssetSources(gamePath, tile.Pack.FullPath, background);
        var viewport = PlatformBounds.For(map.BaseLevel, PlatformBounds.Pad, Aspect);
        var previews = Shell.Services.Previews;
        var thumbnails = Shell.Services.RowThumbnails;

        ImageSource? image = null;
        if (snapshot.Catalog.HasLevelData)
        {
            try
            {
                // The whole call goes on the pool: GetOrRenderAsync hashes every input file on its caller's
                // thread before it reaches the render queue.
                image = await Task.Run(
                    async () =>
                    {
                        var path = await previews
                            .GetOrRenderAsync(map.BaseLevel, SetWidth, SetHeight, sources, ct, viewport)
                            .ConfigureAwait(false);
                        return await thumbnails.GetAsync(path, ct).ConfigureAwait(false);
                    },
                    ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or FileFormatException or ArgumentException)
            {
                image = null;
            }
        }

        // The v1 single-file tile, which stands in for a composite that could not be drawn and for the whole
        // no-level-data fallback (spec 3.6).
        if (image is null
            && tile.Pack.FindFolder(map.FolderName) is { } folder
            && ThumbnailProvider.PickRepresentative(folder) is { } file)
        {
            image = await thumbnails.GetAsync(file.FullPath, ct);
        }

        if (image is not null && !ct.IsCancellationRequested)
        {
            tile.Preview = image;
        }
    }
}
```

- [ ] **Step 8: the page's markup**

Create `src\BhMaps.App\Views\Pages\PlatformsView.xaml`. It is the Backgrounds page without the primary button and
without the first-run line's second link, because this page has nothing to add a picture to:

```xml
<UserControl x:Class="BhMaps.App.Views.Pages.PlatformsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"
             xmlns:pages="clr-namespace:BhMaps.App.ViewModels.Pages"
             PreviewKeyDown="OnPreviewKeyDown">
  <UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
  </UserControl.Resources>

  <DockPanel Margin="24,16,24,24">
    <!-- Addendum C: title, search and zoom. No primary button: nothing is added to the library from here. -->
    <controls:PageHeader DockPanel.Dock="Top" Title="Platforms">
      <controls:PageHeader.Actions>
        <StackPanel Orientation="Horizontal">
          <Grid Width="200" Height="{StaticResource ActionHeight}" VerticalAlignment="Center">
            <!-- Padding 33,6,10,0 is Task 4's measurement, not a typo; do not put 3 back. Tagged, not named: an
                 element inside PageHeader's content cannot take an x:Name, and RowKeys finds the box by this. -->
            <TextBox Padding="33,6,10,0"
                     AutomationProperties.Name="{Binding SearchPlaceholder}"
                     Style="{StaticResource FieldTextBox}"
                     Tag="SearchBox"
                     Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />

            <controls:Icon Margin="11,0,0,0"
                           HorizontalAlignment="Left"
                           VerticalAlignment="Center"
                           Foreground="{StaticResource Text3Brush}"
                           Geometry="{StaticResource Icon.Search}"
                           Size="15" />

            <TextBlock Margin="34,0,11,0"
                       VerticalAlignment="Center"
                       IsHitTestVisible="False"
                       Text="{Binding SearchPlaceholder}">
              <TextBlock.Style>
                <Style TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
                  <Setter Property="Foreground" Value="{StaticResource Text3Brush}" />
                  <Setter Property="Visibility" Value="Collapsed" />
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding SearchText}" Value="">
                      <Setter Property="Visibility" Value="Visible" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </TextBlock.Style>
            </TextBlock>
          </Grid>

          <controls:ZoomSlider Margin="12,0,0,0"
                               VerticalAlignment="Center"
                               Label="{Binding ZoomLabel}"
                               Maximum="{x:Static pages:RowsPageViewModel.MaxZoom}"
                               Minimum="{x:Static pages:RowsPageViewModel.MinZoom}"
                               Value="{Binding Zoom}" />
        </StackPanel>
      </controls:PageHeader.Actions>
    </controls:PageHeader>

    <ListBox DockPanel.Dock="Top"
             Margin="0,4,0,8"
             HorizontalAlignment="Left"
             ItemContainerStyle="{StaticResource ChipRow}"
             ItemsSource="{Binding Chips}"
             SelectedItem="{Binding SelectedChip}"
             Style="{StaticResource TileListBox}">
      <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
          <StackPanel Orientation="Horizontal" />
        </ItemsPanelTemplate>
      </ListBox.ItemsPanel>
    </ListBox>

    <TextBlock DockPanel.Dock="Top"
               Margin="0,0,0,12"
               Foreground="{StaticResource Text2Brush}"
               TextWrapping="Wrap"
               Visibility="{Binding ShowFirstRunLine, Converter={StaticResource BoolToVis}}">
      <Run Text="No packs yet. Import a folder of map art on the " /><Hyperlink Command="{Binding DataContext.NavigatePacksCommand, RelativeSource={RelativeSource AncestorType=Window}}" Foreground="{StaticResource TextBrush}"><Run Text="Packs" /></Hyperlink><Run Text=" page." />
    </TextBlock>

    <Grid>
      <ItemsControl ItemTemplate="{StaticResource MapRow}"
                    ItemsSource="{Binding Rows}"
                    KeyboardNavigation.DirectionalNavigation="Continue"
                    KeyboardNavigation.TabNavigation="Continue"
                    ScrollViewer.CanContentScroll="True"
                    ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                    ScrollViewer.VerticalScrollBarVisibility="Auto"
                    VirtualizingPanel.IsVirtualizing="True"
                    VirtualizingPanel.VirtualizationMode="Recycling">
        <ItemsControl.Template>
          <!-- An ItemsControl has no ScrollViewer of its own, and without one the VirtualizingStackPanel is
               handed infinite height and realises every row. -->
          <ControlTemplate TargetType="ItemsControl">
            <ScrollViewer CanContentScroll="True" Focusable="False" Padding="{TemplateBinding Padding}">
              <ItemsPresenter />
            </ScrollViewer>
          </ControlTemplate>
        </ItemsControl.Template>
        <ItemsControl.ItemsPanel>
          <ItemsPanelTemplate>
            <VirtualizingStackPanel />
          </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
      </ItemsControl>

      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel.Style>
          <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding Rows.Count}" Value="0">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </StackPanel.Style>
        <TextBlock HorizontalAlignment="Center"
                   Foreground="{StaticResource Text2Brush}"
                   Text="{Binding EmptyText}"
                   TextWrapping="Wrap" />
        <Button Margin="0,8,0,0"
                HorizontalAlignment="Center"
                Command="{Binding ClearSearchCommand}"
                Content="Clear"
                Style="{StaticResource PlainButton}"
                Visibility="{Binding ShowClearSearch, Converter={StaticResource BoolToVis}}" />
      </StackPanel>
    </Grid>
  </DockPanel>
</UserControl>
```

Create `src\BhMaps.App\Views\Pages\PlatformsView.xaml.cs`:

```csharp
using System.Windows.Controls;
using System.Windows.Input;
using BhMaps.App.Views.Controls;

namespace BhMaps.App.Views.Pages;

public partial class PlatformsView : UserControl
{
    public PlatformsView()
    {
        InitializeComponent();
    }

    /// <summary>Addendum C: the keyboard is Backgrounds', through the one handler both pages share.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => RowKeys.OnPreviewKeyDown(sender, e);
}
```

- [ ] **Step 9: the fifth tab**

In `src\BhMaps.App\App.xaml`, beside the other page templates:

```xml
      <DataTemplate DataType="{x:Type pages:PlatformsViewModel}">
        <pageviews:PlatformsView />
      </DataTemplate>
```

In `src\BhMaps.App\ViewModels\MainViewModel.cs`, build the page and put it in the list, in tab order, so a scan
refreshes it and the Maps page is still the first page refreshed:

```csharp
        Maps = new MapsViewModel(this);
        Backgrounds = new BackgroundsViewModel(this);
        Platforms = new PlatformsViewModel(this);
        Packs = new PacksViewModel(this);
        PackDetail = new PackDetailViewModel(this);
        SettingsPage = new SettingsPageViewModel(this);
        _pages = [Maps, Backgrounds, Platforms, Packs, PackDetail, SettingsPage];
```

```csharp
    public BackgroundsViewModel Backgrounds { get; }

    /// <summary>Addendum C: the tab the owner asked back, between Backgrounds and Packs.</summary>
    public PlatformsViewModel Platforms { get; }
```

```csharp
    [RelayCommand]
    private void NavigateBackgrounds() => CurrentPage = Backgrounds;

    [RelayCommand]
    private void NavigatePlatforms() => CurrentPage = Platforms;
```

In `src\BhMaps.App\Views\MainWindow.xaml`, the comment, the key binding and the tab:

```xml
  <!-- Nothing on a page triggers a rescan, so the keyboard does; Ctrl+1..5 are the five tabs (addendum A).
       Ctrl+K is in the code-behind: it navigates and then focuses, which a command cannot do. -->
  <Window.InputBindings>
    <KeyBinding Key="F5" Command="{Binding RefreshCommand}" />
    <KeyBinding Key="D1" Modifiers="Control" Command="{Binding NavigateMapsCommand}" />
    <KeyBinding Key="D2" Modifiers="Control" Command="{Binding NavigateBackgroundsCommand}" />
    <KeyBinding Key="D3" Modifiers="Control" Command="{Binding NavigatePlatformsCommand}" />
    <KeyBinding Key="D4" Modifiers="Control" Command="{Binding NavigatePacksCommand}" />
    <KeyBinding Key="D5" Modifiers="Control" Command="{Binding NavigateSettingsCommand}" />
  </Window.InputBindings>
```

```xml
        <StackPanel Grid.Column="1" Margin="24,0,0,0" VerticalAlignment="Center" Orientation="Horizontal">
          <Button Command="{Binding NavigateMapsCommand}" Content="Maps" Style="{StaticResource TopBarTab}" Tag="{Binding Maps}" />
          <Button Command="{Binding NavigateBackgroundsCommand}" Content="Backgrounds" Style="{StaticResource TopBarTab}" Tag="{Binding Backgrounds}" />
          <Button Command="{Binding NavigatePlatformsCommand}" Content="Platforms" Style="{StaticResource TopBarTab}" Tag="{Binding Platforms}" />
          <Button Command="{Binding NavigatePacksCommand}" Content="Packs" Style="{StaticResource TopBarTab}" Tag="{Binding Packs}" />
          <Button Command="{Binding NavigateSettingsCommand}" Content="Settings" Style="{StaticResource TopBarTab}" Tag="{Binding SettingsPage}" />
        </StackPanel>
```

and the top bar's comment above the `Border` becomes "Addendum A: brand, five tabs, and the game line at the
right. 44 px with a hairline under it."

- [ ] **Step 10: build, test, format, and look at the page**

```powershell
dotnet build BhMaps.slnx -c Debug
dotnet test BhMaps.slnx
dotnet format BhMaps.slnx
dotnet run --project src\BhMaps.App -- --game "<DEV>\game\mapArt" --library "<DEV>\lib" --appdata "<DEV>\appdata"
```

Expected: 0 warnings; the test count is the previous plus 5; and:

1. The top bar reads Maps, Backgrounds, Platforms, Packs, Settings, and at a 1000 px window it still fits beside
   the game line. Ctrl+1 to Ctrl+5 reach the five in that order; Ctrl+K still lands in the Maps search.
2. Platforms shows one row per map, the tag naming the pack whose set is in game and nothing for Default.
3. Each thumbnail is the map's platforms, cropped, over the map's current background, not a whole level with the
   platforms as slivers. The same map's tiles are all cropped identically. The zoom starts at 3.
4. A map no pack has a set for shows Default alone, or nothing at all when no Default pack has been captured.
5. Clicking a tile applies it: "flowermap platforms applied to Brawlhaven." plus the shared sentence, with Undo.
6. The menu reads "Apply to Brawlhaven", "Show files", "Open folder", and grows "Apply to the 3 selected maps"
   once three maps are ticked on Maps.
7. Searching "flower" keeps the rows with a flowermap set; an empty result reads
   "No map or pack matches 'flower'."
8. Leaving the page and coming back folds any row that was unfolded.

Captures: `<SHOTS>\t31-platforms-rows.png`, `<SHOTS>\t31-platforms-crop-vs-panel.png` (the same map's row tile
beside the map panel's whole-level tile), `<SHOTS>\t31-top-bar-five-tabs.png`.

- [ ] **Step 11: commit**

```powershell
git add src/BhMaps.Core/Imaging/PlatformBounds.cs src/BhMaps.Core/Imaging/MapCompositor.cs src/BhMaps.Core/Imaging/PreviewCache.cs tests/BhMaps.Core.Tests/PlatformBoundsTests.cs src/BhMaps.App/ViewModels/PlatformSetTileViewModel.cs src/BhMaps.App/ViewModels/Pages/PlatformsViewModel.cs src/BhMaps.App/Views/Pages/PlatformsView.xaml src/BhMaps.App/Views/Pages/PlatformsView.xaml.cs src/BhMaps.App/Theme/Controls.xaml src/BhMaps.App/App.xaml src/BhMaps.App/ViewModels/MainViewModel.cs src/BhMaps.App/Views/MainWindow.xaml
git commit -m "feat(platforms): a rows page of cropped set previews, and the fifth tab" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

### Task 32: the Packs list gets a face

**Files:**
- Create: `src\BhMaps.App\ViewModels\PackPreviewTileViewModel.cs`
- Rewrite: `src\BhMaps.App\ViewModels\PackRowViewModel.cs` (37 lines today)
- Modify: `src\BhMaps.App\ViewModels\Pages\PacksViewModel.cs` (192 lines today)
- Modify: `src\BhMaps.App\Views\Controls\RowRealiser.cs` (the pack row learns to realise)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (the `PictureTile` style moves in)
- Modify: `src\BhMaps.App\Views\Pages\MapsView.xaml` (its local `PictureTile` copy goes)
- Rewrite: `src\BhMaps.App\Views\Pages\PacksView.xaml` (173 lines today)

**Interfaces:**

Consumes, from Tasks 29 to 31 and from HEAD:

```csharp
// Task 29
public sealed class ThumbnailCache { public Task<ImageSource?> GetAsync(string fullPath, CancellationToken ct); }
// AppServices.RowThumbnails is one of these, cleared by RescanAsync

// Task 30
public sealed class StripPanel : Panel
{
    public const double MoreWidth = 52;
    public bool IsUnfolded { get; set; }
    public double Spacing { get; set; }     // 8 by default
    public int Overflow { get; }            // written with SetCurrentValue, BindsTwoWayByDefault
}
public static class RowRealiser
{
    public static void SetRealise(DependencyObject element, bool value);
    public static bool GetRealise(DependencyObject element);
}
public static class TileMenus
{
    public static void SetOpensMenu(DependencyObject element, bool value);
}

// HEAD
public sealed record Pack(string Name, string FullPath, IReadOnlyList<GameFolder> Folders)
{
    public GameFolder? FindFolder(string name);
    public int FileCount { get; }
    public IReadOnlyList<string> RelativePaths { get; }
}
public sealed record GameFile(string Name, string FullPath, long Length, long MtimeTicks);
public sealed record MapEntry(...) { public string FolderName { get; } public string DisplayName { get; } public LevelDesc BaseLevel { get; } }
public sealed class MapCatalog { public IReadOnlyList<MapEntry> Maps { get; } public bool HasLevelData { get; } }
public sealed record ScanSnapshot(MapCatalog Catalog, IReadOnlyList<Pack> Packs, ...);
public sealed class AssetSources { public AssetSources(string mapArtPath, string? packRoot = null, string? backgroundOverride = null); }
public sealed class PreviewCache
{
    public Task<string> GetOrRenderAsync(LevelDesc level, int width, int height, AssetSources sources,
        CancellationToken ct = default, CameraBounds? viewport = null);
}
public sealed class ThumbnailProvider { public static GameFile? PickRepresentative(GameFolder folder); }
public sealed record TileMenuCommand(string Text, ICommand Command);
// Theme keys: CardBorder, PrimaryButton, OutlineButton, PlainButton, TileMenu (ContextMenu, x:Shared="False"),
// Icon.Dots, Icon.Download, Icon.Plus, Icon.Refresh, Icon.FolderOpen, TileBrush, Text2Brush, Radius
```

Produces:

```csharp
// BhMaps.App\ViewModels\PackPreviewTileViewModel.cs
public sealed partial class PackPreviewTileViewModel : ObservableObject
{
    public PackPreviewTileViewModel(MapEntry map);
    public MapEntry Map { get; }
    public string DisplayName { get; }
    public partial ImageSource? Preview { get; set; }
}

// BhMaps.App\ViewModels\PackRowViewModel.cs
public sealed partial class PackRowViewModel : ObservableObject
{
    public const double LeadWidth = 128;
    public const double LeadHeight = 72;
    public const double PreviewWidth = 96;
    public const double PreviewHeight = 54;
    public const int ComposeWidth = 256;
    public const int ComposeHeight = 144;
    public const int MaxPreviews = 16;
    public PackRowViewModel(Pack pack, IReadOnlyList<MapEntry> maps);
    public Pack Pack { get; }
    public string Name { get; }
    public IReadOnlyList<MapEntry> Maps { get; }
    public int MapCount { get; }
    public int BackgroundCount { get; }
    public string CountsText { get; }
    public ObservableCollection<PackPreviewTileViewModel> Previews { get; }
    public IReadOnlyList<TileMenuCommand> MenuItems { get; }
    public partial ImageSource? Lead { get; set; }
    public partial int Overflow { get; set; }
    public string MoreText { get; }
    public bool ShowMore { get; }
    public void SetMenu(IReadOnlyList<TileMenuCommand> items);
    public bool BeginRealise();
    public static string Plural(int count, string noun);
}

// BhMaps.App\ViewModels\Pages\PacksViewModel.cs
public const string EmptyText = "No packs in the library.";
public void RealiseRow(PackRowViewModel row);
```

- [ ] **Step 1: the preview tile**

Create `src\BhMaps.App\ViewModels\PackPreviewTileViewModel.cs`:

```csharp
using System.Windows.Media;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One map in a Packs row's preview strip (addendum E): the pack's art for that map put together, drawn
/// at 96 x 54. No commands and no menu: the whole row is one click that opens the pack, and the strip is there to
/// say what is in it, not to be operated.</summary>
public sealed partial class PackPreviewTileViewModel : ObservableObject
{
    public PackPreviewTileViewModel(MapEntry map)
    {
        Map = map;
    }

    public MapEntry Map { get; }

    /// <summary>The tile's tooltip and its automation name. There is no caption: at 96 px wide a map name would
    /// be three letters and an ellipsis.</summary>
    public string DisplayName => Map.DisplayName;

    /// <summary>Null until the composite is ready. Always frozen, because it is drawn off the UI thread.</summary>
    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }
}
```

- [ ] **Step 2: the row**

Rewrite `src\BhMaps.App\ViewModels\PackRowViewModel.cs`, whole file:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the Packs list (addendum E): the pack's face as well as its name. A lead thumbnail of its
/// first map put together, the counts, a strip of the maps it touches, and the dots menu that took three of the
/// row's four buttons (q7). An ObservableObject now, because the pictures land after the row is built.</summary>
public sealed partial class PackRowViewModel : ObservableObject
{
    /// <summary>Addendum E's lead thumbnail, in device independent units because the markup sets Width and
    /// Height from them.</summary>
    public const double LeadWidth = 128;

    public const double LeadHeight = 72;

    /// <summary>Addendum E's strip tile.</summary>
    public const double PreviewWidth = 96;

    public const double PreviewHeight = 54;

    /// <summary>The size both are composed at, in pixels. Twice the lead and exactly 16:9, so the lead and the
    /// strip's first tile are one render and one decode rather than two, and both are still sharp on a high DPI
    /// screen.</summary>
    public const int ComposeWidth = 256;

    public const int ComposeHeight = 144;

    /// <summary>How many strip tiles a row builds at most. At 96 px plus the 8 px gap a 1920 px window fits about
    /// twelve, so sixteen covers the widest window with room to spare, and a pack that touches 67 maps costs
    /// sixteen elements per row rather than 67. <see cref="MoreText" /> counts the maps that were given no tile
    /// as well as the ones the strip had no room for, so the number the row shows is the true remainder.</summary>
    public const int MaxPreviews = 16;

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    private bool _realised;

    public PackRowViewModel(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        Pack = pack;
        Maps = maps;
        MapCount = pack.Folders.Count(f => !f.Name.Equals(BackgroundsFolder, StringComparison.OrdinalIgnoreCase));
        BackgroundCount = pack.FindFolder(BackgroundsFolder)?.Files.Count ?? 0;
        CountsText = $"{Plural(MapCount, "map")}, {Plural(BackgroundCount, "background")}";
        Previews = [.. maps.Take(MaxPreviews).Select(map => new PackPreviewTileViewModel(map))];
        MenuItems = [];
    }

    public Pack Pack { get; }

    public string Name => Pack.Name;

    /// <summary>The catalog maps the pack has files for, in catalog order. The strip's order, and the first of
    /// them is the lead.</summary>
    public IReadOnlyList<MapEntry> Maps { get; }

    /// <summary>The pack's folders other than Backgrounds.</summary>
    public int MapCount { get; }

    /// <summary>Files in the pack's Backgrounds folder.</summary>
    public int BackgroundCount { get; }

    /// <summary>The line under the name, as in "1 map, 3 backgrounds".</summary>
    public string CountsText { get; }

    /// <summary>The strip, capped at <see cref="MaxPreviews" />. Filled in when the row comes on screen.</summary>
    public ObservableCollection<PackPreviewTileViewModel> Previews { get; }

    /// <summary>The dots menu (addendum E, q7). Set by the page straight after construction, because each line
    /// carries this row as its command parameter and the row does not exist until its constructor has run.</summary>
    public IReadOnlyList<TileMenuCommand> MenuItems { get; private set; }

    /// <summary>The pack's first map, put together. Null until the row is realised, and null afterwards for a
    /// pack with nothing to draw, which leaves the tile colour showing.</summary>
    [ObservableProperty]
    public partial ImageSource? Lead { get; set; }

    /// <summary>How many of the strip's tiles the row had no width for. Written by StripPanel through a
    /// OneWayToSource binding in the row template.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    [NotifyPropertyChangedFor(nameof(ShowMore))]
    public partial int Overflow { get; set; }

    /// <summary>Addendum E's "+58": every map the strip is not showing, whether it was cut by the width or never
    /// given a tile.</summary>
    public string MoreText => $"+{Remaining}";

    public bool ShowMore => Remaining > 0;

    private int Remaining => Math.Max(0, Maps.Count - Math.Max(0, Previews.Count - Overflow));

    public void SetMenu(IReadOnlyList<TileMenuCommand> items) => MenuItems = items;

    /// <summary>True the first time it is called and false ever after, so the row reads its files once. The
    /// row template's Loaded fires again every time the list scrolls it back into view.</summary>
    public bool BeginRealise()
    {
        if (_realised)
        {
            return false;
        }

        _realised = true;
        return true;
    }

    /// <summary>"1 map" but "0 maps" and "3 maps". Shared with the page's confirm text, which counts files.</summary>
    public static string Plural(int count, string noun) => count == 1 ? $"{count} {noun}" : $"{count} {noun}s";
}
```

- [ ] **Step 3: the page builds the rows, their menus and their pictures [q7]**

In `src\BhMaps.App\ViewModels\Pages\PacksViewModel.cs`, the using block gains three lines:

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
```

Replace the constructor, `Refresh` and `MapsTouched`, and add the loaders. Everything else in the file is
untouched:

```csharp
    /// <summary>Addendum E: the empty library's line. "No packs yet." was the v2 wording and said less.</summary>
    public const string EmptyText = "No packs in the library.";

    /// <summary>The folder a pack keeps its background images in. Every other folder is a map.</summary>
    private const string BackgroundsFolder = "Backgrounds";

    /// <summary>What a background is: the game ships every one of its background slots as a JPEG.</summary>
    private const string BackgroundExtension = ".jpg";

    /// <summary>The snapshot the rows were built from, which the composites are drawn against.</summary>
    private ScanSnapshot? _snapshot;

    /// <summary>Cancels the loads the last scan's rows started. Replaced, never disposed, exactly as
    /// PackDetailViewModel does, because those loads still hold the token.</summary>
    private CancellationTokenSource? _cts;

    public PacksViewModel(MainViewModel shell)
        : base(shell)
    {
        Rows = [];
    }

    public override void Refresh(ScanSnapshot snapshot)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _snapshot = snapshot;
        Rows.Clear();
        foreach (var pack in snapshot.Packs)
        {
            var row = new PackRowViewModel(pack, MapsIn(snapshot.Catalog, pack));
            row.SetMenu(BuildMenu(row));
            Rows.Add(row);
        }

        IsEmpty = Rows.Count == 0;
    }

    /// <summary>Addendum E: a row reads its files when it comes on screen and not before, so a library of forty
    /// packs does not compose forty leads to show six. Called from the row template's Loaded through
    /// RowRealiser; the row itself refuses every call after the first, so scrolling back costs nothing.</summary>
    public void RealiseRow(PackRowViewModel row)
    {
        if (_cts is { } cts && row.BeginRealise())
        {
            Load(row, cts.Token);
        }
    }

    /// <summary>The lead, then the strip left to right, one at a time, so the single render thread works down
    /// the list in the order the eye does. Fire and forget, like PackDetailViewModel.Load.</summary>
    private async void Load(PackRowViewModel row, CancellationToken ct)
    {
        try
        {
            row.Lead = await LeadAsync(row, ct);
            foreach (var tile in row.Previews)
            {
                ct.ThrowIfCancellationRequested();
                tile.Preview = await ComposeAsync(row.Pack, tile.Map, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a rescan.
        }
    }

    /// <summary>Addendum E's lead: the pack's first map put together. A pack with no map folders has no
    /// composite to draw, so its first background stands in; a pack with neither leaves the tile colour showing,
    /// which is what an empty slot looks like everywhere else in the app.</summary>
    private async Task<ImageSource?> LeadAsync(PackRowViewModel row, CancellationToken ct)
    {
        if (row.Maps.Count > 0)
        {
            return await ComposeAsync(row.Pack, row.Maps[0], ct);
        }

        var file = (row.Pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
            .FirstOrDefault(f => Path.GetExtension(f.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase));
        return file is null ? null : await Shell.Services.RowThumbnails.GetAsync(file.FullPath, ct);
    }

    /// <summary>One map with the pack's own files over it, through the preview cache so the render is queued on
    /// the one STA thread and kept on disk, and then through the shared decode cache so the same file never
    /// decodes twice. AssetSources takes the pack second, which means the pack's background when it ships one
    /// and the game's otherwise: what the pack looks like in game (addendum E). A composite that cannot be drawn
    /// falls back to a plain file tile; it is a picture, never an error dialog.</summary>
    private async Task<ImageSource?> ComposeAsync(Pack pack, MapEntry map, CancellationToken ct)
    {
        var previews = Shell.Services.Previews;
        var thumbnails = Shell.Services.RowThumbnails;
        var gamePath = Shell.Services.GamePath;
        ImageSource? image = null;

        // Spec 3.6: with no level data there are no camera bounds and no platform tree, so there is nothing to
        // compose, and the pack's own art is the only picture of the map there is.
        if (_snapshot?.Catalog.HasLevelData == true)
        {
            try
            {
                var sources = new AssetSources(gamePath, pack.FullPath);
                image = await Task.Run(
                    async () =>
                    {
                        var path = await previews
                            .GetOrRenderAsync(
                                map.BaseLevel, PackRowViewModel.ComposeWidth, PackRowViewModel.ComposeHeight,
                                sources, ct)
                            .ConfigureAwait(false);
                        return await thumbnails.GetAsync(path, ct).ConfigureAwait(false);
                    },
                    ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                          or FileFormatException or ArgumentException)
            {
                image = null;
            }
        }

        if (image is null
            && pack.FindFolder(map.FolderName) is { } folder
            && ThumbnailProvider.PickRepresentative(folder) is { } file)
        {
            image = await thumbnails.GetAsync(file.FullPath, ct);
        }

        return image;
    }

    /// <summary>The row's dots menu (addendum E, q7). Each line wraps the page's own command with this row as
    /// its parameter, because a TileMenuCommand carries no parameter of its own. Remove is last: it is the
    /// destructive one, and the pointer should not have to pass over it to reach another line.</summary>
    private IReadOnlyList<TileMenuCommand> BuildMenu(PackRowViewModel row) =>
    [
        new TileMenuCommand("Export", new RelayCommand(() => ExportCommand.Execute(row))),
        new TileMenuCommand("Open folder", new RelayCommand(() => OpenFolderCommand.Execute(row))),
        new TileMenuCommand("Remove", new RelayCommand(() => RemoveCommand.Execute(row))),
    ];

    /// <summary>The maps the pack touches: the catalog maps it has at least one file for, in catalog order. A
    /// pack folder that is not a map, such as a theme folder other maps borrow from (spec 4), is not one of
    /// them, and neither is Backgrounds. The same rule pack detail uses, so a row's strip and the pack's own
    /// page show the same maps in the same order.</summary>
    private static IReadOnlyList<MapEntry> MapsIn(MapCatalog catalog, Pack pack) =>
        [.. catalog.Maps.Where(map => pack.FindFolder(map.FolderName) is { Files.Count: > 0 })];

    /// <summary>Display names of the maps the pack writes into, for the confirm that names them.</summary>
    private IReadOnlyList<string> MapsTouched(Pack pack) =>
        Shell.Snapshot is { } snapshot
            ? [.. MapsIn(snapshot.Catalog, pack).Select(map => map.DisplayName)]
            : Array.Empty<string>();
```

- [ ] **Step 4: the realiser learns the second kind of row**

In `src\BhMaps.App\Views\Controls\RowRealiser.cs`, replace `OnLoaded` and `FindPage` with one pair that serves
both lists. The file's using block gains `using BhMaps.App.ViewModels.Pages;` if it is not there already:

```csharp
    /// <summary>Loaded fires again every time a recycled container comes back on screen; both row types ignore
    /// every call after the first, so scrolling up and down costs one read of each file and no more.</summary>
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.DataContext)
        {
            case MapRowViewModel row when Find<RowsPageViewModel>(element) is { } page:
                page.RealiseRow(row);
                break;
            case PackRowViewModel pack when Find<PacksViewModel>(element) is { } packs:
                packs.RealiseRow(pack);
                break;
        }
    }

    /// <summary>The nearest ancestor whose DataContext is that page. The rows page and the Packs page are both
    /// bound as the view's DataContext, so the walk ends at the UserControl.</summary>
    private static T? Find<T>(DependencyObject start)
        where T : class
    {
        for (DependencyObject? d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement element && element.DataContext is T found)
            {
                return found;
            }
        }

        return null;
    }
```

- [ ] **Step 5: one PictureTile in the theme**

Add to `src\BhMaps.App\Theme\Controls.xaml`, after the `TileAction` style:

```xml
  <!-- The ground under a picture, wherever one is drawn: Radius 6 everywhere, so a preview is an ImageBrush on a
       Border rather than an Image with square corners, and the tile colour is what an empty slot shows while the
       file is still decoding. Lifted out of MapsView, which had the only copy, because the Packs rows draw
       pictures too. -->
  <Style x:Key="PictureTile" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource TileBrush}" />
    <Setter Property="CornerRadius" Value="{StaticResource Radius}" />
  </Style>
```

and delete the identical local copy from `src\BhMaps.App\Views\Pages\MapsView.xaml`, comment included:

```xml
    <!-- Radius 6 everywhere, so the picture is an ImageBrush on a Border rather than an Image with square corners.
         The tile colour behind it is what an empty slot shows. -->
    <Style x:Key="PictureTile" TargetType="Border">
      <Setter Property="Background" Value="{StaticResource TileBrush}" />
      <Setter Property="CornerRadius" Value="{StaticResource Radius}" />
    </Style>
```

Every `{StaticResource PictureTile}` in `MapsView.xaml` now resolves from the theme instead, which is the same
two setters, so nothing about the page changes.

- [ ] **Step 6: the page's markup [q7]**

Rewrite `src\BhMaps.App\Views\Pages\PacksView.xaml`, whole file:

```xml
<UserControl x:Class="BhMaps.App.Views.Pages.PacksView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"
             xmlns:vm="clr-namespace:BhMaps.App.ViewModels">
  <UserControl.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />

    <!-- The clickable body of a row: the lead, the name and the strip, so a click anywhere across them opens the
         pack and a click on an action does only that action. Page-only, like the sidebar's own row styles.
         Keyboard focus draws the same 60% ring the tiles wear, because focus has to be visible here too
         (spec 7.8). The presenter stretches, so the strip's star column is the width the row has left rather
         than the width the tiles want. -->
    <Style x:Key="PackRowBody" TargetType="Button">
      <Setter Property="Background" Value="Transparent" />
      <Setter Property="Padding" Value="14,12" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="FocusVisualStyle" Value="{x:Null}" />
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Grid>
              <Border x:Name="Chrome"
                      Padding="{TemplateBinding Padding}"
                      Background="{TemplateBinding Background}"
                      CornerRadius="{StaticResource Radius}">
                <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center" />
              </Border>
              <Border x:Name="Ring"
                      BorderBrush="{StaticResource TextBrush}"
                      BorderThickness="1"
                      CornerRadius="{StaticResource Radius}"
                      IsHitTestVisible="False"
                      Opacity="0" />
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Chrome" Property="Background" Value="{StaticResource Surface2Brush}" />
              </Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter TargetName="Ring" Property="Opacity" Value="0.6" />
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter TargetName="Chrome" Property="Opacity" Value="0.4" />
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- One map of the pack, 96 x 54, no caption. The tooltip is the map name (addendum E). -->
    <DataTemplate DataType="{x:Type vm:PackPreviewTileViewModel}">
      <Border Width="{x:Static vm:PackRowViewModel.PreviewWidth}"
              Height="{x:Static vm:PackRowViewModel.PreviewHeight}"
              AutomationProperties.Name="{Binding DisplayName}"
              Style="{StaticResource PictureTile}"
              ToolTip="{Binding DisplayName}">
        <Border CornerRadius="{StaticResource Radius}" RenderOptions.BitmapScalingMode="HighQuality">
          <Border.Background>
            <ImageBrush ImageSource="{Binding Preview}" Stretch="UniformToFill" />
          </Border.Background>
        </Border>
      </Border>
    </DataTemplate>
  </UserControl.Resources>

  <DockPanel Margin="24,16,24,24">
    <controls:PageHeader DockPanel.Dock="Top" Title="Packs">
      <controls:PageHeader.Actions>
        <!-- Disabled while the shell is busy, the same hop past the page to the window's MainViewModel the header
             itself makes (decision D11), so a second operation cannot be started from a live-looking button.
             Import and Open library are library-only and stop there; Capture defaults reads the game folder, so
             it takes CanWrite on top, which WPF ands with this one (spec 7.8). -->
        <StackPanel IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}" Orientation="Horizontal">
          <Button Command="{Binding ImportCommand}" Content="Import folder" controls:Icon.Glyph="{StaticResource Icon.Plus}" Style="{StaticResource OutlineButton}" />
          <Button Margin="8,0,0,0"
                  Command="{Binding CaptureDefaultsCommand}"
                  Content="Capture defaults"
                  controls:Icon.Glyph="{StaticResource Icon.Refresh}"
                  IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                  Style="{StaticResource OutlineButton}" />
          <Button Margin="8,0,0,0"
                  Command="{Binding OpenLibraryCommand}"
                  Content="Open library"
                  controls:Icon.Glyph="{StaticResource Icon.FolderOpen}"
                  Style="{StaticResource PlainButton}" />
        </StackPanel>
      </controls:PageHeader.Actions>
    </controls:PageHeader>

    <Grid IsEnabled="{Binding DataContext.IsNotBusy, RelativeSource={RelativeSource AncestorType=Window}}">
      <ScrollViewer Focusable="False" VerticalScrollBarVisibility="Auto">
        <ItemsControl ItemsSource="{Binding Rows}">
          <ItemsControl.ItemContainerStyle>
            <!-- Without this a screen reader announces each row as its view model's type name, and the two
                 identically labelled action controls inside it have nothing to say which pack they belong to. -->
            <Style TargetType="ContentPresenter">
              <Setter Property="AutomationProperties.Name" Value="{Binding Name}" />
            </Style>
          </ItemsControl.ItemContainerStyle>
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <!-- The menu is the row's own instance, because TileMenu is x:Shared="False", and it inherits this
                   Border's DataContext, which is the row (q7). Realise is what starts the row's pictures. -->
              <Border Margin="0,0,0,8"
                      ContextMenu="{StaticResource TileMenu}"
                      controls:RowRealiser.Realise="True"
                      Style="{StaticResource CardBorder}">
                <Grid>
                  <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                  </Grid.ColumnDefinitions>

                  <!-- The row's commands live on the page, so each control reaches past its own row to the
                       list's DataContext and hands its row back as the parameter. -->
                  <Button Grid.Column="0"
                          AutomationProperties.Name="{Binding Name, StringFormat=Open pack {0}}"
                          Command="{Binding DataContext.OpenCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                          CommandParameter="{Binding}"
                          Style="{StaticResource PackRowBody}">
                    <Grid>
                      <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="190" />
                        <ColumnDefinition Width="*" />
                      </Grid.ColumnDefinitions>

                      <!-- Addendum E: the pack's first map put together, 128 x 72. -->
                      <Border Grid.Column="0"
                              Width="{x:Static vm:PackRowViewModel.LeadWidth}"
                              Height="{x:Static vm:PackRowViewModel.LeadHeight}"
                              Style="{StaticResource PictureTile}">
                        <Border CornerRadius="{StaticResource Radius}" RenderOptions.BitmapScalingMode="HighQuality">
                          <Border.Background>
                            <ImageBrush ImageSource="{Binding Lead}" Stretch="UniformToFill" />
                          </Border.Background>
                        </Border>
                      </Border>

                      <StackPanel Grid.Column="1" Margin="14,0,12,0" VerticalAlignment="Center">
                        <TextBlock FontWeight="Medium" Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                        <TextBlock Margin="0,3,0,0" Foreground="{StaticResource Text2Brush}" Text="{Binding CountsText}" />
                      </StackPanel>

                      <!-- The strip and its count share one star column: a count in an Auto column of its own
                           would narrow the strip, which would change the count, which would change the column.
                           StripPanel keeps its own MoreWidth clear at the right end instead. -->
                      <Grid Grid.Column="2" VerticalAlignment="Center">
                        <ItemsControl ItemsSource="{Binding Previews}">
                          <ItemsControl.Style>
                            <Style TargetType="ItemsControl">
                              <Style.Triggers>
                                <!-- Addendum E's fade at the right edge, and only while something is cut off. An
                                     opacity mask reads alpha alone, so the two stops are not theme colours and
                                     no palette entry is added by them. -->
                                <DataTrigger Binding="{Binding ShowMore}" Value="True">
                                  <Setter Property="OpacityMask">
                                    <Setter.Value>
                                      <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                        <GradientStop Offset="0.84" Color="#FF000000" />
                                        <GradientStop Offset="1" Color="#00000000" />
                                      </LinearGradientBrush>
                                    </Setter.Value>
                                  </Setter>
                                </DataTrigger>
                              </Style.Triggers>
                            </Style>
                          </ItemsControl.Style>
                          <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                              <controls:StripPanel Overflow="{Binding Overflow, Mode=OneWayToSource}" Spacing="8" />
                            </ItemsPanelTemplate>
                          </ItemsControl.ItemsPanel>
                        </ItemsControl>

                        <TextBlock HorizontalAlignment="Right"
                                   VerticalAlignment="Center"
                                   Foreground="{StaticResource Text2Brush}"
                                   Text="{Binding MoreText}"
                                   Visibility="{Binding ShowMore, Converter={StaticResource BoolToVis}}" />
                      </Grid>
                    </Grid>
                  </Button>

                  <StackPanel Grid.Column="1"
                              Margin="8,0,10,0"
                              VerticalAlignment="Center"
                              Orientation="Horizontal">
                    <!-- The one action in the row that writes into the game, so the only one gated on CanWrite;
                         Export, Open folder and Remove are the library's own and now live in the menu (q7). -->
                    <Button Command="{Binding DataContext.ApplyAllCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                            CommandParameter="{Binding}"
                            Content="Apply all"
                            controls:Icon.Glyph="{StaticResource Icon.Download}"
                            IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                            Style="{StaticResource OutlineButton}" />
                    <Button Margin="6,0,0,0"
                            AutomationProperties.Name="{Binding Name, StringFormat=More actions for {0}}"
                            controls:Icon.Glyph="{StaticResource Icon.Dots}"
                            controls:TileMenus.OpensMenu="True"
                            Style="{StaticResource PlainButton}"
                            ToolTip="More actions" />
                  </StackPanel>
                </Grid>
              </Border>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>

      <!-- Empty library (addendum E): the line and Import folder on one line, with Capture defaults beside it,
           because an empty library on a machine that has the game is one click from being a full one. -->
      <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Orientation="Horizontal">
        <StackPanel.Style>
          <Style TargetType="StackPanel">
            <Setter Property="Visibility" Value="Collapsed" />
            <Style.Triggers>
              <DataTrigger Binding="{Binding IsEmpty}" Value="True">
                <Setter Property="Visibility" Value="Visible" />
              </DataTrigger>
            </Style.Triggers>
          </Style>
        </StackPanel.Style>
        <TextBlock VerticalAlignment="Center"
                   Foreground="{StaticResource Text2Brush}"
                   Text="{x:Static pages:PacksViewModel.EmptyText}" />
        <Button Margin="12,0,0,0"
                Command="{Binding ImportCommand}"
                Content="Import folder"
                controls:Icon.Glyph="{StaticResource Icon.Plus}"
                Style="{StaticResource PrimaryButton}" />
        <Button Margin="8,0,0,0"
                Command="{Binding CaptureDefaultsCommand}"
                Content="Capture defaults"
                controls:Icon.Glyph="{StaticResource Icon.Refresh}"
                IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}"
                Style="{StaticResource OutlineButton}" />
      </StackPanel>
    </Grid>
  </DockPanel>
</UserControl>
```

The root element needs one more prefix for that last `x:Static`:

```xml
             xmlns:pages="clr-namespace:BhMaps.App.ViewModels.Pages"
```

- [ ] **Step 7: build, test, format, and look at the list**

```powershell
dotnet build BhMaps.slnx -c Debug
dotnet test BhMaps.slnx
dotnet format BhMaps.slnx
dotnet run --project src\BhMaps.App -- --game "<DEV>\game\mapArt" --library "<DEV>\lib" --appdata "<DEV>\appdata"
```

Expected: 0 warnings, the test count unchanged from Task 31, and:

1. Each pack row leads with a 128 x 72 composite of its first map, not a blank tile and not the raw PNG.
2. The strip fills the middle of the row with 96 x 54 previews, fading at the right edge, with "+N" over the
   fade naming every map the row is not showing.
3. Widening the window adds previews and lowers the count; narrowing removes them and raises it. The count never
   flickers between two values while dragging the edge.
4. A background-only pack shows its first background as the lead and an empty strip.
5. The row carries "Apply all" and a dots button; Export, Open folder and Remove are in the menu, in that order,
   and each acts on that row's pack. Right-clicking the row opens the same menu.
6. Clicking anywhere on the lead, the name or the strip opens pack detail. Tab reaches the row body, then Apply
   all, then the dots; the focus ring is visible on each.
7. Scrolling to the end of a long list and back does not re-decode: the pictures are there at once the second
   time.
8. An empty library reads "No packs in the library." with Import folder and Capture defaults on the same line.
9. F5 rescans and the rows come back with their pictures.

Captures: `<SHOTS>\t32-packs-list.png`, `<SHOTS>\t32-packs-row-menu.png`, `<SHOTS>\t32-packs-empty.png`.

- [ ] **Step 8: commit**

```powershell
git add src/BhMaps.App/ViewModels/PackPreviewTileViewModel.cs src/BhMaps.App/ViewModels/PackRowViewModel.cs src/BhMaps.App/ViewModels/Pages/PacksViewModel.cs src/BhMaps.App/Views/Controls/RowRealiser.cs src/BhMaps.App/Theme/Controls.xaml src/BhMaps.App/Views/Pages/MapsView.xaml src/BhMaps.App/Views/Pages/PacksView.xaml
git commit -m "feat(packs): composed lead thumbnail, preview strip and a row menu" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

### Task 33: the amendments to the 2.1 plan

This task changes `docs\superpowers\plans\2026-09-11-bhmaps-v2-1.md`, which Tasks 1 to 28 are executed from, so
that the five tasks the addendum supersedes say what they now mean. Nothing else in that file moves, and no task
is renumbered. One code change rides along, because the string it fixes is already in the app.

**Files:**
- Modify: `docs\superpowers\plans\2026-09-11-bhmaps-v2-1.md` (Tasks 19, 25, 26, 27 and 28)

**Interfaces:** none. This task adds no type and changes no signature. The one code change deletes a property:

```csharp
// Gone from MapsViewModel: the button no longer names the count (addendum G, owner change O7).
public string SelectAllShownText { get; }
```

- [ ] **Step 1: Task 19's table learns the two new strings**

In Task 19, Step 1, replace the `Select-String` line with:

```powershell
Select-String -Path src\BhMaps.App\**\*.xaml,src\BhMaps.App\**\*.cs -Pattern "Add picture","Add pictures","\bUse\b","Reset to default \(this map\)","Search backgrounds","Nothing to apply to","No backgrounds","Select all shown"
```

and add two rows to the end of the table, before the paragraph that begins "AddPicturesViewModel":

```markdown
| "Select all shown" / "Select all 12 shown" (chip row) | "Select all", with the tooltip "Ticks the maps the chips and search show" | `MapsView.xaml`, `MapsViewModel.cs` |
| "Select all shown" (selection bar) | "Select all", same tooltip | `MapsView.xaml` |
```

Then add this paragraph after the table:

```markdown
The two "Select all" rows are owner change O7 in the rows addendum, section G: "it should be implied to the user
that the select all will only select the things shown". The count moves to the tooltip and to the line the
selection bar already shows, "12 of 67 maps selected", which is what confirms what the click did. The rows pages
have no such button at all (addendum B, q4), so this sweep touches the Maps page only.
```

- [x] **Step 2: make O7 true in the app: already landed**

Owner change O7 landed at 5a6163d (chip row and selection bar say "Select all"; `SelectAllShownText` deleted),
O8 at e533c5d (every user-visible "ticked" is "selected") and O9 at a20e2c0 (the bar floats on the page gutter),
all on 2026-09-11 before this plan ran. This task changes no code; its edits are to the base plan's text only.

- [ ] **Step 3: Task 25 loses the segment [q6]**

In Task 25's **Interfaces** block, delete these ten lines:

```csharp
public const string CombinedSegment = "Combined";
public const string BackgroundsSegment = "Backgrounds";
public const string PlatformsSegment = "Platforms";
public IReadOnlyList<string> Segments { get; }                       // the three above, in order
public partial string Segment { get; set; }                          // observable, defaults to Combined
public bool IsCombined { get; set; }                                 // and IsBackgrounds, IsPlatforms
public ObservableCollection<PackTileViewModel> Combined { get; }      // was PutTogether
public ObservableCollection<PackTileViewModel> Backgrounds { get; }
public ObservableCollection<PackTileViewModel> Platforms { get; }
public IEnumerable<PackTileViewModel> Items { get; }                 // the collection the segment picks
```

and put one line in their place:

```csharp
public ObservableCollection<PackTileViewModel> Items { get; }        // was PutTogether; the only grid there is
```

In the `PackTileViewModel` constructor in the same block, drop the last parameter, so it reads:

```csharp
    public PackTileViewModel(string key, string caption, MapEntry? map, GameFile? file,
        int composeWidth, int composeHeight);
```

and delete its `public bool DropBackground { get; }` line. `PlatformWidth` and `PlatformHeight` go with it: no
grid renders a level with its backgrounds dropped any more.

Replace Task 25 Step 1 in full with:

```markdown
- [ ] **Step 1: one grid and the zoom.** In `PackDetailViewModel`, replace the three old collections with one and
add the zoom. There is no segment: addendum F, q6 says Combined is the only view and the drawer lists the halves,
so the page has one grid and the three-way control is gone.

    public PackDetailViewModel(MainViewModel shell)
        : base(shell)
    {
        Items = [];
        TransparentText = "";
        Zoom = shell.Services.Settings.PackZoom;
    }

    /// <summary>Every map the pack touches, composed with the pack's own files over the background the pack ships
    /// for it, or the game's when it ships none (spec 5, addendum F).</summary>
    public ObservableCollection<PackTileViewModel> Items { get; }

    /// <summary>What the grid says when the pack has nothing to show.</summary>
    public string EmptyNote => NoMapsText;
```

`Zoom` keeps the `[ObservableProperty]` and the `packZoom` persistence exactly as the task already writes them.
```

Replace Task 25 Step 3's `Rebuild` body between `_transparentFiles = Array.Empty<string>();` and the
`OnPropertyChanged(nameof(Items));` line with:

```csharp
        foreach (var map in MapsIn(snapshot.Catalog, pack))
        {
            Items.Add(new PackTileViewModel(
                map.FolderName, map.DisplayName, map, null, MapCompositor.CardWidth, MapCompositor.CardHeight));
        }

        // .jpg only: the game's backgrounds are all JPEGs, so a PNG in the folder fills no slot and belongs in
        // the transparent-files line instead. A background whose slot a map in the grid already covers is drawn
        // by that map's own tile, so only the orphans get a tile of their own, captioned by file name.
        foreach (var file in pack.FindFolder(BackgroundsFolder)?.Files ?? Array.Empty<GameFile>())
        {
            if (!Path.GetExtension(file.Name).Equals(BackgroundExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var owner = MapForSlot(snapshot.Catalog, file.Name);
            if (owner is null)
            {
                Items.Add(new PackTileViewModel(
                    file.Name, file.Name, null, file, MapCompositor.CardWidth, MapCompositor.CardHeight));
            }
            else if (!Items.Any(t => t.Key.Equals(owner.FolderName, StringComparison.OrdinalIgnoreCase)))
            {
                Items.Add(new PackTileViewModel(
                    owner.FolderName, owner.DisplayName, owner, null,
                    MapCompositor.CardWidth, MapCompositor.CardHeight));
            }
        }
```

with `Load([.. Items], pack, _cts.Token);` in place of the three-list call, and `MapsIn` unchanged. Add this
paragraph under the code:

```markdown
The second loop is what keeps a background-only pack from opening on an empty page now that the Backgrounds
segment is gone: a pack that ships a slot some map uses adds that map to the grid, composed, and a slot no map
names still gets its own file tile. `MapForSlot` and `PackDrawerViewModel` are unchanged; the drawer already
lists the map's folder files and its background slots together, which is what "the drawer lists the halves"
means (addendum F).
```

Finally, in Task 25 Step 2, delete the `OnSegmentChanged` hook and the sentence naming it, and in Step 4 delete
`DropBackground` from `PackTileViewModel` and from its constructor.

- [ ] **Step 4: Task 26's header loses the segment [q6]**

In Task 26 Step 2, delete this block from the header markup:

```xml
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
```

and delete the paragraph that follows the markup, the one beginning "Part B's `SegmentControl` is a style for one
`ToggleButton`". In its place:

```markdown
There is no segment control on this page (addendum F, q6): the header is the back chevron, the pack name, the
zoom slider, Apply all and Open folder. `SegmentControl` stays in `Controls.xaml`, because the map panel's
Background | Platforms control is the same style and the panel is kept as built (addendum D, q5).
```

In Task 26 Step 4's checks, replace the first two bullets with:

```markdown
  - the header reads back chevron, pack name, the zoom slider, Apply all, Open folder;
  - the grid is five columns on arrival, and the zoom slider takes it to 2 and to 10;
```

and delete the bullet that reads "leaving the page and coming back keeps the segment, and restarting the app
keeps the zoom", replacing it with "restarting the app keeps the zoom".

- [ ] **Step 5: Task 27's manual and README describe the five-tab app**

In Task 27 Step 2, replace the bullet list with:

```markdown
  - The **top bar**: BhMaps, then Maps, Backgrounds, Platforms, Packs, Settings; Ctrl+1 to Ctrl+5, Ctrl+K for the
    Maps search; the game line on the right with Launch; F5 rescans, Cancel in the header stops a long operation.
  - **Maps**: the grid, the chips (All, the set chips, Changed, Selected), "Select all", zoom 2 to 10, the card tag
    rules and where the tag is drawn at each width, ticking with click, Ctrl+click, Shift+click, Space and
    Ctrl+A, the selection bar and what each of its buttons does, the map panel with its Background | Platforms
    segment and the tile menus, "Reset this map" and "Reset all to default".
  - **Backgrounds**: one row per map, the choices as thumbnails with the in-game one first and checked, custom
    pictures folded behind "Custom (N)", "+N" for a row too narrow, one click to apply, the tile menus, search
    across map, pack and file names, the chips, zoom 1 to 5, "Add Custom Image". No ticks on this page: ticking
    is the Maps page's, and the menus here offer "Apply to the N selected maps" for the maps selected there.
  - **Platforms**: the same rows for platform sets, one thumbnail per pack that has a set for that map, cropped
    to the platforms so they can be seen at row height, search across map and pack names, zoom 1 to 5.
  - **Packs** and **pack detail**: the list with its lead thumbnail, preview strip, Apply all and the row menu
    (Export, Open folder, Remove), and pack detail as one grid with its drawer.
  - **Settings**: the rows as spec 6 lists them, including the one line about applying, and no "while the game
    runs" row.
  - The windows: **Welcome**, **Add Custom Image** with its four "Then" choices, **Import folder**, and the
    background editor as spec 7.2 describes it.
  - Writes: every change is written straight into the game folder with the game open or closed and shows on the
    next match load; the done line and Undo; multi-map writes confirm with a count.
```

In the paragraph list under it, replace the Backgrounds and Platforms entries with:

```markdown
  - Lines 50 to 55, the Backgrounds paragraph: replace it wholesale. It describes one flat grid, a tick meaning
    "in use by a selected map", and a selection bar at the bottom of the page. 2.1 has one row per map, a strip
    of choices, no ticks and no bar on this page, and the check means the game is showing that picture.
  - Lines 56 to 61, the Platforms paragraph: replace it wholesale rather than deleting it. The page is back
    (addendum C) and is now rows of cropped set previews, one row per map.
```

In Task 27 Step 3, replace the sentence beginning "rewrite "What it does" for the four tabs" with:

```markdown
rewrite "What it does" for the five tabs: Maps as the grid and the map panel, Backgrounds and Platforms as a row
per map with every choice on it, Packs as import, apply, export and capture defaults, the editor, ticks and the
selection bar for group applies, and Undo.
```

In Task 27 Step 4, the second `Select-String` must stop forbidding the two names Task 31 brings back, and the
first must forbid the string O7 removed. Replace those two commands with:

```powershell
$files | Select-String -SimpleMatch -Pattern 'Put together','Add pictures','Restart and apply','Apply live','Use platform set','Select all shown'
$files | Select-String -Pattern 'HomeViewModel|HomeView\b|NavigateHome'
```

and add this sentence to the paragraph under the commands:

```markdown
`PlatformsViewModel` and `PlatformsView` are no longer forbidden names: the rows addendum brings the Platforms
tab back as the fifth tab, and Task 31 creates both types again. The gate forbids the old Home names only.
```

- [ ] **Step 6: Task 28's gate counts the new tests and the fifth tab**

In Task 28 Step 2, replace the sentence naming the count with:

```markdown
`dotnet test` -> every test passes, and the count is at least the 2.0.0 count plus the 9 in C1, the 5 in C2, the
4 in `BackgroundChoicesTests`, the 2 added `SettingsStoreTests` and the 5 in `PlatformBoundsTests`. Record the
number.
```

In Task 28 Step 4, replace the sentence with:

```markdown
Run the `Select-String` commands from C8 step 4 again, as this plan's Task 33 amended them, the
`homeZoom|whileRunning` one with its two excluded files. All four print nothing.
```

In Task 28 Step 5, add these two lines to the owner's checks:

```markdown
Confirm the top bar shows five tabs and Ctrl+1 to Ctrl+5 reach them, that Backgrounds and Platforms each show one
row per map with their pictures filled in, and that the Packs list leads each row with a composite. The live
write with Brawlhalla open is the owner's to do on their own machine; do not start the game from here.
```

- [ ] **Step 7: check the plan still reads and commit**

```powershell
Select-String -Path docs\superpowers\plans\2026-09-11-bhmaps-v2-1.md -Pattern '^### Task ' | Measure-Object
Select-String -Path docs\superpowers\plans\2026-09-11-bhmaps-v2-1*.md -Pattern '[\u2013\u2014]'
Select-String -Path docs\superpowers\plans\2026-09-11-bhmaps-v2-1.md -SimpleMatch -Pattern 'IsCombined','IsBackgrounds','IsPlatforms','DropBackground','SelectAllShownText'
dotnet build BhMaps.slnx -c Debug
dotnet test BhMaps.slnx
```

Expected: 28 task headings in the main plan, no em-dash or en-dash in either plan, nothing left naming the
segment or the deleted property, 0 warnings, every test green. Then look at the Maps page once on the dev tree:
the chip row's button reads "Select all", its tooltip reads "Ticks the maps the chips and search show", clicking
it ticks every card the chips and search show, and the selection bar's line names the count.

Capture: `<SHOTS>\t33-maps-select-all.png`.

```powershell
dotnet format BhMaps.slnx
git add docs/superpowers/specs/2026-09-11-bhmaps-v2-1-rows-addendum.md docs/superpowers/plans/2026-09-11-bhmaps-v2-1-rows.md docs/superpowers/plans/2026-09-11-bhmaps-v2-1.md src/BhMaps.App/ViewModels/Pages/MapsViewModel.cs src/BhMaps.App/Views/Pages/MapsView.xaml
git commit -m "docs(plan): rows addendum, and amend the five tasks it supersedes" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`nClaude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg"
```

---

## Risks

- **The two rows pages share one base class and one row template.** A change to `RowsPageViewModel` or to the
  `MapRow` template lands on both pages at once. That is the point of Task 30's shape, and it is also the way to
  break Platforms while working on Backgrounds. Run both pages after either file changes.
- **Overflow is a measured value fed back into the view model.** `StripPanel` writes it during measure through
  `SetCurrentValue`, and the row's "+N" reads it. Writing it with `SetValue`, or binding it two ways from the
  view model's side, would clear the binding and freeze the count. If a "+N" ever stops changing with the window
  width, that is the first thing to look at.
- **A cropped platform preview and a whole-level one are the same files at the same size.** They are kept apart
  only by the viewport in `PreviewCache.KeyFor`. If that parameter is ever dropped from the key, the map panel
  and the Platforms rows will start handing each other the wrong picture out of the cache, silently. The fix
  would be to clear `<appdata>\previews`, but the bug would come straight back.
- **One decode cache for every row.** `ThumbnailCache` shares one task per path, so a row that scrolls away while
  a decode is running stops waiting without cancelling it for the rows that are still on screen. It is cleared on
  rescan; nothing else clears it, so a very long session with a very large library holds every thumbnail it has
  ever drawn. Sixty-seven maps times a handful of packs at 128 px is a few tens of megabytes, which is why it is
  left alone; a library ten times that size is the case to measure.
- **Nothing in Tasks 29 to 33 writes to the game folder.** Every apply on a rows page is
  `MainViewModel.ApplyPictureAsync` or `ApplySetAsync`, which are `RunGameWriteAsync` calls. If a new write path
  ever appears in a rows page, the undo snapshot and the busy boundary are both gone, and no test will say so.
- **Not verified while planning:** the exact per-pack counts in Task 32's acceptance, and how the fade reads on
  the owner's display. The numbers in the checks are the dev tree's; treat the shapes as the assertion.
