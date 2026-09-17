using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BhMaps.App.Views.Controls;

/// <summary>The app's one empty state (3.0): a sentence saying why there is nothing here, and at most one action
/// that fills it. Every page with nothing to show uses this, so they all say it in the same place and shape.</summary>
public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null));

    public EmptyState()
    {
        InitializeComponent();
    }

    /// <summary>The one sentence. It wraps, and it is the whole of what the state says.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The action's label, which is its verb. Empty, the default, leaves the state with no button.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }
}
