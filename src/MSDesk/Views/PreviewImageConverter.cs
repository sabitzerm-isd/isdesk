using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MSDesk.Views;

/// <summary>
/// Laedt ein Vorschaubild von der Platte und gibt die Datei sofort wieder frei.
///
/// Wichtig: Ohne <see cref="BitmapCacheOption.OnLoad"/> haelt WPF die Datei
/// offen, solange das Bild angezeigt wird — das naechste Auffrischen der
/// Vorschau wuerde dann fehlschlagen.
/// </summary>
public sealed class PreviewImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string pfad || string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
            return null;

        try
        {
            var bild = new BitmapImage();
            bild.BeginInit();
            bild.UriSource = new Uri(pfad);
            bild.CacheOption = BitmapCacheOption.OnLoad;      // Datei sofort freigeben
            bild.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // stets der neueste Stand
            bild.DecodePixelWidth = 264;                       // doppelte Anzeigebreite reicht
            bild.EndInit();
            bild.Freeze();
            return bild;
        }
        catch (Exception)
        {
            return null; // ein fehlendes Vorschaubild darf die Optionen nicht stoeren
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
