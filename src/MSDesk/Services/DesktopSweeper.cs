using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace MSDesk.Services;

/// Sammelt (wenn aktiviert) Dateien und Ordner vom BENUTZER-Desktop automatisch ein:
/// bekannte Dateinamen wandern in ihren gelernten Bereich (PlacementRegistry),
/// alles andere in den Bereich "Ablage". Der oeffentliche Desktop bleibt unberuehrt.
public sealed class DesktopSweeper : IDisposable
{
    private readonly ConfigService _config;
    private readonly Func<string> _ablageFolderProvider;
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private readonly object _sync = new();

    public DesktopSweeper(ConfigService config, Func<string> ablageFolderProvider)
    {
        _config = config;
        _ablageFolderProvider = ablageFolderProvider;
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_watcher != null) return;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop)) return;

            _debounce = new System.Timers.Timer(4000) { AutoReset = false };
            // Nur solange die Dauerueberwachung eingeschaltet ist (schuetzt gegen
            // ein noch laufendes Debounce nach dem Abschalten).
            _debounce.Elapsed += (_, _) => { if (_config.Config.DesktopSweep) Sweep(); };

            _watcher = new FileSystemWatcher(desktop)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, _) => Restart();
            _watcher.Renamed += (_, _) => Restart();

            Restart(); // Erstlauf kurz nach dem Aktivieren
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _watcher?.Dispose();
            _watcher = null;
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    private void Restart()
    {
        lock (_sync)
        {
            _debounce?.Stop();
            _debounce?.Start();
        }
    }

    /// Manueller Anstoss (Refresh-Button): sammelt den Desktop EINMAL ein —
    /// unabhaengig davon, ob die Dauerueberwachung eingeschaltet ist.
    public void SweepNow() => Sweep();

    /// Sortiert INNERHALB der Ablage nach den Regeln um (Dateien und Ordner).
    ///
    /// WICHTIG: Quelle sind ausschliesslich die Tabs des Ablage-Bereichs. Andere
    /// Bereiche werden nie durchsucht — was der Nutzer dort bewusst einsortiert
    /// hat, darf der Aktualisieren-Knopf nicht wegziehen. (Als ZIEL duerfen
    /// Regeln weiterhin in jedem Bereich stehen.)
    public void ApplyRulesEverywhere()
    {
        try
        {
            var ablage = _config.Config.Fences
                .Where(f => string.Equals(f.Title, "Ablage", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var fence in ablage)
            {
                foreach (var tab in fence.Tabs.ToList())
                {
                    if (!Directory.Exists(tab.FolderPath)) continue;
                    // FileSystemInfos statt nur Files: sonst blieben Ordner und
                    // Ordner-Verknuepfungen von der Regel „ordner" unberuehrt.
                    foreach (var file in new DirectoryInfo(tab.FolderPath).EnumerateFileSystemInfos())
                    {
                        try
                        {
                            var target = LookupRuleFolder(file);
                            if (target == null) continue;
                            if (string.Equals(target, tab.FolderPath, StringComparison.OrdinalIgnoreCase)) continue;
                            MoveInto(file, target);
                        }
                        catch (Exception)
                        {
                            // gesperrt o. ae. → beim naechsten Refresh
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ApplyRulesEverywhere");
        }
    }

    private void Sweep()
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var ablage = _ablageFolderProvider();
            if (string.IsNullOrEmpty(ablage) || !Directory.Exists(ablage)) return;

            foreach (var entry in new DirectoryInfo(desktop).EnumerateFileSystemInfos())
            {
                try
                {
                    if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (string.Equals(entry.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    // Prioritaet: explizite Endungs-Regel → gelernter Platz → Ablage
                    var target = LookupRuleFolder(entry)
                                 ?? PlacementRegistry.Lookup(entry.Name)
                                 ?? ablage;
                    MoveInto(entry, target);
                }
                catch (Exception)
                {
                    // Datei gesperrt o. ae. → naechster Durchlauf versucht es erneut
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopSweeper");
        }
    }

    /// Tab mit passender Endungs-Regel (TabConfig.AutoExtensions), z. B. sza → HiCAD-Tab.
    private string? LookupRuleFolder(FileSystemInfo entry)
    {
        // Ordner und Ordner-Verknuepfungen: eigene Regel „ordner" (haben keine
        // gemeinsame Endung). Hat Vorrang, damit ein Ordner-Tab sie einsammelt,
        // bevor eine .lnk-Regel greifen wuerde.
        if (ShortcutFactory.PointsToFolder(entry.FullName))
        {
            foreach (var fence in _config.Config.Fences)
                foreach (var tab in fence.Tabs)
                    if (FileCategories.IsFolderRule(tab.AutoExtensions) && Directory.Exists(tab.FolderPath))
                        return tab.FolderPath;
        }

        if ((entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory) return null;
        var ext = Path.GetExtension(entry.Name).TrimStart('.').ToLowerInvariant();
        if (ext.Length == 0) return null;

        // Priorität 1: exakt zugewiesene Einzelendung (z. B. "pdf" gehoert dem PDF-Tab,
        // nicht dem Office-Tab, obwohl "office" pdf mit abdeckt).
        foreach (var fence in _config.Config.Fences)
            foreach (var tab in fence.Tabs)
                if (FileCategories.MatchesExact(tab.AutoExtensions, ext) && Directory.Exists(tab.FolderPath))
                    return tab.FolderPath;

        // Priorität 2: Kategorie-Regel (z. B. "office", "bilder").
        foreach (var fence in _config.Config.Fences)
            foreach (var tab in fence.Tabs)
                if (FileCategories.MatchesCategory(tab.AutoExtensions, ext) && Directory.Exists(tab.FolderPath))
                    return tab.FolderPath;

        return null;
    }

    private static void MoveInto(FileSystemInfo entry, string targetDir)
    {
        var dest = Path.Combine(targetDir, entry.Name);
        var isDir = (entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

        if (File.Exists(dest) || Directory.Exists(dest))
        {
            if (!isDir && FilesEqual(entry.FullName, dest))
            {
                // Inhaltsgleiches Duplikat (typisch nach Programm-Update):
                // Desktop-Kopie in den Papierkorb, Bereichs-Icon bleibt an seinem Platz.
                FileSystem.DeleteFile(entry.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                return;
            }
            // Gleicher Name, anderer Inhalt (neue Version): alte Datei in den
            // Papierkorb, neue uebernimmt Name UND Platz in der Anordnung.
            if (!isDir && File.Exists(dest))
                FileSystem.DeleteFile(dest, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else if (Directory.Exists(dest))
            {
                var n = 2;
                var stem = entry.Name;
                while (Directory.Exists(dest) || File.Exists(dest))
                    dest = Path.Combine(targetDir, $"{stem} ({n++})");
            }
        }

        if (isDir) Directory.Move(entry.FullName, dest);
        else File.Move(entry.FullName, dest);
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length) return false;
            if (infoA.Length > 4 * 1024 * 1024) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose() => Stop();
}
