using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ISDesk.Services;

/// Laedt Titel-/Tab-Symbole (PNG). Werte sind entweder Dateinamen aus der
/// mitgelieferten Galerie (Assets\TabIcons) oder absolute Pfade zu eigenen PNGs.
public static class IconLibrary
{
    public static string GalleryFolder
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "TabIcons");

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache
        = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Load(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = Path.IsPathRooted(value) ? value : Path.Combine(GalleryFolder, value);
        return Cache.GetOrAdd(path, static p =>
        {
            try
            {
                if (!File.Exists(p)) return null;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(p);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    /// Alle Galerie-PNGs (ohne interne Dateien wie den Kontaktabzug).
    public static IReadOnlyList<string> GalleryFiles()
    {
        try
        {
            if (!Directory.Exists(GalleryFolder)) return Array.Empty<string>();
            return Directory.GetFiles(GalleryFolder, "*.png")
                .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .Select(Path.GetFileName)
                .ToList()!;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
