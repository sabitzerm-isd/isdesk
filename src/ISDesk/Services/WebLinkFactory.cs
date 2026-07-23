using System.IO;
using System.Net.Http;
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

        File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n", Encoding.Unicode);

        // Favicon im Hintergrund besorgen und nachtragen (Fehler still ignorieren).
        var urlPath = path;
        _ = Task.Run(async () =>
        {
            try
            {
                var icoPath = await DownloadFaviconAsync(uri).ConfigureAwait(false);
                if (icoPath == null || !File.Exists(urlPath)) return;

                File.WriteAllText(urlPath,
                    $"[InternetShortcut]\r\nURL={url}\r\nIconFile={icoPath}\r\nIconIndex=0\r\n",
                    Encoding.Unicode);
                ShellIconProvider.Instance.Invalidate(urlPath);
                // Das Neuschreiben loest den FileSystemWatcher aus → Ansicht laedt das Icon frisch.
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "WebLinkFactory.Favicon");
            }
        });

        return path;
    }

    /// Laedt das Favicon als .ico in den lokalen Cache (%APPDATA%\ISDesk\FavIcons).
    private static async Task<string?> DownloadFaviconAsync(Uri site)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ISDesk", "FavIcons");
        Directory.CreateDirectory(dir);
        var icoPath = Path.Combine(dir, SanitizeFileName(site.Host) + ".ico");
        if (File.Exists(icoPath)) return icoPath;

        // 1. Versuch: klassisches /favicon.ico der Seite
        var direct = await TryDownload($"{site.Scheme}://{site.Host}/favicon.ico").ConfigureAwait(false);
        if (direct != null && IsIco(direct))
        {
            await File.WriteAllBytesAsync(icoPath, direct).ConfigureAwait(false);
            return icoPath;
        }

        // 2. Versuch: Favicon-Dienst (liefert PNG) → in einen ICO-Container verpacken
        var png = await TryDownload(
            $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(site.Host)}&sz=64")
            .ConfigureAwait(false);
        if (png == null || png.Length < 8) return null;

        await File.WriteAllBytesAsync(icoPath, WrapPngAsIco(png)).ConfigureAwait(false);
        return icoPath;
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
