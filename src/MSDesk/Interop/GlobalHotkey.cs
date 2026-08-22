using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MSDesk.Interop;

/// <summary>
/// Strg+Alt+D blendet die Bereiche aus und wieder ein.
///
/// Der eigentliche Notausgang. Der Doppelklick auf den Desktop setzt voraus,
/// dass man an eine freie Desktop-Flaeche herankommt — im Notfall („die
/// Bereiche liegen ueber allem" oder „die Bereiche sind weg") ist das gerade
/// nicht gegeben. Ein Tastenkuerzel wirkt auch ueber einem Vollbildfenster.
///
/// Bewusst ueber RegisterHotKey und ein reines Nachrichtenfenster: kein
/// systemweiter Hook, kein zusaetzlicher Faden, keine Nachricht in einen
/// fremden Prozess. Rund zwanzig Zeilen fuer den verlaesslichsten Weg.
/// </summary>
public static class GlobalHotkey
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const uint VK_D = 0x44;
    private const int Kennung = 0xD5C;   // beliebig, nur innerhalb des Fensters eindeutig

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    private static HwndSource? _fenster;

    public static void Start()
    {
        if (_fenster != null) return;

        try
        {
            // Reines Nachrichtenfenster: nie sichtbar, kostet nichts, und es
            // ueberlebt das Schliessen einzelner Bereiche — an ein Bereichs-
            // fenster gebunden waere das Kuerzel mit dem Bereich verschwunden.
            _fenster = new HwndSource(new HwndSourceParameters("MSDesk-Hotkey")
            {
                WindowStyle = 0,
                ParentWindow = new IntPtr(-3) // HWND_MESSAGE
            });
            _fenster.AddHook(Behandeln);

            if (!RegisterHotKey(_fenster.Handle, Kennung, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_D))
                Services.StartupLog.Write(
                    "Strg+Alt+D ist bereits von einem anderen Programm belegt — Kuerzel nicht aktiv.");
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "GlobalHotkey.Start");
        }
    }

    public static void Stop()
    {
        if (_fenster == null) return;
        try
        {
            UnregisterHotKey(_fenster.Handle, Kennung);
            _fenster.RemoveHook(Behandeln);
            _fenster.Dispose();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "GlobalHotkey.Stop");
        }
        _fenster = null;
    }

    private static IntPtr Behandeln(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY || wParam.ToInt32() != Kennung) return IntPtr.Zero;

        DesktopPinning.ToggleSichtbarkeit();
        handled = true;
        return IntPtr.Zero;
    }
}
