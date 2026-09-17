using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BhMaps.Core.Settings;

namespace BhMaps.App.Views.Controls;

/// <summary>3.0: the three-stop size picker that replaces the zoom slider (wireframe 8.2). A slider asked the
/// owner to choose a number for something they judge by eye; this asks for large, medium or small, and the grid
/// behind it justifies itself to whichever they pick. The glyphs carry the meaning, so there is no label: a 2x2,
/// a 3x3 and a 4x4 grid in the order they shrink.</summary>
public partial class SizePicker : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(TileSize),
            typeof(SizePicker),
            new FrameworkPropertyMetadata(
                TileSize.Medium, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public SizePicker()
    {
        InitializeComponent();
        Sync();
    }

    /// <summary>Two way by default, because the only reason this control exists is to write a setting back.</summary>
    public TileSize Value
    {
        get => (TileSize)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SizePicker)d).Sync();

    private void OnSegmentChecked(object sender, RoutedEventArgs e)
    {
        if (Size(sender) is { } size)
        {
            Value = size;
        }
    }

    private void OnSegmentUnchecked(object sender, RoutedEventArgs e)
    {
        // A segmented control has no off. Clicking the size already chosen, or pressing Space on it, leaves it
        // chosen rather than emptying the row.
        if (sender is ToggleButton toggle && Size(sender) == Value)
        {
            toggle.IsChecked = true;
        }
    }

    /// <summary>Which size a segment stands for, off the Tag the XAML gives it.</summary>
    private static TileSize? Size(object sender) =>
        sender is ToggleButton { Tag: string name } && Enum.TryParse<TileSize>(name, out var size) ? size : null;

    /// <summary>Puts the pill on the segment <see cref="Value"/> names, whether the change came from a click here
    /// or from the keyboard shortcut on the page.</summary>
    private void Sync()
    {
        LargeSegment.IsChecked = Value == TileSize.Large;
        MediumSegment.IsChecked = Value == TileSize.Medium;
        SmallSegment.IsChecked = Value == TileSize.Small;
    }
}
