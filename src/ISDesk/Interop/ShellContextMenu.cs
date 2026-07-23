using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ISDesk.Interop;

/// Zeigt das ECHTE Windows-Explorer-Kontextmenue fuer eine Datei/einen Ordner
/// (inkl. „Öffnen mit", „Senden an", installierte Erweiterungen wie 7-Zip,
/// „Eigenschaften" …). Die Untermenues funktionieren, weil WM_INITMENUPOPUP &
/// Co. ueber IContextMenu2/3 an die Shell weitergereicht werden (dafuer das
/// versteckte NativeWindow als Message-Sink).
public static class ShellContextMenu
{
    private const int MIN_ID = 1;
    private const int MAX_ID = 0x7FFF;

    private const uint CMF_NORMAL = 0x0000;
    private const uint CMF_EXPLORE = 0x0004;
    private const uint CMF_EXTENDEDVERBS = 0x0100;

    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;

    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_MENUCHAR = 0x0120;

    private static readonly Guid IID_IContextMenu = new("000214e4-0000-0000-c000-000000000046");
    private static readonly Guid IID_IShellFolder = new("000214e6-0000-0000-c000-000000000046");

    [DllImport("shell32.dll")] private static extern int SHGetDesktopFolder(out IShellFolder ppshf);
    [DllImport("shell32.dll")] private static extern void SHBindToParent(IntPtr pidl, ref Guid riid, out IShellFolder ppv, out IntPtr ppidlLast);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint flags, int x, int y, IntPtr hwnd, IntPtr lptpm);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    /// Leitet die Menue-Nachrichten an die Shell weiter — sonst bleiben Untermenues leer.
    private sealed class MenuMessageSink : NativeWindow
    {
        private readonly IContextMenu2 _menu;
        public MenuMessageSink(IContextMenu2 menu) => _menu = menu;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                if (_menu.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam) == 0)
                {
                    m.Result = m.Msg == WM_INITMENUPOPUP ? IntPtr.Zero : new IntPtr(1);
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    /// Zeigt das Kontextmenue an Bildschirmposition (screenX/screenY) fuer die Datei.
    public static void Show(string path, IntPtr ownerHwnd, int screenX, int screenY)
    {
        IntPtr fullPidl = IntPtr.Zero, contextPtr = IntPtr.Zero, hMenu = IntPtr.Zero;
        IShellFolder? parent = null;
        try
        {
            if (SHParseDisplayName(path, IntPtr.Zero, out fullPidl, 0, out _) != 0 || fullPidl == IntPtr.Zero)
                return;

            var iidFolder = IID_IShellFolder;
            SHBindToParent(fullPidl, ref iidFolder, out parent, out var childPidl);
            if (parent == null || childPidl == IntPtr.Zero) return;

            var apidl = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(apidl, childPidl);
            try
            {
                var iidCtx = IID_IContextMenu;
                if (parent.GetUIObjectOf(ownerHwnd, 1, apidl, ref iidCtx, IntPtr.Zero, out contextPtr) != 0
                    || contextPtr == IntPtr.Zero)
                    return;
            }
            finally { Marshal.FreeHGlobal(apidl); }

            var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextPtr);
            var contextMenu2 = contextMenu as IContextMenu2;

            hMenu = CreatePopupMenu();
            if (contextMenu.QueryContextMenu(hMenu, 0, MIN_ID, MAX_ID, CMF_NORMAL | CMF_EXPLORE | CMF_EXTENDEDVERBS) < 0)
                return;

            MenuMessageSink? sink = contextMenu2 != null ? new MenuMessageSink(contextMenu2) : null;
            sink?.AssignHandle(ownerHwnd);
            // Ohne Foreground-Fenster wuerde das Popup sofort wieder schliessen
            // (Bereiche liegen bottom-most). Das Popup selbst zeichnet TOPMOST,
            // der Bereich bleibt in seiner Z-Ordnung.
            SetForegroundWindow(ownerHwnd);
            uint cmd;
            try
            {
                cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN, screenX, screenY, ownerHwnd, IntPtr.Zero);
            }
            finally
            {
                sink?.ReleaseHandle();
                PostMessage(ownerHwnd, 0, IntPtr.Zero, IntPtr.Zero); // WM_NULL: Menue sauber beenden
            }

            if (cmd >= MIN_ID)
            {
                var invoke = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    hwnd = ownerHwnd,
                    lpVerb = new IntPtr(cmd - MIN_ID),
                    nShow = 1 // SW_SHOWNORMAL
                };
                contextMenu.InvokeCommand(ref invoke);
            }

            Marshal.ReleaseComObject(contextMenu);
        }
        catch (Exception ex)
        {
            ISDesk.App.LogCrash(ex, "ShellContextMenu");
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (contextPtr != IntPtr.Zero) Marshal.Release(contextPtr);
            if (parent != null) Marshal.ReleaseComObject(parent);
            if (fullPidl != IntPtr.Zero) CoTaskMemFree(fullPidl);
        }
    }
}
