using System.Windows;
using BhMaps.Core.Model;

namespace BhMaps.App.Views;

public partial class ErrorSummaryWindow : Window
{
    public ErrorSummaryWindow(string title, IReadOnlyList<FileFailure> failures)
    {
        InitializeComponent();
        Title = title;
        DataContext = new Model($"{failures.Count} file(s) failed. Everything else completed.", failures);
    }

    public sealed record Model(string Message, IReadOnlyList<FileFailure> Failures);
}
