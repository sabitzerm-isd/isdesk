using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

/// Rastet Bereiche beim Verschieben UND beim Groessenziehen live auf ein
/// 20-Pixel-Raster ein (WM_MOVING/WM_SIZING) — so lassen sich Bereiche sauber
/// aneinander ausrichten.
public static class GridSnapBehavior
{
    private const int GridSize = 20;
    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;

    // Kanten-Codes von WM_SIZING (wParam)
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
        WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd)!.AddHook(Hook);
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOVING)
        {
            var r = Marshal.PtrToStructure<RECT>(lParam);
            int w = r.R - r.L, h = r.B - r.T;
            r.L = Snap(r.L);
            r.T = Snap(r.T);
            r.R = r.L + w;
            r.B = r.T + h;
            Marshal.StructureToPtr(r, lParam, false);
            handled = true;
            return new IntPtr(1);
        }

        if (msg == WM_SIZING)
        {
            var r = Marshal.PtrToStructure<RECT>(lParam);
            var edge = wParam.ToInt32();

            if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) r.L = Snap(r.L);
            if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT) r.R = Snap(r.R);
            if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.T = Snap(r.T);
            if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT) r.B = Snap(r.B);

            // Mindestgroesse grob wahren (WPF-MinWidth/-Height greifen zusaetzlich).
            if (r.R - r.L < 180)
            {
                if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) r.L = r.R - 180;
                else r.R = r.L + 180;
            }
            if (r.B - r.T < 120)
            {
                if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.T = r.B - 120;
                else r.B = r.T + 120;
            }

            Marshal.StructureToPtr(r, lParam, false);
            handled = true;
            return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    private static int Snap(int value)
        => (int)Math.Round(value / (double)GridSize) * GridSize;
}
