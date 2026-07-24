using System.IO;

namespace MSDesk.Services;

/// Favoriten im Lesezeichen-Bereich: markierte Einträge werden in den Tab
/// „Favoriten" kopiert, der immer an erster Stelle steht. Die Originale bleiben
/// in ihrem Ordner — so bleibt der Abgleich mit dem Browser unberührt.
public static class FavoriteService
{
    public const string TabTitle = "Favoriten";

    /// Der Favoriten-Tab steht vor allen anderen (SortOrder unter 0).
    public const int SortOrder = -100;

    /// Liegt bereits eine gleichnamige Datei im Favoriten-Ordner?
    public static bool IsFavorite(string favoritesFolder, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(favoritesFolder)) return false;
            return File.Exists(Path.Combine(favoritesFolder, Path.GetFileName(path)));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// Setzt bzw. entfernt die Markierung. Rueckgabe: neuer Zustand.
    public static bool Toggle(string favoritesFolder, string path)
    {
        try
        {
            Directory.CreateDirectory(favoritesFolder);
            var target = Path.Combine(favoritesFolder, Path.GetFileName(path));

            if (File.Exists(target))
            {
                File.Delete(target);   // Favorit entfernen (Original bleibt)
                return false;
            }

            File.Copy(path, target, overwrite: false);
            return true;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "FavoriteService.Toggle");
            return IsFavorite(favoritesFolder, path);
        }
    }
}
