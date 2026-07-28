using System.IO;
using Microsoft.VisualBasic.FileIO;
using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Bestimmt, wo die Ordner der Bereiche liegen.
///
/// Der Wert stand frueher fest im Programm auf „D:\Fences". Auf einem Rechner
/// ohne Laufwerk D: — etwa einem Notebook — scheiterte damit schon der erste
/// Start: keine Bereiche, kein Symbol im Infobereich, keine Meldung. Von aussen
/// sah es aus, als wuerde MSDesk gar nicht starten.
///
/// Deshalb wird der Ort jetzt hergeleitet: bevorzugt ein vorhandenes zweites
/// Laufwerk (dort liegen Arbeitsdaten ueblicherweise), sonst der Benutzerordner.
/// Beim Erststart laesst er sich ausserdem frei waehlen.
/// </summary>
public static class BaseFolderResolver
{
    /// Name des Ordners, unabhaengig vom Laufwerk.
    public const string FolderName = "Fences";

    /// <summary>
    /// Schlaegt einen Ablageort vor: das erste feste Laufwerk ausser dem
    /// Systemlaufwerk (dort liegen Arbeitsdaten meist), sonst der Benutzerordner.
    /// </summary>
    public static string Suggest()
    {
        try
        {
            var system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                         ?? @"C:\";

            // Mindestens 1 GB frei: sonst faellt die Wahl auf eine
            // Wiederherstellungs- oder Herstellerpartition, die zwar einen
            // Laufwerksbuchstaben hat, aber kein Platz fuer Arbeitsdaten ist.
            const long MindestPlatz = 1024L * 1024 * 1024;

            var datenlaufwerk = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady
                            && d.AvailableFreeSpace >= MindestPlatz)
                .Select(d => d.RootDirectory.FullName)
                .Where(r => !string.Equals(r, system, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (datenlaufwerk != null && Beschreibbar(datenlaufwerk))
                return Path.Combine(datenlaufwerk, FolderName);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BaseFolderResolver.Suggest");
        }

        return ImBenutzerordner();
    }

    /// Rueckfallebene: immer vorhanden und immer beschreibbar.
    public static string ImBenutzerordner()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), FolderName);

    /// <summary>
    /// Stellt sicher, dass der eingestellte Ordner benutzbar ist. Laesst er sich
    /// nicht anlegen (Laufwerk fehlt, keine Rechte), wird auf den Benutzerordner
    /// ausgewichen und das im Protokoll vermerkt.
    /// Rueckgabe: der tatsaechlich benutzbare Ordner.
    /// </summary>
    public static string EnsureUsable(string? gewuenscht)
    {
        var kandidat = string.IsNullOrWhiteSpace(gewuenscht) ? Suggest() : gewuenscht!;

        // Schnellpfad: gibt es den Ordner schon, ist nichts zu tun. Wichtig,
        // weil Anlegbar() eine Probe-Datei schreibt und wieder loescht — bei
        // jedem Start unnoetige Last, und in einem Cloud-Ordner loest das
        // ausserdem jedes Mal einen Abgleich aus.
        if (Directory.Exists(kandidat)) return kandidat;

        if (Anlegbar(kandidat)) return kandidat;

        var ausweich = ImBenutzerordner();
        StartupLog.Write($"Ordner der Bereiche nicht nutzbar: {kandidat} → weiche aus auf {ausweich}");

        if (Anlegbar(ausweich)) return ausweich;

        // Selbst das schlug fehl — dann bleibt nur der urspruengliche Wunsch,
        // damit sich der Anwender die Meldung ansehen kann.
        StartupLog.Write($"Auch der Ausweichordner ist nicht nutzbar: {ausweich}");
        return kandidat;
    }

    /// <summary>
    /// Zieht alle gespeicherten Pfade von einem alten Basisordner auf einen
    /// neuen um.
    ///
    /// Noetig, weil die Ordner der Tabs ABSOLUT in der Konfiguration stehen
    /// („D:\Fences\Support\Allgemein"). Nur den Basisordner zu aendern reicht
    /// deshalb nicht — auf einem Rechner ohne D: zeigten danach saemtliche Tabs
    /// ins Leere. Betroffen sind ausserdem beide Platz-Gedaechtnisse, die
    /// ebenfalls auf Tab-Ordner verweisen.
    ///
    /// Rueckgabe: Anzahl der umgeschriebenen Eintraege (0 = nichts zu tun).
    /// </summary>
    public static int Remap(AppConfig config, string altBase, string neuBase)
    {
        if (string.IsNullOrWhiteSpace(altBase) || string.IsNullOrWhiteSpace(neuBase)) return 0;

        // Erst den abschliessenden Trenner abschneiden, DANN vergleichen:
        // „D:\Fences" und „D:\Fences\" sind derselbe Ordner.
        var alt = altBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var neu = neuBase.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(alt, neu, StringComparison.OrdinalIgnoreCase)) return 0;

        var geaendert = 0;

        string? Umschreiben(string? pfad)
        {
            if (string.IsNullOrWhiteSpace(pfad)) return null;
            if (!pfad.StartsWith(alt + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(pfad, alt, StringComparison.OrdinalIgnoreCase)) return null;

            return neu + pfad[alt.Length..];
        }

        foreach (var fence in config.Fences)
            foreach (var tab in fence.Tabs)
            {
                var neuerPfad = Umschreiben(tab.FolderPath);
                if (neuerPfad == null) continue;
                tab.FolderPath = neuerPfad;
                geaendert++;
            }

        // Beide Platz-Gedaechtnisse zeigen ebenfalls auf Tab-Ordner. Bleiben sie
        // stehen, wandern eingesammelte Dateien wieder ins Nichts.
        geaendert += RemapWerte(config.Placements, Umschreiben);
        geaendert += RemapWerte(config.TargetPlacements, Umschreiben);

        return geaendert;
    }

    /// Ergebnis eines Ordnerwechsels. Fehler ist gesetzt, wenn Erfolg false ist.
    public sealed record Umzug(bool Erfolg, int Pfade, string? Fehler)
    {
        public static Umzug Fehlgeschlagen(string grund) => new(false, 0, grund);
    }

    /// <summary>
    /// Verlegt den Ordner der Bereiche samt Inhalt an einen neuen Ort und zieht
    /// alle gespeicherten Pfade mit.
    ///
    /// Verschoben wird EINTRAG FUER EINTRAG statt in einem Rutsch: so
    /// funktioniert es auch, wenn am Ziel schon etwas liegt — <see
    /// cref="FileSystem.MoveDirectory(string,string)"/> verlangt sonst einen
    /// leeren Zielordner und bricht mitten im Vorgang ab.
    /// </summary>
    public static Umzug MoveTo(AppConfig config, string? ziel)
    {
        if (string.IsNullOrWhiteSpace(ziel))
            return Umzug.Fehlgeschlagen("Es wurde kein Ordner angegeben.");

        var alt = (config.BaseFolder ?? "").TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string neu;
        try
        {
            neu = Path.GetFullPath(ziel.Trim()).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return Umzug.Fehlgeschlagen($"Das ist kein gültiger Pfad:\n{ziel}");
        }

        if (string.Equals(alt, neu, StringComparison.OrdinalIgnoreCase))
            return new Umzug(true, 0, null);

        // Ein Ziel INNERHALB des bisherigen Ordners wuerde sich beim Verschieben
        // selbst in die Quere kommen.
        if (alt.Length > 0
            && neu.StartsWith(alt + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Umzug.Fehlgeschlagen(
                "Der neue Ordner liegt innerhalb des bisherigen. Bitte einen Ordner außerhalb wählen.");

        if (!Anlegbar(neu))
            return Umzug.Fehlgeschlagen($"In diesen Ordner kann nicht geschrieben werden:\n{neu}");

        if (alt.Length > 0 && Directory.Exists(alt))
        {
            // ZUERST pruefen, DANN verschieben. Ein Eintrag, den es am Ziel
            // schon gibt, darf nicht stillschweigend uebersprungen werden:
            // anschliessend zeigten die Bereiche auf den fremden Inhalt am
            // Ziel, waehrend die echten Dateien unsichtbar am alten Ort
            // liegenblieben.
            var kollisionen = Kollisionen(alt, neu);
            if (kollisionen.Count > 0)
                return Umzug.Fehlgeschlagen(
                    "Im gewählten Ordner liegen bereits Einträge mit gleichem Namen:\n" +
                    string.Join(", ", kollisionen.Take(6)) +
                    (kollisionen.Count > 6 ? $" … ({kollisionen.Count} insgesamt)" : "") +
                    "\n\nBitte einen leeren Ordner wählen oder die vorhandenen Einträge vorher wegräumen.");

            var verschoben = new List<(string Von, string Nach)>();
            try
            {
                foreach (var ordner in Directory.GetDirectories(alt))
                {
                    var zielOrdner = Path.Combine(neu, Path.GetFileName(ordner));
                    FileSystem.MoveDirectory(ordner, zielOrdner);
                    verschoben.Add((ordner, zielOrdner));
                }

                foreach (var datei in Directory.GetFiles(alt))
                {
                    var zielDatei = Path.Combine(neu, Path.GetFileName(datei));
                    File.Move(datei, zielDatei);
                    verschoben.Add((datei, zielDatei));
                }
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "BaseFolderResolver.MoveTo");

                // Zurueckholen, was schon drueben ist. Ohne das laege ein Teil
                // am neuen und ein Teil am alten Ort, waehrend die Einstellungen
                // unveraendert auf den alten zeigen — die betroffenen Bereiche
                // waeren danach einfach leer.
                var zurueck = Ruecknahme(verschoben);

                var meldung = $"Der Inhalt konnte nicht vollständig verschoben werden:\n{ex.Message}";
                meldung += zurueck == verschoben.Count
                    ? "\n\nAlles Verschobene wurde zurückgeholt, es hat sich nichts geändert."
                    : $"\n\nAchtung: {verschoben.Count - zurueck} Eintrag/Einträge konnten nicht " +
                      $"zurückgeholt werden und liegen jetzt unter:\n{neu}";
                return Umzug.Fehlgeschlagen(meldung);
            }
        }

        var pfade = Remap(config, alt, neu);
        config.BaseFolder = neu;

        // Den leeren Rest wegraeumen. Beim Erststart legt EnsureUsable den
        // vorgeschlagenen Ordner bereits an; waehlt man im Assistenten einen
        // anderen, bliebe sonst ein leerer „Fences"-Ordner zurueck, den nie
        // jemand angefordert hat. Nur wenn er WIRKLICH leer ist.
        try
        {
            if (alt.Length > 0 && Directory.Exists(alt)
                && !Directory.EnumerateFileSystemEntries(alt).Any())
                Directory.Delete(alt);
        }
        catch (Exception)
        {
            // Bleibt er liegen, ist das kein Schaden.
        }

        StartupLog.Write($"Ordner der Bereiche verlegt: {alt} → {neu} ({pfade} Pfade angepasst)");
        return new Umzug(true, pfade, null);
    }

    /// Namen, die es unter <paramref name="alt"/> UND unter <paramref name="neu"/> gibt.
    private static List<string> Kollisionen(string alt, string neu)
    {
        var vorhanden = new List<string>();
        try
        {
            foreach (var eintrag in Directory.GetFileSystemEntries(alt))
            {
                var name = Path.GetFileName(eintrag);
                var ziel = Path.Combine(neu, name);
                if (Directory.Exists(ziel) || File.Exists(ziel)) vorhanden.Add(name);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BaseFolderResolver.Kollisionen");
        }
        return vorhanden;
    }

    /// Holt bereits verschobene Eintraege zurueck. Liefert die Anzahl der
    /// erfolgreichen Ruecknahmen — mehr als der beste Versuch ist hier nicht
    /// moeglich, deshalb wird das Ergebnis ausdruecklich zurueckgegeben.
    private static int Ruecknahme(List<(string Von, string Nach)> verschoben)
    {
        var geschafft = 0;
        foreach (var (von, nach) in verschoben)
        {
            try
            {
                if (Directory.Exists(nach)) FileSystem.MoveDirectory(nach, von);
                else if (File.Exists(nach)) File.Move(nach, von);
                else continue; // war gar nicht da → nichts zurueckzunehmen
                geschafft++;
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "BaseFolderResolver.Ruecknahme");
            }
        }
        return geschafft;
    }

    private static int RemapWerte(Dictionary<string, string> karte, Func<string?, string?> umschreiben)
    {
        // Erst sammeln, dann setzen: waehrend der Aufzaehlung darf die
        // Sammlung nicht veraendert werden.
        var neu = new List<KeyValuePair<string, string>>();
        foreach (var (schluessel, wert) in karte)
        {
            var ziel = umschreiben(wert);
            if (ziel != null) neu.Add(new(schluessel, ziel));
        }

        foreach (var (schluessel, wert) in neu) karte[schluessel] = wert;
        return neu.Count;
    }

    private static bool Anlegbar(string pfad)
    {
        try
        {
            Directory.CreateDirectory(pfad);
            return Beschreibbar(pfad);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool Beschreibbar(string ordner)
    {
        try
        {
            var probe = Path.Combine(ordner, $".msdesk-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
