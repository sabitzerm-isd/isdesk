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
    public FenceWindow CreateFence(string title, Point? at)
    {
        var folder = MakeUniqueFolder(_config.Config.BaseFolder, SanitizeLeaf(title));
        Directory.CreateDirectory(folder);
        var tabFolder = Path.Combine(folder, "Allgemein");
        Directory.CreateDirectory(tabFolder);

        // Neue Bereiche erben das aktuelle Erscheinungsbild der bestehenden.
        var template = _config.Config.Fences.FirstOrDefault();
        var fenceConfig = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = title,
            X = at?.X ?? 120,
            Y = at?.Y ?? 120,
            Width = 400,
            Height = 260,
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
