using System.IO;

namespace MSDesk.Services;

/// Legt den Inhalt aller Bereiche zurueck auf den Desktop. Wird gebraucht, wenn
/// MSDesk deinstalliert oder nicht mehr genutzt wird — sonst blieben die
/// Verknuepfungen unsichtbar in den Bereichs-Ordnern liegen.
public static class DesktopRestore
{
    /// Kommandozeilen-Schalter, mit dem der Uninstaller die Rueckgabe anstoesst.
    public const string CommandLineSwitch = "--icons-auf-desktop";

    private static string Desktop
        => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// Anzahl der Dateien, die zurueckgelegt wuerden (ohne virtuelle Systemobjekte).
    public static int Count(AppConfigSource config)
    {
        var count = 0;
        foreach (var folder in TabFolders(config))
        {
            try
            {
                if (Directory.Exists(folder)) count += Directory.GetFiles(folder).Length;
            }
            catch (Exception) { /* Ordner nicht lesbar */ }
        }
        return count;
    }

    /// Verschiebt alle Dateien aus den Bereichen auf den Desktop.
    /// Rueckgabe: (verschoben, fehlgeschlagen).
    public static (int Moved, int Failed) RestoreAll(AppConfigSource config)
    {
        int moved = 0, failed = 0;
        var desktop = Desktop;

        foreach (var folder in TabFolders(config))
        {
            if (!Directory.Exists(folder)) continue;

            string[] files;
            try { files = Directory.GetFiles(folder); }
            catch (Exception) { continue; }

            foreach (var file in files)
            {
                try
                {
                    var target = UniqueTarget(desktop, Path.GetFileName(file));
                    File.Move(file, target);
                    moved++;
                }
                catch (Exception)
                {
                    failed++;
                }
            }
        }
        return (moved, failed);
    }

    private static IEnumerable<string> TabFolders(AppConfigSource config)
        => config.Fences
            .SelectMany(f => f.Tabs)
            .Select(t => t.FolderPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string UniqueTarget(string folder, string name)
    {
        var target = Path.Combine(folder, name);
        if (!File.Exists(target) && !Directory.Exists(target)) return target;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var n = 2;
        do { target = Path.Combine(folder, $"{stem} ({n++}){ext}"); }
        while (File.Exists(target) || Directory.Exists(target));
        return target;
    }
}

/// Schmale Sicht auf die Konfiguration — haelt <see cref="DesktopRestore"/> testbar.
public sealed record AppConfigSource(IReadOnlyList<Models.FenceConfig> Fences)
{
    public static AppConfigSource From(ConfigService config) => new(config.Config.Fences);
}
