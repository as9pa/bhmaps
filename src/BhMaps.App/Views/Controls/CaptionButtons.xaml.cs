using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>3.9: minimize, maximize and close for a window drawn under WindowChrome. The buttons act on the window
/// the control sits in; minimize and maximize are hidden where that window's ResizeMode has no such button, and
/// the maximize glyph turns into restore while the window is maximized.</summary>
public partial class CaptionButtons : UserControl
{
    private Window? _window;

    public CaptionButtons()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        if (window is null || ReferenceEquals(window, _window))
        {
            return;
        }

        Detach();
        _window = window;
        _window.StateChanged += OnStateChanged;
        var mode = _window.ResizeMode;
        MinimizeButton.Visibility = mode == ResizeMode.NoResize ? Visibility.Collapsed : Visibility.Visible;
        MaxRestoreButton.Visibility =
            mode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip ? Visibility.Visible : Visibility.Collapsed;
        OnStateChanged(_window, EventArgs.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Detach()
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnStateChanged;
            _window = null;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var maximized = _window?.WindowState == WindowState.Maximized;
        MaxRestoreButton.Content = maximized ? "" : "";
        AutomationProperties.SetName(MaxRestoreButton, maximized ? "Restore" : "Maximize");
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.MinimizeWindow(_window);
        }
    }

    private void OnMaxRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.CloseWindow(_window);
        }
    }
}
