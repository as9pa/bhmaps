using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.Views.Controls;

/// <summary>The search box with an autocomplete popup (decision D12): a text box, a popup holding the
/// suggestions, and the four keys that drive them. Everything else about it is markup and bindings.</summary>
public partial class SuggestBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SuggestBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    public static readonly DependencyProperty IsDismissedProperty =
        DependencyProperty.Register(nameof(IsDismissed), typeof(bool), typeof(SuggestBox), new PropertyMetadata(false));

    public static readonly DependencyProperty SuggestionsProperty =
        DependencyProperty.Register(nameof(Suggestions), typeof(IEnumerable), typeof(SuggestBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SuggestBox), new PropertyMetadata(""));

    public static readonly DependencyProperty ChooseCommandProperty =
        DependencyProperty.Register(nameof(ChooseCommand), typeof(ICommand), typeof(SuggestBox), new PropertyMetadata(null));

    public SuggestBox()
    {
        // Before InitializeComponent, because the popup's list binds to it while the XAML is being parsed.
        ChooseItem = new RelayCommand<object?>(Choose);
        InitializeComponent();
    }

    /// <summary>What a click on a suggestion runs, so the mouse takes the same path as Enter and the popup shuts
    /// behind it. The caller's <see cref="ChooseCommand"/> is what actually does the choosing.</summary>
    public ICommand ChooseItem { get; }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>What the popup lists. The popup stays shut while this is empty.</summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Grey text shown while the box is empty.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Run with one suggestion as its parameter when Enter picks it or the mouse clicks it.</summary>
    public ICommand? ChooseCommand
    {
        get => (ICommand?)GetValue(ChooseCommandProperty);
        set => SetValue(ChooseCommandProperty, value);
    }

    /// <summary>Whether Escape or Enter has shut the popup on the text as it now stands. The popup's style reads
    /// it, and the next keystroke clears it, which is what lets the popup come back after Escape.</summary>
    public bool IsDismissed
    {
        get => (bool)GetValue(IsDismissedProperty);
        set => SetValue(IsDismissedProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SuggestBox)d).IsDismissed = false;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when SuggestList.HasItems:
                SuggestList.SelectedIndex = Math.Min(SuggestList.SelectedIndex + 1, SuggestList.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up when SuggestList.HasItems:
                SuggestList.SelectedIndex = Math.Max(SuggestList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Enter when SuggestList.HasItems:
                Choose(SuggestList.SelectedItem ?? SuggestList.Items[0]);
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void Choose(object? suggestion)
    {
        if (suggestion is not null && ChooseCommand is { } command && command.CanExecute(suggestion))
        {
            command.Execute(suggestion);
        }

        // After the command, because choosing rewrites the text, and rewriting the text un-dismisses the popup.
        Close();
    }

    private void Close() => IsDismissed = true;
}
