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
/// did, or why it did nothing. In the top bar from 3.1, where a line that stays is a line in the way: a note, a
/// done, an undone and a cancelled line fade after their few seconds, a running line stays while the work does,
/// and an error stays until the × puts it away. Fading is the view's business; what the view model holds is
/// whether the line has faded, because that is what decides whether the dot is still standing for it.</summary>
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
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(ShowLine), nameof(Remembered), nameof(Fades), nameof(CanCancel), nameof(CanDismiss), nameof(CanUndo), nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand), nameof(UndoCommand), nameof(RetryCommand))]
    public partial StatusKind Kind { get; set; }

    /// <summary>Whether the line has had its few seconds and gone. The dot outlives it when there is still
    /// something to undo, so this is not the same thing as the strip being away.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(ShowLine), nameof(Remembered))]
    public partial bool Faded { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>Whether there is a snapshot behind the line the last write left, which is the only case where Undo
    /// puts back what the line is about. False for a library-only line, and the strip offers no Undo beside it
    /// rather than offering to undo an older write.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible), nameof(Remembered), nameof(CanUndo))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool Undoable { get; set; }

    /// <summary>What Retry runs, or null when the failure is not one we can offer to run again. Observable so the
    /// link appears and goes with it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial Func<Task>? RetryAction { get; set; }

    /// <summary>The strip is there for a line that has not faded, and for the dot left standing after one that
    /// has: a faded line with nothing to undo takes the whole strip with it.</summary>
    public bool IsVisible => Kind != StatusKind.Idle && (!Faded || CanUndo);

    /// <summary>The words and the links, which is the strip minus the dot.</summary>
    public bool ShowLine => IsVisible && !Faded;

    /// <summary>Whether this line goes on its own. Running stays while the work does and an error stays until it
    /// is put away; everything else has said its piece in a few seconds.</summary>
    public bool Fades => Kind is StatusKind.Note or StatusKind.Done or StatusKind.Undone or StatusKind.Cancelled;

    /// <summary>The dot on its own, after the line faded and with the undo still behind it: the one thing left
    /// saying the last write can be taken back, and clicking it says what it was.</summary>
    public bool Remembered => Faded && CanUndo;

    public bool CanCancel => Kind == StatusKind.Running;

    /// <summary>Only a failure is put away by hand (3.1). Every other line goes on its own, and a running one
    /// has Cancel: an × beside them would be a button for something that was about to happen anyway.</summary>
    public bool CanDismiss => Kind == StatusKind.Error;

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

    /// <summary>The view saying the line has finished fading. Only the view knows when that is, because only the
    /// view knows whether the pointer was resting on it.</summary>
    public void Fade() => Faded = true;

    private void Set(StatusKind kind, string text, bool undoable, Func<Task>? retry)
    {
        Faded = false;
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

    /// <summary>The dot left standing after a faded line, clicked: the line it was about comes back with its
    /// Undo, and it has its few seconds again.</summary>
    [RelayCommand]
    private void Recall() => Faded = false;
}

/// <summary>A line the strip was showing, held while something else borrows the strip.</summary>
public readonly record struct StatusState(StatusKind Kind, string Text, bool Undoable, Func<Task>? Retry);
