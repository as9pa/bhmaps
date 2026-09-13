# BhMaps 2.4 Part B Implementation Plan: right click on pictures and maps

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship spec sections 4.1 to 4.4: a right-click menu on every picture tile in a pack detail grid, a shared chooser window that picks one map or one picture, a right-click menu on every map card with an Apply pack and an Apply picture flyout, and one vocabulary across the three tile menus.

**Architecture:** Nothing new writes to the game. `MapsViewModel` splits its three ticked commands into target-taking bodies (`ApplyPackToAsync`, `ApplyPictureToAsync`, `ResetAsync`) that the bar and the card menu both call, so the bar's behaviour cannot drift from the menu's. `TileMenuCommand` grows from a two-field record into the whole menu vocabulary (header, separator, flyout, disabled with a tooltip) and the existing `TileMenuItem` style grows four setters and two triggers, so one `ItemsSource` of `TileMenuCommand` still drives every menu in the app and no page writes a `MenuItem` by hand. Menus are built when the menu opens, not when the ticks change: a card menu depends on the ticked set and there are up to a few hundred cards, so rebuilding all of them per tick is the one hot path this feature could add and it is avoided by a `ContextMenuOpening` handler. The chooser is one window class with two list sources, which never writes: it returns a choice and the caller applies through the paths that already exist.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3 (tests/BhMaps.Core.Tests, net10.0-windows), `dotnet format BhMaps.slnx`.

**Spec:** `docs/superpowers/specs/2026-09-12-bhmaps-v2-4-design.md` (binding). Sections cited below as "spec 4.1" and so on; rulings as "spec 9.N". Part A (spec 3) is a separate plan and no task here touches the platform editor.

## Global Constraints

Copied from spec section 5, plus the mechanical rules this plan runs under.

- Game writes go only through `MainViewModel.RunGameWriteAsync` and the existing apply paths; no new writer.
- Never run the app or tests against the real game folder, the real library or the real %APPDATA%\BhMaps; dev tree only. Launch only with all three of `--game`, `--library`, `--appdata` under `C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\devtree`, and always with `--quiet`. Never launch, close or kill Brawlhalla, and never touch the owner's own running BhMaps.exe.
- `dotnet format BhMaps.slnx` is the only formatter; csharpier must not be added or run. Builds with 0 warnings in Debug and Release (TreatWarningsAsErrors is on).
- Tests: BhMaps.Core.Tests only (the App has no test project); view models are verified by build and UIA captures on the dev tree.
- Copy: no em-dashes, no emoji. Wording constants are `public const string` on the view model that owns them. New strings in this plan are given verbatim; use them byte for byte.
- `FocusVisualStyle` must be a local attribute; never a bare `x:Static` of a const int into a double property.
- docs/manual.md stays CRLF, UTF-8 without BOM. This plan does not edit the manual; the release task of the Part A plan does.
- Every build and test command passes `--artifacts-path C:\Users\alexa\AppData\Local\Temp\claude\C--Users-alexa-projects-bhmaps\4d9e4a5d-5cec-4956-9fc6-2f4e47200cf2\scratchpad\art` (written `<ART>` below) so bin and obj under src are never touched while the owner's exe is running.
- One commit per task, imperative mood, message written to a file and committed with `git commit -F`, ending with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg`. Git identity must be as9pa; check `git config user.email` before the first commit.
- Implementers never dispatch subagents. No whole-file Reads over about 400 lines: use offset/limit or grep first.
- Branch is `feature/bhmaps-v2.4`. No task here changes the version number.

## Decisions this plan makes, so no task re-decides them

| Mark | Decision | Where |
|---|---|---|
| D1 | A flyout is a `TileMenuCommand` with a `Children` list, bound by two new setters on the existing `TileMenuItem` style: `ItemsSource="{Binding Children}"` and `ItemContainerStyle="{DynamicResource TileMenuItem}"`. DynamicResource, because a style cannot StaticResource itself. No HierarchicalDataTemplate: the style already sets `Header`, and an `ItemTemplate` would set `Header` as a local value and fight it. | Task 3 Step 1 |
| D2 | A header line and a separator line are the same `TileMenuCommand` type with a `Kind`, drawn by two `Style.Triggers` on `TileMenuItem`. No new style key, which is what spec 4.1 asks for. Both set `Focusable="False"` so arrow navigation skips them. | Task 3 Step 1 |
| D3 | A disabled line is `IsEnabled=false` on the record with a `ToolTip` string; the style binds both. WPF shows a tooltip on a disabled MenuItem only with `ToolTipService.ShowOnDisabled="True"`, so the style sets that too. | Task 3 Step 1 |
| D4 | Menus are built on `ContextMenuOpening`, not on every tick change. `MapsViewModel.BuildCardMenu(card)` and `PackDetailViewModel.BuildTileMenu(tile)` own the building, because the lines need the snapshot, the packs and the ticked set, which the tile view models do not hold. The tile view models hold only `MenuItems`. | Tasks 3 and 4 |
| D5 | "A pack holds nothing for this map" is `PackApplier.ApplyToMapsPaths(pack, maps).Count == 0`, which is exactly the set of files that apply would write. | Task 4 Step 3 |
| D6 | The Maps page search rule moves into Core as `NameFilter.Matches`, so the chooser and the Maps page cannot filter differently and the rule gets the one unit test Part B can have. | Task 2 Step 1 |

---

### Task 1: MapsViewModel command split

Spec 4.3, last paragraph. Pure refactor: the bar must behave exactly as it does today after this task.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`ApplyPackToTickedAsync` at about line 244, `ApplyPictureToTickedAsync` at about 276, `ResetTickedAsync` at about 305, `CanResetTicked` and `Confirm` just after)

**Interfaces:**
- Consumes: `MainViewModel.RunGameWriteAsync(string label, IReadOnlyList<string> undoPaths, Func<IProgress<string>, CancellationToken, Task> work, string doneText, bool clearTicks = false, string? packName = null)`; `MainViewModel.ApplyPictureAsync(string sourcePath, IReadOnlyList<MapEntry> maps, bool clearTicks, string? displayName = null, string? packName = null)`; `MainViewModel.SelectedMaps`; `PackApplier.ApplyToMapsPaths(Pack, IReadOnlyList<MapEntry>)`; `PackApplier.ResetMapPaths(FolderTree, MapEntry, Pack)`; `MapReset.ResetMap(string, string, IReadOnlyList<string>, Pack)`.
- Produces, all public on `MapsViewModel`, all used by Task 4:
  - `public async Task ApplyPackToAsync(IReadOnlyList<MapEntry> maps, Pack pack, bool clearTicks)`
  - `public async Task ApplyPictureToAsync(IReadOnlyList<MapEntry> maps, PictureMenuItem item, bool clearTicks)`
  - `public async Task ResetAsync(IReadOnlyList<MapEntry> maps, bool clearTicks)`
  - `public bool CanReset => _snapshot?.DefaultPack is not null;`

clearTicks is a parameter rather than a constant because the bar clears the ticks after a write it aimed at them and the card menu on an unticked card must not (spec 4.3: right click does not change ticks).

- [ ] **Step 1: Add the pack body.** Move the body of `ApplyPackToTickedAsync` into the new method; the command becomes a one-line caller.

```csharp
/// <summary>Spec 4.3: one pack onto the maps given, whoever gave them. The bar hands the ticked set and clears
/// the ticks; a card menu hands its own target and leaves the ticks alone.</summary>
public async Task ApplyPackToAsync(IReadOnlyList<MapEntry> maps, Pack pack, bool clearTicks)
{
    if (maps.Count == 0 || !Confirm($"Apply {pack.Name}", $"Apply {pack.Name}", maps))
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
        clearTicks,
        pack.Name);

    if (result is not null)
    {
        Shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
    }
}

