using System.Runtime.InteropServices;

namespace ISDesk.Interop;

/// Schaltet den dunklen Modus fuer klassische Win32-Popup-Menues frei, damit das
/// echte Explorer-Kontextmenue dem dunklen System-Theme folgt. Nutzt die
/// undokumentierten uxtheme-Ordinals (#135/#136/#133), die auch Notepad++,
/// Files & Co. verwenden — auf alten Builds fehlen sie, daher defensiv.
public static class DarkMenuMode
{
    private enum PreferredAppMode { Default = 0, AllowDark = 1, ForceDark = 2, ForceLight = 3 }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(PreferredAppMode mode);
    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);

    /// Einmal beim App-Start aufrufen (vor dem Oeffnen der Fenster).
    public static void EnableForApp()
    {
        try
        {
            SetPreferredAppMode(PreferredAppMode.AllowDark);
            FlushMenuThemes();
        }
        catch (Exception) { /* aeltere Windows-Builds ohne diese Ordinals */ }
    }

    /// Pro Fenster aufrufen (dessen Popup-Menues sollen dunkel sein).
    public static void AllowForWindow(IntPtr hwnd)
    {
        try { AllowDarkModeForWindow(hwnd, true); }
        catch (Exception) { }
    }
}
