using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MSDesk.Interop;
using MSDesk.Services;
using MSDesk.ViewModels;

namespace MSDesk.Views;

/// Optionen: links Navigation, rechts der Inhalt der gewaehlten Kategorie.
/// „Allgemein" = zentrale Einstellungen (Lesezeichen, Ablage, Sicherung),
/// „Dieser Bereich" = Optik/Icon/Zaehler des Bereichs. Erscheint mittig auf
/// dem Hauptbildschirm.
public partial class SettingsDialog : Window
{
    private readonly FenceViewModel _vm;
    private readonly FenceManager? _manager;
    private bool _initialized;

    public SettingsDialog(FenceViewModel vm, FenceManager? manager, Window? centerOn)
    {
        _vm = vm;
        _manager = manager;
        DataContext = vm;
        InitializeComponent();

        var size = vm.ActiveTab?.IconSize ?? 32;
        foreach (ComboBoxItem item in IconSizeBox.Items)
        {
            if (item.Tag is string tag && tag == size.ToString())
            {
                IconSizeBox.SelectedItem = item;
                break;
            }
        }
        IconSizeBox.SelectedItem ??= IconSizeBox.Items[1]; // Mittel (32)
        SweepCheck.IsChecked = manager?.DesktopSweepEnabled ?? false;
        BackupPathBox.Text = manager?.AutoBackupFolder ?? "";
        BookmarkButton.IsEnabled = manager?.Bookmarks?.ChromeAvailable ?? false;
        if (!BookmarkButton.IsEnabled) BookmarkButton.Content = "Chrome nicht gefunden";
        FirefoxButton.IsEnabled = manager?.Bookmarks?.FirefoxAvailable ?? false;
        if (!FirefoxButton.IsEnabled) FirefoxButton.Content = "Firefox nicht gefunden";

        // Raster/Kanten-Einrasten: 0 = aus, sonst Rastergroesse in Pixeln.
        var grid = manager?.GridSize ?? 20;
        GridSnapCheck.IsChecked = grid > 0;
        GridSizeBox.IsEnabled = grid > 0;
        SelectGridSize(grid > 0 ? grid : 20);

        SelectSnapGap(manager?.SnapGapMillimeters ?? 6);
        HideRecycleBinCheck.IsChecked = DesktopIcons.IsRecycleBinHidden();
        BlurCheck.IsChecked = manager?.BlurEnabled ?? true;
        FaviconCheck.IsChecked = manager?.AutoFavicons ?? true;
        EdgeSnapCheck.IsChecked = manager?.EdgeSnapEnabled ?? true;
        WidthBox.Text = ((int)Math.Round(vm.Width)).ToString();
        HeightBox.Text = ((int)Math.Round(vm.Height)).ToString();

        if (manager?.ConfigForDisplayOverview is { } appConfig)
        {
            FirstNameBox.Text = appConfig.UserFirstName;
            LastNameBox.Text = appConfig.UserLastName;
        }
        UpdateBackupCloudHint();

        DailyBackupCheck.IsChecked = manager?.Config.Config.AutoBackupDaily ?? true;

        VersionText.Text = $"MSDesk v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        _initialized = true;
        UpdateDailyBackupStatus();

        Loaded += async (_, _) => await CheckUpdateAsync();
    }

    // --- Update ---

    private readonly UpdateService _updates = new();
    private UpdateService.UpdateInfo? _updateInfo;

    private async Task CheckUpdateAsync()
    {
        UpdateStatusText.Text = "Suche nach Updates…";
        UpdateCheckButton.IsEnabled = false;
        UpdateInstallButton.Visibility = Visibility.Collapsed;

        _updateInfo = await _updates.CheckAsync();

        UpdateCheckButton.IsEnabled = true;
        if (_updateInfo == null)
        {
            UpdateStatusText.Text = $"MSDesk {UpdateService.CurrentVersion} ist aktuell.";
            return;
        }

        var mb = _updateInfo.Size > 0 ? $", {_updateInfo.Size / 1024 / 1024} MB" : "";
        UpdateStatusText.Text = $"Neue Version {_updateInfo.LatestVersion} verfügbar (du hast {UpdateService.CurrentVersion}{mb}).";
        UpdateInstallButton.Visibility = Visibility.Visible;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdateAsync();

    /// <summary>
    /// Oeffnet den Dialog direkt beim Abschnitt „Update".
    ///
    /// Hier stand frueher NavAllgemein — aus einer Zeit, als Update noch oben
    /// unter „Allgemein" lag. Seit es einen eigenen Abschnitt hat, landete man
    /// ueber „Nach Updates suchen…" auf der falschen Seite und musste links
    /// noch einmal umschalten.
    /// </summary>
    public void ShowUpdateSection() => NavUpdate.IsChecked = true;

    private void OpenHelp_Click(object sender, RoutedEventArgs e) => HelpPage.Open();

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInfo == null) return;
        UpdateInstallButton.IsEnabled = false;
        UpdateInstallButton.Content = "Wird geladen…";