/// <summary>Spec 3.3: the pack, on the ticked maps only.</summary>
[RelayCommand]
private Task ApplyPackToTickedAsync(Pack? pack) =>
    pack is null ? Task.CompletedTask : ApplyPackToAsync(Shell.SelectedMaps, pack, clearTicks: true);
```

- [ ] **Step 2: The picture body.** The Add Image row (a `PictureMenuItem` whose Picture is null) keeps opening the Add window, on the target rather than always on the ticked set.

```csharp
/// <summary>Spec 4.3: one picture onto the maps given. The last menu row carries no picture: it opens Add Image
/// on the same target (spec 7.1).</summary>
public async Task ApplyPictureToAsync(IReadOnlyList<MapEntry> maps, PictureMenuItem item, bool clearTicks)
{
    if (item.Picture is not { } picture)
    {
        await Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.Ticked, null, maps));
        return;
    }

    // A custom picture need not be in a pack: one the user put in the game folder by hand has no library copy
    // at all, so the file is resolved by the rule a tile's own menu uses rather than by indexing the packs.
    if (CustomPictureTileViewModel.SourcePath(picture, Shell.Services.GamePath) is not { } source)
    {
        Shell.Dialogs.Info(
            "Nothing to apply",
            $"{picture.DisplayName} has no file in the library or in the game folder.");
        return;
    }

    // The name the menu row was labelled with, so the done line reports the row that was clicked (spec 2.2).
    await Shell.ApplyPictureAsync(source, maps, clearTicks, picture.DisplayName, picture.PackName);
}

/// <summary>Spec 3.3: one picture into every ticked map's own slots.</summary>
[RelayCommand]
private Task ApplyPictureToTickedAsync(PictureMenuItem? item) =>
    item is null ? Task.CompletedTask : ApplyPictureToAsync(Shell.SelectedMaps, item, clearTicks: true);
```

- [ ] **Step 3: The reset body.** The confirm stays as it is: spec 4.3 line 7 wants one map to reset without asking, and `Confirm` already returns true for `maps.Count <= 1`.

```csharp
/// <summary>Spec 4.3: the maps given, back to the Default pack. One map resets without asking; more than one
/// names the count and the maps first.</summary>
public async Task ResetAsync(IReadOnlyList<MapEntry> maps, bool clearTicks)
{
    if (_snapshot is not { } snapshot || snapshot.DefaultPack is not { } defaultPack || maps.Count == 0
        || !Confirm("Reset to default", "Reset", maps))
    {
        return;
    }

    var gamePath = Shell.Services.GamePath;
    var failures = new List<FileFailure>();
    await Shell.RunGameWriteAsync(
        "Resetting",
        maps.SelectMany(m => PackApplier.ResetMapPaths(snapshot.Tree, m, defaultPack))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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
        clearTicks);

    Shell.Dialogs.ShowFailures("Some files could not be reset", failures);
}

/// <summary>Spec 3.3: the ticked maps back to the Default pack.</summary>
[RelayCommand(CanExecute = nameof(CanResetTicked))]
private Task ResetTickedAsync() => ResetAsync(Shell.SelectedMaps, clearTicks: true);

/// <summary>True once a scan has found a Default pack, which is the only thing a reset needs.</summary>
public bool CanReset => _snapshot?.DefaultPack is not null;

private bool CanResetTicked() => CanReset;
```

- [ ] **Step 4: Build, test, format.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: build succeeds with 0 warnings; 370 tests pass; format reports no changes. Nothing in the test project references MapsViewModel, so the test count does not change.

- [ ] **Step 5: Commit.** Write the message to a file, then `git commit -F`.

```
Split the Maps page ticked commands into target-taking bodies

Spec 4.3: ApplyPackToAsync, ApplyPictureToAsync and ResetAsync take the maps
they act on, and the three ticked commands become one-line callers that pass
the ticked set. clearTicks is a parameter because the card menu of task 4 must
not change the ticks. The bar's behaviour is unchanged.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

**Acceptance:** the three public methods exist with the signatures above; `ApplyPackToTickedCommand`, `ApplyPictureToTickedCommand` and `ResetTickedCommand` still exist with the same names and parameter types, so `MapsView.xaml` needs no edit; build, tests and format are clean.

---

### Task 2: The chooser window

Spec 4.2.

**Files:**
- Create: `src\BhMaps.Core\Maps\NameFilter.cs`
- Create: `tests\BhMaps.Core.Tests\NameFilterTests.cs`
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`Matches`, at about line 575 after Task 1)
- Create: `src\BhMaps.App\ViewModels\ChooserViewModel.cs`
- Create: `src\BhMaps.App\Views\ChooserWindow.xaml` and `src\BhMaps.App\Views\ChooserWindow.xaml.cs`
- Modify: `src\BhMaps.App\ViewModels\MainViewModel.cs` (the two entry points, next to `OpenBackgroundEditorAsync` at about line 494)

There is no unit test for `ChooserViewModel` itself: the test project is `BhMaps.Core.Tests` and the App has no test project (spec 5), so view models are verified by build and UIA capture. The one testable rule is the filter, and Step 1 moves it into Core so it can carry a test.

**Interfaces:**
- Consumes: `ScanSnapshot` (`Catalog.Maps`, `Packs`, the status accessor the Maps page uses), `MapEntry.DisplayName` and `BackgroundSlots`, `MapStatus.Text`, `Pack.FindFolder(string)`, `AppServices.Thumbnails.GetAsync(string path, long mtime, CancellationToken)`, `IDialogs.Owner`.
- Produces:
  - `public static bool NameFilter.Matches(string name, string search)` in `BhMaps.Core.Maps`.
  - `public sealed partial class ChooserRow` with `Name`, `Detail`, `Path`, `Map`, `Thumbnail`.
  - `public sealed partial class ChooserViewModel : ObservableObject` with `Title`, `Subtitle`, `SearchText`, `Rows`, `Selected`, `EmptyText`, `ShowEmpty`, `PrimaryPrefix`, `PrimaryText`, `CanApply`.
  - `public Task<MapEntry?> MainViewModel.ChooseMapAsync(string picturePath, string caption)`
  - `public Task<string?> MainViewModel.ChoosePictureAsync(MapEntry map)`

Tasks 3 and 4 call only the two `MainViewModel` methods.

- [ ] **Step 1: The filter in Core, test first.** Create `tests\BhMaps.Core.Tests\NameFilterTests.cs`:

