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
        var services = new AppServices(parsed.AppData ?? SettingsStore.DefaultAppDataDir, parsed.Game, parsed.Library);
        var dialogs = new WpfDialogs();

        if (!SettingsStore.ValidateGamePath(services.GamePath, out var error))
        {
            dialogs.Error("BhMaps", $"{error}\n\nEdit {services.SettingsPath} or start with --game <path>.");
            Shutdown();
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(services, dialogs) };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
