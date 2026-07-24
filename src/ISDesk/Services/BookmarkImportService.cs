using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISDesk.Services;

/// Importiert Lesezeichen von Google Chrome UND Mozilla Firefox in den Bereich
/// "Lesezeichen" und gleicht sie ab (neue Lesezeichen kommen hinzu, vorhandene
/// bleiben unangetastet — auch wenn sie im Bereich umbenannt wurden).
/// Browser-Ordner werden zu Tabs, lose Links landen im Tab "Leiste".
public sealed class BookmarkImportService
{
    public const string FenceTitle = "Lesezeichen";
    private const string LooseTab = "Leiste";

    private readonly ConfigService _config;
    private readonly FenceManager _manager;

    public BookmarkImportService(ConfigService config, FenceManager manager)
    {
        _config = config;
        _manager = manager;
    }

    // ===================== Chrome =====================

    public static string ChromeBookmarksPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Google", "Chrome", "User Data", "Default", "Bookmarks");

    public bool ChromeAvailable => File.Exists(ChromeBookmarksPath);

    /// Legt den Bereich an (falls noetig) und gleicht die Chrome-Lesezeichen ab.
    /// Rueckgabe: Anzahl NEU angelegter Links.
    public int SyncChrome()
    {
        if (!ChromeAvailable) return 0;

        ChromeBookmarks? data;
        try
        {
            var json = File.ReadAllText(ChromeBookmarksPath);
            data = JsonSerializer.Deserialize<ChromeBookmarks>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BookmarkImport.Read");
            return 0;
        }
        if (data?.Roots?.BookmarkBar == null) return 0;

        // Chrome-Struktur → Tabname → Links (lose Leisten-Links unter "Leiste").
        var tabs = new List<(string Tab, List<(string Name, string Url)> Links)>();
        var loose = new List<(string, string)>();
        foreach (var child in data.Roots.BookmarkBar.Children ?? new())
        {
            if (child.Type == "url" && IsHttp(child.Url))
                loose.Add((child.Name ?? "", child.Url!));
            else if (child.Type == "folder")
            {
                var links = new List<(string, string)>();
                Collect(child, links);
                if (links.Count > 0) tabs.Add((child.Name ?? "Ordner", links));
            }
        }
        if (loose.Count > 0) tabs.Insert(0, (LooseTab, loose));

        return Import(tabs);
    }

    private static void Collect(ChromeNode node, List<(string, string)> into)
    {
        foreach (var c in node.Children ?? new())
        {
            if (c.Type == "url" && IsHttp(c.Url)) into.Add((c.Name ?? "", c.Url!));
            else if (c.Type == "folder") Collect(c, into);
        }
    }

    // ===================== Firefox =====================

    /// %APPDATA%\Mozilla\Firefox
    public static string FirefoxRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox");

    /// True, wenn ein Firefox-Profil gefunden wurde (die Lesezeichen selbst liegen
    /// in dessen Sicherungen — siehe <see cref="SyncFirefox"/>).
    public bool FirefoxAvailable => FindFirefoxProfile() != null;

    /// Kurzer Hinweis des letzten Firefox-Abgleichs (z. B. "keine Sicherung gefunden"),
    /// null wenn alles normal lief.
    public string? LastFirefoxNote { get; private set; }

