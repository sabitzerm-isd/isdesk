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

    private static double Clamp(double value, double min, double max)
        => max < min ? min : Math.Min(Math.Max(value, min), max);
}
