using System.ComponentModel;
using System.Diagnostics;

namespace BhMaps.App.Services;

/// <summary>The one way the app shows a folder, so every page guards and reports it the same. What a caller does
/// with the reason it failed is the caller's: a page with a row for it shows it inline, the rest use a dialog.</summary>
public static class ExplorerLauncher
{
    /// <summary>Opens <paramref name="path"/> in explorer. Returns why it could not be opened, or null when it
    /// was. Explorer opens the user's Documents folder when handed a path that is not there, which looks like the
    /// button doing nothing, so a missing folder is caught first.</summary>
    public static string? Open(string path)
    {
        if (!Directory.Exists(path))
        {
            return $"Folder does not exist: {path}";
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
