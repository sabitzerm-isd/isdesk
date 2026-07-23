using System.IO;
using System.Windows;
using ISDesk.Models;
using ISDesk.ViewModels;
using ISDesk.Views;

namespace ISDesk.Services;

public sealed class FenceManager
{
    private readonly ConfigService _config;
    private readonly List<FenceWindow> _windows = new();

    public FenceManager(ConfigService config) => _config = config;

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

        var fenceConfig = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = title,
            X = at?.X ?? 120,
            Y = at?.Y ?? 120,
            Width = 400,
            Height = 260,
            Opacity = _config.Config.DefaultOpacity,
            Blur = _config.Config.DefaultBlur,
            ActiveTab = 0
        };
        fenceConfig.Tabs.Add(new TabConfig { Title = "Allgemein", FolderPath = tabFolder, IconSize = 32 });

        _config.Config.Fences.Add(fenceConfig);
        _config.SaveDebounced();
        return OpenFence(fenceConfig);
    }

    /// Schliesst das Fenster, entfernt den Bereich aus der Config (der Ordner bleibt erhalten), persistiert.
    public void RemoveFence(FenceViewModel vm)
    {
        var window = _windows.FirstOrDefault(w => w.ViewModel.Id == vm.Id);
        window?.Close();
        _config.Config.Fences.RemoveAll(f => f.Id == vm.Id);
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
