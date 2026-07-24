using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MSDesk.Services;

/// Prueft beim Start das neueste GitHub-Release und laedt bei Bedarf den
/// Installer. Ein Release-Asset heisst "MSDesk-Setup-x.y.z.exe".
public sealed class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/sabitzerm-isd/isdesk/releases/latest";

    public sealed record UpdateInfo(string LatestVersion, string DownloadUrl, long Size, string HtmlUrl);

    /// Ein Eintrag der Versionsgeschichte fuer die Anzeige in den Optionen.
    public sealed record ReleaseNote(string Version, DateTime? Published, string Text);

    private const string ReleasesApi = "https://api.github.com/repos/sabitzerm-isd/isdesk/releases?per_page=10";

    /// <summary>
    /// Holt die letzten Versionshinweise von GitHub. Leere Liste, wenn offline
    /// oder nicht erreichbar — die Optionen bleiben dann einfach ohne Verlauf.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseNote>> GetReleaseNotesAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("MSDesk", CurrentVersion));

            var json = await http.GetStringAsync(ReleasesApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var notes = new List<ReleaseNote>();
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
                var body = release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                DateTime? published = release.TryGetProperty("published_at", out var p)
                                      && p.TryGetDateTime(out var when) ? when : null;

                notes.Add(new ReleaseNote(tag.TrimStart('v', 'V'), published, CleanMarkdown(body)));
            }
            return notes;
        }
        catch (Exception)
        {
            return Array.Empty<ReleaseNote>();
        }
    }

    /// Macht aus den Markdown-Hinweisen gut lesbaren Fliesstext (ohne #, ** und `).
    private static string CleanMarkdown(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var result = new List<string>(lines.Length);

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            line = line.TrimStart('#', ' ').Replace("**", "").Replace("`", "");
            if (line.StartsWith("- ", StringComparison.Ordinal)) line = "•  " + line[2..];
            result.Add(line);
        }
        return string.Join("\n", result).Trim();
    }

    public static string CurrentVersion
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// Gibt Update-Infos zurueck, wenn online eine neuere Version bereitliegt; sonst null.
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("MSDesk", CurrentVersion));
            http.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = await http.GetStringAsync(LatestApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "0.0.0";
            if (CompareVersions(tag, CurrentVersion) <= 0) return null;

            // Passendes Setup-Asset suchen (.exe mit "setup" im Namen bevorzugt).
            if (!root.TryGetProperty("assets", out var assets)) return null;
            JsonElement? chosen = null;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)) { chosen = a; break; }
                chosen ??= a;
            }
            if (chosen is not { } asset) return null;

            return new UpdateInfo(
                tag,
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                root.GetProperty("html_url").GetString() ?? "");
        }
        catch (Exception)
        {
            return null; // offline / kein Release / API-Limit → still ignorieren
        }
    }

    /// Laedt den Installer in den Temp-Ordner und startet ihn; gibt den Pfad zurueck
    /// (oder null bei Fehler). Der Aufrufer beendet danach die App.
    public async Task<string?> DownloadAndRunAsync(UpdateInfo info)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"MSDesk-Setup-{info.LatestVersion}.exe");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.Add(
                    new System.Net.Http.Headers.ProductInfoHeaderValue("MSDesk", CurrentVersion));
                var bytes = await http.GetByteArrayAsync(info.DownloadUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            }
            // Stumm durchlaufen lassen: kein Durchklicken der Installationsschritte.
            // Der Installer schliesst das laufende MSDesk selbst und startet es
            // danach ueber seinen [Run]-Eintrag wieder (als normaler Anwender).
            // Die Windows-Rueckfrage zur Rechteerhoehung erscheint weiterhin —
            // die verlangt Windows fuer Schreibzugriff auf "Programme".
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            });
            return path;
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "UpdateService.Download");
            return null;
        }
    }

    /// Vergleicht "x.y.z"-Versionen; >0 wenn a neuer als b.
    private static int CompareVersions(string a, string b)
    {
        var pa = Parse(a);
        var pb = Parse(b);
        for (var i = 0; i < 3; i++)
        {
            if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
        }
        return 0;

        static int[] Parse(string v)
        {
            var parts = v.Split('.', '-', '+');
            var r = new int[3];
            for (var i = 0; i < 3 && i < parts.Length; i++)
                int.TryParse(parts[i], out r[i]);
            return r;
        }
    }
}
