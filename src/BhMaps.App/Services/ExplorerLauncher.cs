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

    /// <summary>Opens a https url in whatever the machine's browser is. Returns the reason it could not, or null.
    /// Anything that is not an absolute https uri is refused rather than handed to the shell, because what the
    /// shell does with a string that is not a url is open whatever program claims it.</summary>
    public static string? OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"Not a https address: {url}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }

    /// <summary>Opens the file's folder with the file selected, which is what "Show in folder" means. Null when it
    /// worked, otherwise why it did not. A missing file is caught first, because explorer handed a path that is
    /// not there opens Documents instead, which looks like the menu item doing nothing.</summary>
    public static string? Reveal(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return $"File does not exist: {filePath}";
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true })
                ?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
