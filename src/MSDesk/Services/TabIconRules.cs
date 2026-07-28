using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Ordnet Tabs automatisch ein passendes Symbol zu — anhand des Tab-Namens.
/// Damit muss nach einem Lesezeichen-Abgleich oder bei neuen Tabs nichts
/// mehr von Hand gesetzt werden.
///
/// Grundsatz: eine EINMAL von Hand gesetzte Zuordnung wird nie ueberschrieben.
/// Die Automatik greift ausschliesslich dort, wo noch kein Symbol steht.
/// </summary>
public static class TabIconRules
{
    /// Reihenfolge ist bedeutsam: die erste passende Regel gewinnt, deshalb
    /// stehen spezielle Begriffe vor allgemeinen ("lesezeichenleiste" vor "lesezeichen").
    private static readonly (string Icon, string[] Keywords)[] Rules =
    {
        // --- Haeufige Sammelnamen ---
        // Stehen bewusst weit oben: „Allgemein" ist der Standardname jedes
        // ersten Tabs und blieb bisher ohne Symbol.
        ("allgemein",        new[] { "allgemein", "alles", "sonstiges", "diverse", "misch" }),
        ("rakete",           new[] { "launcher", "starter", "schnellstart" }),
        ("support",          new[] { "support", "hilfe", "service", "hotline", "unterstuetzung" }),
        ("bug",              new[] { "trac", "ticket", "bug", "fehler", "problem", "stoerung", "störung" }),
        ("lesezeichenleiste", new[] { "lesezeichenleiste", "lesezeichen-symbolleiste", "bookmarks bar",
                                      "leiste", "symbolleiste" }),

        // --- Arbeit / Organisation ---
        ("verwaltung",       new[] { "verwaltung", "organisation", "buero", "büro" }),
        ("administration",   new[] { "administration", "admin", "it-admin", "systemverwaltung" }),
        ("firma",            new[] { "firma", "firmen", "unternehmen", "betrieb", "konzern" }),
        ("kunde",            new[] { "kunde", "kunden", "auftraggeber", "mandant" }),
        ("euro",             new[] { "rechnung", "buchhaltung", "finanzen", "kosten", "preis", "angebot", "einkauf", "lohn" }),
        ("kalender",         new[] { "kalender", "termin", "planung", "urlaub" }),
        ("zeit",             new[] { "zeit", "stunden", "erfassung", "uhr" }),

        // --- Dateien / Dokumente ---
        ("pdf",              new[] { "pdf" }),
        ("tabelle",          new[] { "tabelle", "excel", "kalkulation", "liste" }),
        ("praesentation",    new[] { "praesentation", "präsentation", "powerpoint", "vortrag", "folien" }),
        ("text",             new[] { "text", "word", "notiz", "notizen", "schreiben" }),
        ("office",           new[] { "office", "microsoft 365", "m365" }),
        ("archiv",           new[] { "archiv", "alt", "ablage alt", "historie", "alte" }),
        ("zip",              new[] { "zip", "archive", "packer", "komprimiert" }),
        ("dokumente",        new[] { "dokument", "unterlagen", "papiere", "formular" }),
        ("ordner",           new[] { "ordner", "verzeichnis", "sammlung" }),

        // --- Technik / Entwicklung ---
        ("terminal",         new[] { "terminal", "konsole", "powershell", "eingabeaufforderung", "shell", "cmd" }),
        ("code",             new[] { "code", "programmieren", "entwicklung", "quelltext", "git", "repository" }),
        ("skript",           new[] { "skript", "script", "makro", "automatisierung", "batch" }),
        ("datenbank",        new[] { "datenbank", "database", "sql", "daten" }),
        ("netzwerk",         new[] { "netzwerk", "server", "lan", "vpn", "domaene", "domäne" }),
        // Mehrzahl mit Umlaut eigens auffuehren: „Passwörter" wird zu
        // „passwoerter" normalisiert und enthaelt „passwort" dann nicht mehr.
        ("sicherheit",       new[] { "sicherheit", "passwort", "passwoerter", "kennwort", "kennwoerter",
                                     "security", "zugang", "zertifikat", "firewall" }),
        ("remote",           new[] { "remote", "fernwartung", "rdp", "teamviewer", "fernzugriff" }),
        ("monitor",          new[] { "monitor", "ueberwachung", "überwachung", "bildschirm", "status", "dashboard" }),
        ("werkzeugkasten",   new[] { "werkzeugkasten", "toolbox", "hilfsmittel" }),
        ("werkzeuge",        new[] { "werkzeug", "tool", "tools", "utility", "dienstprogramm" }),
        ("einstellungen",    new[] { "einstellung", "konfiguration", "optionen", "setup", "system" }),

        // --- Konstruktion (ISD-Umfeld) ---
        ("cad",              new[] { "cad", "konstruktion", "zeichnung", "hicad" }),
        ("dwg",              new[] { "dwg", "autocad" }),
        ("ifc",              new[] { "ifc", "bim" }),
        ("sza",              new[] { "sza", "anlagenbau" }),
        ("drucker3d",        new[] { "3d-druck", "3d druck", "drucker", "druck" }),

        // --- Internet / Kommunikation ---
        ("web",              new[] { "lesezeichen", "bookmark", "internet", "web", "seiten", "links", "favoriten web" }),
        ("mail",             new[] { "mail", "e-mail", "outlook", "post", "nachricht" }),
        ("chat",             new[] { "chat", "teams", "messenger", "slack", "kommunikation" }),
        ("cloud",            new[] { "cloud", "onedrive", "sharepoint", "dropbox", "online" }),
        ("wiki",             new[] { "wiki", "dokumentation", "handbuch", "anleitung", "wissen" }),
        ("import",           new[] { "import", "importiert", "uebernahme", "übernahme", "eingelesen" }),
        ("download",         new[] { "download", "downloads", "herunterladen" }),
        ("ki",               new[] { "ki", "ai", "kuenstliche", "künstliche", "chatgpt", "claude" }),

        // --- Medien ---
        ("foto",             new[] { "foto", "fotos", "kamera" }),
        ("bilder",           new[] { "bild", "bilder", "grafik", "grafiken" }),
        ("video",            new[] { "video", "film", "youtube" }),
        ("musik",            new[] { "musik", "audio", "sound", "radio" }),
        ("spiel",            new[] { "spiel", "spiele", "game", "gaming" }),

        // --- Personen / Privat ---
        ("personen",         new[] { "person", "personen", "team", "mitarbeiter", "kontakte", "kollegen" }),
        ("privat",           new[] { "privat", "persoenlich", "persönlich" }),
        ("home",             new[] { "home", "start", "startseite", "hauptseite" }),
        ("feuerwehr",        new[] { "feuerwehr", "einsatz", "florian" }),

        // --- Sonstiges ---
        ("stern",            new[] { "favorit", "favoriten", "wichtig", "merkliste" }),
        ("herz",             new[] { "herz", "lieblings", "beliebt" }),
        ("papierkorb",       new[] { "papierkorb", "muell", "müll", "geloescht", "gelöscht" }),
        ("blitz",            new[] { "blitz", "schnell", "sofort", "express" }),
        ("warnung",          new[] { "warnung", "achtung", "wichtig!", "dringend" }),
        ("info",             new[] { "info", "information", "hinweise" }),
        ("frage",            new[] { "frage", "fragen", "faq", "unklar", "offen" }),
        ("glueh",            new[] { "idee", "ideen", "vorschlag", "konzept" }),
        ("apps",             new[] { "app", "apps", "programm", "programme", "anwendung", "software" }),
    };

