using System.IO;
using System.Windows;
using MSDesk.Models;
using MSDesk.ViewModels;
using MSDesk.Views;
using Microsoft.VisualBasic.FileIO;

namespace MSDesk.Services;

public sealed class FenceManager
{
    private readonly ConfigService _config;
    private readonly List<FenceWindow> _windows = new();

    public FenceManager(ConfigService config) => _config = config;

    /// Wird von App nach der Konstruktion gesetzt (Sicherung/Wiederherstellung).
    public BackupService? Backup { get; set; }

    /// Wird von App gesetzt (Desktop-Einsammler fuer die Ablage).
    public DesktopSweeper? Sweeper { get; set; }

    /// Wird von App gesetzt (Chrome-Lesezeichen-Import).
    public BookmarkImportService? Bookmarks { get; set; }

    public bool DesktopSweepEnabled => _config.Config.DesktopSweep;

    public void PersistNow() => _config.SaveDebounced();

    /// Schmale Sicht auf die Bereiche (fuer das Zurueckgeben auf den Desktop).
    public AppConfigSource ConfigSource() => AppConfigSource.From(_config);

    /// Irgendein offener Bereich — fuer Dialoge, die aus dem Tray heraus
    /// geoeffnet werden und ein Bezugs-ViewModel brauchen.
    public ViewModels.FenceViewModel? FirstFenceViewModel()
        => _windows.FirstOrDefault()?.ViewModel;

    /// Rundet Position UND Groesse ALLER Bereiche auf das eingestellte Raster —
    /// bringt gewachsene, krumme Layouts in einem Schritt in Ordnung.
    public int SnapAllToGrid()
    {
        var grid = _config.Config.GridSize;
        if (grid <= 0) return 0;

        var changed = 0;
        foreach (var window in _windows)
        {
            var vm = window.ViewModel;
            double x = Round(vm.X, grid), y = Round(vm.Y, grid);
            double w = Math.Max(grid * 3, Round(vm.Width, grid));
            double h = Math.Max(grid * 3, Round(vm.Height, grid));

            if (Same(x, vm.X) && Same(y, vm.Y) && Same(w, vm.Width) && Same(h, vm.Height)) continue;

            window.Left = x; window.Top = y;
            window.Width = w; window.Height = h;
            changed++;
        }
        _config.SaveDebounced();
        return changed;

        static double Round(double value, int grid) => Math.Round(value / grid) * grid;
        static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;
    }

    /// <summary>
    /// Setzt alle Bereiche auf dieselbe Hoehe (Breite und Position bleiben).
    /// Gleiche Hoehen lassen eine Anordnung deutlich ruhiger wirken.
    /// Rueckgabe: Anzahl der geaenderten Bereiche.
    /// </summary>
    public int ApplyHeightToAll(double hoehe)
    {
        var geaendert = 0;
        foreach (var window in _windows)
        {
            if (Math.Abs(window.Height - hoehe) < 0.5) continue;
            window.Height = Math.Max(80, hoehe);
            geaendert++;
        }

        DisplayConfig.Invalidate();
        _currentLayoutKey = DisplayConfig.Current;
        StoreLayout(_currentLayoutKey);
        _config.Save();

        StartupLog.Layout("VON HAND: Höhe für alle", _currentLayoutKey,
                          $"{hoehe:F0} Pixel", Bereichsliste());
        return geaendert;
    }

    /// Uebertraegt die Groesse eines Bereichs auf alle anderen (gleiche Optik).
    public int ApplySizeToAll(FenceViewModel source)
    {
        var changed = 0;
        foreach (var window in _windows)
        {
            if (ReferenceEquals(window.ViewModel, source)) continue;
            if (Math.Abs(window.Width - source.Width) < 0.5
                && Math.Abs(window.Height - source.Height) < 0.5) continue;

            window.Width = source.Width;
            window.Height = source.Height;
            changed++;
        }
        _config.SaveDebounced();
        return changed;
    }

    /// Setzt Groesse (und optional Position) eines Bereichs auf exakte Werte.
    public void SetGeometry(FenceViewModel vm, double? x, double? y, double? width, double? height)
    {
        var window = _windows.FirstOrDefault(w => ReferenceEquals(w.ViewModel, vm));
        if (window == null) return;

        if (x.HasValue) window.Left = x.Value;
        if (y.HasValue) window.Top = y.Value;
        if (width.HasValue) window.Width = Math.Max(110, width.Value);
        if (height.HasValue) window.Height = Math.Max(80, height.Value);
        _config.SaveDebounced();
    }

    /// Zwischenraum (mm), in dem Bereiche neben- und untereinander einrasten.
    public double SnapGapMillimeters
    {
        get => _config.Config.SnapGapMillimeters;
        set
        {
            if (Math.Abs(_config.Config.SnapGapMillimeters - value) < 0.01) return;
            _config.Config.SnapGapMillimeters = value;
            Interop.GridSnapBehavior.GapMillimeters = value;
            _config.SaveDebounced();
        }
    }

    /// Kanten-Einrasten an anderen Bereichen (separat vom Raster schaltbar).
    public bool EdgeSnapEnabled
    {
        get => _config.Config.EdgeSnap;
        set
        {
            if (_config.Config.EdgeSnap == value) return;
            _config.Config.EdgeSnap = value;
            Interop.GridSnapBehavior.EdgeSnapEnabled = value;
            _config.SaveDebounced();
        }
    }

