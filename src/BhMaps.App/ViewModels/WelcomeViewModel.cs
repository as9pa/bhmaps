using BhMaps.App.Services;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>Spec 7.7: the three numbered steps the app asks for before the shell exists. Replaces the v1
/// first-run backup prompt; firstRunDone stays in settings for compatibility and is not read here.</summary>
public partial class WelcomeViewModel : ObservableObject
{
    private const string CaptureLabel = "Capturing the Default pack";

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public WelcomeViewModel(AppServices services, IDialogs dialogs)
    {
        _services = services;
        _dialogs = dialogs;

        // A command-line override, or a saved path that still works, is already the answer; Steam is only asked
        // when neither is, so a development run never goes looking at the real install.
        GamePath = SettingsStore.ValidateGamePath(services.GamePath, out _)
            ? services.GamePath
            : SteamLocator.FindMapArt() ?? "";
        LibraryPath = SettingsStore.ValidateLibraryPath(services.LibraryPath, out _)
            ? services.LibraryPath
            : AppSettings.DefaultLibraryPath;
        GameError = "";
        LibraryError = "";
        ProgressText = "";
        CaptureNow = true;
    }

    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial string GamePath { get; set; }

    [ObservableProperty]
    public partial string LibraryPath { get; set; }

    [ObservableProperty]
    public partial string GameError { get; set; }

    [ObservableProperty]
    public partial string LibraryError { get; set; }

    /// <summary>Step 3's answer. Yes by default, because a library with no Default pack cannot reset anything.</summary>
    [ObservableProperty]
    public partial bool CaptureNow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(
        nameof(ChangeGameCommand),
        nameof(ChangeLibraryCommand),
        nameof(FinishCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; }

    public bool IsNotBusy => !IsBusy;

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void ChangeGame()
    {
        var folder = _dialogs.PickFolder("Choose the Brawlhalla mapArt folder");
        if (folder is not null)
        {
            GamePath = folder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void ChangeLibrary()
    {
        var folder = _dialogs.PickFolder("Choose the pack library folder");
        if (folder is not null)
        {
            LibraryPath = folder;
        }
    }

    /// <summary>Validates both paths, saves them with welcomeDone set, then runs step 3 when the answer was Yes.
    /// The save happens even with overrides active: those still win for this run, and the saved values are what
    /// the next normal start reads.</summary>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task FinishAsync()
    {
        var game = Normalize(GamePath);
        var library = Normalize(LibraryPath);
        GamePath = game;
        LibraryPath = library;
        var gameOk = SettingsStore.ValidateGamePath(game, out var gameError);
        var libraryOk = SettingsStore.ValidateLibraryPath(library, out var libraryError);
        GameError = gameOk ? "" : gameError;
        LibraryError = libraryOk ? "" : libraryError;
        if (!gameOk || !libraryOk)
        {
            return;
        }

        try
        {
            _services.UpdateSettings(
                _services.Settings with { GamePath = game, LibraryPath = library, WelcomeDone = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The window stays open so the user can retry or cancel.
            _dialogs.Error("Settings not saved", ex.Message);
            return;
        }

        if (CaptureNow)
        {
            await CaptureAsync(game, library);
        }

        CloseRequested?.Invoke(true);
    }

    /// <summary>An error next to a path the user has since changed is worse than no error at all, so any edit,
    /// typed or picked, clears it; Finish is what puts one back.</summary>
    partial void OnGamePathChanged(string value) => GameError = "";

    partial void OnLibraryPathChanged(string value) => LibraryError = "";

    /// <summary>Step 3, spec 6.1. The shell and its busy boundary do not exist yet, so this is the window's own:
    /// the buttons are disabled through IsNotBusy and the progress line says what is being copied. The paths are
    /// the ones the user just confirmed, not the run's, so the capture matches what the two steps say.</summary>
    private async Task CaptureAsync(string game, string library)
    {
        IsBusy = true;
        ProgressText = CaptureLabel;
        ApplyResult? result = null;
        var progress = new Progress<string>(message => ProgressText = $"{CaptureLabel}: {message}");
        try
        {
            await Task.Run(() => { result = DefaultPack.Capture(game, library, progress); });
        }
        catch (Exception ex)
        {
            _dialogs.Error("Something went wrong", ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }

        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be captured", result.Failures);
        }
    }

    /// <summary>Explorer's "Copy as path" wraps the path in quotes; strip one surrounding pair so it still resolves.</summary>
    private static string Normalize(string path)
    {
        var trimmed = path.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"')
            ? trimmed[1..^1]
            : trimmed;
    }
}