    /// Schlaegt anhand des Tab-Namens ein Galerie-Symbol vor. null = keine Regel passt.
    public static string? Suggest(string? tabTitle)
    {
        if (string.IsNullOrWhiteSpace(tabTitle)) return null;
        var name = Normalize(tabTitle);
        if (name.Length == 0) return null;

        foreach (var (icon, keywords) in Rules)
        {
            foreach (var keyword in keywords)
            {
                if (name.Contains(Normalize(keyword), StringComparison.Ordinal))
                    return icon + ".png";
            }
        }
        return null;
    }

    /// <summary>
    /// Vergibt Symbole fuer alle Tabs OHNE eigenes Symbol. Bereits gesetzte
    /// Zuordnungen bleiben unangetastet. Liefert die Anzahl der Aenderungen.
    /// </summary>
    public static int ApplyMissing(AppConfig config)
    {
        var changed = 0;
        foreach (var fence in config.Fences)
        {
            // Auch der BEREICH selbst bekommt ein Symbol. Bisher galt die
            // Automatik nur fuer Tabs, weshalb Bereiche wie „Support",
            // „Launcher" oder „Papierkorb" ohne Symbol blieben.
            if (string.IsNullOrWhiteSpace(fence.IconPath))
            {
                var fuerBereich = Suggest(fence.Title);
                if (fuerBereich != null)
                {
                    fence.IconPath = fuerBereich;
                    changed++;
                }
            }

            foreach (var tab in fence.Tabs)
            {
                if (!string.IsNullOrWhiteSpace(tab.IconPath)) continue;

                // Erst der Tab-Name, sonst der Name des Bereichs. So bekommt
                // auch ein Tab „Allgemein" in einem Bereich „Papierkorb" ein
                // sinnvolles Symbol, statt leer zu bleiben.
                var suggestion = Suggest(tab.Title) ?? Suggest(fence.Title);
                if (suggestion == null) continue;
                tab.IconPath = suggestion;
                changed++;
            }
        }
        return changed;
    }

    /// Kleinschreibung + Umlaute vereinheitlichen, damit "Büro" und "Buero" gleich behandelt werden.
    private static string Normalize(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        return text
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal);
    }
}
