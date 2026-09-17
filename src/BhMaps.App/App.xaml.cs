using System.Net.Http;
using System.Reflection;
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

        // Spec 7.1: one HttpClient for the life of the app, created here because only the App knows its own
        // version for the User-Agent. No timeout on the client itself: UpdateClient puts 10 s on the check, and a
        // 135 MB download must not be cut off by the check's limit. No credential of any kind is ever set.
        var version = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BhMaps/{version.Major}.{version.Minor}.{version.Build}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var services = new AppServices(
            parsed.AppData ?? SettingsStore.DefaultAppDataDir, parsed.Game, parsed.Library, http);
        Exit += (_, _) => services.Dispose();
        var dialogs = new WpfDialogs();

        // Spec 5: one sweep a run keeps the preview folder from growing without bound.
        services.Previews.Sweep(DateTimeOffset.UtcNow);

        // Spec 7.7: the welcome window is the first run, and it also stands in for the v1 settings loop, so a
        // saved game path that has stopped working comes back here rather than to a crash or an empty grid.
        // Shown at most once: with a command-line override in play, Finish cannot change the path this run uses,
        // so a loop on the same condition would never end.
        WelcomeCapture? captured = null;
        var startOnPacks = false;
        if (!services.Settings.WelcomeDone || !SettingsStore.ValidateGamePath(services.GamePath, out _))
        {
            var welcomeModel = new WelcomeViewModel(services, dialogs);
            var welcome = new WelcomeWindow { DataContext = welcomeModel, ShowActivated = !Quiet };
            if (welcome.ShowDialog() != true)
            {
                Shutdown(0);
                return;
            }

            // 3.0: step 3 answered No leaves a library with no pack in it, so the shell opens where the offer to
            // capture one is: the Packs page's empty state, with the first-run line above it.
            captured = welcomeModel.Capture;
            startOnPacks = captured is null;
        }

        // Spec 3.5: an unchanged game starts from the cache; anything else re-reads while the window opens.
        if (!services.LevelData.LoadCached())
        {
            _ = services.LevelData.RefreshAsync();
        }

        var model = new MainViewModel(services, dialogs);

        // 3.0: a capture that ran before the shell existed still says what it did, on the strip the rest of the
        // app says everything through. No Retry beside a failure: the welcome is over, and Settings offers the
        // capture again.
        if (captured is { } outcome)
        {
            if (outcome.Failed)
            {
                model.Status.Error(outcome.Text, retry: null);
            }
            else
            {
                model.Status.Done(outcome.Text, undoable: false);
            }
        }
        else if (startOnPacks)
        {
            model.NavigatePacks();
        }

        var window = new MainWindow { DataContext = model, ShowActivated = !Quiet };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
