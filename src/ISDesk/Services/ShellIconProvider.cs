using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ISDesk.Services;

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
            Name = "ISDesk.ShellIcons"
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

    public async Task<ImageSource?> GetIconAsync(string path, int size)
    {
        var key = size.ToString() + "|" + path;
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = await LoadOnStaAsync(path, size).ConfigureAwait(false);
        if (icon == null)
        {
            // Datei war evtl. gerade mitten im Verschieben — einmal kurz spaeter erneut.
            await Task.Delay(600).ConfigureAwait(false);
            icon = await LoadOnStaAsync(path, size).ConfigureAwait(false);
        }

        // Fehlschlaege NICHT cachen, damit der naechste Reload erneut versucht.
        if (icon != null)
            _cache[key] = icon;
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
