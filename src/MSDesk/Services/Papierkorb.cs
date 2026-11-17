using System.IO;
using System.Runtime.InteropServices;

namespace MSDesk.Services;

/// <summary>
/// Der EINE Weg, mit dem MSDesk etwas entfernt: in den Papierkorb, still.
///
/// Frueher stand an sieben Stellen FileSystem.DeleteFile(..., OnlyErrorDialogs,
/// SendToRecycleBin). Aus der Automatik (Zeitgeber, ohne Zutun des Anwenders)
/// konnte das bei einer gesperrten Verknuepfung ein Shell-Fenster „Datei wird
/// verwendet" ohne erkennbaren Anlass aufmachen. Hier gibt es keine
/// Fehlerfenster — ein Fehler kommt als IOException zurueck, die der Aufrufer
/// wie bisher auffaengt.
///
/// Einzige Ausnahme, gewollt: Hat das Laufwerk KEINEN Papierkorb (Netz-,
/// Wechsellaufwerk), fragt Windows nach, statt still endgueltig zu loeschen.
/// Lieber ein seltenes Fenster als ein Datenverlust.
/// </summary>
public static class Papierkorb
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// Legt Datei ODER Ordner in den Papierkorb. Wirft bei Misserfolg.
    public static void Verschieben(string pfad)
    {
        var voll = Path.GetFullPath(pfad);
        if (!File.Exists(voll) && !Directory.Exists(voll))
            throw new FileNotFoundException("Nicht vorhanden", voll);

        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = voll + "\0",   // doppelt nullterminiert (die zweite Null haengt der Marshaller an)
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING)
        };
        var rc = SHFileOperation(ref op);
        if (rc != 0 || op.fAnyOperationsAborted)
            throw new IOException($"Papierkorb fehlgeschlagen (0x{rc:X}): {voll}");
        if (File.Exists(voll) || Directory.Exists(voll))
            throw new IOException($"Nach dem Papierkorb noch vorhanden: {voll}");
    }
}
