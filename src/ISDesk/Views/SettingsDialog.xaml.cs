using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ISDesk.Interop;
using ISDesk.Services;
using ISDesk.ViewModels;

namespace ISDesk.Views;

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

        BlurCheck.IsChecked = manager?.BlurEnabled ?? true;
        FaviconCheck.IsChecked = manager?.AutoFavicons ?? true;

        VersionText.Text = $"ISDesk v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        _initialized = true;

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
            UpdateStatusText.Text = $"ISDesk {UpdateService.CurrentVersion} ist aktuell.";
            return;
        }

        var mb = _updateInfo.Size > 0 ? $", {_updateInfo.Size / 1024 / 1024} MB" : "";
        UpdateStatusText.Text = $"Neue Version {_updateInfo.LatestVersion} verfügbar (du hast {UpdateService.CurrentVersion}{mb}).";
        UpdateInstallButton.Visibility = Visibility.Visible;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdateAsync();

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
        if (PanelAllgemein == null || PanelBereich == null) return;
        PanelAllgemein.Visibility = NavAllgemein.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelBereich.Visibility = NavBereich.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
    }

    private void BrowseBackupPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner für automatische Sicherungen" };
        if (dialog.ShowDialog() == true)
            BackupPathBox.Text = dialog.FolderName;
    }

    private void AutoBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.CreateBackupAuto(this);

    private void CreateBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.CreateBackupInteractive(this);

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
        => _manager?.Backup?.RestoreBackupInteractive(this);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
