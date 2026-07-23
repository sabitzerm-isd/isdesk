using System.Runtime.InteropServices;

namespace ISDesk.Services;

/// Liest virtuelle Shell-Objekte (Papierkorb, Dieser PC, Systemsteuerung …) aus
/// einem Drag&Drop-DataObject. Solche Objekte haben keinen Dateipfad, sondern
/// einen "::{CLSID}"-Parsing-Namen — im Bereich werden sie als CLSID-Ordner
/// abgelegt ("Name.{CLSID}"), die Windows nativ mit Icon und Funktion versieht.
public static class ShellDropHelper
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHCreateShellItemArrayFromDataObject(
        System.Runtime.InteropServices.ComTypes.IDataObject pdo,
        ref Guid riid,
        out IShellItemArray ppv);

    private const uint SIGDN_NORMALDISPLAY = 0x00000000;
    private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    /// Virtuelle Objekte (Parsing-Name beginnt mit "::{") aus dem DataObject.
    public static List<(string ClsidParsing, string DisplayName)> GetVirtualItems(IDataObject wpfData)
    {
        var result = new List<(string, string)>();
        try
        {
            if (wpfData is not System.Runtime.InteropServices.ComTypes.IDataObject comData)
                return result;

            var riid = typeof(IShellItemArray).GUID;
            SHCreateShellItemArrayFromDataObject(comData, ref riid, out var array);
            try
            {
                array.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    array.GetItemAt(i, out var item);
                    try
                    {
                        var parsing = GetName(item, SIGDN_DESKTOPABSOLUTEPARSING);
                        if (parsing == null || !parsing.StartsWith("::{", StringComparison.Ordinal))
                            continue;
                        var display = GetName(item, SIGDN_NORMALDISPLAY) ?? "Systemobjekt";
                        result.Add((parsing, display));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(item);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(array);
            }
        }
        catch (Exception)
        {
            // kein Shell-Inhalt im DataObject → leer
        }
        return result;
    }

    private static string? GetName(IShellItem item, uint sigdn)
    {
        var ptr = IntPtr.Zero;
        try
        {
            item.GetDisplayName(sigdn, out ptr);
            return Marshal.PtrToStringUni(ptr);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// Legt fuer ein virtuelles Objekt den CLSID-Ordner im Zielordner an.
    /// "::{645FF040-…}" + "Papierkorb" → "<ziel>\Papierkorb.{645FF040-…}"
    public static void CreateClsidFolder(string targetDir, string clsidParsing, string displayName)
    {
        var clsid = clsidParsing[2..]; // "::{GUID}" → "{GUID}"
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c, '_');
        var path = System.IO.Path.Combine(targetDir, $"{displayName}.{clsid}");
        if (!System.IO.Directory.Exists(path))
            System.IO.Directory.CreateDirectory(path);
    }
}
