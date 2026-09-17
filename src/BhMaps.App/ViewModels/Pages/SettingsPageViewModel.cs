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

    /// <summary>Set once the download has finished and been verified: the path of the exe the swap will move.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    public partial string? ReadyExe { get; set; }

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

    /// <summary>Spec 7.3: a build that cannot swap itself never offers a download at all.</summary>
    public bool CanSwap { get; } = UpdateInstaller.CanSwap(Environment.ProcessPath ?? "");

    public bool ShowWhatChanged => Newer is not null && !Downloading;

    public bool CanUpdate => Newer is not null && !Downloading;

    public string UpdateButtonText =>
        !CanSwap ? "Release page"
        : ReadyExe is not null ? "Close and update"
        : Newer is not null ? "Get"
        : "";

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
            OnPropertyChanged(nameof(ShowWhatChanged));
            OnPropertyChanged(nameof(UpdateButtonText));
            return;
        }

        if (ReadyExe is not null && Newer is { } ready)
        {
            UpdateLine = $"{UpdateText.Short(ready.Version)} is ready. It installs when you close BhMaps.";
        }
        else if (Newer is { } release)
        {
            // What the download does is the Get button's tooltip, so the line says only what is out there.
            var available = $"Update available: {UpdateText.Short(release.Version)}.";
            UpdateLine = CanSwap && release.CanDownload
                ? available
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
        OnPropertyChanged(nameof(ShowWhatChanged));
        OnPropertyChanged(nameof(UpdateButtonText));
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

    /// <summary>Spec 7.3: one button, three jobs. No swap possible, or no assets: the release page. Nothing
    /// downloaded yet: download it. Downloaded and verified: write the script, start it hidden and close.</summary>
    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (Newer is not { } release)
        {
            return;
        }

        if (!CanSwap || !release.CanDownload)
        {
            OpenReleasePage(release);
            return;
        }

        if (ReadyExe is { } ready)
        {
            CloseAndUpdate(ready);
            return;
        }

        _downloadCts = new CancellationTokenSource();
        Downloading = true;
        DownloadProgress = 0;
        UpdateLine = $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(0, release.ExeSize)}";
        RefreshUpdateRow();
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                DownloadProgress = p.Total > 0 ? (double)p.Done / p.Total : 0;
                UpdateLine =
                    $"Downloading {UpdateText.Short(release.Version)}, {UpdateText.Megabytes(p.Done, p.Total)}";
            });

            ReadyExe = await Services.Updates.DownloadAsync(
                release, Services.UpdatesDir, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            ReadyExe = null;
        }
        catch (InvalidDataException)
        {
            ReadyExe = null;
            Shell.Dialogs.Error(
                "Update not installed",
                "The downloaded file did not match the checksum on the release, so it was deleted. Try again later.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            ReadyExe = null;
            Shell.Dialogs.Error("Update not downloaded", ex.Message);
        }
        finally
        {
            Downloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
            RefreshUpdateRow();
        }
    }

    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    [RelayCommand]
    private void WhatChanged()
    {
        if (Newer is { } release)
        {
            OpenReleasePage(release);
        }
    }

    /// <summary>The Check now button: the same check, past the once-a-day rule.</summary>
    [RelayCommand]
    private Task CheckNowAsync() => Shell.CheckForUpdateAsync(force: true);

    /// <summary>Spec 7.3: the script is started hidden and the window closed; the script waits for this process to
    /// be gone before it touches anything. Nothing restarts on its own: this runs only from the button.</summary>
    private void CloseAndUpdate(string newExe)
    {
        if (Environment.ProcessPath is not { Length: > 0 } running)
        {
            return;
        }

        try
        {
            var script = UpdateInstaller.WriteApplyScript(
                Services.UpdatesDir, newExe, running, Environment.ProcessId);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                WorkingDirectory = Services.UpdatesDir,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            Shell.Dialogs.Error("Update not installed", ex.Message);
            return;
        }

        Application.Current.MainWindow?.Close();
    }

    /// <summary>The release page, for a build that cannot swap itself and for What changed. The browser not
    /// opening is a dialog here: this row has no line of its own to put a reason on.</summary>
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
