using System.Windows;
using System.Windows.Threading;
using BhMaps.App.ViewModels;
using BhMaps.App.Views;
using BhMaps.Core.Model;
using Microsoft.Win32;

namespace BhMaps.App.Services;

public sealed class WpfDialogs : IDialogs
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp|All files|*.*";

    /// <summary>The app's own dialog window that is up right now, if any. See ShowModal.</summary>
    private static Window? _open;

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;

    public bool Confirm(string title, string message, string primary, bool destructive = false) =>
        ShowModal(
            new DialogWindow(new DialogViewModel(DialogKind.Confirm, title, message, Primary: primary, IsDestructive: destructive)))
        == true;

    public void Error(string title, string message) => Show(DialogKind.Error, title, message);

    public void Info(string title, string message) => Show(DialogKind.Info, title, message);

    public void ShowFailures(string title, IReadOnlyList<FileFailure> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }

        ShowModal(new ErrorSummaryWindow(title, failures));
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
        var window = new TextPromptWindow(title, message, initial);
        return ShowModal(window) == true ? window.Value : null;
    }

    private static bool ShowDialog(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    /// <summary>The app's own window in place of MessageBox for the two one-button kinds. Called on the UI
    /// thread, as the MessageBox calls it replaces were; neither kind has anything to read back.</summary>
    private static void Show(DialogKind kind, string title, string message) =>
        ShowModal(new DialogWindow(new DialogViewModel(kind, title, message)));

    /// <summary>Owns, centres and shows one of the app's dialog windows, one at a time. A second dialog can be
    /// asked for while one is up: the game-running poll and a task that finishes late both land back on the UI
    /// thread with news of their own. Two ShowDialog calls there would stack two modals over the shell, so the
    /// second waits on a nested message loop until the first closes and only then shows.</summary>
    private static bool? ShowModal(Window window)
    {
        while (_open is { } open)
        {
            var frame = new DispatcherFrame();
            void Continue(object? sender, EventArgs e) => frame.Continue = false;
            open.Closed += Continue;
            Dispatcher.PushFrame(frame);
            open.Closed -= Continue;
        }

        window.Owner = Owner;
        window.ShowActivated = !App.Quiet;
        if (window.Owner is null)
        {
            // CenterOwner has nothing to centre on before the shell exists, as at a start-up error.
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _open = window;

        // Cleared on Closed rather than only after ShowDialog returns: Closed fires first, and a request made from
        // a Closed handler would otherwise wait on a window that has already gone, in a frame nothing continues.
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, window))
            {
                _open = null;
            }
        };
        try
        {
            return window.ShowDialog();
        }
        finally
        {
            if (ReferenceEquals(_open, window))
            {
                _open = null;
            }
        }
    }
}
