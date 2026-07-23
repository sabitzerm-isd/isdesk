using System.IO;
using System.Windows;
using ISDesk.Models;
using ISDesk.ViewModels;
using ISDesk.Views;
using Microsoft.VisualBasic.FileIO;

namespace ISDesk.Services;

public sealed class FenceManager
{
    private readonly ConfigService _config;
    private readonly List<FenceWindow> _windows = new();

    public FenceManager(ConfigService config) => _config = config;

    /// Wird von App nach der Konstruktion gesetzt (Sicherung/Wiederherstellung).
    public BackupService? Backup { get; set; }

    /// Wird von App gesetzt (Desktop-Einsammler fuer die Ablage).
    public DesktopSweeper? Sweeper { get; set; }

    public bool DesktopSweepEnabled => _config.Config.DesktopSweep;

    public void PersistNow() => _config.SaveDebounced();

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

        foreach (var window in _windows)
        {
            var cfg = window.ViewModel.Config;
            if (cfg.Layouts.TryGetValue(key, out var rect))
            {
                cfg.X = rect.X; cfg.Y = rect.Y;
                cfg.Width = Math.Max(rect.Width, 180); cfg.Height = Math.Max(rect.Height, 120);
            }
            EnsureOnScreen(cfg);
            window.Left = cfg.X;
            window.Top = cfg.Y;
            window.Width = cfg.Width;
            window.Height = cfg.Height;
            cfg.Layouts[key] = new LayoutRect { X = cfg.X, Y = cfg.Y, Width = cfg.Width, Height = cfg.Height };
        }
        _config.SaveDebounced();
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
        foreach (var window in _windows.ToList())
            window.Close();
        _config.Save();
    }

    /// Fenster ausserhalb aller Bildschirme auf den Primaermonitor (100,100) zuruecksetzen.
    private static void EnsureOnScreen(FenceConfig f)
    {
        var rect = new System.Drawing.Rectangle(
            (int)f.X, (int)f.Y, (int)Math.Max(1, f.Width), (int)Math.Max(1, f.Height));
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            if (screen.Bounds.IntersectsWith(rect))
                return;
        }
        f.X = 100;
        f.Y = 100;
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
