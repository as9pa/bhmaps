using System.Windows;
using System.Windows.Input;
using BhMaps.App.ViewModels;
using BhMaps.Core.Settings;

namespace BhMaps.App.Behaviors;

/// <summary>3.0: Ctrl and plus, minus or zero, and Ctrl and the wheel, over a page that draws tiles. The old
/// zoom slider had arrow keys of its own once it was focused; a size picker has three stops, so the shortcut has
/// to reach the page rather than the control. Attached to the page root with the page's own view model as the
/// target, so every sized page gets the same two gestures from one place.</summary>
public static class SizeShortcuts
{
    /// <summary>On a page root: the view model whose size these gestures change. Null unhooks them.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(ITileSized), typeof(SizeShortcuts), new PropertyMetadata(null, OnTargetChanged));

    public static ITileSized? GetTarget(DependencyObject element) => (ITileSized?)element.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject element, ITileSized? value) => element.SetValue(TargetProperty, value);

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewKeyDown -= OnKeyDown;
        element.PreviewMouseWheel -= OnWheel;

        if (e.NewValue is ITileSized)
        {
            element.PreviewKeyDown += OnKeyDown;
            element.PreviewMouseWheel += OnWheel;
        }
    }

    private static void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || GetTarget((DependencyObject)sender) is not { } target)
        {
            return;
        }

        // The key a keyboard sends for + and - depends on whether it came from the number row or the pad, and
        // Ctrl and 0 is the "back to normal" every browser has.
        var size = e.Key switch
        {
            Key.OemPlus or Key.Add => Larger(target.TileSize),
            Key.OemMinus or Key.Subtract => Smaller(target.TileSize),
            Key.D0 or Key.NumPad0 => TileSize.Medium,
            _ => (TileSize?)null,
        };

        if (size is { } chosen)
        {
            target.TileSize = chosen;
            e.Handled = true;
        }
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control
            || e.Delta == 0
            || GetTarget((DependencyObject)sender) is not { } target)
        {
            return;
        }

        // Handled, or the list scrolls a notch as well as resizing. SmoothScroll leaves Ctrl and the wheel alone
        // for exactly this, but it sits on the ScrollViewer inside the page and this preview runs first anyway.
        target.TileSize = e.Delta > 0 ? Larger(target.TileSize) : Smaller(target.TileSize);
        e.Handled = true;
    }

    /// <summary>One step towards Large, and no further: there is no wrap round from the biggest to the smallest.</summary>
    private static TileSize Larger(TileSize size) => size switch
    {
        TileSize.Small => TileSize.Medium,
        _ => TileSize.Large,
    };

    private static TileSize Smaller(TileSize size) => size switch
    {
        TileSize.Large => TileSize.Medium,
        _ => TileSize.Small,
    };
}
