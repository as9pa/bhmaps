# BhMaps 2.1 Implementation Plan, Part A: the top bar, the Maps page, ticks, and live writes

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (or
> `superpowers:executing-plans`) to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for
> tracking. Do not reorder tasks: each one leaves the app building and running, and several depend on the one
> before.

**Goal:** Replace the sidebar with a 44 px top bar of four tabs, turn Home into Maps with a tickable grid and a
floating selection bar, make every write land live in the game folder with one shared "when it shows" sentence,
and fix the two capture bugs (chips ignore a mouse click, typed text starts about 35 px right of its padding).

**Architecture:** `BhMaps.App` keeps its no-DI composition root (`AppServices`), its single-busy-operation
boundary (`MainViewModel.RunBusyAsync`) and its one-write wrapper (`MainViewModel.RunGameWriteAsync`). The shell
loses the sidebar: the map list, its search box and its autocomplete go, and the Maps page's grid becomes the
one list of maps. The ticked set stays on the shell (`SelectedMaps`, `SelectedMapCount`) so every page aims at
the same maps, but it is now computed from the Maps page's cards. `BhMaps.Core` gains one operation
(`PackApplier.ApplyToMaps`) and one record (`CustomPicture`); nothing else in Core changes.

**Tech Stack:** .NET 10 (`net10.0-windows`), C# 14, WPF with `ThemeMode="Dark"` (Fluent), CommunityToolkit.Mvvm
8.4.2 (the only package in `BhMaps.App`), xunit 2.9.3 in `tests\BhMaps.Core.Tests`, `System.Text.Json.Nodes`
for settings.

**Spec:** `docs\superpowers\specs\2026-09-11-bhmaps-v2-1-design.md`. Section numbers below cite it ("spec 3.1").
Part A covers spec sections 2, 3.1, 3.3, 6, 8, 9 (both bugs) and 11. Sections 3.2, 4, 5, 7 and the themed
`ContextMenu` and `MenuItem` styles belong to parts B and C.

**Branch:** `feature/bhmaps-v2.1`, based on `main` b0e4181. Work from `C:\Users\alexa\projects\bhmaps`.

---

## Global Constraints

Every task's requirements implicitly include this section.

**Safety, hard rules.**

- **Never point the app, `dotnet run`, or any test at the real game folder**
  `C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt`, and never write under
  `C:\Users\alexa\files\bh`.
- Every manual run uses all three overrides together:
  `dotnet run --project src\BhMaps.App -- --game "<DEV>\game\mapArt" --library "<DEV>\lib" --appdata "<DEV>\appdata"`
  where `<DEV>` is
  `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`.
  `--game` or `--library` without `--appdata` is refused at startup by design. Do not weaken that check.
- **Every write into the game folder goes through `MainViewModel.RunGameWriteAsync`.** No page, panel or dialog
  copies a file into the game folder by itself.

**Build and tooling.**

- Solution file is **`BhMaps.slnx`**, not `.sln`. Build: `dotnet build BhMaps.slnx -c Debug`.
- All three projects set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. A warning fails the build.
- `<UseWPF>true</UseWPF>` removes `System.IO` from implicit usings; each `.csproj` carries
  `<Using Include="System.IO" />`. Never add a per-file `using System.IO;`.
- **`dotnet format BhMaps.slnx` is the only formatter.** Check with
  `dotnet format BhMaps.slnx --verify-no-changes`. csharpier is not installed, and the repo-root
  `.csharpierignore` (a single `*`) disables the external csharpier hook. Do not remove it.
- `.editorconfig`: CRLF, 4-space C#, 2-space XAML, json and md.
- Tests: `dotnet test`, or `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~PackApplierTests"`.
- **No new NuGet packages.** CommunityToolkit.Mvvm 8.4.2 only.

**Copy.** **No em-dashes and no emoji** anywhere: app strings, code comments, commit messages. Copy comes from
spec 11 and is quoted literally in the tasks below. Type it exactly.

**MVVM style.** CommunityToolkit source generators with partial properties
(`[ObservableProperty] public partial bool IsBusy { get; set; }`) and `[RelayCommand]`. Every view model is
`partial` and derives from `ObservableObject`. `partial void OnFooChanged(T value)` is the generated change hook.

**Commit trailers.** Every commit ends with exactly these two lines, in this order, in the last paragraph:

```
Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

Each task ends with the exact `git commit -F -` command to run through the Bash tool.

---

## Shared contracts

Parts B and C are written against these. Do not rename them, do not change their shapes.

```csharp
// MainViewModel
public MapsViewModel Maps { get; }
public BackgroundsViewModel Backgrounds { get; }
public PacksViewModel Packs { get; }
public PackDetailViewModel PackDetail { get; }
public SettingsPageViewModel SettingsPage { get; }
[RelayCommand] void NavigateMaps();          // NavigateMapsCommand
[RelayCommand] void NavigateBackgrounds();   // NavigateBackgroundsCommand
[RelayCommand] void NavigatePacks();         // NavigatePacksCommand
[RelayCommand] void NavigateSettings();      // NavigateSettingsCommand
[RelayCommand] void ClearSelection();        // ClearSelectionCommand
public IReadOnlyList<MapEntry> SelectedMaps { get; }
public int SelectedMapCount { get; }
public void NotifySelectionChanged();
[ObservableProperty] public partial string? LastOpenedMap { get; set; }
public static string DoneSentence(bool gameRunning);
public Task<bool> RunGameWriteAsync(
    string label,
    IReadOnlyList<string> undoPaths,
    Func<IProgress<string>, CancellationToken, Task> work,
    string doneText,
    bool clearTicks = false);

// MapCardViewModel
[ObservableProperty] public partial bool IsSelected { get; set; }

// BhMaps.Core.Settings
public sealed record AppSettings(
    string GamePath, string LibraryPath, bool FirstRunDone,
    int MapsZoom = 6, int BackgroundsZoom = 6, int PackZoom = 5, bool WelcomeDone = false);
// JSON keys: gamePath, libraryPath, firstRunDone, mapsZoom, backgroundsZoom, packZoom, welcomeDone.

// BhMaps.Core.Operations
public static ApplyResult PackApplier.ApplyToMaps(
    Pack pack, IReadOnlyList<MapEntry> maps, string gamePath,
    IProgress<string>? progress, CancellationToken ct);
public static IReadOnlyList<string> PackApplier.ApplyToMapsPaths(Pack pack, IReadOnlyList<MapEntry> maps);
public sealed record CustomPicture(
    string Hash, string DisplayName, IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots, string? PackName);
```

Theme keys part A adds to `src\BhMaps.App\Theme\Controls.xaml`: **`TopBarTab`** (Button), **`SelectionBar`**
(Border), **`CardTick`** (CheckBox). `ContextMenu` and `MenuItem` styles are part B's job; part A's two menus use
the Fluent defaults and part B restyles them.

**Platforms is gone.** `NavigatePlatformsCommand`, `PlatformsViewModel`, `PlatformsView` and their `App.xaml`
`DataTemplate` are deleted in task A6. `MapPanelViewModel` keeps its platform-set logic untouched; part B extends
it into the Background | Platforms segment (spec 3.2).

---

## Decisions this plan makes where the spec or the contract left a choice open

An implementer must **not** re-litigate these.

| # | Question | Decision |
|---|---|---|
| A-D1 | The contract says `SelectedMaps` is computed from `Maps.Cards`. `Cards` is the **filtered** list, so typing in the search box would silently shrink the ticked set and a write would hit fewer maps than the bar says. | `MainViewModel.SelectedMaps => Maps.TickedMaps`, where `MapsViewModel.TickedMaps` reads the **unfiltered** `_all` list. The public shape (`IReadOnlyList<MapEntry>`, `SelectedMapCount`, `NotifySelectionChanged`) is exactly as contracted; only the private source differs. |
| A-D2 | Active tab colour: spec 2.1 says "Text on a Surface2 pill" and also "the same look as the old sidebar row", which used Tile for the current row and Surface2 for hover. | Active = `TileBrush` fill with `TextBrush` text; hover = `Surface2Brush` with `TextBrush`. Using Surface2 for both would make hover and active identical. |
| A-D3 | Tab control type: `ToggleButton` or `Button`. | **`Button`** with the existing `IsCurrentPageConverter` `DataTrigger`, as `NavRow` used. A `ToggleButton` whose `IsChecked` is a one-way `MultiBinding` loses that binding on the first click. |
| A-D4 | Whether the `Ticked` chip re-filters live as ticks change. | The `Ticked` chip filters on the tick state **at the moment the chip is chosen, or the grid is rebuilt by a scan**. It does not re-filter on every tick: that would mutate the `ListBox`'s `ItemsSource` in the middle of a `Ctrl+A` or a shift-range selection. |
| A-D5 | Which picture name the Custom tag shows, before part B's `CustomPictureLibrary` exists. | The map's first in-game background slot file name, ellipsised at 16. Part B replaces the one expression in `MapCardViewModel.CustomName`. |
| A-D6 | The first-run line's "or a custom picture exists" half needs `CustomPictureLibrary`. | Part A shows the line when the library holds **no pack other than Default**. Part B ands in the custom-picture test. |
| A-D7 | The done line an Undo leaves. Spec 11 gives no string. | `MainViewModel.UndoDoneText = "Last change undone."` plus the shared sentence, with no Undo button beside it: a restore that succeeds discards its snapshot. |
| A-D8 | Where the "Game folder not found" red state lives now that the top bar owns it. | It moves to the top bar. `PageHeader` loses its red `TextBlock` and keeps the `GameFolderMissing` triggers that hide the progress and done lines. Saying it twice on one screen is noise (spec 2.1). |
| A-D9 | The zoom slider's home. | The page's `PageHeader.Actions`, after the search box. `PageHeader` itself is unchanged by that. |
| A-D10 | Virtualisation on the map grid. | The grid's `ItemsPanel` is a `UniformGrid`, as v2's `ItemsControl` already used, and the `ListBox` sets `ScrollViewer.CanContentScroll="False"`. Every container is realised, which is what makes the container `IsSelected` binding and `Ctrl+A` exact. 67 cards is well inside what that costs. |
| A-D11 | Where `CustomPicture` lives before part B builds the library. | `src\BhMaps.Core\Operations\CustomPictureLibrary.cs`, created in A9 holding the record alone. Part B adds `public static class CustomPictureLibrary` to the same file. |

---

## File map

```
src\BhMaps.Core\Settings\AppSettings.cs                  MODIFY  A1  MapsZoom, PackZoom, WhileRunning gone
src\BhMaps.Core\Settings\SettingsStore.cs                MODIFY  A1  keys, homeZoom migration, clamps
src\BhMaps.Core\Operations\PackApplier.cs                MODIFY  A9  ApplyToMaps, ApplyToMapsPaths
src\BhMaps.Core\Operations\CustomPictureLibrary.cs       CREATE  A9  the CustomPicture record only

src\BhMaps.App\Services\GameLauncher.cs                  MODIFY  A1  RunWriteAsync is the write
src\BhMaps.App\ViewModels\MainViewModel.cs               MODIFY  A1 A2 A5 A6
src\BhMaps.App\ViewModels\MapCardViewModel.cs            MODIFY  A6 A7  IsSelected, TagText, tooltip
src\BhMaps.App\ViewModels\MapPanelViewModel.cs           MODIFY  A5  Close targets Maps
src\BhMaps.App\ViewModels\MapListItemViewModel.cs        DELETE  A6
src\BhMaps.App\ViewModels\Pages\HomeViewModel.cs         RENAME  A5  -> MapsViewModel.cs
src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs         MODIFY  A6 A7 A10
src\BhMaps.App\ViewModels\Pages\BackgroundsViewModel.cs  MODIFY  A1 A6  zoom range, own SearchText
src\BhMaps.App\ViewModels\Pages\PlatformsViewModel.cs    DELETE  A6
src\BhMaps.App\ViewModels\Pages\SettingsPageViewModel.cs MODIFY  A1  WhileRunning gone
src\BhMaps.App\Views\MainWindow.xaml(.cs)                MODIFY  A5 A6  top bar, Ctrl+1..4, Ctrl+K
src\BhMaps.App\Views\Pages\HomeView.xaml(.cs)            RENAME  A5  -> MapsView.xaml(.cs)
src\BhMaps.App\Views\Pages\MapsView.xaml(.cs)            MODIFY  A3 A6 A7 A8 A10
src\BhMaps.App\Views\Pages\BackgroundsView.xaml          MODIFY  A6  own SearchText binding
src\BhMaps.App\Views\Pages\PlatformsView.xaml(.cs)       DELETE  A6
src\BhMaps.App\Views\Pages\SettingsPageView.xaml         MODIFY  A1  Applying row replaces the combo
src\BhMaps.App\Views\Controls\PageHeader.xaml            MODIFY  A6  red state moves to the top bar
src\BhMaps.App\Views\Controls\SuggestBox.xaml(.cs)       DELETE  A6
src\BhMaps.App\Views\Controls\ZoomSlider.xaml            MODIFY  A7  end glyphs
src\BhMaps.App\Theme\Controls.xaml                       MODIFY  A4 A6 A8 A10
src\BhMaps.App\Theme\Tokens.xaml                         MODIFY  A6  SidebarWidth gone
src\BhMaps.App\App.xaml                                  MODIFY  A5 A6  DataTemplates

