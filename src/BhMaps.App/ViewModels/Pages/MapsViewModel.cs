using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>One row of the Apply picture menu. A null Picture is the "Add Image..." row.</summary>
public sealed record PictureMenuItem(string Header, CustomPicture? Picture);

/// <summary>Spec 3.1: the chip row with "Select all" at its right end, and the grid of composed map cards.
/// No summary bar and no composition bars; the header carries the search box, the zoom and Reset all to
/// default, so nothing the chips do can move the slider.</summary>
public partial class MapsViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinZoom;
    public const int MaxZoom = AppSettings.MaxZoom;

    private const string AllChip = "All";
    private const string TickedChip = "Selected";

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

        // After the collections, because setting them runs the change hooks that filter them.
        SearchText = "";
        SelectedChip = AllChip;

        // A stored zoom from another version, or a hand-edited one, is clamped rather than trusted.
        Zoom = Math.Clamp(shell.Services.Settings.MapsZoom, MinZoom, MaxZoom);

        // The ticked count is the shell's, and this page's own cards are what it counts. The page lives as long
        // as the shell, so there is nothing to unsubscribe from.
        shell.PropertyChanged += OnShellChanged;
    }

    public override string Title => "Maps";

    /// <summary>The cards the chip and the search leave visible, in display-name order.</summary>
    public ObservableCollection<MapCardViewModel> Cards { get; }

    /// <summary>Every card the last scan produced, filtered or not. The shell's ticked set reads this.</summary>
    public IReadOnlyList<MapCardViewModel> AllCards => _all;

    /// <summary>The ticked maps as catalog entries, in display order.</summary>
    public IReadOnlyList<MapEntry> TickedMaps => _all.Where(c => c.IsSelected).Select(c => c.Map).ToList();

    /// <summary>The header's search box (spec 3.1). The page's own string now; the shell has no search box left.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>"All", the UI set labels, and "Selected" once anything is ticked. Without level data the
    /// set chips are gone entirely (spec 3.6). An ObservableCollection behind the read-only surface, because the
    /// row changes when the level data does and when the first map is ticked.</summary>
    public IReadOnlyList<string> Chips => _chips;

    [ObservableProperty]
    public partial string SelectedChip { get; set; }

    /// <summary>Columns in the grid, MinZoom to MaxZoom, persisted as mapsZoom.</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    /// <summary>The card whose right panel is open. Null closes the panel.</summary>
    [ObservableProperty]
    public partial MapCardViewModel? Selected { get; set; }

    /// <summary>The right panel for <see cref="Selected"/> (spec 7.2), rebuilt whenever the selection changes and
    /// after every scan, so the panel always shows the state the last scan measured. Null when nothing is open.</summary>
    [ObservableProperty]
    public partial MapPanelViewModel? Panel { get; set; }

    /// <summary>Which half of the map panel's segment is showing, for the life of the session (spec 3.2).</summary>
    public bool PanelShowsPlatforms { get; set; }

    /// <summary>Spec 3.1's density steps. At 7 and 8 the name shrinks and the tag goes; at 9 and 10 the name row
    /// goes with it, the card tightens to 4 px padding and 8 px gaps, and Missing becomes a mark on the picture.
    /// ShowTagRow is the zoom's answer for every card; MapCardViewModel.ShowTag is one card's own answer about
    /// whether it has a tag at all. Both have to be true for a tag to be drawn, so they keep different names.</summary>
    public bool ShowName => Zoom <= 8;

    public bool ShowTagRow => Zoom <= 6;

    public bool ShowMissingMark => Zoom >= 9;

    public double NameFontSize => Zoom <= 6 ? 13 : 12;

    public Thickness CardPadding => Zoom <= 8 ? new Thickness(8) : new Thickness(4);

    public Thickness CardMargin => Zoom <= 8 ? new Thickness(0, 0, 12, 12) : new Thickness(0, 0, 8, 8);

    /// <summary>Spec 3.1: why the grid is empty, in one line.</summary>
    public string EmptyText
    {
        get
        {
            var what = SelectedChip switch
            {
                AllChip => "map",
                TickedChip => "selected map",

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

    /// <summary>Spec 3.1's first-run line: nothing in the library but the Default pack, and no any-map picture
    /// either (plan decision A-D6, settled here).</summary>
    public bool ShowFirstRunLine =>
        _snapshot is { } s
        && !s.Packs.Any(p => !p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase))
        && s.CustomPictures.Count == 0;

    /// <summary>Spec 11: "3 of 67 maps selected". The second number is every map, not the shown ones.</summary>
    public string SelectionText => $"{Shell.SelectedMapCount} of {_all.Count} maps selected";

    public bool HasTicks => Shell.SelectedMapCount > 0;

    /// <summary>The packs the Apply pack menu offers, in library order.</summary>
    public IReadOnlyList<Pack> PackChoices => _snapshot?.Packs ?? [];

    /// <summary>The Apply picture menu: the library's any-map pictures, then "Add Image..." (spec 3.3).
    /// Rebuilt by every scan, so a picture that has just been imported is on the menu the next time it opens.</summary>
    public IReadOnlyList<PictureMenuItem> PictureChoices { get; private set; } = [];

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

    /// <summary>Spec 3.1: ticks every map the chips and the search currently show, which is what makes a preset
    /// two clicks: a chip, then this.</summary>
    [RelayCommand]
    private void SelectAllShown()
    {
        foreach (var card in Cards.ToList())
        {
            card.IsSelected = true;
        }
    }

    /// <summary>The no-results state's way back when a search caused it (spec 3.1).</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>Owner change O4, which turns spec 3.2's order around: Escape cancels the selection first, because
    /// someone mid-selection who reaches for Escape means the ticks, and closes the panel once nothing is ticked.
    /// The search box takes Escape before either of them, in MapsView.OnPageKeyDown.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (HasTicks)
        {
            Shell.ClearSelectionCommand.Execute(null);
            return;
        }

        Selected = null;
    }

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
            ? $"Reset the map art in all {count} folders? The files are deleted and Brawlhalla regenerates the defaults on its next launch."
            : $"Reset the map art in all {count} folders to the Default pack?";
        if (!Shell.Dialogs.Confirm("Reset all to default", message))
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

        ResetOutcome? outcome = null;
        await Shell.RunGameWriteAsync(
            "Resetting all",
            undoPaths,
            (progress, ct) => Task.Run(() => { outcome = MapReset.ResetAll(gamePath, defaultPack, progress, ct); }, ct),
            "Reset every map to default");

        if (outcome is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be reset", outcome.Failures);
        }
    }

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
            clearTicks: true,
            pack.Name);

        if (result is not null)
        {
            Shell.Dialogs.ShowFailures("Some files could not be applied", result.Failures);
        }
    }

    /// <summary>Spec 3.3: one picture into every ticked map's own slots, through the shell's shared write, so the
    /// confirm, the undo list and the done line are the same ones a tile menu produces. The last menu row has no
    /// picture: it opens Add Custom Image with the ticked maps as its target (spec 7.1).</summary>
    [RelayCommand]
    private async Task ApplyPictureToTickedAsync(PictureMenuItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.Picture is not { } picture)
        {
            await Shell.OpenAddPicturesAsync(
                new AddPicturesTarget(AddPicturesTargetKind.Ticked, null, Shell.SelectedMaps));
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
        await Shell.ApplyPictureAsync(source, Shell.SelectedMaps, clearTicks: true, picture.DisplayName, picture.PackName);
    }

    /// <summary>Spec 3.3: the ticked maps back to the Default pack, the same reset one map's panel offers.</summary>
    [RelayCommand(CanExecute = nameof(CanResetTicked))]
    private async Task ResetTickedAsync()
    {
        var maps = Shell.SelectedMaps;
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

    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;

        // Every card is replaced, so the previews still in flight are for objects nothing shows any more.
        _previews?.Cancel();
        _previews?.Dispose();
        _previews = new CancellationTokenSource();

        var opened = Selected?.FolderName;
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
            var card = new MapCardViewModel(map, status, snapshot.CustomPictures)
            {
                IsSelected = ticked.Contains(map.FolderName),
            };
            card.PropertyChanged += OnCardChanged;
            _all.Add(card);
        }

        RebuildChips(snapshot.Catalog);
        ApplyFilter();
        OnPropertyChanged(nameof(ShowFirstRunLine));

        // The card the panel was on is a new object now, so it is found again by folder name rather than left
        // pointing at one nothing draws.
        Selected = opened is null
            ? null
            : _all.FirstOrDefault(c => c.FolderName.Equals(opened, StringComparison.OrdinalIgnoreCase));

        _ = LoadPreviewsAsync([.. _all], snapshot.Catalog.HasLevelData, _previews.Token);

        // Spec 3.3: the two menus the selection bar opens, rebuilt from the scan they describe. The Add Custom
        // Image row is last and is always there, so the menu is never empty.
        PictureChoices =
        [
            .. CustomPictures().Select(p => new PictureMenuItem(p.DisplayName, p)),
            new PictureMenuItem("Add Image...", null),
        ];
        OnPropertyChanged(nameof(PackChoices));
        OnPropertyChanged(nameof(PictureChoices));

        // The reset is off until a scan finds a Default pack, and this is the only thing that changes that answer.
        ResetTickedCommand.NotifyCanExecuteChanged();

        // Last, because a map that has gone from the catalog has just left the ticked set.
        Shell.NotifySelectionChanged();
    }

    partial void OnSelectedChipChanged(string value) => ApplyFilter();

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    /// <summary>A card click and the re-selection every scan does both land here, so the panel is built in one
    /// place. The panel it replaces is cancelled: its composites are for a state that is gone.</summary>
    partial void OnSelectedChanged(MapCardViewModel? value)
    {
        Panel?.Cancel();
        if (value is null || _snapshot is not { } snapshot)
        {
            Panel = null;
            return;
        }

        snapshot.MapStatuses.TryGetValue(value.FolderName, out var status);
        var panel = new MapPanelViewModel(Shell, this, value.Map, status, snapshot);
        Panel = panel;

        // Fire and forget: the panel turns its own file failures into fallbacks, so there is nothing to await for.
        _ = panel.LoadAsync();
    }

    partial void OnZoomChanged(int value)
    {
        if (Shell.Services.Settings.MapsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { MapsZoom = value });
        }

        // The card template reads these numbers rather than carrying a pile of triggers of its own.
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowTagRow));
        OnPropertyChanged(nameof(ShowMissingMark));
        OnPropertyChanged(nameof(NameFontSize));
        OnPropertyChanged(nameof(CardPadding));
        OnPropertyChanged(nameof(CardMargin));
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedMaps is raised alongside this one; reacting to the count alone does the work once. The first
        // tick is what puts the Selected chip in the row, and the last untick takes it away again. A10 hangs the
        // selection bar's lines on the same branch.
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            RebuildChips(_snapshot?.Catalog);
            OnPropertyChanged(nameof(SelectionText));
            OnPropertyChanged(nameof(HasTicks));

            // Every open tile menu names the ticked maps, so the count is what rewords them (spec 4).
            Panel?.RebuildMenus(Shell.SelectedMapCount);
        }
    }

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapCardViewModel.IsSelected))
        {
            Shell.NotifySelectionChanged();
        }
    }

    /// <summary>The library's custom pictures, as the last scan built them (spec 4). Empty before the first scan.</summary>
    private IReadOnlyList<CustomPicture> CustomPictures() => _snapshot?.CustomPictures ?? [];

    /// <summary>Fills the cards one at a time. The render queue serialises the composites anyway, and going in
    /// display order means the cards the grid shows first are the ones that fill first. Fire and forget: the card
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
    /// ListBox's selection and push a null back through it. The catalog is nullable because the tick hook can
    /// reach this before the first scan, and there is no row to build then.</summary>
    private void RebuildChips(MapCatalog? catalog)
    {
        if (catalog is null)
        {
            return;
        }

        // Spec 11: All, the set chips, and Selected once anything is ticked.
        List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label)];
        if (Shell.SelectedMapCount > 0)
        {
            wanted.Add(TickedChip);
        }

        if (_chips.SequenceEqual(wanted))
        {
            return;
        }

        _uiSets.Clear();
        _uiSets.AddRange(catalog.UiSets);
        var chosen = SelectedChip;

        // Synced in place rather than cleared and refilled. The first tick is what adds the Selected chip, and a
        // Ctrl+A ticks the whole grid from inside the ListBox's own loop: clearing the row there pushes a null
        // SelectedChip through the two-way binding, and the ApplyFilter answering it empties and refills Cards
        // under that loop, which leaves one card ticked out of the set. Adding one chip at the end disturbs
        // neither the chip ListBox's selection nor Cards.
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
        var ticked = _all.Where(c => c.IsSelected).Select(c => c.FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Cards.Clear();
        foreach (var card in _all.Where(Matches))
        {
            Cards.Add(card);
        }

        // A card the filter just removed loses its container, and a released ListBoxItem clears its own
        // IsSelected, which the two-way binding would write back as an untick. The set is restored by name.
        // Only the cards whose value actually changed are written: this runs on every keystroke, and each write
        // raises PropertyChanged twice and rebuilds the chip row through A7's hook.
        foreach (var card in _all)
        {
            var wanted = ticked.Contains(card.FolderName);
            if (card.IsSelected != wanted)
            {
                card.IsSelected = wanted;
            }
        }

        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowClearSearch));
    }

    /// <summary>The chip filter, then the header search box on top of it.</summary>
    private bool Matches(MapCardViewModel card)
    {
        var search = SearchText;
        if (search.Length > 0 && !card.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
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

}