```csharp
using BhMaps.Core.Maps;

namespace BhMaps.Core.Tests;

public sealed class NameFilterTests
{
    [Theory]
    [InlineData("Brawlhaven", "", true)]
    [InlineData("Brawlhaven", "haven", true)]
    [InlineData("Brawlhaven", "HAVEN", true)]
    [InlineData("Brawlhaven", "  ", true)]
    [InlineData("Brawlhaven", "dojo", false)]
    [InlineData("", "a", false)]
    public void Matches_IsCaseInsensitiveContains(string name, string search, bool expected) =>
        Assert.Equal(expected, NameFilter.Matches(name, search));
}
```

Run it and watch it fail to compile, then create `src\BhMaps.Core\Maps\NameFilter.cs`:

```csharp
namespace BhMaps.Core.Maps;

/// <summary>The one rule a name-typed search uses, so the Maps page and the chooser (spec 4.2) cannot filter the
/// same list differently. An empty or blank search matches everything, which is what an empty box means.</summary>
public static class NameFilter
{
    public static bool Matches(string name, string search) =>
        string.IsNullOrWhiteSpace(search)
        || name.Contains(search, StringComparison.OrdinalIgnoreCase);
}
```

The blank-search case widens today's Maps page rule, which tests `search.Length > 0`. A box holding only spaces matching everything is the intended reading; nothing else changes.

- [ ] **Step 2: The Maps page uses it.** In `MapsViewModel.Matches`, replace the first clause and leave the chip switch alone:

```csharp
private bool Matches(MapCardViewModel card)
{
    if (!NameFilter.Matches(card.DisplayName, SearchText))
    {
        return false;
    }

    return SelectedChip switch
    {
        AllChip => true,
        TickedChip => card.IsSelected,
        _ => _uiSets.FirstOrDefault(s => s.Label == SelectedChip) is { } set
            && card.Map.Sets.Contains(set.Name, StringComparer.OrdinalIgnoreCase),
    };
}
```

- [ ] **Step 3: ChooserViewModel.** Create `src\BhMaps.App\ViewModels\ChooserViewModel.cs`. It never writes, so it takes no write path and no `MainViewModel`.

```csharp
using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the chooser: what it is called, the muted right-hand word, the file behind it and, in map
/// mode, the map it stands for. Picture mode leaves Map null and the caller reads Path.</summary>
public sealed partial class ChooserRow : ObservableObject
{
    public ChooserRow(string name, string detail, string path, MapEntry? map)
    {
        Name = name;
        Detail = detail;
        Path = path;
        Map = map;
    }

    public string Name { get; }

    /// <summary>Map mode: the applied pack name or "Default" (spec 9.2, owner answer q4). Picture mode: the pack.</summary>
    public string Detail { get; }

    /// <summary>The picture the thumbnail is decoded from, and what picture mode returns.</summary>
    public string Path { get; }

    public MapEntry? Map { get; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }
}

/// <summary>Spec 4.2: one small window that picks one map or one picture. It never writes anything: it returns a
/// choice and the menu that opened it applies through the path it already used.</summary>
public sealed partial class ChooserViewModel : ObservableObject
{
    private readonly IReadOnlyList<ChooserRow> _all;
    private readonly string _noun;

    public ChooserViewModel(string title, string subtitle, string noun, IReadOnlyList<ChooserRow> rows)
    {
        Title = title;
        Subtitle = subtitle;
        _noun = noun;
        _all = rows;
        SearchText = "";
        Rows = [.. rows];
    }

    /// <summary>"Apply BG_Dojo.jpg to a map" or "Apply a picture to Brawlhaven". The window's UIA name too.</summary>
    public string Title { get; }

    public string Subtitle { get; }

    public ObservableCollection<ChooserRow> Rows { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial ChooserRow? Selected { get; set; }

    /// <summary>Spec 4.2: the Maps page's own empty wording, with the noun swapped.</summary>
    public string EmptyText => $"No {_noun} matches '{SearchText}'.";

    public bool ShowEmpty => Rows.Count == 0;

    /// <summary>"Apply to" in map mode, "Apply" in picture mode, so this class holds no mode flag.</summary>
    public string PrimaryPrefix { get; init; } = "Apply";

    public string PrimaryText => Selected is null ? PrimaryPrefix : $"{PrimaryPrefix} {Selected.Name}";

    public bool CanApply => Selected is not null;

    partial void OnSearchTextChanged(string value)
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => NameFilter.Matches(r.Name, value)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowEmpty));
        if (Selected is { } chosen && !Rows.Contains(chosen))
        {
            Selected = null;
        }
    }

    /// <summary>Every decode is off the UI thread and through the shared cache, as a tile's is.</summary>
    public async Task LoadThumbnailsAsync(AppServices services, CancellationToken ct)
    {
        foreach (var row in _all)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(row.Path).Ticks, ct);
                if (await services.Thumbnails.GetAsync(row.Path, mtime, ct) is { } image)
                {
                    row.Thumbnail = image;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A row whose file vanished still picks; a blank thumbnail is not worth a dialog.
            }
        }
    }
}
```

- [ ] **Step 4: ChooserWindow.** Create `src\BhMaps.App\Views\ChooserWindow.xaml` with the DialogWindow chrome: WindowStyle None, CenterOwner, no taskbar entry, the BgBrush fill, and one Border with the 1 px LineBrush edge and the card radius as the window's own edge.

```xml
<Window x:Class="BhMaps.App.Views.ChooserWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="420" Height="460" ResizeMode="NoResize"
        WindowStyle="None"
        WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="{StaticResource BgBrush}"
        AutomationProperties.Name="{Binding Title}"
        Title="{Binding Title}"
        KeyDown="OnKeyDown">
  <Window.Resources>
    <BooleanToVisibilityConverter x:Key="BoolToVis" />
  </Window.Resources>
  <Border BorderBrush="{StaticResource LineBrush}"
          BorderThickness="1"
          CornerRadius="{StaticResource Radius}">
    <Grid Margin="20">
      <Grid.RowDefinitions>
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="Auto" />
        <RowDefinition Height="*" />
        <RowDefinition Height="Auto" />
      </Grid.RowDefinitions>

      <TextBlock FontSize="17" FontWeight="SemiBold" Text="{Binding Title}" TextWrapping="Wrap" />
      <TextBlock Grid.Row="1" Margin="0,6,0,0" FontSize="12"
                 Foreground="{StaticResource Text3Brush}" Text="{Binding Subtitle}" TextWrapping="Wrap" />

      <TextBox x:Name="Search" Grid.Row="2" Margin="0,14,0,0"
               AutomationProperties.Name="Search"
               Style="{StaticResource FieldTextBox}"
               Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />

      <Grid Grid.Row="3" Margin="0,12,0,0">
        <ListBox x:Name="Rows"
                 FocusVisualStyle="{x:Null}"
                 ItemsSource="{Binding Rows}"
                 SelectedItem="{Binding Selected, Mode=TwoWay}"
                 SelectionMode="Single"
                 ScrollViewer.HorizontalScrollBarVisibility="Disabled">
          <ListBox.ItemContainerStyle>
            <Style TargetType="ListBoxItem" BasedOn="{StaticResource TileListBoxItem}">
              <Setter Property="AutomationProperties.Name" Value="{Binding Name}" />
              <Setter Property="Padding" Value="6,5" />
              <EventSetter Event="MouseDoubleClick" Handler="OnRowDoubleClick" />
            </Style>
          </ListBox.ItemContainerStyle>
          <ListBox.ItemTemplate>
            <DataTemplate>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto" />
                  <ColumnDefinition Width="*" />
                  <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <Border Width="40" Height="22" CornerRadius="3"
                        RenderOptions.BitmapScalingMode="HighQuality">
                  <Border.Background>
                    <ImageBrush ImageSource="{Binding Thumbnail}" Stretch="UniformToFill" />
                  </Border.Background>
                </Border>
                <TextBlock Grid.Column="1" Margin="10,0,10,0" VerticalAlignment="Center"
                           Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                <TextBlock Grid.Column="2" VerticalAlignment="Center" FontSize="11"
                           Foreground="{StaticResource Text3Brush}" Text="{Binding Detail}" />
              </Grid>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>

        <TextBlock HorizontalAlignment="Center" VerticalAlignment="Center"
                   Foreground="{StaticResource Text2Brush}" Text="{Binding EmptyText}"
                   Visibility="{Binding ShowEmpty, Converter={StaticResource BoolToVis}}" />
      </Grid>

      <StackPanel Grid.Row="4" Margin="0,16,0,0" HorizontalAlignment="Right" Orientation="Horizontal">
        <Button Margin="0,0,8,0" AutomationProperties.Name="Cancel" Content="Cancel"
                IsCancel="True" Style="{StaticResource OutlineButton}" />
        <Button AutomationProperties.Name="{Binding PrimaryText}"
                Click="OnApply"
                Content="{Binding PrimaryText}"
                IsDefault="True"
                IsEnabled="{Binding CanApply}"
                Style="{StaticResource PrimaryButton}" />
      </StackPanel>
    </Grid>
  </Border>
</Window>
```

