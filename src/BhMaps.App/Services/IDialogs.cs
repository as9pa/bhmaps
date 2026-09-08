using BhMaps.Core.Model;

namespace BhMaps.App.Services;

/// <summary>Everything a view model needs from the UI that blocks for the user.</summary>
public interface IDialogs
{
    /// <summary>OK/Cancel question. True on OK.</summary>
    bool Confirm(string title, string message);

    void Error(string title, string message);

    void Info(string title, string message);

    /// <summary>Lists failed paths and errors. Does nothing when the list is empty.</summary>
    void ShowFailures(string title, IReadOnlyList<FileFailure> failures);

    string? PickFolder(string title);

    string? PickImageFile(string title);

    /// <summary>Single-line text prompt. Null when cancelled.</summary>
    string? PromptText(string title, string message, string initial);
}
