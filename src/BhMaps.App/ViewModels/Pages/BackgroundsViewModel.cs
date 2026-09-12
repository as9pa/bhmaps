using BhMaps.App.Services;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Addendum B: one row per map, and the row is that map's answer to "what can my background be, and
/// which is it now". The pack sections and the custom shelf of spec 4 are gone: a pack's contents are the Packs
/// page's business.</summary>
public partial class BackgroundsViewModel : RowsPageViewModel
{
    public BackgroundsViewModel(MainViewModel shell)
        : base(shell, shell.Services.Settings.BackgroundsZoom)
    {
    }

    public override string Title => "Backgrounds";

    public override string SearchPlaceholder => "Search maps and pictures";

    protected override string NoResultsText => $"No map or picture matches '{SearchText}'.";

    /// <summary>Spec 4's one header action. The page adds to the library and names no maps, which is what the
    /// None kind says (spec 7.1).</summary>
    [RelayCommand]
    private Task AddPicturesAsync() =>
        Shell.OpenAddPicturesAsync(new AddPicturesTarget(AddPicturesTargetKind.None, null, null));

    protected override void SaveZoom(int value)
    {
        if (Shell.Services.Settings.BackgroundsZoom != value)
        {
            Shell.Services.UpdateSettings(Shell.Services.Settings with { BackgroundsZoom = value });
        }
    }

    /// <summary>Addendum B's order: the in-game choice first with the check, then Default, then each pack that
    /// has a picture for this map's slot in the Packs page order, then the any-map pictures, which are folded
    /// behind one "My Backgrounds (N)" tile (q3). An any-map picture the game is showing is unfolded and first,
    /// because it is the in-game choice.</summary>
    protected override MapRowViewModel BuildRow(MapCardViewModel card, MapStatus? status, ScanSnapshot snapshot)
    {
        var map = card.Map;
        var packs = MapChoices.PackBackgrounds(Shell, map, status, snapshot);
        var customs = MapChoices.CustomBackgrounds(Shell, map, snapshot);

        List<PictureTileViewModel> pictures = [.. packs, .. customs];
        List<object> alwaysShown = [.. pictures.Where(t => t.IsInGame), .. packs.Where(t => !t.IsInGame)];
        List<object> foldedAway = [.. customs.Where(t => !t.IsInGame)];
        var foldedLabel = foldedAway.Count == 0 ? "" : $"My Backgrounds ({foldedAway.Count})";

        return new MapRowViewModel(
            map,
            card.TagText,
            card.IsMissing,
            Haystack(map, pictures),
            alwaysShown,
            foldedAway,
            foldedLabel,
            pictures,
            [],
            composeSet: null);
    }

    /// <summary>Addendum B: the map's name, then every thumbnail's caption and file name, one per line, so a
    /// search for a pack name or a file name keeps the rows that offer it.</summary>
    private static string Haystack(MapEntry map, IReadOnlyList<PictureTileViewModel> pictures) =>
        string.Join(
            '\n',
            [map.DisplayName, .. pictures.Select(t => t.Title), .. pictures.Select(t => Path.GetFileName(t.FullPath))]);
}
