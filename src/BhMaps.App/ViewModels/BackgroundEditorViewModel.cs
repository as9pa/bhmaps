using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What one Save left in the library: the fitted picture, the slot it was fitted for, and whether the
/// user asked for it to go into the game as well. The shell does that part, so the editor never writes there.</summary>
public sealed record BackgroundSave(string PackFile, string Slot, bool ApplyToGame);

public partial class BackgroundEditorViewModel : ObservableObject
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 360;
    public const string NewPackChoice = "New pack...";
    public const string DefaultPackName = "My Backgrounds";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly Debouncer _render = new();

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

    /// <summary>What Save wrote into the library, or null while nothing has been saved. The shell reads it once
    /// the window closes with OK and applies it to the game when it says so.</summary>
    public BackgroundSave? Saved { get; private set; }

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

    /// <summary>Saves the fitted picture into the pack and nothing else. The apply the "Apply to game now" box asks
    /// for is a game write, so it is left to the shell, which has the busy boundary, the undo snapshot and the
    /// running-game policy to put around it.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packName = EffectivePackName;
        var slot = Slot.Trim();
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
            Saved = new BackgroundSave(packFile, slot, ApplyNow);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the background", ex.Message);
        }
    }

    /// <summary>Spec 5.3: debounce 150 ms, render off the UI thread, latest request wins. A source that is not
    /// there clears the preview at once rather than after the quiet period, because there is nothing to wait for.</summary>
    private void ScheduleRender()
    {
        var path = SourcePath;
        var options = Options;
        if (!File.Exists(path))
        {
            _render.Cancel();
            Preview = null;
            return;
        }

        _render.Run(async ct =>
        {
            try
            {
                var bitmap = await Task.Run(() => BackgroundFitter.Render(path, options, PreviewWidth, PreviewHeight), ct);
                if (!ct.IsCancellationRequested)
                {
                    Preview = bitmap;
                    Error = "";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
            {
                if (!ct.IsCancellationRequested)
                {
                    Preview = null;
                    Error = "Could not read the image: " + ex.Message;
                }
            }
        });
    }
}