If `PrimaryButton` is not the key the theme uses for a filled button, grep `src\BhMaps.App\Theme\Controls.xaml` for the key the Add Pictures window's primary button uses and use that one; change nothing else.

Code-behind `src\BhMaps.App\Views\ChooserWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Input;

namespace BhMaps.App.Views;

/// <summary>Spec 4.2: the chooser's chrome. The window closes with true when a row is picked and with false or
/// nothing otherwise; the caller reads the view model's Selected. Nothing here writes to the game.</summary>
public partial class ChooserWindow : Window
{
    public ChooserWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Search.Focus();
    }

    private void OnApply(object sender, RoutedEventArgs e) => Accept();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    /// <summary>Enter in the search box would otherwise fire IsDefault with nothing picked, so the guard is here
    /// as well as on the button's IsEnabled.</summary>
    private void Accept()
    {
        if (DataContext is ViewModels.ChooserViewModel { CanApply: true })
        {
            DialogResult = true;
        }
    }
}
```

- [ ] **Step 5: The two shell entry points.** In `src\BhMaps.App\ViewModels\MainViewModel.cs`, next to `OpenBackgroundEditorAsync`, with the Owner and ShowActivated pattern the other windows use:

```csharp
/// <summary>Spec 4.2: pick one map for a picture. Null when the window was cancelled. Applying is the caller's,
/// through the path it already uses, so the chooser itself never writes.</summary>
public Task<MapEntry?> ChooseMapAsync(string picturePath, string caption)
{
    if (Snapshot is not { } snapshot)
    {
        return Task.FromResult<MapEntry?>(null);
    }

    var rows = new List<ChooserRow>();
    foreach (var map in snapshot.Catalog.Maps.Where(m => m.BackgroundSlots.Count > 0))
    {
        var status = StatusFor(snapshot, map.FolderName);
        rows.Add(new ChooserRow(map.DisplayName, status?.Text ?? "Default", picturePath, map));
    }

    var vm = new ChooserViewModel(
        $"Apply {caption} to a map",
        "One map. Pick it and the picture is written to the game.",
        "map",
        rows)
    { PrimaryPrefix = "Apply to" };
    return Task.FromResult(ShowChooser(vm) ? vm.Selected?.Map : null);
}

/// <summary>Spec 4.2: pick one picture for a map. The file, or null when cancelled.</summary>
public Task<string?> ChoosePictureAsync(MapEntry map)
{
    if (Snapshot is not { } snapshot)
    {
        return Task.FromResult<string?>(null);
    }

    var rows = new List<ChooserRow>();
    foreach (var pack in snapshot.Packs.OrderByDescending(
                 p => p.Name.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase)))
    {
        foreach (var file in pack.FindFolder("Backgrounds")?.Files ?? Array.Empty<GameFile>())
        {
            rows.Add(new ChooserRow(
                Path.GetFileNameWithoutExtension(file.Name), pack.Name, file.FullPath, null));
        }
    }

    var vm = new ChooserViewModel(
        $"Apply a picture to {map.DisplayName}",
        "One picture. Pick it and it is written to the game.",
        "picture",
        rows);
    return Task.FromResult(ShowChooser(vm) ? vm.Selected?.Path : null);
}

/// <summary>The chooser's window, opened the way every other owned window here is. The thumbnails load while it
/// is up and are cancelled when it closes.</summary>
private bool ShowChooser(ChooserViewModel vm)
{
    var window = new ChooserWindow
    {
        DataContext = vm,
        Owner = Application.Current.MainWindow,
        ShowActivated = !App.Quiet,
    };
    using var cts = new CancellationTokenSource();
    _ = vm.LoadThumbnailsAsync(Services, cts.Token);
    var ok = window.ShowDialog() == true;
    cts.Cancel();
    return ok;
}
```

`OrderByDescending` on the My Backgrounds test is what puts My Backgrounds first and leaves the other packs in the Packs page order, because LINQ to Objects ordering is stable. `BackgroundEditorViewModel.DefaultPackName` is the existing constant for the My Backgrounds pack. `StatusFor(snapshot, folderName)` stands for whatever accessor the Maps page already uses to find a map's `MapStatus` in the snapshot: grep `MapStatus` in `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` and use that expression rather than writing a second lookup.

- [ ] **Step 6: Build, test, format.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings; 376 tests pass (370 plus the six NameFilterTests cases); format clean.

- [ ] **Step 7: Commit.**

