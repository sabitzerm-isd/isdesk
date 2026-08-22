using System.Runtime.InteropServices;
using System.Windows;

namespace MSDesk.Interop;

/// <summary>
/// Doppelklick auf eine freie Stelle des Desktops blendet alle Bereiche aus und
/// wieder ein — wie beim Vorbild.
///
/// Zweck ist doppelt: eine bequeme Geste, UND ein Notausgang. Sollte die
/// Erkennung des Desktop-Zustands auf einer Windows-Fassung einmal versagen,
/// kommt man damit immer noch an seine Bereiche.
///
/// Umgesetzt ueber einen systemweiten Maus-Hook. Der ist heikel: Er laeuft bei
/// JEDER Mausbewegung mit, und wer darin traedelt, verzoegert die Eingabe des
/// ganzen Systems (Windows haengt zu langsame Hooks kommentarlos aus). Deshalb
/// steigt der Rumpf sofort aus, sobald die Nachricht kein linker Tastendruck
/// ist — alles Weitere passiert hoechstens zweimal pro Sekunde.
/// </summary>
public static class DesktopToggleWatcher
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    /// LVM_GETSELECTEDCOUNT (LVM_FIRST + 50). Liefert eine ZAHL zurueck und
    /// braucht deshalb keinen Zeiger — nur so laesst sich die Desktop-Liste
    /// ueber Prozessgrenzen hinweg befragen, ohne Speicher im Explorer zu
    /// belegen. Genau das waere bei einem Schutzprogramm auf dem Rechner die
    /// falsche Idee.
    private const uint LVM_GETSELECTEDCOUNT = 0x1000 + 50;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT punkt);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr eltern, IntPtr nachKind, string? klasse, string? name);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam,
                                                    uint flags, uint timeout, out IntPtr ergebnis);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint faden, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

    private const uint SMTO_BLOCK = 0x0001, SMTO_ABORTIFHUNG = 0x0002;
    private const uint WM_QUIT = 0x0012;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);

    private const uint GA_ROOT = 2;

    private static IntPtr _hook;
    private static HookProc? _proc;   // Referenz halten, sonst raeumt der GC ihn weg
    private static uint _letzterKlick;
    private static int _letztesX, _letztesY;
    private static bool _ersterKlickAufFreiemDesktop;

    private static Thread? _faden;
    private static uint _fadenId;

    /// <summary>
    /// Der Hook bekommt einen EIGENEN Faden mit eigener Nachrichtenschleife.
    ///
    /// Am Oberflaechen-Faden wuerde jede WPF-Verzoegerung — ein Symbol aus der
    /// Shell holen, ein Ordner auf einem Netzpfad, eine groessere
    /// Speicherbereinigung — unmittelbar zu einer Verzoegerung der Maus im
    /// GANZEN System. Windows haengt zu langsame Hooks ausserdem kommentarlos
    /// aus; danach waere die Geste still verschwunden.
    /// </summary>
    public static void Start()
    {
        if (_faden != null) return;

        _faden = new Thread(() =>
        {
            _fadenId = GetCurrentThreadId();
            _proc = Verarbeiten;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);

            if (_hook == IntPtr.Zero)
            {
                Services.StartupLog.Write(
                    $"Doppelklick-Umschalter nicht eingerichtet (Fehler {Marshal.GetLastWin32Error()}).");
                return;
            }

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        {
            IsBackground = true,
            Name = "MSDesk Desktop-Doppelklick"
        };
        _faden.SetApartmentState(ApartmentState.STA);
        _faden.Start();
    }

    public static void Stop()
    {
        if (_faden == null) return;
        if (_fadenId != 0) PostThreadMessage(_fadenId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _faden = null;
        _fadenId = 0;
        _proc = null;
    }

    private static IntPtr Verarbeiten(int code, IntPtr wParam, IntPtr lParam)
    {
        // Zuerst und ohne Umschweife: alles, was kein linker Tastendruck ist,
        // sofort durchreichen. Das ist der weit ueberwiegende Teil aller
        // Aufrufe (jede Mausbewegung).
        if (code < 0 || wParam.ToInt64() != WM_LBUTTONDOWN)
            return CallNextHookEx(_hook, code, wParam, lParam);

        try
        {
            var daten = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var vorher = _letzterKlick;
            var vorherX = _letztesX;
            var vorherY = _letztesY;

            _letzterKlick = daten.time;
            _letztesX = daten.pt.X;
            _letztesY = daten.pt.Y;

            // Der Auswahlstand wird beim ERSTEN Klick festgehalten. Gesendete
            // Nachrichten ueberholen Eingabenachrichten: fragte man erst beim
            // zweiten Klick, koennte der Explorer die Auswahl des ersten Klicks
            // noch gar nicht verarbeitet haben — oder schon wieder verworfen.
            var freiJetzt = AufFreiemDesktop(daten.pt);
            var freiVorher = _ersterKlickAufFreiemDesktop;
            _ersterKlickAufFreiemDesktop = freiJetzt;

            // Zweiter Klick nah genug an Ort und Zeit? Die Doppelklickzeit
            // stammt aus den Systemeinstellungen, nicht aus einer eigenen Zahl.
            // Der Zeitstempel ist die Laufzeit in Millisekunden und laeuft nach
            // rund 49 Tagen ueber. Die Subtraktion in uint liefert dann trotzdem
            // den richtigen Abstand — nur ein Stand von genau 0 wird verworfen.
            if (vorher == 0 || unchecked(daten.time - vorher) > GetDoubleClickTime())
                return Weiter(code, wParam, lParam);
            if (Math.Abs(daten.pt.X - vorherX) > 6 || Math.Abs(daten.pt.Y - vorherY) > 6)
                return Weiter(code, wParam, lParam);

            // Nach dem Auswerten zuruecksetzen, sonst zaehlt ein Dreifachklick
            // als zweiter Doppelklick.
            _letzterKlick = 0;

            // Beide Klicke muessen auf freier Desktop-Flaeche gelegen haben.
            if (!freiVorher || !freiJetzt) return Weiter(code, wParam, lParam);

            // Umschalten NICHT im Hook selbst: hier laeuft die Eingabe des
            // ganzen Systems durch.
            Application.Current?.Dispatcher.BeginInvoke(DesktopPinning.ToggleSichtbarkeit);
        }
        catch (Exception)
        {
            // Im Hook darf nichts hochkommen — im Zweifel einfach durchreichen.
        }

        return Weiter(code, wParam, lParam);
    }

    private static IntPtr Weiter(int code, IntPtr wParam, IntPtr lParam)
        => CallNextHookEx(_hook, code, wParam, lParam);

    /// <summary>
    /// Liegt der Punkt auf einer FREIEN Stelle des Desktops?
    ///
    /// „Frei" heisst: kein Symbol darunter. Sonst wuerde ein Doppelklick auf ein
    /// Desktop-Symbol das Programm starten UND die Bereiche ausblenden — man
    /// haette den Zusammenhang nie erraten. Erkannt wird das an der Auswahl der
    /// Desktop-Liste: ein Klick auf ein Symbol waehlt es aus, ein Klick ins
    /// Leere hebt jede Auswahl auf.
    /// </summary>
    private static bool AufFreiemDesktop(POINT punkt)
    {
        var unterCursor = WindowFromPoint(punkt);
        if (unterCursor == IntPtr.Zero) return false;

        if (!IstDesktopKlasse(unterCursor) && !IstDesktopKlasse(GetAncestor(unterCursor, GA_ROOT)))
            return false;

        var liste = DesktopListe(unterCursor);
        if (liste == IntPtr.Zero) return true; // keine Liste (Symbole ausgeblendet) → frei

        // Mit Zeitlimit und Abbruch, falls der Explorer haengt. Ein blockierender
        // Aufruf aus einem Maus-Hook heraus wuerde die Maus des ganzen Rechners
        // anhalten, bis der Explorer wieder antwortet — bei einem Netzlaufwerk
        // oder einer Pruefung durch das Schutzprogramm keine Seltenheit.
        if (SendMessageTimeout(liste, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero,
                               SMTO_ABORTIFHUNG | SMTO_BLOCK, 60, out var anzahl) == IntPtr.Zero)
            return false; // keine Antwort → lieber NICHTS tun

        return anzahl.ToInt64() == 0;
    }

    private static bool IstDesktopKlasse(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, 64);
        return sb.ToString() is "WorkerW" or "Progman" or "SHELLDLL_DefView" or "SysListView32";
    }

    /// Die Symbolliste des Desktops — entweder ist der Punkt schon sie selbst,
    /// oder sie liegt unter dem getroffenen Desktop-Fenster.
    private static IntPtr DesktopListe(IntPtr unterCursor)
    {
        var sb = new System.Text.StringBuilder(64);
        GetClassName(unterCursor, sb, 64);
        if (sb.ToString() == "SysListView32") return unterCursor;

        var wurzel = GetAncestor(unterCursor, GA_ROOT);
        var view = FindWindowEx(wurzel, IntPtr.Zero, "SHELLDLL_DefView", null);
        return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
    }
}
