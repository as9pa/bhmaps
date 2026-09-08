using System.Windows;
using BhMaps.App.Services;
using BhMaps.App.ViewModels;
using BhMaps.App.Views;
using BhMaps.Core.Settings;

namespace BhMaps.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, unhandled) =>
        {
            MessageBox.Show(unhandled.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            unhandled.Handled = true;
        };

        var parsed = CommandLine.Parse(e.Args);
        if (parsed.MissingAppDataError() is { } overrideError)
        {
            // Refuse before anything loads settings or scans, so the real %APPDATA% files stay as they were.
            MessageBox.Show(overrideError, "Development overrides need --appdata", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var services = new AppServices(parsed.AppData ?? SettingsStore.DefaultAppDataDir, parsed.Game, parsed.Library);
        var dialogs = new WpfDialogs();

        // Spec 6: no valid game path means Settings first, not a crash and not an empty grid.
        while (!SettingsStore.ValidateGamePath(services.GamePath, out var error))
        {
            var settings = new SettingsWindow
            {
                DataContext = new SettingsViewModel(services, dialogs, $"Set the Brawlhalla mapArt folder to continue. {error}"),
            };
            if (settings.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        var window = new MainWindow { DataContext = new MainViewModel(services, dialogs) };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
