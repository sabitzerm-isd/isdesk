using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using MSDesk.Models;

namespace MSDesk.ViewModels;

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

        // Bewusst NACH dem Anlegen der Tabs: so ist beim Umschalten sichergestellt,
        // dass die Trefferzahlen bereits stehen.
        Services.SearchService.TermChanged += OnSearchTermChanged;
    }

    // --- Suche: automatisch auf den Tab mit den Treffern umschalten ---

    /// Tab, der vor Beginn der Suche aktiv war — wird danach wiederhergestellt.
    private TabViewModel? _tabBeforeSearch;

    /// <summary>
    /// Schaltet auf den Tab mit den meisten Treffern um — aber nur, wenn der
    /// gerade sichtbare Tab selbst KEINEN Treffer hat. Sonst wuerde einem beim
    /// Weitertippen der Tab unter den Fingern weggezogen, obwohl man den
    /// gesuchten Eintrag schon vor sich hat.
    ///
    /// Liegen Treffer in mehreren Tabs, bleibt das sichtbar: jeder Reiter zeigt
    /// seine Trefferzahl an, sodass die uebrigen Fundstellen mit einem Klick
    /// erreichbar sind.
    /// </summary>
    private void OnSearchTermChanged()
    {
        // Zaehlungen zuerst verbindlich aktualisieren (Reihenfolge der
        // Ereignis-Empfaenger ist sonst nicht garantiert).
        foreach (var tab in Tabs) tab.RecomputeSearch();

        if (!Services.SearchService.IsActive)
        {
            // Suche beendet: zurueck auf den urspruenglichen Tab.
            if (_tabBeforeSearch != null && Tabs.Contains(_tabBeforeSearch))
                ActiveTab = _tabBeforeSearch;
            _tabBeforeSearch = null;
            return;
        }

        if (ActiveTab is { SearchMatchCount: > 0 }) return; // Treffer schon sichtbar

        var best = Tabs
            .Where(t => t.IsVisible && t.SearchMatchCount > 0)
            .OrderByDescending(t => t.SearchMatchCount)
            .FirstOrDefault();
        if (best == null || ReferenceEquals(best, ActiveTab)) return;

        _tabBeforeSearch ??= ActiveTab;
        ActiveTab = best;
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
                var previous = _activeTab;
                if (previous != null) previous.IsActive = false;
                _activeTab = value;
                if (_activeTab != null)
                {
                    _activeTab.IsActive = true;
                    _activeTab.EnsureLoaded(); // Icons erscheinen beim Umschalten
                }
                // Der vorherige Tab gibt Ueberwachung und Icons wieder frei.
                previous?.Unload();

                // Tab-Zaehler der nicht geladenen Tabs auffrischen (nur wenn angezeigt).
                if (_config.ShowTabCounts)
                    foreach (var tab in Tabs) tab.RefreshItemCount();

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
        ActiveTab = tab; // laedt den Tab und persistiert
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

    /// Nimmt einen Tab aus diesem Bereich heraus, OHNE ihn zu zerstoeren
    /// (fuer das Verschieben in einen anderen Bereich — Watcher laeuft weiter).
    public void DetachTab(TabViewModel tab)
    {
        var idx = Tabs.IndexOf(tab);
        if (idx < 0) return;

        var removingActive = ReferenceEquals(_activeTab, tab);
        Tabs.RemoveAt(idx);
        _config.Tabs.Remove(tab.Config);
        UpdateTabFlags();

        if (removingActive && Tabs.Count > 0)
        {
            _activeTab = null; // erzwingt Wechsel im Setter
            ActiveTab = Tabs[Math.Clamp(idx, 0, Tabs.Count - 1)];
        }
        else
        {
            _config.ActiveTab = _activeTab != null ? Math.Max(0, Tabs.IndexOf(_activeTab)) : 0;
            Persist();
        }
    }

    /// Haengt einen (anderswo geloesten) Tab an diesen Bereich an und aktiviert ihn.
    public void AttachTab(TabViewModel tab)
    {
        _config.Tabs.Add(tab.Config);
        Tabs.Add(tab);
        UpdateTabFlags();
        ActiveTab = tab; // persistiert
    }

    private void UpdateTabFlags()
    {
        OnChanged(nameof(ShowTabStrip));
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

    /// Laedt NUR den sichtbaren Tab (Icons + Ordnerueberwachung). Die uebrigen Tabs
    /// laden erst beim Umschalten nach — das spart bei vielen Tabs deutlich
    /// Speicher, Threads und Handles. Auf UI-Thread aufrufen.
    public void ActivateVisibleTab()
    {
        InitFavoritesIfPresent(); // Sterne aktivieren, falls es Favoriten gibt
        _activeTab?.EnsureLoaded();
    }

    /// Die Reiter-Zeile lohnt erst ab zwei sichtbaren Tabs — bei einem einzigen
    /// nimmt sie nur Platz weg (das „+" zum Anlegen bleibt trotzdem).
    public bool ShowTabStrip => Tabs.Count(t => !t.Hidden) >= 2;

    /// Nach Aenderungen an Anzahl/Sichtbarkeit der Tabs aufrufen.
    public void RefreshTabStrip()
    {
        UpdateTabFlags();
        OnChanged(nameof(ShowTabStrip));
    }

    // --- Vorschau beim Verweilen auf der Ueberschrift ---

    private const int VorschauMax = 12;

    public IReadOnlyList<string> PreviewLines { get; private set; } = Array.Empty<string>();
    public bool PreviewEmpty => PreviewLines.Count == 0;
    public string PreviewHint { get; private set; } = "";

    /// <summary>
    /// Baut die Vorschau fuer die Bereichs-Ueberschrift.
    ///
    /// Hat der Bereich mehrere Tabs, ist die Uebersicht „welcher Reiter, wie
    /// viele Eintraege" die nuetzlichere Auskunft. Bei nur EINEM Reiter waere
    /// die Zeile „1 Tab" wertlos — dann werden gleich die Eintraege selbst
    /// gezeigt.
    /// </summary>
    public void RefreshPreview()
    {
        var sichtbar = Tabs.Where(t => !t.Hidden).ToList();

        if (sichtbar.Count == 1)
        {
            var einziger = sichtbar[0];
            einziger.RefreshPreview();
            PreviewLines = einziger.PreviewNames;
            PreviewHint = einziger.PreviewMoreText;
        }
        else
        {
            // Die Anzahl nur zeigen, wo sie ohne Plattenzugriff zu haben ist.
            // Ueber ItemCount zu gehen haette je nicht geladenem Reiter einen
            // eigenen Ordner-Lesevorgang ausgeloest — synchron im Bedienfaden,
            // allein weil die Maus kurz auf der Ueberschrift steht.
            PreviewLines = sichtbar.Take(VorschauMax)
                                   .Select(t => t.FreieAnzahl is { } n ? $"{t.Title} ({n})" : t.Title)
                                   .ToList();
            var rest = sichtbar.Count - PreviewLines.Count;
            PreviewHint = rest > 0 ? $"… und {rest} weitere" : $"{sichtbar.Count} Reiter";
        }

        OnChanged(nameof(PreviewLines));
        OnChanged(nameof(PreviewEmpty));
        OnChanged(nameof(PreviewHint));
    }

    public void DisposeTabs()
    {
        // Abmelden, sonst reagierte ein geschlossener Bereich weiter auf die Suche.
        Services.SearchService.TermChanged -= OnSearchTermChanged;

        foreach (var tab in Tabs)
            tab.Dispose();
    }

    public string Title
    {
        get => _config.Title;
        set
        {
            if (_config.Title != value)
            {
                _config.Title = value;
                OnChanged();
                OnChanged(nameof(IsAblage));
                Persist();
            }
        }
    }

    /// Der Ablage-Bereich zeigt einen Refresh-Button (Regeln ausfuehren).
    public bool IsAblage => string.Equals(_config.Title, "Ablage", StringComparison.OrdinalIgnoreCase);

    /// Der Lesezeichen-Bereich: Refresh gleicht Chrome + Firefox ab, Einzelklick oeffnet.
    public bool IsBookmarks => string.Equals(_config.Title, "Lesezeichen", StringComparison.OrdinalIgnoreCase);

    /// Bereiche mit Refresh-Button (Ablage: Regeln, Lesezeichen: Browser-Abgleich).
    public bool IsRefreshable => IsAblage || IsBookmarks;

    /// Sorgt dafuer, dass es im Lesezeichen-Bereich einen Tab „Favoriten" gibt —
    /// er steht durch SortOrder immer an erster Stelle.
    public TabViewModel? EnsureFavoritesTab()
    {
        if (!IsBookmarks) return null;

        var existing = Tabs.FirstOrDefault(t =>
            string.Equals(t.Title, Services.FavoriteService.TabTitle, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var folder = FavoritesFolderPath;
        Directory.CreateDirectory(folder);

        var cfg = new TabConfig
        {
            Title = Services.FavoriteService.TabTitle,
            FolderPath = folder,
            IconSize = 32,
            IconPath = "stern2.png",
            SortOrder = Services.FavoriteService.SortOrder
        };
        _config.Tabs.Insert(0, cfg);

        var tab = new TabViewModel(cfg, _persist);
        Tabs.Insert(0, tab);
        UpdateTabFlags();
        ApplyFavoritesFolder(folder);
        Persist();
        return tab;
    }

    /// <summary>
    /// Teilt allen Tabs mit, wo die Favoriten liegen (aktiviert die Sterne) —
    /// einschliesslich des Favoriten-Tabs selbst: dort ist der Stern gefuellt
    /// und ein Klick nimmt den Eintrag wieder heraus.
    /// </summary>
    private void ApplyFavoritesFolder(string folder)
    {
        foreach (var tab in Tabs)
            tab.FavoritesFolder = folder;
    }

    /// Wo die Favoriten liegen (bzw. liegen wuerden) — auch bevor der Tab existiert.
    private string FavoritesFolderPath =>
        Path.Combine(_baseFolder, SanitizeLeaf(_config.Title), Services.FavoriteService.TabTitle);

    /// <summary>
    /// Aktiviert die Sterne im Lesezeichen-Bereich — bewusst AUCH dann, wenn es
    /// den Favoriten-Tab noch gar nicht gibt. Sonst entstuende eine Sackgasse:
    /// der Stern waere unsichtbar, bis der Tab existiert, und der Tab entsteht
    /// erst durch einen Klick auf den Stern. Angelegt wird erst beim Klick.
    /// </summary>
    private void InitFavoritesIfPresent()
    {
        if (!IsBookmarks) return;
        var favorites = Tabs.FirstOrDefault(t =>
            string.Equals(t.Title, Services.FavoriteService.TabTitle, StringComparison.OrdinalIgnoreCase));
        ApplyFavoritesFolder(favorites?.FolderPath ?? FavoritesFolderPath);
    }

    /// Zieht Tabs, die in der Konfiguration neu dazugekommen sind, in die Ansicht nach
    /// (geladen wird erst beim Anzeigen).
    public void SyncTabsFromConfig()
    {
        foreach (var tabConfig in _config.Tabs)
        {
            if (Tabs.Any(t => ReferenceEquals(t.Config, tabConfig))) continue;
            Tabs.Add(new TabViewModel(tabConfig, _persist));
        }
        UpdateTabFlags();

        // Neu importierte Dateien im sichtbaren Tab zeigen.
        if (_activeTab is { IsLoaded: true }) _activeTab.Reload();
        else _activeTab?.EnsureLoaded();
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

    public bool Locked
    {
        get => _config.Locked;
        set { if (_config.Locked != value) { _config.Locked = value; OnChanged(); Persist(); } }
    }

    public double TitleBarOpacity
    {
        get => _config.TitleBarOpacity;
        set { if (Math.Abs(_config.TitleBarOpacity - value) > double.Epsilon) { _config.TitleBarOpacity = value; OnChanged(); Persist(); } }
    }

    /// Symbol in der Titelzeile (Galerie-Dateiname oder absoluter PNG-Pfad).
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
                Persist();
            }
        }
    }

    public System.Windows.Media.ImageSource? IconImage => Services.IconLibrary.Load(_config.IconPath);

    /// Zeigt hinter jedem Tab-Titel die Dateianzahl (nur fuer diesen Bereich).
    public bool ShowTabCounts
    {
        get => _config.ShowTabCounts;
        set { if (_config.ShowTabCounts != value) { _config.ShowTabCounts = value; OnChanged(); Persist(); } }
    }

    // WICHTIG: Diese vier Eigenschaften halten nur den zuletzt bekannten Stand
    // fest. Sie schreiben BEWUSST NICHT in Config.Layouts.
    //
    // Frueher tat eine Hilfsmethode („SnapshotLayout") genau das — bei jeder
    // einzelnen Aenderung von X, Y, Breite oder Hoehe, mit der gerade gueltigen
    // Bildschirm-Kennung. Das umging die gesamte Absicherung: die Sperre
    // waehrend eines Bildschirmwechsels, die Pruefung der Kennung und das
    // gebuendelte Schreiben.
    //
    // Die Folge war der lange gesuchte Fehler: Beim Anstecken eines Monitors
    // verschiebt Windows die Fenster, jede dieser Zwischenpositionen landete
    // sofort in der Anordnung — teils noch unter der alten, teils schon unter
    // der neuen Kennung. So entstanden Anordnungen, die X aus der einen und Y
    // aus der anderen Konfiguration trugen.
    //
    // Die Anordnung wird ausschliesslich vom FenceManager gespeichert
    // (StoreLayout), und zwar geprueft, gebuendelt und nie waehrend eines
    // Bildschirmwechsels.

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
