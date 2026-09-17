using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.App.Services;
using BhMaps.Core.Hashing;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Operations;
using BhMaps.Core.Packs;
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

    /// <summary>Spec 8: what the Values from line says instead, after the file name, when the picture the saved
    /// entry names has gone from the library.</summary>
    public const string PictureGoneText = "missing. Showing the saved file.";

    private const string BackgroundsFolder = "Backgrounds";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly BackgroundEditorRequest _request;
    private readonly Throttler _preview = new();

    private WorkingSource? _working;
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

        // The tile's own pack, so Save replaces the picture the user was looking at; anything else keeps it.
        var requested = packNames.FirstOrDefault(p => p.Equals(request.PackName, StringComparison.OrdinalIgnoreCase));
        var existingDefault = packNames.FirstOrDefault(p => p.Equals(DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = requested ?? existingDefault ?? NewPackChoice;
        NewPackName = TargetPack == NewPackChoice ? DefaultPackName : "";
        ValuesFromText = "";

        // Last: the change hook reads everything above it.
        SourcePath = request.SourcePath;

        // Later still: the record's own source has to win over the request's, and its stage over the empty one the
        // hook above leaves behind.
        LoadRecord(request);
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<MapSlotChoice> Maps { get; }

    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public BackgroundSave? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint), nameof(MapLabel), nameof(SaveAndApplyText), nameof(Title))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand))]
    public partial MapSlotChoice? SelectedMap { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasSource), nameof(Title), nameof(SourceFileName), nameof(EmptyText), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand))]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial ImageSource? SourceThumbnail { get; set; }

    /// <summary>"flowermap, 1920 x 1080" under the source's file name, or "" while it is unknown.</summary>
    [ObservableProperty]
    public partial string SourceDetail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFill), nameof(FitFill), nameof(FitFit), nameof(FitCenter), nameof(FitStretch))]
    public partial PictureFit Fit { get; set; }

    [ObservableProperty]
    public partial double PanX { get; set; }

    [ObservableProperty]
    public partial double PanY { get; set; }

    [ObservableProperty]
    public partial double DarkenPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(OverwriteHint))]
    [NotifyCanExecuteChangedFor(nameof(SaveAndApplyCommand), nameof(SaveOnlyCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    /// <summary>"Values from Default, saved 12 Sep 22:03.", or the missing-picture line (spec 8).</summary>
    [ObservableProperty]
    public partial string ValuesFromText { get; set; }

    /// <summary>Whether the Values from line and its Start fresh link are shown (spec 8).</summary>
    [ObservableProperty]
    public partial bool HasValuesFrom { get; set; }

    /// <summary>3.0: the window is named for the map, the way the platform editor is, never for the file.</summary>
    public string Title =>
        SelectedMap is { } map ? $"Edit background, {map.DisplayNames}" : "Edit background";

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

    public bool IsFill => Fit == PictureFit.Fill;

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public string Slot => SelectedMap?.Slot ?? "";

    /// <summary>Under All maps the save writes every map's slot, so the button says so before it is pressed.</summary>
    public string SaveAndApplyText =>
        SelectedMap is { IsAllMaps: true } ? "Save and apply to all maps" : "Save and apply";

    /// <summary>Spec 7.2, shown only when Save would replace a file that is already in the pack.</summary>
    public string OverwriteHint =>
        PackFileName() is { Length: > 0 } name && !IsNewPack && File.Exists(PackFilePath())
            ? $"Replaces {name} in {TargetPack}. Pick another pack to keep the original."
            : "";

    public bool FitFill
    {
        get => Fit == PictureFit.Fill;
        set { if (value) { Fit = PictureFit.Fill; } }
    }

    public bool FitFit
    {
        get => Fit == PictureFit.Fit;
        set { if (value) { Fit = PictureFit.Fit; } }
    }

    public bool FitCenter
    {
        get => Fit == PictureFit.Center;
        set { if (value) { Fit = PictureFit.Center; } }
    }

    public bool FitStretch
    {
        get => Fit == PictureFit.Stretch;
        set { if (value) { Fit = PictureFit.Stretch; } }
    }

    public bool CanSave => HasSource && SelectedMap is not null && PackNameValidator.IsValid(EffectivePackName, out _);

    private FitOptions Options => PictureFits.Options(Fit, PanX, PanY, DarkenPercent / 100.0);

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

    partial void OnFitChanged(PictureFit value) => SchedulePreview();

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

    /// <summary>Spec 8: the remembered values go and the editor is the one a first save would have opened. A
    /// request that named a picture keeps it, because that picture is what the user opened.</summary>
    [RelayCommand]
    private void StartFresh()
    {
        SourcePath = _request.SourcePath;
        Fit = PictureFit.Fill;
        PanX = 0.5;
        PanY = 0.5;
        DarkenPercent = 0;
        HasValuesFrom = false;
        ValuesFromText = "";

        // Every value above may already have been the default, in which case no change hook ran and the saved
        // file would still be on the stage.
        SchedulePreview();
    }

    /// <summary>Spec 8: the slot's saved entry opens the editor where the last save left it. The picture it names
    /// is the source again when it is still in the library; when it has gone the fit, the pan and the darken still
    /// load and the stage shows the file that was saved, which Save cannot replace until a source is chosen.
    /// </summary>
    private void LoadRecord(BackgroundEditorRequest request)
    {
        if (request.SourcePack is not { } pack || Slot.Length == 0)
        {
            return;
        }

        var relative = AssetPath.Background(Slot);
        if (BackgroundEditRecord.Load(pack.FullPath).Entry(relative) is not { } entry)
        {
            return;
        }

        Fit = FromRecord(entry.Mode);
        PanX = entry.PanX;
        PanY = entry.PanY;
        DarkenPercent = entry.Darken;
        HasValuesFrom = true;
        ValuesFromText = $"Values from {pack.Name}, saved {entry.SavedAt.ToLocalTime():d MMM HH:mm}.";

        // Save goes back to the pack the values came from, when the list still has it, because that is the set the
        // user is carrying on with (spec 5.3).
        if (PackChoices.Any(p => p.Equals(pack.Name, StringComparison.OrdinalIgnoreCase)))
        {
            TargetPack = pack.Name;
        }

        if (request.SourcePath.Length > 0)
        {
            // Opened from a picture tile: that picture is the source, whatever the entry remembers.
            return;
        }

        if (File.Exists(entry.Picture))
        {
            SourcePath = entry.Picture;
            return;
        }

        ValuesFromText = $"{Path.GetFileName(entry.Picture)}, {PictureGoneText}";
        ShowSavedFile(Path.Combine(pack.FullPath, relative));
    }

    /// <summary>The stage for an entry whose picture has gone: the file that save left in the pack, decoded for the
    /// preview. async void like the source row, because nothing above the constructor's frame can catch.</summary>
    private async void ShowSavedFile(string packFile)
    {
        if (!File.Exists(packFile))
        {
            return;
        }

        WorkingSource decoded;
        try
        {
            decoded = await Task.Run(() => BackgroundFitter.LoadWorkingSource(packFile, PreviewWidth, PreviewHeight));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            Error = "Could not read the saved background: " + ex.Message;
            return;
        }

        // A source picked while the decode ran owns the stage; this one is only what there was without it.
        if (SourcePath.Length == 0)
        {
            Preview = decoded.Bitmap;
        }
    }

    /// <summary>The editor's fit and the record's are the same three choices under two names (spec 8).</summary>
    internal static BackgroundMode ToRecord(PictureFit fit) => fit switch
    {
        PictureFit.Fit => BackgroundMode.Contain,
        PictureFit.Center => BackgroundMode.Center,
        PictureFit.Stretch => BackgroundMode.Stretch,
        _ => BackgroundMode.Cover,
    };

    /// <summary>The way back, with an unknown value from a newer version reading as the default.</summary>
    internal static PictureFit FromRecord(BackgroundMode mode) => mode switch
    {
        BackgroundMode.Contain => PictureFit.Fit,
        BackgroundMode.Center => PictureFit.Center,
        BackgroundMode.Stretch => PictureFit.Stretch,
        _ => PictureFit.Fill,
    };

    /// <summary>"Save and apply": the picture goes into the pack and on into the game. The game write itself is
    /// the shell's business, because it needs the boundary, the snapshot and the undo (spec 8, 3.0 E).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAndApplyAsync() => SaveAsync(apply: true);

    /// <summary>"Save only": the same write into the library, with nothing said to the game (3.0 E).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveOnlyAsync() => SaveAsync(apply: false);

    private async Task SaveAsync(bool apply)
    {
        var packFile = PackFilePath();
        var slot = Slot;
        var allMaps = SelectedMap is { IsAllMaps: true };
        if (File.Exists(packFile)
            && !_dialogs.Confirm(
                "Replace background?",
                $"{Path.GetFileNameWithoutExtension(packFile)} already exists in {EffectivePackName}. Replace it?",
                "Replace"))
        {
            return;
        }

        var path = SourcePath;
        var fit = Fit;
        var options = Options;
        var darken = DarkenPercent;
        var packRoot = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName);
        try
        {
            // The original, not the working bitmap: Save fits at 2048x1151 (spec 7.2).
            var bytes = await Task.Run(() => BackgroundFitter.Fit(path, options));
            Directory.CreateDirectory(Path.GetDirectoryName(packFile)!);
            await File.WriteAllBytesAsync(packFile, bytes);

            // Spec 8: what the pack remembers about this slot, so the next open is where this save left it. The
            // key is the pack-relative path, which is the slot's file for a map and the picture's own name under
            // All maps.
            await Task.Run(() =>
            {
                var record = BackgroundEditRecord.Load(packRoot);
                record.Set(
                    Path.GetRelativePath(packRoot, packFile),
                    new BackgroundSlotEntry
                    {
                        SavedAt = DateTimeOffset.Now,
                        Picture = path,
                        Mode = ToRecord(fit),
                        PanX = options.PanX,
                        PanY = options.PanY,
                        Darken = darken,
                        Hash = FileHasher.Hash(packFile),
                    });
                record.Save(packRoot);
            });
            Saved = new BackgroundSave(
                packFile, slot, SelectedMap?.DisplayNames ?? slot, apply, EffectivePackName, allMaps);
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
