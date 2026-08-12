using System.Text;
using System.Windows;
using MSDesk.Views;

namespace MSDesk.Services;

/// <summary>
/// Der Ablauf „Symbole neu zuordnen" mit Vorschau und Rueckfrage — EINMAL.
///
/// Er wird von zwei Stellen aus angestossen (Optionen und Rechtsklick auf einen
/// Bereich). Die Entscheidungen — was gefunden wurde, was das bedeutet, was
/// nicht geht und warum — stehen deshalb ausschliesslich hier. Nur die Anzeige
/// des Ergebnisses unterscheidet sich: in den Optionen als Zeile unter den
/// Knoepfen, im Menue als kurze Meldung.
///
/// Zwei getrennte Umsetzungen derselben Sache waren in diesem Projekt schon
/// mehrfach die Ursache auseinanderlaufenden Verhaltens.
/// </summary>
public static class ReclaimPrompt
{
    /// <param name="Meldung">Kurztext fuer die aufrufende Stelle.</param>
    /// <param name="Gutartig">Fuer die Einfaerbung: true = alles in Ordnung.</param>
    /// <param name="BereitsGemeldet">
    /// true, wenn der Anwender das Ergebnis schon gesehen hat — weil ein Dialog
    /// lief oder weil er selbst abgebrochen hat. Aufrufer ohne eigene
    /// Ergebniszeile duerfen dann NICHT noch ein Fenster hinterherschieben.
    /// </param>
    public sealed record Ausgang(string Meldung, bool Gutartig, bool BereitsGemeldet);

    public static Ausgang Ausfuehren(FenceManager manager, Window? centerOn)
    {
        var vorschau = manager.ReclaimDesktopIcons(nurVorschau: true);

        if (vorschau.Gesamt == 0 && vorschau.Gesperrt.Count == 0)
            return new Ausgang("Auf dem Desktop liegt nichts, das in einen Bereich gehört.",
                               Gutartig: true, BereitsGemeldet: false);

        var text = new StringBuilder();

        if (vorschau.Gesamt > 0)
        {
            text.AppendLine("Auf dem Desktop wurden Symbole gefunden, deren Platz bekannt ist:\n");
            if (vorschau.Zurueckgeholt > 0)
                text.AppendLine($"• {vorschau.Zurueckgeholt} wandern zurück in ihren Bereich");
            if (vorschau.Ersetzt > 0)
                text.AppendLine($"• {vorschau.Ersetzt} ersetzen eine ältere Verknüpfung desselben Programms");
            text.AppendLine("\nAlles andere bleibt unberührt auf dem Desktop liegen.");
        }

        // Der Desktop „für alle Benutzer" ist ohne Administratorrechte gesperrt.
        // Das gehört klar gesagt — sonst sucht man den Fehler bei MSDesk.
        if (vorschau.Gesperrt.Count > 0)
        {
            if (text.Length > 0) text.AppendLine();
            text.AppendLine($"Nicht möglich bei: {string.Join(", ", vorschau.Gesperrt)}");
            text.AppendLine("\nDiese Verknüpfungen wurden bei der Installation für ALLE Benutzer " +
                            "angelegt und liegen in C:\\Users\\Public\\Desktop. Dieser Ordner ist " +
                            "ohne Administratorrechte schreibgeschützt — MSDesk darf sie weder " +
                            "verschieben noch löschen.");
        }

        if (vorschau.Gesamt == 0)
        {
            ConfirmDialog.Info(text.ToString(), centerOn);
            return new Ausgang($"{vorschau.Gesperrt.Count} Verknüpfung(en) benötigen Administratorrechte.",
                               Gutartig: false, BereitsGemeldet: true);
        }

        var (ok, _) = ConfirmDialog.Show(text.ToString(), centerOn, okText: "Einordnen");
        // Abbruch ist eine bewusste Entscheidung des Anwenders — die braucht
        // keine Bestaetigung hinterher.
        if (!ok) return new Ausgang("Abgebrochen — es wurde nichts verschoben.",
                                    Gutartig: true, BereitsGemeldet: true);

        var ergebnis = manager.ReclaimDesktopIcons();

        var meldung = $"{ergebnis.Gesamt} Symbole eingeordnet";
        if (ergebnis.Fehlgeschlagen > 0) meldung += $", {ergebnis.Fehlgeschlagen} nicht (in Benutzung)";
        if (ergebnis.Gesperrt.Count > 0) meldung += $", {ergebnis.Gesperrt.Count} ohne Administratorrechte";

        return new Ausgang(meldung + ".",
                           Gutartig: ergebnis.Fehlgeschlagen == 0 && ergebnis.Gesperrt.Count == 0,
                           BereitsGemeldet: false);
    }
}
