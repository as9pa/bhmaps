using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using BhMaps.App.Services;
using BhMaps.Core.Settings;
using BhMaps.Core.Update;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>3.0: the rows are grouped under Folders, Game and Updates, one line each. There is no OK button, so every row writes
/// settings.json the moment it changes and rescans when the change moves what the app is looking at.</summary>
public partial class SettingsPageViewModel : PageViewModel
{
    /// <summary>True while this page is writing the saved values into its own rows, so a row's setter does not
    /// save back the value it was just handed.</summary>
    private bool _refreshing;

    /// <summary>Live only while a download is running, so Cancel and the window closing can both stop it.</summary>
    private CancellationTokenSource? _downloadCts;

    public SettingsPageViewModel(MainViewModel shell)
        : base(shell)
    {
        _refreshing = true;
        GameError = "";
        LibraryError = "";
        UpdateLine = "";
        GamePath = shell.Services.GamePath;
        LibraryPath = shell.Services.LibraryPath;
        GameDataStatus = shell.Services.LevelData.StatusSentence;
        CheckForUpdates = shell.Services.Settings.CheckForUpdates;
        Version = ReadVersion();
        _refreshing = false;
        RefreshUpdateRow();

        // Spec 3.5: a startup re-read runs in the background and finishes after this page exists, and nothing
        // rescans behind it, so the sentence has to follow the service rather than only the scan.
        shell.Services.LevelData.Changed += OnLevelDataChanged;
    }

    public override string Title => "Settings";

    /// <summary>Set only when --game or --library was passed. It explains why Change does not move the path above
    /// it: the row shows what this run is using, and a save applies on the next normal start. From the v1 dialog.</summary>
    public string OverrideNote =>
        Services.GameOverride is null && Services.LibraryOverride is null
            ? ""
            : "Command-line overrides are active for this run. Saved values apply on the next normal start.";

    public bool HasOverrideNote => OverrideNote.Length > 0;

    /// <summary>The version, as major.minor.build. The assembly version's fourth part is always 0 here.</summary>
    public string Version { get; }

    /// <summary>The path in use this run, so a command-line override is what the row shows (AppServices.GamePath).</summary>
    [ObservableProperty]
    public partial string GamePath { get; set; }

