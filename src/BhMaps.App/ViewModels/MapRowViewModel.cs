using System.Collections.ObjectModel;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the Backgrounds or Platforms page (addendum B and C): one map, the tag that says what it
/// is showing, and every choice that map has on that page. No tick box: a row is for clicking the choice you
/// want, and the ticked set the tile menus write to is made on Maps (q4).</summary>
public sealed partial class MapRowViewModel : ObservableObject
{
    private readonly IReadOnlyList<object> _alwaysShown;
    private readonly IReadOnlyList<object> _extras;
    private readonly Func<PlatformSetTileViewModel, CancellationToken, Task>? _composeSet;

    /// <summary>True once the row has been on screen and asked for its pictures, so scrolling back to it costs
    /// nothing (addendum B, Performance).</summary>
    private bool _loadStarted;

    public MapRowViewModel(
        MapEntry map,
        string tagText,
        bool isMissing,
        string haystack,
        IReadOnlyList<object> alwaysShown,
        IReadOnlyList<object> extras,
        IReadOnlyList<PictureTileViewModel> pictures,
        IReadOnlyList<PlatformSetTileViewModel> sets,
        Func<PlatformSetTileViewModel, CancellationToken, Task>? composeSet)
    {
        Map = map;
        TagText = tagText;
        IsMissing = isMissing;
        Haystack = haystack;
        _alwaysShown = alwaysShown;
        _extras = extras;
        Pictures = pictures;
        Sets = sets;
        _composeSet = composeSet;
        Choices = [];
        Rebuild();
    }

    public MapEntry Map { get; }

    public string FolderName => Map.FolderName;

    public string DisplayName => Map.DisplayName;

    /// <summary>The pack name, the any-map picture's name, "Missing", or empty for Default. The page decides it:
    /// Backgrounds takes the Maps card's own tag, Platforms measures the map's folder instead.</summary>
    public string TagText { get; }

    public bool IsMissing { get; }

    public bool ShowTag => TagText.Length > 0;

    /// <summary>Everything the search box matches against: the map's name, then every choice's caption and file
    /// name, one per line (addendum B).</summary>
    public string Haystack { get; }

    /// <summary>Every picture tile in the row, folded or not, so one load reaches all of them.</summary>
    public IReadOnlyList<PictureTileViewModel> Pictures { get; }

    public IReadOnlyList<PlatformSetTileViewModel> Sets { get; }

    /// <summary>What the strip draws right now: the row's own choices, and the any-map pictures after them while
    /// the page's switch is on. Typed as object because a Backgrounds strip holds two tile types; the view picks
    /// a template by type, which is what an implicit DataTemplate in the page's resources is for.</summary>
    public ObservableCollection<object> Choices { get; }

    /// <summary>Whether the row is showing every choice it has, on as many lines as that needs (addendum B, q2).
    /// Written by the row's own "+N" tile and by the switch below; the page folds every row when it is left.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMore))]
    public partial bool IsUnfolded { get; set; }

    /// <summary>Spec 4: whether the any-map pictures follow the row's own choices. One page switch writes it on
    /// every row at once, so a row never asks the question for itself.</summary>
    [ObservableProperty]
    public partial bool ShowExtras { get; set; }

    /// <summary>How many choices the strip could not fit on one line. Written by StripPanel through a
    /// OneWayToSource binding, because the panel is the only thing that measured them (addendum B, q2).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText), nameof(ShowMore))]
    public partial int Overflow { get; set; }

    /// <summary>"+7". Empty at zero, which is also when the tile is hidden.</summary>
    public string MoreText => Overflow > 0 ? $"+{Overflow}" : "";

    public bool ShowMore => Overflow > 0 && !IsUnfolded;

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

    /// <summary>Puts the row back to one line (addendum B: unfolded rows fold back when the page is left). A row
    /// showing the any-map pictures stays on as many lines as they need: the switch, not the row, folds those.</summary>
    public void Fold()
    {
        if (!ShowExtras)
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

    /// <summary>What the row's own "+N" tile does: the row shows every choice it has, on as many lines as that
    /// needs (addendum B, q2).</summary>
    [RelayCommand]
    private void Unfold() => IsUnfolded = true;

    /// <summary>The pictures wrap the row onto further lines, so the strip unfolds with them and folds back with
    /// them rather than leaving the row a "+N" tile for choices the switch has just added.</summary>
    partial void OnShowExtrasChanged(bool value)
    {
        IsUnfolded = value;
        Rebuild();
    }

    private void Rebuild()
    {
        Choices.Clear();
        foreach (var choice in _alwaysShown)
        {
            Choices.Add(choice);
        }

        if (!ShowExtras)
        {
            return;
        }

        foreach (var choice in _extras)
        {
            Choices.Add(choice);
        }
    }
}
