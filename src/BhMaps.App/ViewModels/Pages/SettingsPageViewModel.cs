using System.Reflection;
using BhMaps.App.Services;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels.Pages;

/// <summary>Spec 7.6: one line per setting, no explanatory subtext. There is no OK button, so every row writes
/// settings.json the moment it changes and rescans when the change moves what the app is looking at.</summary>
public partial class SettingsPageViewModel : PageViewModel
{
    public SettingsPageViewModel(MainViewModel shell)
        : base(shell)
    {
        GameError = "";
        LibraryError = "";
        GamePath = shell.Services.GamePath;
        LibraryPath = shell.Services.LibraryPath;
        GameDataStatus = shell.Services.LevelData.StatusSentence;
        Version = ReadVersion();

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

    public bool HasGameError => GameError.Length > 0;

    public bool HasLibraryError => LibraryError.Length > 0;

    private AppServices Services => Shell.Services;

    public override void Refresh(ScanSnapshot snapshot)
    {
        GamePath = Services.GamePath;
        LibraryPath = Services.LibraryPath;
        GameDataStatus = Services.LevelData.StatusSentence;
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
        await Shell.RunBusyAsync("Reading game data", (_, ct) => Services.LevelData.RefreshAsync(ct));
        await Shell.RescanAsync();

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
