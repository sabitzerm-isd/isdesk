using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Uebertraegt die Anordnung der Bereiche auf eine bislang unbekannte
/// Bildschirm-Konfiguration. Wird genau einmal gebraucht: beim ersten Mal, wenn
/// z. B. der zweite Monitor abgesteckt wird — danach ist fuer diese Konfiguration
/// ein eigenes Layout gespeichert und wird unveraendert weiterverwendet.
///
/// Grundgedanke: Die Bereiche bleiben dieselben, also soll auch ihre relative
/// Anordnung erhalten bleiben. Die Position wird anteilig umgerechnet, die
/// Groesse mit einem einheitlichen Faktor — sonst wuerden Bereiche verzerrt,
/// wenn sich das Seitenverhaeltnis stark aendert (zwei Monitore → einer).
/// </summary>
public static class LayoutTransfer
{
    public const double MinWidth = 110;
    public const double MinHeight = 80;

    /// Ein Bereich in einem Koordinatensystem (DIP).
    public readonly record struct Area(double X, double Y, double Width, double Height);

    /// <summary>
    /// Rechnet <paramref name="item"/> aus der Flaeche <paramref name="from"/>
    /// in die Flaeche <paramref name="to"/> um.
    /// </summary>
    public static LayoutRect Map(LayoutRect item, Area from, Area to)
    {
        if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0)
            return item; // ohne brauchbare Flaechen nichts veraendern

        var scaleX = to.Width / from.Width;
        var scaleY = to.Height / from.Height;

        // Groesse einheitlich skalieren: erhaelt die Form der Bereiche.
        var sizeScale = Math.Min(scaleX, scaleY);
        var width = Math.Max(MinWidth, item.Width * sizeScale);
        var height = Math.Max(MinHeight, item.Height * sizeScale);

        // Nie breiter/hoeher als die Zielflaeche.
        width = Math.Min(width, to.Width);
        height = Math.Min(height, to.Height);

        // Position anteilig: was links oben lag, liegt danach wieder links oben.
        var x = to.X + (item.X - from.X) * scaleX;
        var y = to.Y + (item.Y - from.Y) * scaleY;

        // Vollstaendig innerhalb der Zielflaeche halten.
        x = Clamp(x, to.X, to.X + to.Width - width);
        y = Clamp(y, to.Y, to.Y + to.Height - height);

        return new LayoutRect { X = Math.Round(x), Y = Math.Round(y), Width = Math.Round(width), Height = Math.Round(height) };
    }

    /// Abstand zwischen zwei Bereichen bei der Neuanordnung (DIP).
    private const double Gap = 8;

    /// Wie stark bei jedem Versuch verkleinert wird, wenn nicht alles passt.
    private const double ShrinkStep = 0.85;

    /// Hoehe eines „Zeilenfachs" fuer die Sortierung — Bereiche innerhalb
    /// dieses Bandes gelten als in derselben Zeile liegend.
    private const double RowBand = 120;

    /// <summary>
    /// Ordnet ALLE Bereiche gemeinsam auf der neuen Flaeche an — anteilig wie
    /// bisher, aber anschliessend ueberschneidungsfrei.
    ///
    /// Das einzelne Abbilden genuegt nicht: Bereiche, die vorher auf
    /// VERSCHIEDENEN Monitoren an aehnlicher Stelle lagen, landen sonst
    /// uebereinander. Deshalb werden sie danach in Leserichtung (oben links
    /// nach unten rechts) nebeneinandergelegt und bei Bedarf gemeinsam
    /// verkleinert, bis alles nebeneinander Platz hat.
    ///
    /// Die Rueckgabe hat dieselbe Reihenfolge wie die Eingabe.
    /// </summary>
    public static IReadOnlyList<LayoutRect> Arrange(IReadOnlyList<LayoutRect> items, Area from, Area to)
    {
        if (items.Count == 0) return items;
        if (to.Width <= 0 || to.Height <= 0) return items;

        // 1. Anteilig abbilden — daraus ergeben sich Wunschgroesse und -reihenfolge.
        var mapped = items.Select(i => Map(i, from, to)).ToList();

        // 2. Leserichtung bestimmen: erst Zeile (grob gebuendelt), dann von links.
        var order = Enumerable.Range(0, mapped.Count)
            .OrderBy(i => (int)Math.Floor(mapped[i].Y / RowBand))
            .ThenBy(i => mapped[i].X)
            .ToList();

        // 3. Nebeneinanderlegen; passt es in der Hoehe nicht, alles gemeinsam
        //    verkleinern und erneut versuchen.
        var scale = 1.0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (TryPack(mapped, order, to, scale, out var packed)) return packed;
            scale *= ShrinkStep;
        }

        // Notfall: letzter Versuch, Ergebnis in jedem Fall innerhalb der Flaeche.
        TryPack(mapped, order, to, scale, out var last);
        return last;
    }

    /// Legt die Bereiche zeilenweise nebeneinander. false = passt in der Hoehe nicht.
    private static bool TryPack(List<LayoutRect> mapped, List<int> order, Area to, double scale,
                                out List<LayoutRect> result)
    {
        result = new List<LayoutRect>(new LayoutRect[mapped.Count]);

        double x = to.X, y = to.Y, rowHeight = 0;
        var fits = true;

        foreach (var index in order)
        {
            var width = Math.Min(to.Width, Math.Max(MinWidth, mapped[index].Width * scale));
            var height = Math.Min(to.Height, Math.Max(MinHeight, mapped[index].Height * scale));

            // Zeilenumbruch, sobald die Zeile voll ist (aber nie bei leerer Zeile).
            if (x > to.X && x + width > to.X + to.Width)
            {
                x = to.X;
                y += rowHeight + Gap;
                rowHeight = 0;
            }

            if (y + height > to.Y + to.Height) fits = false;

            result[index] = new LayoutRect
            {
                X = Math.Round(x),
                Y = Math.Round(Clamp(y, to.Y, Math.Max(to.Y, to.Y + to.Height - height))),
                Width = Math.Round(width),
                Height = Math.Round(height)
            };

            x += width + Gap;
            rowHeight = Math.Max(rowHeight, height);
        }

        return fits;
    }

    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
