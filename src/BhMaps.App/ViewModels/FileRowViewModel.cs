using System.Windows.Media;
using BhMaps.Core.Imaging;
using BhMaps.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One row in the folder detail view: a game file, or a file that exists only in packs (IsMissing).</summary>
public partial class FileRowViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly string _folderName;
    private readonly GameFile? _gameFile;

    public FileRowViewModel(
        MainViewModel main,
        string folderName,
        string fileName,
        GameFile? gameFile,
        FileStatus? status,
        IReadOnlyList<string> packsWithFile)
    {
        _main = main;
        _folderName = folderName;
        _gameFile = gameFile;
        FileName = fileName;
        IsMissing = gameFile is null;
        SizeText = gameFile is null ? "" : $"{gameFile.Size / 1024.0:0.#} KB";
        StatusText = gameFile is null ? "Missing, launch game or apply" : status?.Text ?? "Default or unmanaged";
        ApplyFromOptions = packsWithFile
            .Select(pack => new ApplyFromOption(
                pack,
                new AsyncRelayCommand(() => main.ApplyFileFromPackAsync(folderName, fileName, pack))))
            .ToList();
    }

    public string FileName { get; }

    public string SizeText { get; }

    public bool IsMissing { get; }

    public string StatusText { get; }

    /// <summary>Packs that have this exact file (spec 5.2).</summary>
    public IReadOnlyList<ApplyFromOption> ApplyFromOptions { get; }

    public bool HasApplyOptions => ApplyFromOptions.Count > 0;

    public bool CanReset => !IsMissing;

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private Task ResetAsync() => _main.ResetFileAsync(_folderName, FileName);

    public async Task LoadThumbnailAsync(ThumbnailProvider thumbnails)
    {
        if (_gameFile is null)
        {
            return;
        }

        Thumbnail = await thumbnails.GetAsync(_gameFile.FullPath, _gameFile.MtimeTicks);
    }
}
