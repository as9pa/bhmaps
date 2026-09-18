using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BhMaps.App.ViewModels;

namespace BhMaps.App.Views.Controls;

/// <summary>The status strip (3.0): one line saying what is running, what the last operation did, or why it did
/// nothing, with the links that line offers. It is bound to the shell's StatusViewModel, which is the only thing
/// that decides what it says. In the top bar from 3.1, which is why the going is timed here: a line that stays
/// in the bar is a line in the way, so the kinds that have finished speaking fade after six seconds and the view
/// model is told once they have. The pointer and the keyboard hold the clock, because a line being read is a line
/// that has not been read yet.</summary>
public partial class StatusStrip : UserControl
{
    private static readonly TimeSpan Life = TimeSpan.FromSeconds(6);
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(400));

    private readonly DispatcherTimer _timer;
    private StatusViewModel? _status;
    private bool _fading;

    public StatusStrip()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = Life };
        _timer.Tick += (_, _) => Fade();
        DataContextChanged += (_, e) => Follow(e.NewValue as StatusViewModel);
        MouseEnter += (_, _) => Restart();
        MouseLeave += (_, _) => Restart();
        IsKeyboardFocusWithinChanged += (_, _) => Restart();
        Unloaded += (_, _) => Stop();
    }

    private void Follow(StatusViewModel? status)
    {
        if (_status is not null)
        {
            _status.PropertyChanged -= OnStatusChanged;
        }

        _status = status;
        if (_status is not null)
        {
            _status.PropertyChanged += OnStatusChanged;
        }

        Restart();
    }

    /// <summary>A new line, a rewritten one, or one brought back by its dot: all three are the clock starting
    /// again from the top.</summary>
    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null
            or nameof(StatusViewModel.Kind)
            or nameof(StatusViewModel.Text)
            or nameof(StatusViewModel.Faded))
        {
            Restart();
        }
    }

    private void Restart()
    {
        Stop();
        if (_status is { Fades: true, Faded: false } && !IsMouseOver && !IsKeyboardFocusWithin)
        {
            _timer.Start();
        }
    }

    /// <summary>Back to a line at rest: no clock, no animation, and the words at full strength however far
    /// through a fade they were.</summary>
    private void Stop()
    {
        _timer.Stop();
        _fading = false;
        Line.BeginAnimation(OpacityProperty, null);
        Line.Opacity = 1;
    }

    private void Fade()
    {
        _timer.Stop();
        if (_status is null)
        {
            return;
        }

        // Whoever turned the animations off asked for things to happen rather than be shown happening.
        if (!SystemParameters.ClientAreaAnimation)
        {
            _status.Fade();
            return;
        }

        _fading = true;
        var fade = new DoubleAnimation(0, FadeOut);
        fade.Completed += (_, _) =>
        {
            // A line written, hovered or recalled while this ran took the animation off; the flag says whether
            // this is still the fade that was started.
            if (!_fading)
            {
                return;
            }

            _fading = false;
            _status?.Fade();
            Line.BeginAnimation(OpacityProperty, null);
            Line.Opacity = 1;
        };
        Line.BeginAnimation(OpacityProperty, fade);
    }
}
