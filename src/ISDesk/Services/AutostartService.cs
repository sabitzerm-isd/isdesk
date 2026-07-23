using Microsoft.Win32;

namespace ISDesk.Services;

/// Autostart ueber HKCU\...\Run. Selbstheilend: zeigt der Eintrag auf eine
/// andere (z. B. verschobene, alte oder neu installierte) EXE, wird er beim
/// naechsten Start auf die laufende EXE korrigiert.
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ISDesk";

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
