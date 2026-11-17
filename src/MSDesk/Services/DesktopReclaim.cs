using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace MSDesk.Services;

/// <summary>
/// Holt Verknuepfungen, die wieder auf dem Desktop gelandet sind, zurueck an
/// ihren gewohnten Platz — und raeumt dabei Doppelte weg.
///
/// Anlass: Programm-Updates legen ihre Desktop-Verknuepfung neu an. Sie liegt
/// dann lose auf dem Desktop, obwohl dasselbe Programm laengst in einem Bereich
/// einsortiert ist. Oft aendert das Update auch den Namen
/// („Camtasia 2024" → „Camtasia 2025"), sodass anschliessend zwei Eintraege
/// desselben Programms existieren.
///
/// Anders als der Desktop-Einsammler arbeitet dieser Vorgang GEZIELT: Er fasst
/// ausschliesslich Eintraege an, deren Platz bekannt ist. Alles Uebrige bleibt
/// unberuehrt auf dem Desktop liegen.
/// </summary>
public static class DesktopReclaim
{
    /// <summary>
    /// Was ein Durchlauf bewirkt hat.
    /// <paramref name="Zurueckgeholt"/> = an den gemerkten Platz gelegt,
    /// <paramref name="Ersetzt"/> = eine aeltere Verknuepfung desselben
    /// Programms an deren Stelle abgeloest,
    /// <paramref name="Gesperrt"/> = liegt auf dem Desktop FUER ALLE BENUTZER
    /// und laesst sich ohne Administratorrechte nicht anfassen.
    /// </summary>
    public sealed record Ergebnis(int Zurueckgeholt, int Ersetzt, int Fehlgeschlagen,
                                  IReadOnlyList<string> Gesperrt, int Doppelte = 0)
    {
        public int Gesamt => Zurueckgeholt + Ersetzt;
        public static Ergebnis Leer => new(0, 0, 0, Array.Empty<string>());
    }

    /// <summary>
    /// Durchsucht den Desktop und ordnet bekannte Eintraege wieder ein.
    /// <paramref name="nurVorschau"/> = nichts verschieben, nur zaehlen.
    /// </summary>
    public static Ergebnis Run(ConfigService config, bool nurVorschau = false,
                               bool nurVerknuepfungen = false)
    {
        if (nurVorschau) return RunIntern(config, true, nurVerknuepfungen);

        // Von Hand wie Automatik: kurz warten, dann aufgeben — ein gleichzeitiger
        // zweiter Lauf hat sich frueher mit dem ersten in die Quere gebracht.
        if (!Einsammeln.Betreten(15000)) return Ergebnis.Leer;
        try { return RunIntern(config, false, nurVerknuepfungen); }
        finally { Einsammeln.Verlassen(); }
    }

