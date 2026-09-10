using System.Windows;
using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>The compact zoom control at the right end of a page's chip row (spec 7.2): a snapped slider over the
/// theme's ZoomSliderStyle with the column count beside it. Whole columns, so every value is an int.</summary>
public partial class ZoomSlider : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(int),
            typeof(ZoomSlider),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(int), typeof(ZoomSlider), new PropertyMetadata(0));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(int), typeof(ZoomSlider), new PropertyMetadata(10));

    public ZoomSlider()
    {
        InitializeComponent();
    }

    /// <summary>Two way by default, because the only reason this control exists is to write a setting back.</summary>
    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }
}