    /// Legt den Bereich an (falls noetig) und gleicht die Firefox-Lesezeichen ab.
    /// Quelle ist die NEUESTE Sicherung unter <profil>\bookmarkbackups (mozLz4/JSON) —
    /// places.sqlite ist gesperrt, solange Firefox laeuft, und braeuchte eine
    /// SQLite-Abhaengigkeit. Rueckgabe: Anzahl NEU angelegter Links.
    public int SyncFirefox()
    {
        LastFirefoxNote = null;

        var profile = FindFirefoxProfile();
        if (profile == null)
        {
            LastFirefoxNote = "Kein Firefox-Profil gefunden.";
            return 0;
        }

        var backup = FindNewestBackup(profile);
        if (backup == null)
        {
            LastFirefoxNote = "Keine Firefox-Lesezeichen-Sicherung gefunden (Ordner „bookmarkbackups“). "
                            + "Firefox einmal starten und wieder beenden, dann erneut versuchen.";
            return 0;
        }

        FirefoxNode? root;
        try
        {
            var bytes = File.ReadAllBytes(backup);
            var json = MozLz4.HasMagic(bytes)
                ? Encoding.UTF8.GetString(MozLz4.Decompress(bytes))
                : Encoding.UTF8.GetString(bytes);
            root = JsonSerializer.Deserialize<FirefoxNode>(json.TrimStart(BomChar),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BookmarkImport.Firefox");
            LastFirefoxNote = "Die Firefox-Sicherung konnte nicht gelesen werden.";
            return 0;
        }
        if (root?.Children == null)
        {
            LastFirefoxNote = "Die Firefox-Sicherung enthaelt keine Lesezeichen.";
            return 0;
        }

        // Symbolleiste und Lesezeichen-Menue: Unterordner → Tabs, lose Links → "Leiste".
        var tabs = new List<(string Tab, List<(string Name, string Url)> Links)>();
        var loose = new List<(string, string)>();
        foreach (var section in root.Children)
        {
            if (!IsWantedRoot(section)) continue;
            foreach (var child in section.Children ?? new())
            {
                if (child.TypeCode == 1 && IsHttp(child.Uri))
                    loose.Add((child.Title ?? "", child.Uri!));
                else if (child.TypeCode == 2)
                {
                    var links = new List<(string, string)>();
                    Collect(child, links);
                    if (links.Count > 0) tabs.Add((child.Title ?? "Ordner", links));
                }
            }
        }
        if (loose.Count > 0) tabs.Insert(0, (LooseTab, loose));

        if (tabs.Count == 0) LastFirefoxNote = "In der Firefox-Sicherung stehen keine Lesezeichen.";
        return Import(tabs);
    }

    /// Nur Symbolleiste und Lesezeichen-Menue uebernehmen (Tags/Verlauf ignorieren).
    /// Erkennung ueber die stabilen Kennungen, nicht ueber den uebersetzten Titel.
    private static bool IsWantedRoot(FirefoxNode node)
    {
        if (node.Root is "toolbarFolder" or "bookmarksMenuFolder") return true;
        if (node.Guid is "toolbar_____" or "menu________") return true;
        return node.Root == null && node.Guid == null
               && node.Title is "toolbar" or "menu";
    }

    private static void Collect(FirefoxNode node, List<(string, string)> into)
    {
        foreach (var c in node.Children ?? new())
        {
            if (c.TypeCode == 1 && IsHttp(c.Uri)) into.Add((c.Title ?? "", c.Uri!));
            else if (c.TypeCode == 2) Collect(c, into);
        }
    }

    /// Profilordner des Standardprofils: erst profiles.ini auswerten
    /// ([InstallXXX] Default=… bzw. [ProfileN] Default=1), sonst *.default-release.
    public static string? FindFirefoxProfile()
    {
        try
        {
            var root = FirefoxRoot;
            if (!Directory.Exists(root)) return null;

            var ini = Path.Combine(root, "profiles.ini");
            if (File.Exists(ini))
            {
                var fromIni = ProfileFromIni(root, File.ReadAllLines(ini));
                if (fromIni != null) return fromIni;
            }

            var profiles = Path.Combine(root, "Profiles");
            if (!Directory.Exists(profiles)) return null;

            var dirs = Directory.GetDirectories(profiles);
            return dirs.FirstOrDefault(d => d.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase))
                   ?? dirs.FirstOrDefault(d => d.EndsWith(".default", StringComparison.OrdinalIgnoreCase))
                   ?? dirs.FirstOrDefault(d => Directory.Exists(Path.Combine(d, "bookmarkbackups")));
        }
        catch (Exception)
        {
            return null; // Profil nicht lesbar → Firefox gilt als nicht vorhanden
        }
    }

    /// Minimaler INI-Leser fuer profiles.ini (Abschnitte [Install…] und [Profile…]).
    internal static string? ProfileFromIni(string root, IEnumerable<string> lines)
    {
        string? section = null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? installDefault = null, markedDefault = null, releaseProfile = null, firstProfile = null;

        void FlushSection()
        {
            if (section == null) return;

            if (section.StartsWith("Install", StringComparison.OrdinalIgnoreCase))
            {
                // [InstallXXXX] Default=Profiles/xyz.default-release (Pfad, relativ)
                if (values.TryGetValue("Default", out var path) && path.Length > 0 && path != "1")
                    installDefault ??= Resolve(root, path, !Path.IsPathRooted(path));
            }
            else if (section.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
            {
                if (values.TryGetValue("Path", out var path) && path.Length > 0)
                {
                    var relative = !values.TryGetValue("IsRelative", out var rel) || rel.Trim() != "0";
                    var full = Resolve(root, path, relative);
                    firstProfile ??= full;
                    if (full.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase))
                        releaseProfile ??= full;
                    if (values.TryGetValue("Default", out var def) && def.Trim() == "1")
                        markedDefault ??= full;
                }
            }
            values.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                FlushSection();
                section = line[1..^1].Trim();
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0) values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        FlushSection();

        foreach (var candidate in new[] { installDefault, markedDefault, releaseProfile, firstProfile })
            if (candidate != null && Directory.Exists(candidate)) return candidate;
        return null;
    }

