using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel vm)
        {
            await vm.RescanAsync();
        }
    }

    /// <summary>App.xaml.cs adds its own Closed handler to shut the application down; both run.</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        if (DataContext is MainViewModel vm)
        {
            vm.Shutdown();
        }
    }
}
