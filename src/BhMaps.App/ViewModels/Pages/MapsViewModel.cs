using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using BhMaps.App.Services;
using BhMaps.Core.Layout;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 3.1: the chip row with "Select all" at its right end, and the grid of composed map cards.
/// No summary bar and no composition bars; the header carries the search box, the zoom and Reset all to
/// default, so nothing the chips do can move the slider.</summary>
public partial class MapsViewModel : PageViewModel, ITileSized
{
    private const string AllChip = MainViewModel.AllLevelSet;

    /// <summary>Every card the last scan produced. Cards is this list under the chip and the search.</summary>
    private readonly List<MapCardViewModel> _all = [];

    private readonly ObservableCollection<string> _chips = [];

    /// <summary>The sets behind the set chips, so a chip label maps back to the set name a map is measured against.</summary>
    private readonly List<UiSet> _uiSets = [];

    private ScanSnapshot? _snapshot;
    private CancellationTokenSource? _previews;

    public MapsViewModel(MainViewModel shell)
        : base(shell)
    {
        Cards = [];
        _chips.Add(AllChip);

        // After the collections, because setting it runs the change hook that filters them.
        SearchText = "";

        TileSize = shell.Services.Settings.MapsTileSize;

        // 3.1: the chip is the shell's, so a chip picked on Backgrounds or Platforms arrives as a shell change
        // rather than as a set of this page's property. Subscribed for the life of the app, like the page is.
        shell.PropertyChanged += OnShellLevelSetChanged;
    }

    public override string Title => "Maps";

    /// <summary>The cards the chip and the search leave visible, in display-name order.</summary>
    public ObservableCollection<MapCardViewModel> Cards { get; }

    /// <summary>Every card the last scan produced, filtered or not. The rows pages build their rows from it.</summary>
    public IReadOnlyList<MapCardViewModel> AllCards => _all;

    /// <summary>The header's search box (spec 3.1). The page's own string now; the shell has no search box left.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>"All" and the UI set labels. Without level data the set chips are gone entirely (spec 3.6). An
    /// ObservableCollection behind the read-only surface, because the row changes when the level data does.
    /// Owner change 2026-09-13: the "Selected" chip that used to appear with a selection is gone.</summary>
    public IReadOnlyList<string> Chips => _chips;

    /// <summary>The chip the level-set filter is on. 3.1: one filter for Maps, Backgrounds and Platforms, so
    /// the value lives on the shell and this is the chip row's way in and out of it.</summary>
    public string SelectedChip
    {
        get => Shell.SelectedLevelSet;
        set => Shell.SelectedLevelSet = value;
    }

    /// <summary>How big the cards are drawn, persisted as mapsTileSize. It sets the card's target width
    /// through <see cref="TargetCardWidth"/>; how many cards a row holds, and the width they end up at, is the
    /// justified panel's answer.</summary>
    [ObservableProperty]
    public partial TileSize TileSize { get; set; }

    /// <summary>The card whose right panel is open. Null closes the panel.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EscapeCommand))]
    public partial MapCardViewModel? Selected { get; set; }

    /// <summary>The right panel for <see cref="Selected"/> (spec 7.2), rebuilt whenever the selection changes and
    /// after every scan, so the panel always shows the state the last scan measured. Null when nothing is open.</summary>
    [ObservableProperty]
    public partial MapPanelViewModel? Panel { get; set; }

    /// <summary>3.0: about how wide a card should be at this size. The panel takes it as a wish and hands the
    /// cards whatever makes the row reach the right edge, so the grid no longer leaves a hole beside the open
    /// panel (wireframe 8.1).</summary>
    public double TargetCardWidth => TileSizes.CardWidth(TileSize);

    /// <summary>Spec 3.1's density steps, now three rather than nine. Large and Medium carry the name and the
    /// tag; Small drops both, tightens the card to 4 px padding, and shows Missing as a mark on the picture
    /// instead of a word under it. ShowTagRow is the size's answer for every card; MapCardViewModel.ShowTag is
    /// one card's own answer about whether it has a tag at all. Both have to be true for a tag to be drawn, so
    /// they keep different names.</summary>
    public bool ShowName => TileSize != TileSize.Small;

    public bool ShowTagRow => TileSize != TileSize.Small;

    public bool ShowMissingMark => TileSize == TileSize.Small;

