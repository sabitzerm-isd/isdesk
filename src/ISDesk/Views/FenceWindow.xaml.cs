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

        // "Desktop anzeigen" (Win+D) minimiert alle Fenster — Bereiche gehoeren
        // aber ZUM Desktop und stellen sich sofort wieder her.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        };

        LocationChanged += (_, _) => UpdateBlurCrop();
        SizeChanged += (_, _) => { UpdateRootClip(); UpdateBlurCrop(); UpdateSearchVisibility(); };
        WallpaperService.Changed += UpdateBlurCrop;
        SearchService.TermChanged += SyncSearchBox;
        Closed += (_, _) =>
        {
            WallpaperService.Changed -= UpdateBlurCrop;
            SearchService.TermChanged -= SyncSearchBox;
        };
        Loaded += (_, _) => { UpdateRootClip(); UpdateBlurCrop(); UpdateSearchVisibility(); };
        MouseEnter += (_, _) => UpdateSearchVisibility();
        MouseLeave += (_, _) => UpdateSearchVisibility();
        SearchBox.GotKeyboardFocus += (_, _) => UpdateSearchVisibility();
        SearchBox.LostKeyboardFocus += (_, _) => UpdateSearchVisibility();
    }

    /// Suchbox nur zeigen, wenn Platz da ist (sonst blockiert sie die Greif-Flaeche
    /// der Titelzeile) und der Bereich gehovert wird bzw. eine Suche laeuft.
    private void UpdateSearchVisibility()
    {
        var show = ActualWidth >= 340
                   && (IsMouseOver || SearchBox.Text.Length > 0 || SearchBox.IsKeyboardFocusWithin);
        SearchContainer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Live-Suche (global ueber alle Bereiche) ---

    private bool _searchSyncing;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchVisibility();
        if (_searchSyncing) return;
        SearchService.SetTerm(SearchBox.Text);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            SearchService.SetTerm("");
            e.Handled = true;
        }
    }

    /// Suchboxen aller Bereiche zeigen denselben Begriff (ausser der, in der getippt wird).
    private void SyncSearchBox()
    {
        if (SearchBox.IsKeyboardFocusWithin) return;
        _searchSyncing = true;
        SearchBox.Text = SearchService.Term;
        _searchSyncing = false;
    }

    /// Setzt den weichgezeichneten Wallpaper-Ausschnitt passend zur Fensterposition
    /// (Fill-Modus: Bild skaliert auf Monitorgroesse, mittig beschnitten).
    private void UpdateBlurCrop()
    {
        if (!_vm.Blur || WallpaperService.Current is not { } wallpaper)
        {
            BlurLayer.Background = null;
            return;
        }

        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var sb = screen.Bounds; // Pixel

            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double wx = Left * dpi - sb.X, wy = Top * dpi - sb.Y;
            double ww = ActualWidth * dpi, wh = ActualHeight * dpi;

            double imgW = wallpaper.PixelWidth, imgH = wallpaper.PixelHeight;
            double scale = Math.Max(sb.Width / imgW, sb.Height / imgH);
            double ox = (imgW * scale - sb.Width) / 2.0;   // links/oben weggeschnittener Teil
            double oy = (imgH * scale - sb.Height) / 2.0;

            // Ausschnitt inkl. Rand fuer den Blur-Ueberhang (Margin -40 der BlurLayer)
            double pad = 40 * dpi;
            double ix = (wx - pad + ox) / scale, iy = (wy - pad + oy) / scale;
            double iw = (ww + 2 * pad) / scale, ih = (wh + 2 * pad) / scale;

            BlurLayer.Background = new System.Windows.Media.ImageBrush(wallpaper)
            {
                Viewbox = new Rect(ix / imgW, iy / imgH, iw / imgW, ih / imgH),
                ViewboxUnits = System.Windows.Media.BrushMappingMode.RelativeToBoundingBox,
                Stretch = System.Windows.Media.Stretch.Fill
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Blur-Ausschnitt fehlgeschlagen: {ex.Message}");
            BlurLayer.Background = null;
        }
    }

    public FenceViewModel ViewModel => _vm;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DesktopPinning.Attach(this);
        // GridSnapBehavior.Attach(this); // Raster vorerst deaktiviert (spaeter verbessern)
        ResizeMode = _vm.Locked ? ResizeMode.NoResize : ResizeMode.CanResize;
    }

    /// Runde Ecken selbst clippen — DWM-Rundungen gelten nicht zuverlaessig
    /// fuer Fenster mit echter Transparenz (Layered Windows).
    private void UpdateRootClip()
    {
        RootGrid.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, RootGrid.ActualWidth, RootGrid.ActualHeight), 10, 10);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FenceViewModel.Blur))
            UpdateBlurCrop();
        if (e.PropertyName is nameof(FenceViewModel.Opacity)
            or nameof(FenceViewModel.TitleBarOpacity)
            or nameof(FenceViewModel.Blur))
            Manager?.PropagateAppearance(_vm); // Optik gilt fuer alle Bereiche
        if (e.PropertyName is nameof(FenceViewModel.Locked))
        {
            ResizeMode = _vm.Locked ? ResizeMode.NoResize : ResizeMode.CanResize;
            Manager?.PropagateLock(_vm.Locked); // Sperren gilt fuer alle Bereiche
        }
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
        {
            _vm.ActiveTab = tab;
            e.Handled = true; // nicht zusaetzlich als Fenster-Drag interpretieren
        }
    }

    /// Leere Flaeche der Tab-Leiste: zusaetzliche Greif-Flaeche zum Verschieben.
    private void TabBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || _vm.Locked) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* Randfaelle */ }
    }

    /// Beim Ziehen ueber einen Tab-Reiter: Effekt anzeigen und den Tab gleich
    /// aktivieren (so kann man auch direkt in dessen Flaeche fallen lassen).
    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (!IsDroppable(e.Data))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (ctrl ? DragDropEffects.Copy : DragDropEffects.Move)
            : DragDropEffects.Link;
        e.Handled = true;

        if ((sender as FrameworkElement)?.DataContext is TabViewModel tab && !tab.IsActive)
            _vm.ActiveTab = tab;
    }

    /// Ablegen auf einem Tab-Reiter: Dateien (oder Browser-Links) in DIESEN Tab.
    private void Tab_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true; // nicht zusaetzlich vom Fenster-Drop verarbeiten
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (WebLinkFactory.TryGetUrl(e.Data, out var url, out var name))
            {
                try { WebLinkFactory.CreateUrlFile(tab.FolderPath, url, name); }
                catch (Exception ex) { ConfirmDialog.Info($"Link konnte nicht angelegt werden:\n{ex.Message}", this); }
                return;
            }

            foreach (var (clsid, display) in ShellDropHelper.GetVirtualItems(e.Data))
            {
                try { ShellDropHelper.CreateClsidFolder(tab.FolderPath, clsid, display); }
                catch (Exception ex) { ConfirmDialog.Info($"„{display}“ konnte nicht abgelegt werden:\n{ex.Message}", this); }
            }
            return;
        }

        var sources = (string[])e.Data.GetData(DataFormats.FileDrop);
        var copy = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        var errors = new List<string>();

        foreach (var source in sources)
        {
            try { TransferInto(source, tab.FolderPath, copy); }
            catch (Exception ex) { errors.Add($"{Path.GetFileName(source)} ({ex.Message})"); }
        }

        if (errors.Count > 0)
        {
            ConfirmDialog.Info(
                $"{errors.Count} Element(e) konnten nicht in „{tab.Title}“ verschoben werden:\n\n"
                + string.Join("\n", errors), this);
        }
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

    /// Untermenue "In anderen Bereich verschieben" beim Oeffnen mit den
    /// aktuellen Bereichen fuellen.
    private void MoveTabMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu) return;
        if (menu.DataContext is not TabViewModel tab) return;

        menu.Items.Clear();
        var targets = Manager?.Windows
            .Where(w => !ReferenceEquals(w.ViewModel, _vm))
            .OrderBy(w => w.ViewModel.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (targets is not { Count: > 0 })
        {
            menu.Items.Add(new MenuItem { Header = "(kein weiterer Bereich vorhanden)", IsEnabled = false });
            return;
        }

        foreach (var target in targets)
        {
            var targetVm = target.ViewModel;
            var item = new MenuItem { Header = targetVm.Title };
            item.Click += (_, _) => Manager?.MoveTab(_vm, tab, targetVm);
            menu.Items.Add(item);
        }
    }

    private void TabColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TabViewModel tab, Tag: string hex })
            tab.Color = hex;
    }

    private void TabColorDefault_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: TabViewModel tab })
            tab.Color = null;
    }

    private void TabColorCustom_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TabViewModel tab }) return;

        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (!string.IsNullOrEmpty(tab.Color))
        {
            try
            {
                var current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(tab.Color);
                dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            }
            catch (Exception) { /* Standardfarbe des Dialogs */ }
        }
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            tab.Color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    /// Endungs-Regel des Desktop-Einsammlers fuer diesen Tab bearbeiten
    /// (z. B. "sza; szx" → alle SZA/SZX vom Desktop landen automatisch hier).
    private void TabAutoExtensions_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;

        var current = string.Join("; ", tab.Config.AutoExtensions);
        var input = InputDialog.Ask(
            "Dateiendungen für diesen Tab (mit ; getrennt, ohne Punkt — z. B. sza; szx).\n" +
            "Der Desktop-Einsammler legt passende Dateien automatisch hier ab:",
            current, this);
        if (input == null) return;

        tab.Config.AutoExtensions = input
            .Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().TrimStart('*', '.').ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();
        Manager?.PersistNow();
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
            Manager?.CreateFence(name, this); // Manager waehlt eine freie Stelle daneben
    }

    private void PickFenceIcon_Click(object sender, RoutedEventArgs e)
    {
        var (ok, value) = IconPickerDialog.Show(this);
        if (ok) _vm.IconPath = value;
    }

    private void PickTabIcon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;
        var (ok, value) = IconPickerDialog.Show(this);
        if (ok) tab.IconPath = value;
    }

    private void DetachTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TabViewModel tab) return;
        Manager?.DetachTabToNewFence(_vm, tab);
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
        var dialog = new SettingsDialog(_vm, Manager, this);
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

    private static bool IsDroppable(IDataObject data)
        => data.GetDataPresent(DataFormats.FileDrop)
           || data.GetDataPresent("UniformResourceLocatorW")
           || data.GetDataPresent("UniformResourceLocator")
           || data.GetDataPresent("Shell IDList Array"); // virtuelle Objekte (Papierkorb …)

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (IsDroppable(e.Data))
        {
            var ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? (ctrl ? DragDropEffects.Copy : DragDropEffects.Move)
                : DragDropEffects.Link; // URL aus dem Browser
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

        // Internes Umsortieren: Icon stammt aus DIESEM Tab → nur Reihenfolge aendern.
        if (e.Data.GetDataPresent("ISDesk.SourcePath")
            && e.Data.GetData("ISDesk.SourcePath") is string internalSource
            && string.Equals(Path.GetDirectoryName(internalSource.TrimEnd('\\', '/')),
                tab.FolderPath, StringComparison.OrdinalIgnoreCase))
        {
            var targetItem = FindItem(e.OriginalSource as DependencyObject);
            if (targetItem == null || !string.Equals(targetItem.Path, internalSource, StringComparison.OrdinalIgnoreCase))
                tab.ReorderTo(internalSource, targetItem?.Path);
            return;
        }

        // Browser-Link (Chrome/Edge/Firefox): .url-Verknuepfung mit Favicon anlegen.
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            if (WebLinkFactory.TryGetUrl(e.Data, out var url, out var name))
            {
                try { WebLinkFactory.CreateUrlFile(tab.FolderPath, url, name); }
                catch (Exception ex) { ConfirmDialog.Info($"Link konnte nicht angelegt werden:\n{ex.Message}", this); }
                return;
            }

            // Virtuelle System-Objekte (Papierkorb, Dieser PC …) als CLSID-Ordner ablegen.
            foreach (var (clsid, display) in ShellDropHelper.GetVirtualItems(e.Data))
            {
                try { ShellDropHelper.CreateClsidFolder(tab.FolderPath, clsid, display); }
                catch (Exception ex) { ConfirmDialog.Info($"„{display}“ konnte nicht abgelegt werden:\n{ex.Message}", this); }
            }
            return;
        }

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
            ConfirmDialog.Info(
                $"{errors.Count} Element(e) konnten nicht {verb} werden:\n\n" + string.Join("\n", errors), this);
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

        // Liegt am Ziel schon eine INHALTSGLEICHE Datei gleichen Namens, kein
        // " (2)"-Duplikat anlegen, sondern still ueberspringen.
        var existing = Path.Combine(targetDir, name);
        if (!isDir && File.Exists(existing) && FilesEqual(source, existing))
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

    /// Byte-Vergleich fuer kleine Dateien (Verknuepfungen etc.); grosse Dateien
    /// gelten als verschieden und laufen in die normale " (n)"-Umbenennung.
    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length) return false;
            if (infoA.Length > 4 * 1024 * 1024) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception)
        {
            return false;
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
        data.SetData("ISDesk.SourcePath", _dragItem.Path); // fuer internes Umsortieren
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
        var trimmed = item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(trimmed);
        if (dir is null) return;

        // Nur der ANZEIGE-Name wird bearbeitet — die Dateiendung (.url/.lnk/…)
        // bleibt automatisch erhalten, sonst verliert die Datei Typ und Icon.
        var fileName = Path.GetFileName(trimmed);
        var extension = item.IsFolder ? "" : Path.GetExtension(fileName);
        var stem = item.IsFolder ? fileName : Path.GetFileNameWithoutExtension(fileName);

        var input = InputDialog.Ask("Neuer Name:", stem, this);
        if (string.IsNullOrWhiteSpace(input)) return;

        var newName = input.Trim();
        if (extension.Length > 0 && !newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            newName += extension;
        if (string.Equals(newName, fileName, StringComparison.Ordinal)) return;

        try
        {
            var dest = Path.Combine(dir, newName);
            if (item.IsFolder) Directory.Move(item.Path, dest);
            else File.Move(item.Path, dest);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info($"Umbenennen fehlgeschlagen:\n{ex.Message}", this);
        }
    }

    private void IconRecycle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not IconItemViewModel item) return;

        var (confirmed, _) = ConfirmDialog.Show(
            $"„{item.DisplayName}“ in den Papierkorb verschieben?",
            this, okText: "In den Papierkorb");
        if (!confirmed) return;

        try
        {
            if (item.IsFolder)
                FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex)
        {
            ConfirmDialog.Info($"Löschen fehlgeschlagen:\n{ex.Message}", this);
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