```
Add the chooser window and the two shell entry points

Spec 4.2: ChooserWindow and ChooserViewModel pick one map or one picture in a
small owned window with the DialogWindow chrome, and never write anything.
MainViewModel.ChooseMapAsync and ChoosePictureAsync return the choice for the
caller to apply through the path it already uses. The Maps page search rule
moves into Core as NameFilter so the chooser cannot filter differently, and
carries the one unit test Part B can have.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

**Acceptance:** the two entry points compile and are callable from a view model; NameFilterTests passes; no menu opens the chooser yet, which is Tasks 3 and 4.

---

### Task 3: The pack picture tile menu

Spec 4.1, plus the `TileMenuCommand` and `TileMenuItem` changes every menu after this uses, plus the spec 4.4 line on the Backgrounds tile.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\TileMenuCommand.cs` (the whole file, 7 lines today)
- Modify: `src\BhMaps.App\Theme\Controls.xaml` (`TileMenuItem` at 598 to 617)
- Modify: `src\BhMaps.App\ViewModels\Pages\PackDetailViewModel.cs` (`PackTileViewModel` at about 449; the tile build at about 275 to 302; `BuildTileMenu` added next to `Open` at about 130)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml` (the tile `DataTemplate` at 89 to 114, and the `ListBoxItem` style at 77 to 83)
- Modify: `src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs` (two handlers)
- Modify: `src\BhMaps.App\ViewModels\PictureTileViewModel.cs` (`MapPictureTileViewModel.RebuildMenu` at about 128) and `src\BhMaps.App\ViewModels\CustomPictureTileViewModel.cs` (`RebuildMenu` at about 76) for the spec 4.4 line

**Interfaces:**
- Consumes: `MainViewModel.ChooseMapAsync`, `ApplyPictureAsync`, `RunBusyAsync`, `SetLibraryDone`, `RescanAsync`, `OpenBackgroundEditorAsync(BackgroundEditorRequest)`; `ExplorerLauncher.Reveal(string)`; `PackScanner.PacksRoot(string)`.
- Produces: the widened `TileMenuCommand` record and its three factories, used by Task 4; `PackTileViewModel.MenuItems` and `PackTileViewModel.PicturePath`; `PackDetailViewModel.BuildTileMenu(PackTileViewModel)`.

- [ ] **Step 1: Widen TileMenuCommand (D1, D2, D3).** Replace `src\BhMaps.App\ViewModels\TileMenuCommand.cs` whole:

```csharp
using System.Windows.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What a line of a tile menu is. An ordinary line runs a command; a Header is the muted first line that
/// names what the menu is about (spec 4.1); a Separator is a rule. The style in Controls.xaml draws all three.</summary>
public enum TileMenuKind
{
    Item,
    Header,
    Separator,
}

/// <summary>One line of a tile's menu: the words spec section 11 and spec 4.1 give it, the command it runs, and,
/// for a flyout, the lines behind it. A record, so a tile rebuilding its menu costs allocations and no
/// bookkeeping. Children null rather than empty on an ordinary line, because an empty ItemsSource still draws a
/// submenu arrow.</summary>
public sealed record TileMenuCommand(
    string Text,
    ICommand? Command,
    IReadOnlyList<TileMenuCommand>? Children = null,
    bool IsEnabled = true,
    string? ToolTip = null,
    TileMenuKind Kind = TileMenuKind.Item)
{
    /// <summary>The muted first line naming the file, the map or the count (spec 4.1, spec 4.3).</summary>
    public static TileMenuCommand Header(string text) =>
        new(text, null, IsEnabled: false, Kind: TileMenuKind.Header);

    public static TileMenuCommand Separator() =>
        new("", null, IsEnabled: false, Kind: TileMenuKind.Separator);

    /// <summary>A line that opens a submenu rather than running. Disabled when it has no lines, because a flyout
    /// that opens on nothing reads as the menu being broken.</summary>
    public static TileMenuCommand Flyout(string text, IReadOnlyList<TileMenuCommand> children) =>
        new(text, null, children.Count > 0 ? children : null, children.Count > 0);
}
```

Every existing call site builds `new TileMenuCommand(text, command)` positionally and keeps compiling.

- [ ] **Step 2: The style grows five setters and two triggers.** In `src\BhMaps.App\Theme\Controls.xaml`, inside the existing `TileMenuItem` style, after the `AutomationProperties.Name` setter. A `Style.Triggers` block must come last in a `Style`, so the setters go above it.

```xml
    <!-- Spec 4.3's flyouts: an item with Children is a submenu, and its own rows are containered by this same
         style. DynamicResource because a style cannot StaticResource itself. -->
    <Setter Property="ItemsSource" Value="{Binding Children}" />
    <Setter Property="ItemContainerStyle" Value="{DynamicResource TileMenuItem}" />

    <!-- Spec 4.3: a pack that holds nothing for the target is offered and refused rather than hidden, so the
         menu's shape does not change with the target. WPF hides a tooltip on a disabled item without the third. -->
    <Setter Property="IsEnabled" Value="{Binding IsEnabled}" />
    <Setter Property="ToolTip" Value="{Binding ToolTip}" />
    <Setter Property="ToolTipService.ShowOnDisabled" Value="True" />

    <Style.Triggers>
      <!-- The header line (spec 4.1): the menu's own muted style, not a new style key. Not focusable, so the
           arrow keys land on the first line that does something. -->
      <DataTrigger Binding="{Binding Kind}" Value="Header">
        <Setter Property="FontSize" Value="11.5" />
        <Setter Property="Foreground" Value="{StaticResource Text3Brush}" />
        <Setter Property="Focusable" Value="False" />
        <Setter Property="Padding" Value="10,4" />
      </DataTrigger>

      <DataTrigger Binding="{Binding Kind}" Value="Separator">
        <Setter Property="Focusable" Value="False" />
        <Setter Property="Padding" Value="0" />
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="MenuItem">
              <Border Height="1" Margin="6,4" Background="{StaticResource LineBrush}" />
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </DataTrigger>
    </Style.Triggers>
```

- [ ] **Step 3: PackTileViewModel holds a menu and names its picture.** Add to `PackTileViewModel`:

```csharp
    /// <summary>The lines of this tile's menu (spec 4.1), filled by PackDetailViewModel.BuildTileMenu when the
    /// menu opens. Built then and not before, because the ticked line names a count that changes on another page
    /// and there is one of these per tile.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; } = [];

    /// <summary>The picture the tile is about: the pack's own file for an orphan, or the pack's copy of the map's
    /// background slot. Null on a tile whose map the pack has no background for, and then the tile has no menu.</summary>
    public string? PicturePath { get; set; }
```

In the tile build at about line 275 to 302, set it: the orphan tile gets `file.FullPath` at construction; the map tile gets `file.FullPath` inside the backgrounds loop, both when the loop creates the tile for `owner` and when it finds a tile `MapsIn` already added, so a map tile the pack has no background for keeps null:

```csharp
            var owner = MapForSlot(snapshot.Catalog, file.Name);
            if (owner is null)
            {
                Items.Add(new PackTileViewModel(
                    file.Name, file.Name, null, file, MapCompositor.CardWidth, MapCompositor.CardHeight)
                { PicturePath = file.FullPath });
            }
            else
            {
                var tile = Items.FirstOrDefault(
                    t => t.Key.Equals(owner.FolderName, StringComparison.OrdinalIgnoreCase));
                if (tile is null)
                {
                    tile = new PackTileViewModel(
                        owner.FolderName, owner.DisplayName, owner, null,
                        MapCompositor.CardWidth, MapCompositor.CardHeight);
                    Items.Add(tile);
                }

                tile.PicturePath = file.FullPath;
            }
