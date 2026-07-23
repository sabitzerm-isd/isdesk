namespace ISDesk.Services;

/// Ordnet Dateiendungen zu Sammelkategorien. Eine Tab-Regel (TabConfig.AutoExtensions)
/// darf entweder eine einzelne Endung ("sza") ODER einen Kategorienamen ("bilder")
/// enthalten — eine Kategorie deckt viele Endungen ab.
public static class FileCategories
{
    private static readonly string[] Images =
        { "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "webp", "svg", "ico", "heic" };
    private static readonly string[] OfficeDocs =
        { "docx", "doc", "xlsx", "xls", "pptx", "ppt", "pdf", "odt", "ods", "csv", "rtf" };
    private static readonly string[] Videos =
        { "mp4", "mov", "avi", "mkv", "wmv", "m4v", "webm", "mpg", "mpeg" };
    private static readonly string[] Audio =
        { "mp3", "wav", "flac", "m4a", "aac", "ogg" };
    private static readonly string[] Archives =
        { "zip", "7z", "rar", "tar", "gz" };

    /// Kategoriename → abgedeckte Endungen. Mehrere Namen zeigen bewusst auf
    /// dieselbe Liste, damit z. B. "bilder" und "fotos" beide funktionieren.
    public static readonly IReadOnlyDictionary<string, string[]> Aliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["bilder"] = Images, ["bild"] = Images, ["fotos"] = Images, ["images"] = Images,
            ["office"] = OfficeDocs, ["dokumente"] = OfficeDocs, ["dokument"] = OfficeDocs,
            ["video"] = Videos, ["videos"] = Videos,
            ["audio"] = Audio, ["musik"] = Audio,
            ["archiv"] = Archives, ["archive"] = Archives,
        };

    /// True, wenn die Endung (ohne Punkt, klein) auf eine der Regeln passt —
    /// direkt oder als Teil einer Kategorie.
    public static bool Matches(IEnumerable<string> rules, string ext)
        => MatchesExact(rules, ext) || MatchesCategory(rules, ext);

    /// Nur direkte Endungsgleichheit (z. B. Regel "sza" ↔ Datei .sza).
    public static bool MatchesExact(IEnumerable<string> rules, string ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        foreach (var rule in rules)
            if (string.Equals(rule?.Trim(), ext, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// Nur ueber eine Kategorie (z. B. Regel "office" deckt .docx ab). Bewusst
    /// getrennt von MatchesExact, damit eine explizit zugewiesene Einzelendung
    /// Vorrang vor einer Kategorie hat ("Office = alle Office-Formate AUSSER den
    /// bereits einem anderen Tab fest zugeordneten").
    public static bool MatchesCategory(IEnumerable<string> rules, string ext)
    {
        if (string.IsNullOrEmpty(ext)) return false;
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule)) continue;
            if (Aliases.TryGetValue(rule.Trim(), out var exts)
                && exts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
