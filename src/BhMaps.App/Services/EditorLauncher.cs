using System.ComponentModel;
using System.Diagnostics;

namespace BhMaps.App.Services;

/// <summary>Hands a file to a program of the user's choosing through the Windows "Open with" dialog, because
/// the PNG default on most PCs is a viewer with no edit verb (spec 6). Null when the dialog opened, otherwise
/// why it did not.</summary>
public static class EditorLauncher
{
    public static string? OpenWith(string path)
    {
        if (!File.Exists(path))
        {
            return $"File does not exist: {path}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "openas" })?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
