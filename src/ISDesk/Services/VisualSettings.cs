namespace ISDesk.Services;

/// Globale Schalter, die viele Fenster/Tabs gleichzeitig betreffen. Bewusst
/// statisch, damit Fenster und ViewModels sie ohne Umweg lesen koennen; die
/// Werte kommen beim Start aus der Konfiguration und werden von den Optionen
/// gesetzt.
public static class VisualSettings
{
    /// Milchglas-Effekt global (Hauptschalter ueber die Pro-Bereich-Einstellung).
    public static bool BlurEnabled { get; private set; } = true;

    /// Fehlende Website-Symbole automatisch nachladen.
    public static bool AutoFavicons { get; private set; } = true;

    /// Wird ausgeloest, wenn sich BlurEnabled aendert (Fenster zeichnen neu).
    public static event Action? BlurChanged;

    public static void Init(bool blurEnabled, bool autoFavicons)
    {
        BlurEnabled = blurEnabled;
        AutoFavicons = autoFavicons;
    }

    public static void SetBlurEnabled(bool value)
    {
        if (BlurEnabled == value) return;
        BlurEnabled = value;
        BlurChanged?.Invoke();
    }

    public static void SetAutoFavicons(bool value) => AutoFavicons = value;
}
