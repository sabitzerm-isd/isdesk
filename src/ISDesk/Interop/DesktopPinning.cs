using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

/// Verankert Bereichs-Fenster auf dem Desktop: Normal liegen sie IMMER ganz unten
/// (WM_WINDOWPOSCHANGING erzwingt HWND_BOTTOM). Bei "Desktop anzeigen"/Win+D hebt
/// Windows das Desktop-Band UEBER alle Fenster — die Bereiche wuerden dahinter
/// verschwinden. Ein WinEvent-Hook (Foreground-Wechsel, kein Polling) erkennt den
/// Desktop-Zustand und schaltet die Bereiche solange auf TOPMOST; verlaesst der
/// Nutzer den Desktop wieder, tauchen sie nach ganz unten ab.
/// (SetParent auf Progman scheitert bei Layered-WPF-Fenstern — gemessen.)
public static class DesktopPinning
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr mod,
        WinEventDelegate proc, uint pid, uint tid, uint flags);
    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
        int objectId, int childId, uint thread, uint time);

    private static readonly List<IntPtr> Attached = new();
    private static bool _desktopMode;
    private static WinEventDelegate? _foregroundProc; // Referenz halten, sonst sammelt der GC den Delegate ein

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)!.AddHook(ZOrderHook);

        Attached.Add(hwnd);
        window.Closed += (_, _) => Attached.Remove(hwnd);
        EnsureForegroundWatcher();
    }

    private static IntPtr ZOrderHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Nur im Normalmodus dauerhaft nach unten zwingen. Im Desktop-Modus wird
        // TOPMOST einmal beim Umschalten gesetzt — wuerde es hier bei jeder
        // Positionsaenderung erneut erzwungen, laegen die Bereiche ueber ihren
        // eigenen Kontextmenues.
        if (msg == WM_WINDOWPOSCHANGING && !_desktopMode)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = HWND_BOTTOM;
            Marshal.StructureToPtr(wp, lParam, false);
        }
        return IntPtr.Zero;
    }

    private static void EnsureForegroundWatcher()
    {
        if (_foregroundProc != null) return;
        _foregroundProc = OnForegroundChanged;
        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private static void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd,
        int objectId, int childId, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;

        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, 64);
        var cls = sb.ToString();

        if (cls is "WorkerW" or "Progman" or "SHELLDLL_DefView")
        {
            SetDesktopMode(true);   // Desktop liegt vorn → Bereiche darueber zeigen
        }
        else
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId) return; // eigener Dialog/Bereich → Zustand halten
            SetDesktopMode(false);  // normale App vorn → Bereiche wieder ganz nach unten
        }
    }

    private static void SetDesktopMode(bool active)
    {
        if (_desktopMode == active) return;
        _desktopMode = active;

        foreach (var hwnd in Attached.ToList())
        {
            if (active)
            {
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }
}
