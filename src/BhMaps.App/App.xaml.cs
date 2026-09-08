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
        var args = CommandLine.Parse(e.Args);
        var services = new AppServices(args.AppData ?? SettingsStore.DefaultAppDataDir, args.Game, args.Library);

        if (!SettingsStore.ValidateGamePath(services.GamePath, out var error))
        {
            MessageBox.Show(
                $"{error}\n\nEdit {services.SettingsPath} or start with --game <path>.",
                "BhMaps",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var window = new MainWindow { DataContext = new MainViewModel(services) };
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }
}
