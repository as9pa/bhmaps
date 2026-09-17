using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What the strip is saying, which is also what it is coloured by (3.0). Idle is the strip away.</summary>
public enum StatusKind
{
    Idle,
    Running,
    Done,
    Undone,
    Error,
    Cancelled,
    Note,
}

/// <summary>The one line the shell says everything through (3.0): the operation running now, what the last one
/// did, or why it did nothing. It never goes away on its own, because a line that fades is a line the user has to
/// catch: the next operation writes over it, and the close button puts it away.</summary>
public partial class StatusViewModel : ObservableObject
{
    private readonly Action _cancel;
    private readonly Func<Task> _undo;

    public StatusViewModel(Action cancel, Func<Task> undo)
    {
        _cancel = cancel;
        _undo = undo;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(CanCancel), nameof(CanDismiss), nameof(CanUndo), nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(UndoCommand), nameof(RetryCommand))]
    public partial StatusKind Kind { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Whether there is a snapshot behind the line the last write left, which is the only case where Undo
    /// puts back what the line is about. False for a library-only line, and the strip offers no Undo beside it
    /// rather than offering to undo an older write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool Undoable { get; set; }

    /// <summary>What Retry runs, or null when the failure is not one we can offer to run again. Observable so the
    /// link appears and goes with it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial Func<Task>? RetryAction { get; set; }

    public bool IsVisible => Kind != StatusKind.Idle;

    public bool CanCancel => Kind == StatusKind.Running;

    /// <summary>A running line has Cancel, and closing it would only hide what the app is still doing; every
    /// other line can be put away.</summary>
    public bool CanDismiss => Kind is not (StatusKind.Idle or StatusKind.Running);

    public bool CanUndo => Kind is StatusKind.Done or StatusKind.Cancelled && Undoable;

    public bool CanRetry => Kind == StatusKind.Error && RetryAction is not null;

    public void Running(string text) => Set(StatusKind.Running, text, undoable: false, retry: null);

    public void Done(string text, bool undoable) => Set(StatusKind.Done, text, undoable, retry: null);

    public void Undone(string text) => Set(StatusKind.Undone, text, undoable: false, retry: null);

    public void Error(string text, Func<Task>? retry) => Set(StatusKind.Error, text, undoable: false, retry);

    public void Cancelled(string text, bool undoable) => Set(StatusKind.Cancelled, text, undoable, retry: null);

    public void Note(string text) => Set(StatusKind.Note, text, undoable: false, retry: null);

    public void Clear() => Set(StatusKind.Idle, "", undoable: false, retry: null);

    /// <summary>The whole line, for the busy boundary: an operation that runs inside another one (the rescan every
    /// write ends with) takes the strip for its own progress and puts this back when nothing wrote over it.</summary>
    public StatusState Capture() => new(Kind, Text, Undoable, RetryAction);

    public void Restore(StatusState state) => Set(state.Kind, state.Text, state.Undoable, state.Retry);

    private void Set(StatusKind kind, string text, bool undoable, Func<Task>? retry)
    {
        Text = text;
        Undoable = undoable;
        RetryAction = retry;
        Kind = kind;
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancel();

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private Task UndoAsync() => _undo();

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => RetryAction?.Invoke() ?? Task.CompletedTask;

    /// <summary>The × at the end: the line goes, nothing it described is touched.</summary>
    [RelayCommand]
    private void Dismiss() => Clear();
}

/// <summary>A line the strip was showing, held while something else borrows the strip.</summary>
public readonly record struct StatusState(StatusKind Kind, string Text, bool Undoable, Func<Task>? Retry);