```

- [ ] **Step 4: BuildTileMenu, the eight lines of spec 4.1.** Add to `PackDetailViewModel`, next to `Open`:

```csharp
    /// <summary>Spec 4.1: the lines of one picture tile's menu, in the spec's order, each present only when its
    /// condition holds. Built when the menu opens, so the ticked count is the one the user can see.</summary>
    public void BuildTileMenu(PackTileViewModel tile)
    {
        if (tile.PicturePath is not { } path || Pack is not { } pack)
        {
            tile.MenuItems = [];
            return;
        }

        var name = Path.GetFileName(path);
        var ticked = Shell.SelectedMapCount;
        var slot = tile.Map?.BackgroundSlots.FirstOrDefault();
        var items = new List<TileMenuCommand> { TileMenuCommand.Header(name) };

        if (tile.Map is { } owner)
        {
            items.Add(new TileMenuCommand(
                $"Apply to {owner.DisplayName}",
                new AsyncRelayCommand(() => Shell.ApplyPictureAsync(path, [owner], false, name, pack.Name))));
        }

        if (ticked > 0)
        {
            var text = ticked == 1 ? "Apply to the 1 selected map" : $"Apply to the {ticked} selected maps";
            items.Add(new TileMenuCommand(
                text,
                new AsyncRelayCommand(
                    () => Shell.ApplyPictureAsync(path, Shell.SelectedMaps, true, name, pack.Name))));
        }

        items.Add(new TileMenuCommand(
            "Apply to a map...", new AsyncRelayCommand(() => ApplyToChosenMapAsync(path, name, pack.Name))));
        items.Add(new TileMenuCommand(
            "Apply to all maps", new AsyncRelayCommand(() => ApplyToAllMapsAsync(path, name, pack.Name))));
        items.Add(TileMenuCommand.Separator());
        items.Add(new TileMenuCommand(
            "Edit",
            new AsyncRelayCommand(
                () => Shell.OpenBackgroundEditorAsync(new BackgroundEditorRequest(path, pack.Name, slot)))));
        items.Add(new TileMenuCommand("Show in folder", new RelayCommand(() => ShowInFolder(path))));
        items.Add(new TileMenuCommand(
            $"Remove from {pack.Name}", new AsyncRelayCommand(() => RemoveFromPackAsync(path, name))));
        tile.MenuItems = items;
    }

    /// <summary>Spec 4.1 line 3: the chooser, then the shell's apply on the one map it returned. No confirm,
    /// because one map is one click (spec 4.2).</summary>
    private async Task ApplyToChosenMapAsync(string path, string name, string packName)
    {
        if (await Shell.ChooseMapAsync(path, name) is { } map)
        {
            await Shell.ApplyPictureAsync(path, [map], clearTicks: false, name, packName);
        }
    }

    /// <summary>Spec 4.1 line 4: the shell's own apply over every map, which brings the
    /// "Apply {name} to these {N} maps?" confirm, the undo snapshot and the done line with it.</summary>
    private Task ApplyToAllMapsAsync(string path, string name, string packName) =>
        Shell.Snapshot is { } snapshot
            ? Shell.ApplyPictureAsync(path, snapshot.Catalog.Maps, clearTicks: false, name, packName)
            : Task.CompletedTask;

    private void ShowInFolder(string path)
    {
        if (ExplorerLauncher.Reveal(path) is { } error)
        {
            Shell.Dialogs.Error("Could not show the file", error);
        }
    }

    /// <summary>Spec 4.1 line 8: the file leaves the pack the way Remove from library takes it out of My
    /// Backgrounds. A library write, so RunBusyAsync and SetLibraryDone, never RunGameWriteAsync, and the
    /// packs-root guard is why this is not one File.Delete.</summary>
    private async Task RemoveFromPackAsync(string path, string name)
    {
        if (Pack is not { } pack
            || !Shell.Dialogs.Confirm(
                $"Remove from {pack.Name}?",
                $"{name} is removed from {pack.Name}. The game keeps whatever is applied until you apply something else."))
        {
            return;
        }

        var packsRoot = PackScanner.PacksRoot(Shell.Services.LibraryPath);
        var failures = new List<FileFailure>();
        var ok = await Shell.RunBusyAsync(
            $"Removing {name}",
            (_, ct) => Task.Run(
                () =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Path.GetFullPath(path).StartsWith(
                            Path.GetFullPath(packsRoot) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(new FileFailure(path, "Not a file in the library."));
                        return;
                    }

                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        failures.Add(new FileFailure(path, ex.Message));
                    }
                },
                ct));

        Shell.Dialogs.ShowFailures("Some files could not be removed", failures);
        if (ok && failures.Count == 0)
        {
            Shell.SetLibraryDone($"Removed {name} from {pack.Name}");
        }

        await Shell.RescanAsync();
    }
```

Read `CustomPictureTileViewModel.RemoveFromLibraryAsync` before writing this and keep the two identical in shape: it is the same operation on one file instead of a list, and the two must not drift.

- [ ] **Step 5: The tile carries the menu and the hover button.** In `src\BhMaps.App\Views\Pages\PackDetailView.xaml`, wrap the tile template's StackPanel in a Border that carries the menu and put the dots button over the picture, as the Backgrounds tiles have it. Keep the Viewbox, the Grid, the Image and the caption TextBlock exactly as they are.

```xml
                <DataTemplate>
                  <Border x:Name="Tile"
                          Background="Transparent"
                          ContextMenu="{StaticResource TileMenu}"
                          ContextMenuOpening="OnTileMenuOpening"
                          Focusable="True"
                          FocusVisualStyle="{x:Null}">
                    <StackPanel>
                      <Grid>
                        <Viewbox Stretch="Uniform">
                          <!-- unchanged -->
                        </Viewbox>
                        <Button x:Name="MenuButton"
                                Margin="0,6,6,0"
                                HorizontalAlignment="Right"
                                VerticalAlignment="Top"
                                AutomationProperties.Name="More actions"
                                Click="OnTileMenuButton"
                                Opacity="0"
                                Style="{StaticResource TileAction}">
                          <controls:Icon Geometry="{StaticResource Icon.Dots}" Size="14" />
                        </Button>
                      </Grid>
                      <TextBlock Margin="2,8,2,0" Text="{Binding Caption}" TextTrimming="CharacterEllipsis">
                        <!-- unchanged -->
                      </TextBlock>
                    </StackPanel>
                  </Border>
                  <DataTemplate.Triggers>
                    <Trigger SourceName="Tile" Property="IsMouseOver" Value="True">
                      <Setter TargetName="MenuButton" Property="Opacity" Value="1" />
                    </Trigger>
                    <Trigger SourceName="Tile" Property="IsKeyboardFocusWithin" Value="True">
                      <Setter TargetName="MenuButton" Property="Opacity" Value="1" />
                    </Trigger>
                  </DataTemplate.Triggers>
                </DataTemplate>
```

Add `xmlns:controls="clr-namespace:BhMaps.App.Views.Controls"` to the UserControl if it is not already declared. `Background="Transparent"` on the Border is what makes a right click on the gap between the picture and the caption reach the menu. Keyboard: the page already handles `Tiles_PreviewKeyDown` on the list; add `TileMenus.OnPreviewKeyDown(sender, e);` as its first line so the Menu key and Shift+F10 open the tile's menu, and return straight after if it set `e.Handled`.

- [ ] **Step 6: The two handlers.** In `src\BhMaps.App\Views\Pages\PackDetailView.xaml.cs`:

```csharp
    /// <summary>Spec 4.1: the lines are built here, not when the ticks change, so the ticked line names the count
    /// the user can see and no tile is rebuilt that is never opened.</summary>
    private void OnTileMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackTileViewModel tile }
            && DataContext is PackDetailViewModel page)
        {
            page.BuildTileMenu(tile);
            if (tile.MenuItems.Count == 0)
            {
                e.Handled = true;
            }
        }
    }

    private void OnTileMenuButton(object sender, RoutedEventArgs e) => TileMenus.OpenFor(sender);
