using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BhMaps.App.Behaviors;

/// <summary>A press anywhere on a slider's groove moves the thumb there and goes on dragging it while the button
/// is held. Move-to-point alone jumps the thumb and then leaves it, so the pointer has to be let go and put back
/// on the 12 px thumb before the value can be dragged, which is not what a press on a groove means anywhere else
/// in Windows. The press is handed to the thumb once the value is set, and from there WPF's own drag does the
/// rest, snapping to ticks where the slider asks for it.</summary>
public static class SliderDrag
{
    /// <summary>On a Slider: a press on its groove starts a drag.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SliderDrag), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider)
        {
            return;
        }

        slider.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, (MouseButtonEventHandler)OnPreviewMouseLeftButtonDown);
        if ((bool)e.NewValue)
        {
            // Handled events too: Slider's own class handler runs before any handler of ours and, where
            // move-to-point is on, it sets the value and marks the press handled. We want the press whatever it
            // did with it, because only the thumb drag it never starts is missing.
            slider.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                (MouseButtonEventHandler)OnPreviewMouseLeftButtonDown,
                handledEventsToo: true);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.Template?.FindName("PART_Track", slider) is not Track { Thumb: { } thumb } track
            || IsOn(e.OriginalSource, thumb))
        {
            // A press on the thumb itself already drags, and a slider whose template is something else is left
            // to behave the way its own template says.
            return;
        }

        // The slider's coercion is what rounds to a tick and keeps the value inside the range, so the value is
        // simply assigned. Move-to-point, where it is on, has set the same value from the same point already,
        // which is why the press raises ValueChanged once rather than twice.
        var value = Math.Clamp(track.ValueFromPoint(e.GetPosition(track)), slider.Minimum, slider.Maximum);
        if (double.IsFinite(value))
        {
            slider.Value = value;
        }

        // Setting the value only asks for a new arrange; the thumb is still drawn where it was until layout runs.
        // The thumb measures every drag from where the pointer was over it at the press, so if it took the press
        // now it would remember an origin a whole groove away and every move after that would land the same
        // distance from the pointer. Laying out first puts the thumb under the pointer before it looks.
        slider.UpdateLayout();

        // The thumb is under the pointer now, so the press is given to it and the drag runs as if it had begun
        // there: WPF captures the mouse and every move until the button goes up is its own.
        thumb.RaiseEvent(new MouseButtonEventArgs(e.MouseDevice, e.Timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = thumb,
        });
        e.Handled = true;
    }

    /// <summary>Whether the press landed on the thumb or on something inside its template, which is what a
    /// mouse event names as its source.</summary>
    private static bool IsOn(object source, Thumb thumb) =>
        source is Visual visual && (ReferenceEquals(visual, thumb) || thumb.IsAncestorOf(visual));
}
