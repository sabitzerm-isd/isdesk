using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MSDesk.Interop;

/// <summary>
/// Zeigt beim Verschieben duenne Linien dort an, wo der Bereich an einem
/// Nachbarn einrastet — so ist vor dem Loslassen sichtbar, wo er landet.
///
/// Bewusst als ein paar SEHR schmale Fenster umgesetzt und nicht als
/// bildschirmfuellende Ebene: eine transparente Vollbild-Ebene braeuchte einen
/// Puffer ueber die gesamte Arbeitsflaeche (zweistellige Megabyte), waehrend
/// zwei Linien praktisch nichts kosten. Die Fenster nehmen keine Mausklicks an
/// und holen sich nie den Fokus.
/// </summary>
public static class AlignmentGuides
{
    /// Staerke der Linie in Bildschirm-Pixeln.
    private const int Thickness = 2;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;  // Klicks gehen hindurch
    private const int WS_EX_NOACTIVATE = 0x08000000;   // holt nie den Fokus
    private const int WS_EX_TOOLWINDOW = 0x00000080;   // nicht in der Taskleiste

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_HIDEWINDOW = 0x0080;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    private static readonly List<Window> Lines = new();

    /// Fensterhandles zu <see cref="Lines"/>. Ein- und Ausblenden laeuft
    /// ausschliesslich ueber Win32: WPF haelt die Fenster fuer verborgen (sie
    /// wurden nie ueber WPF eingeblendet), ein Window.Hide() bliebe deshalb
    /// wirkungslos — genau daran blieben die Linien nach dem Verschieben stehen.
    private static readonly List<IntPtr> Handles = new();

    /// Blendet die Linien fuer die uebergebenen Fangpunkte ein (Bildschirm-Pixel).
    public static void Show(IReadOnlyList<GridSnapBehavior.Guide> guides)
    {
        try
        {
            for (var i = 0; i < guides.Count; i++)
            {
                var guide = guides[i];
                var hwnd = LineAt(i);

                var (x, y, w, h) = guide.Vertical
                    ? (guide.Position - Thickness / 2, guide.From, Thickness, Math.Max(1, guide.To - guide.From))
                    : (guide.From, guide.Position - Thickness / 2, Math.Max(1, guide.To - guide.From), Thickness);

                // Direkt in Bildschirm-Pixeln setzen — so entfaellt jede
                // DPI-Umrechnung, die bei gemischten Monitoren schieflaufen wuerde.
                SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            // Nicht mehr benoetigte Linien verbergen (statt sie zu schliessen —
            // beim naechsten Ziehen sind sie sofort wieder da).
            for (var i = guides.Count; i < Handles.Count; i++) HideHandle(Handles[i]);
        }
        catch (Exception ex)
        {
            MSDesk.App.LogCrash(ex, "AlignmentGuides.Show");
        }
    }

    /// Alle Linien ausblenden (Ende des Verschiebens).
    public static void Hide()
    {
        foreach (var handle in Handles) HideHandle(handle);
    }

    private static void HideHandle(IntPtr hwnd)
        => SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW);

    /// Gibt die Linienfenster frei (beim Beenden).
    public static void Dispose()
    {
        foreach (var line in Lines.ToList()) line.Close();
        Lines.Clear();
        Handles.Clear();
    }

    private static IntPtr LineAt(int index)
    {
        while (Lines.Count <= index)
        {
            var (window, hwnd) = CreateLine();
            Lines.Add(window);
            Handles.Add(hwnd);
        }
        return Handles[index];
    }

    private static (Window Window, IntPtr Handle) CreateLine()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,          // darf den gezogenen Bereich nicht stoeren
            Topmost = true,
            AllowsTransparency = false,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7D, 0xB7, 0xFF)),
            Width = 1,
            Height = 1,
            Left = -10000,                  // ausserhalb, bis Show() sie platziert
            Top = -10000
        };

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        // Einmal ueber WPF anzeigen, damit der Inhalt (die Farbfläche) wirklich
        // gezeichnet wird — danach laeuft Ein- und Ausblenden ausschliesslich
        // ueber SetWindowPos. WPF haelt das Fenster durchgehend fuer sichtbar;
        // genau deshalb funktioniert das native Ausblenden zuverlaessig.
        window.Show();
        HideHandle(hwnd);
        return (window, hwnd);
    }
}
