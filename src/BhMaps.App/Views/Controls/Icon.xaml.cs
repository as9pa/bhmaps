using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace BhMaps.App.Views.Controls;

/// <summary>One line icon from the design canvas: a geometry out of Theme/Icons.xaml, drawn at <see cref="Size"/>
/// in the Foreground it inherits from the button or row it sits in, so hover, the current page and disabled all
/// reach it without the icon ever naming a colour of its own.</summary>
public partial class Icon : UserControl
{
    /// <summary>The side of the box the canvas draws every icon in, which is its SVG viewBox.</summary>
    private const double ViewBox = 24;

    /// <summary>What the sidebar's rows and the panel's Close wear; a page button asks for 15.</summary>
    private const double DefaultSize = 16;

    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(Icon),
            new PropertyMetadata(DefaultSize, OnSizeChanged));

    private static readonly DependencyPropertyKey ScalePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Scale),
            typeof(double),
            typeof(Icon),
            new PropertyMetadata(DefaultSize / ViewBox));

    public static readonly DependencyProperty ScaleProperty = ScalePropertyKey.DependencyProperty;

    /// <summary>The icon a themed button draws before its label. Attached rather than set as the button's content,
    /// so every call site keeps its string Content and with it the accessible name a screen reader reads.</summary>
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public Icon()
    {
        InitializeComponent();
    }

    /// <summary>The 24 unit path this icon draws, from a Theme/Icons.xaml key.</summary>
    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    /// <summary>The side of the square the icon is drawn in, in device independent pixels.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>What the 24 unit drawing is multiplied by to come out at <see cref="Size"/>. Read only: it is
    /// Size over the viewBox, and the template binds it rather than working it out again.</summary>
    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        private set => SetValue(ScalePropertyKey, value);
    }

    /// <summary>None: an icon repeats what the button it sits in is already named, so a UserControl peer here
    /// would only put an unnamed element in the automation tree for a screen reader to walk past.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => null!;

    public static Geometry? GetGlyph(DependencyObject element) => (Geometry?)element.GetValue(GlyphProperty);

    public static void SetGlyph(DependencyObject element, Geometry? value) => element.SetValue(GlyphProperty, value);

    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Icon)d).Scale = (double)e.NewValue / ViewBox;
}