    /// Globaler Milchglas-Schalter (Hauptschalter ueber die Pro-Bereich-Einstellung).
    public bool BlurEnabled
    {
        get => _config.Config.BlurEnabled;
        set
        {
            if (_config.Config.BlurEnabled == value) return;
            _config.Config.BlurEnabled = value;
            VisualSettings.SetBlurEnabled(value); // zeichnet alle Bereiche neu
            _config.SaveDebounced();
        }
    }

    /// Fehlende Website-Symbole automatisch nachladen.
    public bool AutoFavicons
    {
        get => _config.Config.AutoFavicons;
        set
        {
            if (_config.Config.AutoFavicons == value) return;
            _config.Config.AutoFavicons = value;
            VisualSettings.SetAutoFavicons(value);
            _config.SaveDebounced();
        }
    }

    /// Refresh-Button der Ablage: Regeln bereichsuebergreifend anwenden und
    /// (falls aktiviert) den Desktop einsammeln — im Hintergrund.
    public void RunRulesNow()
    {
        var sweeper = Sweeper;
        if (sweeper == null) return;
        Task.Run(() =>
        {
            sweeper.ApplyRulesEverywhere();
            sweeper.SweepNow();
        });
    }

    public string? AutoBackupFolder
    {
        get => _config.Config.AutoBackupFolder;
        set
        {
            _config.Config.AutoBackupFolder = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            _config.SaveDebounced();
        }
    }

    /// Rastergroesse beim Verschieben/Groessenziehen (0 = Ausrichten aus).
    /// Wirkt sofort auf alle offenen Bereiche.
    public int GridSize
    {
        get => _config.Config.GridSize;
        set
        {
            var size = Math.Max(0, value);
            if (_config.Config.GridSize == size) return;
            _config.Config.GridSize = size;
            Interop.GridSnapBehavior.GridSize = size;
            _config.SaveDebounced();
        }
    }

    /// Schaltet die Ablage um: an = Bereich "Ablage" sicherstellen + Einsammler starten.
    public void SetDesktopSweep(bool enabled)
    {
        _config.Config.DesktopSweep = enabled;
        _config.SaveDebounced();
        if (enabled)
        {
            EnsureAblageFence();
            Sweeper?.Start();
        }
        else
        {
            Sweeper?.Stop();
        }
    }

    /// Tab-Ordner des Ablage-Bereichs ("" wenn nicht vorhanden) — threadsicher lesbar.
    public string GetAblageFolder()
        => _config.Config.Fences.FirstOrDefault(f =>
                string.Equals(f.Title, "Ablage", StringComparison.OrdinalIgnoreCase))
            ?.Tabs.FirstOrDefault()?.FolderPath ?? "";

    private void EnsureAblageFence()
    {
        if (_config.Config.Fences.Any(f =>
                string.Equals(f.Title, "Ablage", StringComparison.OrdinalIgnoreCase)))
            return;
        var window = CreateFence("Ablage");
        window.ViewModel.IconPath = "download.png";
    }

    /// Sorgt dafuer, dass der Bereich "Lesezeichen" existiert und alle uebergebenen
    /// Tabs (Chrome-Ordner) enthaelt. Neue Tabs werden angelegt und ins offene
    /// Fenster gespiegelt; der aktive Tab bleibt.
    public void EnsureBookmarksFence(string fenceFolder, List<string> tabNames)
    {
        var cfg = _config.Config.Fences.FirstOrDefault(f =>
            string.Equals(f.Title, BookmarkImportService.FenceTitle, StringComparison.OrdinalIgnoreCase));

        if (cfg == null)
        {
            cfg = new FenceConfig
            {
                Id = Guid.NewGuid(),
                Title = BookmarkImportService.FenceTitle,
                Width = 620, Height = 380,
                Opacity = _config.Config.DefaultOpacity,
                TitleBarOpacity = 0.15,
                Blur = _config.Config.DefaultBlur,
                IconPath = "web2.png",
                ActiveTab = 0
            };
            _config.Config.Fences.Add(cfg);
        }

        foreach (var tab in tabNames)
        {
            var folder = Path.Combine(fenceFolder, SanitizeLeaf(tab));
            if (!cfg.Tabs.Any(t => string.Equals(t.FolderPath, folder, StringComparison.OrdinalIgnoreCase)))
                cfg.Tabs.Add(new TabConfig { Title = tab, FolderPath = folder, IconSize = 32 });
        }
        if (cfg.Tabs.Count == 0)
            cfg.Tabs.Add(new TabConfig { Title = "Leiste", FolderPath = fenceFolder, IconSize = 32 });

        _config.SaveDebounced();

        // Wenn der Bereich schon offen ist, neue Tabs live nachziehen; sonst oeffnen.
        var open = _windows.FirstOrDefault(w => w.ViewModel.Id == cfg.Id);
        if (open == null) OpenFence(cfg);
        else open.ViewModel.SyncTabsFromConfig();
    }

    /// Loest den Lesezeichen-Abgleich aus (Refresh-Button des Lesezeichen-Bereichs):
    /// Chrome UND Firefox, Rueckgabe ist die Summe der neuen Links.
    public int SyncBookmarks()
    {
        if (Bookmarks is not { } bookmarks) return 0;
        var added = 0;
        if (bookmarks.ChromeAvailable) added += bookmarks.SyncChrome();
        if (bookmarks.FirefoxAvailable) added += bookmarks.SyncFirefox();
        return added;
    }

    public IReadOnlyList<FenceWindow> Windows => _windows;

    /// Oeffnet je FenceConfig ein FenceWindow.
    public void OpenAll()
    {
        foreach (var fence in _config.Config.Fences.ToList())
            OpenFence(fence);
    }

