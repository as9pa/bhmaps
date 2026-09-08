using System.Windows;

namespace BhMaps.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string initial)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.SelectAll();
            ValueBox.Focus();
        };
    }

    public string Value => ValueBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
