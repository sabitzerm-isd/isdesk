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

    /// Gelernter Tab-Ordner fuer einen Dateinamen (null, wenn unbekannt oder Ordner weg).
    public static string? Lookup(string fileName)
    {
        if (_config == null) return null;
        if (!_config.Config.Placements.TryGetValue(fileName.ToLowerInvariant(), out var folder))
            return null;
        return Directory.Exists(folder) ? folder : null;
    }
}
