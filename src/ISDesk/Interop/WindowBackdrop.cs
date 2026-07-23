using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

/// Hintergrund-Effekte fuer randlose Fenster.
///
/// Bevorzugt wird der DOKUMENTIERTE Win11-SystemBackdrop (DWMWA_SYSTEMBACKDROP_TYPE,
/// ab Build 22621) — die alte Accent-API (SetWindowCompositionAttribute) wird auf
/// aktuellen Windows-11-Builds ignoriert und dient nur noch als Fallback fuer Win10.
/// Die dunkle Toenung samt Deckkraft zeichnet die App selbst (TintLayer im Fenster);
/// dadurch wirkt der Transparenz-Regler unabhaengig vom Windows-Build immer.
public static class WindowBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// opacity wird nur im Legacy-Fallback (Win10) als Tint-Alpha verwendet;
    /// im modernen Pfad kommt die Toenung vom TintLayer des Fensters.
    public static void Apply(Window window, double opacity, bool blur, uint tintRgb = 0x1C1C1E)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        // ENTSCHEIDEND fuer echte Durchsicht: Die WPF-Renderflaeche selbst transparent
        // schalten — ohne das bleibt hinter dem Fensterinhalt eine schwarze Flaeche.
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;

        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        // Accent-Policy als Durchlass zum Desktop. Der moderne Win11-SystemBackdrop
        // (DWMWA_SYSTEMBACKDROP_TYPE) taugt hier NICHT: Er deaktiviert sich bei
        // inaktiven Fenstern und rendert dann eine solide Flaeche — unsere Bereiche
        // sind als Desktop-Fenster aber praktisch immer inaktiv.
        // blur=an  -> BLURBEHIND (3): Desktop dahinter weichgezeichnet
        // blur=aus -> TRANSPARENTGRADIENT (2) mit minimalem Alpha: glasklare Durchsicht
        // Die dunkle Toenung zeichnet die App selbst (TintLayer, an den Regler gebunden).
        var accent = new AccentPolicy
        {
            AccentState = blur ? ACCENT_ENABLE_BLURBEHIND : ACCENT_ENABLE_TRANSPARENTGRADIENT,
            AccentFlags = 2,
            GradientColor = 0x01000000 // AABBGGRR: fast unsichtbarer Grundton
        };
        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}
