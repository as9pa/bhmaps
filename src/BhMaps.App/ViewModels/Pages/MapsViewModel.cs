using System.Collections.ObjectModel;
using System.ComponentModel;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 3.1: the chip row with the zoom slider at its right end, and the grid of composed map cards.
/// No summary bar and no composition bars; the header carries the search box and Reset all to default.</summary>
public partial class MapsViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinZoom;
    public const int MaxZoom = AppSettings.MaxZoom;

    private const string AllChip = "All";
    private const string ChangedChip = "Changed";

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
        _chips.Add(ChangedChip);

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

    /// <summary>"All", the UI set labels, then "Changed". Without level data the set chips are gone entirely and
    /// only the two remain (spec 3.6). An ObservableCollection behind the read-only surface, because the row
    /// changes when the level data does.</summary>
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

    /// <summary>The no-results state's way back (spec 7.8): the All chip and an empty search box.</summary>
    [RelayCommand]
    private void ShowAll()
    {
        SearchText = "";
        SelectedChip = AllChip;
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
            var card = new MapCardViewModel(map, status) { IsSelected = ticked.Contains(map.FolderName) };
            card.PropertyChanged += OnCardChanged;
            _all.Add(card);
        }

        RebuildChips(snapshot.Catalog);
        ApplyFilter();

        // The card the panel was on is a new object now, so it is found again by folder name rather than left
        // pointing at one nothing draws.
        Selected = opened is null
            ? null
            : _all.FirstOrDefault(c => c.FolderName.Equals(opened, StringComparison.OrdinalIgnoreCase));

        _ = LoadPreviewsAsync([.. _all], snapshot.Catalog.HasLevelData, _previews.Token);

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
        var panel = new MapPanelViewModel(Shell, value.Map, status, snapshot);
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
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        // SelectedMaps is raised alongside this one; reacting to the count alone does the work once. The branch
        // is deliberately empty here: A7 hangs the chip row rebuild on it and A10 the selection bar's lines.
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
        }
    }

    private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapCardViewModel.IsSelected))
        {
            Shell.NotifySelectionChanged();
        }
    }

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
    /// ListBox's selection and push a null back through it.</summary>
    private void RebuildChips(MapCatalog catalog)
    {
        List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label), ChangedChip];
        if (_chips.SequenceEqual(wanted))
        {
            return;
        }

        _uiSets.Clear();
        _uiSets.AddRange(catalog.UiSets);
        var chosen = SelectedChip;
        _chips.Clear();
        foreach (var chip in wanted)
        {
            _chips.Add(chip);
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
            ChangedChip => IsChanged(card),
            _ => _uiSets.FirstOrDefault(s => s.Label == SelectedChip) is { } set
                && card.Map.Sets.Contains(set.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Anything the scan did not summarise as Default: a pack, a custom file, or a missing one.</summary>
    private bool IsChanged(MapCardViewModel card) =>
        _snapshot is { } snapshot
        && snapshot.MapStatuses.TryGetValue(card.FolderName, out var status)
        && status.State != MapState.Default;
}
