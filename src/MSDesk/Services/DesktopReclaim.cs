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
    /// Programms an deren Stelle abgeloest.
    /// </summary>
    public sealed record Ergebnis(int Zurueckgeholt, int Ersetzt, int Fehlgeschlagen)
    {
        public int Gesamt => Zurueckgeholt + Ersetzt;
    }

    /// <summary>
    /// Durchsucht den Desktop und ordnet bekannte Eintraege wieder ein.
    /// <paramref name="nurVorschau"/> = nichts verschieben, nur zaehlen.
    /// </summary>
    public static Ergebnis Run(ConfigService config, bool nurVorschau = false)
    {
        int zurueck = 0, ersetzt = 0, fehler = 0;

        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktop)) return new Ergebnis(0, 0, 0);

            // Was liegt bereits in den Bereichen? Ziel → Datei, um Doppelte zu erkennen.
            var vorhanden = BestandNachZiel(config);

            foreach (var eintrag in new DirectoryInfo(desktop).EnumerateFileSystemInfos())
            {
                try
                {
                    if ((eintrag.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (string.Equals(eintrag.Name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                    var ziel = PlacementRegistry.LookupByPath(eintrag.FullName);
                    var zielSchluessel = PlacementRegistry.ZielSchluessel(eintrag.FullName);

                    // Liegt dasselbe Programm schon in einem Bereich?
                    var bereitsDa = zielSchluessel != null
                                    && vorhanden.TryGetValue(zielSchluessel, out var alt) ? alt : null;

                    if (bereitsDa != null)
                    {
                        // Die frische Verknuepfung vom Desktop ist die aktuellere —
                        // sie ersetzt die alte an genau deren Stelle. Sonst bliebe
                        // ein Eintrag zurueck, der ins Leere zeigen kann.
                        if (!nurVorschau) ErsetzeAnGleicherStelle(eintrag, bereitsDa);
                        ersetzt++;
                        continue;
                    }

                    if (ziel == null) continue;                       // unbekannt → liegen lassen
                    if (!Directory.Exists(ziel)) continue;             // Bereich existiert nicht mehr

                    if (!nurVorschau) Verschiebe(eintrag, ziel);
                    zurueck++;
                }
                catch (Exception)
                {
                    fehler++;   // gesperrt o. ae. — der naechste Lauf versucht es erneut
                }
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopReclaim.Run");
        }

        var ergebnis = new Ergebnis(zurueck, ersetzt, fehler);
        if (!nurVorschau && ergebnis.Gesamt > 0)
            StartupLog.Write($"Symbole eingeordnet: {zurueck} zurueckgeholt, {ersetzt} ersetzt, " +
                             $"{fehler} fehlgeschlagen.");
        return ergebnis;
    }

    /// <summary>
    /// Sucht Doppelte INNERHALB der Bereiche: zwei Verknuepfungen auf dasselbe
    /// Programm. Die neuere bleibt, die aeltere wandert in den Papierkorb.
    /// Rueckgabe: Anzahl der entfernten Eintraege.
    /// </summary>
    public static int RemoveDuplicates(ConfigService config, bool nurVorschau = false)
    {
        var entfernt = 0;

        try
        {
            // Ziel → alle Dateien, die darauf zeigen.
            var nachZiel = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

            foreach (var ordner in TabOrdner(config))
            {
                foreach (var datei in new DirectoryInfo(ordner).EnumerateFiles("*.lnk"))
                {
                    var ziel = PlacementRegistry.ZielSchluessel(datei.FullName);
                    if (ziel == null) continue;

                    if (!nachZiel.TryGetValue(ziel, out var liste))
                        nachZiel[ziel] = liste = new List<FileInfo>();
                    liste.Add(datei);
                }
            }

            foreach (var (_, liste) in nachZiel)
            {
                if (liste.Count < 2) continue;

                // Die zuletzt geaenderte behalten — das ist die aus dem Update.
                var behalten = liste.OrderByDescending(f => f.LastWriteTimeUtc).First();
                foreach (var datei in liste.Where(f => f.FullName != behalten.FullName))
                {
                    try
                    {
                        if (!nurVorschau)
                            FileSystem.DeleteFile(datei.FullName, UIOption.OnlyErrorDialogs,
                                                  RecycleOption.SendToRecycleBin);
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
    private static void ErsetzeAnGleicherStelle(FileSystemInfo neu, FileInfo alt)
    {
        var ordner = alt.DirectoryName;
        if (ordner == null) return;

        try { alt.Delete(); } catch (Exception) { /* dann eben ueberschreiben */ }

        var ziel = Path.Combine(ordner, neu.Name);
        FileSystem.MoveFile(neu.FullName, ziel, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
        PlacementRegistry.Learn(ziel, ordner);
    }

    private static void Verschiebe(FileSystemInfo eintrag, string zielOrdner)
    {
        var ziel = Path.Combine(zielOrdner, eintrag.Name);

        if (eintrag is DirectoryInfo)
            FileSystem.MoveDirectory(eintrag.FullName, ziel, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);
        else
            FileSystem.MoveFile(eintrag.FullName, ziel, UIOption.OnlyErrorDialogs, UICancelOption.DoNothing);

        PlacementRegistry.Learn(ziel, zielOrdner);
    }
}
