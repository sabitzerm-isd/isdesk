using System.IO;
using System.Text;

namespace MSDesk.Services;

/// <summary>
/// Kurzes Protokoll des Startablaufs und des Speicherns.
///
/// Grund: Faellt beim Start ein Schritt aus, laeuft MSDesk scheinbar normal
/// weiter — reagiert dann aber z. B. nicht mehr auf Bildschirmwechsel oder
/// speichert die Konfiguration nicht. Ohne Protokoll ist so etwas von aussen
/// nicht zu erkennen.
///
/// Die Datei bleibt klein: sie wird bei jedem Start neu angelegt.
/// </summary>
public static class StartupLog
{
    private static readonly object Sync = new();
    private static string? _path;
    private static bool _fresh = true;

    /// <summary>
    /// Muss von der Anwendung ausdruecklich eingeschaltet werden. Verhindert,
    /// dass automatisierte Tests in das echte Benutzerverzeichnis schreiben und
    /// dort das Protokoll der Anwendung ueberschreiben.
    ///
    /// Bewusst ein eigener Schalter statt einer Abfrage auf Application.Current:
    /// eine solche Abfrage ist von aussen nicht nachpruefbar — bleibt das
    /// Protokoll leer, weiss man nicht, ob nichts passiert ist oder ob die
    /// Bedingung nie zutraf.
    /// </summary>
    private static bool _enabled;

    /// Schaltet das Protokoll ein (nur die Anwendung ruft das auf).
    public static void Enable() => _enabled = true;

    private static string Path_
    {
        get
        {
            if (_path != null) return _path;
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSDesk");
            Directory.CreateDirectory(dir);
            return _path = System.IO.Path.Combine(dir, "start.log");
        }
    }

    public static void Write(string message)
    {
        if (!_enabled) return;

        try
        {
            lock (Sync)
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                if (_fresh)
                {
                    _fresh = false;
                    var version = Versionstext();
                    var kopf = $"=== MSDesk {version} — Start {DateTime.Now:dd.MM.yyyy HH:mm:ss} ==={Environment.NewLine}";
                    File.WriteAllText(Path_, kopf + line, Encoding.UTF8);
                }
                else
                {
                    File.AppendAllText(Path_, line, Encoding.UTF8);
                }
            }
        }
        catch (Exception ex)
        {
            // Protokollieren darf den Ablauf nicht stoeren — aber lautlos
            // scheitern darf es auch nicht, sonst steht man vor einer leeren
            // Datei und weiss nicht warum.
            Fehlgeschlagen(ex);
        }
    }

    private static string Versionstext()
    {
        try { return UpdateService.CurrentVersion; }
        catch (Exception) { return "?"; }
    }

    private static bool _fehlerGemeldet;

    /// Meldet EINMAL, dass das Protokoll nicht geschrieben werden kann.
    private static void Fehlgeschlagen(Exception ex)
    {
        if (_fehlerGemeldet) return;
        _fehlerGemeldet = true;
        try { App.LogCrash(ex, "StartupLog"); } catch (Exception) { /* dann eben nicht */ }
    }

    /// <summary>
    /// Haelt einen kompletten Anordnungs-Vorgang fest: welche Bildschirme
    /// angeschlossen sind, welche Kennung daraus folgt, was entschieden wurde
    /// und wo jeder Bereich anschliessend liegt (absolute Koordinaten).
    ///
    /// Damit laesst sich der ganze Ablauf — anstecken, abstecken, von Hand
    /// verschieben, sichern — spaeter Schritt fuer Schritt nachvollziehen.
    /// </summary>
    public static void Layout(string anlass, string kennung, string entscheidung,
                              IEnumerable<(string Titel, double X, double Y, double Breite, double Hoehe)> bereiche)
    {
        if (!_enabled) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"--- {anlass} — {DateTime.Now:dd.MM.yyyy HH:mm:ss} ---");

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var b = screen.Bounds;
                sb.AppendLine($"  Bildschirm {(screen.Primary ? "(Haupt)" : "       ")} " +
                              $"{b.Width}x{b.Height} an {b.X}/{b.Y}");
            }

            sb.AppendLine($"  Kennung      : {kennung}");
            sb.AppendLine($"  Entscheidung : {entscheidung}");

            foreach (var (titel, x, y, breite, hoehe) in bereiche)
                sb.AppendLine($"    {titel,-22} X={x,7:F0} Y={y,7:F0}  {breite,5:F0} x {hoehe,4:F0}");

            lock (Sync)
            {
                if (_fresh) { Write("Protokoll begonnen."); }
                File.AppendAllText(Path_, sb.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            // Protokollieren darf den Ablauf nicht stoeren — aber lautlos
            // scheitern darf es auch nicht, sonst steht man vor einer leeren
            // Datei und weiss nicht warum.
            Fehlgeschlagen(ex);
        }
    }

    /// Fuehrt einen Startschritt aus und haelt Erfolg oder Fehler fest.
    /// Ein Fehler bricht den Start NICHT ab — die uebrigen Schritte laufen weiter.
    public static void Step(string name, Action action)
    {
        try
        {
            action();
            Write($"OK   {name}");
        }
        catch (Exception ex)
        {
            Write($"FEHL {name} -> {ex.GetType().Name}: {ex.Message}");
            App.LogCrash(ex, $"Start:{name}");
        }
    }
}
