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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? klasse, string? fenstername);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr eltern, IntPtr nachKind, string? klasse, string? fenstername);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_COMMAND = 0x0111;
    private const int RefreshBefehl = 28931; // „Aktualisieren" des Explorer-Fensters (F5)

    /// <summary>
    /// Zeichnet den Desktop sofort neu.
    ///
    /// Die Meldung an die Shell allein genuegt nicht verlaesslich — je nach
    /// Windows-Fassung bleibt das ausgeblendete Symbol bis zur naechsten
    /// Anmeldung stehen. Dann sieht es aus, als haette der Schalter nichts
    /// bewirkt. Deshalb wird dem Desktop zusaetzlich ausdruecklich sein
    /// „Aktualisieren" geschickt.
    ///
    /// Der Desktop liegt je nach Fassung unter „Progman" oder unter einem der
    /// „WorkerW"-Fenster — beide Wege werden probiert.
    /// </summary>
    private static void DesktopNeuZeichnen()
    {
        try
        {
            var liste = DesktopListe();
            if (liste != IntPtr.Zero)
                SendMessage(liste, WM_COMMAND, new IntPtr(RefreshBefehl), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopIcons.DesktopNeuZeichnen");
        }
    }

    private static IntPtr DesktopListe()
    {
        var progman = FindWindow("Progman", null);
        var view = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view != IntPtr.Zero)
            return FindWindowEx(view, IntPtr.Zero, "SysListView32", null);

        // Ist ein Hintergrundbild-Dienst aktiv, haengt der Desktop unter einem
        // WorkerW-Fenster statt unter Progman.
        var worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            view = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
                return FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }
        return IntPtr.Zero;
    }

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
            DesktopNeuZeichnen();

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
