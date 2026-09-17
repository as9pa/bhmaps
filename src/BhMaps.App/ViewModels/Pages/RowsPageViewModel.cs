using System.Collections.ObjectModel;
using System.ComponentModel;
using BhMaps.App.Services;
using BhMaps.Core.Layout;
using BhMaps.Core.Maps;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>What the Backgrounds and Platforms pages have in common (addendum B and C): one row per map, a chip
/// row, a search box, a size that sets the thumbnails, and the rule that a row reads its pictures when it comes
/// on screen. No selection of its own: selecting a map is the Maps page's (q4), and a tile menu here acts on the
/// row it belongs to.</summary>
public abstract partial class RowsPageViewModel : PageViewModel, ITileSized
{
    protected const string AllChip = "All";

    /// <summary>Every row the last scan produced. Rows is this list under the chip and the search.</summary>
    private readonly List<MapRowViewModel> _all = [];

    private readonly ObservableCollection<string> _chips = [];

    /// <summary>The sets behind the set chips, so a chip label maps back to the set name a map is measured against.</summary>
    private readonly List<UiSet> _uiSets = [];

    private CancellationTokenSource? _loads;

    protected RowsPageViewModel(MainViewModel shell, TileSize storedSize)
        : base(shell)
    {
        Rows = [];
        _chips.Add(AllChip);

        // After the collections, because setting them runs the change hooks that filter them.
        SearchText = "";
        SelectedChip = AllChip;

        // Handed in rather than read here, so the constructor reaches for nothing the subclass owns.
        TileSize = storedSize;
    }

    /// <summary>The last scan, or null before the first one.</summary>
    protected ScanSnapshot? Snapshot { get; private set; }

    /// <summary>Every row the last scan produced, filtered or not, for a page that has something to tell all of
    /// them (spec 4's switch writes ShowExtras on each rather than building the list again).</summary>
    protected IReadOnlyList<MapRowViewModel> AllRows => _all;

    /// <summary>The rows the chip and the search leave visible, in map order.</summary>
    public ObservableCollection<MapRowViewModel> Rows { get; }

    /// <summary>"All" and the UI set labels. Without level data the set chips are gone entirely (spec 3.6).</summary>
    public IReadOnlyList<string> Chips => _chips;

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial string SelectedChip { get; set; }

    /// <summary>How big the row's thumbnails are drawn, persisted by the page (wireframe 8.2). A rows page
    /// picks a tile width like a grid page does, and the row height follows it, so one size means the same
    /// thing on every page.</summary>
    [ObservableProperty]
    public partial TileSize TileSize { get; set; }

    /// <summary>224, 150 or 96 px.</summary>
    public double ThumbWidth => TileSizes.RowTileWidth(TileSize);

    /// <summary>16:9, rounded to a whole pixel so a strip of them measures the same on every row.</summary>
    public double ThumbHeight => Math.Round(ThumbWidth * 9 / 16);

    /// <summary>False at Small, where a thumbnail is 96 px wide and the hover row has room for the dots button
    /// alone. A click on the tile still applies, so nothing is lost but the label.</summary>
    public bool ShowTileApply => TileSize != TileSize.Small;

    /// <summary>The words in the search box while it is empty, and its automation name.</summary>
    public abstract string SearchPlaceholder { get; }

    public bool ShowClearSearch => SearchText.Length > 0;

    /// <summary>The empty state's one action, which only a search gives it: with nothing typed there is nothing
    /// to undo and the state is just the sentence.</summary>
    public string EmptyActionText => ShowClearSearch ? "Clear search" : "";

    /// <summary>2.8: how many packs the Packs page is hiding from these rows. Written by <see cref="Refresh" />,
    /// because a list quietly missing a pack reads as a bug.</summary>
    public int HiddenPackCount { get; private set; }

    /// <summary>"1 pack hidden", "2 packs hidden".</summary>
    public string HiddenPacksText => $"{MainViewModel.Count(HiddenPackCount, "pack")} hidden";

    public bool ShowHiddenPacks => HiddenPackCount > 0;

    /// <summary>Spec 3.1's first-run line, on this page too: nothing in the library but the Default pack, and no
    /// any-map picture either.</summary>
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
                null or "" or AllChip => "No map to show.",
                _ => $"No {SelectedChip.ToLowerInvariant()} map to show.",
            };
        }
    }

    /// <summary>The line the search's own empty state uses, which quotes what was typed.</summary>
    protected abstract string NoResultsText { get; }

    /// <summary>Writes the page's own tile size key. Called only from the change hook, never from the
    /// constructor.</summary>
    protected abstract void SaveSize(TileSize value);

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

        // The Default pack is never hidden, whatever the setting holds.
        HiddenPackCount = snapshot.Packs.Count(p =>
            !p.Name.Equals(DefaultPack.Name, StringComparison.OrdinalIgnoreCase)
            && Shell.Services.Settings.IsHidden(p.Name));
        OnPropertyChanged(nameof(HiddenPackCount));
        OnPropertyChanged(nameof(HiddenPacksText));
        OnPropertyChanged(nameof(ShowHiddenPacks));
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

    /// <summary>The header note's way to the page where the hiding was done (2.8). The shell's own command, so
    /// there is one route to a page.</summary>
    [RelayCommand]
    private void GoToPacks() => Shell.NavigatePacksCommand.Execute(null);

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowClearSearch));
        OnPropertyChanged(nameof(EmptyActionText));
        ApplyFilter();
    }

    partial void OnSelectedChipChanged(string value) => ApplyFilter();

    partial void OnTileSizeChanged(TileSize value)
    {
        SaveSize(value);

        // The row template reads these numbers rather than carrying triggers of its own.
        OnPropertyChanged(nameof(ThumbWidth));
        OnPropertyChanged(nameof(ThumbHeight));
        OnPropertyChanged(nameof(ShowTileApply));
    }

    private void RebuildMenus()
    {
        foreach (var row in _all)
        {
            row.RebuildMenus();
        }
    }

    /// <summary>Synced in place rather than cleared and refilled: clearing the chip ListBox's items pushes a null
    /// SelectedChip back through the two-way binding, and the filter answering it runs against no chip at all.</summary>
    private void RebuildChips(MapCatalog catalog)
    {
        List<string> wanted = [AllChip, .. catalog.UiSets.Select(s => s.Label)];
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
            null or "" => true,
            _ => _uiSets.FirstOrDefault(s => s.Label == SelectedChip) is { } set
                && row.Map.Sets.Contains(set.Name, StringComparer.OrdinalIgnoreCase),
        };
    }
}
