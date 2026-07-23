namespace ISDesk.Models;

public sealed class AppConfig
{
    public string BaseFolder { get; set; } = @"D:\Fences";
    public double DefaultOpacity { get; set; } = 0.75;
    public bool DefaultBlur { get; set; } = true;
    public List<FenceConfig> Fences { get; set; } = new();
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
}
