using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace ISDesk.Services;

/// Laedt das aktuelle Windows-Hintergrundbild. Grundlage fuer den Frosted-Glass-
/// Effekt der Bereiche: Da DWM-Blur fuer inaktive Fenster auf aktuellen Win11-Builds
/// nicht mehr verfuegbar ist, zeichnet ISDesk den weichgezeichneten Wallpaper-
/// Ausschnitt hinter jedem Bereich selbst.
public static class WallpaperService
{
    private const int SPI_GETDESKWALLPAPER = 0x0073;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SystemParametersInfo(int action, int size, StringBuilder buffer, int winIni);

    private static BitmapImage? _current;
    private static string? _currentPath;

    public static event Action? Changed;

    /// Aktuelles Hintergrundbild (gecached); null, wenn keines ermittelbar ist.
    public static BitmapImage? Current
    {
        get
        {
            if (_current == null) Reload();
            return _current;
        }
    }

    private static int GetPrimaryScreenWidth()
    {
        try { return System.Windows.Forms.Screen.PrimaryScreen?.Bounds.Width ?? 1920; }
        catch (Exception) { return 1920; }
    }

    /// Neu laden (z. B. nach Wallpaper- oder Monitorwechsel).
    public static void Reload()
    {
        try
        {
            var sb = new StringBuilder(1024);
            SystemParametersInfo(SPI_GETDESKWALLPAPER, sb.Capacity, sb, 0);
            var path = sb.ToString();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _current = null;
                _currentPath = null;
            }
            else if (!string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase) || _current == null)
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                // Nur in Bildschirmbreite dekodieren: das Bild wird ohnehin
                // weichgezeichnet — spart bei 4K-Wallpapern zweistellige MB.
                img.DecodePixelWidth = Math.Max(800, GetPrimaryScreenWidth());
                img.UriSource = new Uri(path);
                img.EndInit();
                img.Freeze();
                _current = img;
                _currentPath = path;
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "WallpaperService.Reload");
            _current = null;
        }
        Changed?.Invoke();
    }
}