    public double NameFontSize => TileSize == TileSize.Large ? 15 : TileSize == TileSize.Medium ? 13 : 12;

    public Thickness CardPadding => TileSize == TileSize.Small ? new Thickness(4) : new Thickness(8);

    /// <summary>Spec 3.1: why the grid is empty, in one line.</summary>
    public string EmptyText
    {
        get
        {
            var what = SelectedChip switch
            {
                AllChip => "map",
                MapCatalog.MinigameLabel => "minigame map",

                // RebuildChips clears the chip ListBox's items, and the ListBox pushes its lost selection back
                // through this two-way binding, so the getter can run between the null and the chip put back.
                null or "" => "map",
                _ => $"{SelectedChip.ToLowerInvariant()} map",
            };
            if (SearchText.Length > 0)
            {
                return $"No map matches '{SearchText}'.";
            }

            return $"No {what} to show.";
        }
    }

    public bool ShowClearSearch => SearchText.Length > 0;

    /// <summary>The empty state's one action, which only a search gives it: with nothing typed there is nothing
    /// to undo and the state is just the sentence.</summary>
    public string EmptyActionText => ShowClearSearch ? "Clear search" : "";

    /// <summary>Opens one map's right panel. A click on a card runs the generated command.</summary>
    [RelayCommand]
    public void OpenMap(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            return;
        }

