using System.IO;
using System.Text;
using System.Windows;
using MSDesk.Models;
using MSDesk.Services;

namespace MSDesk;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private ConfigService? _config;
    private FenceManager? _manager;
    private AutostartService? _autostart;
    private TrayService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception, "DispatcherUnhandledException");
            args.Handled = true;
        };

        // Deinstallation: Inhalte der Bereiche zurueck auf den Desktop legen und
        // sofort beenden (kein Tray, keine Fenster).
        if (e.Args.Any(a => string.Equals(a, DesktopRestore.CommandLineSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            RestoreIconsAndExit();
            return;
        }

        _singleInstanceMutex = new Mutex(true, @"Global\MSDesk_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Bereits eine Instanz aktiv → still beenden.
            Shutdown();
            return;
        }

        // Alles protokollieren, was sonst unbemerkt zum Programmabbruch fuehrt —
        // sonst steht man vor einer Anwendung, die kommentarlos nicht startet.
        DispatcherUnhandledException += (_, args) => LogCrash(args.Exception, "DispatcherUnhandledException");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash(args.ExceptionObject as Exception, "UnhandledException");

        Interop.DarkMenuMode.EnableForApp(); // dunkle Explorer-Kontextmenues

        _config = new ConfigService();
        _config.Load();
        Interop.GridSnapBehavior.GridSize = _config.Config.GridSize; // Raster
        Interop.GridSnapBehavior.EdgeSnapEnabled = _config.Config.EdgeSnap; // Kanten-Einrasten
        VisualSettings.Init(_config.Config.BlurEnabled, _config.Config.AutoFavicons);
        _manager = new FenceManager(_config);
        _autostart = new AutostartService();

        // Autostart: beim allerersten Start automatisch einschalten; danach nur
        // noch den Pfad korrigieren, falls die EXE verschoben/neu installiert wurde.
        AutostartService.RemoveLegacyEntry(); // Vorgaenger "ISDesk" nicht mitstarten
        if (!_config.Config.AutostartConfigured)
        {
            _autostart.Enable();
            _config.Config.AutostartConfigured = true;
            _config.SaveDebounced();
        }
        else if (_config.Config.AutostartWanted)
        {
            // Gewuenscht: fehlenden Eintrag neu anlegen, veralteten Pfad korrigieren.
            _autostart.EnsureEnabled();
        }
        else
        {
            _autostart.EnsureCurrentPath(); // ausdruecklich abgeschaltet → nur Pfadpflege
        }

        if (_config.Config.Fences.Count == 0)
            CreateWelcomeFence();

        _manager.Backup = new BackupService(_config, _manager);
        PlacementRegistry.Init(_config);
        NoteRegistry.Init(_config);

        // Tabs ohne eigenes Symbol bekommen automatisch eins, das zum Namen passt.
        // Von Hand gesetzte Symbole bleiben unangetastet.
        if (TabIconRules.ApplyMissing(_config.Config) > 0) _config.SaveDebounced();

        // Gespeicherte Bildschirm-Anordnungen auf die geraetenamen-freie Kennung
        // umstellen — sonst waeren sie nach dem Wechsel nicht mehr auffindbar.
        if (DisplayConfig.MigrateKeys(_config.Config) > 0) _config.SaveDebounced();

        _manager.Sweeper = new DesktopSweeper(_config, _manager.GetAblageFolder);
        _manager.Bookmarks = new BookmarkImportService(_config, _manager);
        _manager.OpenAll();
        _manager.ApplyLayoutsForCurrentDisplays();
        // Tabs laden erst beim Anzeigen — das Platz-Gedaechtnis lernt die Ordner
        // deshalb einmalig im Hintergrund (ohne Icons/Ueberwachung).
        PlacementRegistry.LearnAllTabFolders();
        if (_config.Config.DesktopSweep)
            _manager.Sweeper.Start();

        _tray = new TrayService(_manager, _autostart);

        // Erststart: erst einrichten (Name, Sicherungsort), dann die Anleitung.
        Views.SetupDialog.RunOnFirstStart(_config);
        HelpPage.OpenOnFirstRun(_config); // beim allerersten Start die Anleitung zeigen
        CheckForUpdatesAsync(); // beim Start still nach neuer Version schauen

        // Bildschirm-Konfigurationswechsel (Docking, RDP, Beamer): entprellt das
        // gemerkte Layout der neuen Konfiguration anwenden.
        _displayDebounce = new System.Timers.Timer(1200) { AutoReset = false };
        _displayDebounce.Elapsed += (_, _) =>
            Dispatcher.Invoke(() => _manager?.ApplyLayoutsForCurrentDisplays());
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// Vor der Deinstallation aufgerufen: fragt einmal nach und legt dann alle
    /// Dateien aus den Bereichen zurueck auf den Desktop.
    private void RestoreIconsAndExit()
    {
        try
        {
            var config = new ConfigService();
            config.Load();
            var source = AppConfigSource.From(config);
            var count = DesktopRestore.Count(source);

            if (count == 0) { Shutdown(); return; }

            var (confirmed, _) = Views.ConfirmDialog.Show(
                $"MSDesk wird entfernt.\n\n{count} Datei(en) liegen in den Bereichen. " +
                "Sollen sie zurück auf den Desktop gelegt werden?\n\n" +
                "Antwortest du mit „Nein“, bleiben sie in ihren Ordnern unter " +
                $"{config.Config.BaseFolder} liegen.",
                null, okText: "Auf den Desktop legen");

            if (confirmed)
            {
                var (moved, failed) = DesktopRestore.RestoreAll(source);
                var text = $"{moved} Datei(en) auf den Desktop gelegt.";
                if (failed > 0) text += $"\n{failed} konnten nicht verschoben werden.";
                Views.ConfirmDialog.Info(text, null);
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex, "RestoreIconsAndExit");
        }
        Shutdown();
    }

    private System.Timers.Timer? _displayDebounce;

    private async void CheckForUpdatesAsync()
    {
        try
        {
            var service = new UpdateService();
            var info = await service.CheckAsync();
            if (info == null) return;
            Dispatcher.Invoke(() =>
            {
                var banner = new Views.UpdateBanner(service, info);
                banner.Show();
            });
        }
        catch (Exception ex)
        {
            LogCrash(ex, "CheckForUpdates");
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // SOFORT sperren — nicht erst nach der Entprellung: Windows schiebt die
        // Fenster des entfallenen Monitors umgehend zusammen, und diese
        // Zwischenlage darf die gemerkte Anordnung nicht ueberschreiben.
        _manager?.SuspendLayoutSaving();

        _displayDebounce?.Stop();
        _displayDebounce?.Start();
    }

    /// Startet die App neu (nach einer Wiederherstellung). Gibt den
    /// Single-Instance-Mutex vorher frei, damit die neue Instanz starten darf.
    internal void RestartForRestore()
    {
        _tray?.Dispose();
        _tray = null;

        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch (Exception ex)
        {
            LogCrash(ex, "RestartForRestore/Mutex");
        }
        _singleInstanceMutex = null;

        var exe = Environment.ProcessPath;
        if (exe != null)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogCrash(ex, "RestartForRestore/Start");
            }
        }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Interop.AlignmentGuides.Dispose();
        _displayDebounce?.Dispose();
        _manager?.Sweeper?.Dispose();
        _tray?.Dispose();
        _manager?.ShutdownAll();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// Erststart: Bereich "Willkommen" mit Demo-Verknuepfungen. Es werden nur NEUE
    /// Dateien erzeugt, bestehende Nutzerdateien werden nie angefasst.
    private void CreateWelcomeFence()
    {
        var baseFolder = _config!.Config.BaseFolder;
        var welcomeFolder = Path.Combine(baseFolder, "Willkommen");
        var tabFolder = Path.Combine(welcomeFolder, "Allgemein");
        Directory.CreateDirectory(tabFolder);

        TryCreateShortcut(Path.Combine(tabFolder, "Editor.lnk"), @"C:\Windows\System32\notepad.exe");
        TryCreateShortcut(Path.Combine(tabFolder, "Paint.lnk"), @"C:\Windows\System32\mspaint.exe");
        TryCreateShortcut(Path.Combine(tabFolder, "Explorer.lnk"), @"C:\Windows\explorer.exe");
        TryCreateShortcut(Path.Combine(tabFolder, "Fences-Ordner.lnk"), baseFolder);

        const double width = 420, height = 300;
        var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                 ?? new System.Drawing.Rectangle(0, 0, 1280, 800);
        var x = Math.Max(wa.Left, wa.Right - wa.Width * 0.15 - width);
        var y = wa.Top + 40;

        var welcome = new FenceConfig
        {
            Id = Guid.NewGuid(),
            Title = "Willkommen",
            X = x, Y = y, Width = width, Height = height,
            Opacity = _config.Config.DefaultOpacity,
            Blur = _config.Config.DefaultBlur,
            ActiveTab = 0
        };
        welcome.Tabs.Add(new TabConfig { Title = "Allgemein", FolderPath = tabFolder, IconSize = 32 });

        _config.Config.Fences.Add(welcome);
        _config.Save();
    }

    private static void TryCreateShortcut(string lnkPath, string target)
    {
        try
        {
            if (!File.Exists(lnkPath))
                ShortcutFactory.CreateLnk(lnkPath, target);
        }
        catch (Exception ex)
        {
            LogCrash(ex, "CreateShortcut");
        }
    }

    internal static void LogCrash(Exception? ex, string origin)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSDesk");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origin}");
            sb.AppendLine(ex?.ToString() ?? "(keine Ausnahmeinformation)");
            sb.AppendLine(new string('-', 60));
            File.AppendAllText(Path.Combine(dir, "crash.log"), sb.ToString());
        }
        catch
        {
            // Logging darf niemals selbst zum Absturz fuehren.
        }
    }
}