        var path = await _updates.DownloadAndRunAsync(_updateInfo);
        if (path == null)
        {
            UpdateInstallButton.Content = "Fehlgeschlagen";
            UpdateInstallButton.IsEnabled = true;
            return;
        }
        Application.Current.Shutdown(); // Installer uebernimmt
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.Apply(this, 0.97, true);
    }

    private void Nav_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized && PanelAllgemein == null) return;
        if (PanelAllgemein == null || PanelBereich == null || PanelBildschirme == null
            || PanelSicherung == null || PanelUpdate == null) return;

        PanelAllgemein.Visibility = NavAllgemein.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelBereich.Visibility = NavBereich.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelBildschirme.Visibility = NavBildschirme.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelSicherung.Visibility = NavSicherung.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelUpdate.Visibility = NavUpdate.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavBildschirme.IsChecked == true) LoadDisplays();
        if (NavUpdate.IsChecked == true) _ = LoadReleaseNotesAsync();
    }

    // --- Anwendername ---

    private void UserName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var config = _manager?.ConfigForDisplayOverview;
        if (config == null) return;

        config.UserFirstName = FirstNameBox.Text.Trim();
        config.UserLastName = LastNameBox.Text.Trim();
        _manager?.SaveSoon();
    }

    // --- Versionshinweise ---

    /// Anzeigemodell fuer die Liste „Was ist neu".
    private sealed record ReleaseNoteItem(string Version, string PublishedText, string Text, bool IsCurrent);

    private bool _releaseNotesLoaded;

    private async Task LoadReleaseNotesAsync()
    {
        if (_releaseNotesLoaded) return;
        _releaseNotesLoaded = true;

        var notes = await _updates.GetReleaseNotesAsync();
        if (notes.Count == 0)
        {
            _releaseNotesLoaded = false; // beim naechsten Aufruf erneut versuchen
            ReleaseNotesStatus.Text = "Versionshinweise konnten nicht geladen werden (keine Verbindung zu GitHub).";
            return;
        }

        ReleaseNotesStatus.Visibility = Visibility.Collapsed;
        ReleaseNotesList.ItemsSource = notes.Select(n => new ReleaseNoteItem(
            n.Version,
            n.Published?.ToLocalTime().ToString("dd.MM.yyyy") ?? "",
            n.Text,
            string.Equals(n.Version, UpdateService.CurrentVersion, StringComparison.Ordinal))).ToList();
    }

    // --- Bildschirme ---

    /// Liest die angeschlossenen Bildschirme und die gespeicherten Anordnungen neu ein.
    private void LoadDisplays()
    {
        DisplayConfig.Invalidate(); // koennte sich seit dem Start geaendert haben
        MonitorList.ItemsSource = DisplayOverview.ConnectedMonitors();

        var config = _manager?.ConfigForDisplayOverview;
        LayoutList.ItemsSource = config != null
            ? DisplayOverview.SavedConfigurations(config)
            : new List<SavedLayoutInfo>();
    }

    private void RefreshDisplays_Click(object sender, RoutedEventArgs e)
    {
        LoadDisplays();
        LayoutSaveHint.Text = "Aktualisiert.";
    }

    /// Bereiche ueberschneidungsfrei neu anordnen (nach einem Bildschirmwechsel).
    private void RearrangeFences_Click(object sender, RoutedEventArgs e)
    {
        var count = _manager?.RearrangeOnCurrentScreen() ?? 0;
        LoadDisplays();
        LayoutSaveHint.Text = count > 0
            ? $"{count} Bereiche überschneidungsfrei angeordnet und gesichert."
            : "Keine Bereiche vorhanden.";
    }

    // --- Symbole nach einem Programm-Update ---

    /// Holt bekannte Symbole vom Desktop zurueck — mit Vorschau vor dem Verschieben.
    private void ReclaimIcons_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;

        var vorschau = _manager.ReclaimDesktopIcons(nurVorschau: true);

        if (vorschau.Gesamt == 0 && vorschau.Gesperrt.Count == 0)
        {
            ReclaimHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            ReclaimHint.Text = "Auf dem Desktop liegt nichts, das in einen Bereich gehört.";
            return;
        }

        var text = new System.Text.StringBuilder();

        if (vorschau.Gesamt > 0)
        {
            text.AppendLine("Auf dem Desktop wurden Symbole gefunden, deren Platz bekannt ist:\n");
            if (vorschau.Zurueckgeholt > 0)
                text.AppendLine($"• {vorschau.Zurueckgeholt} wandern zurück in ihren Bereich");
            if (vorschau.Ersetzt > 0)
                text.AppendLine($"• {vorschau.Ersetzt} ersetzen eine ältere Verknüpfung desselben Programms");
            text.AppendLine("\nAlles andere bleibt unberührt auf dem Desktop liegen.");
        }

        // Der Desktop „für alle Benutzer" ist ohne Administratorrechte gesperrt.
        // Das gehört klar gesagt — sonst sucht man den Fehler bei MSDesk.
        if (vorschau.Gesperrt.Count > 0)
        {
            if (text.Length > 0) text.AppendLine();
            text.AppendLine($"Nicht möglich bei: {string.Join(", ", vorschau.Gesperrt)}");
            text.AppendLine("\nDiese Verknüpfungen wurden bei der Installation für ALLE Benutzer " +
                            "angelegt und liegen in C:\\Users\\Public\\Desktop. Dieser Ordner ist " +
                            "ohne Administratorrechte schreibgeschützt — MSDesk darf sie weder " +
                            "verschieben noch löschen.");
        }

        if (vorschau.Gesamt == 0)
        {
            ConfirmDialog.Info(text.ToString(), this);
            ReclaimHint.Foreground = System.Windows.Media.Brushes.Khaki;
            ReclaimHint.Text = $"{vorschau.Gesperrt.Count} Verknüpfung(en) benötigen Administratorrechte.";
            return;
        }

        var (ok, _) = ConfirmDialog.Show(text.ToString(), this, okText: "Einordnen");
        if (!ok) return;

        var ergebnis = _manager.ReclaimDesktopIcons();
        ReclaimHint.Foreground = System.Windows.Media.Brushes.LightGreen;

        var meldung = $"{ergebnis.Gesamt} Symbole eingeordnet";
        if (ergebnis.Fehlgeschlagen > 0) meldung += $", {ergebnis.Fehlgeschlagen} nicht (in Benutzung)";
        if (ergebnis.Gesperrt.Count > 0) meldung += $", {ergebnis.Gesperrt.Count} ohne Administratorrechte";
        ReclaimHint.Text = meldung + ".";
    }

    /// Doppelte Verknuepfungen in den Bereichen entfernen — ebenfalls mit Vorschau.
    private void RemoveDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;

        var anzahl = _manager.RemoveDuplicateShortcuts(nurVorschau: true);
        if (anzahl == 0)
        {
            ReclaimHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            ReclaimHint.Text = "Keine doppelten Verknüpfungen gefunden.";
            return;
        }

        var (ok, _) = ConfirmDialog.Show(
            $"{anzahl} doppelte Verknüpfung(en) gefunden — also zwei Einträge, die auf dasselbe " +
            "Programm zeigen.\n\nDie neuere bleibt, die ältere wandert in den Papierkorb " +
            "(von dort holst du sie notfalls zurück).",
            this, okText: "Doppelte entfernen");
        if (!ok) return;

        var entfernt = _manager.RemoveDuplicateShortcuts();
        ReclaimHint.Foreground = System.Windows.Media.Brushes.LightGreen;
        ReclaimHint.Text = $"{entfernt} doppelte Verknüpfung(en) in den Papierkorb gelegt.";
    }

    // --- Papierkorb ---

    private void HideRecycleBin_Checked(object sender, RoutedEventArgs e) => SetRecycleBinHidden(true);
    private void HideRecycleBin_Unchecked(object sender, RoutedEventArgs e) => SetRecycleBinHidden(false);

    private void SetRecycleBinHidden(bool ausblenden)
    {
        if (!_initialized) return;

        if (DesktopIcons.SetRecycleBinHidden(ausblenden))
        {
            RecycleBinHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            RecycleBinHint.Text = ausblenden
                ? "Ausgeblendet. Es bleibt der Papierkorb im Bereich — er zeigt selbst an, ob etwas darin liegt."
                : "Wieder eingeblendet.";
        }
        else
        {
            RecycleBinHint.Foreground = System.Windows.Media.Brushes.Khaki;
            RecycleBinHint.Text = "Die Einstellung konnte nicht geändert werden — Näheres im Absturzprotokoll.";
        }
    }

    /// Alle Bereiche an einem gedachten Raster ausrichten (Größen bleiben).
    private void ArrangeOnGrid_Click(object sender, RoutedEventArgs e)
    {
        var anzahl = _manager?.ArrangeOnGrid() ?? 0;
        LoadDisplays();
        LayoutSaveHint.Text = anzahl > 0
            ? $"{anzahl} Bereiche am Raster ausgerichtet — gleicher Abstand, Größen unverändert."
            : "Keine Bereiche vorhanden.";
    }

    /// Oeffnet das Anordnungs-Protokoll im Editor.
    private void OpenLayoutLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pfad = Path.Combine(ConfigService.DefaultFolder, "start.log");

            if (!File.Exists(pfad))
            {
                ConfirmDialog.Info("Es liegt noch kein Protokoll vor.", this);
                return;
            }

            Process.Start(new ProcessStartInfo(pfad) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info($"Das Protokoll konnte nicht geöffnet werden:\n{ex.Message}", this);
        }
    }

    /// Zeigt, ob wirklich gespeichert wird — sonst bleibt jeder Fehler unbemerkt.
    private void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var text = new System.Text.StringBuilder();
        try
        {
            var pfad = _manager?.ConfigFilePath ?? "";
            text.AppendLine($"Konfigurationsdatei:\n{pfad}\n");

            if (File.Exists(pfad))
            {
                var info = new FileInfo(pfad);
                var alter = DateTime.Now - info.LastWriteTime;
                text.AppendLine($"Zuletzt geschrieben: {info.LastWriteTime:dd.MM.yyyy HH:mm:ss}");
                text.AppendLine($"Das ist vor {(int)alter.TotalMinutes} Minuten.");
                text.AppendLine($"Größe: {info.Length / 1024} KB\n");
            }
            else
            {
                text.AppendLine("Die Datei existiert noch nicht.\n");
            }

            text.AppendLine($"Speichervorgänge in dieser Sitzung: {_manager?.SaveCount ?? 0}");

            // Frueherer Ablageort: dort wurden Aenderungen bei einem Anwender von
            // aussen zurueckgesetzt. Liegt die alte Datei noch, ist sie nur ein
            // Ueberbleibsel und wird nicht mehr verwendet.
            var alt = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MSDesk", "config.json");
            if (File.Exists(alt))
                text.AppendLine($"\nHinweis: Unter „Roaming“ liegt noch eine alte Datei " +
                                $"(Stand {new FileInfo(alt).LastWriteTime:dd.MM.yyyy HH:mm}). " +
                                "Sie wird nicht mehr verwendet.");
            text.AppendLine($"Gemerkte Bildschirm-Konfigurationen: {_manager?.ConfigForDisplayOverview.DisplayAreas.Count ?? 0}");
            text.AppendLine($"\nAktuelle Kennung:\n{DisplayConfig.Current}");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Fehler beim Ermitteln: {ex.Message}");
        }

        ConfirmDialog.Info(text.ToString(), this);
    }

    /// Gemerkte Anordnung EINER Konfiguration vergessen (zum Testen).
    private void ResetDisplay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;

        var istAktiv = string.Equals(key, DisplayConfig.Current, StringComparison.Ordinal);
        var (bestaetigt, _) = ConfirmDialog.Show(
            istAktiv
                ? "Die gemerkte Anordnung dieser Konfiguration wird verworfen und sofort neu abgeleitet.\n\n" +
                  "Die Bereiche werden dabei neu angeordnet."
                : "Die gemerkte Anordnung dieser Konfiguration wird verworfen.\n\n" +
                  "Beim nächsten Wechsel dorthin wird sie neu aus der aktuellen Anordnung abgeleitet.",
            this, okText: "Zurücksetzen");
        if (!bestaetigt) return;

        var anzahl = _manager?.ForgetLayout(key) ?? 0;
        LoadDisplays();
        LayoutSaveHint.Text = istAktiv
            ? "Anordnung verworfen und neu abgeleitet."
            : $"Anordnung von {anzahl} Bereich(en) verworfen.";
    }

    /// ALLE gemerkten Anordnungen vergessen.
    private void ResetAllDisplays_Click(object sender, RoutedEventArgs e)
    {
        var (bestaetigt, _) = ConfirmDialog.Show(
            "Alle gemerkten Bildschirm-Anordnungen werden verworfen.\n\n" +
            "Die Bereiche bleiben, wo sie jetzt sind — diese Lage wird zum neuen Ausgangspunkt.",
            this, okText: "Alle zurücksetzen");
        if (!bestaetigt) return;

        var anzahl = _manager?.ForgetAllLayouts() ?? 0;
        LoadDisplays();
        LayoutSaveHint.Text = $"{anzahl} gespeicherte Konfiguration(en) verworfen.";
    }

    /// Eigenen Namen fuer eine Bildschirm-Konfiguration vergeben.
    private void RenameDisplay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        var config = _manager?.ConfigForDisplayOverview;
        if (config == null) return;

        config.DisplayNames.TryGetValue(key, out var existing);
        var name = InputDialog.Ask("Name für diese Bildschirm-Konfiguration:", existing ?? "", this);
        if (name == null) return; // abgebrochen

        name = name.Trim();
        if (name.Length == 0) config.DisplayNames.Remove(key);
        else config.DisplayNames[key] = name;

        _manager?.SaveSoon();
        LoadDisplays();
    }

    /// Sichert die aktuelle Anordnung ausdruecklich fuer die aktive Konfiguration.
    private void SaveLayoutNow_Click(object sender, RoutedEventArgs e)
    {
        _manager?.SaveLayoutForCurrentDisplays();
        LoadDisplays();
        LayoutSaveHint.Text = "Anordnung für die aktive Bildschirm-Konfiguration gesichert.";
    }

    // --- Bereich ---

    private void IconSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (IconSizeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (!int.TryParse(tag, out var size)) return;

        if (_manager != null) _manager.SetIconSizeAll(size);
        else foreach (var tab in _vm.Tabs) tab.IconSize = size;
    }

    // --- Groesse exakt setzen / angleichen ---

    private void ApplySize_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;
        double? width = int.TryParse(WidthBox.Text.Trim(), out var w) ? w : null;
        double? height = int.TryParse(HeightBox.Text.Trim(), out var h) ? h : null;
        if (width == null && height == null)
        {
            ConfirmDialog.Info("Bitte Breite und/oder Höhe als Zahl eingeben.", this);
            return;
        }
        _manager.SetGeometry(_vm, null, null, width, height);
        WidthBox.Text = ((int)Math.Round(_vm.Width)).ToString();
        HeightBox.Text = ((int)Math.Round(_vm.Height)).ToString();
    }

    private void SizeToAll_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;
        var (confirmed, _) = ConfirmDialog.Show(
            $"Alle anderen Bereiche auf {(int)Math.Round(_vm.Width)} × {(int)Math.Round(_vm.Height)} setzen?",
            this, okText: "Übertragen");
        if (!confirmed) return;

        var changed = _manager.ApplySizeToAll(_vm);
        ConfirmDialog.Info($"{changed} Bereich(e) angepasst.", this);
    }

    private void SnapAll_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;
        if (GridSnapCheck.IsChecked != true)
        {
            ConfirmDialog.Info("Dafür muss „Am Raster ausrichten“ eingeschaltet sein.", this);
            return;
        }
        var changed = _manager.SnapAllToGrid();
        ConfirmDialog.Info(changed > 0
            ? $"{changed} Bereich(e) am Raster ausgerichtet."
            : "Alle Bereiche liegen bereits exakt am Raster.", this);
    }

    private void EdgeSnap_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.EdgeSnapEnabled = true;
    }

    private void EdgeSnap_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.EdgeSnapEnabled = false;
    }

    private void PickFenceIcon_Click(object sender, RoutedEventArgs e)
    {
        var (ok, value) = IconPickerDialog.Show(this);
        if (ok) _vm.IconPath = value;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _vm.ActiveTab?.FolderPath;
        try
        {
            if (folder != null && Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ordner oeffnen fehlgeschlagen: {ex.Message}");
        }
    }

    // --- Allgemein ---

    private void ImportBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_manager?.Bookmarks is not { } bookmarks) return;
        var added = bookmarks.SyncChrome();
        ConfirmDialog.Info(added > 0
            ? $"{added} neue Lesezeichen in den Bereich „Lesezeichen“ übernommen."
            : "Keine neuen Lesezeichen gefunden (alles bereits vorhanden).", this);
    }

    private void ImportFirefoxBookmarks_Click(object sender, RoutedEventArgs e)
    {
        if (_manager?.Bookmarks is not { } bookmarks) return;
        var added = bookmarks.SyncFirefox();
        if (added > 0)
        {
            ConfirmDialog.Info($"{added} neue Lesezeichen in den Bereich „Lesezeichen“ übernommen.", this);
            return;
        }
        ConfirmDialog.Info(bookmarks.LastFirefoxNote
                           ?? "Keine neuen Lesezeichen gefunden (alles bereits vorhanden).", this);
    }

    // --- Raster / Kanten-Einrasten ---

    private void SelectGridSize(int size)
    {
        foreach (ComboBoxItem item in GridSizeBox.Items)
        {
            if (item.Tag is string tag && tag == size.ToString())
            {
                GridSizeBox.SelectedItem = item;
                return;
            }
        }
        GridSizeBox.SelectedItem = GridSizeBox.Items[1]; // Normal (20)
    }

    /// Zwischenraum, in dem Bereiche neben- und untereinander einrasten.
    private void SelectSnapGap(double mm)
    {
        foreach (ComboBoxItem item in SnapGapBox.Items)
        {
            if (item.Tag is string tag && double.TryParse(tag, out var wert)
                && Math.Abs(wert - mm) < 0.01)
            {
                SnapGapBox.SelectedItem = item;
                return;
            }
        }
        SnapGapBox.SelectedItem = SnapGapBox.Items[2]; // Normal (6 mm)
    }

    private void SnapGap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _manager == null) return;
        if (SnapGapBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && double.TryParse(tag, out var mm))
            _manager.SnapGapMillimeters = mm;
    }

    private int SelectedGridSize()
        => GridSizeBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var size)
            ? size
            : 20;

    private void GridSnap_Checked(object sender, RoutedEventArgs e)
    {
        GridSizeBox.IsEnabled = true;
        if (_initialized && _manager != null) _manager.GridSize = SelectedGridSize();
    }

    private void GridSnap_Unchecked(object sender, RoutedEventArgs e)
    {
        GridSizeBox.IsEnabled = false;
        if (_initialized && _manager != null) _manager.GridSize = 0; // Ausrichten aus
    }

    private void GridSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || _manager == null) return;
        if (GridSnapCheck.IsChecked != true) return;
        _manager.GridSize = SelectedGridSize();
    }

    // --- Darstellung und Leistung ---

    private void Blur_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.BlurEnabled = true;
    }

    private void Blur_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.BlurEnabled = false;
    }

    private void Favicon_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.AutoFavicons = true;
    }

    private void Favicon_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initialized && _manager != null) _manager.AutoFavicons = false;
    }

    private void RestoreToDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_manager == null) return;
        var source = _manager.ConfigSource();
        var count = DesktopRestore.Count(source);
        if (count == 0)
        {
            ConfirmDialog.Info("In den Bereichen liegen keine Dateien.", this);
            return;
        }

        var (confirmed, _) = ConfirmDialog.Show(
            $"{count} Datei(en) aus allen Bereichen zurück auf den Desktop legen?\n\n" +
            "Die Bereiche bleiben bestehen, sind danach aber leer.",
            this, okText: "Auf den Desktop legen");
        if (!confirmed) return;

        var (moved, failed) = DesktopRestore.RestoreAll(source);
        var text = $"{moved} Datei(en) auf den Desktop gelegt.";
        if (failed > 0) text += $"\n{failed} konnten nicht verschoben werden (evtl. in Benutzung).";
        ConfirmDialog.Info(text, this);
    }

    private void Sweep_Checked(object sender, RoutedEventArgs e)
    {
        if (_initialized) _manager?.SetDesktopSweep(true);
    }

    private void Sweep_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_initialized) _manager?.SetDesktopSweep(false);
    }

    private void BackupPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initialized && _manager != null)
            _manager.AutoBackupFolder = BackupPathBox.Text;
        UpdateBackupCloudHint();
        UpdateDailyBackupStatus(); // Ohne Ordner laeuft die Automatik nicht
    }

    /// Weist darauf hin, ob die Sicherung den Rechner ueberlebt.
    private void UpdateBackupCloudHint()
    {
        if (BackupCloudHint == null) return;
        var path = BackupPathBox?.Text ?? "";

        if (path.Trim().Length == 0)
        {
            BackupCloudHint.Foreground = System.Windows.Media.Brushes.Khaki;
            BackupCloudHint.Text = "Noch kein Ordner hinterlegt — lege ihn am besten auf ein Cloud-Laufwerk.";
            return;
        }

        if (BackupService.IsOffMachine(path))
        {
            BackupCloudHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            BackupCloudHint.Text = "Liegt außerhalb dieses Rechners — die Sicherung überlebt damit auch einen Rechnerwechsel.";
        }
        else
        {
            BackupCloudHint.Foreground = System.Windows.Media.Brushes.Khaki;
            BackupCloudHint.Text = "Dieser Ordner liegt auf dem Rechner selbst. Bei einem Ausfall wäre die Sicherung mit weg — ein Cloud-Ordner ist sicherer.";
        }
    }

    private void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner für automatische Sicherungen" };
        if (dialog.ShowDialog() == true)
            BackupPathBox.Text = dialog.FolderName;
    }

    private void DailyBackup_Checked(object sender, RoutedEventArgs e) => SetDailyBackup(true);
    private void DailyBackup_Unchecked(object sender, RoutedEventArgs e) => SetDailyBackup(false);

    private void SetDailyBackup(bool an)
    {
        if (!_initialized || _manager == null) return;
        _manager.Config.Config.AutoBackupDaily = an;
        _manager.Config.SaveDebounced();
        UpdateDailyBackupStatus();
    }

    /// <summary>
    /// Zeigt, ob und wann zuletzt von selbst gesichert wurde. Ohne diese Zeile
    /// bliebe voellig offen, ob die Automatik ueberhaupt arbeitet — sie meldet
    /// sich sonst ja bewusst nie.
    /// </summary>
    private void UpdateDailyBackupStatus()
    {
        if (DailyBackupStatus == null || _manager == null) return;
        var config = _manager.Config.Config;

        if (!config.AutoBackupDaily)
        {
            DailyBackupStatus.Foreground = System.Windows.Media.Brushes.Khaki;
            DailyBackupStatus.Text = "Es wird nur gesichert, wenn du es von Hand anstößt.";
            return;
        }

        if (string.IsNullOrWhiteSpace(config.AutoBackupFolder))
        {
            DailyBackupStatus.Foreground = System.Windows.Media.Brushes.Khaki;
            DailyBackupStatus.Text = "Ohne Ordner passiert nichts — bitte oben einen Pfad eintragen.";
            return;
        }

        DailyBackupStatus.Foreground = System.Windows.Media.Brushes.LightGreen;

        if (config.LastAutoBackupUtc is not { } letzte)
        {
            DailyBackupStatus.Text = "Eingeschaltet. Die erste Sicherung läuft wenige Minuten nach dem Start.";
            return;
        }

        var vergangen = DateTime.UtcNow - letzte;
        var wann = vergangen < TimeSpan.FromMinutes(2) ? "gerade eben"
                 : vergangen < TimeSpan.FromHours(1) ? $"vor {(int)vergangen.TotalMinutes} Minuten"
                 : vergangen < TimeSpan.FromDays(1) ? $"vor {(int)vergangen.TotalHours} Stunden"
                 : $"am {letzte.ToLocalTime():dd.MM.yyyy 'um' HH:mm} Uhr";

        DailyBackupStatus.Text = $"Zuletzt {wann} von selbst gesichert.";
    }

    private void AutoBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.CreateBackupAuto(this);

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.CreateBackupInteractive(this);

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.RestoreBackupInteractive(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
