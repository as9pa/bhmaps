using BhMaps.Core.Model;

namespace BhMaps.App.Services;

/// <summary>Everything a view model needs from the UI that blocks for the user.</summary>
public interface IDialogs
{
    /// <summary>A question with a way out. True when the user pressed the primary button, whose label
    /// <paramref name="primary" /> is: the verb of the thing about to happen, never "OK". A
    /// <paramref name="destructive" /> primary is the one that deletes or removes something.</summary>
    bool Confirm(string title, string message, string primary, bool destructive = false);

    void Error(string title, string message);

    void Info(string title, string message);

    /// <summary>Lists failed paths and errors. Does nothing when the list is empty.</summary>
    void ShowFailures(string title, IReadOnlyList<FileFailure> failures);

    string? PickFolder(string title);

    /// <summary>Multi-select folder picker. Null when cancelled.</summary>
    IReadOnlyList<string>? PickFolders(string title);

    string? PickImageFile(string title);

    /// <summary>Multi-select image picker. Null when cancelled.</summary>
    IReadOnlyList<string>? PickImageFiles(string title);

    /// <summary>Single-line text prompt. Null when cancelled.</summary>
    string? PromptText(string title, string message, string initial);
}
