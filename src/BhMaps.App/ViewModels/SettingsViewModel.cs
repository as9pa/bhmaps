using BhMaps.App.Services;
using BhMaps.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly IDialogs _dialogs;

    public SettingsViewModel(AppServices services, IDialogs dialogs, string? message)
    {
        _services = services;
        _dialogs = dialogs;
        Message = message ?? "";
        OverrideNote = services.GameOverride is null && services.LibraryOverride is null
            ? ""
            : "Command-line overrides are active for this run. Saved values apply on the next normal start.";
        GamePath = services.Settings.GamePath;
        LibraryPath = services.Settings.LibraryPath;
        GameError = "";
        LibraryError = "";
    }

    public event Action<bool>? CloseRequested;

    public string Message { get; }

    public bool HasMessage => Message.Length > 0;

    public string OverrideNote { get; }

    public bool HasOverrideNote => OverrideNote.Length > 0;

    [ObservableProperty]
    public partial string GamePath { get; set; }

    [ObservableProperty]
    public partial string LibraryPath { get; set; }

    [ObservableProperty]
    public partial string GameError { get; set; }

    [ObservableProperty]
    public partial string LibraryError { get; set; }

    [RelayCommand]
    private void BrowseGame()
    {
        var folder = _dialogs.PickFolder("Choose the Brawlhalla mapArt folder");
        if (folder is not null)
        {
            GamePath = folder;
        }
    }

    [RelayCommand]
    private void BrowseLibrary()
    {
        var folder = _dialogs.PickFolder("Choose the pack library folder");
        if (folder is not null)
        {
            LibraryPath = folder;
        }
    }

    /// <summary>Spec 5.4: validate both paths, show errors inline, save and close only when both pass.</summary>
    [RelayCommand]
    private void Ok()
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
            _services.UpdateSettings(_services.Settings with { GamePath = game, LibraryPath = library });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The dialog stays open so the user can retry or cancel.
            _dialogs.Error("Settings not saved", ex.Message);
            return;
        }

        CloseRequested?.Invoke(true);
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