tests\BhMaps.Core.Tests\SettingsStoreTests.cs            MODIFY  A1
tests\BhMaps.Core.Tests\PackApplierTests.cs              MODIFY  A9
```

**Shorthand used below.**

- `<DEV>` = `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`
- `<CAP>` = the same scratchpad path with `\cap`, `<SHOTS>` with `\shots`
- `<EXE>` = `C:\Users\alexa\projects\bhmaps\src\BhMaps.App\bin\Debug\net10.0-windows\BhMaps.exe`

`<CAP>\CapLib.ps1` gives `Start-BhMaps <EXE> <DEV>` (launches with all three overrides), `Get-MainWindow`,
`Find-ByName`, `Find-AllByType`, `Invoke-El`, `Select-El`, `Set-Value`, `Set-Range`, `Dump-Tree`, `Move-Win`,
`Save-Window`. The visual-check steps below use them. Always stop the previous dev instance first:
`Get-Process BhMaps -ErrorAction SilentlyContinue | Stop-Process -Force`. That process name is the dev build;
the game's is `Brawlhalla` and is never touched.

---

# Task A1: live writes, the settings record, and the Settings page

Spec 8 (G1) and spec 6. The restart flow goes, `whileRunning` goes with it, and the zoom keys are renamed and
widened in the same pass so the store and the pages agree about their ranges from this commit on.

**Files:**
- Modify: `src\BhMaps.Core\Settings\AppSettings.cs` (whole record)
- Modify: `src\BhMaps.Core\Settings\SettingsStore.cs` (`KnownKeys` lines 16-17, `Load` lines 22-67, `Save` lines 69-81)
- Modify: `src\BhMaps.App\Services\GameLauncher.cs` (whole class)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (delete `ConfirmIfGameRunning`, lines 659-664; comments at lines 766-780; the `new GameLauncher(...)` at line 53)
- Modify: `src\BhMaps.App\ViewModels\Pages\SettingsPageViewModel.cs` (line 21, lines 62-64, lines 156-166)
- Modify: `src\BhMaps.App\Views\Pages\SettingsPageView.xaml` (lines 146-160, and the `settings:` xmlns at line 5)
- Modify: `src\BhMaps.App\ViewModels\Pages\HomeViewModel.cs` (lines 15-16, 43, 63, 198-201)
- Modify: `src\BhMaps.App\ViewModels\Pages\BackgroundsViewModel.cs` (lines 18-19)
- Test: `tests\BhMaps.Core.Tests\SettingsStoreTests.cs` (lines 135-216, and line 175)

**Interfaces:**

Consumes: `AtomicFile.WriteAllText`, `JsonObject`, `JsonNodeOptions`, `GameProcess.IsRunning`.
Produces:

```csharp
public sealed record AppSettings(
    string GamePath, string LibraryPath, bool FirstRunDone,
    int MapsZoom = 6, int BackgroundsZoom = 6, int PackZoom = 5, bool WelcomeDone = false);
