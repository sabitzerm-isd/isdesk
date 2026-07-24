using System.Diagnostics;
using System.IO;

namespace MSDesk.Services;

/// Oeffnet die mitgelieferte Anleitung (HTML) im Standardbrowser. Sie liegt
/// neben der Anwendung unter Assets\Hilfe\index.html.
public static class HelpPage
{
    public static string Path => System.IO.Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "Hilfe", "index.html");

    public static bool Exists => File.Exists(Path);

    public static void Open()
    {
        try
        {
            if (!Exists) return;
            Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "HelpPage.Open");
        }
    }

    /// Beim allerersten Start einmalig anzeigen.
    public static void OpenOnFirstRun(ConfigService config)
    {
        if (config.Config.HelpShown) return;
        config.Config.HelpShown = true;
        config.SaveDebounced();
        Open();
    }
}
