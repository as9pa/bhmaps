using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What one Save left in the library: the fitted picture, the slot it was fitted for, the maps that slot
/// belongs to, whether the user asked for it to go into the game as well, the pack it was saved into, which is the
/// pack the shell stamps when it applies it, and whether it was saved for all maps, which makes the apply an apply
/// to every map rather than to one slot. The shell does that part, so the editor never writes into the game
/// folder.</summary>
public sealed record BackgroundSave(
    string PackFile,
    string Slot,
    string MapNames,
    bool ApplyToGame,
    string PackName,
    bool AllMaps);

/// <summary>Spec 7.2: one picture, fitted to one map's background slot. The source is decoded once into a
/// 640x360 working bitmap and every preview is drawn from it on a 16 ms throttle, so a slider drag moves the
/// picture rather than queueing decodes.</summary>
public partial class BackgroundEditorViewModel : ObservableObject
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 360;
    public const string NewPackChoice = "New pack...";
    public const string DefaultPackName = "My Backgrounds";
    public const string NoSourceText = "No picture yet. Drop one here or browse.";
    public const string NoMapsText = "No maps yet. Refresh the game data in Settings.";
    public const string PreviewSizeText = "preview 640 x 360";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly BackgroundEditorRequest _request;
    private readonly Throttler _preview = new();

    private BitmapSource? _working;
    private string _workingPath = "";

    public BackgroundEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<MapSlotChoice> maps,
        IReadOnlyList<string> packNames,
        BackgroundEditorRequest request)
    {
        _services = services;
        _dialogs = dialogs;
        _request = request;
        Maps = maps;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        // No slot means the source belongs to no map, so All maps is the row that keeps it that way (spec 5).
        SelectedMap = request.Slot is null
            ? maps.FirstOrDefault(m => m.IsAllMaps) ?? maps.FirstOrDefault()
            : maps.FirstOrDefault(m => m.Slot.Equals(request.Slot, StringComparison.OrdinalIgnoreCase))
                ?? maps.FirstOrDefault();
        Error = "";
        SourceDetail = "";
        PanX = 0.5;
        PanY = 0.5;
        ApplyNow = true;

        // The tile's own pack, so Save replaces the picture the user was looking at; anything else keeps it.
        var requested = packNames.FirstOrDefault(p => p.Equals(request.PackName, StringComparison.OrdinalIgnoreCase));
        var existingDefault = packNames.FirstOrDefault(p => p.Equals(DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = requested ?? existingDefault ?? NewPackChoice;
        NewPackName = TargetPack == NewPackChoice ? DefaultPackName : "";

        // Last: the change hook reads everything above it.
        SourcePath = request.SourcePath;
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MapSlotChoice> Maps { get; }

    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public BackgroundSave? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint), nameof(MapLabel), nameof(ApplyNowText))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial MapSlotChoice? SelectedMap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasSource), nameof(Title), nameof(SourceFileName), nameof(EmptyText), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial ImageSource? SourceThumbnail { get; set; }

    /// <summary>"flowermap, 1920 x 1080" under the source's file name, or "" while it is unknown.</summary>
    [ObservableProperty]
    public partial string SourceDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFill), nameof(ModeFill), nameof(ModeFit), nameof(ModeStretch))]
    public partial FitMode Mode { get; set; }

    [ObservableProperty]
    public partial double PanX { get; set; }

    [ObservableProperty]
    public partial double PanY { get; set; }

    [ObservableProperty]
    public partial double DarkenPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyNow { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    public string Title => SourceFileName.Length == 0 ? "Edit background" : $"Edit {SourceFileName}";

    public string SourceFileName => SourcePath.Length == 0 ? "" : Path.GetFileName(SourcePath);

    public bool HasSource => File.Exists(SourcePath);

    /// <summary>"All maps" is always there, so the question is whether any map is (spec 5).</summary>
    public bool HasMaps => Maps.Any(m => !m.IsAllMaps);

    /// <summary>The Map row's own note: empty while there are maps to pick from (decision C-D8).</summary>
    public string MapLabel => HasMaps ? "" : NoMapsText;

    /// <summary>What the preview says instead of a picture: nothing chosen yet, or a source that has gone.</summary>
    public string EmptyText => SourcePath.Length == 0
        ? NoSourceText
        : HasSource ? "" : $"{SourceFileName} is no longer in {_request.PackName ?? "the library"}. Choose another source.";

    public bool IsFill => Mode == FitMode.Cover;

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public string Slot => SelectedMap?.Slot ?? "";

    /// <summary>Spec 5: "Apply to all maps now" under All maps, because the tick then writes every map's slot.</summary>
    public string ApplyNowText => SelectedMap is { IsAllMaps: true } ? "Apply to all maps now" : "Apply to game now";

    /// <summary>Spec 7.2, shown only when Save would replace a file that is already in the pack.</summary>
    public string OverwriteHint =>
        PackFileName() is { Length: > 0 } name && !IsNewPack && File.Exists(PackFilePath())
            ? $"Replaces {name} in {TargetPack}. Pick another pack to keep the original."
            : "";

    public bool ModeFill
    {
        get => Mode == FitMode.Cover;
        set { if (value) { Mode = FitMode.Cover; } }
    }

    public bool ModeFit
    {
        get => Mode == FitMode.Contain;
        set { if (value) { Mode = FitMode.Contain; } }
    }

    public bool ModeStretch
    {
        get => Mode == FitMode.Stretch;
        set { if (value) { Mode = FitMode.Stretch; } }
    }

    public bool CanSave => HasSource && SelectedMap is not null && PackNameValidator.IsValid(EffectivePackName, out _);

    private FitOptions Options => new(Mode, PanX, PanY, DarkenPercent / 100.0);

    public void AcceptDroppedFile(string path) => SourcePath = path;

    /// <summary>The render a drag ends with: the throttle can only have dropped values that are stale by now, so
    /// the newest ones are drawn with nothing queued behind them (spec 7.2).</summary>
    public void RenderFinal()
    {
        _preview.Cancel();
        SchedulePreview();
    }

    partial void OnSourcePathChanged(string value)
    {
        _working = null;
        _workingPath = "";
        LoadSourceInfo(value);
        SchedulePreview();
    }

    partial void OnModeChanged(FitMode value) => SchedulePreview();

    partial void OnPanXChanged(double value) => SchedulePreview();

    partial void OnPanYChanged(double value) => SchedulePreview();

    partial void OnDarkenPercentChanged(double value) => SchedulePreview();

    [RelayCommand]
    private void Replace()
    {
        if (_dialogs.PickImageFile("Choose a source image") is { } file)
        {
            SourcePath = file;
        }
    }

    [RelayCommand]
    private void ResetPanX() => PanX = 0.5;

    [RelayCommand]
    private void ResetPanY() => PanY = 0.5;

    [RelayCommand]
    private void ResetDarken() => DarkenPercent = 0;

    /// <summary>Saves the fitted picture into the pack and nothing else; the "Apply to game now" box is the
    /// shell's business, because a game write needs the boundary, the snapshot and the undo (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packFile = PackFilePath();
        var slot = Slot;
        var allMaps = SelectedMap is { IsAllMaps: true };
        if (File.Exists(packFile)
            && !_dialogs.Confirm(
                "Replace background?",
                $"{Path.GetFileNameWithoutExtension(packFile)} already exists in {EffectivePackName}. Replace it?"))
        {
            return;
        }

        var path = SourcePath;
        var options = Options;
        try
        {
            // The original, not the working bitmap: Save fits at 2048x1151 (spec 7.2).
            var bytes = await Task.Run(() => BackgroundFitter.Fit(path, options));
            Directory.CreateDirectory(Path.GetDirectoryName(packFile)!);
            await File.WriteAllBytesAsync(packFile, bytes);
            Saved = new BackgroundSave(
                packFile, slot, SelectedMap?.DisplayNames ?? slot, ApplyNow, EffectivePackName, allMaps);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the background", ex.Message);
        }
    }

    private string PackFilePath() =>
        Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName, BackgroundsFolder, PackFileName());

    /// <summary>The name Save writes under: the map's slot, or under All maps the source's own name, which is what
    /// makes the file an any-map picture rather than one map's (spec 5). A source already named like a slot gets
    /// " all maps" so the save cannot become that map's picture by accident.</summary>
    private string PackFileName()
    {
        if (SelectedMap is not { IsAllMaps: true })
        {
            return Slot;
        }

        var name = Path.GetFileNameWithoutExtension(SourcePath);
        var isSlotName = Maps.Any(
            m => !m.IsAllMaps
                && Path.GetFileNameWithoutExtension(m.Slot).Equals(name, StringComparison.OrdinalIgnoreCase));
        return isSlotName ? $"{name} all maps.jpg" : $"{name}.jpg";
    }

    /// <summary>The Source row: a thumbnail, and the pack and pixel size under the file name. Both are file work,
    /// so both arrive late and neither blocks the preview.</summary>
    private async void LoadSourceInfo(string path)
    {
        SourceThumbnail = null;
        SourceDetail = "";
        if (!File.Exists(path))
        {
            return;
        }

        var pack = PackOfPath(path);
        BitmapSource? thumbnail;
        (int Width, int Height)? size;

        // async void: nothing above this frame can catch, so a file that goes away between the drop and the read
        // has to land in the Error line rather than on the dispatcher. Both callees are total today; this guard is
        // for the awaits themselves and for the day one of them stops being.
        try
        {
            thumbnail = await Task.Run(() => ThumbnailProvider.Decode(path));
            size = await Task.Run(() => ImageDimensions.Read(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Error = ex.Message;
            return;
        }

        if (!string.Equals(path, SourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SourceThumbnail = thumbnail;
        SourceDetail = size is { } s ? $"{pack}, {s.Width} x {s.Height}" : pack;
    }

    /// <summary>The pack a path sits in, when it sits under the library's packs root.</summary>
    private string PackOfPath(string path)
    {
        var root = PackScanner.PacksRoot(_services.LibraryPath);
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return _request.PackName ?? "Not in a pack";
        }

        var rest = full[(Path.GetFullPath(root).Length + 1)..];
        var cut = rest.IndexOf(Path.DirectorySeparatorChar);
        return cut < 0 ? rest : rest[..cut];
    }

    /// <summary>Spec 7.2: 16 ms throttle, newest values win, rendered off the UI thread from a working bitmap that
    /// is decoded once per source. A source that is not there clears the preview at once.</summary>
    private void SchedulePreview()
    {
        var path = SourcePath;
        var options = Options;
        if (!File.Exists(path))
        {
            _preview.Cancel();
            Preview = null;
            return;
        }

        _preview.Run(ct => RenderAsync(path, options, ct));
    }

    private async Task RenderAsync(string path, FitOptions options, CancellationToken ct)
    {
        try
        {
            if (_working is null || !_workingPath.Equals(path, StringComparison.OrdinalIgnoreCase))
            {
                var loaded = await Task.Run(() => BackgroundFitter.LoadWorkingSource(path, PreviewWidth, PreviewHeight), ct);
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                _working = loaded;
                _workingPath = path;
            }

            var source = _working;
            var bitmap = await Task.Run(() => BackgroundFitter.Render(source, options, PreviewWidth, PreviewHeight), ct);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Preview = bitmap;
            Error = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                Preview = null;
                Error = "Could not read the image: " + ex.Message;
            }
        }
    }
}