public const int AppSettings.MinZoom = 2;
public const int AppSettings.MaxZoom = 10;
public Task<bool> GameLauncher.RunWriteAsync(string actionLabel, Func<Task> write);
```

- [ ] **Step 1:** Rewrite the `AppSettings` record. `DefaultGamePath`, `DefaultLibraryPath`, `Default` and
  `Unknown` stay as they are; `RestartWhileRunning` and `LiveWhileRunning` go; add the shared zoom range:

```csharp
public sealed record AppSettings(
    string GamePath,
    string LibraryPath,
    bool FirstRunDone,
    int MapsZoom = 6,
    int BackgroundsZoom = 6,
    int PackZoom = 5,
    bool WelcomeDone = false)
{
    public const string DefaultGamePath = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla\mapArt";

    /// <summary>Columns a grid can be zoomed to (spec 3.1). One range for every page, so a value hand-edited or
    /// carried over from another page is never clamped away by a narrower one.</summary>
    public const int MinZoom = 2;
    public const int MaxZoom = 10;
```

- [ ] **Step 2:** In `SettingsStore.cs`, replace `KnownKeys`:

```csharp
    /// <summary>Keys this version writes, plus the two 2.0 keys it drops. A dropped key has to stay known, or
    /// Load would carry it into <see cref="AppSettings.Unknown"/> and Save would write it straight back.</summary>
    private static readonly string[] KnownKeys =
    [
        "gamePath", "libraryPath", "firstRunDone", "mapsZoom", "backgroundsZoom", "packZoom", "welcomeDone",
        "homeZoom", "whileRunning",
    ];
```

  and replace the `return new AppSettings(...)` block in `Load` with:

```csharp
        // Spec 8: there is no restart flow any more, so an old whileRunning is read once, reported and dropped.
        if (obj["whileRunning"] is not null)
        {
            System.Diagnostics.Trace.WriteLine(
                "BhMaps settings: whileRunning is no longer used and was dropped; writes are always live.");
        }

        // 2.0 stored the Maps zoom as homeZoom. The new key wins where both are present; otherwise the old one is
        // read once and written back under the new name, so an upgrade does not reset anyone's column count.
        var mapsZoom = obj["mapsZoom"] is not null ? Int(obj, "mapsZoom", 6) : Int(obj, "homeZoom", 6);

        return new AppSettings(
            Str(obj, "gamePath") is { Length: > 0 } g ? g : AppSettings.DefaultGamePath,
            Str(obj, "libraryPath") is { Length: > 0 } l ? l : AppSettings.DefaultLibraryPath,
            Bool(obj, "firstRunDone"),
            Math.Clamp(mapsZoom, AppSettings.MinZoom, AppSettings.MaxZoom),
            Math.Clamp(Int(obj, "backgroundsZoom", 6), AppSettings.MinZoom, AppSettings.MaxZoom),
            Math.Clamp(Int(obj, "packZoom", 5), AppSettings.MinZoom, AppSettings.MaxZoom),
            Bool(obj, "welcomeDone"))
        {
            Unknown = unknown.Count == 0 ? null : unknown,
        };
```

  `Load`'s summary becomes: `Missing file, unreadable file, corrupt file or missing fields all fall back to
  defaults. Zooms are clamped, a 2.0 homeZoom loads as MapsZoom, and whileRunning is dropped.` In `Save`, the
  object is `gamePath, libraryPath, firstRunDone, mapsZoom, backgroundsZoom, packZoom, welcomeDone`, in that
  order. No `homeZoom`, no `whileRunning`.

- [ ] **Step 3:** Rewrite `GameLauncher.cs`. The class stays so every call site keeps its shape:

```csharp
using BhMaps.Core.Game;

namespace BhMaps.App.Services;

/// <summary>Spec 8: a write goes straight into the game folder whether Brawlhalla is running or not, and shows
/// on the next match load. There is no restart flow left; this is the write, and the poll behind the top bar's
/// game line is GameProcess.</summary>
public sealed class GameLauncher
{
    public bool IsRunning => GameProcess.IsRunning();

    /// <summary>Runs the write. Always true: nothing turns a write away any more. The shape is kept so
    /// MainViewModel.RunGameWriteAsync reads as it did and the one call site did not have to be rebuilt.</summary>
    public async Task<bool> RunWriteAsync(string actionLabel, Func<Task> write)
    {
        _ = actionLabel;
        await write();
        return true;
    }
}
```

  In `MainViewModel` change `_launcher = new GameLauncher(services, dialogs);` to `_launcher = new GameLauncher();`.

- [ ] **Step 4:** In `MainViewModel.cs` delete `ConfirmIfGameRunning` with its summary (lines 659-664); nothing
  calls it. Inside `RunGameWriteAsync` replace the restart comments with:

```csharp
                // The launcher runs inside the boundary, so the write and the snapshot in front of it are one
                // operation with everything else disabled (spec 8).
```

  and, on the inner callback:

```csharp
                        // The capture is the first step of the work, not a step before it: it is file copying, so
                        // it belongs off the UI thread, behind a progress line, and inside the boundary that turns
                        // an IO failure into the same dialog any other write failure gets.
```

- [ ] **Step 5:** In `SettingsPageViewModel.cs` delete the `WhileRunning = ...` assignment (line 21), the
  `WhileRunning` property with its summary (lines 62-64) and `OnWhileRunningChanged` with its summary
  (lines 156-166). `using BhMaps.Core.Settings;` stays: `SettingsStore.Validate*` still uses it.

- [ ] **Step 6:** In `SettingsPageView.xaml` replace the "While the game runs" row (lines 146-160) with the
  Applying row, and delete the now-unused `xmlns:settings` (line 5):

```xml
          <!-- Applying. One line of prose (spec 6): there is nothing left to choose. -->
          <Border Grid.Row="4" Style="{StaticResource SettingRule}" />
          <TextBlock Grid.Row="4" Grid.Column="0" Style="{StaticResource SettingLabel}" Text="Applying" />
          <TextBlock Grid.Row="4"
                     Grid.Column="1"
                     Margin="0,10"
                     VerticalAlignment="Center"
                     Foreground="{StaticResource Text2Brush}"
                     Text="Changes are written straight into the game folder, with Brawlhalla open or closed, and show on the next match load. Undo puts back the files of the last write."
                     TextWrapping="Wrap" />
```

  Rows 0 to 3 and row 5 (Version) are unchanged.

- [ ] **Step 7:** In `HomeViewModel.cs` add `using BhMaps.Core.Settings;` and make the zoom read the new key:

```csharp
    public const int MinZoom = AppSettings.MinZoom;
    public const int MaxZoom = AppSettings.MaxZoom;
```

  the constructor line becomes `Zoom = Math.Clamp(shell.Services.Settings.MapsZoom, MinZoom, MaxZoom);`, the
  `Zoom` summary says `persisted as mapsZoom`, and `OnZoomChanged` reads and writes `MapsZoom`. Keep the
  `MinZoom`/`MaxZoom` names: `HomeView.xaml` binds them through `x:Static`. Do the same two constants in
  `BackgroundsViewModel.cs` (its 3 and 8 become `AppSettings.MinZoom` and `AppSettings.MaxZoom`); its
  `BackgroundsZoom` read and write already name the right property.

- [ ] **Step 8:** Replace the last three tests of `SettingsStoreTests.cs` (lines 135-216) with these five:

```csharp
    [Fact]
    public void Load_MissingV21Fields_UsesDefaults()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\g","libraryPath":"C:\l","firstRunDone":true}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(6, loaded.MapsZoom);
        Assert.Equal(6, loaded.BackgroundsZoom);
        Assert.Equal(5, loaded.PackZoom);
        Assert.False(loaded.WelcomeDone);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEveryV21Field()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        var settings = new AppSettings(@"C:\g", @"C:\l", true, MapsZoom: 9, BackgroundsZoom: 3, PackZoom: 7,
            WelcomeDone: true);

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(9, loaded.MapsZoom);
        Assert.Equal(3, loaded.BackgroundsZoom);
        Assert.Equal(7, loaded.PackZoom);
        Assert.True(loaded.WelcomeDone);
        var json = File.ReadAllText(path);
        Assert.Contains("\"mapsZoom\": 9", json);
        Assert.Contains("\"packZoom\": 7", json);
    }

    [Fact]
    public void Load_ClampsEveryZoomToTwoThroughTen()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"mapsZoom":99,"backgroundsZoom":0,"packZoom":-4}""");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(10, loaded.MapsZoom);
        Assert.Equal(2, loaded.BackgroundsZoom);
        Assert.Equal(2, loaded.PackZoom);
    }

    [Fact]
    public void Load_MigratesAHomeZoomIntoMapsZoomAndPrefersMapsZoomWhenBothArePresent()
    {
        using var tmp = new TempDir();
        var legacy = tmp.Sub("legacy.json");
        File.WriteAllText(legacy, """{"homeZoom":4}""");
        var both = tmp.Sub("both.json");
        File.WriteAllText(both, """{"homeZoom":4,"mapsZoom":8}""");

        Assert.Equal(4, SettingsStore.Load(legacy).MapsZoom);
        Assert.Equal(8, SettingsStore.Load(both).MapsZoom);
    }

    [Fact]
    public void SaveThenLoad_DropsTheOldWhileRunningAndHomeZoomKeys()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("settings.json");
        File.WriteAllText(path, """{"gamePath":"C:\g","whileRunning":"restart","homeZoom":4,"keepMe":1}""");

        var loaded = SettingsStore.Load(path);
        SettingsStore.Save(path, loaded);

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.DoesNotContain(json, p => p.Key.Equals("whileRunning", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(json, p => p.Key.Equals("homeZoom", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, (int)json["mapsZoom"]!);
        Assert.Equal(1, (int)json["keepMe"]!);
    }
```

  Also change `Save_PreservesUnknownFieldsFromTheExistingFile` (line 175) from `HomeZoom = 2` to `MapsZoom = 2`
  and its `json["homeZoom"]` assertion to `json["mapsZoom"]`.

- [ ] **Step 9:** `dotnet build BhMaps.slnx -c Debug` succeeds with zero warnings. `dotnet test` is green
  (`SettingsStoreTests` has 14 tests).
- [ ] **Step 10: Visual check** on the dev tree. Stop the old instance, then in PowerShell:

```powershell
. "<CAP>\CapLib.ps1"
$p = Start-BhMaps "<EXE>" "<DEV>"; Start-Sleep 8
$h = [IntPtr]$p.MainWindowHandle; Move-Win $h 300 100 1280 800; Start-Sleep 1
$w = Get-MainWindow $p.Id
Invoke-El (Find-ByName $w "Settings" "Button"); Start-Sleep 2
Save-Window $h "<SHOTS>\a1-settings.png"
```

  Expected in the PNG: the rows Game folder, Library, Game data, Defaults, **Applying** with the sentence from
  step 6, and Version. No "While the game runs" row and no combo box.
- [ ] **Step 11:** `dotnet format BhMaps.slnx`, then `dotnet format BhMaps.slnx --verify-no-changes` reports no
  changes.
- [ ] **Step 12:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(core): writes are always live, and the zoom keys become mapsZoom and packZoom

Spec 8: the restart flow is gone. GameLauncher.RunWriteAsync is the write itself,
AppSettings loses whileRunning, and SettingsStore reads an old value once, reports
it and drops it. homeZoom migrates into mapsZoom and every zoom clamps to 2..10.
The Settings page trades its combo box for one line of prose.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A2: the done sentence, clearTicks, and Undo through the wrapper

Spec 2.2 and spec 8 (G2). One shared sentence, appended by the wrapper so no page can drift, and Undo stops
calling the launcher behind the wrapper's back.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (`UndoAsync` lines 316-352; `Count` line 469;
  `RunGameWriteAsync` lines 734-800)

**Interfaces:**

Consumes: `Services.Undo.Begin/Latest/Restore`, `RunBusyAsync`, `GameRunning`.
Produces:

```csharp
public static string DoneSentence(bool gameRunning);       // the two spec 11 strings
public const string UndoDoneText = "Last change undone.";
public static string Count(int n, string noun);            // was private
public Task<bool> RunGameWriteAsync(string label, IReadOnlyList<string> undoPaths,
    Func<IProgress<string>, CancellationToken, Task> work, string doneText, bool clearTicks = false);
```

- [ ] **Step 1:** Add the shared strings and make `Count` public. Put them beside `SetLibraryDone`:

```csharp
    /// <summary>Spec 2.2: what a write reports about when it will show. One string, appended by the wrapper, so
    /// two pages cannot word it differently. Read at completion, not at the start: a game that was launched while
    /// the write ran gets the sentence that is true when the line appears.</summary>
    public static string DoneSentence(bool gameRunning) =>
        gameRunning ? "Shows on the next match load." : "Shows when Brawlhalla starts.";

    /// <summary>The line an undo leaves. Spec 11 names no string for it, so this is the plan's (A-D7).</summary>
    public const string UndoDoneText = "Last change undone.";
```

  and change `private static string Count` to `public static string Count`.

- [ ] **Step 2:** Replace `RunGameWriteAsync` with a thin public face over a shared core, and give `UndoAsync`
  the same core. Both guards, the boundary, the done line, the rescan and the tick clearing now live in one
  place:

```csharp
    /// <summary>One write into the game folder (spec 8): the busy boundary, an undo snapshot of the paths it is
    /// about to touch, a done line ending in the shared sentence, the ticks cleared when the write was aimed at
    /// them, and a rescan. False when the folder was missing, another operation held the boundary, or the write
    /// was cancelled or failed.</summary>
    public Task<bool> RunGameWriteAsync(
        string label,
        IReadOnlyList<string> undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool clearTicks = false) =>
        RunWriteCoreAsync(label, undoPaths, work, doneText, undoable: true, clearTicks);

    /// <summary>The one path every game write takes. <paramref name="undoPaths"/> null means take no snapshot,
    /// which is Undo's case and only Undo's: the snapshot it is restoring is the only one there is, and Begin
    /// would replace it with an empty one.</summary>
    private async Task<bool> RunWriteCoreAsync(
        string label,
        IReadOnlyList<string>? undoPaths,
        Func<IProgress<string>, CancellationToken, Task> work,
        string doneText,
        bool undoable,
        bool clearTicks)
    {
        if (GameFolderMissing || IsBusy)
        {
            // Nothing to write into, or one operation at a time (spec 7.1). Refused silently: the top bar already
            // carries the missing-folder notice, and a refused write must not clear the done line or rescan.
            return false;
        }

        var gamePath = Services.GamePath;
        var ok = await RunBusyAsync(
            label,
            async (progress, ct) =>
            {
                await _launcher.RunWriteAsync(
                    label,
                    async () =>
                    {
                        if (undoPaths is not null)
                        {
                            // The capture is the first step of the work, not a step before it: it is file copying,
                            // so it belongs off the UI thread, behind a progress line, and inside the boundary that
                            // turns an IO failure into the same dialog any other write failure gets.
                            progress.Report("Saving undo");
                            await Task.Run(() => Services.Undo.Begin().Capture(gamePath, undoPaths), ct);
                        }

                        await work(progress, ct);
                    });
            });

        // Begin has already replaced the previous snapshot, so a write that was cancelled or failed has to clear
        // the done line too; leaving it would describe something Undo no longer restores.
        DoneText = ok ? $"{doneText} {DoneSentence(GameRunning)}" : "";
        DoneUndoable = ok && undoable;

        // A restore that fully succeeds discards its snapshot, so what can be undone is always read back from the
        // store rather than remembered.
        CanUndo = Services.Undo.Latest is not null;
        if (ok && clearTicks)
        {
            // Spec 3.3 (S3): a write aimed at the ticked maps is finished with them. A cancelled or failed write
            // keeps them, which is why this is inside the ok branch.
            ClearSelection();
        }

        await RescanAsync();
        return ok;
    }
```

- [ ] **Step 3:** Rewrite `UndoAsync` to go through the same core (spec 8). It takes no snapshot of its own and
  leaves no Undo button beside its line:

```csharp
    /// <summary>Spec 6.4 and 8: puts back the files the last game write was about to overwrite or delete, through
    /// the same wrapper as every other write. It is a game write, so it is off while the folder is missing, and
    /// it takes no snapshot of its own: the one it is restoring is the only one there is.</summary>
    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task UndoAsync()
    {
        if (Services.Undo.Latest is not { } session)
        {
            return;
        }

        var gamePath = Services.GamePath;
        ApplyResult? result = null;
        await RunWriteCoreAsync(
            "Undoing",
            undoPaths: null,
            (_, ct) => Task.Run(() => { result = Services.Undo.Restore(session, gamePath); }, ct),
            UndoDoneText,
            undoable: false,
            clearTicks: false);

        if (result is not null)
        {
            Dialogs.ShowFailures("Some files could not be restored", result.Failures);
        }
    }
```

  The old body's `RunBusyAsync` call, its `accepted` flag, its `DoneText = ""`, its `CanUndo` line and its
  trailing `RescanAsync` all go: the core does each of them.

- [ ] **Step 4:** `dotnet build BhMaps.slnx -c Debug` with zero warnings, `dotnet test` green.
- [ ] **Step 5: Visual check** on the dev tree, with Brawlhalla **not** running. Launch as in A1 step 10, open a
  map card ("Brawlhaven"), apply any background tile through its Apply button, capture, then press Undo in the
  header and capture again:

```powershell
Invoke-El (Find-ByName $w "Brawlhaven" "Button"); Start-Sleep 2
$tiles = Find-AllByType $w "Button" | Where-Object { $_.Current.Name -like "*.jpg (*" }
Invoke-El $tiles[0]; Start-Sleep 4
Save-Window $h "<SHOTS>\a2-done.png"
Invoke-El (Find-ByName $w "Undo" "Button"); Start-Sleep 4
Save-Window $h "<SHOTS>\a2-undone.png"
```

  Expected: `a2-done.png` header reads `<file> applied to Brawlhaven. Shows when Brawlhalla starts.` with Undo
  beside it; `a2-undone.png` reads `Last change undone. Shows when Brawlhalla starts.` with **no** Undo button.
  No confirm dialog appears at any point.
- [ ] **Step 6:** `dotnet format BhMaps.slnx --verify-no-changes` reports no changes.
- [ ] **Step 7:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(app): one done sentence for every write, and Undo through the same wrapper

Spec 2.2 and 8: RunGameWriteAsync appends "Shows on the next match load." or
"Shows when Brawlhalla starts." to every done line, read at completion so a game
launched mid-write gets the true one. Undo now takes the same path with no
snapshot of its own, and a write aimed at the ticked maps can clear them.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A3: chips take a mouse click

Spec 9, first bug. `ChipRow`'s template root is a `Grid` with no `Background`, and both children are
`IsHitTestVisible="False"`, so nothing in the item is hit-testable and the `ListBoxItem` never sees the click.
UI Automation's `SelectionItem.Select` bypasses hit testing, which is why every automated check has passed.

**Files:**
- Modify: `src\BhMaps.App\Views\Pages\HomeView.xaml` (line 21, the `ChipRow` template root)

**Interfaces:** none. Markup only.

- [ ] **Step 1:** In the `ChipRow` style's `ControlTemplate`, give the root `Grid` a background and say why:

```xml
            <!-- Transparent, not unset: a Grid with no Background is not hit-testable, and both children below
                 are IsHitTestVisible=False, so without this the ListBoxItem never sees a mouse click and only UI
                 Automation, which bypasses hit testing, could select a chip (spec 9). -->
            <Grid Background="Transparent">
```

  Nothing else in the style changes.

- [ ] **Step 2:** `dotnet build BhMaps.slnx -c Debug` with zero warnings.
- [ ] **Step 3: Visual check with a real mouse click.** UIA cannot prove this fix; a synthesised click can. Save
  this as `<CAP>\click.ps1` and run it (it moves the cursor for a moment and restores it):

```powershell
param([int]$ProcId, [string]$Name)
. "$PSScriptRoot\CapLib.ps1"
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Click {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@
$w = Get-MainWindow $ProcId
$el = Find-ByName $w $Name "ListItem"
if (-not $el) { throw "chip '$Name' not found" }
$r = $el.Current.BoundingRectangle
$old = New-Object Click+POINT; [Click]::GetCursorPos([ref]$old) | Out-Null
[Click]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)); Start-Sleep -Milliseconds 200
[Click]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero); [Click]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 600
[Click]::SetCursorPos($old.X, $old.Y)
$sp = (Find-ByName $w $Name "ListItem").GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
"selected=" + $sp.Current.IsSelected
```

  Run the app as in A1 step 10, then `& "<CAP>\click.ps1" -ProcId $p.Id -Name "Ranked 1v1"`.
  **Acceptance:** it prints `selected=True`, and a `Save-Window` capture afterwards shows the Ranked 1v1 chip
  filled and the grid narrowed. Before the fix the same script prints `selected=False`.
- [ ] **Step 4:** `dotnet format BhMaps.slnx --verify-no-changes` reports no changes.
- [ ] **Step 5:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
fix(app): a chip takes a mouse click

The ChipRow template root was a Grid with no Background, and both of its children
set IsHitTestVisible=False, so no mouse click ever reached the ListBoxItem. UI
Automation selects without hit testing, which is why the earlier checks passed.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A4: typed text starts at the field's padding

Spec 9, second bug. In the header search box (`Padding="33,3,10,0"`, 200 px wide) a typed "brawl" starts about
70 px from the box's left edge. The padding accounts for 33 of those. **Reproduce and measure first, then
root-cause, then fix in the shared `FieldTextBox` style** so every field in the app gets it. This is a
debugging task: do not guess the fix from the two suspects below, prove which one it is.

**Files:**
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (`FieldTextBox`, lines 290-325)

**Interfaces:** none. The style's keys and setters keep their names; only its layout changes.

**Suspects (spec 9).** (1) The Fluent implicit `ScrollViewer` style reaching `PART_ContentHost`, which the
template declares with no `Style`, so `ThemeMode="Dark"` supplies one with its own padding or chrome.
(2) The inner `TextBoxView` centred in a measure wider than the content host, which
`HorizontalContentAlignment` would decide.

- [ ] **Step 1: reproduce.** Build, stop any old instance, launch on the dev tree, type into the Maps header
  search box through UIA and capture:

```powershell
. "<CAP>\CapLib.ps1"
$p = Start-BhMaps "<EXE>" "<DEV>"; Start-Sleep 8
$h = [IntPtr]$p.MainWindowHandle; Move-Win $h 300 100 1280 800; Start-Sleep 1
$w = Get-MainWindow $p.Id
$box = Find-AllByType $w "Edit" | Where-Object { $_.Current.Name -eq "Search maps" } | Select-Object -First 1
Set-Value $box "brawl"; Start-Sleep 1
$r = $box.Current.BoundingRectangle; "box x=$($r.X) y=$($r.Y) w=$($r.Width) h=$($r.Height)"
Save-Window $h "<SHOTS>\a4-before.png"
```

- [ ] **Step 2: measure.** The offset is read off the PNG, not guessed. This scans the box's rows for the first
  column holding a pixel brighter than the field fill (`#242220`) and prints it in device-independent pixels:

```powershell
Add-Type -AssemblyName System.Drawing
function Measure-TextStart([string]$Png, [double]$BoxX, [double]$BoxY, [double]$BoxW, [double]$BoxH, [int]$WinX, [int]$WinY, [double]$Dpi) {
  $b = [System.Drawing.Bitmap]::FromFile($Png)
  $x0 = [int]($BoxX - $WinX); $y0 = [int]($BoxY - $WinY)
  for ($x = $x0; $x -lt $x0 + $BoxW; $x++) {
    for ($y = $y0 + 3; $y -lt $y0 + $BoxH - 3; $y++) {
      $c = $b.GetPixel($x, $y)
      if ($c.R -gt 120 -and $c.G -gt 120) { $b.Dispose(); return [math]::Round(($x - $x0) * 96.0 / $Dpi, 1) }
    }
  }
  $b.Dispose(); return -1
}
```

  Call it with the box rectangle from step 1, the window's own `GetWindowRect` origin (`Save-Window` crops to the
  DWM frame, so pass that frame's left and top) and the DPI `Save-Window` printed. Record the number. The bug
  report says about 70; the padding says 33.
  **Note:** the search icon sits at `Margin="11,0,0,0"` and is `Text3` (`#6B675F`), which the brightness test
  above rejects; the placeholder is hidden once the box has text.
- [ ] **Step 3: root-cause, one hypothesis at a time.** Change one thing, rebuild, re-measure with the same two
  steps, write the number down, revert if it did not move it:
  - **H1, the implicit ScrollViewer style.** Add `Style="{x:Null}"` to `PART_ContentHost` in `FieldTextBox`.
    Setting `Style` locally, even to null, stops the implicit lookup by type.
  - **H2, the centred TextBoxView.** Add `<Setter Property="HorizontalContentAlignment" Value="Left" />` and
    `<Setter Property="VerticalContentAlignment" Value="Center" />` to `FieldTextBox`.
  - If neither moves the number, dump the visual tree of the box in the running app with `Dump-Tree $box 8` and
    read the actual `PART_ContentHost` and `TextBoxView` rectangles from it; the child whose left edge is 35 px
    past the host's is the culprit, and the setter that moves *that* child is the fix.
- [ ] **Step 4: fix.** Keep the smallest change that moves the measurement to the padding, in `FieldTextBox`
  only, with a comment naming what it defeats. For example, if H1 is the cause:

```xml
            <!-- Style is set, and set to null, on purpose: with ThemeMode=Dark the Fluent implicit ScrollViewer
                 style reaches PART_ContentHost and adds chrome of its own, which pushed the first glyph about
                 35 px past this field's padding (spec 9). A local Style value stops the implicit lookup. -->
            <ScrollViewer x:Name="PART_ContentHost"
                          Margin="{TemplateBinding Padding}"
                          Focusable="False"
                          HorizontalScrollBarVisibility="Hidden"
                          Style="{x:Null}"
                          VerticalScrollBarVisibility="Hidden" />
```

- [ ] **Step 5: acceptance.** Repeat steps 1 and 2 into `<SHOTS>\a4-after.png`. **The measured first text column
  is within 2 device-independent pixels of the field's left padding (33 for the search box).** Record the before
  and after numbers in the commit body.
- [ ] **Step 6: regression check.** `FieldTextBox` is shared by eleven fields. Capture the Welcome window and the
  Import window and confirm nothing is clipped or shifted:

```powershell
Invoke-El (Find-ByName $w "Packs" "Button"); Start-Sleep 2
Invoke-El (Find-ByName $w "Import folder" "Button"); Start-Sleep 3
$other = Get-AppWindows $p.Id | Where-Object { [IntPtr]$_.Current.NativeWindowHandle -ne $h }
Save-Window ([IntPtr]$other[0].Current.NativeWindowHandle) "<SHOTS>\a4-import.png"
```

  Expected: the pack-name field's text starts at its own 10 px padding, the caret is on the text baseline, and
  nothing in the dialog moved vertically. Close the window without importing.
- [ ] **Step 7:** `dotnet build BhMaps.slnx -c Debug` zero warnings, `dotnet test` green,
  `dotnet format BhMaps.slnx --verify-no-changes` clean.
- [ ] **Step 8:** Commit (fill in the two numbers):

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
fix(app): typed text starts at a field's padding

Measured off a capture of the Maps search box: the first glyph column sat NN px
from the field's left edge against a padding of 33. Cause: <the one proven in
step 3>. Fixed in the shared FieldTextBox style, so every field in the app gets
it; after the fix the first glyph column measures NN px.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A5: Home becomes Maps

Spec 2 ("Why Home becomes Maps") and spec 3.1. A rename and nothing else, so the diff that follows in A6 is
about the shell rather than about names. Use `git mv` so the history follows the files.

**Files:**
- Rename: `src\BhMaps.App\ViewModels\Pages\HomeViewModel.cs` -> `MapsViewModel.cs`
- Rename: `src\BhMaps.App\Views\Pages\HomeView.xaml` -> `MapsView.xaml`, `HomeView.xaml.cs` -> `MapsView.xaml.cs`
- Modify: `src\BhMaps.App\App.xaml` (the `HomeViewModel` `DataTemplate`, lines 25-27, and the `FileContainer`
  comment at lines 17-18)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (field/property `Home`, `NavigateHome`, `_pages`,
  `CurrentPage = Home`, `ChooseSuggestion`'s `CurrentPage == Home`)
- Modify: `src\BhMaps.App\ViewModels\MapPanelViewModel.cs` (line 196)
- Modify: `src\BhMaps.App\Views\MainWindow.xaml` (the Home nav row, lines 151-155)

**Interfaces:**

Produces: `MapsViewModel` (was `HomeViewModel`), `MainViewModel.Maps`, `NavigateMapsCommand`,
`MapsViewModel.Title => "Maps"`, `MapsView`.

- [ ] **Step 1:** `git mv` the three files, then rename inside them:
  `class HomeViewModel` -> `class MapsViewModel`; its constructor; `x:Class="BhMaps.App.Views.Pages.HomeView"`
  -> `...MapsView`; `public partial class HomeView : UserControl` -> `MapsView` and its constructor;
  `x:Static pages:HomeViewModel.MaxZoom/MinZoom` -> `pages:MapsViewModel....`.
- [ ] **Step 2:** `MapsViewModel.Title` returns `"Maps"`, and the `PageHeader` in `MapsView.xaml` (line 387)
  becomes `Title="Maps"`. Update the class summary to cite spec 3.1 instead of 7.2.
- [ ] **Step 3:** In `MainViewModel`: `Home` becomes `Maps` (property, construction, `_pages` list,
  `CurrentPage = Maps`), `NavigateHome` becomes `NavigateMaps`, and `ChooseSuggestion`'s
  `CurrentPage == Home` becomes `CurrentPage == Maps` with `Maps.OpenMap(...)`. Fix the two summaries that say
  "on Home" to say "on Maps".
- [ ] **Step 4:** `MapPanelViewModel.Close` becomes `_shell.Maps.Selected = null;` and its summary says
  "clearing the Maps page's selection".
- [ ] **Step 5:** `App.xaml`'s template becomes
  `<DataTemplate DataType="{x:Type pages:MapsViewModel}"><pageviews:MapsView /></DataTemplate>`, and the
  `FileContainer` comment reads "The Maps panel and the pack detail page list the same platform files".
- [ ] **Step 6:** `MainWindow.xaml`'s first nav row becomes `Command="{Binding NavigateMapsCommand}"`,
  `Content="Maps"`, `Tag="{Binding Maps}"`. Leave the sidebar alone otherwise; A6 removes it.
- [ ] **Step 7:** `dotnet build BhMaps.slnx -c Debug` zero warnings; `dotnet test` green;
  `dotnet format BhMaps.slnx --verify-no-changes` clean. Grep for leftovers:
  `git grep -n "HomeViewModel\|HomeView\b\|NavigateHome\|\.Home\b" -- src` returns only `Icon.Home` in
  `Theme\Icons.xaml`.
- [ ] **Step 8: Visual check.** Launch on the dev tree as in A1 step 10 and capture: the first sidebar row reads
  "Maps", it is the page that opens, and its header title reads "Maps".
- [ ] **Step 9:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
refactor(app): Home becomes Maps

Spec 2: "Home" says nothing, "Maps" says what is on the page. Files, class names,
the DataTemplate, the nav command and the title, and nothing else.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A6: the top bar, the sidebar out, Platforms out

Spec 2.1. The biggest task in part A, and it has to be one commit: the shell's search string, its map list and
its ticked set all move at once. Read every step before starting.

**Files:**
- Modify: `src\BhMaps.App\Views\MainWindow.xaml` (whole file), `MainWindow.xaml.cs` (Ctrl+K)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (add `TopBarTab` and the converter instance)
- Modify: `src\BhMaps.App\Theme\Tokens.xaml` (delete `SidebarWidth`, line 20)
- Modify: `src\BhMaps.App\Views\Controls\PageHeader.xaml` (delete the red `TextBlock`, lines 29-34)
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (the sidebar members)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (own `SearchText`, card subscription, ticks)
- Modify: `src\BhMaps.App\ViewModels\MapCardViewModel.cs` (add `IsSelected`)
- Modify: `src\BhMaps.App\ViewModels\Pages\BackgroundsViewModel.cs` (own `SearchText`)
- Modify: `src\BhMaps.App\Views\Pages\BackgroundsView.xaml` (lines 47, 65), `MapsView.xaml` (lines 401, 419)
- Modify: `src\BhMaps.App\App.xaml` (delete the Platforms template, lines 31-33)
- Delete: `src\BhMaps.App\Views\Controls\SuggestBox.xaml`, `SuggestBox.xaml.cs`
- Delete: `src\BhMaps.App\ViewModels\MapListItemViewModel.cs`
- Delete: `src\BhMaps.App\ViewModels\Pages\PlatformsViewModel.cs`, `src\BhMaps.App\Views\Pages\PlatformsView.xaml`, `PlatformsView.xaml.cs`

**Interfaces:**

Consumes: `IsCurrentPageConverter`, `GameProcess`, `MapEntry`.
Produces: `MainViewModel.SelectedMaps : IReadOnlyList<MapEntry>`, `SelectedMapCount`, `NotifySelectionChanged()`,
`MapsViewModel.SearchText`, `MapsViewModel.TickedMaps`, `MapCardViewModel.IsSelected`,
`BackgroundsViewModel.SearchText`, theme key `TopBarTab`.

- [ ] **Step 1: SuggestBox is used nowhere else.** Confirm with
  `git grep -n "SuggestBox" -- src` : only `MainWindow.xaml` line 170 and the control's own two files. Delete
  the two files.

- [ ] **Step 2: the shell's members.** In `MainViewModel`:
  - Delete: `MaxSuggestions`, `_mapListView`, `MapList`, `Suggestions`, `SearchText`, `OnSearchTextChanged`,
    `Suggest`, `MatchesSearch`, `PopulateMapList`, `OnMapItemChanged`, `ChooseSuggestion`, `Platforms`,
    `NavigatePlatforms`, and the `using System.Windows.Data;` and `using System.ComponentModel;` lines if the
    file no longer needs them.
  - `_pages` becomes `[Maps, Backgrounds, Packs, PackDetail, SettingsPage]`.
  - `RescanAsync` drops its `PopulateMapList(snapshot)` call; the comment above the page loop becomes
    `// Maps rebuilds its cards first, because SelectedMaps reads them and a page's Refresh may ask for it.`
    and the loop order is unchanged (`Maps` is the first entry in `_pages`).
  - The ticked set now reads the Maps page:

```csharp
    /// <summary>The ticked maps, in display order (spec 3.3). One list for the whole app: a tile on any page
    /// aims at these. Read from the Maps page's unfiltered cards, not its visible ones, so a search does not
    /// silently shrink what a write is about to touch (plan decision A-D1).</summary>
    public IReadOnlyList<MapEntry> SelectedMaps => Maps.TickedMaps;

    public int SelectedMapCount => SelectedMaps.Count;

    /// <summary>SelectedMaps is computed, so the pages bound to it are told by hand when it changes. Public:
    /// the Maps page raises it when a card is ticked.</summary>
    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedMaps));
        OnPropertyChanged(nameof(SelectedMapCount));
    }

    /// <summary>Unticks every map. The selection bar offers it as "Clear".</summary>
    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var card in Maps.AllCards)
        {
            card.IsSelected = false;
        }
    }
```

  - `AddPicturesTargets` loses its catalog lookup, because `SelectedMaps` is already `MapEntry`:

```csharp
    private IReadOnlyList<MapEntry> AddPicturesTargets(ScanSnapshot snapshot, string? mapFolder) =>
        (mapFolder is null
            ? SelectedMaps
            : [.. new[] { snapshot.Catalog.ByFolder(mapFolder) }.OfType<MapEntry>()])
        .Where(m => m.BackgroundSlots.Count > 0)
        .ToList();
```

- [ ] **Step 3: the card carries the tick.** In `MapCardViewModel`, after `Preview`:

```csharp
    /// <summary>Spec 3.3: whether this map is in the ticked set. The grid's ListBoxItem binds its own IsSelected
    /// to it two ways, so a click, a Ctrl click, a shift range, Space and the tick box all say the same thing.
    /// MapsViewModel watches it and tells the shell.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
```

- [ ] **Step 4: the Maps page owns the search and the ticks.** In `MapsViewModel`:
  - Add `[ObservableProperty] public partial string SearchText { get; set; }` with the summary
    `The header's search box (spec 3.1). The page's own string now; the shell has no search box left.`,
    initialise `SearchText = "";` before `SelectedChip = AllChip;`, and add
    `partial void OnSearchTextChanged(string value) => ApplyFilter();`.
  - Delete the `shell.PropertyChanged += OnShellChanged;` subscription's `SearchText` branch; keep the
    subscription and use it for `SelectedMapCount` (step 5). `Matches` reads `SearchText`, not `Shell.SearchText`.
  - `ShowAll` sets `SearchText = "";` instead of `Shell.SearchText = ""`.
  - Expose the two lists the shell reads:

```csharp
    /// <summary>Every card the last scan produced, filtered or not. The shell's ticked set reads this.</summary>
    public IReadOnlyList<MapCardViewModel> AllCards => _all;

    /// <summary>The ticked maps as catalog entries, in display order.</summary>
    public IReadOnlyList<MapEntry> TickedMaps => _all.Where(c => c.IsSelected).Select(c => c.Map).ToList();
```

  - `Refresh` keeps the ticks across a scan, by folder name, and subscribes to the new cards:

```csharp
        var ticked = _all.Where(c => c.IsSelected).Select(c => c.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in _all)
        {
            old.PropertyChanged -= OnCardChanged;
        }

        _all.Clear();
        foreach (var map in snapshot.Catalog.Maps)
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);

            // Ticked before subscribing, so restoring the set is not mistaken for the user changing it.
            var card = new MapCardViewModel(map, status) { IsSelected = ticked.Contains(map.FolderName) };
            card.PropertyChanged += OnCardChanged;
            _all.Add(card);
        }
```

    followed by the existing `RebuildChips` / `ApplyFilter` / `Selected` / `LoadPreviewsAsync` block, and then
    `Shell.NotifySelectionChanged();` last, because a map that has gone from the catalog has just left the set.
  - The handler:

```csharp
    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapCardViewModel.IsSelected))
        {
            Shell.NotifySelectionChanged();
        }
    }
```

  - `ApplyFilter` protects the ticks from the `ListBox`'s container release (a container being discarded can
    write `IsSelected=false` back through the two-way binding):

```csharp
    private void ApplyFilter()
    {
        var ticked = _all.Where(c => c.IsSelected).Select(c => c.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Cards.Clear();
        foreach (var card in _all.Where(Matches))
        {
            Cards.Add(card);
        }

        // A card the filter just removed loses its container, and a released ListBoxItem clears its own
        // IsSelected, which the two-way binding would write back as an untick. The set is restored by name.
        foreach (var card in _all)
        {
            card.IsSelected = ticked.Contains(card.FolderName);
        }
    }
```

- [ ] **Step 5:** `MapsViewModel`'s shell hook now watches the ticked count, which the chip row and the bar read
  in A7 and A10: `if (e.PropertyName == nameof(MainViewModel.SelectedMapCount)) { RebuildChips(_snapshot?.Catalog); }`
  (A7 gives `RebuildChips` its `Ticked` chip; until then the branch may be empty and the hook is added in A7
  instead, whichever leaves the build clean).
- [ ] **Step 6: Backgrounds keeps its own search.** In `BackgroundsViewModel` add
  `[ObservableProperty] public partial string SearchText { get; set; }` initialised to `""` before the first
  `ApplyFilter`, add `partial void OnSearchTextChanged(string value) => ApplyFilter();`, change `ClearSearch` to
  `SearchText = "";`, delete the `MainViewModel.SearchText` branch of `OnShellChanged`, and make `ApplyFilter`
  read `SearchText`. In `BackgroundsView.xaml` lines 47 and 65 drop
  `DataContext.` and the `RelativeSource` so both bind the page:
  `Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"` and
  `<DataTrigger Binding="{Binding SearchText}" Value="">`. Do the same at `MapsView.xaml` lines 401 and 419, and
  give the `TextBox` there `x:Name="SearchBox"` (Ctrl+K finds it by that name).
- [ ] **Step 7: the tab style.** In `Theme\Controls.xaml` add `xmlns:converters="clr-namespace:BhMaps.App.Converters"`
  to the root element, then, after `ChipToggle`:

```xml
  <converters:IsCurrentPageConverter x:Key="IsCurrentPage" />

  <!-- One of the top bar's four destinations (spec 2.1). A Button, not a ToggleButton: the open page is a
       DataTrigger on the shell's CurrentPage, and a ToggleButton would clobber a one-way IsChecked binding on
       its first click. Tag carries the page this tab opens. -->
  <Style x:Key="TopBarTab" TargetType="Button">
    <Setter Property="FontFamily" Value="{StaticResource Sans}" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Foreground" Value="{StaticResource Text2Brush}" />
    <Setter Property="Padding" Value="12,5" />
    <Setter Property="Margin" Value="0,0,4,0" />
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <ControlTemplate.Resources>
            <!-- See PrimaryButton: the implicit TextBlock style would beat the button's own Foreground. -->
            <DataTemplate DataType="{x:Type sys:String}">
              <TextBlock Text="{Binding}"
                         FontFamily="{Binding FontFamily, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"
                         FontSize="{Binding FontSize, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}"
                         Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type ButtonBase}}}" />
            </DataTemplate>
          </ControlTemplate.Resources>
          <Grid>
            <Border x:Name="Chrome"
                    Background="{TemplateBinding Background}"
                    CornerRadius="{StaticResource Radius}"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Border>
            <Border x:Name="Ring"
                    BorderBrush="{StaticResource TextBrush}"
                    BorderThickness="1"
                    CornerRadius="{StaticResource Radius}"
                    IsHitTestVisible="False"
                    Opacity="0" />
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Ring" Property="Opacity" Value="0.6" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <!-- Hover first, the open page second, so the open tab keeps its fill while the pointer is over it. A
         trigger's binding cannot reach its TemplatedParent, which is why both live out here. -->
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Background" Value="{StaticResource Surface2Brush}" />
        <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
      </Trigger>
      <DataTrigger Value="True">
        <DataTrigger.Binding>
          <MultiBinding Converter="{StaticResource IsCurrentPage}">
            <Binding Path="Tag" RelativeSource="{RelativeSource Self}" />
            <Binding Path="CurrentPage" />
          </MultiBinding>
        </DataTrigger.Binding>
        <Setter Property="Background" Value="{StaticResource TileBrush}" />
        <Setter Property="Foreground" Value="{StaticResource TextBrush}" />
      </DataTrigger>
    </Style.Triggers>
  </Style>
```

  Delete `SidebarWidth` from `Tokens.xaml` and the `IsCurrentPage` instance from `MainWindow.Resources`.

- [ ] **Step 8: the window.** `MainWindow.xaml` becomes exactly this. Its `Window.Resources` is gone with the
  sidebar; `NavRow`, `SidebarDivider` and `MapRow` are deleted with it:

```xml
<Window x:Class="BhMaps.App.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"
        Title="BhMaps" Width="1280" Height="800" MinWidth="1000" MinHeight="640"
        Background="{StaticResource BgBrush}">
  <!-- Nothing on a page triggers a rescan, so the keyboard does; Ctrl+1..4 are the four tabs (spec 2.1).
       Ctrl+K is in the code-behind: it navigates and then focuses, which a command cannot do. -->
  <Window.InputBindings>
    <KeyBinding Key="F5" Command="{Binding RefreshCommand}" />
    <KeyBinding Key="D1" Modifiers="Control" Command="{Binding NavigateMapsCommand}" />
    <KeyBinding Key="D2" Modifiers="Control" Command="{Binding NavigateBackgroundsCommand}" />
    <KeyBinding Key="D3" Modifiers="Control" Command="{Binding NavigatePacksCommand}" />
    <KeyBinding Key="D4" Modifiers="Control" Command="{Binding NavigateSettingsCommand}" />
  </Window.InputBindings>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="44" />
      <RowDefinition Height="*" />
    </Grid.RowDefinitions>

    <!-- Spec 2.1: brand, four tabs, and the game line at the right. 44 px with a hairline under it. -->
    <Border Grid.Row="0"
            Background="{StaticResource BgBrush}"
            BorderBrush="{StaticResource LineBrush}"
            BorderThickness="0,0,0,1">
      <Grid Margin="16,0,12,0">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto" />
          <ColumnDefinition Width="*" />
          <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>

        <TextBlock VerticalAlignment="Center" FontSize="14" FontWeight="SemiBold" Text="BhMaps" />

        <StackPanel Grid.Column="1" Margin="24,0,0,0" VerticalAlignment="Center" Orientation="Horizontal">
          <Button Command="{Binding NavigateMapsCommand}" Content="Maps" Style="{StaticResource TopBarTab}" Tag="{Binding Maps}" />
          <Button Command="{Binding NavigateBackgroundsCommand}" Content="Backgrounds" Style="{StaticResource TopBarTab}" Tag="{Binding Backgrounds}" />
          <Button Command="{Binding NavigatePacksCommand}" Content="Packs" Style="{StaticResource TopBarTab}" Tag="{Binding Packs}" />
          <Button Command="{Binding NavigateSettingsCommand}" Content="Settings" Style="{StaticResource TopBarTab}" Tag="{Binding SettingsPage}" />
        </StackPanel>

        <!-- Three states, one at a time: the folder is gone, the game is up, the game is down. The missing
             folder outranks the other two, so its trigger is the only one that does not test it. -->
        <Grid Grid.Column="2" VerticalAlignment="Center">
          <StackPanel Orientation="Horizontal">
            <StackPanel.Style>
              <Style TargetType="StackPanel">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <DataTrigger Binding="{Binding GameFolderMissing}" Value="True">
                    <Setter Property="Visibility" Value="Visible" />
                  </DataTrigger>
                </Style.Triggers>
              </Style>
            </StackPanel.Style>
            <TextBlock VerticalAlignment="Center"
                       Foreground="{StaticResource MissingFgBrush}"
                       Text="Game folder not found" />
            <Button Margin="8,0,0,0"
                    Command="{Binding NavigateSettingsCommand}"
                    Content="Choose folder"
                    Style="{StaticResource PlainButton}" />
          </StackPanel>

          <StackPanel Orientation="Horizontal">
            <StackPanel.Style>
              <Style TargetType="StackPanel">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                      <Condition Binding="{Binding GameFolderMissing}" Value="False" />
                      <Condition Binding="{Binding GameRunning}" Value="True" />
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible" />
                  </MultiDataTrigger>
                </Style.Triggers>
              </Style>
            </StackPanel.Style>
            <Ellipse Width="6" Height="6" VerticalAlignment="Center" Fill="{StaticResource TextBrush}" />
            <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="Brawlhalla running" />
          </StackPanel>

          <StackPanel Orientation="Horizontal">
            <StackPanel.Style>
              <Style TargetType="StackPanel">
                <Setter Property="Visibility" Value="Collapsed" />
                <Style.Triggers>
                  <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                      <Condition Binding="{Binding GameFolderMissing}" Value="False" />
                      <Condition Binding="{Binding GameRunning}" Value="False" />
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible" />
                  </MultiDataTrigger>
                </Style.Triggers>
              </Style>
            </StackPanel.Style>
            <Ellipse Width="6" Height="6" VerticalAlignment="Center" Fill="{StaticResource Line2Brush}" />
            <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="Brawlhalla not running" />
            <Button Margin="8,0,0,0"
                    Command="{Binding LaunchGameCommand}"
                    Content="Launch"
                    controls:Icon.Glyph="{StaticResource Icon.Play}"
                    Style="{StaticResource PlainButton}" />
          </StackPanel>
        </Grid>
      </Grid>
    </Border>

    <ContentControl x:Name="PageHost" Grid.Row="1" Content="{Binding CurrentPage}" />
  </Grid>
</Window>
```

- [ ] **Step 9: Ctrl+K.** In `MainWindow.xaml.cs` add, and wire `PreviewKeyDown += OnPreviewKeyDown;` in the
  constructor:

```csharp
    /// <summary>Spec 2.1: Ctrl+K reaches the Maps search box from any tab. Navigation is a command, but focus is
    /// not: the page's view does not exist until the ContentControl has built it, so the focus call is queued
    /// behind that at Input priority.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.K || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.NavigateMapsCommand.Execute(null);
        e.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (FindByName(PageHost, "SearchBox") is TextBox box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            });
    }

    /// <summary>The page's search box, by name, through the visual tree: the page is a DataTemplate's content,
    /// so the window's own name scope cannot see it.</summary>
    private static FrameworkElement? FindByName(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { } element && element.Name == name)
            {
                return element;
            }

            if (FindByName(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }
```

  Usings: `System.Windows.Controls`, `System.Windows.Input`, `System.Windows.Media`, `System.Windows.Threading`.

- [ ] **Step 10: PageHeader.** Delete the red `TextBlock` (lines 29-34) and the comment above it that mentions
  the missing-folder notice; keep both `GameFolderMissing` `DataTrigger`s, which still hide the progress and
  done lines. Update the class summary: `the page's own actions on the right, and the shell's progress line or
  done line with Undo between them. The missing-folder notice is the top bar's (spec 2.1).`
- [ ] **Step 11: Platforms.** Delete `PlatformsViewModel.cs`, `PlatformsView.xaml`, `PlatformsView.xaml.cs` and
  the `PlatformsViewModel` `DataTemplate` in `App.xaml`. Confirm nothing is left:
  `git grep -n "Platforms" -- src` returns only `MapPanelViewModel` (`PlatformSets`, `PlatformFiles`,
  `PlatformSetApplier`), `MapsView.xaml`'s panel section, `Icon.Layers` and `PackDetail`'s own platform view.
  `MapPanelViewModel` is untouched by this task.
- [ ] **Step 12:** `dotnet build BhMaps.slnx -c Debug` zero warnings, `dotnet test` green,
  `dotnet format BhMaps.slnx --verify-no-changes` clean.
- [ ] **Step 13: Visual check** on the dev tree. Launch as in A1 step 10, then:

```powershell
Save-Window $h "<SHOTS>\a6-maps.png"
Invoke-El (Find-ByName $w "Packs" "Button"); Start-Sleep 2; Save-Window $h "<SHOTS>\a6-packs.png"
Dump-Tree $w 14 | Set-Content "<CAP>\tree-a6.txt"
```

  Expected: a 44 px bar with "BhMaps", four tabs, the open one filled; no sidebar, so the grid runs the full
  window width; the right end reads "Brawlhalla not running" with Launch; the page header still shows its title
  and its actions; `tree-a6.txt` has no `SuggestBox`, no sidebar `ListBox` and no Platforms tab. Then, by hand
  at the keyboard: Ctrl+2, Ctrl+3, Ctrl+4, Ctrl+1 walk the four tabs, and Ctrl+K from the Packs tab lands on
  Maps with the caret in the search box.
- [ ] **Step 14:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(app): a top bar of four tabs replaces the sidebar

Spec 2.1: brand, Maps, Backgrounds, Packs, Settings, and the game line at the
right, in 44 px. The sidebar's map list was the same 67 names a second time, so
it goes with its search box and its autocomplete; the ticked set now reads the
Maps page's cards. Platforms goes too: a platform set belongs to one map, and
that map's panel is where it lives. Ctrl+1..4 switch tabs, Ctrl+K reaches the
Maps search box.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A7: the Maps header and chip row

Spec 3.1, everything except the grid itself: the zoom in the header with its end glyphs, the density steps, the
tag rules, the Ticked chip, "Select all shown", the empty lines and the first-run line.

**Files:** `MapsViewModel.cs`, `MapCardViewModel.cs`, `MapsView.xaml` (header actions lines 388-437, chip row
lines 440-458, card template lines 484-547, empty state lines 551-571), `Views\Controls\ZoomSlider.xaml`.

**Interfaces produced:**

```csharp
// MapsViewModel
public string SelectAllShownText { get; }   // "Select all shown" / "Select all 12 shown"
public string EmptyText { get; }            // spec 3.1's one line
public bool ShowClearSearch { get; }
public bool ShowFirstRunLine { get; }
public bool ShowName { get; } public bool ShowTag { get; } public bool ShowMissingMark { get; }
public double NameFontSize { get; } public Thickness CardPadding { get; } public Thickness CardMargin { get; }
[RelayCommand] void SelectAllShown();
[RelayCommand] void ClearSearch();
// MapCardViewModel
public string TagText { get; } public bool ShowTag { get; } public string ToolTipText { get; }
```

- [ ] **Step 1: the tag (spec 3.1, H2).** In `MapCardViewModel`, replace the `StateText`/`IsMissing` pair with
  the tag rules, keeping both names (the pack detail page does not use this class, so nothing else reads them):

```csharp
    /// <summary>The longest a custom picture's file name is drawn at (spec 3.1).</summary>
    public const int TagMaxLength = 16;

        StateText = status?.Text ?? "";
        IsMissing = status?.State == MapState.Missing;
        TagText = status?.State switch
        {
            MapState.Missing => "Missing",
            MapState.Packs => status.Text,
            MapState.Custom => Ellipsise(CustomName(map)),
            _ => "",
        };
        ToolTipText = TagText.Length == 0 ? DisplayName : $"{DisplayName} ({TagText})";
```

```csharp
    public bool ShowTag => TagText.Length > 0;

    /// <summary>Which picture is on the map. Part A can only name the slot the game holds; part B replaces this
    /// one expression with CustomPictureLibrary's display name (plan decision A-D5).</summary>
    private static string CustomName(MapEntry map) =>
        map.BackgroundSlots.Count > 0 ? map.BackgroundSlots[0] : "Custom";

    private static string Ellipsise(string name) =>
        name.Length <= TagMaxLength ? name : name[..(TagMaxLength - 1)] + "\u2026";
```

  Add `using BhMaps.Core.Maps;` if it is not already there.
- [ ] **Step 2: density (spec 3.1).** In `MapsViewModel`, six computed properties and one notification, so the
  card template reads numbers rather than carrying a pile of triggers:

```csharp
    /// <summary>Spec 3.1's density steps. At 7 and 8 the name shrinks and the tag goes; at 9 and 10 the name row
    /// goes with it, the card tightens to 4 px padding and 8 px gaps, and Missing becomes a mark on the picture.</summary>
    public bool ShowName => Zoom <= 8;
    public bool ShowTag => Zoom <= 6;
    public bool ShowMissingMark => Zoom >= 9;
    public double NameFontSize => Zoom <= 6 ? 13 : 12;
    public Thickness CardPadding => Zoom <= 8 ? new Thickness(8) : new Thickness(4);
    public Thickness CardMargin => Zoom <= 8 ? new Thickness(0, 0, 12, 12) : new Thickness(0, 0, 8, 8);
```

  and in `OnZoomChanged`, after the settings write:
  `OnPropertyChanged(nameof(ShowName)); OnPropertyChanged(nameof(ShowTag)); OnPropertyChanged(nameof(ShowMissingMark)); OnPropertyChanged(nameof(NameFontSize)); OnPropertyChanged(nameof(CardPadding)); OnPropertyChanged(nameof(CardMargin));`
  (`using System.Windows;` for `Thickness`).
- [ ] **Step 3: chips.** `RebuildChips` takes the ticked count into account and `Matches` learns two chips:

```csharp
    private const string TickedChip = "Ticked";

    // wanted list, spec 3.1: All, the set chips, Changed, and Ticked once anything is ticked.
    List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label), ChangedChip];
    if (Shell.SelectedMapCount > 0)
    {
        wanted.Add(TickedChip);
    }
```

  `Matches`: `TickedChip => card.IsSelected`, and `ChangedChip => IsChanged(card)` where `IsChanged` becomes
  `status.State is MapState.Packs or MapState.Custom` ("everything that has a tag other than Missing", spec 3.1).
  Add the shell hook from A6 step 5 so ticking the first map makes the chip appear:
  `if (e.PropertyName == nameof(MainViewModel.SelectedMapCount)) { RebuildChips(_snapshot?.Catalog); OnPropertyChanged(nameof(SelectAllShownText)); }`,
  guarding `RebuildChips` against a null catalog before the first scan. The `Ticked` chip does **not** re-filter
  as ticks change (decision A-D4).
- [ ] **Step 4: the rest of the page's strings and commands.**

```csharp
    /// <summary>Spec 3.1: the chip row's teaching button. It names the count whenever the grid is filtered.</summary>
    public string SelectAllShownText =>
        Cards.Count == _all.Count ? "Select all shown" : $"Select all {Cards.Count} shown";

    /// <summary>Spec 3.1: why the grid is empty, in one line.</summary>
    public string EmptyText
    {
        get
        {
            var what = SelectedChip switch
            {
                AllChip => "map",
                ChangedChip => "changed map",
                TickedChip => "ticked map",
                _ => $"{SelectedChip.ToLowerInvariant()} map",
            };
            if (SearchText.Length > 0)
            {
                return $"No {what} named '{SearchText}'";
            }

            return SelectedChip == ChangedChip
                ? "No map is changed. Every map matches the Default pack."
                : $"No {what} to show.";
        }
    }

    public bool ShowClearSearch => SearchText.Length > 0;

    /// <summary>Spec 3.1's first-run line: nothing in the library but the Default pack. Part B ands in the
    /// custom-picture half (plan decision A-D6).</summary>
    public bool ShowFirstRunLine =>
        _snapshot is { } s && !s.Packs.Any(p => !p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void SelectAllShown()
    {
        foreach (var card in Cards)
        {
            card.IsSelected = true;
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";
```

  `ApplyFilter` ends with
  `OnPropertyChanged(nameof(SelectAllShownText)); OnPropertyChanged(nameof(EmptyText)); OnPropertyChanged(nameof(ShowClearSearch));`
  and `Refresh` adds `OnPropertyChanged(nameof(ShowFirstRunLine));`. Delete `ShowAllCommand`, which the empty
  state no longer offers.

- [ ] **Step 5: the zoom slider's end glyphs (spec 3.1).** In `ZoomSlider.xaml` wrap the slider between two
  squares, large at the two end and small at the ten end, with the number after them. Everything else in the
  control and its code-behind is unchanged:

```xml
  <StackPanel VerticalAlignment="Center" Orientation="Horizontal">
    <!-- The glyphs say which end is which: a big card at the Minimum end, a small one at the Maximum end. -->
    <Rectangle Width="10" Height="10" Margin="0,0,8,0" VerticalAlignment="Center" Fill="{StaticResource Text3Brush}" RadiusX="2" RadiusY="2" />
    <Slider AutomationProperties.Name="Columns" ... unchanged ... />
    <Rectangle Width="6" Height="6" Margin="8,0,0,0" VerticalAlignment="Center" Fill="{StaticResource Text3Brush}" RadiusX="1" RadiusY="1" />
    <TextBlock Margin="8,0,0,0" VerticalAlignment="Center" Style="{StaticResource MonoText}" Text="{Binding Value, ElementName=Root}" />
  </StackPanel>
```

- [ ] **Step 6: the header.** In `MapsView.xaml`'s `PageHeader.Actions` `StackPanel`, keep the 200 px search grid
  as it is (with `x:Name="SearchBox"` from A6), then insert the zoom slider between it and Reset all to default:

```xml
          <controls:ZoomSlider Margin="16,0,0,0"
                               VerticalAlignment="Center"
                               Maximum="{x:Static pages:MapsViewModel.MaxZoom}"
                               Minimum="{x:Static pages:MapsViewModel.MinZoom}"
                               Value="{Binding Zoom}" />
```

  and delete the `ZoomSlider` from the chip row (lines 453-457).
- [ ] **Step 7: the chip row.** The row keeps its chip `ListBox` on the left and gains the teaching button on the
  right, plus the first-run line under it:

```xml
    <Grid DockPanel.Dock="Top" Margin="0,4,0,8">
      <ListBox ... unchanged chip ListBox ... />
      <Button HorizontalAlignment="Right"
              VerticalAlignment="Center"
              Command="{Binding SelectAllShownCommand}"
              Content="{Binding SelectAllShownText}"
              controls:Icon.Glyph="{StaticResource Icon.Check}"
              Style="{StaticResource PlainButton}" />
    </Grid>

    <!-- Spec 3.1: the only line a first-timer needs, and it goes when a pack arrives. -->
    <TextBlock DockPanel.Dock="Top"
               Margin="0,0,0,12"
               Foreground="{StaticResource Text2Brush}"
               TextWrapping="Wrap"
               Visibility="{Binding ShowFirstRunLine, Converter={StaticResource BoolToVis}}">
      <Run Text="No packs yet. Import a folder of map art on the " /><Hyperlink Command="{Binding DataContext.NavigatePacksCommand, RelativeSource={RelativeSource AncestorType=Window}}" Foreground="{StaticResource TextBrush}"><Run Text="Packs" /></Hyperlink><Run Text=" page, or add a custom image on " /><Hyperlink Command="{Binding DataContext.NavigateBackgroundsCommand, RelativeSource={RelativeSource AncestorType=Window}}" Foreground="{StaticResource TextBrush}"><Run Text="Backgrounds" /></Hyperlink><Run Text="." />
    </TextBlock>
```

  `Icon.Check` does not exist yet: add it to `Theme\Icons.xaml` beside `Icon.X` as
  `<Geometry x:Key="Icon.Check">M5 12l5 5l9 -9</Geometry>`.
- [ ] **Step 8: the card.** In the card's `DataTemplate`, bind the density: the outer `Border`'s `Padding` to
  `{Binding DataContext.CardPadding, RelativeSource={RelativeSource AncestorType=ItemsControl}}`, the name
  `TextBlock`'s `FontSize` to `DataContext.NameFontSize` and its `Visibility` to `DataContext.ShowName` through
  `BoolToVis`, and the tag `Grid`'s `Visibility` to `DataContext.ShowTag` (the two inner wrappers keep their own
  `IsMissing` and `TagText` triggers, now reading `TagText` instead of `StateText`). Add, inside the preview
  `Grid` and after the `Image`, the 10 px Missing mark for the two tightest steps:

```xml
                        <Border Width="10"
                                Height="10"
                                Margin="4"
                                HorizontalAlignment="Right"
                                VerticalAlignment="Top"
                                Background="{StaticResource MissingFgBrush}"
                                CornerRadius="5">
                          <Border.Style>
                            <Style TargetType="Border">
                              <Setter Property="Visibility" Value="Collapsed" />
                              <Style.Triggers>
                                <MultiDataTrigger>
                                  <MultiDataTrigger.Conditions>
                                    <Condition Binding="{Binding IsMissing}" Value="True" />
                                    <Condition Binding="{Binding DataContext.ShowMissingMark, RelativeSource={RelativeSource AncestorType=ItemsControl}}" Value="True" />
                                  </MultiDataTrigger.Conditions>
                                  <Setter Property="Visibility" Value="Visible" />
                                </MultiDataTrigger>
                              </Style.Triggers>
                            </Style>
                          </Border.Style>
                        </Border>
```

- [ ] **Step 9: the empty state** replaces the "No maps match this filter." block:

```xml
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
          ... the existing Cards.Count = 0 Style, unchanged ...
          <TextBlock HorizontalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="{Binding EmptyText}" TextWrapping="Wrap" />
          <Button Margin="0,8,0,0"
                  HorizontalAlignment="Center"
                  Command="{Binding ClearSearchCommand}"
                  Content="Clear search"
                  Style="{StaticResource PlainButton}"
                  Visibility="{Binding ShowClearSearch, Converter={StaticResource BoolToVis}}" />
        </StackPanel>
```

- [ ] **Step 10:** Build, test and format clean, then **visual check** on the dev tree: capture at zoom 6, 8 and
  10 (`Set-Range (Find-ByName $w "Columns" "Slider") 8`), and with the Changed chip. Expected: the slider sits in
  the header row and does not move when chips change; at 8 the names are smaller and no tags are drawn; at 10
  there is no name row, the cards are tight, and a missing map wears a small red dot; "Select all 12 shown"
  counts with the chip; the first-run line shows only while the dev library has no pack but Default.
- [ ] **Step 11:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(app): the Maps header, chip row and card density

Spec 3.1: zoom 2 to 10 in the header row with end glyphs so chips never move it,
tags that name the pack or the picture and nothing for Default, the Ticked chip,
"Select all shown" with its live count, the empty lines, and the first-run line.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A8: the grid is a ListBox, with ticks

Spec 3.1 and 3.3. Extended selection comes from the control rather than being hand-rolled: Ctrl+click toggles,
Shift+click ranges, arrows move focus, Ctrl+A ticks all shown. A plain click opens the panel.

**Files:** `MapsView.xaml` (the grid, lines 462-549), `MapsView.xaml.cs` (three handlers),
`Theme\Controls.xaml` (`CardTick`), `MapsViewModel.cs` (`Escape`).

- [ ] **Step 1: `CardTick` in `Theme\Controls.xaml`,** after `ChipToggle`. An 18 px rounded box, `Bg` fill with a
  `Line2` hairline, filled `Text` with a `Bg` tick when checked, `TextBrush` border on hover. No focus visual:
  the box is `Focusable="False"` and the card behind it is the tab stop.

```xml
  <Style x:Key="CardTick" TargetType="CheckBox">
    <Setter Property="Cursor" Value="Hand" />
    <Setter Property="FocusVisualStyle" Value="{x:Null}" />
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="CheckBox">
          <Border x:Name="Box" Width="18" Height="18" Background="{StaticResource BgBrush}"
                  BorderBrush="{StaticResource Line2Brush}" BorderThickness="1" CornerRadius="4" Opacity="0.95">
            <Path x:Name="Tick" Width="10" Height="7" HorizontalAlignment="Center" VerticalAlignment="Center"
                  Data="M0,3.4 L2.8,6.2 L8,0.6" Stretch="Uniform" Stroke="{StaticResource BgBrush}"
                  StrokeEndLineCap="Round" StrokeLineJoin="Round" StrokeStartLineCap="Round"
                  StrokeThickness="1.6" Visibility="Collapsed" />
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Box" Property="Background" Value="{StaticResource TextBrush}" />
              <Setter TargetName="Box" Property="BorderBrush" Value="{StaticResource TextBrush}" />
              <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="{StaticResource TextBrush}" />
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
```

- [ ] **Step 2: the container style** in `MapsView.xaml`'s resources, replacing the `MapCard` `Button` style. The
  ring the ticked card wears is `TileListBoxItem`'s own, drawn in `Text` at full opacity when `IsSelected`:

```xml
    <!-- The card is a ListBoxItem, so Ctrl and Shift clicks, Space, the arrow keys and Ctrl+A are the control's
         (spec 3.1). A plain click is the page's: it opens the panel and leaves the ticks alone. -->
    <Style x:Key="MapCardItem" TargetType="ListBoxItem" BasedOn="{StaticResource TileListBoxItem}">
      <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />
      <Setter Property="Margin" Value="{Binding DataContext.CardMargin, RelativeSource={RelativeSource AncestorType=ListBox}}" />
      <Setter Property="Cursor" Value="Hand" />
      <Setter Property="AutomationProperties.Name" Value="{Binding DisplayName}" />
      <Setter Property="ToolTip" Value="{Binding ToolTipText}" />
      <EventSetter Event="PreviewMouseLeftButtonDown" Handler="OnCardMouseDown" />
      <EventSetter Event="PreviewKeyDown" Handler="OnCardKeyDown" />
    </Style>
```

- [ ] **Step 3: the grid.** Replace the `ScrollViewer` plus `ItemsControl` (lines 469-549) with one `ListBox`.
  The item template is the card from A7 with its `Button` root swapped for a `Border` (`Style="{StaticResource CardBorder}"`,
  `Padding` bound as in A7 step 8), and the tick box added over the preview:

```xml
        <ListBox ItemContainerStyle="{StaticResource MapCardItem}"
                 ItemsSource="{Binding Cards}"
                 KeyboardNavigation.DirectionalNavigation="Contained"
                 ScrollViewer.CanContentScroll="False"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                 ScrollViewer.VerticalScrollBarVisibility="Auto"
                 SelectionMode="Extended"
                 Style="{StaticResource TileListBox}">
          <ListBox.ItemsPanel>
            <ItemsPanelTemplate>
              <!-- Top aligned, or a short list comes out with enormous cards. A UniformGrid does not virtualise,
                   which is the point: every container is realised, so the IsSelected binding and Ctrl+A are exact
                   (plan decision A-D10). -->
              <UniformGrid VerticalAlignment="Top"
                           Columns="{Binding DataContext.Zoom, RelativeSource={RelativeSource AncestorType=ListBox}}" />
            </ItemsPanelTemplate>
          </ListBox.ItemsPanel>
          <ListBox.ItemTemplate>
            <DataTemplate>
              ... the A7 card, with this inside the preview Grid, above the Image ...
              <CheckBox x:Name="Tick" Margin="6" HorizontalAlignment="Left" VerticalAlignment="Top"
                        AutomationProperties.Name="Ticked" Focusable="False"
                        IsChecked="{Binding IsSelected, Mode=TwoWay}" Style="{StaticResource CardTick}"
                        Visibility="Collapsed" />
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
```

  and in the template's triggers, reveal the box on hover or when it is ticked (spec 3.1):

```xml
              <DataTemplate.Triggers>
                <DataTrigger Binding="{Binding IsSelected}" Value="True">
                  <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
                </DataTrigger>
                <DataTrigger Binding="{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=ListBoxItem}}" Value="True">
                  <Setter TargetName="Tick" Property="Visibility" Value="Visible" />
                </DataTrigger>
              </DataTemplate.Triggers>
```

- [ ] **Step 4: the three handlers** in `MapsView.xaml.cs`:

```csharp
    /// <summary>Spec 3.1: a plain click on the card body opens the map panel and leaves the ticks alone; a Ctrl
    /// or Shift click is the ListBox's, and so is a click on the tick box itself.</summary>
    private void OnCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item || Keyboard.Modifiers != ModifierKeys.None || IsInsideTick(e.OriginalSource))
        {
            return;
        }

        // Focus by hand, because handling the event is what stops the ListBox from moving focus itself, and the
        // arrow keys have to carry on from the card that was clicked.
        item.Focus();
        e.Handled = true;
        if (item.DataContext is MapCardViewModel card && DataContext is MapsViewModel page)
        {
            page.OpenMapCommand.Execute(card.FolderName);
        }
    }

    /// <summary>Space toggles the focused card (spec 3.1). Extended selection would otherwise make Space replace
    /// the whole set with that one card.</summary>
    private void OnCardKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && sender is ListBoxItem item)
        {
            item.IsSelected = !item.IsSelected;
            e.Handled = true;
        }
    }

    private static bool IsInsideTick(object? source)
    {
        for (var d = source as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is CheckBox)
            {
                return true;
            }
        }

        return false;
    }
```

- [ ] **Step 5: Escape.** On `MapsView`'s root `UserControl`, `<UserControl.InputBindings><KeyBinding Key="Escape" Command="{Binding EscapeCommand}" /></UserControl.InputBindings>`,
  and in `MapsViewModel`:

```csharp
    /// <summary>Spec 3.2's order: Escape closes the panel first, and a second Escape clears the ticks.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (Panel is not null)
        {
            Selected = null;
            return;
        }

        Shell.ClearSelectionCommand.Execute(null);
    }
```

- [ ] **Step 6:** Build, test and format clean. **Visual check** on the dev tree with a real mouse and keyboard,
  and a capture of each: (a) hovering a card reveals its tick box, (b) clicking the box ticks the card and leaves
  the panel closed, (c) a plain click on the card body opens the panel and ticks nothing, (d) Ctrl+click ticks a
  second card, (e) Shift+click from it ticks the range, (f) Ctrl+A with the Ranked 1v1 chip ticks exactly the
  shown cards and no others (check the count in `SelectedMapCount` through the selection bar in A10, or
  `Find-AllByType $w "ListItem"` and count `SelectionItem.IsSelected`), (g) Space toggles the focused card,
  (h) Escape closes the panel, a second Escape clears the ticks, (i) ticked cards wear a `Text` outline, and
  (j) after a rescan (F5) the same maps are still ticked.
- [ ] **Step 7:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(app): the map grid is a ListBox with ticks

Spec 3.1 and 3.3: SelectionMode=Extended, so Ctrl and Shift clicks, the arrow
keys and Ctrl+A come from the control rather than from hand-rolled code. The tick
box on each card binds the same IsSelected; a plain click on the card body opens
the panel and leaves the ticks alone; Space toggles; Escape closes the panel
first and clears the ticks second.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A9: Core support for applying a pack to the ticked maps

Spec 3.3 defines it: for each ticked map, the pack's files for that map's folder, plus the pack's pictures for
that map's background slots, and nothing else in the pack. Core only, so it is tested before any UI uses it.

**Files:** `src\BhMaps.Core\Operations\PackApplier.cs`, new `src\BhMaps.Core\Operations\CustomPictureLibrary.cs`,
`tests\BhMaps.Core.Tests\PackApplierTests.cs`.

- [ ] **Step 1:** Add to `PackApplier` (it gains `using BhMaps.Core.Maps;`):

```csharp
    /// <summary>Spec 3.3: the pack, aimed at named maps. For each map it copies the pack's files for that map's
    /// folder and the pack's Backgrounds files whose names are that map's slots, and nothing else in the pack. A
    /// map the pack has nothing for is skipped rather than failed: a pack covers the maps it covers.</summary>
    public static ApplyResult ApplyToMaps(
        Pack pack,
        IReadOnlyList<MapEntry> maps,
        string gamePath,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var copied = 0;
        var failures = new List<FileFailure>();
        var backgrounds = pack.FindFolder(BackgroundsFolder);
        foreach (var map in maps)
        {
            ct.ThrowIfCancellationRequested();
            if (pack.FindFolder(map.FolderName) is { } folder)
            {
                CopyFolder(folder, gamePath, progress, ct, ref copied, failures);
            }

            foreach (var file in SlotFiles(backgrounds, map))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{BackgroundsFolder}\{file.Name}");
                CopyOne(file, Path.Combine(gamePath, BackgroundsFolder), ref copied, failures);
            }
        }

        return new ApplyResult(copied, failures);
    }

    /// <summary>The relative paths <see cref="ApplyToMaps"/> would write, for the undo snapshot (spec 6.6).</summary>
    public static IReadOnlyList<string> ApplyToMapsPaths(Pack pack, IReadOnlyList<MapEntry> maps)
    {
        var backgrounds = pack.FindFolder(BackgroundsFolder);
        return maps
            .SelectMany(map =>
                (pack.FindFolder(map.FolderName)?.Files.Select(f => Path.Combine(map.FolderName, f.Name))
                 ?? Array.Empty<string>())
                .Concat(SlotFiles(backgrounds, map).Select(f => Path.Combine(BackgroundsFolder, f.Name))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The pack's pictures for one map's slots, in slot order, each at most once.</summary>
    private static IEnumerable<GameFile> SlotFiles(GameFolder? backgrounds, MapEntry map) =>
        backgrounds is null
            ? []
            : map.BackgroundSlots
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(backgrounds.FindFile)
                .OfType<GameFile>();

    private const string BackgroundsFolder = "Backgrounds";
```

- [ ] **Step 2:** Create `CustomPictureLibrary.cs` with the record alone (decision A-D11):

```csharp
namespace BhMaps.Core.Operations;

/// <summary>One picture of the user's own, however many maps it is on: the library copies of it, the in-game
/// slots holding it, and the pack it lives in when it has one. Part A consumes the type for the Apply picture
/// menu; part B builds the library that fills it (spec 4).</summary>
public sealed record CustomPicture(
    string Hash,
    string DisplayName,
    IReadOnlyList<string> LibraryPaths,
    IReadOnlyList<string> InGameSlots,
    string? PackName);
```

- [ ] **Step 3:** Add to `PackApplierTests.cs` (add `using BhMaps.Core.LevelData;` and `using BhMaps.Core.Maps;`):

```csharp
    private static MapEntry Map(string folder, params string[] slots) =>
        new(folder, folder, new LevelDesc(folder, folder, new CameraBounds(0, 0, 100, 100), [], []), [], [],
            slots, []);

    [Fact]
    public void ApplyToMaps_CopiesEachMapsFolderAndOnlyItsOwnSlots()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);
        var progress = new List<string>();

        var result = PackApplier.ApplyToMaps(
            pack, [Map("BloodMoon", "BG_Sewer.jpg")], game, new SyncProgress(progress), CancellationToken.None);

        Assert.Equal(3, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("new-a", Read(game, "BloodMoon", "A.png"));
        Assert.Equal("new-b", Read(game, "BloodMoon", "B.png"));
        Assert.Equal("new-bg", Read(game, "Backgrounds", "BG_Sewer.jpg"));
        Assert.Contains(progress, p => p.Contains("BG_Sewer.jpg"));
    }

    [Fact]
    public void ApplyToMaps_LeavesOutSlotsAndFoldersTheMapsDoNotName()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyToMaps(pack, [Map("BloodMoon")], game, null, CancellationToken.None);

        Assert.Equal(2, result.Copied);
        Assert.False(File.Exists(Path.Combine(game, "Backgrounds", "BG_Sewer.jpg")));
        Assert.Equal("old-mud", Read(game, "Swamp", "Mud1.png"));
    }

    [Fact]
    public void ApplyToMaps_SkipsAMapThePackHasNothingFor()
    {
        using var tmp = new TempDir();
        var (pack, game) = Arrange(tmp);

        var result = PackApplier.ApplyToMaps(pack, [Map("Swamp", "BG_Nope.jpg")], game, null, CancellationToken.None);

        Assert.Equal(0, result.Copied);
        Assert.Empty(result.Failures);
        Assert.Equal("old-mud", Read(game, "Swamp", "Mud1.png"));
    }

    [Fact]
    public void ApplyToMapsPaths_NamesEveryFileTheApplyWouldWriteOnceEach()
    {
        using var tmp = new TempDir();
        var (pack, _) = Arrange(tmp);

        var paths = PackApplier.ApplyToMapsPaths(
            pack, [Map("BloodMoon", "BG_Sewer.jpg"), Map("Swamp", "BG_Sewer.jpg")]);

        Assert.Equal(
            new[]
            {
                Path.Combine("BloodMoon", "A.png"),
                Path.Combine("BloodMoon", "B.png"),
                Path.Combine("Backgrounds", "BG_Sewer.jpg"),
            },
            paths);
    }
```

- [ ] **Step 4:** `dotnet test tests\BhMaps.Core.Tests --filter "FullyQualifiedName~PackApplierTests"` is green,
  then `dotnet build BhMaps.slnx -c Debug` and `dotnet format BhMaps.slnx --verify-no-changes` are clean.
- [ ] **Step 5:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(core): apply a pack to named maps

Spec 3.3: for each map, the pack's files for that map's folder plus the pack's
pictures for that map's background slots, and nothing else in the pack. A map the
pack has nothing for is skipped, not failed. ApplyToMapsPaths names the same set
for the undo snapshot. Adds the CustomPicture record part B's library fills.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

# Task A10: the selection bar

Spec 3.3 and the copy in spec 11. The bar floats at the bottom of the grid while anything is ticked.

**Files:** `MapsViewModel.cs`, `MapsView.xaml` (the grid cell), `MapsView.xaml.cs` (the menu opener),
`Theme\Controls.xaml` (`SelectionBar`), `Views\Pages\BackgroundsView.xaml` (its local `SelectionBar` style goes).

- [ ] **Step 1: the shared style.** Move `SelectionBar` out of `BackgroundsView.xaml` (lines 19-28) into
  `Theme\Controls.xaml`, without the page-specific visibility trigger, and give each page the visibility:

```xml
  <!-- The bar a page floats over its grid while maps are ticked (spec 3.3). Visibility belongs to the page. -->
  <Style x:Key="SelectionBar" TargetType="Border" BasedOn="{StaticResource CardBorder}">
    <Setter Property="Padding" Value="12,8" />
  </Style>
```

  In `BackgroundsView.xaml` the bar's `Border` keeps `Style="{StaticResource SelectionBar}"` and gains
  `Margin="0,12,0,0"` and `Visibility="{Binding HasSelection, Converter={StaticResource BoolToVis}}"`; the page's
  local style is deleted (a local style keyed the same name cannot be `BasedOn` itself).
- [ ] **Step 2: the page's members.** In `MapsViewModel`:

```csharp
    /// <summary>Spec 11: "3 of 67 maps ticked". The second number is every map, not the shown ones.</summary>
    public string SelectionText => $"{Shell.SelectedMapCount} of {_all.Count} maps ticked";

    public bool HasTicks => Shell.SelectedMapCount > 0;

    /// <summary>The packs the Apply pack menu offers, in library order.</summary>
    public IReadOnlyList<Pack> PackChoices => _snapshot?.Packs ?? [];

    /// <summary>The Apply picture menu: the library's custom pictures, then "Add Custom Image..." (spec 3.3).
    /// Part A leaves the list empty; part B fills it from CustomPictureLibrary and changes nothing else here.</summary>
    public IReadOnlyList<PictureMenuItem> PictureChoices { get; private set; } = [];
```

  with, in the same file's namespace:

```csharp
/// <summary>One row of the Apply picture menu. A null Picture is the "Add Custom Image..." row.</summary>
public sealed record PictureMenuItem(string Header, CustomPicture? Picture);
```

  `Refresh` sets `PictureChoices = [.. CustomPictures().Select(p => new PictureMenuItem(p.DisplayName, p)), new PictureMenuItem("Add Custom Image...", null)];`
  where `private static IReadOnlyList<CustomPicture> CustomPictures() => [];` is the one line part B replaces
  with `CustomPictureLibrary.Build(snapshot)`. Raise `PackChoices` and `PictureChoices` at the end of `Refresh`,
  and `SelectionText`/`HasTicks` from the `SelectedMapCount` hook added in A7 step 3.

- [ ] **Step 3: the three writes.** All go through `RunGameWriteAsync` with `clearTicks: true`, and all confirm
  only when more than one map is ticked (spec 3.3, spec 8):

```csharp
    /// <summary>Spec 3.3: the pack, on the ticked maps only.</summary>
    [RelayCommand]
    private async Task ApplyPackToTickedAsync(Pack? pack)
    {
        var maps = Shell.SelectedMaps;
        if (pack is null || maps.Count == 0 || !Confirm($"Apply {pack.Name}", $"Apply {pack.Name}", maps))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        ApplyResult? result = null;
        await Shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            PackApplier.ApplyToMapsPaths(pack, maps),
            (progress, ct) => Task.Run(
                () => { result = PackApplier.ApplyToMaps(pack, maps, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {MainViewModel.Count(maps.Count, "map")}",
            clearTicks: true);

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>Spec 3.3: one picture into every ticked map's own slots. The last menu row has no picture: it
    /// opens Add Custom Image with the ticked maps as its target (spec 7.1), which is the shell's flow.</summary>
    [RelayCommand]
    private async Task ApplyPictureToTickedAsync(PictureMenuItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Picture is not { } picture)
        {
            await Shell.OpenAddPicturesAsync(null);
            return;
        }

        var maps = Shell.SelectedMaps.Where(m => m.BackgroundSlots.Count > 0).ToList();
        if (maps.Count == 0)
        {
            // Without level data a map has no slots at all (spec 3.6), so there is nowhere to write.
            Shell.Dialogs.Info(
                "Nothing to apply to",
                "The ticked maps have no background slots. Slots come from the game's own level data.");
            return;
        }

        if (!Confirm("Apply picture", $"Apply {picture.DisplayName}", maps))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        var source = picture.LibraryPaths[0];
        var failures = new List<FileFailure>();
        await Shell.RunGameWriteAsync(
            $"Applying {picture.DisplayName}",
            maps.SelectMany(m => BackgroundApplier.TargetPaths(m.BackgroundSlots))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in maps)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(
                            BackgroundApplier.Apply(source, gamePath, map.BackgroundSlots, null, ct).Failures);
                    }
                },
                ct),
            $"{picture.DisplayName} applied to {MainViewModel.Count(maps.Count, "map")}",
            clearTicks: true);

        Shell.Dialogs.ShowFailures("Some pictures could not be applied", failures);
    }

    /// <summary>Spec 3.3: the ticked maps back to the Default pack, the same reset one map's panel offers.</summary>
    [RelayCommand(CanExecute = nameof(CanResetTicked))]
    private async Task ResetTickedAsync()
    {
        var maps = Shell.SelectedMaps;
        if (_snapshot?.DefaultPack is not { } defaultPack || maps.Count == 0
            || !Confirm("Reset to default", "Reset", maps))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        var failures = new List<FileFailure>();
        await Shell.RunGameWriteAsync(
            "Resetting",
            maps.SelectMany(m => ResetPathsFor(m, defaultPack)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in maps)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(
                            MapReset.ResetMap(gamePath, map.FolderName, map.BackgroundSlots, defaultPack).Failures);
                    }
                },
                ct),
            $"Reset {MainViewModel.Count(maps.Count, "map")} to default",
            clearTicks: true);

        Shell.Dialogs.ShowFailures("Some files could not be reset", failures);
    }

    private bool CanResetTicked() => _snapshot?.DefaultPack is not null;

    /// <summary>Spec 3.3: a write to more than one map names the count and the maps first; one map is one click.</summary>
    private bool Confirm(string title, string verb, IReadOnlyList<MapEntry> maps) =>
        maps.Count <= 1
        || Shell.Dialogs.Confirm(
            title,
            $"{verb} to these {maps.Count} maps?\n\n{string.Join(", ", maps.Select(m => m.DisplayName))}");

    /// <summary>Everything a reset of one map overwrites: the folder's files now, the Default pack's files for
    /// it, and the slots the pack can actually restore (spec 6.6). Mirrors MapPanelViewModel.ResetPaths.</summary>
    private IEnumerable<string> ResetPathsFor(MapEntry map, Pack defaultPack)
    {
        var backgrounds = defaultPack.FindFolder("Backgrounds");
        var slots = map.BackgroundSlots.Where(s => backgrounds?.FindFile(s) is not null).ToList();
        var current = _snapshot?.Tree.FindFolder(map.FolderName)?.Files
            .Select(f => Path.Combine(map.FolderName, f.Name)) ?? [];
        return current
            .Concat(PlatformSetApplier.TargetPaths(defaultPack, map.FolderName))
            .Concat(BackgroundApplier.TargetPaths(slots));
    }
```

- [ ] **Step 4: the bar.** Last child of the grid's cell `Grid` in `MapsView.xaml`, so it floats over the cards:

```xml
        <Border Margin="0,0,0,16"
                HorizontalAlignment="Center"
                VerticalAlignment="Bottom"
                Style="{StaticResource SelectionBar}"
                Visibility="{Binding HasTicks, Converter={StaticResource BoolToVis}}">
          <StackPanel Orientation="Horizontal">
            <TextBlock Margin="4,0,12,0" VerticalAlignment="Center" Foreground="{StaticResource Text2Brush}" Text="{Binding SelectionText}" />
            <Button Click="OnOpenMenu" Content="Apply pack" Style="{StaticResource OutlineButton}"
                    IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}">
              <Button.ContextMenu>
                <ContextMenu ItemsSource="{Binding PackChoices}">
                  <ContextMenu.ItemContainerStyle>
                    <Style TargetType="MenuItem">
                      <Setter Property="Header" Value="{Binding Name}" />
                      <Setter Property="Command" Value="{Binding DataContext.ApplyPackToTickedCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
                      <Setter Property="CommandParameter" Value="{Binding}" />
                    </Style>
                  </ContextMenu.ItemContainerStyle>
                </ContextMenu>
              </Button.ContextMenu>
            </Button>
            <Button Margin="8,0,0,0" Click="OnOpenMenu" Content="Apply picture" Style="{StaticResource OutlineButton}"
                    IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}">
              <Button.ContextMenu>
                <ContextMenu ItemsSource="{Binding PictureChoices}">
                  <ContextMenu.ItemContainerStyle>
                    <Style TargetType="MenuItem">
                      <Setter Property="Header" Value="{Binding Header}" />
                      <Setter Property="Command" Value="{Binding DataContext.ApplyPictureToTickedCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
                      <Setter Property="CommandParameter" Value="{Binding}" />
                    </Style>
                  </ContextMenu.ItemContainerStyle>
                </ContextMenu>
              </Button.ContextMenu>
            </Button>
            <Button Margin="8,0,0,0" Command="{Binding ResetTickedCommand}" Content="Reset to default"
                    controls:Icon.Glyph="{StaticResource Icon.Undo}" Style="{StaticResource OutlineButton}"
                    IsEnabled="{Binding DataContext.CanWrite, RelativeSource={RelativeSource AncestorType=Window}}" />
            <Button Margin="12,0,0,0" Command="{Binding SelectAllShownCommand}" Content="Select all shown" Style="{StaticResource PlainButton}" />
            <Button Command="{Binding DataContext.ClearSelectionCommand, RelativeSource={RelativeSource AncestorType=Window}}" Content="Clear" Style="{StaticResource PlainButton}" />
          </StackPanel>
        </Border>
```

- [ ] **Step 5: the opener** in `MapsView.xaml.cs`. A `ContextMenu` is not in the visual tree, so it is handed
  the button's `DataContext`; that is what makes the bindings above resolve:

```csharp
    /// <summary>Opens a bar button's menu on a left click, above the bar. The menu lives outside the visual
    /// tree, so it is given the button's DataContext: its items bind the page through it.</summary>
    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = button.DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Top;
            menu.IsOpen = true;
        }
    }
```

- [ ] **Step 6:** Build, test and format clean. **Visual check** on the dev tree, whose library holds a pack and
  a Default pack: tick three maps and capture the bar. It reads "3 of 67 maps ticked" with Apply pack, Apply
  picture, Reset to default, Select all shown, Clear. Open Apply pack, choose the pack, confirm the dialog that
  names the three maps, and check that the header then reads
  `<pack> applied to 3 maps. Shows when Brawlhalla starts.`, the ticks are cleared, the bar is gone and the three
  cards wear the pack's tag. Tick one map and apply the same pack: **no** confirm appears. Press Undo: the three
  maps go back. Apply picture opens a menu holding only "Add Custom Image..." until part B fills the list, and
  choosing it opens that window with the ticked maps pre-selected.
- [ ] **Step 7:** Commit:

```bash
cd /c/Users/alexa/projects/bhmaps && git add -A && git commit -F - <<'MSG'
feat(app): the selection bar

Spec 3.3: while maps are ticked a bar floats over the grid reading "3 of 67 maps
ticked", with Apply pack, Apply picture, Reset to default, Select all shown and
Clear. All three writes go through RunGameWriteAsync, confirm with the map count
when more than one map is ticked, and clear the ticks on success. The picture menu
holds "Add Custom Image..." until part B fills the library.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
MSG
```

---

## Risks, ordering, and what is not verified here

- **Ticks and container release (A6 step 4).** The one place in part A where user intent can be lost silently: a
  `ListBoxItem` being released can write `IsSelected=false` back through the two-way binding and untick a map.
  `ApplyFilter` restores the set by folder name for exactly that reason. If A8's check (j) or a search in A6
  still drops ticks, the answer is to stop binding the container and drive `ListBox.SelectedItems` from the page
  instead. Do not "fix" it by making the filter non-destructive.
- **Ordering is load-bearing.** A1 first: the settings record is read in three page constructors. A5 before A6:
  the top bar binds `Maps`. A6 before A7, A8 and A10: they all need `MapCardViewModel.IsSelected` and the pages'
  own `SearchText`. A9 before A10. Inside A6 the Platforms deletion and the sidebar removal have to land
  together, or the build is left with a dangling `DataTemplate` or a dangling command binding.
- **Undo is one snapshot.** `UndoStore.Begin` deletes every earlier session, so a multi-map write is one
  snapshot of every path it touches. That is why `ApplyToMapsPaths` exists and why `ResetPathsFor` mirrors
  `MapPanelViewModel.ResetPaths`. A write whose path list is short is a data-loss path: what it overwrites
  outside that list cannot be put back. Review those two lists first in any code review of A9 and A10.
- **`clearTicks` only on success.** A cancelled or failed write keeps the ticks (spec 3.3, S3). The `ok` guard in
  A2 step 2 is the whole of that rule; moving the `ClearSelection` call outside it breaks it silently.
- **Hot path.** `SelectedMaps` allocates a list on every read and every tick raises it twice. With 67 cards that
  is nothing, and it keeps the set impossible to get stale. Do not cache it without a test that ticks a card and
  reads the count.
- **Not verified while writing this plan.** Which of the two suspects causes the `FieldTextBox` offset: A4
  measures rather than assumes, and its step 3 is written to survive both being wrong. Whether `Ctrl+A` reaches
  every card through a non-virtualising `UniformGrid` panel: A8 check (f) proves it. Whether a stock Fluent
  `ContextMenu` is legible on this palette: part B restyles it either way, and A10 only needs it to work.
- **Open question for the owner.** Spec 11 names no wording for the line an Undo leaves. This plan uses
  "Last change undone." (decision A-D7); changing it is one constant in `MainViewModel`.
- **Deviation from the contract, flagged.** `SelectedMaps` reads the Maps page's unfiltered cards rather than its
  visible `Cards` (decision A-D1). The public shape is unchanged, so parts B and C are unaffected, but if the
  owner meant the filtered list the change is one expression.