    private FenceWindow OpenFence(FenceConfig fenceConfig)
    {
        EnsureOnScreen(fenceConfig);
        var vm = new FenceViewModel(fenceConfig, _config.Config.BaseFolder, _config.SaveDebounced);
        var window = new FenceWindow(vm) { Manager = this };
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Show();
        return window;
    }

    /// Legt Ordner <BaseFolder>\<title> + Standard-Tab "Allgemein" an, oeffnet das Fenster, persistiert.
    /// Der neue Bereich wird an einer FREIEN Stelle platziert (nicht ueber dem Ausloeser).
    public FenceWindow CreateFence(string title, FenceWindow? near = null)
    {
        var folder = MakeUniqueFolder(_config.Config.BaseFolder, SanitizeLeaf(title));
        Directory.CreateDirectory(folder);
        var tabFolder = Path.Combine(folder, "Allgemein");
        Directory.CreateDirectory(tabFolder);

        // Neue Bereiche erben das aktuelle Erscheinungsbild der bestehenden.
        var template = _config.Config.Fences.FirstOrDefault();
        const double width = 400, height = 260;
        var at = FindFreePosition(near, width, height);
        var fenceConfig = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = title,
            X = at.X,
            Y = at.Y,
            Width = width,
            Height = height,
            Opacity = template?.Opacity ?? _config.Config.DefaultOpacity,
            TitleBarOpacity = template?.TitleBarOpacity ?? 0.15,
            Blur = template?.Blur ?? _config.Config.DefaultBlur,
            Locked = template?.Locked ?? false,
            ActiveTab = 0
        };
        fenceConfig.Tabs.Add(new TabConfig { Title = "Allgemein", FolderPath = tabFolder, IconSize = 32 });

