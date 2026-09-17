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

    /// <summary>Step 3's own cancel, because the shell's busy boundary does not exist yet. Null when nothing is
    /// capturing.</summary>
    private CancellationTokenSource? _cts;

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

    /// <summary>What step 3's capture did, or null when it was not run, was cancelled, or has not finished. The
    /// window is gone by the time the outcome can be read, so the shell's strip says it instead.</summary>
    public WelcomeCapture? Capture { get; private set; }

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
        nameof(FinishCommand),
        nameof(CancelCaptureCommand))]
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
            if (!await CaptureAsync(game, library))
            {
                // Cancelled: the window goes back to step 3 rather than out, because the answer it is asking for
                // has not been given yet.
                return;
            }
        }

        CloseRequested?.Invoke(true);
    }

    /// <summary>An error next to a path the user has since changed is worse than no error at all, so any edit,
    /// typed or picked, clears it; Finish is what puts one back.</summary>
    partial void OnGamePathChanged(string value) => GameError = "";

    partial void OnLibraryPathChanged(string value) => LibraryError = "";

    /// <summary>Step 3, spec 6.1. The shell and its busy boundary do not exist yet, so this is the window's own:
    /// the rows are disabled through IsNotBusy, the progress line stands where the buttons were and Cancel beside
    /// it stops the copy. The paths are the ones the user just confirmed, not the run's, so the capture matches
    /// what the two steps say. False when the user cancelled, which is the one outcome that keeps the window
    /// open; a failure closes with the rest and the shell's strip carries it (3.0), because a dialog to dismiss
    /// before the window behind it can be read says no more than the line does.</summary>
    private async Task<bool> CaptureAsync(string game, string library)
    {
        IsBusy = true;
        ProgressText = CaptureLabel;
        ApplyResult? result = null;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(message => ProgressText = $"{CaptureLabel}: {message}");
        try
        {
            var ct = _cts.Token;
            await Task.Run(() => { result = DefaultPack.Capture(game, library, progress, ct); }, ct);
            Capture = new WelcomeCapture(
                result is { } captured
                    ? $"Captured the Default pack, {MainViewModel.Count(captured.Copied, "file")}."
                    : "Captured the Default pack.",
                Failed: false);
        }
        catch (OperationCanceledException)
        {
            // Whatever the capture undoes on its way out it has already undone; nothing here is left to say.
            return false;
        }
        catch (Exception ex)
        {
            Capture = new WelcomeCapture(MainViewModel.FailureLine(CaptureLabel, ex.Message), Failed: true);
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
            _cts.Dispose();
            _cts = null;
        }

        if (result is not null)
        {
            _dialogs.ShowFailures("Some files could not be captured", result.Failures);
        }

        return true;
    }

    /// <summary>Step 3's Cancel, the strip's link before there is a strip. The token is what stops the copy; the
    /// capture's own cleanup is what puts the library back.</summary>
    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void CancelCapture() => _cts?.Cancel();

    /// <summary>Explorer's "Copy as path" wraps the path in quotes; strip one surrounding pair so it still resolves.</summary>
    private static string Normalize(string path)
    {
        var trimmed = path.Trim();
        return trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"')
            ? trimmed[1..^1]
            : trimmed;
    }
}

/// <summary>What step 3's capture did, in the line the status strip will show: the done line the in-app capture
/// says, or the failure line, which the shell tells apart by <paramref name="Failed"/>.</summary>
public readonly record struct WelcomeCapture(string Text, bool Failed);
