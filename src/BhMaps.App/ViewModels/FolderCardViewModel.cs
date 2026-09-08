using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

public partial class FolderCardViewModel : ObservableObject
{
    public FolderCardViewModel(GameFolder folder, FolderStatus status)
    {
        Folder = folder;
        Name = folder.Name;
        State = status.State;
        StatusText = status.Text;
    }

    public GameFolder Folder { get; }

    public string Name { get; }

    public FolderState State { get; }

    public string StatusText { get; }

    public int FileCount => Folder.Files.Count;

    public bool IsBackgrounds => Name.Equals("Backgrounds", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

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
