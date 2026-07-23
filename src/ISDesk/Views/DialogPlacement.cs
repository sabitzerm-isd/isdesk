using System.Windows;

namespace ISDesk.Views;

/// Sorgt dafuer, dass Dialoge immer vollstaendig im Arbeitsbereich des Monitors
/// liegen — auch wenn der Bereich, auf dem sie zentriert werden, am Bildschirmrand steht.
public static class DialogPlacement
{
    public static void ClampToWorkArea(Window window)
    {
        double dpi = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var wa = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point((int)(window.Left * dpi), (int)(window.Top * dpi))).WorkingArea;

        double left = wa.Left / dpi, top = wa.Top / dpi;
        double right = wa.Right / dpi, bottom = wa.Bottom / dpi;

        window.Left = Math.Max(left, Math.Min(window.Left, right - window.ActualWidth));
        window.Top = Math.Max(top, Math.Min(window.Top, bottom - window.ActualHeight));
    }
}
