using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ISDesk.Interop;
using ISDesk.Services;
using ISDesk.ViewModels;

namespace ISDesk.Views;

/// Optionen eines Bereichs (Titel, Icon-Groesse, Transparenz, Blur) plus
/// Sicherung/Wiederherstellung aller Bereiche. Aenderungen wirken sofort.
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
        _initialized = true;

        if (centerOn != null)
        {
            Loaded += (_, _) =>
            {
                Left = centerOn.Left + (centerOn.ActualWidth - ActualWidth) / 2;
                Top = centerOn.Top + (centerOn.ActualHeight - ActualHeight) / 2;
                DialogPlacement.ClampToWorkArea(this);
            };
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Loaded += (_, _) => DialogPlacement.ClampToWorkArea(this);
        }

        VersionText.Text = $"ISDesk v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.Apply(this, 0.95, true);
    }

    private void IconSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (IconSizeBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (!int.TryParse(tag, out var size)) return;

        // Optionen gelten fuer alle Bereiche (Nutzer-Vorgabe)
        if (_manager != null)
        {
            _manager.SetIconSizeAll(size);
        }
        else
        {
            foreach (var tab in _vm.Tabs)
                tab.IconSize = size;
        }
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
