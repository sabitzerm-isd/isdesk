using System.IO;

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

            var datenlaufwerk = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
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

        if (Anlegbar(kandidat)) return kandidat;

        var ausweich = ImBenutzerordner();
        StartupLog.Write($"Ordner der Bereiche nicht nutzbar: {kandidat} → weiche aus auf {ausweich}");

        if (Anlegbar(ausweich)) return ausweich;

        // Selbst das schlug fehl — dann bleibt nur der urspruengliche Wunsch,
        // damit sich der Anwender die Meldung ansehen kann.
        StartupLog.Write($"Auch der Ausweichordner ist nicht nutzbar: {ausweich}");
        return kandidat;
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
