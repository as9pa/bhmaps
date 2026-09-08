using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Game;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class BackgroundEditorViewModel : ObservableObject
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 360;
    public const string NewPackChoice = "New pack...";
    public const string DefaultPackName = "My Backgrounds";

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private CancellationTokenSource? _renderCts;

    public BackgroundEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<string> slots,
        IReadOnlyList<string> packNames,
        string? initialSlot)
    {
        _services = services;
        _dialogs = dialogs;
        Slots = slots;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        Slot = initialSlot ?? slots.FirstOrDefault() ?? "BG_New.jpg";
        SourcePath = "";
        Error = "";
        PanX = 0.5;
        PanY = 0.5;
        ApplyNow = true;
        var existingDefault = packNames.FirstOrDefault(p => p.Equals(DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? NewPackChoice;
        NewPackName = existingDefault is null ? DefaultPackName : "";
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> Slots { get; }

    public IReadOnlyList<string> PackChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlotError), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Slot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave), nameof(HasSource))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string SourcePath { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCover), nameof(ModeCover), nameof(ModeContain), nameof(ModeStretch))]
    public partial FitMode Mode { get; set; }

    [ObservableProperty]
    public partial double PanX { get; set; }

    [ObservableProperty]
    public partial double PanY { get; set; }

    [ObservableProperty]
    public partial double DarkenPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyNow { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    public bool IsCover => Mode == FitMode.Cover;

    public bool HasSource => File.Exists(SourcePath);

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public bool ModeCover
    {
        get => Mode == FitMode.Cover;
        set
        {
            if (value)
            {
                Mode = FitMode.Cover;
            }
        }
    }

    public bool ModeContain
    {
        get => Mode == FitMode.Contain;
        set
        {
            if (value)
            {
                Mode = FitMode.Contain;
            }
        }
    }

    public bool ModeStretch
    {
        get => Mode == FitMode.Stretch;
        set
        {
            if (value)
            {
                Mode = FitMode.Stretch;
            }
        }
    }

    public string SlotError
    {
        get
        {
            var slot = Slot.Trim();
            if (slot.Length == 0)
            {
                return "Slot name is empty.";
            }

            if (!slot.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                return "Slot name must end in .jpg";
            }

            if (slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "Slot name has characters that are not allowed in a file name.";
            }

            return "";
        }
    }

    public bool CanSave => SlotError.Length == 0 && HasSource && PackNameValidator.IsValid(EffectivePackName, out _);

    private FitOptions Options => new(Mode, PanX, PanY, DarkenPercent / 100.0);

    public void AcceptDroppedFile(string path) => SourcePath = path;

    partial void OnSourcePathChanged(string value) => ScheduleRender();

    partial void OnModeChanged(FitMode value) => ScheduleRender();

    partial void OnPanXChanged(double value) => ScheduleRender();

    partial void OnPanYChanged(double value) => ScheduleRender();

    partial void OnDarkenPercentChanged(double value) => ScheduleRender();

    [RelayCommand]
    private void Browse()
    {
        var file = _dialogs.PickImageFile("Choose a source image");
        if (file is not null)
        {
            SourcePath = file;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packName = EffectivePackName;
        var slot = Slot.Trim();
        if (ApplyNow
            && GameProcess.IsRunning()
            && !_dialogs.Confirm(
                "Brawlhalla is running",
                "Brawlhalla is running. Changes will not show until it restarts, and some files may be locked. Continue?"))
        {
            return;
        }

        var packFile = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), packName, "Backgrounds", slot);
        if (File.Exists(packFile)
            && !_dialogs.Confirm("Replace background?", $"{slot} already exists in pack {packName}. Replace it?"))
        {
            return;
        }

        var path = SourcePath;
        var options = Options;
        try
        {
            var bytes = await Task.Run(() => BackgroundFitter.Fit(path, options));
            Directory.CreateDirectory(Path.GetDirectoryName(packFile)!);
            await File.WriteAllBytesAsync(packFile, bytes);
            if (ApplyNow)
            {
                var gameFile = Path.Combine(_services.GamePath, "Backgrounds", slot);
                Directory.CreateDirectory(Path.GetDirectoryName(gameFile)!);
                await File.WriteAllBytesAsync(gameFile, bytes);
            }

            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the background", ex.Message);
        }
    }

    /// <summary>Spec 5.3: debounce 150 ms, render off the UI thread, latest request wins.</summary>
    private async void ScheduleRender()
    {
        _renderCts?.Cancel();
        _renderCts = new CancellationTokenSource();
        var ct = _renderCts.Token;
        var path = SourcePath;
        var options = Options;
        if (!File.Exists(path))
        {
            Preview = null;
            return;
        }

        try
        {
            await Task.Delay(Debounce, ct);
            var bitmap = await Task.Run(() => BackgroundFitter.Render(path, options, PreviewWidth, PreviewHeight), ct);
            if (!ct.IsCancellationRequested)
            {
                Preview = bitmap;
                Error = "";
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            if (!ct.IsCancellationRequested)
            {
                Preview = null;
                Error = "Could not read the image: " + ex.Message;
            }
        }
    }
}
