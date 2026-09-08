using System.Windows;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (DataContext is MainViewModel vm)
        {
            await vm.RescanAsync();
        }
    }
}
