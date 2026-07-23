using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ISDesk.Models;

namespace ISDesk.ViewModels;

public sealed class FenceViewModel : INotifyPropertyChanged
{
    private readonly FenceConfig _config;
    private readonly Action _persist;
    private readonly string _baseFolder;
    private TabViewModel? _activeTab;

    public FenceViewModel(FenceConfig config, string baseFolder, Action? persist = null)
    {
        _config = config;
        _baseFolder = baseFolder;
        _persist = persist ?? (static () => { });

        foreach (var tabConfig in _config.Tabs)
            Tabs.Add(new TabViewModel(tabConfig, _persist));

        if (Tabs.Count > 0)
        {
            var index = Math.Clamp(_config.ActiveTab, 0, Tabs.Count - 1);
            _activeTab = Tabs[index];
            _activeTab.IsActive = true;
        }
        UpdateTabFlags();
    }

    public FenceConfig Config => _config;
    public Guid Id => _config.Id;
    public string BaseFolder => _baseFolder;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (!ReferenceEquals(_activeTab, value))
            {
                if (_activeTab != null) _activeTab.IsActive = false;
                _activeTab = value;
                if (_activeTab != null) _activeTab.IsActive = true;
                _config.ActiveTab = value != null ? Math.Max(0, Tabs.IndexOf(value)) : 0;
                OnChanged();
                Persist();
            }
        }
    }

    /// Legt einen neuen Tab an: Ordner <BaseFolder>\<FenceTitle>\<TabName> (bei Kollision " (n)").
    public TabViewModel AddTab(string title)
    {
        var parent = Path.Combine(_baseFolder, SanitizeLeaf(_config.Title));
        Directory.CreateDirectory(parent);
        var folder = MakeUniqueFolder(parent, SanitizeLeaf(title));
        Directory.CreateDirectory(folder);

        var tabConfig = new TabConfig { Title = title, FolderPath = folder, IconSize = 32 };
        _config.Tabs.Add(tabConfig);
        var tab = new TabViewModel(tabConfig, _persist);
        Tabs.Add(tab);
        UpdateTabFlags();
        tab.Reload();
        tab.StartWatching();
        ActiveTab = tab; // persistiert
        return tab;
    }

    /// Entfernt einen Tab aus der Konfiguration (der Ordner auf der Platte bleibt erhalten).
    public void RemoveTab(TabViewModel tab)
    {
        if (Tabs.Count <= 1) return;
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;

        var removingActive = ReferenceEquals(_activeTab, tab);
        Tabs.RemoveAt(idx);
        _config.Tabs.Remove(tab.Config);
        tab.Dispose();
        UpdateTabFlags();

        if (removingActive)
        {
            var newIdx = Math.Clamp(idx, 0, Tabs.Count - 1);
            _activeTab = null;         // erzwingt Wechsel im Setter
            ActiveTab = Tabs[newIdx];
        }
        else
        {
            _config.ActiveTab = _activeTab != null ? Math.Max(0, Tabs.IndexOf(_activeTab)) : 0;
            Persist();
        }
    }

    public void RenameTab(TabViewModel tab, string newTitle)
    {
        tab.Title = newTitle; // nur Anzeige/Config, Ordner bleibt
    }

    private void UpdateTabFlags()
    {
        foreach (var tab in Tabs)
            tab.CanRemove = Tabs.Count > 1;
    }

    private static string SanitizeLeaf(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return string.IsNullOrEmpty(name) ? "Ordner" : name;
    }

    private static string MakeUniqueFolder(string parent, string leaf)
    {
        var candidate = Path.Combine(parent, leaf);
        var n = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(parent, $"{leaf} ({n++})");
        return candidate;
    }

    /// Laedt alle Tabs (Icons) und startet die Ordnerueberwachung. Auf UI-Thread aufrufen.
    public void ActivateAllTabs()
    {
        foreach (var tab in Tabs)
        {
            tab.Reload();
            tab.StartWatching();
        }
    }

    public void DisposeTabs()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
    }

    public string Title
    {
        get => _config.Title;
        set { if (_config.Title != value) { _config.Title = value; OnChanged(); Persist(); } }
    }

    public double Opacity
    {
        get => _config.Opacity;
        set { if (Math.Abs(_config.Opacity - value) > double.Epsilon) { _config.Opacity = value; OnChanged(); Persist(); } }
    }

    public bool Blur
    {
        get => _config.Blur;
        set { if (_config.Blur != value) { _config.Blur = value; OnChanged(); Persist(); } }
    }

    public double X
    {
        get => _config.X;
        set { if (_config.X != value) { _config.X = value; OnChanged(); Persist(); } }
    }

    public double Y
    {
        get => _config.Y;
        set { if (_config.Y != value) { _config.Y = value; OnChanged(); Persist(); } }
    }

    public double Width
    {
        get => _config.Width;
        set { if (_config.Width != value) { _config.Width = value; OnChanged(); Persist(); } }
    }

    public double Height
    {
        get => _config.Height;
        set { if (_config.Height != value) { _config.Height = value; OnChanged(); Persist(); } }
    }

    private void Persist() => _persist();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
