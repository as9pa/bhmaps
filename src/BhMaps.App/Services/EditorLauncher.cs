using System.Runtime.InteropServices;

namespace BhMaps.App.Services;

/// <summary>Hands a file to a program of the user's choosing through the Windows "Open with" dialog, because
/// the PNG default on most PCs is a viewer with no edit verb (spec 6). Calls SHOpenWithDialog directly rather
/// than the "openas" verb, which Windows 11 does not register for PNGs owned by a Store app such as Photos, so
/// ShellExecute fails there with "No application is associated". The dialog launches the picked app and never
/// offers to change the user's default. Null when the dialog opened or the user cancelled it, otherwise why it
/// did not open.</summary>
public static class EditorLauncher
{
    private const int OAIF_EXEC = 0x4;
    private const int OAIF_HIDE_REGISTRATION = 0x20;
    private const int S_OK = 0;
    private const int E_CANCELLED = unchecked((int)0x800704C7);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENASINFO
    {
        public string pcszFile;
        public string? pcszClass;
        public int oaifInFlags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OPENASINFO poainfo);

    /// <summary>The dialog is modal and blocks its thread until the user picks, so it runs on its own STA thread
    /// and the UI stays responsive.</summary>
    public static Task<string?> OpenWithAsync(string path, IntPtr owner = default)
    {
        if (!File.Exists(path))
        {
            return Task.FromResult<string?>($"File does not exist: {path}");
        }

        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var info = new OPENASINFO { pcszFile = path, oaifInFlags = OAIF_EXEC | OAIF_HIDE_REGISTRATION };
                var hr = SHOpenWithDialog(owner, ref info);
                done.SetResult(hr is S_OK or E_CANCELLED
                    ? null
                    : Marshal.GetExceptionForHR(hr)?.Message ?? $"Could not open the Open with dialog (0x{hr:X8}).");
            }
            catch (Exception ex)
            {
                done.SetResult(ex.Message);
            }
        })
        {
            IsBackground = true,
            Name = "Open with dialog",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
