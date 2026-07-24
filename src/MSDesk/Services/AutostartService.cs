using Microsoft.Win32;

namespace MSDesk.Services;

/// Autostart ueber HKCU\...\Run. Selbstheilend: zeigt der Eintrag auf eine
/// andere (z. B. verschobene, alte oder neu installierte) EXE, wird er beim
/// naechsten Start auf die laufende EXE korrigiert.
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MSDesk";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
    }

    /// Aktuell eingetragener Befehl (mit Anfuehrungszeichen) oder null.
    public string? CurrentCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) as string;
        }
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(ValueName, $"\"{ExePath}\"");
        RemoveLegacyEntry();
    }

    /// Entfernt den Autostart-Eintrag der Vorgaengerversion ("ISDesk") — sonst
    /// wuerde die alte Anwendung beim Anmelden zusaetzlich starten.
    public static void RemoveLegacyEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue("ISDesk") != null)
                key.DeleteValue("ISDesk", throwOnMissingValue: false);
        }
        catch (Exception) { /* kein Zugriff → beim naechsten Start erneut */ }
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// Korrigiert einen vorhandenen Eintrag, der auf eine andere EXE zeigt
    /// (z. B. alter Build-Pfad nach der Installation nach Program Files).
    public void EnsureCurrentPath()
    {
        var current = CurrentCommand;
        if (current == null) return; // nicht aktiviert → nichts tun

        var expected = $"\"{ExePath}\"";
        if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
            Enable();
    }

    private static string ExePath => Environment.ProcessPath ?? "";
}
