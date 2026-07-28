using System.Drawing;
using System.Windows;
using MSDesk.Views;
using WinForms = System.Windows.Forms;

namespace MSDesk.Services;

/// Symbol im Infobereich samt Menue. Das Menue ist bewusst dunkel gestaltet
/// (eigener Renderer), damit es zur Optik der Bereiche passt, und traegt vor
/// jedem Eintrag ein Symbol.
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
            Text = "MSDesk",
            Visible = true,
            Icon = LoadTrayIcon()
        };
        // Doppelklick aufs Tray: Bereiche kurz nach vorn (praktisch waehrend Vollbild-Apps/Teams).
        _icon.DoubleClick += (_, _) => Interop.DesktopPinning.BringToFrontTemporarily();

        var menu = new WinForms.ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            BackColor = MenuColors.Background,
            ForeColor = MenuColors.Text,
            ShowImageMargin = true
        };

        // Kopfzeile mit Name und Fassung: so ist ohne Umweg ueber die Optionen
        // erkennbar, welche Fassung gerade laeuft.
        menu.Items.Add(Kopfzeile($"MSDesk v{UpdateService.CurrentVersion}"));
        menu.Items.Add(Separator());

        menu.Items.Add(Item("Bereiche in den Vordergrund", "",
            (_, _) => Interop.DesktopPinning.BringToFrontTemporarily()));
        menu.Items.Add(Item("Neuer Bereich…", "", (_, _) => NewFence()));
        menu.Items.Add(Item("Alle Bereiche neu ausrichten", "", (_, _) => _manager.RealignAll()));
        menu.Items.Add(Separator());

        menu.Items.Add(Item("Optionen…", "", (_, _) => OpenSettings()));
        menu.Items.Add(Item("Anleitung öffnen", "", (_, _) => HelpPage.Open()));
        menu.Items.Add(Item("Nach Updates suchen…", "", (_, _) => CheckUpdates()));
        menu.Items.Add(Separator());

        menu.Items.Add(Item("Sicherung erstellen…", "",
            (_, _) => _manager.Backup?.CreateBackupInteractive(null)));
        menu.Items.Add(Item("Sicherung wiederherstellen…", "",
            (_, _) => _manager.Backup?.RestoreBackupInteractive(null)));

        menu.Items.Add(Separator());

        _autostartItem = new WinForms.ToolStripMenuItem("Mit Windows starten")
        {
            CheckOnClick = true,
            Checked = _autostart.IsEnabled,
            ForeColor = MenuColors.Text
        };
        _autostartItem.CheckedChanged += (_, _) =>
        {
            if (_autostartItem.Checked) _autostart.Enable();
            else _autostart.Disable();
            // Wunsch merken: nur so laesst sich „abgeschaltet" von „Eintrag
            // verlorengegangen" unterscheiden.
            _manager.SetAutostartWanted(_autostartItem.Checked);
        };
        menu.Items.Add(_autostartItem);

        menu.Items.Add(Separator());
        menu.Items.Add(Item("MSDesk beenden", "", (_, _) => Application.Current.Shutdown(), MenuColors.SymbolWarnung));

        _icon.ContextMenuStrip = menu;
    }

    // --- Menue-Bausteine ---

    private static WinForms.ToolStripMenuItem Item(string text, string glyph, EventHandler onClick,
                                                  Color? symbolFarbe = null)
    {
        var item = new WinForms.ToolStripMenuItem(text) { ForeColor = MenuColors.Text };
        item.Image = GlyphImage(glyph, symbolFarbe ?? MenuColors.Symbol);
        item.Click += onClick;
        return item;
    }

    /// <summary>
    /// Kopfzeile des Menues: Name und Fassung. Nicht anklickbar — sie dient
    /// nur der Auskunft, welche Fassung gerade laeuft.
    /// </summary>
    private static WinForms.ToolStripMenuItem Kopfzeile(string text)
    {
        var item = new WinForms.ToolStripMenuItem(text)
        {
            Enabled = false,
            ForeColor = MenuColors.Kopfzeile
        };
        item.Font = new Font(item.Font, System.Drawing.FontStyle.Regular);
        return item;
    }

    private static WinForms.ToolStripSeparator Separator() => new();

    /// <summary>
    /// Zeichnet eine Symbol-Glyphe als kleines Bild fuer das Menue.
    ///
    /// Die Schriftart wird geprueft statt blind verwendet: fehlt sie, faellt
    /// .NET stillschweigend auf eine Ersatzschrift zurueck und zeichnet ein
    /// leeres Kaestchen — das saehe schlimmer aus als gar kein Symbol.
    /// „Segoe Fluent Icons" gibt es ab Windows 11, davor „Segoe MDL2 Assets".
    /// </summary>
    private static Bitmap? GlyphImage(string glyph, Color farbe)
    {
        if (string.IsNullOrEmpty(glyph)) return null;

        var name = Symbolschrift();
        if (name == null) return null;

        try
        {
            var bmp = new Bitmap(16, 16);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font(name, 11f,
                System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(farbe);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, 16, 16), format);
            return bmp;
        }
        catch (Exception)
        {
            return null; // ohne Symbol weiter
        }
    }

    /// Vorhandene Symbolschrift (einmal ermittelt) oder null.
    private static string? _symbolschrift;
    private static bool _symbolschriftGeprueft;

    private static string? Symbolschrift()
    {
        if (_symbolschriftGeprueft) return _symbolschrift;
        _symbolschriftGeprueft = true;

        foreach (var kandidat in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                using var test = new Font(kandidat, 11f, GraphicsUnit.Pixel);
                // .NET liefert bei fehlender Schrift die Ersatzschrift zurueck —
                // erkennbar am abweichenden Namen.
                if (string.Equals(test.Name, kandidat, StringComparison.OrdinalIgnoreCase))
                {
                    _symbolschrift = kandidat;
                    return _symbolschrift;
                }
            }
            catch (Exception)
            {
                // naechster Kandidat
            }
        }
        return null;
    }

    private static class MenuColors
    {
        public static readonly Color Background = Color.FromArgb(30, 33, 40);
        public static readonly Color Text = Color.FromArgb(242, 255, 255);
        public static readonly Color Hover = Color.FromArgb(52, 57, 68);
        public static readonly Color Separator = Color.FromArgb(60, 65, 78);
        public static readonly Color Border = Color.FromArgb(70, 76, 90);

        /// Farbe der Symbole. Weiss wirkte flach und ging im dunklen Menue
        /// unter — ein heller Blauton hebt sie ab, ohne laut zu sein.
        public static readonly Color Symbol = Color.FromArgb(125, 183, 255);

        /// Fuer Eintraege, die etwas beenden.
        public static readonly Color SymbolWarnung = Color.FromArgb(232, 122, 122);

        /// Kopfzeile: sichtbar, aber deutlich zurueckgenommen.
        public static readonly Color Kopfzeile = Color.FromArgb(150, 158, 172);
    }

    /// Dunkles Menue passend zur App (WinForms zeichnet sonst hellgrau).
    private sealed class DarkMenuRenderer : WinForms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemText(WinForms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item?.Enabled == true ? MenuColors.Text : Color.FromArgb(130, 140, 150);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(WinForms.ToolStripSeparatorRenderEventArgs e)
        {
            var bounds = e.Item.ContentRectangle;
            using var pen = new Pen(MenuColors.Separator);
            var y = bounds.Top + bounds.Height / 2;
            e.Graphics.DrawLine(pen, bounds.Left + 8, y, bounds.Right - 8, y);
        }

        private sealed class DarkColorTable : WinForms.ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => MenuColors.Background;
            public override Color MenuItemSelected => MenuColors.Hover;
            public override Color MenuItemSelectedGradientBegin => MenuColors.Hover;
            public override Color MenuItemSelectedGradientEnd => MenuColors.Hover;
            public override Color MenuItemBorder => MenuColors.Hover;
            public override Color MenuBorder => MenuColors.Border;
            public override Color ImageMarginGradientBegin => MenuColors.Background;
            public override Color ImageMarginGradientMiddle => MenuColors.Background;
            public override Color ImageMarginGradientEnd => MenuColors.Background;
            public override Color CheckBackground => MenuColors.Hover;
            public override Color CheckSelectedBackground => MenuColors.Hover;
        }
    }

    // --- Aktionen ---

    private void OpenSettings()
    {
        var vm = _manager.FirstFenceViewModel();
        if (vm == null) return;
        new SettingsDialog(vm, _manager, null).ShowDialog();
    }

    private void CheckUpdates()
    {
        var vm = _manager.FirstFenceViewModel();
        if (vm == null) return;
        var dialog = new SettingsDialog(vm, _manager, null);
        dialog.ShowUpdateSection();
        dialog.ShowDialog();
    }

    private void NewFence()
    {
        var name = InputDialog.Ask("Name des neuen Bereichs:", "Neuer Bereich", null);
        if (string.IsNullOrWhiteSpace(name)) return;

        _manager.CreateFence(name);
        // Bereiche liegen hinter allen Fenstern — bei belegtem Bildschirm sonst unsichtbar.
        _icon.ShowBalloonTip(3000, "MSDesk",
            $"Bereich „{name}“ wurde auf dem Desktop angelegt (liegt hinter den offenen Fenstern).",
            WinForms.ToolTipIcon.Info);
    }

    private static Icon? LoadTrayIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/MSDesk.ico"));
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
