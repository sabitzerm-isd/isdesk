using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Ein angeschlossener Bildschirm, aufbereitet fuer die Anzeige in den Optionen.
/// <paramref name="Lage"/> beschreibt in Worten, wo er steht — die nackten
/// Koordinaten allein sind erklaerungsbeduerftig.
/// </summary>
public sealed record MonitorInfo(string Name, string Details, string Lage, bool IsPrimary);

/// Eine gespeicherte Bildschirm-Konfiguration samt Anzahl der darin gemerkten Bereiche.
/// <paramref name="Name"/> ist der selbst vergebene Name (z. B. „Homeoffice"),
/// <paramref name="Description"/> die technische Beschreibung der Monitore.
/// <paramref name="PreviewPath"/> = Vorschaubild der Anordnung (leer, wenn keins vorliegt).
public sealed record SavedLayoutInfo(string Key, string Name, string Description, int FenceCount,
                                     bool IsCurrent, string PreviewPath, bool HasPreview);

/// <summary>
/// Bereitet auf, welche Bildschirme gerade angeschlossen sind und welche
/// Konfigurationen MSDesk bereits gespeichert hat. Dient der Kontrolle: so ist
/// vor einem Ortswechsel sichtbar, ob das Layout wirklich gesichert wurde.
/// </summary>
public static class DisplayOverview
{
    /// Die aktuell angeschlossenen Bildschirme.
    public static List<MonitorInfo> ConnectedMonitors()
    {
        var list = new List<MonitorInfo>();
        try
        {
            foreach (var screen in System.Windows.Forms.Screen.AllScreens
                         .OrderByDescending(s => s.Primary)
                         .ThenBy(s => s.Bounds.X))
            {
                var b = screen.Bounds;
                list.Add(new MonitorInfo(
                    FriendlyName(screen.DeviceName),
                    $"{b.Width} × {b.Height} Pixel",
                    Lagebeschreibung(screen),
                    screen.Primary));
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DisplayOverview.ConnectedMonitors");
        }
        return list;
    }

    /// Alle gespeicherten Konfigurationen — neueste Bewertung: die aktuelle zuerst.
    public static List<SavedLayoutInfo> SavedConfigurations(AppConfig config)
    {
        var current = DisplayConfig.Current;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var fence in config.Fences)
        {
            foreach (var key in fence.Layouts.Keys)
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        // Die aktuelle Konfiguration immer zeigen, auch wenn noch nichts gemerkt ist.
        if (!counts.ContainsKey(current)) counts[current] = 0;

        return counts
            .Select(kv =>
            {
                var bild = LayoutPreview.FileFor(kv.Key);
                var vorhanden = System.IO.File.Exists(bild);
                return new SavedLayoutInfo(
                    kv.Key,
                    config.DisplayNames.TryGetValue(kv.Key, out var name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : "Ohne Namen",
                    Describe(kv.Key),
                    kv.Value,
                    string.Equals(kv.Key, current, StringComparison.Ordinal),
                    vorhanden ? bild : "",
                    vorhanden);
            })
            .OrderByDescending(i => i.IsCurrent)
            .ThenByDescending(i => i.FenceCount)
            .ToList();
    }

    /// Macht aus dem Fingerabdruck einen lesbaren Text (versteht auch die alte Form).
    public static string Describe(string key)
    {
        var monitors = DisplayConfig.Parse(key);
        if (monitors.Count == 0) return "unbekannt";

        var sizes = monitors.Select(m => $"{m.Width} × {m.Height}").ToList();
        var count = sizes.Count == 1 ? "1 Bildschirm" : $"{sizes.Count} Bildschirme";
        return $"{count}: {string.Join(", ", sizes)}";
    }

    /// <summary>
    /// Beschreibt in Worten, wo ein Bildschirm steht.
    ///
    /// Windows legt alle Bildschirme in EIN gemeinsames Koordinatensystem; der
    /// Hauptbildschirm liegt dabei immer im Nullpunkt. Die Zahlen „3440/172"
    /// bedeuten also: rechts neben dem Hauptbildschirm und 172 Pixel tiefer.
    /// Das versteht man ohne Erklaerung nicht — deshalb hier im Klartext.
    /// </summary>
    private static string Lagebeschreibung(System.Windows.Forms.Screen screen)
    {
        var b = screen.Bounds;
        if (screen.Primary) return $"Hauptbildschirm · Nullpunkt der Anordnung ({b.X}/{b.Y})";

        var haupt = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                    ?? new System.Drawing.Rectangle(0, 0, 0, 0);

        var teile = new List<string>();

        if (b.X >= haupt.Right) teile.Add("rechts daneben");
        else if (b.Right <= haupt.X) teile.Add("links daneben");
        else if (b.Y >= haupt.Bottom) teile.Add("darunter");
        else if (b.Bottom <= haupt.Y) teile.Add("darüber");
        else teile.Add("überlappend angeordnet");

        var versatz = b.Y - haupt.Y;
        if (Math.Abs(versatz) >= 10)
            teile.Add(versatz > 0 ? $"{versatz} Pixel tiefer" : $"{-versatz} Pixel höher");
        else
            teile.Add("oben bündig");

        return $"{string.Join(", ", teile)} · Position {b.X}/{b.Y}";
    }

    /// "\\.\DISPLAY1" → "Bildschirm 1"
    private static string FriendlyName(string deviceName)
    {
        var digits = new string(deviceName.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"Bildschirm {digits}" : deviceName;
    }
}
