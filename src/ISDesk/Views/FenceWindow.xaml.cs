using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ISDesk.Interop;
using ISDesk.Services;
using ISDesk.ViewModels;
using Microsoft.VisualBasic.FileIO;

namespace ISDesk.Views;

public partial class FenceWindow : Window
{
    private readonly FenceViewModel _vm;
    private Point _dragStart;
    private IconItemViewModel? _dragItem;

    public FenceManager? Manager { get; set; }

    public FenceWindow(FenceViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = vm.X;
        Top = vm.Y;
        Width = vm.Width;
        Height = vm.Height;

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) => _vm.ActivateAllTabs();
        Closed += (_, _) => _vm.DisposeTabs();
    }

    public FenceViewModel ViewModel => _vm;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        BottomMostBehavior.Attach(this);
        WindowBackdrop.Apply(this, _vm.Opacity, _vm.Blur);
        ResizeMode = _vm.Locked ? ResizeMode.NoResize : ResizeMode.CanResize;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FenceViewModel.Opacity) or nameof(FenceViewModel.Blur))
            WindowBackdrop.Apply(this, _vm.Opacity, _vm.Blur);
        if (e.PropertyName is nameof(FenceViewModel.Locked))
            ResizeMode = _vm.Locked ? ResizeMode.NoResize : ResizeMode.CanResize;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        if (e.ClickCount == 2)
        {
            BeginTitleEdit();
            e.Handled = true;
            return;
        }

        if (_vm.Locked) return; // fixiert: nicht verschieben

        try { DragMove(); }
        catch (InvalidOperationException) { /* DragMove kann in Randfaellen werfen */ }
    }

    // --- Titel direkt in der Titelzeile umbenennen (Doppelklick) ---

    private void BeginTitleEdit()
    {
        TitleEditBox.Text = _vm.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditBox.Visibility = Visibility.Visible;
        Activate();
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    private void EndTitleEdit(bool save)
    {
        if (TitleEditBox.Visibility != Visibility.Visible) return;

        if (save)
        {
            var text = TitleEditBox.Text.Trim();
            if (text.Length > 0) _vm.Title = text;
        }
        TitleEditBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
    }

    private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { EndTitleEdit(save: true); e.Handled = true; }
        else if (e.Key == Key.Escape) { EndTitleEdit(save: false); e.Handled = true; }
    }

    private void TitleEditBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        => EndTitleEdit(save: true);

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TabViewModel tab)
            _vm.ActiveTab = tab;
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask("Name des Tabs:", "", this);
        if (!string.IsNullOrWhiteSpace(name))
            _vm.AddTab(name);
    }

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;
        var name = InputDialog.Ask("Neuer Name des Tabs:", tab.Title, this);
        if (!string.IsNullOrWhiteSpace(name))
            _vm.RenameTab(tab, name);
    }

    private void RemoveTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;
        var (confirmed, _) = ConfirmDialog.Show(
            $"Tab „{tab.Title}“ entfernen?\n\nDer zugehörige Ordner bleibt auf der Platte erhalten.",
            this, okText: "Entfernen");
        if (confirmed)
            _vm.RemoveTab(tab);
    }

    // --- Bereichs-Kontextmenue ---

    private void RenameFence_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask("Neuer Name des Bereichs:", _vm.Title, this);
        if (!string.IsNullOrWhiteSpace(name))
            _vm.Title = name;
    }

    private void NewFence_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask("Name des neuen Bereichs:", "Neuer Bereich", this);
        if (!string.IsNullOrWhiteSpace(name))
            Manager?.CreateFence(name, new Point(Left + 40, Top + 40));
    }

    private void RemoveFence_Click(object sender, RoutedEventArgs e)
    {
        var (confirmed, deleteFolders) = ConfirmDialog.Show(
            $"Bereich „{_vm.Title}“ entfernen?",
            this, okText: "Entfernen",
            checkboxText: "Zugehörige Ordner in den Papierkorb verschieben");
        if (confirmed)
            Manager?.RemoveFence(_vm, deleteFolders);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_vm, Manager?.Backup, this);
        dialog.ShowDialog();
    }

    /// Legt im aktiven Tab eine .lnk-Verknuepfung auf einen frei gewaehlten Ordner an.
    private void AddFolderLink_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ActiveTab is not { } tab) return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Ordner auswählen, auf den die Verknüpfung zeigen soll"
        };
        if (dialog.ShowDialog() != true) return;

        var target = dialog.FolderName;
        var name = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) name = target.Replace(":", "").Replace(@"\", "");

        try
        {
            var lnkPath = Path.Combine(tab.FolderPath, name + ".lnk");
            var n = 2;
            while (File.Exists(lnkPath))
                lnkPath = Path.Combine(tab.FolderPath, $"{name} ({n++}).lnk");

            ShortcutFactory.CreateLnk(lnkPath, target);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info($"Verknüpfung konnte nicht angelegt werden:\n{ex.Message}", this);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(_vm.BaseFolder, _vm.Title);
        if (!Directory.Exists(folder))
            folder = _vm.ActiveTab?.FolderPath ?? folder;
        try
        {
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ordner oeffnen fehlgeschlagen: {ex.Message}");
        }
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void IconList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FindItem(e.OriginalSource as DependencyObject) is { } item)
            Launch(item.Path);
    }

    private static IconItemViewModel? FindItem(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
            source = VisualTreeHelper.GetParent(source);
        return (source as ListBoxItem)?.DataContext as IconItemViewModel;
    }

    private static void Launch(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Start fehlgeschlagen: {path} — {ex.Message}");
        }
    }

    // --- Drag & Drop: hinein ---

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
            e.Effects = ctrl ? DragDropEffects.Copy : DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (_vm.ActiveTab is not { } tab) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
        var copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        var targetDir = tab.FolderPath;
        var errors = new List<string>();

        foreach (var source in sources)
        {
            try { TransferInto(source, targetDir, copy); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(source)} ({ex.Message})"); }
        }

        if (errors.Count > 0)
        {
            var verb = copy ? "kopiert" : "verschoben";
            MessageBox.Show(
                $"{errors.Count} Element(e) konnten nicht {verb} werden:\n\n" + string.Join("\n", errors),
                "ISDesk", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void TransferInto(string source, string targetDir, bool copy)
    {
        var isDir = Directory.Exists(source);
        if (!isDir && !File.Exists(source)) return;

        var trimmed = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        var sourceParent = Path.GetDirectoryName(trimmed);

        // Beim Verschieben: Quelle liegt bereits im Zielordner → nichts tun.
        if (!copy && string.Equals(sourceParent, targetDir, StringComparison.OrdinalIgnoreCase))
            return;

        var dest = MakeUniqueDestination(targetDir, name);
        if (isDir)
        {
            if (copy || !SameVolume(source, dest))
            {
                CopyDirectory(source, dest);
                if (!copy) Directory.Delete(source, true);
            }
            else
            {
                Directory.Move(source, dest);
            }
        }
        else
        {
            if (copy) File.Copy(source, dest);
            else File.Move(source, dest); // File.Move funktioniert auch ueber Volumes
        }
    }

    private static bool SameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)),
                         Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }

    private static string MakeUniqueDestination(string targetDir, string name)
    {
        var dest = Path.Combine(targetDir, name);
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var n = 2;
        do { dest = Path.Combine(targetDir, $"{stem} ({n++}){ext}"); }
        while (File.Exists(dest) || Directory.Exists(dest));
        return dest;
    }

    // --- Drag & Drop: heraus ---

    private void IconList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = FindItem(e.OriginalSource as DependencyObject);
    }

    private void IconList_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4) return;

        var data = new DataObject(DataFormats.FileDrop, new[] { _dragItem.Path });
        _dragItem = null;
        try { DragDrop.DoDragDrop(IconList, data, DragDropEffects.Move | DragDropEffects.Copy); }
        catch (Exception ex) { Debug.WriteLine($"Drag fehlgeschlagen: {ex.Message}"); }
    }

    // --- Icon-Kontextmenue ---

    private void IconOpen_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is IconItemViewModel item)
            Launch(item.Path);
    }

    private void IconShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IconItemViewModel item) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Explorer-Anzeige fehlgeschlagen: {ex.Message}");
        }
    }

    private void IconRename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IconItemViewModel item) return;
        var dir = Path.GetDirectoryName(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (dir is null) return;

        var currentName = Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var newName = InputDialog.Ask("Neuer Name:", currentName, this);
        if (string.IsNullOrWhiteSpace(newName) || newName == currentName) return;

        try
        {
            var dest = Path.Combine(dir, newName);
            if (item.IsFolder) Directory.Move(item.Path, dest);
            else File.Move(item.Path, dest);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Umbenennen fehlgeschlagen:\n{ex.Message}", "ISDesk",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void IconRecycle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IconItemViewModel item) return;

        var result = MessageBox.Show(
            $"„{item.DisplayName}“ in den Papierkorb verschieben?", "ISDesk",
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        try
        {
            if (item.IsFolder)
                FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Loeschen fehlgeschlagen:\n{ex.Message}", "ISDesk",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        _vm.X = Left;
        _vm.Y = Top;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _vm.Width = ActualWidth;
        _vm.Height = ActualHeight;
    }
}