    private static string Resolve(string root, string path, bool relative)
    {
        path = path.Replace('/', Path.DirectorySeparatorChar);
        return relative ? Path.GetFullPath(Path.Combine(root, path)) : path;
    }

    /// Neueste Lesezeichen-Sicherung des Profils (*.jsonlz4, ersatzweise *.json).
    private static string? FindNewestBackup(string profile)
    {
        try
        {
            var dir = Path.Combine(profile, "bookmarkbackups");
            if (!Directory.Exists(dir)) return null;

            return new DirectoryInfo(dir).EnumerateFiles("*.json*")
                .Where(f => f.Length > 0)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// Byte Order Mark am Anfang der entpackten JSON-Datei (stoert den Parser).
    private const char BomChar = (char)0xFEFF;

    // ===================== Gemeinsame Ablage =====================

    /// Schreibt die neuen Links (Dedup ueber URL) und sorgt fuer Bereich und Tabs.
    private int Import(List<(string Tab, List<(string Name, string Url)> Links)> tabs)
    {
        if (tabs.Count == 0) return 0;

        var fenceFolder = Path.Combine(_config.Config.BaseFolder, FenceTitle);
        Directory.CreateDirectory(fenceFolder);

        // Bereits vorhandene URLs im Bereich sammeln (URL-Vergleich, nicht Name).
        var existingUrls = CollectExistingUrls(fenceFolder);

        var added = 0;
        foreach (var (tabName, links) in tabs)
        {
            var newLinks = links.Where(l => !existingUrls.Contains(NormalizeUrl(l.Url))).ToList();
            if (newLinks.Count == 0) continue;

            var tabFolder = EnsureTab(fenceFolder, tabName);
            foreach (var (name, url) in newLinks)
            {
                try
                {
                    WebLinkFactory.CreateUrlFile(tabFolder, url, string.IsNullOrWhiteSpace(name) ? null : name);
                    existingUrls.Add(NormalizeUrl(url));
                    added++;
                }
                catch (Exception ex) { App.LogCrash(ex, "BookmarkImport.Write"); }
            }
        }

        // Bereich sicherstellen bzw. neue Tabs ins offene Fenster spiegeln.
        _manager.EnsureBookmarksFence(fenceFolder, tabs.Select(t => t.Tab).ToList());
        return added;
    }

    private HashSet<string> CollectExistingUrls(string fenceFolder)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(fenceFolder, "*.url", SearchOption.AllDirectories))
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                    {
                        urls.Add(NormalizeUrl(line[4..].Trim()));
                        break;
                    }
                }
            }
        }
        catch (Exception) { /* Ordner evtl. gerade nicht lesbar */ }
        return urls;
    }

    private string EnsureTab(string fenceFolder, string tabName)
    {
        var folder = Path.Combine(fenceFolder, Sanitize(tabName));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static bool IsHttp(string? url)
        => url != null && (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeUrl(string url)
        => url.TrimEnd('/').ToLowerInvariant();

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "Ordner" : name;
    }

    // --- Chrome-JSON ---
    private sealed class ChromeBookmarks { public ChromeRoots? Roots { get; set; } }
    private sealed class ChromeRoots
    {
        [JsonPropertyName("bookmark_bar")] public ChromeNode? BookmarkBar { get; set; }
    }
    private sealed class ChromeNode
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Url { get; set; }
        public List<ChromeNode>? Children { get; set; }
    }

    // --- Firefox-JSON (bookmarkbackups) ---
    private sealed class FirefoxNode
    {
        /// 1 = Lesezeichen, 2 = Ordner
        [JsonPropertyName("typeCode")] public int TypeCode { get; set; }
        public string? Title { get; set; }
        public string? Uri { get; set; }
        public string? Guid { get; set; }
        /// Kennung der Wurzelordner ("toolbarFolder", "bookmarksMenuFolder" …)
        public string? Root { get; set; }
        public List<FirefoxNode>? Children { get; set; }
    }
}
