using System.IO;
using System.Runtime.InteropServices;

namespace MSDesk.Services;

public static class ShortcutFactory
{
    /// Ziel einer .lnk-Verknuepfung (null, wenn keine Verknuepfung oder nicht lesbar).
    public static string? ResolveLnkTarget(string lnkPath)
    {
        if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return null;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            Marshal.FinalReleaseComObject(shortcut);
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
    }

    /// True, wenn der Eintrag ein Ordner IST oder eine Verknuepfung auf einen
    /// Ordner ist — fuer die Ablage-Regel „ordner".
    public static bool PointsToFolder(string path)
    {
        try
        {
            if (Directory.Exists(path)) return true;
            var target = ResolveLnkTarget(path);
            return target != null && Directory.Exists(target);
        }
        catch (Exception)
        {
            return false;
        }
    }

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
