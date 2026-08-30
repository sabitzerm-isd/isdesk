using System.IO;
using System.Text.RegularExpressions;

namespace MSDesk.Services;

/// <summary>
/// Stellt versehentlich geloeschte SYSTEMOBJEKTE in den Bereichen wieder her —
/// Papierkorb, „Dieser PC", Netzwerk und aehnliche.
///
/// Solche Objekte sind keine Dateien, sondern Ordner mit angehaengter Kennung
/// („Papierkorb.{645FF040-5081-101B-9F08-00AA002F954E}"). Genau deshalb laesst
/// sich das hier gefahrlos machen: Es entsteht kein Inhalt, es geht nichts
/// verloren, und wer das Objekt wirklich nicht will, loescht es einfach wieder.
///
/// Angelegt wird ausschliesslich dort, wo das Platz-Gedaechtnis es ohnehin
/// verzeichnet — also nur, wo der Anwender das Objekt selbst einmal hingelegt
/// hat. Eine Verknuepfung oder eine echte Datei wird NIE nachgebaut: deren
/// Inhalt kann MSDesk nicht kennen, und etwas Falsches vorzugaukeln waere
/// schlimmer als die Luecke.
/// </summary>
public static class ShellObjectHealing
{
    /// Name plus Kennung in geschweiften Klammern, z. B. „papierkorb.{645ff040-…}".
    private static readonly Regex Systemobjekt =
        new(@"^(?<name>.+)\.\{(?<kennung>[0-9a-fA-F-]{36})\}$", RegexOptions.Compiled);

    /// <summary>
    /// Legt fehlende Systemobjekte wieder an. Rueckgabe: Anzahl der
    /// wiederhergestellten Objekte.
    /// </summary>
    public static int Heilen(ConfigService config)
    {
        var wiederhergestellt = 0;

        try
        {
            foreach (var (eintrag, ordner) in config.Config.Placements)
            {
                var treffer = Systemobjekt.Match(eintrag);
                if (!treffer.Success) continue;
                if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner)) continue;

                // Schon da? Dann nichts tun. Der Vergleich laeuft ueber den
                // gesamten Namen — zwei Objekte mit gleicher Kennung, aber
                // unterschiedlichem Namen sind fuer Windows dasselbe Ziel, fuer
                // den Anwender aber zwei Eintraege.
                if (Vorhanden(ordner, treffer.Groups["kennung"].Value)) continue;

                var name = Grossschreiben(treffer.Groups["name"].Value);
                var kennung = treffer.Groups["kennung"].Value.ToUpperInvariant();
                var ziel = Path.Combine(ordner, $"{name}.{{{kennung}}}");

                Directory.CreateDirectory(ziel);
                wiederhergestellt++;
                StartupLog.Write($"Systemobjekt wiederhergestellt: {ziel}");
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ShellObjectHealing.Heilen");
        }

        return wiederhergestellt;
    }

    /// Liegt im Ordner bereits ein Objekt mit dieser Kennung?
    private static bool Vorhanden(string ordner, string kennung)
    {
        try
        {
            foreach (var vorhanden in Directory.EnumerateDirectories(ordner))
                if (vorhanden.Contains(kennung, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (Exception)
        {
            return true; // nicht lesbar → lieber nichts anlegen
        }
        return false;
    }

    /// Das Gedaechtnis speichert klein geschrieben; angezeigt wird der Teil vor
    /// der Kennung. Ohne das hiesse der Papierkorb danach „papierkorb".
    private static string Grossschreiben(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
