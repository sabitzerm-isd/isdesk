using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ISDesk.Services;

/// Prueft beim Start das neueste GitHub-Release und laedt bei Bedarf den
/// Installer. Ein Release-Asset heisst "ISDesk-Setup-x.y.z.exe".
public sealed class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/sabitzerm-isd/isdesk/releases/latest";

    public sealed record UpdateInfo(string LatestVersion, string DownloadUrl, long Size, string HtmlUrl);

    public static string CurrentVersion
        => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// Gibt Update-Infos zurueck, wenn online eine neuere Version bereitliegt; sonst null.
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(
                new System.Net.Http.Headers.ProductInfoHeaderValue("ISDesk", CurrentVersion));
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
            var path = Path.Combine(Path.GetTempPath(), $"ISDesk-Setup-{info.LatestVersion}.exe");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.Add(
                    new System.Net.Http.Headers.ProductInfoHeaderValue("ISDesk", CurrentVersion));
                var bytes = await http.GetByteArrayAsync(info.DownloadUrl).ConfigureAwait(false);
                await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
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
