namespace BhMaps.App.ViewModels;

/// <summary>Which of the three IDialogs message dialogs DialogWindow is showing. The kind is the only thing
/// that differs between them: it picks the title's colour and whether there is a Cancel.</summary>
public enum DialogKind
{
    Info,
    Error,
    Confirm,
}

/// <summary>What DialogWindow shows. Nothing on it changes while the dialog is up, so it is a plain record
/// rather than an ObservableObject and the window binds to it once.</summary>
/// <param name="Detail">A quieter line under the message. Empty, the default, takes its line back.</param>
public sealed record DialogViewModel(DialogKind Kind, string Title, string Message, string Detail = "")
{
    /// <summary>Missing is the app's one coloured state (D8) and an error is the same kind of news.</summary>
    public bool IsError => Kind == DialogKind.Error;

    /// <summary>Only Confirm has a way out that is not OK.</summary>
    public bool HasCancel => Kind == DialogKind.Confirm;
}