```

`e.Handled = true` on an empty list is what stops a tile with no picture from opening an empty box.

- [ ] **Step 7: Spec 4.4's one change to the existing tile menus.** In `MapPictureTileViewModel.RebuildMenu` and in `CustomPictureTileViewModel.RebuildMenu`, insert one line between the ticked line and "Apply to all maps":

```csharp
        items.Add(new TileMenuCommand("Apply to a map...", ApplyToChosenMapCommand));
```

and give each class the command. In `MapPictureTileViewModel`:

```csharp
    /// <summary>Spec 4.4: the chooser in map mode, then the shell's apply on the one map it returned.</summary>
    [RelayCommand]
    private async Task ApplyToChosenMapAsync()
    {
        if (await Shell.ChooseMapAsync(FullPath, Title) is { } map)
        {
            await Shell.ApplyPictureAsync(FullPath, [map], clearTicks: false, Title, PackName);
        }
    }
```

In `CustomPictureTileViewModel` the same body with `_picture.DisplayName` in place of `Title` and `_picture.PackName` in place of `PackName`, matching the other commands in that class.

- [ ] **Step 8: Build, test, format, then look at it.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
```

Expected: 0 warnings, 376 tests pass, format clean. Then start the dev-tree app with the three switches and `--quiet`, open a pack, and capture the menu on an orphan tile and on an owned tile plus the chooser with a search typed. The orphan's menu has no "Apply to <map>" line; the owned tile's has one; both have the file name as a muted header, "Apply to a map...", "Apply to all maps", the rule, Edit, Show in folder and "Remove from <pack>". Watch the Debug output for BindingExpression path errors while the menus are open. Stop the dev-tree exe before the task ends.

- [ ] **Step 9: Commit.**

```
Add the right-click menu to pack picture tiles

Spec 4.1: every tile in a pack detail grid carries the TileMenu with the file
name as a header and the eight lines the spec orders, plus the hover menu
button the Backgrounds tiles have so the menu is reachable without a right
click. TileMenuCommand grows a Children list, an enabled flag, a tooltip and a
kind, and the TileMenuItem style grows the setters and triggers that draw a
header, a separator and a flyout, so one ItemsSource still drives every menu.
Spec 4.4's one change to the existing menus: Apply to a map... between the
ticked line and Apply to all maps.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

**Acceptance:** the menu opens on right click, on the dots button and on Shift+F10; every line runs; Remove from pack never touches the game folder and rescans afterwards; the Backgrounds page tiles show the new line in the right place.

---

### Task 4: The map card menu

Spec 4.3.

**Files:**
- Modify: `src\BhMaps.App\ViewModels\MapCardViewModel.cs` (add `MenuItems` next to `IsSelected` at about line 66)
- Modify: `src\BhMaps.App\ViewModels\Pages\MapsViewModel.cs` (`TargetFor` and `BuildCardMenu`, at the end next to `Matches`)
- Modify: `src\BhMaps.App\Views\Pages\MapsView.xaml` (the `MapCardItem` style at 30 to 38)
- Modify: `src\BhMaps.App\Views\Pages\MapsView.xaml.cs` (one handler next to `OnTileMenuButton` at 175)

**Interfaces:**
- Consumes: `MapsViewModel.ApplyPackToAsync`, `ApplyPictureToAsync`, `ResetAsync`, `CanReset` (Task 1); `MainViewModel.ChoosePictureAsync` (Task 2); `TileMenuCommand.Header`, `Separator`, `Flyout` (Task 3); `MainViewModel.OpenBackgroundEditorAsync`, `OpenPlatformEditorAsync(MapEntry, Pack?, string?)`, `OpenAddPicturesAsync(AddPicturesTarget)`; `ExplorerLauncher.Open(string)`; `PackApplier.ApplyToMapsPaths`.
- Produces: `MapCardViewModel.MenuItems`; `MapsViewModel.BuildCardMenu(MapCardViewModel card)`.

- [ ] **Step 1: The card holds a menu.** In `src\BhMaps.App\ViewModels\MapCardViewModel.cs`, next to `IsSelected`:

```csharp
    /// <summary>Spec 4.3: the lines of this card's menu, filled by MapsViewModel.BuildCardMenu when the menu
    /// opens. The building lives on the page, not here, because the lines need the packs, the pictures and the
    /// ticked set, and because building one card's menu per open is cheaper than rebuilding every card's on
    /// every tick.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<TileMenuCommand> MenuItems { get; set; } = [];
```

- [ ] **Step 2: The target rule.** Add to `MapsViewModel`:

```csharp
    /// <summary>Spec 4.3's target rule: a ticked card with other ticked cards acts on the whole ticked set;
    /// anything else acts on itself alone. Right click never changes the ticks (spec 9.2, owner answer q2).</summary>
    private IReadOnlyList<MapEntry> TargetFor(MapCardViewModel card) =>
        card.IsSelected && Shell.SelectedMapCount > 1 ? Shell.SelectedMaps : [card.Map];
```

- [ ] **Step 3: BuildCardMenu.** Also on `MapsViewModel`:

```csharp
    /// <summary>Spec 4.3: one card's menu, built when it opens. A set target disables the three lines that only
    /// mean something for one map rather than hiding them, so the menu's shape does not move under the cursor.</summary>
    public void BuildCardMenu(MapCardViewModel card)
    {
        if (_snapshot is not { } snapshot)
        {
            card.MenuItems = [];
            return;
        }

        var target = TargetFor(card);
        var single = target.Count == 1;
        var one = target[0];
        var setNote = single ? null : "Not for more than one map at a time.";

        var packs = new List<TileMenuCommand>();
        foreach (var pack in snapshot.Packs)
        {
            var has = PackApplier.ApplyToMapsPaths(pack, target).Count > 0;
            var why = single
                ? $"Nothing for {one.DisplayName} in this pack"
                : "Nothing for these maps in this pack";
            packs.Add(new TileMenuCommand(
                pack.Name,
                new AsyncRelayCommand(() => ApplyPackToAsync(target, pack, clearTicks: false)),
                IsEnabled: has,
                ToolTip: has ? null : why));
        }

        // Spec 9.2, owner answer q3: eight My Backgrounds pictures inline, then the chooser, then Add.
        var pictures = new List<TileMenuCommand>();
        foreach (var picture in CustomPictures().Take(8))
        {
            var item = new PictureMenuItem(picture.DisplayName, picture);
            pictures.Add(new TileMenuCommand(
                picture.DisplayName,
                new AsyncRelayCommand(() => ApplyPictureToAsync(target, item, clearTicks: false))));
        }

        pictures.Add(new TileMenuCommand(
            "More pictures...",
            new AsyncRelayCommand(() => ChoosePictureForAsync(one)),
            IsEnabled: single,
            ToolTip: setNote));
        pictures.Add(new TileMenuCommand(
            "Add Custom Image...",
            new AsyncRelayCommand(
                () => Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.Ticked, null, target)))));

        var slot = one.BackgroundSlots.FirstOrDefault();
        card.MenuItems =
        [
            TileMenuCommand.Header(single ? one.DisplayName : $"{target.Count} selected maps"),
            TileMenuCommand.Flyout("Apply pack", packs),
            TileMenuCommand.Flyout("Apply picture", pictures),
            TileMenuCommand.Separator(),
            new TileMenuCommand(
                "Edit background",
                new AsyncRelayCommand(() => EditBackgroundAsync(one, slot)),
                IsEnabled: single && slot is not null,
                ToolTip: single ? null : setNote),
            new TileMenuCommand(
                "Edit platforms",
                new AsyncRelayCommand(() => Shell.OpenPlatformEditorAsync(one, null)),
                IsEnabled: single,
                ToolTip: setNote),
            TileMenuCommand.Separator(),
            new TileMenuCommand(
                "Reset to default",
                new AsyncRelayCommand(() => ResetAsync(target, clearTicks: false)),
                IsEnabled: CanReset,
                ToolTip: CanReset ? null : "There is no Default pack to reset to."),
            new TileMenuCommand("Show in game folder", new RelayCommand(() => ShowMapFolder(one))),
        ];
    }

    /// <summary>Spec 4.3 line 2's last inline row: the chooser in picture mode, then the shell's apply.</summary>
    private async Task ChoosePictureForAsync(MapEntry map)
    {
        if (await Shell.ChoosePictureAsync(map) is { } path)
        {
            await Shell.ApplyPictureAsync(
                path, [map], clearTicks: false, Path.GetFileNameWithoutExtension(path));
        }
    }

    /// <summary>Spec 4.3 line 4: the background editor on the game's own copy of the map's first slot, which is
    /// the only picture a card on its own names.</summary>
    private Task EditBackgroundAsync(MapEntry map, string? slot) =>
        slot is null
            ? Task.CompletedTask
            : Shell.OpenBackgroundEditorAsync(
                new BackgroundEditorRequest(
                    Path.Combine(Shell.Services.GamePath, AssetPath.Background(slot)), null, slot));

    private void ShowMapFolder(MapEntry map)
    {
        if (ExplorerLauncher.Open(Path.Combine(Shell.Services.GamePath, map.FolderName)) is { } error)
        {
            Shell.Dialogs.Error("Could not open the folder", error);
        }
    }
