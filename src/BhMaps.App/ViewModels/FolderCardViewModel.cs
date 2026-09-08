using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class FolderCardViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public FolderCardViewModel(MainViewModel main, GameFolder folder, FolderStatus status, IReadOnlyList<Pack> packs)
    {
        _main = main;
        Folder = folder;
        Name = folder.Name;
        State = status.State;
        StatusText = status.Text;
        ApplyFromOptions = packs
            .Where(p => p.FindFolder(folder.Name) is { Files.Count: > 0 })
            .Select(p => new ApplyFromOption(
                p.Name,
                new AsyncRelayCommand(() => main.ApplyFolderFromPackAsync(folder.Name, p.Name))))
            .ToList();
    }

    public GameFolder Folder { get; }

    public string Name { get; }

    public FolderState State { get; }

    public string StatusText { get; }

    public int FileCount => Folder.Files.Count;

    public bool IsBackgrounds => Name.Equals("Backgrounds", StringComparison.OrdinalIgnoreCase);

    /// <summary>Only packs that have at least one file for this folder (spec 5.1).</summary>
    public IReadOnlyList<ApplyFromOption> ApplyFromOptions { get; }

    public bool HasApplyOptions => ApplyFromOptions.Count > 0;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [RelayCommand]
    private Task ResetAsync() => _main.ResetFolderAsync(Name);

    /// <summary>Backgrounds shows the fixed tile; every other folder shows its largest file, decoded off the UI thread.</summary>
    public async Task LoadThumbnailAsync(ThumbnailProvider thumbnails, ImageSource? backgroundsTile)
    {
        if (IsBackgrounds)
        {
            Thumbnail = backgroundsTile;
            return;
        }

        var pick = ThumbnailProvider.PickRepresentative(Folder);
        if (pick is null)
        {
            return;
        }

        Thumbnail = await thumbnails.GetAsync(pick.FullPath, pick.MtimeTicks);
    }
}
