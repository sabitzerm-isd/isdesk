using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Identifiziert die aktuelle Bildschirm-Konfiguration als stabilen Fingerabdruck.
/// Damit merkt sich MSDesk je Konfiguration (z. B. nur Laptop / Homeoffice mit
/// zwei Monitoren / Dortmund) ein eigenes Layout aller Bereiche.
///
/// WICHTIG: Der Fingerabdruck enthaelt bewusst KEINE Windows-Geraetenamen
/// (\\.\DISPLAY1 …). Windows vergibt diese Nummern beim Ab- und Wiederanstecken
/// haeufig neu — dieselbe Monitor-Anordnung galt dadurch als unbekannt, und die
/// gespeicherte Anordnung der Bereiche wurde nicht wiederhergestellt.
/// Massgeblich sind nur Groesse und Lage der Bildschirme.
/// </summary>
public static class DisplayConfig
{
    private static string? _cached;

    public static string Current => _cached ??= Compute();

    /// Nach einem Wechsel der Bildschirm-Konfiguration aufrufen.
    public static void Invalidate() => _cached = null;

    private static string Compute()
        => Format(System.Windows.Forms.Screen.AllScreens
            .Select(s => (s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)));

    /// Baut den Fingerabdruck: je Bildschirm "BreitexHoehe@X,Y", nach Lage sortiert.
    public static string Format(IEnumerable<(int X, int Y, int Width, int Height)> monitors)
        => string.Join("|", monitors
            .OrderBy(m => m.X).ThenBy(m => m.Y)
            .Select(m => $"{m.Width}x{m.Height}@{m.X},{m.Y}"));

    /// <summary>
    /// Bringt einen Schluessel auf die heutige Form. Erkennt auch die alte Form
    /// mit Geraetenamen ("\\.\DISPLAY1:0,0,1920,1080|…") und rechnet sie um, damit
    /// bereits gespeicherte Anordnungen nicht verloren gehen.
    /// Unlesbare Schluessel kommen unveraendert zurueck.
    /// </summary>
    public static string Normalize(string? key)
    {
        var monitors = Parse(key);
        return monitors.Count == 0 ? key ?? "" : Format(monitors);
    }

    /// <summary>
    /// Zerlegt einen Schluessel in die einzelnen Bildschirme — versteht die
    /// heutige und die alte Form. Leere Liste, wenn nichts lesbar ist.
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Parse(string? key)
    {
        var monitors = new List<(int X, int Y, int Width, int Height)>();
        if (string.IsNullOrWhiteSpace(key)) return monitors;

        foreach (var part in key.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseCurrent(part, out var m) || TryParseLegacy(part, out m))
                monitors.Add(m);
            else
                return Array.Empty<(int, int, int, int)>(); // unbekannte Form
        }
        return monitors;
    }

    /// "1920x1080@0,0"
    private static bool TryParseCurrent(string part, out (int X, int Y, int Width, int Height) monitor)
    {
        monitor = default;
        var at = part.IndexOf('@');
        if (at < 0) return false;

        var size = part[..at].Split('x');
        var position = part[(at + 1)..].Split(',');
        if (size.Length != 2 || position.Length != 2) return false;

        if (!int.TryParse(size[0], out var w) || !int.TryParse(size[1], out var h)) return false;
        if (!int.TryParse(position[0], out var x) || !int.TryParse(position[1], out var y)) return false;

        monitor = (x, y, w, h);
        return true;
    }

    /// Alte Form: "\\.\DISPLAY1:0,0,1920,1080" (Name:X,Y,Breite,Hoehe)
    private static bool TryParseLegacy(string part, out (int X, int Y, int Width, int Height) monitor)
    {
        monitor = default;
        var colon = part.LastIndexOf(':');
        if (colon < 0 || colon + 1 >= part.Length) return false;

        var numbers = part[(colon + 1)..].Split(',');
        if (numbers.Length != 4) return false;

        if (!int.TryParse(numbers[0], out var x) || !int.TryParse(numbers[1], out var y)
            || !int.TryParse(numbers[2], out var w) || !int.TryParse(numbers[3], out var h)) return false;

        monitor = (x, y, w, h);
        return true;
    }

    /// <summary>
    /// Rechnet alle gespeicherten Schluessel auf die heutige Form um — einmalig
    /// beim Start. Ohne das waeren nach der Umstellung alle bisher gemerkten
    /// Anordnungen unerreichbar. Rueckgabe: Anzahl der umgeschriebenen Eintraege.
    /// </summary>
    public static int MigrateKeys(AppConfig config)
    {
        var changed = 0;

        foreach (var fence in config.Fences)
            changed += Rewrite(fence.Layouts);

        changed += Rewrite(config.DisplayAreas);
        changed += Rewrite(config.DisplayNames);
        return changed;

        static int Rewrite<T>(Dictionary<string, T> map)
        {
            var count = 0;
            foreach (var oldKey in map.Keys.ToList())
            {
                var newKey = Normalize(oldKey);
                if (string.Equals(oldKey, newKey, StringComparison.Ordinal)) continue;

                var value = map[oldKey];
                map.Remove(oldKey);
                // Ein bereits vorhandener neuer Eintrag hat Vorrang (aktueller).
                if (!map.ContainsKey(newKey)) map[newKey] = value;
                count++;
            }
            return count;
        }
    }
}
