using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISDesk.Services;

/// Importiert Google-Chrome-Lesezeichen in den Bereich "Lesezeichen" und gleicht
/// sie ab (neue Lesezeichen kommen hinzu, vorhandene bleiben unangetastet — auch
/// wenn sie im Bereich umbenannt wurden). Weitere Browser sind spaeter ergaenzbar.
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

        var baseFolder = _config.Config.BaseFolder;
        var fenceFolder = Path.Combine(baseFolder, FenceTitle);
        Directory.CreateDirectory(fenceFolder);

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
        if (tabs.Count == 0) return 0;

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

    private static void Collect(ChromeNode node, List<(string, string)> into)
    {
        foreach (var c in node.Children ?? new())
        {
            if (c.Type == "url" && IsHttp(c.Url)) into.Add((c.Name ?? "", c.Url!));
            else if (c.Type == "folder") Collect(c, into);
        }
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
}
