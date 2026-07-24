using System.IO;

namespace MSDesk.Services;

/// <summary>
/// Eigene Notizen zu einzelnen Eintraegen. Sie werden in der Listendarstellung
/// als zweite Spalte gezeigt und ausserdem in der Kurzinfo (Tooltip).
///
/// Schluessel ist — wie beim <see cref="PlacementRegistry"/> — der Dateiname
/// in Kleinschreibung. Dadurch bleibt die Notiz erhalten, wenn ein Eintrag in
/// einen anderen Tab oder Bereich verschoben wird.
/// </summary>
public static class NoteRegistry
{
    private static ConfigService? _config;

    public static void Init(ConfigService config) => _config = config;

    private static string KeyOf(string path)
        => Path.GetFileName(path.TrimEnd('\\', '/')).ToLowerInvariant();

    /// Notiz zu einem Eintrag (null, wenn keine hinterlegt ist).
    public static string? Get(string path)
    {
        if (_config == null || string.IsNullOrWhiteSpace(path)) return null;
        var key = KeyOf(path);
        if (key.Length == 0) return null;
        return _config.Config.Notes.TryGetValue(key, out var note) && !string.IsNullOrWhiteSpace(note)
            ? note
            : null;
    }

    /// Setzt bzw. loescht die Notiz (leerer Text = loeschen). Liefert den neuen Wert.
    public static string? Set(string path, string? note)
    {
        if (_config == null || string.IsNullOrWhiteSpace(path)) return null;
        var key = KeyOf(path);
        if (key.Length == 0) return null;

        var text = note?.Trim();
        if (string.IsNullOrEmpty(text)) _config.Config.Notes.Remove(key);
        else _config.Config.Notes[key] = text;

        _config.SaveDebounced();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
