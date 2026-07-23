using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace ISDesk.Services;

/// Verarbeitet URL-Drops aus Browsern (Chrome/Edge/Firefox): erzeugt eine
/// .url-Internetverknuepfung im Zielordner und laedt im Hintergrund das Favicon
/// der Seite, damit das Icon der Webseite angezeigt wird.
public static class WebLinkFactory
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// Liest eine URL (und optional den vom Browser mitgelieferten Titel) aus dem DataObject.
    public static bool TryGetUrl(IDataObject data, out string url, out string? suggestedName)
    {
        url = "";
        suggestedName = null;

        url = ReadStringFormat(data, "UniformResourceLocatorW", Encoding.Unicode)
              ?? ReadStringFormat(data, "UniformResourceLocator", Encoding.Default)
              ?? "";

        if (url.Length == 0
            && data.GetDataPresent(DataFormats.UnicodeText)
            && data.GetData(DataFormats.UnicodeText) is string text)
        {
            var trimmed = text.Trim();
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = trimmed;
        }

        if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out _))
            return false;

        suggestedName = ReadFileGroupDescriptorName(data);
        return true;
    }

    /// Legt die .url-Datei an (Rueckgabe: Pfad) und ergaenzt das Favicon asynchron.
    public static string CreateUrlFile(string folder, string url, string? suggestedName)
    {
        var uri = new Uri(url);
        var name = SanitizeFileName(
            !string.IsNullOrWhiteSpace(suggestedName) ? suggestedName! : uri.Host);

        var path = Path.Combine(folder, name + ".url");
        var n = 2;
        while (File.Exists(path))
            path = Path.Combine(folder, $"{name} ({n++}).url");

        // ANSI ist das uebliche .url-Format der Shell.
        File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n", Encoding.Default);

        EnsureFavicon(path);
        return path;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> FaviconAttempted
        = new(StringComparer.OrdinalIgnoreCase);

    /// Traegt bei einer .url-Datei ohne IconFile das Favicon nach (asynchron,
    /// einmal je Datei und Sitzung). Heilt auch Links, deren Download frueher
    /// abgebrochen wurde (z. B. App-Neustart waehrend des Ladens).
    public static void EnsureFavicon(string urlFilePath)
    {
        if (!urlFilePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return;
        if (!FaviconAttempted.TryAdd(urlFilePath, true)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (!File.Exists(urlFilePath)) return;
                var lines = await File.ReadAllLinesAsync(urlFilePath).ConfigureAwait(false);

                // Schon versorgt? Nur wenn die eingetragene Icon-Datei auch existiert —
                // sonst neu besorgen (z. B. geleerter Cache oder anderer Rechner).
                var iconLine = lines.FirstOrDefault(l => l.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase));
                if (iconLine != null && File.Exists(iconLine["IconFile=".Length..].Trim())) return;

                var urlLine = lines.FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase));
                if (urlLine == null) return;
                var url = urlLine[4..].Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

                var icoPath = await DownloadFaviconAsync(uri).ConfigureAwait(false);
                if (icoPath == null || !File.Exists(urlFilePath)) return;

                File.WriteAllText(urlFilePath,
                    $"[InternetShortcut]\r\nURL={url}\r\nIconFile={icoPath}\r\nIconIndex=0\r\n",
                    Encoding.Default);
                ShellIconProvider.Instance.Invalidate(urlFilePath);
                NotifyShellItemChanged(urlFilePath);
                // Das Neuschreiben loest den FileSystemWatcher aus → Ansicht laedt das Icon frisch.
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "WebLinkFactory.Favicon");
            }
        });
    }

    /// Laedt das Favicon als .ico in den lokalen Cache (%APPDATA%\ISDesk\FavIcons).
    /// Sucht wie der Browser: zuerst die im HTML deklarierten Icons, dann
    /// /favicon.ico, zuletzt der Favicon-Dienst.
    private static async Task<string?> DownloadFaviconAsync(Uri site)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ISDesk", "FavIcons");
        Directory.CreateDirectory(dir);
        var icoPath = Path.Combine(dir, SanitizeFileName(site.Host) + ".ico");
        if (File.Exists(icoPath)) return icoPath;

        // 1. Wie der Browser: <link rel="icon"> aus dem HTML der Startseite
        var declared = await TryDownloadDeclaredIcon(site).ConfigureAwait(false);
        if (declared != null)
        {
            await File.WriteAllBytesAsync(icoPath, declared).ConfigureAwait(false);
            return icoPath;
        }

        // 2. Klassisches /favicon.ico der Seite
        var direct = await TryDownload($"{site.Scheme}://{site.Host}/favicon.ico").ConfigureAwait(false);
        if (direct != null && IsIco(direct))
        {
            await File.WriteAllBytesAsync(icoPath, direct).ConfigureAwait(false);
            return icoPath;
        }

        // 3. Favicon-Dienst (liefert PNG) → in einen ICO-Container verpacken
        var png = await TryDownload(
            $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(site.Host)}&sz=64")
            .ConfigureAwait(false);
        if (png == null || png.Length < 8) return null;

        await File.WriteAllBytesAsync(icoPath, WrapPngAsIco(png)).ConfigureAwait(false);
        return icoPath;
    }

    /// Icon-Deklarationen aus dem HTML-Kopf der Seite (wie der Browser-Tab):
    /// bevorzugt PNG mit groesster angegebener Groesse, sonst .ico; SVG wird uebersprungen.
    private static async Task<byte[]?> TryDownloadDeclaredIcon(Uri site)
    {
        var html = await TryDownloadText($"{site.Scheme}://{site.Host}/").ConfigureAwait(false);
        if (html == null) return null;

        var candidates = new List<(Uri Href, int Size, bool IsPng)>();
        var linkMatches = System.Text.RegularExpressions.Regex.Matches(
            html, "<link\\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match link in linkMatches)
        {
            var tag = link.Value;
            var rel = AttrValue(tag, "rel");
            if (rel == null) continue;
            if (!rel.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue;
            if (rel.Contains("mask-icon", StringComparison.OrdinalIgnoreCase)) continue;

            var href = AttrValue(tag, "href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(new Uri($"{site.Scheme}://{site.Host}/"), href, out var abs)) continue;

            var sizeAttr = AttrValue(tag, "sizes");
            var size = 0;
            if (sizeAttr != null)
            {
                var x = sizeAttr.IndexOf('x');
                if (x > 0 && int.TryParse(sizeAttr[..x].Trim(), out var parsed)) size = parsed;
            }
            var isPng = abs.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            candidates.Add((abs, size, isPng));
        }
        if (candidates.Count == 0) return null;

        // Beste Wahl: 32-64 px bevorzugt (Icon-Groesse), sonst die groesste Angabe.
        foreach (var candidate in candidates
                     .OrderByDescending(c => c.Size is >= 32 and <= 64)
                     .ThenByDescending(c => c.Size)
                     .ThenByDescending(c => c.IsPng))
        {
            var bytes = await TryDownload(candidate.Href.ToString()).ConfigureAwait(false);
            if (bytes == null || bytes.Length < 8) continue;
            if (IsIco(bytes)) return bytes;
            if (IsPngBytes(bytes)) return WrapPngAsIco(bytes);
        }
        return null;
    }

    private static string? AttrValue(string tag, string attribute)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            tag, attribute + "\\s*=\\s*[\"']([^\"']*)[\"']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsPngBytes(byte[] data)
        => data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    private static async Task<string?> TryDownloadText(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length > 512 * 1024) Array.Resize(ref bytes, 512 * 1024);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// Der Shell mitteilen, dass sich das Icon der Datei geaendert hat.
    private static void NotifyShellItemChanged(string path)
    {
        const int SHCNE_UPDATEITEM = 0x00002000;
        const uint SHCNF_PATHW = 0x0005;
        var ptr = Marshal.StringToHGlobalUni(path);
        try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, ptr, IntPtr.Zero); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static async Task<byte[]?> TryDownload(string url)
    {
        try
        {
            using var response = await Http.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsIco(byte[] data)
        => data.Length > 4 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0;

    /// ICO-Container mit einem PNG-Eintrag (ab Vista gueltig, wie App-Icons mit PNG-Frames).
    private static byte[] WrapPngAsIco(byte[] png)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
        w.Write((byte)64); w.Write((byte)64); w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write((uint)png.Length); w.Write((uint)22);
        w.Write(png);
        w.Flush();
        return ms.ToArray();
    }

    /// Dateiname aus dem FileGroupDescriptorW des Browsers (= Seitentitel).
    private static string? ReadFileGroupDescriptorName(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent("FileGroupDescriptorW")) return null;
            if (data.GetData("FileGroupDescriptorW") is not MemoryStream ms) return null;

            var bytes = ms.ToArray();
            // FILEGROUPDESCRIPTORW: 4 Byte Anzahl, dann FILEDESCRIPTORW; der Dateiname
            // (260 WCHAR) liegt am Ende der 592-Byte-Struktur.
            const int structSize = 592, nameChars = 260;
            if (bytes.Length < 4 + structSize) return null;
            var name = Encoding.Unicode.GetString(
                bytes, 4 + structSize - nameChars * 2, nameChars * 2);
            var zero = name.IndexOf('\0');
            if (zero >= 0) name = name[..zero];
            if (name.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadStringFormat(IDataObject data, string format, Encoding encoding)
    {
        try
        {
            if (!data.GetDataPresent(format)) return null;
            if (data.GetData(format) is not MemoryStream ms) return null;
            var text = encoding.GetString(ms.ToArray());
            var zero = text.IndexOf('\0');
            if (zero >= 0) text = text[..zero];
            text = text.Trim();
            return text.Length > 0 ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "Link" : (name.Length > 80 ? name[..80] : name);
    }
}
