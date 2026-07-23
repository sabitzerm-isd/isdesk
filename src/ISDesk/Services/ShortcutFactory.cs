using System.Runtime.InteropServices;

namespace ISDesk.Services;

public static class ShortcutFactory
{
    /// Legt eine .lnk-Verknuepfung via WScript.Shell (COM) an.
    public static void CreateLnk(string lnkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell == null) return;

        try
        {
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = target;
            shortcut.Save();
            Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
