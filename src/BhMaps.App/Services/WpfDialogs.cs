using System.Windows;
using BhMaps.App.Views;
using BhMaps.Core.Model;
using Microsoft.Win32;

namespace BhMaps.App.Services;

public sealed class WpfDialogs : IDialogs
{
    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public void Error(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void Info(string title, string message) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowFailures(string title, IReadOnlyList<FileFailure> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var window = new ErrorSummaryWindow(title, failures) { Owner = Owner };
        window.ShowDialog();
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public string? PickImageFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*",
        };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public string? PromptText(string title, string message, string initial)
    {
        var window = new TextPromptWindow(title, message, initial) { Owner = Owner };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private static bool ShowDialog(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
