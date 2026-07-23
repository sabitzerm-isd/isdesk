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
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_ENABLE_TRANSPARENTGRADIENT = 2;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    /// opacity wird nur im Legacy-Fallback (Win10) als Tint-Alpha verwendet;
    /// im modernen Pfad kommt die Toenung vom TintLayer des Fensters.
    public static void Apply(Window window, double opacity, bool blur, uint tintRgb = 0x1C1C1E)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        // Glasrahmen in die gesamte Clientflaeche ziehen, damit der Backdrop durchscheint.
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);

        int type = blur ? DWMSBT_TRANSIENTWINDOW : DWMSBT_NONE;
        if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0)
            return; // moderner Weg aktiv

        ApplyLegacyAccent(hwnd, opacity, blur, tintRgb);
    }

    private static void ApplyLegacyAccent(IntPtr hwnd, double opacity, bool blur, uint tintRgb)
    {
        byte a = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        // GradientColor-Format: 0xAABBGGRR (Kanaele aus 0xRRGGBB umsortieren)
        uint abgr = ((uint)a << 24)
                  | ((tintRgb & 0x0000FF) << 16)
                  | (tintRgb & 0x00FF00)
                  | ((tintRgb & 0xFF0000) >> 16);
        var accent = new AccentPolicy
        {
            AccentState = blur ? ACCENT_ENABLE_ACRYLICBLURBEHIND : ACCENT_ENABLE_TRANSPARENTGRADIENT,
            AccentFlags = 2,
            GradientColor = abgr
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