        _config.Config.Fences.Add(fenceConfig);
        _config.SaveDebounced();
        return OpenFence(fenceConfig);
    }

    /// Trennt einen Tab ab und macht daraus einen eigenen Bereich daneben.
    public void DetachTabToNewFence(FenceViewModel from, TabViewModel tab)
    {
        if (from.Tabs.Count <= 1)
        {
            ConfirmDialog.Info("Der letzte Tab eines Bereichs kann nicht abgetrennt werden.", null);
            return;
        }

        var sourceWindow = _windows.FirstOrDefault(w => ReferenceEquals(w.ViewModel, from));
        from.DetachTab(tab);
        tab.Dispose(); // Watcher stoppen — der neue Bereich baut einen frischen auf

        var template = from.Config;
        const double width = 400, height = 260;
        var at = FindFreePosition(sourceWindow, width, height);
        var fenceConfig = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = tab.Title,
            X = at.X, Y = at.Y, Width = width, Height = height,
            Opacity = template.Opacity,
            TitleBarOpacity = template.TitleBarOpacity,
            Blur = template.Blur,
            Locked = template.Locked,
            IconPath = tab.IconPath,
            ActiveTab = 0
        };
        fenceConfig.Tabs.Add(tab.Config);

        _config.Config.Fences.Add(fenceConfig);
        _config.SaveDebounced();
        OpenFence(fenceConfig);
    }

    /// Freie Position fuer ein neues Fenster: rechts, darunter, links, darueber vom
    /// Anker — die erste Stelle, die keinen bestehenden Bereich schneidet und im
    /// Arbeitsbereich liegt; sonst Kaskade.
    private Point FindFreePosition(FenceWindow? near, double width, double height)
    {
        const double gap = 16;
        double ax = near?.Left ?? 120, ay = near?.Top ?? 120;
        double aw = near?.ActualWidth ?? 0, ah = near?.ActualHeight ?? 0;

        var wa = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)ax, (int)ay)).WorkingArea;

        var candidates = new List<Point>
        {
            new(ax + aw + gap, ay),          // rechts
            new(ax, ay + ah + gap),          // darunter
            new(ax - width - gap, ay),       // links
            new(ax, ay - height - gap),      // darueber
        };
        for (var i = 1; i <= 6; i++)
            candidates.Add(new Point(ax + 40 * i, ay + 40 * i)); // Kaskade als Fallback

        foreach (var c in candidates)
        {
            if (c.X < wa.Left || c.Y < wa.Top || c.X + width > wa.Right || c.Y + height > wa.Bottom)
                continue;
            var rect = new Rect(c.X, c.Y, width, height);
            var overlaps = _windows.Any(w =>
                rect.IntersectsWith(new Rect(w.Left, w.Top, w.ActualWidth, w.ActualHeight)));
            if (!overlaps) return c;
        }
        return new Point(ax + 40, ay + 40); // Notfall: leicht versetzt
    }

    /// Schliesst das Fenster und entfernt den Bereich aus der Config. Auf Wunsch
    /// wandern die zugehoerigen Ordner in den Papierkorb — aber ausschliesslich
    /// Ordner UNTERHALB des Basisordners; extern verknuepfte Ordner bleiben immer.
    public void RemoveFence(FenceViewModel vm, bool deleteFolders = false)
    {
        var window = _windows.FirstOrDefault(w => w.ViewModel.Id == vm.Id);
        window?.Close();
        _config.Config.Fences.RemoveAll(f => f.Id == vm.Id);
        _config.SaveDebounced();

        if (!deleteFolders) return;

        var baseFolder = Path.GetFullPath(_config.Config.BaseFolder);
        var toRecycle = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in vm.Config.Tabs)
        {
            string folder;
            try { folder = Path.GetFullPath(tab.FolderPath); }
            catch (Exception) { continue; }

            if (!IsUnder(folder, baseFolder)) continue;

            // Standardlayout Basis\Bereich\Tab → ganzen Bereichsordner entfernen,
            // liegt der Tab-Ordner direkt unter der Basis → nur ihn selbst.
            var parent = Path.GetDirectoryName(folder);
            toRecycle.Add(parent != null && IsUnder(parent, baseFolder) ? parent : folder);
        }

        foreach (var dir in toRecycle)
        {
            try
            {
                if (Directory.Exists(dir))
                    FileSystem.DeleteDirectory(dir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "RemoveFence/DeleteFolder");
            }
        }
    }

    private static bool IsUnder(string path, string baseFolder)
        => path.Length > baseFolder.Length
           && path.StartsWith(baseFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// Sperren gilt auf Wunsch des Nutzers IMMER fuer alle Bereiche gemeinsam:
    /// Ein Umschalten an einem Bereich zieht alle anderen nach.
    public void PropagateLock(bool locked)
    {
        foreach (var window in _windows)
            window.ViewModel.Locked = locked; // Setter ignoriert unveraenderte Werte
    }

    /// Erscheinungsbild (Transparenz, Titelleiste, Blur) gilt vorerst ebenfalls
    /// fuer alle Bereiche gemeinsam (Nutzer-Vorgabe; spaeter evtl. je Bereich).
    public void PropagateAppearance(FenceViewModel source)
    {
        foreach (var window in _windows)
        {
            var vm = window.ViewModel;
            if (ReferenceEquals(vm, source)) continue;
            vm.Opacity = source.Opacity;
            vm.TitleBarOpacity = source.TitleBarOpacity;
            vm.Blur = source.Blur;
        }
    }

    /// Verschiebt einen Tab samt Ordner-Zuordnung in einen anderen Bereich
    /// (auf der Platte aendert sich nichts, nur die Zuordnung wandert).
    public void MoveTab(FenceViewModel from, TabViewModel tab, FenceViewModel to)
    {
        if (ReferenceEquals(from, to)) return;
        if (from.Tabs.Count <= 1)
        {
            ConfirmDialog.Info("Der letzte Tab eines Bereichs kann nicht verschoben werden.", null);
            return;
        }
        from.DetachTab(tab);
        to.AttachTab(tab);
        _config.SaveDebounced();
    }

    /// Icon-Groesse fuer ALLE Tabs ALLER Bereiche setzen.
    public void SetIconSizeAll(int size)
    {
        foreach (var window in _windows)
            foreach (var tab in window.ViewModel.Tabs)
                tab.IconSize = size;
    }

    /// Schliesst alle Fenster OHNE zu speichern (fuer die Wiederherstellung).
    public void CloseAllWithoutSave()
    {
        foreach (var window in _windows.ToList())
            window.Close();
    }

    /// Wendet fuer die aktuelle Bildschirm-Konfiguration das gemerkte Layout an
    /// (bzw. lernt sie kennen). Wird beim Start und bei Monitor-Wechseln aufgerufen.
    public void ApplyLayoutsForCurrentDisplays()
    {
        DisplayConfig.Invalidate();
        var key = DisplayConfig.Current;

        // Die bisherige Anordnung wird hier BEWUSST NICHT mehr gesichert: Windows
        // hat die Fenster des entfallenen Monitors zu diesem Zeitpunkt laengst
        // selbst zusammengeschoben. Sie zu sichern hiesse, die gute Anordnung
        // durch diese Zwischenlage zu ersetzen. Gesichert wird stattdessen
        // laufend beim Verschieben (siehe LayoutChanged) sowie beim Beenden.

        // Die gesamte Entscheidung steckt in DisplaySwitchPlan — als reine
        // Rechnung, damit sich der komplette Ablauf (anstecken → abstecken →
        // verschieben → wieder anstecken) vollstaendig durchspielen laesst.
        var fences = _windows
            .Select(w => new DisplaySwitchPlan.Fence(
                new LayoutRect { X = w.Left, Y = w.Top, Width = w.Width, Height = w.Height },
                w.ViewModel.Config.Layouts))
            .ToList();

        var plan = DisplaySwitchPlan.Compute(fences, key, VirtualArea(), CurrentDesktopArea());

        IsApplyingLayout = true;
        try
        {
        for (var i = 0; i < _windows.Count; i++)
        {
            var window = _windows[i];
            var cfg = window.ViewModel.Config;
            var ziel = plan.Positions[i];

            // Zielwerte in EIGENEN Variablen halten — nicht ueber cfg.X/cfg.Y
            // arbeiten.
            //
            // Grund: window.Left zu setzen loest sofort OnLocationChanged aus.
            // Dieser Handler schreibt Left UND Top in das ViewModel zurueck —
            // Top hat zu diesem Zeitpunkt aber noch den ALTEN Wert der
            // vorherigen Bildschirm-Konfiguration. Ueber cfg.Y wurde damit der
            // gerade gesetzte Zielwert wieder ueberschrieben, und die
            // anschliessende Zeile "window.Top = cfg.Y" setzte den alten Wert.
            //
            // Genau daraus entstand das lange gesuchte Bild: X stimmte, Y kam
            // aus der zuvor aktiven Konfiguration.
            var zielX = ziel.X;
            var zielY = ziel.Y;
            var zielBreite = Math.Max(ziel.Width, 110);
            var zielHoehe = Math.Max(ziel.Height, 80);

            window.Left = zielX;
            window.Top = zielY;
            window.Width = zielBreite;
            window.Height = zielHoehe;

            // Erst NACH dem Setzen festschreiben: die Handler haben in der
            // Zwischenzeit Uebergangswerte hineingeschrieben.
            cfg.X = zielX;
            cfg.Y = zielY;
            cfg.Width = zielBreite;
            cfg.Height = zielHoehe;
            cfg.Layouts[key] = new LayoutRect
            {
                X = zielX, Y = zielY, Width = zielBreite, Height = zielHoehe
            };
        }
        }
        finally
        {
            IsApplyingLayout = false;
        }

        StartupLog.Layout(
            "BILDSCHIRM-WECHSEL / START",
            key,
            plan.Kind switch
            {
                DisplaySwitchPlan.PlanKind.Restored  => "gespeicherte Anordnung EXAKT wiederhergestellt",
                DisplaySwitchPlan.PlanKind.Unchanged => "passt unveraendert — nichts verschoben",
                _                                    => "neu abgeleitet (keine vollstaendige Anordnung gemerkt)"
            },
            Bereichsliste());

        // Gesamtflaeche dieser Konfiguration festhalten — Grundlage fuer ein
        // spaeteres anteiliges Uebertragen auf die naechste unbekannte.
        var current = VirtualArea();
        _config.Config.DisplayAreas[key] = new LayoutRect
        {
            X = current.X, Y = current.Y, Width = current.Width, Height = current.Height
        };

        _currentLayoutKey = key;
        _config.Save();          // sofort, nicht gebuendelt: der Wechsel ist abgeschlossen
        ResumeLayoutSaving();    // ab jetzt darf wieder laufend gesichert werden

        // Kurz warten, bis Windows den Bildschirm fertig aufgebaut hat — sonst
        // zeigt die Vorschau einen halb gezeichneten Zustand.
        var kennung = key;
        var timer = new System.Timers.Timer(2500) { AutoReset = false };
        timer.Elapsed += (_, _) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                () => LayoutPreview.Capture(kennung, erzwingen: true));
            timer.Dispose();
        };
        timer.Start();
    }

    /// Aktuelle Lage aller Bereiche — fuer das Anordnungs-Protokoll.
    private IEnumerable<(string, double, double, double, double)> Bereichsliste()
        => _windows.Select(w => (w.ViewModel.Title, w.Left, w.Top, w.Width, w.Height));

    /// Konfiguration fuer die Bildschirm-Uebersicht in den Optionen.
    public Models.AppConfig ConfigForDisplayOverview => _config.Config;

    /// Konfigurationsdienst — fuer Dialoge, die Einstellungen aendern und speichern.
    public ConfigService Config => _config;

    /// Fuer die Diagnose in den Optionen.
    public string ConfigFilePath => _config.ConfigPath;
    public int SaveCount => _config.SaveCount;

    /// Speichert die Konfiguration in Kuerze (gebuendelt, nicht bei jedem Tastendruck).
    public void SaveSoon() => _config.SaveDebounced();

    /// Holt bekannte Symbole vom Desktop zurueck in ihre Bereiche.
    public DesktopReclaim.Ergebnis ReclaimDesktopIcons(bool nurVorschau = false)
    {
        var ergebnis = DesktopReclaim.Run(_config, nurVorschau);
        if (!nurVorschau && ergebnis.Gesamt > 0) RefreshAllTabs();
        return ergebnis;
    }

    /// Entfernt doppelte Verknuepfungen innerhalb der Bereiche.
    public int RemoveDuplicateShortcuts(bool nurVorschau = false)
    {
        var anzahl = DesktopReclaim.RemoveDuplicates(_config, nurVorschau);
        if (!nurVorschau && anzahl > 0) RefreshAllTabs();
        return anzahl;
    }

    /// Laedt die Inhalte aller angezeigten Tabs neu (nach dem Verschieben von Dateien).
    private void RefreshAllTabs()
    {
        PlacementRegistry.ClearTargetCache(); // Pfade haben sich geaendert
        foreach (var window in _windows)
            window.ViewModel.ActiveTab?.Reload();
    }

    private System.Timers.Timer? _layoutSaveTimer;

    /// <summary>
    /// Meldet, dass ein Bereich verschoben oder in der Groesse veraendert wurde.
    /// Die Anordnung wird kurz darauf automatisch fuer die aktuelle
    /// Bildschirm-Konfiguration festgehalten — gebuendelt, damit waehrend des
    /// Ziehens nicht bei jedem Pixel geschrieben wird.
    /// </summary>
    private bool _layoutSavingSuspended;

    /// <summary>
    /// Waehrend eines Bildschirmwechsels darf keine Anordnung gespeichert werden.
    /// Windows schiebt beim Abstecken die Fenster des entfallenen Monitors selbst
    /// auf den verbleibenden — diese Zwischenlage wuerde sonst die gemerkte
    /// Anordnung der bisherigen Konfiguration ueberschreiben und waere fuer immer
    /// verloren.
    /// </summary>
    public void SuspendLayoutSaving()
    {
        _layoutSavingSuspended = true;
        _layoutSaveTimer?.Stop();
    }

    public void ResumeLayoutSaving() => _layoutSavingSuspended = false;

    /// <summary>
    /// True, solange eine Anordnung auf die Fenster uebertragen wird.
    ///
    /// Absicherung gegen einen bereits behobenen, aber heiklen Mechanismus:
    /// Das Setzen von Window.Left loest noch WAEHREND der Zuweisung
    /// OnLocationChanged aus, und der Handler schreibt Position und Groesse in
    /// die Konfiguration zurueck — mit Werten, die zu diesem Zeitpunkt erst
    /// halb gesetzt sind. Solange dieses Flag steht, unterbleibt das
    /// Zurueckschreiben.
    /// </summary>
    public bool IsApplyingLayout { get; private set; }

    private System.Timers.Timer? _displayWatch;

    /// <summary>
    /// Sicherheitsnetz: prueft regelmaessig, ob sich die Bildschirm-Konfiguration
    /// geaendert hat. Windows meldet das nicht in jedem Fall — beim blossen
    /// EINSCHALTEN eines bereits angesteckten Monitors bleibt das Ereignis
    /// gelegentlich aus. Ohne diese Pruefung arbeitet MSDesk dann mit der
    /// falschen Konfiguration weiter.
    ///
    /// Kostet praktisch nichts: ein Aufruf alle drei Sekunden, der nur die
    /// Bildschirmliste ausliest.
    /// </summary>
    public void StartDisplayWatch()
    {
        if (_displayWatch != null) return;

        _displayWatch = new System.Timers.Timer(3000) { AutoReset = true };
        _displayWatch.Elapsed += (_, _) =>
        {
            var app = System.Windows.Application.Current;
            if (app == null) return;

            app.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_layoutSavingSuspended) return; // Wechsel laeuft bereits

                    DisplayConfig.Invalidate();
                    var aktuell = DisplayConfig.Current;
                    if (_currentLayoutKey == null
                        || string.Equals(aktuell, _currentLayoutKey, StringComparison.Ordinal)) return;

                    StartupLog.Write($"Bildschirmwechsel selbst erkannt (kein Ereignis von Windows): " +
                                      $"{_currentLayoutKey} → {aktuell}");
                    ApplyLayoutsForCurrentDisplays();
                }
                catch (Exception ex)
                {
                    App.LogCrash(ex, "DisplayWatch");
                }
            });
        };
        _displayWatch.Start();
    }

    public void LayoutChanged()
    {
        if (_layoutSavingSuspended) return;

        if (_layoutSaveTimer == null)
        {
            _layoutSaveTimer = new System.Timers.Timer(1500) { AutoReset = false };
            _layoutSaveTimer.Elapsed += (_, _) =>
            {
                var app = System.Windows.Application.Current;
                if (app == null) return;
                app.Dispatcher.BeginInvoke(() =>
                {
                    // Erneut pruefen: der Timer kann bereits gelaufen sein, als
                    // waehrend eines Bildschirmwechsels gesperrt wurde.
                    if (_layoutSavingSuspended) return;

                    // Kennung frisch ermitteln — nie auf den gemerkten Wert vertrauen.
                    DisplayConfig.Invalidate();
                    var aktuell = DisplayConfig.Current;

                    if (!string.Equals(aktuell, _currentLayoutKey, StringComparison.Ordinal))
                    {
                        // WICHTIG: Der Bildschirmwechsel ist unbemerkt geblieben —
                        // das kommt vor, wenn ein Monitor nur ein- statt
                        // angeschaltet wird und Windows kein Ereignis meldet.
                        //
                        // Jetzt zu speichern waere fatal: Die Fenster stehen noch
                        // an den Positionen der VORHERIGEN Konfiguration, und die
                        // wuerden unter der NEUEN Kennung abgelegt. Genau dadurch
                        // tauchten am Laptop vorgenommene Verschiebungen
                        // anschliessend am Doppelmonitor wieder auf.
                        //
                        // Stattdessen wird die Anordnung der neuen Konfiguration
                        // angewandt. Gespeichert wird erst wieder, wenn danach
                        // wirklich etwas verschoben wird.
                        StartupLog.Write($"Kennung hatte sich unbemerkt geaendert: {_currentLayoutKey} → {aktuell}. " +
                                          "Es wird NICHT gespeichert, sondern die passende Anordnung geladen.");
                        ApplyLayoutsForCurrentDisplays();
                        return;
                    }

                    StoreLayout(aktuell);
                    _config.Save();

                    StartupLog.Layout("GESICHERT (nach Verschieben)", aktuell,
                                      $"{_windows.Count} Bereiche gespeichert", Bereichsliste());

                    // Vorschaubild auffrischen (gedrosselt, siehe LayoutPreview).
                    LayoutPreview.Capture(aktuell);
                });
            };
        }

        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Start();
    }

    /// Merkt den im Tray gewaehlten Autostart-Wunsch dauerhaft.
    public void SetAutostartWanted(bool wanted)
    {
        _config.Config.AutostartWanted = wanted;
        _config.SaveDebounced();
    }

    /// Bildschirm-Konfiguration, deren Anordnung gerade aktiv ist.
    private string? _currentLayoutKey;

    /// <summary>
    /// Sichert die aktuelle Anordnung aller Bereiche ausdruecklich fuer die
    /// gerade aktive Bildschirm-Konfiguration — wird sofort geschrieben, damit
    /// das Ergebnis in den Optionen unmittelbar nachpruefbar ist.
    /// </summary>
    public void SaveLayoutForCurrentDisplays()
    {
        DisplayConfig.Invalidate();
        _currentLayoutKey = DisplayConfig.Current;
        StoreLayout(_currentLayoutKey);
        _config.Save();

        StartupLog.Layout("VON HAND GESICHERT", _currentLayoutKey,
                          $"{_windows.Count} Bereiche gespeichert", Bereichsliste());

        // Von Hand gesichert = ausdruecklich gewollter Stand → Bild in jedem Fall.
        LayoutPreview.Capture(_currentLayoutKey, erzwingen: true);
    }

    /// <summary>
    /// Ordnet ALLE Bereiche auf der aktuellen Arbeitsflaeche neu an —
    /// ueberschneidungsfrei, in der bisherigen Leserichtung. Fuer den Fall,
    /// dass eine Anordnung bereits mit Ueberlappungen gespeichert wurde.
    /// Rueckgabe: Anzahl der Bereiche.
    /// </summary>
    public int RearrangeOnCurrentScreen()
    {
        if (_windows.Count == 0) return 0;

        var area = CurrentDesktopArea();
        var vorher = _windows
            .Select(w => new LayoutRect { X = w.Left, Y = w.Top, Width = w.Width, Height = w.Height })
            .ToList();

        // Quellflaeche so waehlen, dass sie alle Bereiche umfasst — liegt einer
        // ausserhalb, waere seine relative Lage sonst groesser als 100 %.
        var from = LayoutTransfer.Enclose(area, vorher) ?? area;
        var nachher = LayoutTransfer.Arrange(vorher, from, area);

        for (var i = 0; i < _windows.Count; i++)
        {
            var window = _windows[i];
            window.Left = nachher[i].X;
            window.Top = nachher[i].Y;
            window.Width = nachher[i].Width;
            window.Height = nachher[i].Height;
        }

        DisplayConfig.Invalidate();
        _currentLayoutKey = DisplayConfig.Current;
        StoreLayout(_currentLayoutKey);
        _config.Save();

        StartupLog.Layout("VON HAND: Bereiche neu anordnen", _currentLayoutKey,
                          "ueberschneidungsfrei geschoben", Bereichsliste());
        return _windows.Count;
    }

    /// <summary>
    /// Ordnet alle Bereiche an einem gedachten Raster an — gleicher Abstand
    /// ueberall, Groessen unveraendert. Rueckgabe: Anzahl der Bereiche.
    /// </summary>
    public int ArrangeOnGrid()
    {
        if (_windows.Count == 0) return 0;

        var flaeche = CurrentDesktopArea();
        var abstand = Math.Max(8, _config.Config.GridSize > 0 ? _config.Config.GridSize : 16);

        var lage = _windows
            .Select(w => new LayoutRect { X = w.Left, Y = w.Top, Width = w.Width, Height = w.Height })
            .ToList();

        var neu = LayoutTransfer.ArrangeOnGrid(lage, flaeche, abstand);

        for (var i = 0; i < _windows.Count; i++)
        {
            var window = _windows[i];
            window.Left = neu[i].X;
            window.Top = neu[i].Y;
            // Breite/Hoehe bewusst NICHT anfassen.
        }

        DisplayConfig.Invalidate();
        _currentLayoutKey = DisplayConfig.Current;
        StoreLayout(_currentLayoutKey);
        _config.Save();

        StartupLog.Layout("VON HAND: Am Raster anordnen", _currentLayoutKey,
                          $"Abstand {abstand}, Groessen unveraendert", Bereichsliste());
        return _windows.Count;
    }

    /// <summary>
    /// Vergisst die gespeicherte Anordnung EINER Bildschirm-Konfiguration.
    /// Beim naechsten Wechsel dorthin wird sie neu aus der aktuellen Anordnung
    /// abgeleitet — damit laesst sich das automatische Anordnen gezielt testen,
    /// ohne wirklich Kabel zu stecken.
    /// Rueckgabe: Anzahl der Bereiche, deren Anordnung entfernt wurde.
    /// </summary>
    public int ForgetLayout(string key)
    {
        var entfernt = 0;
        foreach (var fence in _config.Config.Fences)
            if (fence.Layouts.Remove(key)) entfernt++;

        _config.Config.DisplayAreas.Remove(key);
        LayoutPreview.Remove(key); // Vorschaubild gehoert zur Anordnung

        // Ist es die AKTIVE Konfiguration, sofort neu ableiten — sonst saehe man
        // erst beim naechsten Umstecken eine Wirkung.
        if (string.Equals(key, DisplayConfig.Current, StringComparison.Ordinal))
        {
            _currentLayoutKey = null; // erzwingt die Neuberechnung
            ApplyLayoutsForCurrentDisplays();
        }
        else
        {
            _config.Save();
        }
        return entfernt;
    }

    /// <summary>
    /// Vergisst ALLE gespeicherten Bildschirm-Anordnungen (Namen bleiben erhalten).
    /// Rueckgabe: Anzahl der entfernten Konfigurationen.
    /// </summary>
    public int ForgetAllLayouts()
    {
        var keys = _config.Config.Fences
            .SelectMany(f => f.Layouts.Keys)
            .Concat(_config.Config.DisplayAreas.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var fence in _config.Config.Fences) fence.Layouts.Clear();
        _config.Config.DisplayAreas.Clear();

        _currentLayoutKey = null;
        StoreLayout(DisplayConfig.Current); // aktuelle Lage als neuen Ausgangspunkt
        _currentLayoutKey = DisplayConfig.Current;
        _config.Save();
        return keys.Count;
    }

    /// Nutzbare Flaeche des Hauptbildschirms in DIP (dorthin wird zusammengefuehrt).
    private static LayoutTransfer.Area CurrentDesktopArea()
    {
        var work = System.Windows.SystemParameters.WorkArea;
        return new LayoutTransfer.Area(work.X, work.Y, work.Width, work.Height);
    }

    /// <summary>
    /// Gesamte Flaeche ueber ALLE Bildschirme in DIP.
    ///
    /// Bewusst ueber Screen.AllScreens statt SystemParameters: WPF haelt die
    /// Bildschirmmasse zwischengespeichert und liefert direkt nach dem An- oder
    /// Abstecken noch die alten Werte. Die Bereiche des neuen Monitors gaelten
    /// dann als „ausserhalb" — und die gerade geladene Anordnung wuerde
    /// faelschlich umgeraeumt.
    /// </summary>
    private static LayoutTransfer.Area VirtualArea()
    {
        try
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return CurrentDesktopArea();

            var scale = DipScale();
            var left = screens.Min(s => s.Bounds.Left) / scale;
            var top = screens.Min(s => s.Bounds.Top) / scale;
            var right = screens.Max(s => s.Bounds.Right) / scale;
            var bottom = screens.Max(s => s.Bounds.Bottom) / scale;

            return new LayoutTransfer.Area(left, top, right - left, bottom - top);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FenceManager.VirtualArea");
            return CurrentDesktopArea();
        }
    }

    /// <summary>
    /// Umrechnungsfaktor Pixel → DIP (Screen liefert Pixel, WPF rechnet in DIP).
    ///
    /// Ermittelt aus dem Verhaeltnis der Arbeitsflaeche des Hauptbildschirms in
    /// beiden Einheiten. Bewusst OHNE PresentationSource: MSDesk hat kein
    /// Hauptfenster (nur Bereichsfenster), der Aufruf lief deshalb mit null ins
    /// Leere — und die gesamte Flaechenberechnung fiel auf einen falschen Wert
    /// zurueck, sodass beim Anstecken falsch entschieden wurde.
    /// </summary>
    private static double DipScale()
    {
        try
        {
            var primary = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
            var work = System.Windows.SystemParameters.WorkArea;
            if (primary is { Width: > 0 } p && work.Width > 0)
            {
                var scale = p.Width / work.Width;
                // Nur plausible Werte uebernehmen (100 % bis 400 %).
                if (scale >= 0.9 && scale <= 4.5) return scale;
            }
        }
        catch (Exception)
        {
            // Unkritisch: ohne brauchbares Verhaeltnis wird 1:1 gerechnet.
        }
        return 1.0;
    }

    /// Schreibt die aktuelle Fenster-Geometrie aller Bereiche unter dem Schluessel fest.
    private void StoreLayout(string key)
    {
        // Zur Anordnung gehoert auch, wie gross die Flaeche damals war — nur damit
        // laesst sie sich spaeter anteilig umrechnen. Bewusst die GESAMTE Flaeche
        // ueber alle Bildschirme: Bereiche auf dem zweiten Monitor laegen sonst
        // ausserhalb und wuerden beim Uebertragen an den Rand geklemmt.
        var area = VirtualArea();
        _config.Config.DisplayAreas[key] = new LayoutRect
        {
            X = area.X, Y = area.Y, Width = area.Width, Height = area.Height
        };

        foreach (var window in _windows)
        {
            var cfg = window.ViewModel.Config;
            cfg.X = window.Left;
            cfg.Y = window.Top;
            cfg.Width = window.Width;
            cfg.Height = window.Height;
            cfg.Layouts[key] = new LayoutRect { X = cfg.X, Y = cfg.Y, Width = cfg.Width, Height = cfg.Height };
        }
    }

    /// Holt alle Fenster wieder in einen sichtbaren Bildschirmbereich.
    public void RealignAll()
    {
        foreach (var window in _windows)
        {
            var cfg = window.ViewModel.Config;
            EnsureOnScreen(cfg);
            window.Left = cfg.X;
            window.Top = cfg.Y;
        }
    }

    public void ShutdownAll()
    {
        // Anordnung noch VOR dem Schliessen festhalten (danach sind die
        // Fensterkoordinaten nicht mehr aussagekraeftig) — unter der aktuell
        // gueltigen Kennung, nicht unter der gemerkten.
        DisplayConfig.Invalidate();
        StoreLayout(DisplayConfig.Current);

        foreach (var window in _windows.ToList())
            window.Close();
        _config.Save();
    }

    /// Fenster ausserhalb aller Bildschirme auf den Primaermonitor (100,100) zuruecksetzen.
    /// <summary>
    /// Holt einen Bereich zurueck in den sichtbaren Bildschirmbereich.
    ///
    /// Frueher wurde er dabei auf den festen Punkt (100/100) gesetzt — lagen
    /// mehrere Bereiche daneben, stapelten sie sich dort alle uebereinander.
    /// Ausserdem wurde in DIP gerechnet, aber gegen Bildschirmgrenzen in
    /// PIXELN geprueft; bei skalierter Anzeige stimmte das nicht.
    /// Jetzt wird durchgaengig in DIP gerechnet und nur so weit geschoben, wie
    /// noetig — die relative Lage bleibt damit erhalten.
    /// </summary>
    private static void EnsureOnScreen(FenceConfig f)
    {
        var virtuell = new LayoutTransfer.Area(
            System.Windows.SystemParameters.VirtualScreenLeft,
            System.Windows.SystemParameters.VirtualScreenTop,
            System.Windows.SystemParameters.VirtualScreenWidth,
            System.Windows.SystemParameters.VirtualScreenHeight);

        var korrigiert = LayoutTransfer.ClampIntoArea(
            new LayoutRect { X = f.X, Y = f.Y, Width = f.Width, Height = f.Height }, virtuell);

        f.X = korrigiert.X;
        f.Y = korrigiert.Y;
    }

    private static string SanitizeLeaf(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Bereich" : name;
    }

    private static string MakeUniqueFolder(string parent, string leaf)
    {
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, leaf);
        var n = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(parent, $"{leaf} ({n++})");
        return candidate;
    }
}
