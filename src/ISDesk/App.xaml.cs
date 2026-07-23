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

        // Task 3 Mini-Test: ein hart erzeugtes FenceWindow (wird in Task 7/8 durch FenceManager/Tray ersetzt).
        var demo = new FenceConfig { Title = "Testbereich", X = 200, Y = 200, Width = 400, Height = 260, Opacity = 0.75, Blur = true };
        new FenceWindow(new FenceViewModel(demo)).Show();
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
