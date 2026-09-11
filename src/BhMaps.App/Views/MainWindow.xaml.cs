using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BhMaps.App.ViewModels;
using BhMaps.App.ViewModels.Pages;

namespace BhMaps.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;
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

    /// <summary>Spec 2.1: Ctrl+K reaches the Maps search box from any tab. Navigation is a command, but focus is
    /// not: the page's view does not exist until the ContentControl has built it, so the focus call is queued
    /// behind that at Input priority.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.K || Keyboard.Modifiers != ModifierKeys.Control || DataContext is not MainViewModel vm)
        {
            return;
        }

        vm.NavigateMapsCommand.Execute(null);
        e.Handled = true;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (FindByTag(PageHost, "SearchBox") is TextBox box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            });
    }

    /// <summary>Owner change O5: Ctrl+A ticks every shown map from anywhere on the Maps page. The page declares
    /// the shortcut itself, but until something on the page takes keyboard focus the window is what holds it, and
    /// a page's KeyBinding never sees a key routed from the window above it, so the window hands that one on.
    /// KeyDown rather than PreviewKeyDown: the grid's own Ctrl+A and the search box's both run first and mark the
    /// key handled, so what reaches here is only what nothing else wanted.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled
            || e.Key != Key.A
            || Keyboard.Modifiers != ModifierKeys.Control
            || DataContext is not MainViewModel { CurrentPage: MapsViewModel maps })
        {
            return;
        }

        maps.SelectAllShownCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>The page's search box, by its Tag, through the visual tree: the page is a DataTemplate's content,
    /// so the window's own name scope cannot see it, and the box cannot carry an x:Name at all, because it sits
    /// inside the PageHeader user control's own name scope.</summary>
    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && Equals(element.Tag, tag))
            {
                return element;
            }

            if (FindByTag(child, tag) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