        if (_all.FirstOrDefault(c => c.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase)) is { } card)
        {
            Selected = card;
        }

        Shell.LastOpenedMap = folderName;
    }

    /// <summary>The no-results state's way back when a search caused it (spec 3.1).</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>Spec 3.2: Escape closes the panel. The search box takes Escape before it does, in
    /// MapsView.OnPageKeyDown. With no panel open it says no rather than doing nothing, so the keystroke carries
    /// on to the window's own Escape, which is the status strip's Cancel (3.0).</summary>
    [RelayCommand(CanExecute = nameof(HasPanel))]
    private void Escape() => Selected = null;

    private bool HasPanel => Selected is not null;

    /// <summary>The header's menu (3.0): a reset of every map is destructive and rare, so it reads as a line in
    /// the menu rather than as a button one slip away from the zoom. CanWrite, not IsNotBusy: it writes into the
    /// game folder, so the line is dead while that folder is missing (spec 7.8), the way a tile's menu is
    /// built.</summary>
    public override IReadOnlyList<TileMenuCommand>? PageMenu =>
        [new TileMenuCommand("Reset all maps", ResetAllCommand, IsEnabled: Shell.CanWrite, IsDestructive: true)];

    /// <summary>Spec 7.2's one page action: every folder in the game tree back to the Default pack, or deleted for
    /// the game to regenerate when there is no Default pack.</summary>
    [RelayCommand]
    private async Task ResetAllAsync()
    {
        if (_snapshot is not { } snapshot)
        {
            return;
        }

        var count = snapshot.Tree.Folders.Count;
        var defaultPack = snapshot.DefaultPack;
        var message = defaultPack is null
            ? $"Reset the map art in {MainViewModel.Count(count, "folder")}? The files are deleted and Brawlhalla regenerates its defaults on the next launch."
            : $"Reset {MainViewModel.Count(count, "map")} to the Default pack? {MainViewModel.RestoresArt} Undo puts them back.";

        // The button names what it resets, in the same noun the message counts in: folders when there is no
        // Default pack to put back, maps when there is.
        var verb = defaultPack is null
            ? $"Reset {MainViewModel.Count(count, "folder")}"
            : $"Reset {MainViewModel.Count(count, "map")}";
        if (!Shell.Dialogs.Confirm("Reset all maps", message, verb))
        {
            return;
        }

        // The game-running policy belongs to the launcher inside RunGameWriteAsync, so there is no second prompt.
        var gamePath = Shell.Services.GamePath;

        // Everything the reset may write: what the folders hold now, plus the Default pack's own files, because a
        // file the pack creates where the game has none is only undoable when the snapshot records it as absent.
        var undoPaths = snapshot.Tree.Folders
            .SelectMany(f => f.Files.Select(file => Path.Combine(f.Name, file.Name)))
            .Concat(defaultPack?.RelativePaths ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Spec 7: every map goes back to default, so every remembered edit for every map goes too.
        var matched = new Dictionary<string, IReadOnlyList<Pack>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in snapshot.Catalog.Maps)
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);
            matched[map.FolderName] = RecordReset.MatchedPacks(map, status, snapshot.Packs);
        }

        var allMatched = matched.Values
            .SelectMany(packs => packs)
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResetOutcome? outcome = null;
        await Shell.RunGameWriteAsync(
            $"Resetting {MainViewModel.Count(count, "map")}",
            undoPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    outcome = MapReset.ResetAll(gamePath, defaultPack, progress, ct);
                    foreach (var map in snapshot.Catalog.Maps)
                    {
                        RecordReset.Clear(map, matched[map.FolderName]);
                    }
                },
                ct),
            $"Reset {MainViewModel.Count(count, "map")} to the Default pack.",
            libraryUndoPaths: RecordReset.UndoPaths(allMatched, Shell.Services.LibraryPath),
            artMaps: snapshot.Catalog.Maps,
            resetThumbnails: true,
            // Without a Default pack the reset deletes rather than copies, so there is no source to record.
            sources: defaultPack is null ? null : AppliedSources.FromPack(defaultPack, undoPaths));

        if (outcome is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be reset", outcome.Failures);
        }
    }

    /// <summary>Spec 4.3: one pack onto the maps given, whoever gave them.</summary>
    public async Task ApplyPackToAsync(IReadOnlyList<MapEntry> maps, Pack pack)
    {
        if (maps.Count == 0
            || !Confirm(
                $"Apply {pack.Name}",
                $"Apply {pack.Name} to {MainViewModel.Count(maps.Count, "map")}?",
                MainViewModel.WritesArt,
                $"Apply to {MainViewModel.Count(maps.Count, "map")}",
                maps))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        ApplyResult? result = null;
        var targetPaths = PackApplier.ApplyToMapsPaths(pack, maps);
        await Shell.RunGameWriteAsync(
            $"Applying {pack.Name}",
            targetPaths,
            (progress, ct) => Task.Run(
                () => { result = PackApplier.ApplyToMaps(pack, maps, gamePath, progress, ct); }, ct),
            $"{pack.Name} applied to {MainViewModel.Count(maps.Count, "map")}.",
            pack.Name,
            artMaps: maps,
            sources: AppliedSources.FromPack(pack, targetPaths));

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>Spec 4.3: one picture onto the maps given.</summary>
    public async Task ApplyPictureToAsync(IReadOnlyList<MapEntry> maps, CustomPicture picture)
    {
        // A custom picture need not be in a pack: one the user put in the game folder by hand has no library copy
        // at all, so the file is resolved by the rule a tile's own menu uses rather than by indexing the packs.
        if (CustomPictureTileViewModel.SourcePath(picture, Shell.Services.GamePath) is not { } source)
        {
            Shell.Dialogs.Info(
                "Nothing to apply",
                $"{picture.Name} has no file in the library or in the game folder.");
            return;
        }

        // The name the menu row was labelled with, so the done line reports the row that was clicked (spec 2.2).
        await Shell.ApplyPictureAsync(source, maps, picture.Name, picture.PackName);
    }

    /// <summary>Spec 4.3: the maps given, back to the Default pack. One map resets without asking; more than one
    /// names the count and the maps first.</summary>
    public async Task ResetAsync(IReadOnlyList<MapEntry> maps)
    {
        if (_snapshot is not { } snapshot || snapshot.DefaultPack is not { } defaultPack || maps.Count == 0
            || !Confirm(
                maps.Count == 1
                    ? $"Reset {maps[0].DisplayName}"
                    : $"Reset {MainViewModel.Count(maps.Count, "map")}",
                $"Reset {MainViewModel.Count(maps.Count, "map")} to the Default pack?",
                MainViewModel.RestoresArt,
                $"Reset {MainViewModel.Count(maps.Count, "map")}",
                maps))
        {
            return;
        }

        var gamePath = Shell.Services.GamePath;
        var failures = new List<FileFailure>();

        // Spec 7: each map's files go back to default, so the edits the packs that map matches remembered for it
        // go too. The packs are read from the scan the reset started from, once per map.
        var matched = new Dictionary<string, IReadOnlyList<Pack>>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);
            matched[map.FolderName] = RecordReset.MatchedPacks(map, status, snapshot.Packs);
        }

        var allMatched = matched.Values
            .SelectMany(packs => packs)
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resetPaths = maps.SelectMany(m => PackApplier.ResetMapPaths(snapshot.Tree, m, defaultPack))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await Shell.RunGameWriteAsync(
            "Resetting",
            resetPaths,
            (progress, ct) => Task.Run(
                () =>
                {
                    foreach (var map in maps)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(map.DisplayName);
                        failures.AddRange(
                            MapReset.ResetMap(gamePath, map.FolderName, map.BackgroundSlots, defaultPack).Failures);
                        RecordReset.Clear(map, matched[map.FolderName]);
                    }
                },
                ct),
            $"Reset {MainViewModel.Count(maps.Count, "map")} to the Default pack.",
            libraryUndoPaths: RecordReset.UndoPaths(allMatched, Shell.Services.LibraryPath),
            artMaps: maps,
            resetThumbnails: true,
            sources: AppliedSources.FromPack(defaultPack, resetPaths));

        Shell.Dialogs.ShowFailures("Some files could not be reset", failures);
    }

    /// <summary>True once a scan has found a Default pack, which is the only thing a reset needs.</summary>
    public bool CanReset => _snapshot?.DefaultPack is not null;

    /// <summary>Spec 3.3: a write to more than one map names the count and the maps first; one map is one click.</summary>
    private bool Confirm(string title, string question, string effect, string primary, IReadOnlyList<MapEntry> maps) =>
        maps.Count <= 1
        || Shell.Dialogs.Confirm(
            title,
            MainViewModel.ConfirmBody(question, effect, [.. maps.Select(m => m.DisplayName)]),
            primary);

    public override void Refresh(ScanSnapshot snapshot) => Refresh(snapshot, null);

    public override void Refresh(ScanSnapshot snapshot, IReadOnlyList<string>? writtenFolders)
    {
        _snapshot = snapshot;

        // Every card is replaced, so the previews still in flight are for objects nothing shows any more.
        _previews?.Cancel();
        _previews?.Dispose();
        _previews = new CancellationTokenSource();

        var opened = Selected?.FolderName;
        _all.Clear();
        foreach (var map in snapshot.Catalog.Maps)
        {
            snapshot.MapStatuses.TryGetValue(map.FolderName, out var status);

            _all.Add(new MapCardViewModel(map, status, snapshot.CustomPictures));
        }

        RebuildChips(snapshot.Catalog);
        ApplyFilter();

        // The card the panel was on is a new object now, so it is found again by folder name rather than left
        // pointing at one nothing draws.
        Selected = opened is null
            ? null
            : _all.FirstOrDefault(c => c.FolderName.Equals(opened, StringComparison.OrdinalIgnoreCase));

        // Spec 11: a new card shows nothing until its preview lands, so the maps the write touched are loaded
        // before the rest of the alphabet and stop showing what was there before the write that much sooner.
        var byFolder = new Dictionary<string, MapCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in _all)
        {
            byFolder[card.FolderName] = card;
        }

        var order = LoadOrder.Prioritise([.. _all.Select(c => c.FolderName)], writtenFolders);
        _ = LoadPreviewsAsync([.. order.Select(folder => byFolder[folder])], snapshot.Catalog.HasLevelData, _previews.Token);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>The shared chip changed, here or on another page: the row shows it and the grid answers it.</summary>
    private void OnShellLevelSetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.SelectedLevelSet))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedChip));
        ApplyFilter();
    }

    /// <summary>A card click and the re-selection every scan does both land here, so the panel is built in one
    /// place. The panel it replaces is cancelled: its composites are for a state that is gone.</summary>
    partial void OnSelectedChanged(MapCardViewModel? value)
    {
        Panel?.Cancel();
        RestoreSelectedCard();
        if (value is null || _snapshot is not { } snapshot)
        {
            Panel = null;
            return;
        }

        snapshot.MapStatuses.TryGetValue(value.FolderName, out var status);
        var panel = new MapPanelViewModel(Shell, value.Map, status, snapshot);
        Panel = panel;

        // Fire and forget: the panel turns its own file failures into fallbacks, so there is nothing to await for.
        _ = panel.LoadAsync();
    }

    /// <summary>2.8: one map is selected at a time, and it is the map the panel is open on. The grid's own
    /// selection is what draws the light border, so it is put back here whenever something else has moved it: a
    /// right press selects the card it lands on, and a card the filter removed loses its container with it.</summary>
    public void RestoreSelectedCard()
    {
        foreach (var card in _all)
        {
            card.IsSelected = ReferenceEquals(card, Selected);
        }
    }

    partial void OnTileSizeChanged(TileSize value)
    {
        if (Shell.Services.Settings.MapsTileSize != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { MapsTileSize = value });
        }

        // The card template reads these numbers rather than carrying a pile of triggers of its own.
        OnPropertyChanged(nameof(TargetCardWidth));
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowTagRow));
        OnPropertyChanged(nameof(ShowMissingMark));
        OnPropertyChanged(nameof(NameFontSize));
        OnPropertyChanged(nameof(CardPadding));
    }

    /// <summary>The library's custom pictures, as the last scan built them (spec 4). Empty before the first scan.</summary>
    private IReadOnlyList<CustomPicture> CustomPictures() => _snapshot?.CustomPictures ?? [];

    /// <summary>Fills the cards one at a time, in the order given: the maps a write touched first (spec 11), then
    /// display order, so the cards the grid shows first are the ones that fill first. The render queue serialises
    /// the composites anyway, so one at a time costs nothing. Fire and forget: the card
    /// turns its own file failures into a fallback, so the only thing left to stop for is cancellation.</summary>
    private async Task LoadPreviewsAsync(IReadOnlyList<MapCardViewModel> cards, bool hasLevelData, CancellationToken ct)
    {
        foreach (var card in cards)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await card.LoadPreviewAsync(Shell.Services, hasLevelData, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Rebuilt only when the row actually changes, because replacing the items would clear the chip
    /// ListBox's selection and push a null back through it. The catalog is nullable because the chip hook can
    /// reach this before the first scan, and there is no row to build then.</summary>
    private void RebuildChips(MapCatalog? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        // All and the set chips.
        List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label)];

        if (_chips.SequenceEqual(wanted))
        {
            return;
        }

        _uiSets.Clear();
        _uiSets.AddRange(catalog.UiSets);
        var chosen = SelectedChip;

        // Synced in place rather than cleared and refilled: clearing the row pushes a null SelectedChip through
        // the two-way binding, and the ApplyFilter answering it empties and refills Cards, which disturbs the
        // selection. Inserting and removing single chips disturbs neither the chip ListBox's selection nor Cards.
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

        // A set chip that has just gone, because the level data went with it, would otherwise leave the grid
        // filtered by a chip the row no longer offers.
        SelectedChip = wanted.Contains(chosen) ? chosen : AllChip;
    }

    private void ApplyFilter()
    {
        Cards.Clear();
        foreach (var card in _all.Where(Matches))
        {
            Cards.Add(card);
        }

        // A card the filter just removed loses its container, and a released ListBoxItem clears its own
        // IsSelected, which the two-way binding writes back, so the open card's border is put back here.
        RestoreSelectedCard();

        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowClearSearch));
        OnPropertyChanged(nameof(EmptyActionText));
    }

    /// <summary>The chip filter, then the header search box on top of it.</summary>
    private bool Matches(MapCardViewModel card)
    {
        if (!NameFilter.Matches(card.DisplayName, SearchText))
        {
            return false;
        }

        // 3.0: a search reads every map, whatever the chip says, so a typed name still reaches a minigame map
        // that the All chip leaves out.
        if (SearchText.Length > 0)
        {
            return true;
        }

        return SelectedChip switch
        {
            AllChip => !MapCatalog.IsMinigame(card.Map),
            _ => _uiSets.FirstOrDefault(s => s.Label == SelectedChip) is { } set
                && (set.Name == MapCatalog.MinigameSetName
                    ? MapCatalog.IsMinigame(card.Map)
                    : card.Map.Sets.Contains(set.Name, StringComparer.OrdinalIgnoreCase)),
        };
    }

    /// <summary>Spec 4.3: one card's menu, built when it opens. 2.8: the card under the pointer is the whole
    /// subject, so every line is about that one map.</summary>
    public void BuildCardMenu(MapCardViewModel card)
    {
        if (_snapshot is not { } snapshot)
        {
            card.MenuItems = [];
            return;
        }

        var one = card.Map;
        IReadOnlyList<MapEntry> target = [one];

        var packs = new List<TileMenuCommand>();
        var refused = new List<string>();
        foreach (var pack in snapshot.Packs)
        {
            // 3.1: a pack with nothing for this map is left out of the flyout rather than offered and refused,
            // so every line in it is a line that would do something. The ones left out are counted in the foot
            // line below, which names them in its tooltip, so a missing pack is still accounted for.
            if (PackApplier.ApplyToMapsPaths(pack, target).Count == 0)
            {
                refused.Add(pack.Name);
                continue;
            }

            // 2.8: hiding a pack is for the browsing lists, not for acting, so a hidden pack is still offered
            // here and only says that it is hidden.
            // 3.1: the Default pack can be hidden like any other, so a hidden one says so here too.
            var hidden = Shell.Services.Settings.IsHidden(pack.Name);
            packs.Add(new TileMenuCommand(
                hidden ? $"{pack.Name} \u00b7 hidden" : pack.Name,
                new AsyncRelayCommand(() => ApplyPackToAsync(target, pack))));
        }

        if (refused.Count > 0 && packs.Count == 0)
        {
            // Every pack refused: the flyout is the one line saying so, rather than a separator over nothing.
            packs.Add(new TileMenuCommand(
                $"No pack has anything for {one.DisplayName}",
                null,
                IsEnabled: false,
                ToolTip: string.Join(", ", refused)));
        }
        else if (refused.Count > 0)
        {
            packs.Add(TileMenuCommand.Separator());
            packs.Add(new TileMenuCommand(
                refused.Count == 1
                    ? $"1 pack has nothing for {one.DisplayName}"
                    : $"{refused.Count} packs have nothing for {one.DisplayName}",
                null,
                IsEnabled: false,
                ToolTip: string.Join(", ", refused)));
        }
        else if (packs.Count == 0)
        {
            // No packs at all: an empty flyout draws as a dead line with an arrow, so it says why it is empty.
            packs.Add(new TileMenuCommand("No packs yet", null, IsEnabled: false));
        }

        // Spec 9.2, owner answer q3: eight My Backgrounds pictures inline, then the chooser, then Add.
        var pictures = new List<TileMenuCommand>();
        foreach (var picture in CustomPictures().Take(8))
        {
            pictures.Add(new TileMenuCommand(
                picture.Name,
                new AsyncRelayCommand(() => ApplyPictureToAsync(target, picture))));
        }

        pictures.Add(new TileMenuCommand(
            "More pictures...",
            new AsyncRelayCommand(() => ChoosePictureForAsync(one))));

        // Owner ruling 8: the Add window is given the one map it would land on, so the window's own target line
        // says what the menu's header said.
        pictures.Add(new TileMenuCommand(
            "Add Custom Image...",
            new AsyncRelayCommand(() => Shell.OpenAddPicturesAsync(
                new AddPicturesTarget(AddPicturesTargetKind.Map, one)))));

        var slot = one.BackgroundSlots.FirstOrDefault();
        snapshot.MapStatuses.TryGetValue(one.FolderName, out var status);

        card.MenuItems =
        [
            TileMenuCommand.Header(one.DisplayName, MapArtText.Describe(one, status, snapshot)),
            TileMenuCommand.Flyout("Apply pack", packs),
            TileMenuCommand.Flyout("Apply background", pictures),
            TileMenuCommand.Separator(),
            new TileMenuCommand(
                "Edit background",
                new AsyncRelayCommand(() => EditBackgroundAsync(one, slot)),
                IsEnabled: slot is not null),
            // Spec 9: the editor still opens on a set of maps, and from here that set is the one map named above.
            new TileMenuCommand(
                "Edit platforms",
                new AsyncRelayCommand(() => Shell.OpenPlatformEditorAsync(target, null))),
            TileMenuCommand.Separator(),
            new TileMenuCommand(
                "Reset map",
                new AsyncRelayCommand(() => ResetAsync(target)),
                IsEnabled: CanReset,
                ToolTip: CanReset ? null : MapPanelViewModel.NoDefaultPackTip),
            new TileMenuCommand("Show in game folder", new RelayCommand(() => ShowMapFolder(one))),
        ];
    }

    /// <summary>Spec 4.3 line 2's last inline row: the chooser in picture mode, then the shell's apply.</summary>
    private async Task ChoosePictureForAsync(MapEntry map)
    {
        if (await Shell.ChoosePictureAsync(map) is { } path)
        {
            await Shell.ApplyPictureAsync(path, [map], Path.GetFileNameWithoutExtension(path));
        }
    }

    /// <summary>Spec 4.3 line 4: the background editor on the game's own copy of the map's first slot, which is
    /// the only picture a card on its own names (owner ruling 5).</summary>
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
}
