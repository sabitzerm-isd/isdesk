namespace ISDesk.Models;

public sealed class AppConfig
{
    public string BaseFolder { get; set; } = @"D:\Fences";
    public double DefaultOpacity { get; set; } = 0.75;
    public bool DefaultBlur { get; set; } = true;
    public List<FenceConfig> Fences { get; set; } = new();

    /// Ablage aktiv: Desktop-Dateien werden automatisch eingesammelt —
    /// bekannte in ihren gelernten Bereich, unbekannte in den Bereich "Ablage".
    public bool DesktopSweep { get; set; }

    /// Platz-Gedaechtnis: Dateiname (klein) → Tab-Ordner, in dem er zuletzt lag.
    /// So findet z. B. die neue Verknuepfung nach einem Programm-Update ihren Bereich wieder.
    public Dictionary<string, string> Placements { get; set; } = new();

    /// Zielordner fuer die Ein-Klick-Sicherung ("Automatische Sicherung").
    public string? AutoBackupFolder { get; set; }

    /// Wurde der Autostart schon einmal eingerichtet? Beim allerersten Start
    /// schaltet ISDesk ihn automatisch ein (im Tray abschaltbar).
    public bool AutostartConfigured { get; set; }

    /// Rastergroesse (Pixel) beim Verschieben/Groessenziehen von Bereichen.
    /// 0 = Ausrichten komplett aus (dann auch kein Kanten-Einrasten).
    public int GridSize { get; set; } = 20;
}

public sealed class FenceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 400;
    public double Height { get; set; } = 260;
    public double Opacity { get; set; } = 0.75;
    public double TitleBarOpacity { get; set; } = 0.15;
    public bool Blur { get; set; } = true;
    public bool Locked { get; set; }
    public int ActiveTab { get; set; }

    /// Symbol in der Titelzeile: Galerie-Dateiname (Assets\TabIcons) oder absoluter PNG-Pfad.
    public string? IconPath { get; set; }

    /// Zeigt hinter jedem Tab-Titel die Anzahl der Dateien (nur fuer diesen Bereich).
    public bool ShowTabCounts { get; set; }
    public List<TabConfig> Tabs { get; set; } = new();

    /// Fenster-Geometrie je Bildschirm-Konfiguration (Schluessel = Display-Fingerprint,
    /// z. B. Mobil / Homeoffice / Dortmund). X/Y/Width/Height oben sind der zuletzt
    /// aktive Stand und dienen als Fallback fuer unbekannte Konfigurationen.
    public Dictionary<string, LayoutRect> Layouts { get; set; } = new();
}

public sealed class LayoutRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class TabConfig
{
    public string Title { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public int IconSize { get; set; } = 32;

    /// Hintergrundfarbe des Tab-Reiters als "#RRGGBB"; null = Standard-Optik.
    public string? Color { get; set; }

    /// Symbol vor dem Tab-Titel: Galerie-Dateiname oder absoluter PNG-Pfad.
    public string? IconPath { get; set; }

    /// Manuelle Icon-Reihenfolge (Dateinamen). Neue Dateien werden hinten angefuegt,
    /// verschwundene automatisch entfernt — es wird NICHT automatisch sortiert.
    public List<string> Order { get; set; } = new();

    /// Automatik-Regel des Desktop-Einsammlers: Dateien mit diesen Endungen
    /// (ohne Punkt, z. B. "sza") landen automatisch in diesem Tab.
    public List<string> AutoExtensions { get; set; } = new();
}
