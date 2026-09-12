using System.Windows;
using System.Windows.Controls;

namespace BhMaps.App.Views.Controls;

/// <summary>The 56 px header every page docks at its top (spec 7.1): the page title on the left, the page's own
/// actions on the right, and the shell's progress line or done line with Undo between them. The missing-folder
/// notice is the top bar's (spec 2.1).</summary>
public partial class PageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    /// <summary>Anything the page wants before its title, such as pack detail's back button (spec 5).</summary>
    public static readonly DependencyProperty LeadingProperty =
        DependencyProperty.Register(nameof(Leading), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null));

    public PageHeader()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Leading
    {
        get => GetValue(LeadingProperty);
        set => SetValue(LeadingProperty, value);
    }

    /// <summary>The page's own action buttons, shown at the right end of the header.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
