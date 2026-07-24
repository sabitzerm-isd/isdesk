using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ISDesk.Interop;

/// Richtet Bereiche beim Verschieben UND beim Groessenziehen live aus
/// (WM_MOVING/WM_SIZING):
///   1. Kanten-Einrasten (Vorrang): Kommt eine Kante naeher als ~12 px an die
///      Kante eines ANDEREN Bereichs, springt sie exakt darauf — Bereiche lassen
///      sich so lueckenlos aneinanderreihen und buendig ausrichten.
///   2. Sonst Raster: die Kante rastet auf ein Vielfaches der Rastergroesse ein.
/// <see cref="GridSize"/> = 0 schaltet beides ab.
/// Alle Rechnungen laufen in BILDSCHIRM-Pixeln (so kommen die Nachrichten an);
/// die eingestellten Groessen sind DIP und werden mit der Fenster-DPI skaliert.
public static class GridSnapBehavior
{
    /// Ein Fenster-Rechteck in Bildschirm-Pixeln (links, oben, rechts, unten).
    public readonly record struct Box(int L, int T, int R, int B)
    {
        public int Width => R - L;
        public int Height => B - T;
    }

    /// Rastergroesse in DIP (0 = ausgeschaltet). Wird aus der Konfiguration gesetzt.
    public static int GridSize { get; set; } = 20;

    /// Kanten-Einrasten an anderen Bereichen (separat abschaltbar — mit vielen
    /// Bereichen empfinden manche das Fangen als "klebrig").
    public static bool EdgeSnapEnabled { get; set; } = true;

    /// Fangbereich fuer das Kanten-Einrasten in DIP. Bewusst klein: bei vielen
    /// Bereichen gibt es sonst fast ueberall einen Fangpunkt.
    public const int EdgeSnapDip = 8;

    /// Wie weit zwei Bereiche quer zur Snap-Richtung auseinanderliegen duerfen,
    /// damit ihre Kanten noch als "benachbart" gelten. Klein halten: sonst zaehlt
    /// bei vielen Bereichen praktisch jeder als Nachbar und liefert Fangpunkte.
    public const int NeighbourReachDip = 8;

    private const int MinWidthDip = 180, MinHeightDip = 120;

    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;

