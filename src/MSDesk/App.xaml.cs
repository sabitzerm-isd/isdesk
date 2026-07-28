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
    private AutoBackupService? _autoBackup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ganz zuerst: ohne Protokoll bleibt jeder fruehe Fehler unsichtbar.
        StartupLog.Enable();
        StartupLog.Write($"OnStartup erreicht (Argumente: {e.Args.Length})");

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
        StartupLog.Step("Ordner der Bereiche", EnsureBaseFolder);
        Interop.GridSnapBehavior.GridSize = _config.Config.GridSize; // Raster
        Interop.GridSnapBehavior.EdgeSnapEnabled = _config.Config.EdgeSnap; // Kanten-Einrasten
        Interop.GridSnapBehavior.GapMillimeters = _config.Config.SnapGapMillimeters; // Zwischenraum
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

        // Erststart-Assistent VOR dem Anlegen des ersten Bereichs: dort laesst
        // sich der Ordner der Bereiche waehlen, und der Willkommen-Bereich soll
        // gleich am richtigen Ort entstehen. Fuer alle spaeteren Starts faellt
        // der Aufruf sofort durch (SetupCompleted).
        StartupLog.Step("Erststart-Assistent", () => Views.SetupDialog.RunOnFirstStart(_config));

        // Ueber StartupLog.Step und damit abgesichert: Scheitert das Anlegen
        // (Ordner nicht erreichbar, keine Rechte), lief bisher der GESAMTE
        // restliche Start nicht mehr — kein Symbol im Infobereich, keine
        // Bereiche, keine Meldung, und wegen OnExplicitShutdown blieb der
        // Prozess unsichtbar stehen und sperrte ueber den Mutex jeden weiteren
        // Versuch. Nach aussen: „MSDesk startet nicht."
        if (_config.Config.Fences.Count == 0)
            StartupLog.Step("Willkommen-Bereich", CreateWelcomeFence);

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

        // Bildschirm-Ueberwachung ZUERST einrichten. Sie stand frueher ganz am
        // Ende — faellt davor ein Schritt aus, reagierte MSDesk anschliessend
        // ueberhaupt nicht mehr auf das An- und Abstecken von Monitoren, ohne
        // dass das nach aussen erkennbar gewesen waere.
        StartupLog.Step("Bildschirm-Ueberwachung", () =>
        {
            _displayDebounce = new System.Timers.Timer(1200) { AutoReset = false };
            _displayDebounce.Elapsed += (_, _) =>
            {
                try
                {
                    Dispatcher.Invoke(() => _manager?.ApplyLayoutsForCurrentDisplays());
                }
                catch (Exception ex)
                {
                    // Ohne diesen Fang bliebe das Speichern nach einem Fehler
                    // dauerhaft gesperrt (SuspendLayoutSaving).
                    LogCrash(ex, "DisplayChange");
                    _manager?.ResumeLayoutSaving();
                }
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _manager.StartDisplayWatch(); // Sicherheitsnetz, falls das Ereignis ausbleibt
        });

        StartupLog.Step("Bereiche oeffnen", () => _manager.OpenAll());
        StartupLog.Step("Anordnung anwenden", () => _manager.ApplyLayoutsForCurrentDisplays());

        // Tabs laden erst beim Anzeigen — das Platz-Gedaechtnis lernt die Ordner
        // deshalb einmalig im Hintergrund (ohne Icons/Ueberwachung).
        StartupLog.Step("Platz-Gedaechtnis", PlacementRegistry.LearnAllTabFolders);
        StartupLog.Step("Ablage", () =>
        {
            if (_config.Config.DesktopSweep) _manager.Sweeper.Start();
        });
        StartupLog.Step("Infobereich-Symbol", () => _tray = new TrayService(_manager, _autostart));

        // Taegliche Sicherung. Der Dienst schaut erst einige Minuten nach dem
        // Start das erste Mal hin — der Start selbst bleibt davon unberuehrt.
        StartupLog.Step("Selbsttaetige Sicherung", () =>
        {
            _autoBackup = new AutoBackupService(_config, _manager.Backup!);
            _autoBackup.Start();
        });

        // Die Anleitung bewusst ZULETZT: sie haelt den Start an, bis sie
        // geschlossen wird — die Bereiche stehen bis dahin schon.
        StartupLog.Step("Anleitung", () => HelpPage.OpenOnFirstRun(_config));

        // Erst wenn die Bereiche stehen: dann ist der doppelte Papierkorb auch
        // wirklich zu sehen, und die Frage kommt im richtigen Augenblick.
        StartupLog.Step("Papierkorb-Doppelung", () =>
            DesktopIcons.OfferHideIfDuplicated(_config.Config, _config.Save));
        StartupLog.Step("Update-Pruefung", () => CheckForUpdatesAsync());

        StartupLog.Write("Start abgeschlossen.");
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
        _autoBackup?.Dispose();
        _manager?.Sweeper?.Dispose();
        _tray?.Dispose();
        _manager?.ShutdownAll();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Stellt sicher, dass der Ordner der Bereiche wirklich benutzbar ist —
    /// und zieht die gespeicherten Pfade mit, falls er sich aendert.
    ///
    /// Der Ort stand frueher fest auf „D:\Fences". Auf einem Rechner ohne
    /// dieses Laufwerk brach schon das Anlegen des ersten Bereichs ab, ohne
    /// Meldung; von aussen sah es aus, als starte MSDesk ueberhaupt nicht.
    /// Genau das ist der Fall bei jedem Kollegen, der nicht dieselbe
    /// Laufwerksaufteilung hat.
    ///
    /// Fuer eine bestehende Installation mit vorhandenem Laufwerk aendert sich
    /// hier nichts: der eingetragene Ordner besteht die Pruefung und bleibt.
    /// </summary>
    private void EnsureBaseFolder()
    {
        var gewuenscht = _config!.Config.BaseFolder;

        // Steht bereits ein Ordner drin, wird er NIE selbsttaetig umgeschrieben.
        // Waere das anders, genuegte ein einziger Start mit gerade nicht
        // erreichbarem Laufwerk (BitLocker noch nicht entsperrt, Netzlaufwerk
        // noch nicht verbunden, Wechselplatte abgezogen), um die Verbindung zu
        // saemtlichen Bereichen dauerhaft zu kappen. Fehlt der Ordner nur
        // voruebergehend, bleiben die Bereiche eben leer — beim naechsten Start
        // ist alles wieder da.
        // Bewusst OHNE Directory.Exists: bei einem Netzpfad, dessen Server noch
        // nicht antwortet (Autostart vor der VPN-Anmeldung), liefe der Aufruf in
        // den Zeitablauf des Netzprotokolls — und bis dahin gaebe es weder
        // Fenster noch Symbol im Infobereich. Fuer eine reine Protokollzeile ist
        // das zu teuer; ob der Ordner da ist, zeigt sich ohnehin beim Oeffnen
        // der Bereiche.
        if (!string.IsNullOrWhiteSpace(gewuenscht)) return;

        // Leer = Erststart. Erst hier wird ein Ort bestimmt und festgehalten.
        var nutzbar = BaseFolderResolver.EnsureUsable(null);
        _config.Config.BaseFolder = nutzbar;
        _config.Save();
        StartupLog.Write($"Ordner der Bereiche festgelegt: {nutzbar}");
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
            // Gleicher Ort wie die Konfiguration (AppData\Local).
            var dir = Services.ConfigService.DefaultFolder;
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
