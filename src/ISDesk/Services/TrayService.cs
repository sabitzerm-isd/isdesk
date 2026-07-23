using System.Drawing;
using System.Windows;
using ISDesk.Views;
using WinForms = System.Windows.Forms;

namespace ISDesk.Services;

public sealed class TrayService : IDisposable
{
    private readonly FenceManager _manager;
    private readonly AutostartService _autostart;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _autostartItem;

    public TrayService(FenceManager manager, AutostartService autostart)
    {
        _manager = manager;
        _autostart = autostart;

        _icon = new WinForms.NotifyIcon
        {
            Text = "ISDesk",
            Visible = true,
            Icon = LoadTrayIcon()
        };
        _icon.DoubleClick += (_, _) => NewFence();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Neuer Bereich", null, (_, _) => NewFence());
        menu.Items.Add("Alle Bereiche neu ausrichten", null, (_, _) => _manager.RealignAll());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Sicherung erstellen…", null, (_, _) => _manager.Backup?.CreateBackupInteractive(null));
        menu.Items.Add("Sicherung wiederherstellen…", null, (_, _) => _manager.Backup?.RestoreBackupInteractive(null));

        _autostartItem = new WinForms.ToolStripMenuItem("Autostart")
        {
            CheckOnClick = true,
            Checked = _autostart.IsEnabled
        };
        _autostartItem.CheckedChanged += (_, _) =>
        {
            if (_autostartItem.Checked) _autostart.Enable();
            else _autostart.Disable();
        };
        menu.Items.Add(_autostartItem);

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => Application.Current.Shutdown());

        _icon.ContextMenuStrip = menu;
    }

    private void NewFence()
    {
        var name = InputDialog.Ask("Name des neuen Bereichs:", "Neuer Bereich", null);
        if (string.IsNullOrWhiteSpace(name)) return;

        _manager.CreateFence(name, null);
        // Bereiche liegen hinter allen Fenstern — bei belegtem Bildschirm sonst unsichtbar.
        _icon.ShowBalloonTip(3000, "ISDesk",
            $"Bereich „{name}“ wurde auf dem Desktop angelegt (liegt hinter den offenen Fenstern).",
            WinForms.ToolTipIcon.Info);
    }

    private static Icon? LoadTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ISDesk.ico"));
            return info?.Stream != null ? new Icon(info.Stream) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