    // Kanten-Codes von WM_SIZING (wParam)
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
        WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// Alle angemeldeten Bereichs-Fenster — Quelle der Kanten zum Einrasten.
    private static readonly List<IntPtr> Registered = new();
    private static readonly object Sync = new();

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd)!.AddHook(Hook);

        lock (Sync)
        {
            if (!Registered.Contains(hwnd)) Registered.Add(hwnd);
        }
        window.Closed += (_, _) => { lock (Sync) Registered.Remove(hwnd); };
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOVING && msg != WM_SIZING) return IntPtr.Zero;

        var grid = GridSize;
        if (grid <= 0) return IntPtr.Zero; // Ausrichten komplett abgeschaltet

        var scale = DpiScale(hwnd);
        var gridPx = Math.Max(1, (int)Math.Round(grid * scale));
        var snapPx = Math.Max(2, (int)Math.Round(EdgeSnapDip * scale));
        var reachPx = (int)Math.Round(NeighbourReachDip * scale);

        var r = Marshal.PtrToStructure<RECT>(lParam);
        var me = new Box(r.L, r.T, r.R, r.B);
        var others = EdgeSnapEnabled ? OtherRects(hwnd) : new List<Box>();

        Box result;
        if (msg == WM_MOVING)
        {
            var (left, top) = ResolveMove(me, others, gridPx, snapPx, reachPx);
            result = new Box(left, top, left + me.Width, top + me.Height);
        }
        else
        {
            result = ResolveResize(me, wParam.ToInt32(), others, gridPx, snapPx, reachPx,
                (int)Math.Round(MinWidthDip * scale), (int)Math.Round(MinHeightDip * scale));
        }

        r.L = result.L; r.T = result.T; r.R = result.R; r.B = result.B;
        Marshal.StructureToPtr(r, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    // ===================== Reine Rechnung (ohne Fenster, daher testbar) =====================

    /// Neue linke obere Ecke beim Verschieben: erst an fremden Kanten einrasten,
    /// sonst auf das Raster. Groesse bleibt unveraendert.
    public static (int Left, int Top) ResolveMove(Box me, IReadOnlyList<Box> others,
        int gridPx, int snapPx, int reachPx)
    {
        var left = SnapEdge(me.L, MoveCandidatesX(others, me, reachPx), snapPx)
                   ?? SnapToGrid(me.L, gridPx);
        var top = SnapEdge(me.T, MoveCandidatesY(others, me, reachPx), snapPx)
                  ?? SnapToGrid(me.T, gridPx);
        return (left, top);
    }

    /// Neues Rechteck beim Ziehen an einer Kante/Ecke (edge = WMSZ_*).
    public static Box ResolveResize(Box me, int edge, IReadOnlyList<Box> others,
        int gridPx, int snapPx, int reachPx, int minWidth, int minHeight)
    {
        int l = me.L, t = me.T, r = me.R, b = me.B;

        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
            l = SnapEdge(l, VerticalEdges(others, me, reachPx), snapPx) ?? SnapToGrid(l, gridPx);
        if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT)
            r = SnapEdge(r, VerticalEdges(others, me, reachPx), snapPx) ?? SnapToGrid(r, gridPx);
        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
            t = SnapEdge(t, HorizontalEdges(others, me, reachPx), snapPx) ?? SnapToGrid(t, gridPx);
        if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT)
            b = SnapEdge(b, HorizontalEdges(others, me, reachPx), snapPx) ?? SnapToGrid(b, gridPx);

        // Mindestgroesse grob wahren (WPF-MinWidth/-Height greifen zusaetzlich).
        if (r - l < minWidth)
        {
            if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) l = r - minWidth;
            else r = l + minWidth;
        }
        if (b - t < minHeight)
        {
            if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) t = b - minHeight;
            else b = t + minHeight;
        }
        return new Box(l, t, r, b);
    }

    /// Naechstliegender Kandidat innerhalb des Fangbereichs (null = keiner).
    public static int? SnapEdge(int value, IEnumerable<int> candidates, int snap)
    {
        int? best = null;
        var bestDistance = snap + 1;
        foreach (var candidate in candidates)
        {
            var distance = Math.Abs(candidate - value);
            if (distance > snap || distance >= bestDistance) continue;
            bestDistance = distance;
            best = candidate;
        }
        return best;
    }

    public static int SnapToGrid(int value, int grid)
        => grid <= 0 ? value : (int)Math.Round(value / (double)grid) * grid;

    /// Neue linke Kante beim Verschieben: an fremde Kante andocken (links/rechts
    /// daneben) oder buendig ausrichten (linke an linke, rechte an rechte).
    private static IEnumerable<int> MoveCandidatesX(IReadOnlyList<Box> others, Box me, int reach)
    {
        foreach (var o in others)
        {
            if (!NearVertically(me, o, reach)) continue;
            yield return o.R;             // mein linker Rand an seinen rechten
            yield return o.L - me.Width;  // mein rechter Rand an seinen linken
            yield return o.L;             // linksbuendig
            yield return o.R - me.Width;  // rechtsbuendig
        }
    }

    /// Neue obere Kante beim Verschieben (analog zu <see cref="MoveCandidatesX"/>).
    private static IEnumerable<int> MoveCandidatesY(IReadOnlyList<Box> others, Box me, int reach)
    {
        foreach (var o in others)
        {
            if (!NearHorizontally(me, o, reach)) continue;
            yield return o.B;              // meine Oberkante an seine Unterkante
            yield return o.T - me.Height;  // meine Unterkante an seine Oberkante
            yield return o.T;              // oben buendig
            yield return o.B - me.Height;  // unten buendig
        }
    }

    /// Senkrechte Kanten (links/rechts) benachbarter Bereiche — fuers Groessenziehen.
    private static IEnumerable<int> VerticalEdges(IReadOnlyList<Box> others, Box me, int reach)
    {
        foreach (var o in others)
        {
            if (!NearVertically(me, o, reach)) continue;
            yield return o.L;
            yield return o.R;
        }
    }

    /// Waagerechte Kanten (oben/unten) benachbarter Bereiche — fuers Groessenziehen.
    private static IEnumerable<int> HorizontalEdges(IReadOnlyList<Box> others, Box me, int reach)
    {
        foreach (var o in others)
        {
            if (!NearHorizontally(me, o, reach)) continue;
            yield return o.T;
            yield return o.B;
        }
    }

    private static bool NearVertically(Box a, Box b, int reach)
        => a.T <= b.B + reach && b.T <= a.B + reach;

    private static bool NearHorizontally(Box a, Box b, int reach)
        => a.L <= b.R + reach && b.L <= a.R + reach;

    // ===================== Fenster-Zugriff =====================

    private static List<Box> OtherRects(IntPtr self)
    {
        List<IntPtr> handles;
        lock (Sync) handles = new List<IntPtr>(Registered);

        var boxes = new List<Box>(handles.Count);
        foreach (var hwnd in handles)
        {
            if (hwnd == self) continue;
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;
            if (GetWindowRect(hwnd, out var rect))
                boxes.Add(new Box(rect.L, rect.T, rect.R, rect.B));
        }
        return boxes;
    }

    private static double DpiScale(IntPtr hwnd)
    {
        try
        {
            var dpi = GetDpiForWindow(hwnd);
            return dpi > 0 ? dpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0; // aeltere Windows-Versionen: ohne Skalierung weiter
        }
    }
}
