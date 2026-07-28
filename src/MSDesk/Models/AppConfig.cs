namespace MSDesk.Models;

public sealed class AppConfig
{
    /// <summary>
    /// Ordner, unter dem die Bereiche liegen.
    ///
    /// Leer = noch nicht festgelegt. Der Wert stand frueher fest auf „D:\Fences";
    /// auf einem Rechner ohne dieses Laufwerk scheiterte damit schon das Anlegen
    /// des ersten Bereichs, und MSDesk startete scheinbar gar nicht. Beim Start
    /// bestimmt <see cref="Services.BaseFolderResolver"/> deshalb einen
    /// nutzbaren Ort und traegt ihn hier ein.
    /// </summary>
    public string BaseFolder { get; set; } = "";
    public double DefaultOpacity { get; set; } = 0.75;
    public bool DefaultBlur { get; set; } = true;
    public List<FenceConfig> Fences { get; set; } = new();

    /// Ablage aktiv: Desktop-Dateien werden automatisch eingesammelt —
    /// bekannte in ihren gelernten Bereich, unbekannte in den Bereich "Ablage".
    public bool DesktopSweep { get; set; }

    /// Platz-Gedaechtnis: Dateiname (klein) → Tab-Ordner, in dem er zuletzt lag.
    /// So findet z. B. die neue Verknuepfung nach einem Programm-Update ihren Bereich wieder.
    public Dictionary<string, string> Placements { get; set; } = new();

    /// <summary>
    /// Zweites Platz-Gedaechtnis, diesmal ueber das ZIEL einer Verknuepfung
    /// (die zugehoerige Programmdatei, klein geschrieben) → Tab-Ordner.
    ///
    /// Noetig, weil Programm-Updates die Verknuepfung oft umbenennen
    /// („Camtasia 2024.lnk" → „Camtasia 2025.lnk"). Ueber den Dateinamen allein
    /// findet man den alten Platz dann nicht mehr wieder, ueber das Ziel schon.
    /// </summary>
    public Dictionary<string, string> TargetPlacements { get; set; } = new();

    /// Zielordner fuer die Ein-Klick-Sicherung ("Automatische Sicherung").
    public string? AutoBackupFolder { get; set; }

    /// <summary>
    /// Taegliche Sicherung ohne Zutun. Standardmaessig an — eine Sicherung, an
    /// die man denken muss, ist keine. Sie laeuft still im Hintergrund: kein
    /// Fenster, keine Meldung, auch nicht im Fehlerfall (der Zielordner kann in
    /// der Cloud liegen und gerade nicht erreichbar sein).
    /// </summary>
    public bool AutoBackupDaily { get; set; } = true;

    /// Zeitpunkt der letzten selbsttaetigen Sicherung (UTC). Null = noch keine.
    /// Bewusst UTC: sonst verschiebt die Zeitumstellung den Abstand.
    public DateTime? LastAutoBackupUtc { get; set; }

    /// Anzahl der Sicherungen, die im Zielordner aufgehoben werden.
    public int AutoBackupKeep { get; set; } = 5;

    /// Wurde der Autostart schon einmal eingerichtet? Beim allerersten Start
    /// schaltet MSDesk ihn automatisch ein (im Tray abschaltbar).
    public bool AutostartConfigured { get; set; }

    /// Soll MSDesk mit Windows starten? Wird nur ueber den Tray-Schalter
    /// veraendert. Solange true, wird ein fehlender Eintrag beim Start wieder
    /// angelegt — sonst bliebe der Autostart nach einem Verlust dauerhaft aus.
    public bool AutostartWanted { get; set; } = true;

    /// Rastergroesse (Pixel) beim Verschieben/Groessenziehen von Bereichen.
    /// 0 = Ausrichten komplett aus (dann auch kein Kanten-Einrasten).
    public int GridSize { get; set; } = 20;

    /// Kanten-Einrasten an benachbarten Bereichen (unabhaengig vom Raster).
    public bool EdgeSnap { get; set; } = true;