```

`CustomPictures()` is the private helper `MapsViewModel` already uses to build `PictureChoices`; use it unchanged, so the flyout and the bar's menu cannot offer different pictures in a different order. "Show in game folder" is `ExplorerLauncher.Open`, not `Reveal`, because the target is a folder.

- [ ] **Step 4: The card carries the menu.** In `src\BhMaps.App\Views\Pages\MapsView.xaml`, add two lines to the `MapCardItem` style at line 30, after the ToolTip setter:

```xml
      <Setter Property="ContextMenu" Value="{StaticResource TileMenu}" />
      <EventSetter Event="ContextMenuOpening" Handler="OnCardMenuOpening" />
```

`TileMenu` is `x:Shared="False"`, so each container gets its own instance, and a ContextMenu set on an element inherits that element's DataContext, which is the card. `OnCardMouseDown` needs no change: it handles the left button only, so a right click already leaves the ticks and the panel alone (spec 4.3).

- [ ] **Step 5: The handler.** In `src\BhMaps.App\Views\Pages\MapsView.xaml.cs`, next to `OnTileMenuButton`:

```csharp
    /// <summary>Spec 4.3: the card's lines are built when the menu opens, because the target depends on the ticks
    /// and there are as many cards as the game has maps.</summary>
    private void OnCardMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: MapCardViewModel card } && DataContext is MapsViewModel page)
        {
            page.BuildCardMenu(card);
        }
    }
```

Press the Menu key on a focused card in Step 6. If nothing opens, add `TileMenus.OnPreviewKeyDown(sender, e);` as the first line of the existing `OnCardKeyDown`, before its Space and Enter branches, and return when it sets `e.Handled`.

- [ ] **Step 6: Build, test, format, then look at it.**

```
dotnet build BhMaps.slnx -c Debug --artifacts-path <ART>
dotnet test BhMaps.slnx --artifacts-path <ART>
dotnet format BhMaps.slnx --verify-no-changes
dotnet build BhMaps.slnx -c Release --artifacts-path <ART>
```

Expected: both configurations 0 warnings; 376 tests pass; format clean. Then on the dev tree: right-click an unticked card (the header is the map's name and the four one-map lines are live), tick three cards and right-click one of them (the header reads "3 selected maps", Edit background, Edit platforms and More pictures... are greyed and show their tooltips, Reset to default confirms with the count), open both flyouts, and check that a right click changes neither the ticks nor the panel. Capture the unticked menu, the set menu and both flyouts. Stop the dev-tree exe.

- [ ] **Step 7: Commit.**

```
Add the right-click menu to map cards

Spec 4.3: a card menu with an Apply pack and an Apply picture flyout, the two
editors, Reset to default and Show in game folder, built when it opens so the
target follows the ticks without rebuilding every card. A ticked card among
other ticked cards acts on the set and the header says so; a pack that holds
nothing for the target is disabled with the reason. Right click changes
neither the ticks nor the panel.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Cg2v1G84yHmoB3TBBM3omg
```

**Acceptance:** every line of spec 4.3 is present in the spec's order; the target rule holds both ways; the selection bar behaves exactly as it did before Task 1; Debug and Release build with 0 warnings.

---

## Risks

- **The DynamicResource self-reference (D1).** If `ItemContainerStyle="{DynamicResource TileMenuItem}"` does not resolve, a flyout's rows draw with the default MenuItem look and their Header binding fails silently rather than throwing. Check the first flyout capture in Task 4 Step 6 before going further; the fallback is a second key `TileMenuSubItem` that copies `TileMenuItem` and points back at it with StaticResource.
- **Style.Triggers ordering.** WPF requires the triggers block last in a Style. Putting it in the wrong place is a XAML parse error at startup, not a build error, so run the app after Task 3 Step 2 rather than only building.
- **Silent binding failures.** The widened record means every existing menu now binds Children, IsEnabled, ToolTip and Kind. Watch the Debug output for BindingExpression path errors while a menu is open; a failure there degrades a menu quietly.
- **Remove from pack deletes a file.** It is the one destructive line in Part B. The packs-root guard and the confirm are both required; without the guard a wrong PicturePath would delete outside the library. Never run it against the real library.
- **Ordering.** Task 4 needs Tasks 1, 2 and 3. Task 3 needs Task 2 only for its "Apply to a map..." line. The tasks cannot be reordered.
- **A card menu on a map with no level data** has no background slots, so Edit background is disabled and Apply picture writes nothing; the shell's own "These maps have no background slots" line covers it.

## Open questions

- Spec 4.1 line 8 says Remove from pack uses "the same removed-by-BhMaps folder convention" as Remove from library, but the code Remove from library runs deletes the file outright rather than moving it anywhere. This plan follows the code, so the two lines stay identical in behaviour. If the owner meant a move into a removed-by-BhMaps folder, that is a change to `RemoveFromLibraryAsync` too and belongs in its own task.
- Spec 4.3 line 4 says Edit background opens "as the panel's tile Edit does", but a panel tile opens on a chosen pack's copy, which a card on its own does not name. This plan opens on the game's own copy of the map's first slot and passes a null pack name. Confirm on the capture in Task 4 Step 6.
- The chooser's picture list is every picture in every pack's Backgrounds folder; the spec does not say whether the picture the map is already showing should be marked. It is not, in this plan.
