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
