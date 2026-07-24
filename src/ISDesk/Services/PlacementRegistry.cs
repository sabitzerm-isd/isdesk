using System.IO;

namespace ISDesk.Services;

/// Merkt sich, welche Datei in welchem Tab-Ordner liegt (lernt bei jedem Reload).
/// Der DesktopSweeper nutzt das, um z. B. nach einem Programm-Update die neu
/// angelegte Desktop-Verknuepfung automatisch in ihren Bereich zurueckzulegen.
public static class PlacementRegistry
{
    private static ConfigService? _config;

    public static void Init(ConfigService config) => _config = config;

    public static void Learn(string filePath, string tabFolder)
    {
        if (_config == null) return;
        var name = Path.GetFileName(filePath.TrimEnd('\\', '/')).ToLowerInvariant();
        if (name.Length == 0) return;

        var placements = _config.Config.Placements;
        if (placements.TryGetValue(name, out var known)
            && string.Equals(known, tabFolder, StringComparison.OrdinalIgnoreCase))
            return;

        placements[name] = tabFolder;
        _config.SaveDebounced();
    }

    /// Liest einmalig ALLE Tab-Ordner ein und merkt sich, welche Datei wo liegt.
    /// Noetig, seit Tabs erst beim Anzeigen geladen werden (frueher lernte jeder
    /// Reload mit): laeuft im Hintergrund, ohne Icons und ohne Ueberwachung.
    public static void LearnAllTabFolders()
    {
        var config = _config;
        if (config == null) return;

        var folders = config.Config.Fences
            .SelectMany(f => f.Tabs)
            .Select(t => t.FolderPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Task.Run(() =>
        {
            var found = new List<(string File, string Folder)>();
            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in Directory.EnumerateFiles(folder))
                        found.Add((file, folder));
                }
                catch (Exception)
                {
                    // Ordner gerade nicht lesbar → beim naechsten Start erneut
                }
            }

            // Uebernahme auf dem UI-Thread: die Konfiguration wird nur dort veraendert.
            void Apply()
            {
                foreach (var (file, folder) in found) Learn(file, folder);
            }

            var app = Application.Current;
            if (app != null) app.Dispatcher.BeginInvoke(Apply);
            else Apply();
        });
    }

    /// Gelernter Tab-Ordner fuer einen Dateinamen (null, wenn unbekannt oder Ordner weg).
    public static string? Lookup(string fileName)
    {
        if (_config == null) return null;
        if (!_config.Config.Placements.TryGetValue(fileName.ToLowerInvariant(), out var folder))
            return null;
        return Directory.Exists(folder) ? folder : null;
    }
}
