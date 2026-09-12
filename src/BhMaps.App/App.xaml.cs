using System.Windows;
using BhMaps.App.Services;
using BhMaps.App.ViewModels;
using BhMaps.App.Views;
using BhMaps.Core.Settings;

namespace BhMaps.App;

public partial class App : Application
{
    /// <summary>--quiet: every window this run opens is shown without activating it, so an automated capture
    /// never takes the foreground from whoever is at the machine.</summary>
    public static bool Quiet { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, unhandled) =>
        {
            MessageBox.Show(unhandled.Exception.Message, "Unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            unhandled.Handled = true;
        };

        var parsed = CommandLine.Parse(e.Args);
        Quiet = parsed.Quiet;
        if (parsed.MissingAppDataError() is { } overrideError)
        {
            // Refuse before anything loads settings or scans, so the real %APPDATA% files stay as they were.
            MessageBox.Show(overrideError, "Development overrides need --appdata", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var services = new AppServices(parsed.AppData ?? SettingsStore.DefaultAppDataDir, parsed.Game, parsed.Library);
        Exit += (_, _) => services.Dispose();
        var dialogs = new WpfDialogs();

        // Spec 5: one sweep a run keeps the preview folder from growing without bound.
        services.Previews.Sweep(DateTimeOffset.UtcNow);

        // Spec 7.7: the welcome window is the first run, and it also stands in for the v1 settings loop, so a
        // saved game path that has stopped working comes back here rather than to a crash or an empty grid.
        // Shown at most once: with a command-line override in play, Finish cannot change the path this run uses,
        // so a loop on the same condition would never end.
        if (!services.Settings.WelcomeDone || !SettingsStore.ValidateGamePath(services.GamePath, out _))
        {
            var welcome = new WelcomeWindow { DataContext = new WelcomeViewModel(services, dialogs), ShowActivated = !Quiet };
            if (welcome.ShowDialog() != true)
            {
                Shutdown(0);
                return;
            }
        }

        // Spec 3.5: an unchanged game starts from the cache; anything else re-reads while the window opens.
        if (!services.LevelData.LoadCached())
        {
            _ = services.LevelData.RefreshAsync();
        }

        var window = new MainWindow { DataContext = new MainViewModel(services, dialogs), ShowActivated = !Quiet };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
