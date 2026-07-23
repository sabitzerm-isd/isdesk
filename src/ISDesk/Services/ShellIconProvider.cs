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

        var icon = await Task.Run(() => LoadIcon(path, size)).ConfigureAwait(false);
        _cache[key] = icon;
        return icon;
    }

    private static ImageSource? LoadIcon(string path, int size)
    {
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
}
