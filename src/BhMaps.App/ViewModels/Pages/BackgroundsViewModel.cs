using System.Collections.ObjectModel;
using System.ComponentModel;
using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 4: the library of pictures, picture-first. The custom pictures are the first section, one tile
/// per picture however many copies of it the library holds; the pack sections and the flat search grid follow.
/// The ticked maps are the shell's and are reached through a tile's own menu, so the page has no bottom bar.</summary>
public partial class BackgroundsViewModel : PageViewModel
{
    public const int MinZoom = AppSettings.MinZoom;
    public const int MaxZoom = AppSettings.MaxZoom;

    private ScanSnapshot? _snapshot;
    private CancellationTokenSource? _thumbnails;

    public BackgroundsViewModel(MainViewModel shell)
        : base(shell)
    {
        CustomTiles = [];

        // Before the first ApplySearch, because setting it runs the change hook.
        SearchText = "";

        // A stored zoom from another version, or a hand-edited one, is clamped rather than trusted.
        Zoom = Math.Clamp(shell.Services.Settings.BackgroundsZoom, MinZoom, MaxZoom);

        // A tile's menu names the ticked count, which is the shell's. The page lives as long as the shell, so
        // there is nothing to unsubscribe from.
        shell.PropertyChanged += OnShellChanged;
    }

    public override string Title => "Backgrounds";

    /// <summary>Spec 4's first section: one tile per picture, however many copies of it the library holds.</summary>
    public ObservableCollection<CustomPictureTileViewModel> CustomTiles { get; }

    /// <summary>This page's own box (spec 4): file names, pack names and map names, not the Maps page's string.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; }

    /// <summary>Columns in the grid, MinZoom to MaxZoom, persisted as backgroundsZoom.</summary>
    [ObservableProperty]
    public partial int Zoom { get; set; }

    /// <summary>The first section's header, which carries its own count (spec 4).</summary>
    public string CustomHeader => $"Custom pictures ({CustomTiles.Count})";

    /// <summary>False while the library holds no custom picture, which is the state that says so and offers the
    /// way in beside the line (spec 4).</summary>
    public bool HasCustomPictures => CustomTiles.Count > 0;

    /// <summary>True while the box has something in it, which is when the sections give way to one flat grid of
    /// what matches (spec 4).</summary>
    public bool IsSearching => SearchText.Length > 0;

    /// <summary>Spec 4's no-results line, which quotes what was typed.</summary>
    public string NoResultsText => $"No picture matches '{SearchText}'.";

    public override void Refresh(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;

        // Every tile is replaced, so the thumbnails still in flight are for objects nothing shows any more.
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
            // A slot resolves through AssetPath, because one borrowed from a theme folder through "../" is not
            // under Backgrounds at all; the picture's own list is of game file names.
            var maps = snapshot.Catalog.Maps
                .Count(m => m.BackgroundSlots.Any(s =>
                    picture.InGameSlots.Contains(
                        Path.GetFileName(AssetPath.Background(s)), StringComparer.OrdinalIgnoreCase)));
            return maps == 1 ? "in game on 1 map" : $"in game on {maps} maps";
        }

        return picture.PackName ?? "In game";
    }

    /// <summary>Spec 4's one header action, and the empty state's button. The page adds to the library and names
    /// no maps, which is what the None kind says (spec 7.1).</summary>
    [RelayCommand]
    private Task AddPicturesAsync() =>
        Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.None, null, null));

    /// <summary>The no-results state's way back (spec 4).</summary>
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
        // SelectedMaps is raised alongside this one; reacting to the count alone does the work once.
        if (e.PropertyName == nameof(MainViewModel.SelectedMapCount))
        {
            RebuildMenus();
        }
    }

    /// <summary>The ticked row is the one line of a tile's menu that changes without a scan, so every tile is
    /// asked to rebuild whenever the count moves.</summary>
    private void RebuildMenus()
    {
        foreach (var tile in CustomTiles)
        {
            tile.RebuildMenu(Shell.SelectedMapCount);
        }
    }

    /// <summary>Fills the tiles one at a time, in grid order, so the ones on screen fill first. Fire and forget:
    /// the tile turns its own file failures into a blank picture, so the only thing left to stop for is
    /// cancellation.</summary>
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

    /// <summary>What the search box does to the page. Empty while the custom section is the only one there is:
    /// the sections it collapses into one flat grid of matches arrive with the pack sections.</summary>
    private void ApplySearch()
    {
    }
}
