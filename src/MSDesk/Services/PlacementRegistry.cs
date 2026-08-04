using System.IO;

namespace MSDesk.Services;

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

        var geaendert = false;

        var placements = _config.Config.Placements;
        if (!placements.TryGetValue(name, out var known)
            || !string.Equals(known, tabFolder, StringComparison.OrdinalIgnoreCase))
        {
            placements[name] = tabFolder;
            geaendert = true;
        }

        // Zusaetzlich das Ziel merken: Programm-Updates benennen Verknuepfungen
        // haeufig um, das Ziel bleibt aber dasselbe.
        var ziel = ZielSchluessel(filePath);
        if (ziel != null)
        {
            var ziele = _config.Config.TargetPlacements;
            if (!ziele.TryGetValue(ziel, out var bekannt)
                || !string.Equals(bekannt, tabFolder, StringComparison.OrdinalIgnoreCase))
            {
                ziele[ziel] = tabFolder;
                geaendert = true;
            }
        }

        if (geaendert) _config.SaveDebounced();
    }

    /// <summary>
    /// Schluessel fuer das Ziel-Gedaechtnis: der Name der Programmdatei, auf die
    /// eine Verknuepfung zeigt (klein geschrieben). null, wenn es keine
    /// Verknuepfung ist oder das Ziel nicht ermittelbar war.
    ///
    /// Bewusst nur der Dateiname und nicht der ganze Pfad: nach einem Update
    /// liegt das Programm oft in einem Ordner mit neuer Versionsnummer.
    /// </summary>
    public static string? ZielSchluessel(string filePath)
    {
        // Nur Verknuepfungen haben ein Ziel — alles andere gar nicht erst anfassen.
        if (!filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;

        // Zwischenspeicher: Das Aufloesen laeuft ueber COM und kostet einige
        // Millisekunden. Ohne Zwischenspeicher waeren das bei jedem Anzeigen
        // eines Tabs hunderte Aufrufe — sichtbar als Ruckeln.
        lock (ZielCacheSync)
        {
            if (ZielCache.TryGetValue(filePath, out var bekannt)) return bekannt;
        }

        string? schluessel = null;
        try
        {
            var angaben = ShortcutFactory.ResolveLnk(filePath);
            if (angaben != null && !string.IsNullOrWhiteSpace(angaben.Ziel))
            {
                var datei = Path.GetFileName(angaben.Ziel).ToLowerInvariant();
                if (datei.Length > 0)
                {
                    // Die ARGUMENTE gehoeren zwingend in den Schluessel.
                    //
                    // Ohne sie galt jede Verknuepfung auf dasselbe Programm als
                    // dieselbe Sache: zwei Arbeitsmappen ueber excel.exe, zwei
                    // Server ueber mstsc.exe, zwei Anwendungen ueber chrome.exe.
                    // MSDesk hat sie fuer Doppelte gehalten und eine davon
                    // entfernt — ein echter Verlust, kein Aufraeumen.
                    var args = angaben.Argumente.ToLowerInvariant();
                    schluessel = args.Length > 0 ? datei + "|" + args : datei;
                }
            }
        }
        catch (Exception)
        {
            // nicht aufloesbar → als „kein Ziel" merken, nicht erneut versuchen
        }

        lock (ZielCacheSync)
        {
            if (ZielCache.Count > 2000) ZielCache.Clear(); // Notbremse
            ZielCache[filePath] = schluessel;
        }
        return schluessel;
    }

    private static readonly Dictionary<string, string?> ZielCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ZielCacheSync = new();

    /// Vergisst zwischengespeicherte Ziele (nach dem Verschieben von Dateien).
    public static void ClearTargetCache()
    {
        lock (ZielCacheSync) ZielCache.Clear();
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
                    {
                        found.Add((file, folder));

                        // Ziel schon hier im HINTERGRUND aufloesen und in den
                        // Zwischenspeicher legen. Sonst liefen die COM-Aufrufe
                        // gleich darauf auf dem Oberflaechen-Thread und wuerden
                        // den Start sichtbar verzoegern.
                        ZielSchluessel(file);
                    }

                    // Ordner ebenfalls merken — bewusst EINSTUFIG (kein
                    // AllDirectories): gemerkt wird der Ordner selbst, nicht
                    // sein Inhalt. Ein Verknuepfungsziel gibt es hier nicht,
                    // deshalb kein ZielSchluessel-Aufruf.
                    foreach (var dir in Directory.EnumerateDirectories(folder))
                        found.Add((dir, folder));
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

    /// <summary>
    /// Gelernter Tab-Ordner fuer einen vollstaendigen Pfad — erst ueber den
    /// Dateinamen, dann ueber das Verknuepfungsziel. Der zweite Weg greift,
    /// wenn ein Programm-Update die Verknuepfung umbenannt hat.
    /// </summary>
    public static string? LookupByPath(string path)
    {
        if (_config == null) return null;

        var ueberNamen = Lookup(Path.GetFileName(path.TrimEnd('\\', '/')));
        if (ueberNamen != null) return ueberNamen;

        var ziel = ZielSchluessel(path);
        if (ziel == null) return null;

        return _config.Config.TargetPlacements.TryGetValue(ziel, out var ordner)
               && Directory.Exists(ordner)
            ? ordner
            : null;
    }
}
