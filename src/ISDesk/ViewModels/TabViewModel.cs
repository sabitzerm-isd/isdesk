using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ISDesk.Models;
using ISDesk.Services;

namespace ISDesk.ViewModels;

public sealed class TabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly TabConfig _config;
    private readonly Action _persist;
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private bool _isActive;
    private bool _canRemove;

    public TabViewModel(TabConfig config, Action persist)
    {
        _config = config;
        _persist = persist;
    }

    public TabConfig Config => _config;

    /// True, wenn dieser Tab der aktive im Bereich ist (steuert die Tab-Optik).
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnChanged(); } }
    }

    /// False, wenn es der letzte Tab ist (Entfernen dann gesperrt).
    public bool CanRemove
    {
        get => _canRemove;
        set { if (_canRemove != value) { _canRemove = value; OnChanged(); } }
    }

    public string Title
    {
        get => _config.Title;
        set { if (_config.Title != value) { _config.Title = value; OnChanged(); _persist(); } }
    }

    public string FolderPath => _config.FolderPath;

    /// Hintergrundfarbe des Reiters ("#RRGGBB"), null = Standard.
    public string? Color
    {
        get => _config.Color;
        set
        {
            if (_config.Color != value)
            {
                _config.Color = value;
                OnChanged();
                OnChanged(nameof(PillBrush));
                OnChanged(nameof(AreaBrush));
                _persist();
            }
        }
    }

    /// Flaechen-Hintergrund des Bereichs, wenn dieser Tab aktiv ist:
    /// die Tab-Farbe dunkel abgetoent (lesbar), sonst das Standard-Anthrazit.
    public System.Windows.Media.Brush AreaBrush
    {
        get
        {
            var baseColor = System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1E);
            if (!string.IsNullOrWhiteSpace(_config.Color))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_config.Color);
                    baseColor = System.Windows.Media.Color.FromRgb(
                        (byte)(baseColor.R + (c.R - baseColor.R) * 0.45),
                        (byte)(baseColor.G + (c.G - baseColor.G) * 0.45),
                        (byte)(baseColor.B + (c.B - baseColor.B) * 0.45));
                }
                catch (Exception) { /* ungueltige Farbe → Standard */ }
            }
            var brush = new System.Windows.Media.SolidColorBrush(baseColor);
            brush.Freeze();
            return brush;
        }
    }

    /// Fertiger Brush fuer den Reiter-Hintergrund (null = keine Einfaerbung).
    public System.Windows.Media.Brush? PillBrush
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_config.Color)) return null;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_config.Color);
                var brush = new System.Windows.Media.SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public int IconSize
    {
        get => _config.IconSize;
        set
        {
            if (_config.IconSize != value)
            {
                _config.IconSize = value;
                OnChanged();
                OnChanged(nameof(CellWidth));
                _persist();
                Reload(); // Icons in neuer Groesse laden
            }
        }
    }

    /// Breite einer Icon-Kachel, abgeleitet aus der Icon-Groesse.
    public double CellWidth => IconSize + 52;

    public ObservableCollection<IconItemViewModel> Items { get; } = new();

    public void Reload()
    {
        Items.Clear();
        foreach (var path in FolderContents.ListVisibleEntries(_config.FolderPath))
        {
            var item = new IconItemViewModel(path, FolderContents.GetDisplayName(path), Directory.Exists(path));
            Items.Add(item);
            LoadIconAsync(item);
        }
    }

    private async void LoadIconAsync(IconItemViewModel item)
    {
        var icon = await ShellIconProvider.Instance.GetIconAsync(item.Path, _config.IconSize).ConfigureAwait(true);
        item.Icon = icon;
    }

    public void StartWatching()
    {
        try
        {
            if (!Directory.Exists(_config.FolderPath)) return;

            _debounce = new System.Timers.Timer(300) { AutoReset = false };
            _debounce.Elapsed += (_, _) => DispatchReload();

            _watcher = new FileSystemWatcher(_config.FolderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.Attributes | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.Changed += OnFileSystemChanged;
        }
        catch (Exception)
        {
            // Ordner nicht ueberwachbar → ohne Watcher weiter.
        }
    }

    private readonly HashSet<string> _changedPaths = new(StringComparer.OrdinalIgnoreCase);

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        lock (_sync)
        {
            _changedPaths.Add(e.FullPath);
            if (e is RenamedEventArgs renamed)
                _changedPaths.Add(renamed.OldFullPath);
            _debounce?.Stop();
            _debounce?.Start();
        }
    }

    private void DispatchReload()
    {
        // Geaenderte Dateien koennten beim ersten Laden ein falsches/generisches
        // Icon geliefert haben (Datei mitten im Verschieben) → Cache verwerfen.
        lock (_sync)
        {
            foreach (var path in _changedPaths)
                ShellIconProvider.Instance.Invalidate(path);
            _changedPaths.Clear();
        }

        var app = Application.Current;
        if (app != null)
            app.Dispatcher.Invoke(Reload);
        else
            Reload();
    }

    public void Dispose()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileSystemChanged;
            _watcher.Deleted -= OnFileSystemChanged;
            _watcher.Renamed -= OnFileSystemChanged;
            _watcher.Changed -= OnFileSystemChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        _debounce?.Dispose();
        _debounce = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
