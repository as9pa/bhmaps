using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Addendum B: one row per map, and the row is that map's answer to "what can my background be, and
/// which is it now". Spec 4 puts the one question the rows all shared, whether to show the any-map pictures, on
/// one switch at the head of the page rather than on a folded tile in every row.</summary>
public partial class BackgroundsViewModel : RowsPageViewModel
{
    /// <summary>How many any-map pictures a row would show. The same number on every row, so it is counted while
    /// the rows are built rather than worked out a second time for the label.</summary>
    private int _pictureCount;

    public BackgroundsViewModel(MainViewModel shell)
        : base(shell, shell.Services.Settings.BackgroundsTileSize)
    {
        ShowPictures = shell.Services.Settings.BackgroundsShowPictures;
    }

    public override string Title => "Backgrounds";

    public override string SearchPlaceholder => "Search maps and pictures";

    /// <summary>Spec 4: the page's one switch. Off, a row shows its pack pictures only; on, the any-map pictures
    /// follow them on every row. Saved when it is toggled, so the page opens the way it was left.</summary>
    [ObservableProperty]
    public partial bool ShowPictures { get; set; }

    /// <summary>"My Backgrounds (11)": the switch's label and its automation name.</summary>
    public string PicturesLabel => $"My Backgrounds ({_pictureCount})";

    /// <summary>There is nothing to switch on while the library holds no any-map picture, so the switch is not
    /// there at all (spec 4).</summary>
    public bool HasPictures => _pictureCount > 0;

    protected override string NoResultsText => $"No map or picture matches '{SearchText}'.";

    public override void Refresh(ScanSnapshot snapshot)
    {
        _pictureCount = 0;
        base.Refresh(snapshot);
        OnPropertyChanged(nameof(PicturesLabel));
        OnPropertyChanged(nameof(HasPictures));
    }

    /// <summary>Spec 4's one header action. The page adds to the library and names no maps, which is what the
    /// None kind says (spec 7.1).</summary>
    [RelayCommand]
    private Task AddPicturesAsync() =>
        Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.None, null));

    /// <summary>What the switch does. A command rather than a two-way binding, because the switch is a button:
    /// Space and Enter reach it the same way a click does.</summary>
    [RelayCommand]
    private void TogglePictures() => ShowPictures = !ShowPictures;

    protected override void SaveSize(TileSize value)
    {
        if (Shell.Services.Settings.BackgroundsTileSize != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsTileSize = value });
        }
    }

    /// <summary>Addendum B's order, with spec 4's switch on the end: the in-game choice first with the check, then
    /// each pack that has a picture for this map's slot in the Packs page order, then, while the switch is on,
    /// every any-map picture. An any-map picture the game is showing is first whatever the switch says, because it
    /// is the in-game choice.</summary>
    protected override MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot)
    {
        var map = card.Map;
        var packs = MapChoices.PackBackgrounds(Shell, map, status, snapshot);

        // The whole list, so the picture the game is showing on this row is found whatever pack it came from.
        // Only the user's own imports are counted and offered under the switch, though: a picture only the game
        // has is already the in-game tile on the row that shows it (2.2.1).
        var customs = MapChoices.CustomBackgrounds(Shell, map, snapshot);
        List<CustomPictureTileViewModel> imports = [.. customs.Where(t => t.PackName is not null)];
        _pictureCount = Math.Max(_pictureCount, imports.Count);

        List<PictureTileViewModel> pictures = [.. packs, .. customs];
        List<object> alwaysShown = [.. pictures.Where(t => t.IsInGame), .. packs.Where(t => !t.IsInGame)];
        List<object> extras = [.. imports.Where(t => !t.IsInGame)];

        return new MapRowViewModel(
            map,
            card.TagText,
            card.IsMissing,
            Haystack(map, pictures),
            alwaysShown,
            extras,
            pictures,
            [],
            composeSet: null)
        {
            ShowExtras = ShowPictures,
        };
    }

    /// <summary>Saved, then written to every row that exists: the rows are told one by one rather than built
    /// again, so the list the page is scrolled through is the same list afterwards and the offset is kept.</summary>
    partial void OnShowPicturesChanged(bool value)
    {
        if (Shell.Services.Settings.BackgroundsShowPictures != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsShowPictures = value });
        }

        foreach (var row in AllRows)
        {
            row.ShowExtras = value;
        }
    }

    /// <summary>Addendum B: the map's name, then every thumbnail's caption and file name, one per line, so a
    /// search for a pack name or a file name keeps the rows that offer it.</summary>
    private static string Haystack(MapEntry map, IReadOnlyList<PictureTileViewModel> pictures) =>
        string.Join(
            '\n',
            [map.DisplayName, .. pictures.Select(t => t.Title), .. pictures.Select(t => Path.GetFileName(t.FullPath))]);
}
