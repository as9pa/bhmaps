using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BhMaps.App.Services;

namespace BhMaps.App.Behaviors;

/// <summary>Spec 4.1: the one fold animation. Four things fold after 2.2, and all of them time the same: 160 ms,
/// ease-out, a caret turning 90 degrees or the switch's plus becoming a minus, and content that appears fading in
/// as it slides 6 px up. It runs from code rather than from storyboards in the theme because the durations have to
/// fall to zero when Windows' own animation switch is off, and a Duration inside a style's storyboard is frozen
/// where a binding could have answered.</summary>
public static class Fold
{
    /// <summary>How far content that appears slides up, in pixels.</summary>
    private const double Slide = 6;

    /// <summary>Whether the fold this element belongs to is open. Inherited, so a header sets it once and the
    /// caret or glyph inside its template is told without a binding of its own.</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.RegisterAttached(
        "IsOpen",
        typeof(bool),
        typeof(Fold),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnIsOpenChanged));

    /// <summary>On the panel a fold shows: it fades in and slides up when it appears.</summary>
    public static readonly DependencyProperty AnimateProperty = DependencyProperty.RegisterAttached(
        "Animate", typeof(bool), typeof(Fold), new PropertyMetadata(false, OnAnimateChanged));

    /// <summary>On a caret: it turns 0 to 90 degrees with <see cref="IsOpenProperty" />.</summary>
    public static readonly DependencyProperty ChevronProperty = DependencyProperty.RegisterAttached(
        "Chevron", typeof(bool), typeof(Fold), new PropertyMetadata(false));

    /// <summary>On the upright bar of a plus: it scales to nothing with <see cref="IsOpenProperty" />, which is
    /// what turns the plus into a minus.</summary>
    public static readonly DependencyProperty UprightProperty = DependencyProperty.RegisterAttached(
        "Upright", typeof(bool), typeof(Fold), new PropertyMetadata(false));

    /// <summary>160 ms (spec 4.1).</summary>
    private static readonly Duration Time = new(TimeSpan.FromMilliseconds(160));

    private static readonly Duration Instant = new(TimeSpan.Zero);

    private static readonly IEasingFunction Ease = EaseOut();

    public static bool GetIsOpen(DependencyObject element) => (bool)element.GetValue(IsOpenProperty);

    public static void SetIsOpen(DependencyObject element, bool value) => element.SetValue(IsOpenProperty, value);

    public static bool GetAnimate(DependencyObject element) => (bool)element.GetValue(AnimateProperty);

    public static void SetAnimate(DependencyObject element, bool value) => element.SetValue(AnimateProperty, value);

    public static bool GetChevron(DependencyObject element) => (bool)element.GetValue(ChevronProperty);

    public static void SetChevron(DependencyObject element, bool value) => element.SetValue(ChevronProperty, value);

    public static bool GetUpright(DependencyObject element) => (bool)element.GetValue(UprightProperty);

    public static void SetUpright(DependencyObject element, bool value) => element.SetValue(UprightProperty, value);

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var open = (bool)e.NewValue;
        if (GetChevron(element))
        {
            Own<RotateTransform>(element).BeginAnimation(RotateTransform.AngleProperty, Move(open ? 90 : 0, element));
        }

        if (GetUpright(element))
        {
            Own<ScaleTransform>(element).BeginAnimation(ScaleTransform.ScaleYProperty, Move(open ? 0 : 1, element));
        }

        if (open && GetAnimate(element))
        {
            FadeIn(element);
        }
    }

    private static void OnAnimateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;
        if ((bool)e.NewValue)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;
        }
    }

    /// <summary>A panel that says for itself when its fold is open is animated by that; one that is simply shown
    /// and hidden, which is how the three older folds are written, is animated by appearing.</summary>
    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement element
            && (bool)e.NewValue
            && element.ReadLocalValue(IsOpenProperty) == DependencyProperty.UnsetValue)
        {
            FadeIn(element);
        }
    }

    /// <summary>Opacity 0 to 1 and 6 px up, once. Nothing runs before the element has loaded: a row realised by a
    /// scroll is not a fold opening, and it is the fold opening that this is for.</summary>
    private static void FadeIn(FrameworkElement element)
    {
        if (!element.IsLoaded || !AppServices.AnimationsEnabled)
        {
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Time) { EasingFunction = Ease });
        Own<TranslateTransform>(element).BeginAnimation(
            TranslateTransform.YProperty, new DoubleAnimation(Slide, 0, Time) { EasingFunction = Ease });
    }

    /// <summary>A transform of the element's own. One set from a style is frozen and shared by every element that
    /// style reaches, so animating it would either throw or move all of them.</summary>
    private static T Own<T>(FrameworkElement element)
        where T : Transform, new()
    {
        if (element.RenderTransform is T mine && !mine.IsFrozen)
        {
            return mine;
        }

        var fresh = new T();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = fresh;
        return fresh;
    }

    /// <summary>Instant while the element is still loading, so a caret whose fold is already open is simply drawn
    /// open, and instant when Windows' animations are off (spec 4.1).</summary>
    private static DoubleAnimation Move(double to, FrameworkElement element) =>
        new(to, element.IsLoaded && AppServices.AnimationsEnabled ? Time : Instant) { EasingFunction = Ease };

    private static IEasingFunction EaseOut()
    {
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        ease.Freeze();
        return ease;
    }
}
