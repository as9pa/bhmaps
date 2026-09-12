using System.Windows;
using BhMaps.App.ViewModels;
using BhMaps.App.Views;
using BhMaps.Core.Model;
using Microsoft.Win32;

namespace BhMaps.App.Services;

public sealed class WpfDialogs : IDialogs
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*";

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message) => Show(DialogKind.Confirm, title, message);

    public void Error(string title, string message) => Show(DialogKind.Error, title, message);

    public void Info(string title, string message) => Show(DialogKind.Info, title, message);

    public void ShowFailures(string title, IReadOnlyList<FileFailure> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        var window = new ErrorSummaryWindow(title, failures) { Owner = Owner, ShowActivated = !App.Quiet };
        window.ShowDialog();
    }

    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return ShowDialog(dialog) ? dialog.FolderName : null;
    }

    public IReadOnlyList<string>? PickFolders(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = true };
        return ShowDialog(dialog) ? dialog.FolderNames : null;
    }

    public string? PickImageFile(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = ImageFilter };
        return ShowDialog(dialog) ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? PickImageFiles(string title)
    {
        var dialog = new OpenFileDialog { Title = title, Multiselect = true, Filter = ImageFilter };
        return ShowDialog(dialog) ? dialog.FileNames : null;
    }

    public string? PromptText(string title, string message, string initial)
    {
        var window = new TextPromptWindow(title, message, initial) { Owner = Owner, ShowActivated = !App.Quiet };
        return window.ShowDialog() == true ? window.Value : null;
    }

    private static bool ShowDialog(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    /// <summary>The app's own window in place of MessageBox for the three message kinds. Called on the UI
    /// thread, as the MessageBox calls it replaces were. True only when the user pressed OK, which is what
    /// Confirm returns; the one-button kinds have nothing to read.</summary>
    private static bool Show(DialogKind kind, string title, string message)
    {
        var window = new DialogWindow(new DialogViewModel(kind, title, message)) { Owner = Owner, ShowActivated = !App.Quiet };
        if (window.Owner is null)
        {
            // CenterOwner has nothing to centre on before the shell exists, as at a start-up error.
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true;
    }
}
