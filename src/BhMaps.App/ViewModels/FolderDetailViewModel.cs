using System.Collections.ObjectModel;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class FolderDetailViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public FolderDetailViewModel(MainViewModel main, ScanSnapshot snapshot, string folderName, ThumbnailProvider thumbnails)
    {
        _main = main;
        FolderName = folderName;
        IsBackgrounds = folderName.Equals("Backgrounds", StringComparison.OrdinalIgnoreCase);

        var gameFolder = snapshot.Tree.FindFolder(folderName);
        var gameFiles = gameFolder?.Files ?? Array.Empty<GameFile>();

        // Union of game files and every pack's files for this folder, so reset files still show up (spec 5.2).
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in gameFiles)
        {
            names.Add(file.Name);
        }

        foreach (var pack in snapshot.Packs)
        {
            foreach (var file in pack.FindFolder(folderName)?.Files ?? Array.Empty<GameFile>())
            {
                names.Add(file.Name);
            }
        }

        foreach (var name in names)
        {
            var gameFile = gameFolder?.FindFile(name);
            var packsWithFile = snapshot.Packs
                .Where(p => p.FindFolder(folderName)?.FindFile(name) is not null)
                .Select(p => p.Name)
                .ToList();
            var status = gameFile is null ? null : snapshot.Status.ForFile(folderName, name);
            var row = new FileRowViewModel(main, folderName, name, gameFile, status, packsWithFile);
            Rows.Add(row);
            _ = row.LoadThumbnailAsync(thumbnails);
        }

        Title = $"{folderName}   ({gameFiles.Count} files in game, {Rows.Count - gameFiles.Count} only in packs)";
    }

    public string FolderName { get; }

    public bool IsBackgrounds { get; }

    public string Title { get; }

    public ObservableCollection<FileRowViewModel> Rows { get; } = new();

    [RelayCommand]
    private void Back() => _main.CloseDetail();
}
