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
        VersionText.Text = $"ISDesk v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        _initialized = true;
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
