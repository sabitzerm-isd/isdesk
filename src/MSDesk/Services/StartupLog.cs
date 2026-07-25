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
        try
        {
            lock (Sync)
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                if (_fresh)
                {
                    _fresh = false;
                    var kopf = $"=== MSDesk {UpdateService.CurrentVersion} — Start {DateTime.Now:dd.MM.yyyy HH:mm:ss} ==={Environment.NewLine}";
                    File.WriteAllText(Path_, kopf + line, Encoding.UTF8);
                }
                else
                {
                    File.AppendAllText(Path_, line, Encoding.UTF8);
                }
            }
        }
        catch (Exception)
        {
            // Protokollieren darf niemals selbst stoeren.
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