    [ObservableProperty]
    public partial string LibraryPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameError))]
    public partial string GameError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryError))]
    public partial string LibraryError { get; set; }

    /// <summary>LevelDataService.StatusSentence, which already carries "Read on &lt;date&gt;." (spec 3.5).</summary>
    [ObservableProperty]
    public partial string GameDataStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateLine))]
    public partial string UpdateLine { get; set; }

    [ObservableProperty]
    public partial bool Downloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    /// <summary>Spec 7.2: the Updates row's checkbox. Saves the moment it moves, like every other row here.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    public bool HasGameError => GameError.Length > 0;

    public bool HasLibraryError => LibraryError.Length > 0;

    public bool HasUpdateLine => UpdateLine.Length > 0;

    /// <summary>Which asset Update downloads. A development run has none and never offers Update, only
    /// Changelog. Fixed for the life of the process.</summary>
    public UpdateBuild Build { get; } = UpdateInstaller.DetectBuild();

    public bool ShowChangelog => Newer is not null && !Downloading;

    /// <summary>Spec 7.3: one Update button whenever a newer release carries the asset this build updates from.</summary>
    public bool CanUpdate => Newer is { } release && release.CanDownload(Build) && !Downloading;

    private AppServices Services => Shell.Services;

    /// <summary>The available release when it is actually newer than this build, otherwise null.</summary>
    private ReleaseInfo? Newer =>
        Shell.AvailableUpdate is { } release && ReleaseChecker.IsNewer(release, Services.AppVersion)
            ? release
            : null;

    public override void Refresh(ScanSnapshot snapshot)
    {
        _refreshing = true;
        GamePath = Services.GamePath;
        LibraryPath = Services.LibraryPath;
        GameDataStatus = Services.LevelData.StatusSentence;
        CheckForUpdates = Services.Settings.CheckForUpdates;
        _refreshing = false;
        RefreshUpdateRow();
    }

    /// <summary>Spec 7.3's four states of the Version row's second line, plus the two download states. Called
    /// from the constructor, from Refresh, and by the shell after every check.</summary>
    public void RefreshUpdateRow()
    {
        if (Downloading)
        {
            OnPropertyChanged(nameof(CanUpdate));
            OnPropertyChanged(nameof(ShowChangelog));
            return;
        }

        if (Newer is { } release)
        {
            // What Update does is its tooltip, so the line says only what is out there.
            var available = $"Update available: {UpdateText.Short(release.Version)}.";
            UpdateLine =
                Build == UpdateBuild.Development ? available + " A development build does not update itself."
                : release.CanDownload(Build) ? available
                : available + " Get it from the release page.";
        }
        else if (Shell.UpdateCheckFailed)
        {
            UpdateLine = "Could not reach GitHub. Try again later.";
        }
        else if (Services.Settings.LastUpdateCheck is null)
        {
            UpdateLine = "Not checked yet.";
        }
        else
        {
            UpdateLine =
                "Latest. Last checked "
                + UpdateText.CheckedWhen(Services.Settings.LastUpdateCheck, DateTimeOffset.UtcNow)
                + ".";
        }

        OnPropertyChanged(nameof(CanUpdate));
        OnPropertyChanged(nameof(ShowChangelog));
    }

    /// <summary>The shell calls this as the window closes, so a download in flight stops with it rather than
    /// writing into the updates folder behind a window that is already gone.</summary>
    public void Shutdown() => _downloadCts?.Cancel();

    /// <summary>There is no OK button here either, and the shell's own line follows AvailableUpdate, so the only
    /// thing the save has to move is this page's own Updates line.</summary>
    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_refreshing || Services.Settings.CheckForUpdates == value)
        {
            return;
        }

        if (!Save(Services.Settings with { CheckForUpdates = value }))
        {
            _refreshing = true;
            CheckForUpdates = Services.Settings.CheckForUpdates;
            _refreshing = false;
            return;
        }

        RefreshUpdateRow();
    }

    /// <summary>Spec 7.3, like openmacro: one button. Download and verify the asset this build updates from,
    /// then swap it in place and restart on it. Nothing is left for a second click.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (Newer is not { } release || !release.CanDownload(Build) || !ReadyToClose())
        {
            return;
        }

        string? downloaded = null;
        _downloadCts = new CancellationTokenSource();
        Downloading = true;
        DownloadProgress = 0;
        UpdateLine =
            $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(0, release.SizeFor(Build))}";
        RefreshUpdateRow();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                DownloadProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                UpdateLine =
                    $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(p.Done, p.Total)}";
            });

            downloaded = await Services.Updates.DownloadAsync(
                release, Build, Services.UpdatesDir, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancel: the row goes back to offering Update.
        }
        catch (InvalidDataException ex)
        {
            Shell.Dialogs.Error("Update not installed", ex.Message + " It was deleted. Try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            Shell.Dialogs.Error("Update not downloaded", ex.Message);
        }
        finally
        {
            Downloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            RefreshUpdateRow();
        }

        // The download took a while: the app has to be free to close now, not only when the button was pressed.
        if (downloaded is not null && ReadyToClose())
        {
            SwapAndRestart(downloaded, release);
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    /// <summary>The release notes, on the release page.</summary>
    [RelayCommand]
    private void Changelog()
    {
        if (Newer is { } release)
        {
            OpenReleasePage(release);
        }
    }

    /// <summary>The Check now button: the same check, past the once-a-day rule.</summary>
    [RelayCommand]
    private Task CheckNowAsync() => Shell.CheckForUpdateAsync(force: true);

    /// <summary>The window has no close guard of its own; what closing could cut off is an operation still
    /// writing. An update asked for while the shell is busy waits for it rather than stopping it.</summary>
    private bool ReadyToClose()
    {
        if (!Shell.IsBusy)
        {
            return true;
        }

        Shell.Dialogs.Error(
            "Update not installed",
            "BhMaps is still working. Wait for it to finish, then press Update again.");
        return false;
    }

    /// <summary>The openmacro swap: rename the running exe to .old, move the new one into its place, start it
    /// with --after-update so it waits for this process, and close. Any failure puts both files back and says
    /// why; only then is the release page offered, as the way to update by hand.</summary>
    private void SwapAndRestart(string newExe, ReleaseInfo release)
    {
        if (Environment.ProcessPath is not { Length: > 0 } running)
        {
            return;
        }

        try
        {
            UpdateInstaller.Swap(newExe, running);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SwapFailed(ex.Message, release);
            return;
        }

        try
        {
            var start = new ProcessStartInfo(running) { UseShellExecute = false };
            foreach (var arg in UpdateInstaller.RestartArgs(Environment.GetCommandLineArgs()[1..], Environment.ProcessId))
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            try
            {
                UpdateInstaller.Rollback(newExe, running);
            }
            catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"BhMaps: the update rollback failed: {undo.Message}");
            }

            SwapFailed(ex.Message, release);
            return;
        }

        Application.Current.MainWindow?.Close();
    }

    private void SwapFailed(string reason, ReleaseInfo release)
    {
        var open = Shell.Dialogs.Confirm(
            "Update not installed",
            reason
                + " BhMaps was left as it was. To update itself it has to run from a folder it can write to, such"
                + " as Documents or Desktop. Move BhMaps.exe there and press Update again, or get the new version"
                + " from the release page.",
            "Open release page");
        if (open)
        {
            OpenReleasePage(release);
        }
    }

    /// <summary>The release page, for Changelog and for a swap that failed. The browser not opening is a dialog
    /// here: this row has no line of its own to put a reason on.</summary>
    private void OpenReleasePage(ReleaseInfo release)
    {
        if (ExplorerLauncher.OpenUrl(release.HtmlUrl) is { } reason)
        {
            Shell.Dialogs.Error("Release page not opened", reason);
        }
    }

    /// <summary>Pick, validate, save, re-read the game's data, rescan. A path that does not pass stays on the row
    /// rather than in a dialog, and nothing is saved. With an override active the save still happens, and the row
    /// still shows the override.</summary>
    [RelayCommand]
    private async Task ChangeGameAsync()
    {
        if (Shell.Dialogs.PickFolder("Choose the Brawlhalla mapArt folder") is not { } picked)
        {
            return;
        }

        var path = Normalize(picked);
        if (!SettingsStore.ValidateGamePath(path, out var error))
        {
            GameError = error;
            return;
        }

        GameError = "";
        if (!Save(Services.Settings with { GamePath = path }))
        {
            return;
        }

        // The level data belongs to the install, so a new game folder needs the same re-read Refresh now does,
        // not just a rescan: the saved path is already in AppServices, so LevelDataService reads the new root.
        // The rescan behind it is that method's, which is why there is no second one here.
        await RefreshGameDataAsync();
    }

    [RelayCommand]
    private async Task ChangeLibraryAsync()
    {
        if (Shell.Dialogs.PickFolder("Choose the pack library folder") is not { } picked)
        {
            return;
        }

        var path = Normalize(picked);
        if (!SettingsStore.ValidateLibraryPath(path, out var error))
        {
            LibraryError = error;
            return;
        }

        LibraryError = "";
        if (!Save(Services.Settings with { LibraryPath = path }))
        {
            return;
        }

        await Shell.RescanAsync();
    }

    // This page has a line under each path to put the reason in, so it shows it there rather than in a dialog.
    [RelayCommand]
    private void OpenGame() => GameError = ExplorerLauncher.Open(Services.GamePath) ?? "";

    [RelayCommand]
    private void OpenLibrary() => LibraryError = ExplorerLauncher.Open(Services.LibraryPath) ?? "";

    /// <summary>Spec 3.5: force a re-read of the game's four files, then rescan so the catalog, the sidebar and
    /// every page pick up the new names.</summary>
    [RelayCommand]
    private async Task RefreshGameDataAsync()
    {
        var ok = await Shell.RunBusyAsync("Reading game data", (_, ct) => Services.LevelData.RefreshAsync(ct));
        await Shell.RescanAsync();

        // After the rescan, because the scan puts back whatever line it found on the strip when it started.
        if (ok && Services.LevelData.Available)
        {
            Shell.Status.Note(Services.LevelData.ReadNote);
        }

        // Again by hand, because a cancelled scan never reaches Refresh and the read behind it still happened.
        GameDataStatus = Services.LevelData.StatusSentence;
    }

    /// <summary>Spec 6.1. The shell owns the flow so the confirm text and the busy boundary match the Packs page.</summary>
    [RelayCommand]
    private Task CaptureDefaultsAsync() => Shell.CaptureDefaultsAsync();

    private void OnLevelDataChanged() => GameDataStatus = Services.LevelData.StatusSentence;

    /// <summary>v1's behaviour: a settings file that cannot be written is a dialog, not an inline error, because
    /// what failed is not the path that was just chosen. False means nothing was saved.</summary>
    private bool Save(AppSettings settings)
    {
        try
        {
            Services.UpdateSettings(settings);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Shell.Dialogs.Error("Settings not saved", ex.Message);
            return false;
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

    private static string ReadVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
