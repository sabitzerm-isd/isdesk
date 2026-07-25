using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MSDesk.Services;

/// <summary>
/// Haelt zu jeder Bildschirm-Konfiguration ein Vorschaubild fest — einen
/// verkleinerten Bildschirmausdruck des Augenblicks, in dem die Anordnung
/// gesichert wurde.
///
/// Zweck: In den Optionen sieht man damit auf einen Blick, WIE eine gespeicherte
/// Konfiguration aussieht, statt nur Zahlen zu lesen. Besonders hilfreich, wenn
/// mehrere Arbeitsplaetze mit aehnlichen Aufloesungen hinterlegt sind.
///
/// Bewusst sparsam: das Bild wird auf 640 Pixel Breite verkleinert (rund
/// 60-120 KB als JPEG) und hoechstens alle 20 Sekunden neu erzeugt.
/// </summary>
public static class LayoutPreview
{
    /// Breite des gespeicherten Bildes. Reicht fuer einen Gesamteindruck.
    private const int Breite = 640;

    /// Kuerzester Abstand zwischen zwei Aufnahmen.
    private static readonly TimeSpan Mindestabstand = TimeSpan.FromSeconds(20);

    private static readonly Dictionary<string, DateTime> ZuletztErstellt = new(StringComparer.Ordinal);
    private static readonly object Sync = new();

    /// Ordner der Vorschaubilder (neben der Konfiguration).
    public static string Folder => Path.Combine(ConfigService.DefaultFolder, "Vorschau");

    /// <summary>
    /// Dateiname zu einer Bildschirm-Kennung. Die Kennung enthaelt Zeichen, die
    /// in Dateinamen nicht erlaubt sind (| : ,) — deshalb ein kurzer Abdruck.
    /// </summary>
    public static string FileFor(string kennung)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(kennung ?? ""));
        var kurz = Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
        return Path.Combine(Folder, $"{kurz}.jpg");
    }

    /// <summary>
    /// Nimmt den aktuellen Bildschirminhalt auf und legt ihn als Vorschau zu
    /// dieser Kennung ab. Laeuft im Hintergrund und meldet keine Fehler nach
    /// aussen — eine fehlende Vorschau darf nichts blockieren.
    /// </summary>
    public static void Capture(string kennung, bool erzwingen = false)
    {
        if (string.IsNullOrWhiteSpace(kennung)) return;

        lock (Sync)
        {
            if (!erzwingen
                && ZuletztErstellt.TryGetValue(kennung, out var zuletzt)
                && DateTime.Now - zuletzt < Mindestabstand)
                return;

            ZuletztErstellt[kennung] = DateTime.Now;
        }

        // Die Aufnahme selbst muss auf dem aufrufenden Thread passieren (der
        // Bildschirminhalt aendert sich sonst waehrenddessen); nur das
        // Verkleinern und Schreiben laeuft im Hintergrund.
        try
        {
            var bereich = GesamteFlaeche();
            if (bereich.Width <= 0 || bereich.Height <= 0) return;

            using var voll = new Bitmap(bereich.Width, bereich.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(voll))
                g.CopyFromScreen(bereich.Left, bereich.Top, 0, 0, bereich.Size, CopyPixelOperation.SourceCopy);

            var hoehe = Math.Max(1, (int)Math.Round(Breite * (double)bereich.Height / bereich.Width));
            using var klein = new Bitmap(voll, new Size(Breite, hoehe));

            Directory.CreateDirectory(Folder);
            var ziel = FileFor(kennung);

            // Erst in eine Nebendatei, dann ersetzen: sonst kann die Anzeige ein
            // halb geschriebenes Bild erwischen.
            var tmp = ziel + ".tmp";
            klein.Save(tmp, ImageFormat.Jpeg);
            File.Move(tmp, ziel, overwrite: true);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "LayoutPreview.Capture");
        }
    }

    /// Gesamte Flaeche ueber alle Bildschirme (in Pixeln).
    private static Rectangle GesamteFlaeche()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return Rectangle.Empty;

        var links = screens.Min(s => s.Bounds.Left);
        var oben = screens.Min(s => s.Bounds.Top);
        var rechts = screens.Max(s => s.Bounds.Right);
        var unten = screens.Max(s => s.Bounds.Bottom);
        return new Rectangle(links, oben, rechts - links, unten - oben);
    }

    /// Entfernt die Vorschau einer Konfiguration (beim Zuruecksetzen).
    public static void Remove(string kennung)
    {
        try
        {
            var datei = FileFor(kennung);
            if (File.Exists(datei)) File.Delete(datei);
            lock (Sync) ZuletztErstellt.Remove(kennung);
        }
        catch (Exception)
        {
            // Eine liegengebliebene Vorschau stoert nicht.
        }
    }
}
