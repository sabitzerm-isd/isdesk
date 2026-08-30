using System.Runtime.InteropServices;

namespace MSDesk.Interop;

/// <summary>
/// Die drei Handgriffe, die es beim Papierkorb wirklich braucht: oeffnen,
/// leeren, Eigenschaften.
///
/// Warum nicht das Explorer-Menue: Dort haengt sich JEDE installierte
/// Erweiterung ein — Packer, Umbenenner, Dateisperren-Werkzeuge. Beim
/// Papierkorb ergibt davon nichts einen Sinn, und dazwischen stehen Punkte, die
/// echten Schaden anrichten koennen („Loeschen" hat den Papierkorb schon einmal
/// aus einem Bereich entfernt). Ein Menue mit drei Eintraegen ist hier
/// verlaesslicher als eines mit fuenfzehn.
/// </summary>
public static class RecycleBinActions
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? wurzel, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    private const int SW_SHOW = 5;

    /// Leert den Papierkorb — mit der gewohnten Rueckfrage von Windows.
    /// Bewusst OHNE eigene Rueckfrage davor: eine zweite waere nur laestig.
    public static void Leeren(IntPtr besitzer)
    {
        try
        {
            // Ohne Flags: Windows fragt nach, zeigt den Fortschritt und den Ton.
            SHEmptyRecycleBin(besitzer, null, 0);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "RecycleBinActions.Leeren");
        }
    }

    /// Oeffnet den Eigenschaften-Dialog des Papierkorbs (Groesse je Laufwerk).
    public static void Eigenschaften(string pfad, IntPtr besitzer)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST | SEE_MASK_FLAG_NO_UI,
                hwnd = besitzer,
                lpVerb = "properties",
                lpFile = pfad,
                nShow = SW_SHOW
            };
            ShellExecuteEx(ref info);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "RecycleBinActions.Eigenschaften");
        }
    }
}
