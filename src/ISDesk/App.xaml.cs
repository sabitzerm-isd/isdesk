using System.IO;
using System.Text;
using System.Windows;
using ISDesk.Models;
using ISDesk.Services;

namespace ISDesk;

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

        _singleInstanceMutex = new Mutex(true, @"Global\ISDesk_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Bereits eine Instanz aktiv → still beenden.
            Shutdown();
            return;
        }

        _config = new ConfigService();
        _config.Load();
        _manager = new FenceManager(_config);
        _autostart = new AutostartService();

        if (_config.Config.Fences.Count == 0)
            CreateWelcomeFence();

        _manager.OpenAll();

        _tray = new TrayService(_manager, _autostart);
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ISDesk");
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
