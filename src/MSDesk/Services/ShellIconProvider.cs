using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MSDesk.Services;

public sealed class ShellIconProvider
{
    public static ShellIconProvider Instance { get; } = new();

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    // Ein dedizierter STA-Thread fuer die Shell-Icon-Extraktion: manche Icon-Handler
    // (z. B. ClickOnce .appref-ms) liefern auf MTA-Threadpool-Threads nur das
    // generische Blatt-Icon oder schlagen fehl.
    private static readonly System.Collections.Concurrent.BlockingCollection<Action> StaQueue = new();

    static ShellIconProvider()
    {
        var thread = new Thread(() =>
        {
            foreach (var work in StaQueue.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception) { /* Einzelfehler duerfen den Worker nicht beenden */ }
            }
        })
        {
            IsBackground = true,
            Name = "MSDesk.ShellIcons"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    private const int SIIGBF_RESIZETOFIT = 0x00, SIIGBF_ICONONLY = 0x04;

    private static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Symbole, deren Aussehen sich bei GLEICHBLEIBENDEM Pfad aendert, duerfen
    /// nicht zwischengespeichert werden.
    ///
    /// Betrifft den Papierkorb: voll und leer sehen unterschiedlich aus, der
    /// Pfad ist beide Male derselbe. Aus dem Zwischenspeicher kam deshalb immer
    /// das Bild vom ersten Anzeigen — der Papierkorb in einem Bereich zeigte
    /// dauerhaft „leer", waehrend der auf dem Desktop „voll" zeigte. Genau der
    /// Widerspruch, der auffaellt.
    ///
    /// Der Aufwand ist zu vernachlaessigen: es geht um ein einzelnes Objekt,
    /// das nur beim Anzeigen seines Reiters und bei einem Wechsel des
    /// Fuellstands neu geholt wird.
    /// </summary>
    private static bool Zustandsabhaengig(string path)
        => path.Contains(RecycleBinMonitor.ClsidMarker, StringComparison.OrdinalIgnoreCase);

    public async Task<ImageSource?> GetIconAsync(string path, int size)
    {
        var zwischenspeichern = !Zustandsabhaengig(path);
        var key = size.ToString() + "|" + path;

        if (zwischenspeichern && _cache.TryGetValue(key, out var cached))
            return cached;

        var icon = await LoadOnStaAsync(path, size).ConfigureAwait(false);
        if (icon == null)
        {
            // Datei war evtl. gerade mitten im Verschieben — einmal kurz spaeter erneut.
            await Task.Delay(600).ConfigureAwait(false);
            icon = await LoadOnStaAsync(path, size).ConfigureAwait(false);
        }

        // Fehlschlaege NICHT cachen, damit der naechste Reload erneut versucht.
        if (icon != null && zwischenspeichern)
        {
            // Obergrenze, damit der Cache ueber lange Laufzeiten nicht unbegrenzt waechst.
            if (_cache.Count > 1500) _cache.Clear();
            _cache[key] = icon;
        }
        return icon;
    }

    private static Task<ImageSource?> LoadOnStaAsync(string path, int size)
    {
        var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        StaQueue.Add(() =>
        {
            try { tcs.TrySetResult(LoadIcon(path, size)); }
            catch (Exception) { tcs.TrySetResult(null); }
        });
        return tcs.Task;
    }

    /// Gecachte Icons eines Pfads verwerfen (z. B. nachdem einer .url-Datei
    /// nachtraeglich ihr Favicon zugewiesen wurde).
    public void Invalidate(string path)
    {
        foreach (var key in _cache.Keys)
        {
            if (key.EndsWith("|" + path, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
        }
    }

    private static ImageSource? LoadIcon(string path, int size)
    {
        // .url mit eingetragenem IconFile: Favicon direkt aus der .ico laden —
        // umgeht saemtliche Shell-Icon-Caches (die gern das alte Icon festhalten).
        if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            var custom = TryLoadUrlIconFile(path, size);
            if (custom != null) return custom;
        }

        if (Zustandsabhaengig(path))
        {
            var papierkorb = PapierkorbSymbol(size);
            if (papierkorb != null) return papierkorb;
            // Sonst weiter auf dem ueblichen Weg — lieber das falsche Symbol
            // als gar keines.
        }

        IntPtr hbm = IntPtr.Zero;
        try
        {
            var riid = IShellItemImageFactoryGuid;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref riid, out var factory);
            var sz = new SIZE { cx = size, cy = size };
            int hr = factory.GetImage(sz, SIIGBF_RESIZETOFIT | SIIGBF_ICONONLY, out hbm);
            Marshal.ReleaseComObject(factory);
            if (hr != 0 || hbm == IntPtr.Zero)
                return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null; // Pfad weg / kein Icon → null
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Holt das Papierkorb-Symbol passend zum Fuellstand — an der Shell vorbei,
    /// direkt aus der Symboldatei.
    ///
    /// Warum nicht der uebliche Weg: Der Papierkorb in einem Bereich ist kein
    /// echtes Papierkorb-Objekt, sondern ein Ordner mit angehaengter Kennung
    /// („Papierkorb.{645FF040-…}"). Fragt man die Shell nach dem Symbol DIESES
    /// Ordners, liefert sie den Standardeintrag der Kennung — und der zeigt in
    /// der Registrierung fest auf „leer" (imageres.dll,-55). Deshalb blieb der
    /// Papierkorb im Bereich dauerhaft leer, waehrend der auf dem Desktop voll
    /// war. Nachladen half nicht: die Auskunft war jedes Mal dieselbe.
    ///
    /// Die Registrierung fuehrt neben dem Standard aber beide Faelle getrennt
    /// („Empty" und „Full"). Genau die werden hier gelesen — damit stimmt das
    /// Symbol auch bei einem anderen Symbolpaket, das der Anwender gesetzt hat.
    /// </summary>
    private static ImageSource? PapierkorbSymbol(int size)
    {
        try
        {
            var voll = RecycleBinMonitor.IsFull();

            using var key = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(
                $@"CLSID\{RecycleBinMonitor.ClsidMarker}\DefaultIcon");
            if (key == null) return null;

            // Faellt der gesuchte Eintrag aus, bleibt der Standard — besser als nichts.
            var eintrag = key.GetValue(voll ? "Full" : "Empty") as string
                          ?? key.GetValue("") as string;
            if (string.IsNullOrWhiteSpace(eintrag)) return null;

            var komma = eintrag.LastIndexOf(',');
            if (komma <= 0) return null;

            var datei = Environment.ExpandEnvironmentVariables(eintrag[..komma].Trim().Trim('"'));
            if (!int.TryParse(eintrag[(komma + 1)..].Trim(), out var index)) return null;

            return AusSymboldatei(datei, index, size);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ImageSource? AusSymboldatei(string datei, int index, int size)
    {
        var gross = IntPtr.Zero;
        var klein = IntPtr.Zero;
        try
        {
            // Beide Groessen in EINEM Wert: unteres Wort gross, oberes klein.
            var groessen = (uint)((size & 0xFFFF) | ((size & 0xFFFF) << 16));
            if (SHDefExtractIcon(datei, index, 0, out gross, out klein, groessen) != 0)
                return null;

            var handle = gross != IntPtr.Zero ? gross : klein;
            if (handle == IntPtr.Zero) return null;

            var bild = Imaging.CreateBitmapSourceFromHIcon(
                handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bild.Freeze();
            return bild;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (gross != IntPtr.Zero) DestroyIcon(gross);
            if (klein != IntPtr.Zero) DestroyIcon(klein);
        }
    }

    private static ImageSource? TryLoadUrlIconFile(string urlPath, int size)
    {
        try
        {
            string? icoFile = null;
            foreach (var line in System.IO.File.ReadAllLines(urlPath))
            {
                if (line.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase))
                {
                    icoFile = line["IconFile=".Length..].Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(icoFile) || !System.IO.File.Exists(icoFile)) return null;
            if (!icoFile.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)) return null;

            using var stream = System.IO.File.OpenRead(icoFile);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            // Frame waehlen: kleinstes Bild, das die Zielgroesse noch abdeckt.
            BitmapFrame? best = null;
            foreach (var frame in decoder.Frames)
            {
                if (best == null) { best = frame; continue; }
                var coversTarget = frame.PixelWidth >= size;
                var bestCovers = best.PixelWidth >= size;
                if ((coversTarget && (!bestCovers || frame.PixelWidth < best.PixelWidth))
                    || (!coversTarget && !bestCovers && frame.PixelWidth > best.PixelWidth))
                    best = frame;
            }
            if (best == null) return null;
            best.Freeze();
            return best;
        }
        catch (Exception)
        {
            return null; // dann normaler Shell-Weg
        }
    }
}
