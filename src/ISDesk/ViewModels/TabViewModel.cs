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
    private bool _isLoaded;

    public TabViewModel(TabConfig config, Action persist)
    {
        _config = config;
        _persist = persist;
        SearchService.TermChanged += ApplySearch;
    }

    private bool _hasSearchMatch;

    /// True, wenn dieser Tab bei aktiver Suche Treffer enthaelt (markiert den Reiter).
    public bool HasSearchMatch
    {
        get => _hasSearchMatch;
        private set { if (_hasSearchMatch != value) { _hasSearchMatch = value; OnChanged(); } }
    }

    /// Treffer hervorheben, Nicht-Treffer abdunkeln (laeuft auf dem UI-Thread).
    /// NICHT geladene Tabs (Lazy Loading) haben keine Items — dort entscheiden die
    /// Dateinamen im Ordner ueber die Reiter-Markierung.
    private void ApplySearch()
    {
        var active = SearchService.IsActive;
        var any = false;
        foreach (var item in Items)
        {
            var match = active && SearchService.Matches(item.DisplayName);
            item.IsHighlighted = match;
            item.IsDimmed = active && !match;
            if (match) any = true;
        }

        if (active && !_isLoaded)
            any = FolderNames().Any(SearchService.Matches);

        HasSearchMatch = active && any;
    }

    // --- Namens-Zwischenspeicher fuer nicht geladene Tabs ---

    private IReadOnlyList<string>? _cachedNames;
    private DateTime _cachedAt;

    /// Anzeigenamen des Ordners ohne Icons zu laden (kurz zwischengespeichert,
    /// damit die Live-Suche nicht bei jedem Tastendruck die Platte anfasst).
    private IReadOnlyList<string> FolderNames()
    {
        if (_cachedNames != null && (DateTime.UtcNow - _cachedAt).TotalSeconds < 2)
            return _cachedNames;

        var names = FolderContents.ListVisibleEntries(_config.FolderPath)
            .Select(FolderContents.GetDisplayName).ToList();
        _cachedNames = names;
        _cachedAt = DateTime.UtcNow;
        return names;
    }

    private void InvalidateNameCache() => _cachedNames = null;

    /// Erzwingt eine Neuberechnung des Tab-Zaehlers (liest den Ordner erneut).
    public void RefreshItemCount()
    {
        InvalidateNameCache();
        OnChanged(nameof(ItemCount));
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
                if (_isLoaded) Reload(); // Icons in neuer Groesse laden (nur wenn sichtbar)
            }
        }
    }

    /// Breite einer Icon-Kachel, abgeleitet aus der Icon-Groesse.
    public double CellWidth => IconSize + 52;

    public ObservableCollection<IconItemViewModel> Items { get; } = new();

    /// True, solange dieser Tab geladen ist (Icons im Speicher, Ordner ueberwacht).
    /// Immer nur der sichtbare Tab eines Bereichs ist geladen (Lazy Loading).
    public bool IsLoaded => _isLoaded;

    /// Anzahl der Eintraege fuer den Tab-Zaehler — geladen aus den Items,
    /// sonst aus dem Ordner (ohne Icons zu laden).
    public int ItemCount => _isLoaded ? Items.Count : FolderNames().Count;

    /// Laedt Icons und startet die Ordnerueberwachung — nur beim ersten Anzeigen.
    public void EnsureLoaded()
    {
        if (_isLoaded) return;
        _isLoaded = true;
        Reload();
        StartWatching();
        OnChanged(nameof(IsLoaded));
    }

    /// Gibt alles frei, was nur fuer die Anzeige gebraucht wird: Ueberwachung,
    /// Papierkorb-Abo und die Icon-Liste. Die manuelle Reihenfolge lebt in der
    /// Konfiguration und bleibt davon unberuehrt.
    public void Unload()
    {
        if (!_isLoaded) return;
        _isLoaded = false;

        StopWatching();
        UnsubscribeRecycleBin();
        Items.Clear();
        InvalidateNameCache();

        OnChanged(nameof(IsLoaded));
        OnChanged(nameof(ItemCount));
        if (SearchService.IsActive) ApplySearch(); // Reiter-Markierung erhalten
    }

    /// Symbol vor dem Tab-Titel (Galerie-Dateiname oder absoluter PNG-Pfad).
    public string? IconPath
    {
        get => _config.IconPath;
        set
        {
            if (_config.IconPath != value)
            {
                _config.IconPath = value;
                OnChanged();
                OnChanged(nameof(IconImage));
                _persist();
            }
        }
    }

    public System.Windows.Media.ImageSource? IconImage => IconLibrary.Load(_config.IconPath);

    public void Reload()
    {
        InvalidateNameCache();

        // Ordner (gerade) nicht erreichbar: Anzeige leeren, aber die gespeicherte
        // Reihenfolge NICHT mit einer leeren Liste ueberschreiben.
        if (!Directory.Exists(_config.FolderPath))
        {
            Items.Clear();
            OnChanged(nameof(ItemCount));
            return;
        }

        // Manuelle Reihenfolge anwenden: bekannte Dateien in gespeicherter Ordnung,
        // neue hinten anfuegen, verschwundene aus der Ordnung entfernen.
        var entries = FolderContents.ListVisibleEntries(_config.FolderPath);
        var byName = entries.ToDictionary(e => Path.GetFileName(e.TrimEnd('\\', '/')),
            e => e, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        foreach (var name in _config.Order)
            if (byName.TryGetValue(name, out var path))
                ordered.Add(path);

        var known = new HashSet<string>(_config.Order, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            if (!known.Contains(Path.GetFileName(entry.TrimEnd('\\', '/'))))
                ordered.Add(entry);

        var newOrder = ordered.Select(e => Path.GetFileName(e.TrimEnd('\\', '/'))).ToList();
        if (!newOrder.SequenceEqual(_config.Order, StringComparer.OrdinalIgnoreCase))
        {
            _config.Order = newOrder;
            _persist();
        }

        Items.Clear();
        var hasRecycleBin = false;
        foreach (var path in ordered)
        {
            var item = new IconItemViewModel(path, FolderContents.GetDisplayName(path), Directory.Exists(path));
            Items.Add(item);
            LoadIconAsync(item);
            if (!item.IsFolder)
            {
                if (VisualSettings.AutoFavicons)
                    WebLinkFactory.EnsureFavicon(path); // heilt .url ohne Icon (prueft intern)
                PlacementRegistry.Learn(path, _config.FolderPath); // Platz-Gedaechtnis
            }
            if (path.Contains(RecycleBinMonitor.ClsidMarker, StringComparison.OrdinalIgnoreCase))
                hasRecycleBin = true;
        }

        // Papierkorb nur ueberwachen, solange der Tab tatsaechlich angezeigt wird.
        if (hasRecycleBin && !_binSubscribed && _isLoaded)
        {
            _binSubscribed = true;
            RecycleBinMonitor.Ensure();
            RecycleBinMonitor.StateChanged += OnRecycleBinStateChanged;
        }

        OnChanged(nameof(ItemCount));
        if (SearchService.IsActive)
            ApplySearch(); // frisch geladene Items sofort markieren
    }

    private bool _binSubscribed;

    private void UnsubscribeRecycleBin()
    {
        if (!_binSubscribed) return;
        RecycleBinMonitor.StateChanged -= OnRecycleBinStateChanged;
        _binSubscribed = false;
    }

    /// Fuellstand des Papierkorbs hat gewechselt → Icons der Papierkorb-Objekte
    /// dieses Tabs neu laden (ueber den normalen Debounce-/Reload-Weg).
    private void OnRecycleBinStateChanged()
    {
        try
        {
            lock (_sync)
            {
                foreach (var dir in Directory.EnumerateDirectories(_config.FolderPath))
                {
                    if (dir.Contains(RecycleBinMonitor.ClsidMarker, StringComparison.OrdinalIgnoreCase))
                        _changedPaths.Add(dir);
                }
                _debounce?.Stop();
                _debounce?.Start();
            }
        }
        catch (Exception)
        {
            // Ordner nicht lesbar → beim naechsten Wechsel erneut
        }
    }

    /// Verschiebt ein Icon in der manuellen Reihenfolge vor das Ziel-Icon
    /// (beforePath = null → ans Ende).
    public void ReorderTo(string sourcePath, string? beforePath)
    {
        var sourceName = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
        var order = _config.Order;
        var comparer = StringComparer.OrdinalIgnoreCase;

        var oldIndex = order.FindIndex(n => comparer.Equals(n, sourceName));
        if (oldIndex < 0) return;
        order.RemoveAt(oldIndex);

        var insertAt = order.Count;
        if (beforePath != null)
        {
            var beforeName = Path.GetFileName(beforePath.TrimEnd('\\', '/'));
            var idx = order.FindIndex(n => comparer.Equals(n, beforeName));
            if (idx >= 0) insertAt = idx;
        }
        order.Insert(insertAt, sourceName);
        _persist();
        Reload();
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
            if (_watcher != null) return; // laeuft bereits
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
    private readonly List<(string OldName, string NewName)> _pendingRenames = new();

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        lock (_sync)
        {
            _changedPaths.Add(e.FullPath);
            if (e is RenamedEventArgs renamed)
            {
                _changedPaths.Add(renamed.OldFullPath);
                _pendingRenames.Add((Path.GetFileName(renamed.OldFullPath), Path.GetFileName(renamed.FullPath)));
            }
            _debounce?.Stop();
            _debounce?.Start();
        }
    }

    private void DispatchReload()
    {
        // Geaenderte Dateien koennten beim ersten Laden ein falsches/generisches
        // Icon geliefert haben (Datei mitten im Verschieben) → Cache verwerfen.
        List<(string OldName, string NewName)> renames;
        lock (_sync)
        {
            foreach (var path in _changedPaths)
                ShellIconProvider.Instance.Invalidate(path);
            _changedPaths.Clear();
            renames = new List<(string, string)>(_pendingRenames);
            _pendingRenames.Clear();
        }

        void Apply()
        {
            // Umbenannte Dateien behalten ihren Platz in der manuellen Reihenfolge.
            foreach (var (oldName, newName) in renames)
            {
                var idx = _config.Order.FindIndex(n =>
                    string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _config.Order[idx] = newName;
            }
            Reload();
        }

        var app = Application.Current;
        if (app != null)
            app.Dispatcher.Invoke(Apply);
        else
            Apply();
    }

    /// Beendet die Ordnerueberwachung (Watcher + Entprellung) — der Tab bleibt
    /// benutzbar und kann jederzeit wieder geladen werden.
    private void StopWatching()
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
        _debounce?.Stop();
        _debounce?.Dispose();
        _debounce = null;
    }

    public void Dispose()
    {
        SearchService.TermChanged -= ApplySearch;
        UnsubscribeRecycleBin();
        StopWatching();
        _isLoaded = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
