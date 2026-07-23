using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ISDesk.Models;
using ISDesk.ViewModels;
using ISDesk.Views;

namespace ISDesk;

public partial class App : Application
{
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

        // Mini-Test (wird in Task 7/8 durch FenceManager/Tray ersetzt): ein Bereich mit zwei Demo-Tabs.
        var demo = new FenceConfig { Title = "Testbereich", X = 200, Y = 200, Width = 460, Height = 320, Opacity = 0.75, Blur = true };
        demo.Tabs.Add(new TabConfig { Title = "Desktop", FolderPath = @"C:\Users\Public\Desktop", IconSize = 32 });
        demo.Tabs.Add(new TabConfig { Title = "Oeffentlich", FolderPath = @"C:\Users\Public", IconSize = 32 });
        new FenceWindow(new FenceViewModel(demo, @"D:\Fences")).Show();
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
