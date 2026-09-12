using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BhMaps.App.Services;

namespace BhMaps.App.Behaviors;

/// <summary>Spec 10: one eased wheel behaviour for every list. A notch of the wheel moves three 48 px lines, and
/// the offset glides there over 180 ms ease-out instead of jumping, so a row page whose rows are 200 px tall no
/// longer lurches a whole row per notch. Only the wheel is touched: a touchpad already sends fine deltas (its
/// delta is not a multiple of 120) and is passed through, and the scroll bar, the keys and touch never reach
/// here. Ctrl and the wheel is zoom, so that combination is left for whoever asked for it.</summary>
public static class SmoothScroll
{
    /// <summary>A notch moves three lines of 48 px (spec 10).</summary>
    private const double LinesPerNotch = 3;

    private const double LineHeight = 48;

    /// <summary>One notch of a wheel, in <see cref="MouseWheelEventArgs.Delta" /> units.</summary>
    private const int Notch = 120;

    /// <summary>180 ms (spec 10).</summary>
    private const double Duration = 180;

    /// <summary>On a ScrollViewer: its wheel scrolling eases.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The glide a ScrollViewer is in the middle of, if it is in one. Weak, so a page that has gone
    /// away takes its state with it; the table is the only thing that knows about a ScrollViewer between two
    /// notches.</summary>
    private static readonly ConditionalWeakTable<ScrollViewer, Glide> Glides = new();

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        viewer.Unloaded -= OnUnloaded;
        Stop(viewer);
        if ((bool)e.NewValue)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
            viewer.Unloaded += OnUnloaded;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (!AppServices.AnimationsEnabled
            || e.Delta == 0
            || e.Delta % Notch != 0
            || (Keyboard.Modifiers & ModifierKeys.Control) != 0
            || viewer.ScrollableHeight <= 0)
        {
            return;
        }

        // Where the last notch was headed, or where the content sits when nothing is running. A second notch
        // during a glide moves that target further rather than queueing a glide of its own.
        var running = Glides.TryGetValue(viewer, out var glide) && glide.Tick is not null;
        var end = running ? glide!.Target : viewer.VerticalOffset;
        if ((e.Delta < 0 && end >= viewer.ScrollableHeight) || (e.Delta > 0 && end <= 0))
        {
            // Already at that end: leave the event alone so it bubbles to an outer scroller, as WPF does.
            return;
        }

        e.Handled = true;
        glide = Glides.GetOrCreateValue(viewer);
        glide.From = viewer.VerticalOffset;
        glide.Target = Math.Clamp(
            end + (-e.Delta / (double)Notch * LinesPerNotch * LineHeight), 0, viewer.ScrollableHeight);
        glide.Start = Stopwatch.GetTimestamp();
        if (glide.Tick is null)
        {
            glide.Tick = (_, _) => OnRendering(viewer, glide);
            CompositionTarget.Rendering += glide.Tick;
        }
    }

    /// <summary>Ease-out over 180 ms from wherever the content was when the notch arrived.</summary>
    private static void OnRendering(ScrollViewer viewer, Glide glide)
    {
        var t = Math.Clamp(Stopwatch.GetElapsedTime(glide.Start).TotalMilliseconds / Duration, 0, 1);
        var eased = 1 - ((1 - t) * (1 - t));
        viewer.ScrollToVerticalOffset(glide.From + ((glide.Target - glide.From) * eased));
        if (t >= 1)
        {
            Stop(viewer);
        }
    }

    /// <summary>A page that scrolls away mid-glide would otherwise leave its handler on
    /// <see cref="CompositionTarget.Rendering" />, which fires for the whole application.</summary>
    private static void OnUnloaded(object sender, RoutedEventArgs e) => Stop((ScrollViewer)sender);

    private static void Stop(ScrollViewer viewer)
    {
        if (!Glides.TryGetValue(viewer, out var glide) || glide.Tick is null)
        {
            return;
        }

        CompositionTarget.Rendering -= glide.Tick;
        glide.Tick = null;
    }

    /// <summary>Where a glide started, where it is going, and when it began. A handler that is not null is the
    /// one sign that the glide is running.</summary>
    private sealed class Glide
    {
        public double From { get; set; }

        public double Target { get; set; }

        public long Start { get; set; }

        public EventHandler? Tick { get; set; }
    }
}