    /// Zwischenraum zwischen benachbarten Bereichen in Millimetern, in dem beim
    /// Verschieben eingerastet wird. 0 = bündig aneinander.
    public double SnapGapMillimeters { get; set; } = 6;

    /// Wurde die Anleitung beim ersten Start schon gezeigt?
    public bool HelpShown { get; set; }

    /// Wurde der Erststart-Assistent (Name, Sicherungsort) bereits durchlaufen?
    public bool SetupCompleted { get; set; }

    /// Vor- und Nachname des Anwenders — erscheint in den Optionen und im
    /// Dateinamen der Sicherungen, damit sich Sicherungen zuordnen lassen.
    public string UserFirstName { get; set; } = "";
    public string UserLastName { get; set; } = "";

    /// Vollstaendiger Name, sofern hinterlegt (sonst leer).
    public string UserFullName => $"{UserFirstName} {UserLastName}".Trim();

    /// Globaler Hauptschalter fuer den Milchglas-Effekt. Aus = kein Bereich
    /// zeichnet ihn (spart Speicher und Rechenzeit), unabhaengig von der
    /// Einstellung des einzelnen Bereichs.
    public bool BlurEnabled { get; set; } = true;

    /// Fehlende Website-Symbole beim Anzeigen automatisch aus dem Netz holen.
    public bool AutoFavicons { get; set; } = true;

    /// <summary>
    /// Wurde schon einmal angeboten, den Papierkorb vom Desktop auszublenden?
    ///
    /// Liegt er in einem Bereich, steht er trotzdem weiter auf dem Desktop —
    /// Windows laesst ihn dort weder verschieben noch loeschen. Man sieht ihn
    /// dann doppelt. Ausblenden ist eine Windows-Einstellung; MSDesk fragt
    /// deshalb EINMAL nach und nie wieder, egal wie die Antwort ausfaellt.
    /// </summary>
    public bool RecycleBinHideAsked { get; set; }

    /// Eigene Namen fuer Bildschirm-Konfigurationen: Fingerabdruck → Name
    /// (z. B. „Homeoffice", „Mobil", „Dortmund"). Reine Anzeigehilfe.
    public Dictionary<string, string> DisplayNames { get; set; } = new();

    /// Groesse der Arbeitsflaeche (DIP) je Bildschirm-Konfiguration. Dient dazu,
    /// die Anordnung beim ERSTEN Wechsel auf eine unbekannte Konfiguration
    /// anteilig umzurechnen, statt die Bereiche irgendwo landen zu lassen.
    public Dictionary<string, LayoutRect> DisplayAreas { get; set; } = new();

    /// Eigene Notizen zu Eintraegen: Dateiname (klein) → Notiztext.
    /// Bewusst ueber den Dateinamen (wie <see cref="Placements"/>), damit die
    /// Notiz erhalten bleibt, wenn ein Eintrag in einen anderen Tab wandert.
    public Dictionary<string, string> Notes { get; set; } = new();
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

    /// Ausgeblendet: Tab bleibt in der Konfiguration (und beim Abgleich erhalten),
    /// wird aber nicht angezeigt.
    public bool Hidden { get; set; }

    /// Feste Reihenfolge in der Tab-Leiste (kleiner = weiter vorn). Der
    /// Favoriten-Tab nutzt einen negativen Wert, damit er immer zuerst steht.
    public int SortOrder { get; set; }

    /// Manuelle Icon-Reihenfolge (Dateinamen). Neue Dateien werden hinten angefuegt,
    /// verschwundene automatisch entfernt — es wird NICHT automatisch sortiert.
    public List<string> Order { get; set; } = new();

    /// Automatik-Regel des Desktop-Einsammlers: Dateien mit diesen Endungen
    /// (ohne Punkt, z. B. "sza") landen automatisch in diesem Tab.
    public List<string> AutoExtensions { get; set; } = new();

    /// Darstellung: false = Kacheln (grosse Symbole), true = Liste mit Notizspalte.
    public bool ListView { get; set; }
}
