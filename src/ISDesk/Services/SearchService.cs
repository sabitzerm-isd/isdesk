namespace ISDesk.Services;

/// Globale Live-Suche: Der Suchbegriff gilt app-weit — Tippen in irgendeinem
/// Bereich hebt Treffer in ALLEN Bereichen und Tabs hervor (ohne Enter).
public static class SearchService
{
    private static string _term = "";

    public static string Term => _term;

    public static bool IsActive => _term.Length > 0;

    /// Wird auf dem UI-Thread ausgeloest (Quelle sind TextChanged-Handler).
    public static event Action? TermChanged;

    public static void SetTerm(string term)
    {
        term = term.Trim();
        if (string.Equals(_term, term, StringComparison.Ordinal)) return;
        _term = term;
        TermChanged?.Invoke();
    }

    public static bool Matches(string displayName)
        => IsActive && displayName.Contains(_term, StringComparison.OrdinalIgnoreCase);
}
