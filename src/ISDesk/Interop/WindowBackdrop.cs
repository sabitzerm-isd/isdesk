using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

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
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    /// opacity 0..1 (Tint-Deckkraft), blur an/aus. tint = Grundfarbe (dunkel).
    public static void Apply(Window window, double opacity, bool blur, uint tintRgb = 0x1C1C1E)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        byte a = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        // GradientColor-Format: 0xAABBGGRR
        uint abgr = ((uint)a << 24)
                  | ((tintRgb & 0x0000FF) << 16)      // R -> BB-Position
                  | (tintRgb & 0x00FF00)              // G bleibt
                  | ((tintRgb & 0xFF0000) >> 16);     // B -> RR-Position
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

        int on = 1; DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND; DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }
}
