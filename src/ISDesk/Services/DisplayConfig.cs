namespace ISDesk.Services;

/// Identifiziert die aktuelle Bildschirm-Konfiguration (welche Monitore, welche
/// Aufloesungen/Positionen) als stabilen Text-Fingerprint. Damit merkt sich ISDesk
/// je Konfiguration (z. B. nur Laptop / Homeoffice mit 3 Monitoren / Dortmund)
/// ein eigenes Layout aller Bereiche.
public static class DisplayConfig
{
    private static string? _cached;

    public static string Current => _cached ??= Compute();

    /// Nach einem Wechsel der Bildschirm-Konfiguration aufrufen.
    public static void Invalidate() => _cached = null;

    private static string Compute()
        => string.Join("|", System.Windows.Forms.Screen.AllScreens
            .OrderBy(s => s.DeviceName, StringComparer.Ordinal)
            .Select(s => $"{s.DeviceName}:{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height}"));
}
