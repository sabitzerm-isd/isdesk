using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MSDesk.Services;

/// <summary>
/// Blendet Windows-eigene Desktop-Symbole aus oder ein — hier: den Papierkorb.
///
/// Hintergrund: Der Papierkorb auf dem Desktop ist keine Datei, sondern ein
/// Systemobjekt. Er laesst sich weder verschieben noch loeschen. Liegt er
/// zusaetzlich in einem Bereich, sieht man ihn doppelt. Windows bietet dafuer
/// eine Einstellung, die MSDesk hier direkt setzt (dieselbe, die unter
/// „Desktopsymboleinstellungen" steht).
/// </summary>
public static class DesktopIcons
{
    /// Kennung des Papierkorbs im System.
    private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";

    /// Windows fuehrt zwei Listen — beide muessen gesetzt werden, sonst taucht
    /// das Symbol je nach Startmenue-Fassung wieder auf.
    private static readonly string[] Keys =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu",
    };

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

    /// Ist der Papierkorb auf dem Desktop derzeit ausgeblendet?
    public static bool IsRecycleBinHidden()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Keys[0]);
            return key?.GetValue(RecycleBinClsid) is int wert && wert == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Blendet den Papierkorb auf dem Desktop aus oder wieder ein und teilt der
    /// Oberflaeche die Aenderung mit, damit sie sofort sichtbar wird.
    /// Rueckgabe: true, wenn es geklappt hat.
    /// </summary>
    public static bool SetRecycleBinHidden(bool ausblenden)
    {
        try
        {
            foreach (var pfad in Keys)
            {
                using var key = Registry.CurrentUser.CreateSubKey(pfad);
                key?.SetValue(RecycleBinClsid, ausblenden ? 1 : 0, RegistryValueKind.DWord);
            }

            // Ohne diese Meldung zeichnet der Explorer den Desktop erst beim
            // naechsten Anmelden neu.
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

            StartupLog.Write($"Papierkorb auf dem Desktop {(ausblenden ? "ausgeblendet" : "eingeblendet")}.");
            return true;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopIcons.SetRecycleBinHidden");
            return false;
        }
    }
}