    private static Ergebnis RunIntern(ConfigService config, bool nurVorschau, bool nurVerknuepfungen)
    {
        int zurueck = 0, ersetzt = 0, fehler = 0;
        var gesperrt = new List<string>();

        try
        {
            // BEIDE Desktops durchsehen. Installationen „fuer alle Benutzer"
            // legen ihre Verknuepfung in den oeffentlichen Desktop — dort lag
            // z. B. Camtasia, weshalb sie nie gefunden wurde.
            var eigener = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var oeffentlich = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            var quellen = new List<string>();
            if (Directory.Exists(eigener)) quellen.Add(eigener);
            if (Directory.Exists(oeffentlich)
                && !string.Equals(oeffentlich, eigener, StringComparison.OrdinalIgnoreCase))
                quellen.Add(oeffentlich);

            if (quellen.Count == 0) return Ergebnis.Leer;

            // Was liegt bereits in den Bereichen? Ziel → Datei, um Doppelte zu erkennen.
            var vorhanden = BestandNachZiel(config);

            foreach (var eintrag in quellen.SelectMany(q => new DirectoryInfo(q).EnumerateFileSystemInfos()))
            {
                try
                {
                    if ((eintrag.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (string.Equals(eintrag.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    // Die Automatik fasst NUR Verknuepfungen an. Ohne diese Grenze
                    // wanderte bei ausgeschalteter Ablage trotzdem jeder neue
                    // Ordner und jedes Archiv (.zip, .sza, .dwg …) nach wenigen
                    // Sekunden vom Desktop — ueber die Endungs-Regeln der Ablage
                    // und das Platz-Gedaechtnis fuer gleichnamige Ordner.
                    if (nurVerknuepfungen && !IstVerknuepfung(eintrag)) continue;

                    var ziel = PlacementRegistry.LookupByPath(eintrag.FullName)
                               ?? (nurVerknuepfungen ? null : RegelOrdner(config, eintrag));
                    var zielSchluessel = PlacementRegistry.ZielSchluessel(eintrag.FullName);

                    // Liegt dasselbe Programm schon in einem Bereich?
                    var bereitsDa = zielSchluessel != null
                                    && vorhanden.TryGetValue(zielSchluessel, out var alt) ? alt : null;

                    // Gleicher Schluessel heisst nur: gleicher Programmname und
                    // gleiche Argumente. Eine ZWEITE Installation (andere
                    // HiCAD-Fassung, andere start.bat) hat denselben Schluessel,
                    // ist aber etwas anderes — sie verdraengte frueher die
                    // vorhandene still in den Papierkorb. Ersetzt wird deshalb
                    // nur bei gleichem vollem Ziel oder wenn die alte
                    // Verknuepfung nachweislich ins Leere zeigt.
                    if (bereitsDa != null && !DieselbeInstallation(eintrag.FullName, bereitsDa.FullName))
                        bereitsDa = null;

                    if (bereitsDa == null && ziel == null) continue;   // unbekannt → liegen lassen
                    if (bereitsDa == null && !Directory.Exists(ziel!)) continue;

                    if (bereitsDa != null)
                    {
                        // Die frische Verknuepfung vom Desktop ist die aktuellere —
                        // sie ersetzt die alte an genau deren Stelle. Sonst bliebe
                        // ein Eintrag zurueck, der ins Leere zeigen kann.
                        if (!nurVorschau) ErsetzeAnGleicherStelle(eintrag, bereitsDa);
                        ersetzt++;
                        continue;
                    }

                    if (!nurVorschau) Verschiebe(eintrag, ziel!);
                    zurueck++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Rechte werden nicht mehr VORAB geprueft, sondern am
                    // Versuch gemessen. Die alte Probe schrieb eine Testdatei
                    // in den Ordner — dafuer braucht es Schreibrechte. Zum
                    // WEGverschieben genuegen aber Lesen und Loeschen, und
                    // genau die hat ein Benutzerkonto auf dem Desktop "fuer
                    // alle Benutzer" oft (Loeschrecht auf den Dateien, kein
                    // Schreibrecht im Ordner). Die Probe meldete deshalb
                    // "gesperrt", wo das Verschieben laengst moeglich war.
                    if (eintrag.FullName.StartsWith(oeffentlich, StringComparison.OrdinalIgnoreCase))
                        gesperrt.Add(eintrag.Name);
                    else
                        fehler++;
                }
                catch (Exception)
                {
                    fehler++;   // der naechste Lauf versucht es erneut
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopReclaim.Run");
        }

        // Doppelte im SELBEN Ordner gleich mitraeumen — nach jedem Einordnen
        // lag sonst wieder "Camtasia" neben "Camtasia (2)", bis eigens der
        // Doppelte-Knopf gedrueckt wurde. Bewusst NUR innerhalb eines Ordners:
        // dieselbe Verknuepfung in ZWEI Bereichen kann gewollt sein (Launcher
        // UND Programme) — die raeumt weiterhin nur der Knopf ab.
        var doppelte = nurVorschau ? 0 : RemoveDuplicates(config, nurVorschau: false, nurGleicherOrdner: true);

        var ergebnis = new Ergebnis(zurueck, ersetzt, fehler, gesperrt, doppelte);
        if (!nurVorschau && (ergebnis.Gesamt > 0 || gesperrt.Count > 0 || doppelte > 0))
            StartupLog.Write($"Symbole eingeordnet: {zurueck} zurueckgeholt, {ersetzt} ersetzt, " +
                             $"{doppelte} Doppelte entfernt, {fehler} fehlgeschlagen, " +
                             $"{gesperrt.Count} ohne Berechtigung.");
        return ergebnis;
    }

    /// <summary>
    /// Zielordner aus den Ablage-Regeln — vor allem fuer ORDNER, die auf dem
    /// Desktop liegen. Ohne das blieben sie liegen, weil fuer sie kein
    /// gemerkter Platz existiert.
    /// </summary>
    private static string? RegelOrdner(ConfigService config, FileSystemInfo eintrag)
    {
        try
        {
            var istOrdner = ShortcutFactory.PointsToFolder(eintrag.FullName);

            foreach (var fence in config.Config.Fences)
            {
                foreach (var tab in fence.Tabs)
                {
                    if (!Directory.Exists(tab.FolderPath)) continue;

                    if (istOrdner && FileCategories.IsFolderRule(tab.AutoExtensions))
                        return tab.FolderPath;

                    if (istOrdner) continue;

                    var endung = Path.GetExtension(eintrag.Name).TrimStart('.').ToLowerInvariant();
                    if (endung.Length > 0 && FileCategories.MatchesExact(tab.AutoExtensions, endung))
                        return tab.FolderPath;
                }
            }
        }
        catch (Exception)
        {
            // Regel nicht auswertbar → Eintrag bleibt liegen
        }
        return null;
    }

    /// <summary>
    /// Sucht Doppelte INNERHALB der Bereiche: zwei Verknuepfungen auf dasselbe
    /// Programm. Die neuere bleibt, die aeltere wandert in den Papierkorb.
    /// Rueckgabe: Anzahl der entfernten Eintraege.
    /// </summary>
    public static int RemoveDuplicates(ConfigService config, bool nurVorschau = false,
                                       bool nurGleicherOrdner = false)
    {
        if (nurVorschau) return RemoveDuplicatesIntern(config, true, nurGleicherOrdner);
        if (!Einsammeln.Betreten(15000)) return 0;
        try { return RemoveDuplicatesIntern(config, false, nurGleicherOrdner); }
        finally { Einsammeln.Verlassen(); }
    }

    private static int RemoveDuplicatesIntern(ConfigService config, bool nurVorschau,
                                              bool nurGleicherOrdner)
    {
        var entfernt = 0;

        try
        {
            // Zwei Wege der Erkennung, weil beide allein Luecken haben:
            //   1. gleiches ZIEL — auch bei unterschiedlichen Namen
            //   2. gleicher NAME — auch wenn ein Ziel nicht auflösbar ist
            //
            // Der zweite Weg ist noetig: Eine Verknuepfung kann ins Leere
            // zeigen (Programm deinstalliert, Pfad geaendert). Ihr Ziel ist
            // dann nicht ermittelbar, und ueber Weg 1 faellt sie durch — sie
            // blieb dadurch als Doppelte liegen, obwohl daneben eine
            // funktionierende Verknuepfung gleichen Namens stand.
            var gruppen = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var ordner in TabOrdner(config))
            {
                foreach (var datei in new DirectoryInfo(ordner).EnumerateFiles("*.lnk"))
                {
                    var ziel = PlacementRegistry.ZielSchluessel(datei.FullName);

                    if (ziel != null)
                    {
                        // Gleiches Ziel INKLUSIVE Argumente — nur das ist
                        // wirklich dieselbe Verknuepfung. Mit nurGleicherOrdner
                        // zaehlt der Ordner zum Schluessel: die Automatik nach
                        // dem Einordnen raeumt nur Doppelte ab, die
                        // NEBENEINANDER liegen.
                        var schluesselZiel = nurGleicherOrdner ? ordner + "|" + ziel : ziel;
                        if (!gruppen.TryGetValue(schluesselZiel, out var liste))
                            gruppen[schluesselZiel] = liste = new List<FileInfo>();
                        liste.Add(datei);
                        continue;
                    }

                    // Kein ermittelbares Ziel. Nach dem NAMEN zu gruppieren ist
                    // hier nur zulaessig, wenn die Verknuepfung nachweislich ins
                    // Leere zeigt. „Nicht aufloesbar" heisst naemlich nicht
                    // zwingend „kaputt": ein Netzlaufwerk kann gerade getrennt,
                    // eine Wechselplatte abgezogen, COM kurz belegt sein. Wer
                    // das verwechselt, entfernt voellig intakte Verknuepfungen.
                    if (!ZeigtNachweislichInsLeere(datei.FullName)) continue;

                    var schluessel = (nurGleicherOrdner ? ordner + "|" : "") + "kaputt:" + datei.Name;
                    if (!gruppen.TryGetValue(schluessel, out var kaputte))
                        gruppen[schluessel] = kaputte = new List<FileInfo>();
                    kaputte.Add(datei);
                }
            }

            foreach (var (_, liste) in gruppen)
            {
                if (liste.Count < 2) continue;

                // Welche bleibt? Zuerst eine, die tatsaechlich funktioniert —
                // eine Verknuepfung ins Leere zu behalten waere sinnlos.
                // Unter mehreren funktionierenden die zuletzt geaenderte.
                var behalten = liste
                    .OrderByDescending(f => PlacementRegistry.ZielSchluessel(f.FullName) != null)
                    .ThenByDescending(f => f.LastWriteTimeUtc)
                    .First();

                foreach (var datei in liste.Where(f => !string.Equals(f.FullName, behalten.FullName,
                                                                     StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        if (!nurVorschau)
                            Papierkorb.Verschieben(datei.FullName);
                        entfernt++;
                    }
                    catch (Exception)
                    {
                        // gesperrt — beim naechsten Mal
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopReclaim.RemoveDuplicates");
        }

        if (!nurVorschau && entfernt > 0)
            StartupLog.Write($"{entfernt} doppelte Verknuepfung(en) in den Papierkorb gelegt.");
        return entfernt;
    }

    // ===================== Hilfsmittel =====================

    private static IEnumerable<string> TabOrdner(ConfigService config)
        => config.Config.Fences
            .SelectMany(f => f.Tabs)
            .Select(t => t.FolderPath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Zeigt die Verknuepfung nachweislich ins Leere?
    ///
    /// Nur dann, wenn das Ziel ausgelesen werden KONNTE und die Datei bzw. der
    /// Ordner dort nicht existiert. Schlaegt schon das Auslesen fehl, lautet die
    /// Antwort bewusst „nein" — daraus laesst sich nichts schliessen, und im
    /// Zweifel wird nichts angefasst.
    /// </summary>
    private static bool ZeigtNachweislichInsLeere(string lnkPfad)
    {
        try
        {
            var angaben = ShortcutFactory.ResolveLnk(lnkPfad);
            if (angaben == null || string.IsNullOrWhiteSpace(angaben.Ziel)) return false;

            // Netz- und Wechselziele ausdruecklich ausklammern: sie sind oft
            // nur voruebergehend nicht da.
            if (angaben.Ziel.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            var wurzel = Path.GetPathRoot(angaben.Ziel);
            if (!string.IsNullOrEmpty(wurzel) && !Directory.Exists(wurzel)) return false;

            return !File.Exists(angaben.Ziel) && !Directory.Exists(angaben.Ziel);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Alle bereits einsortierten Verknuepfungen, nach ihrem Ziel.
    private static Dictionary<string, FileInfo> BestandNachZiel(ConfigService config)
    {
        var bestand = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var ordner in TabOrdner(config))
        {
            foreach (var datei in new DirectoryInfo(ordner).EnumerateFiles("*.lnk"))
            {
                var ziel = PlacementRegistry.ZielSchluessel(datei.FullName);
                if (ziel != null) bestand.TryAdd(ziel, datei);
            }
        }
        return bestand;
    }

    /// Legt den Desktop-Eintrag an die Stelle der vorhandenen Verknuepfung.
    /// <summary>
    /// Ist die Verknuepfung auf dem Desktop dieselbe Installation wie die im
    /// Bereich? Gleiches volles Ziel — oder die alte zeigt nachweislich ins
    /// Leere (Update hat den Versionsordner gewechselt).
    /// </summary>
    private static bool DieselbeInstallation(string neuPfad, string altPfad)
    {
        try
        {
            var neu = ShortcutFactory.ResolveLnk(neuPfad)?.Ziel;
            var alt = ShortcutFactory.ResolveLnk(altPfad)?.Ziel;
            if (string.IsNullOrWhiteSpace(neu) || string.IsNullOrWhiteSpace(alt)) return false;

            if (string.Equals(Path.GetFullPath(neu), Path.GetFullPath(alt), StringComparison.OrdinalIgnoreCase))
                return true;

            return ZeigtNachweislichInsLeere(altPfad);
        }
        catch (Exception)
        {
            return false; // im Zweifel nichts ersetzen
        }
    }

    /// <summary>
    /// Legt den Desktop-Eintrag an die Stelle der vorhandenen Verknuepfung.
    ///
    /// Reihenfolge: ERST die neue sicher in den Ordner holen, DANN die alte in
    /// den Papierkorb. Frueher umgekehrt — scheiterte das Holen, war die alte
    /// schon weg. Gleicher Name → gleicher Platz in der Anordnung.
    /// </summary>
    private static void ErsetzeAnGleicherStelle(FileSystemInfo neu, FileInfo alt)
    {
        var ordner = alt.DirectoryName;
        if (ordner == null) return;

        // 1. Holen — unter freiem Namen, falls der alte Name gleich ist.
        var zwischen = FreierZiel(ordner, neu);
        FileSystem.MoveFile(neu.FullName, zwischen);   // ohne UIOption: kein Shell-Fenster aus dem Zeitgeber

        // 2. Alte in den Papierkorb, nie endgueltig.
        try
        {
            Papierkorb.Verschieben(alt.FullName);
            StartupLog.Write($"Ersetzt (alte Fassung in den Papierkorb): {alt.FullName}");
        }
        catch (Exception)
        {
            // Gesperrt → beide bleiben; die neue liegt unter „(2)" daneben.
            PlacementRegistry.Learn(zwischen, ordner);
            return;
        }

        // 3. Wurde der Name frei, die neue unter ihrem eigenen Namen ablegen.
        var ziel = Path.Combine(ordner, neu.Name);
        if (!string.Equals(zwischen, ziel, StringComparison.OrdinalIgnoreCase) && !File.Exists(ziel))
        {
            try { File.Move(zwischen, ziel); } catch (Exception) { ziel = zwischen; }
        }
        else
        {
            ziel = zwischen;
        }

        PlacementRegistry.Learn(ziel, ordner);
    }

    private static bool IstVerknuepfung(FileSystemInfo eintrag)
        => eintrag is FileInfo
           && Path.GetExtension(eintrag.Name).ToLowerInvariant() is ".lnk" or ".url" or ".appref-ms";

    /// <summary>
    /// Freier Zielname: bei Namensgleichheit „Name (2)". FileSystem.MoveDirectory
    /// MISCHT einen Ordner sonst stillschweigend in einen gleichnamigen am Ziel
    /// („Neuer Ordner" landete im Inhalt eines alten), und MoveFile scheitert.
    /// </summary>
    private static string FreierZiel(string zielOrdner, FileSystemInfo eintrag)
    {
        var ziel = Path.Combine(zielOrdner, eintrag.Name);
        if (!File.Exists(ziel) && !Directory.Exists(ziel)) return ziel;

        var istOrdner = eintrag is DirectoryInfo;
        var stamm = istOrdner ? eintrag.Name : Path.GetFileNameWithoutExtension(eintrag.Name);
        var endung = istOrdner ? "" : Path.GetExtension(eintrag.Name);
        for (var n = 2; ; n++)
        {
            ziel = Path.Combine(zielOrdner, $"{stamm} ({n}){endung}");
            if (!File.Exists(ziel) && !Directory.Exists(ziel)) return ziel;
        }
    }

    private static void Verschiebe(FileSystemInfo eintrag, string zielOrdner)
    {
        var ziel = FreierZiel(zielOrdner, eintrag);

        if (eintrag is DirectoryInfo)
            FileSystem.MoveDirectory(eintrag.FullName, ziel);
        else
            FileSystem.MoveFile(eintrag.FullName, ziel);

        PlacementRegistry.Learn(ziel, zielOrdner);
    }
}
