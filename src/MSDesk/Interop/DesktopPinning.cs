using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MSDesk.Interop;

/// <summary>
/// Verankert Bereichs-Fenster auf dem Desktop: Normal liegen sie IMMER ganz unten
/// (WM_WINDOWPOSCHANGING erzwingt HWND_BOTTOM). Bei "Desktop anzeigen"/Win+D hebt
/// Windows das Desktop-Band UEBER alle Fenster — die Bereiche wuerden dahinter
/// verschwinden. Ein WinEvent-Hook (Foreground-Wechsel, kein Polling) erkennt den
/// Desktop-Zustand und schaltet die Bereiche solange auf TOPMOST; verlaesst der
/// Nutzer den Desktop wieder, tauchen sie nach ganz unten ab.
/// (SetParent auf Progman scheitert bei Layered-WPF-Fenstern — gemessen.)
///
/// ACHTUNG bei Aenderungen an der Z-Reihenfolge: Es gibt ZWEI Ausloeser mit
/// unterschiedlichem Bedarf — der Desktop-Fall braucht TOPMOST, der Tray-Fall
/// darf ihn nicht haben. Sie wurden in v0.16.0 einmal ueber einen Kamm geschoren
/// (pauschal HWND_TOP, damit Bereiche keine Teams-Anrufe verdecken); seitdem
/// verschwanden die Bereiche bei "Desktop anzeigen". Aufgefallen ist das erst
/// viel spaeter, weil dieser Kommentar weiterhin "TOPMOST" behauptete, waehrend
/// der Code laengst etwas anderes tat. Siehe SetDesktopMode.
///
/// Das Wiederherstellen nach einem Minimieren steht NICHT hier, sondern EINMAL
/// in FenceWindow (StateChanged). Zwei Wege fuer dieselbe Sache waren in diesem
/// Projekt schon einmal die Ursache haengengebliebener Ausrichtungslinien.
/// </summary>
public static class DesktopPinning
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    private const uint GW_HWNDNEXT = 2;
    private const int WS_EX_TOPMOST = 0x00000008;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr mod,
        WinEventDelegate proc, uint pid, uint tid, uint flags);
    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
        int objectId, int childId, uint thread, uint time);

    private static readonly List<IntPtr> Attached = new();
    private static volatile bool _desktopMode;

    /// Liegt der Desktop selbst vorn (Win+D / Klick auf den Desktop)? Nur dann
    /// braucht es TOPMOST — siehe SetDesktopMode.
    private static volatile bool _ueberDemDesktop;
    private static readonly HashSet<string> GeseheneKlassen = new(StringComparer.Ordinal);
    private static WinEventDelegate? _foregroundProc; // Referenz halten, sonst sammelt der GC den Delegate ein

    private static System.Timers.Timer? _manualTimer;
    private static System.Timers.Timer? _wachTimer;

    /// <summary>
    /// 800 ms: schnell genug, dass niemand wartet, und selten genug, dass der
    /// Rechner seine Kerne weiter schlafen legen darf. Bei gesperrter Sitzung
    /// ruht der Blick ganz — dort gibt es nichts zu sehen.
    /// </summary>
    private static void EnsureWachTimer()
    {
        if (_wachTimer != null) return;
        _wachTimer = new System.Timers.Timer(800) { AutoReset = true };
        _wachTimer.Elapsed += (_, _) => Nachschauen();
        _wachTimer.Start();

        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (_wachTimer is not { } t) return;
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) t.Stop();
            else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) t.Start();
        };
    }

    /// Beim Beenden: erst die Blicke einstellen, dann duerfen die Fenster gehen.
    public static void Dispose()
    {
        _wachTimer?.Stop(); _wachTimer?.Dispose(); _wachTimer = null;
        _manualTimer?.Stop(); _manualTimer?.Dispose(); _manualTimer = null;
    }

    /// Holt alle Bereiche kurz nach vorn (Tray-Menue / Tray-Doppelklick). Sie sinken
    /// automatisch zurueck, sobald der Nutzer in eine andere App klickt — spaetestens
    /// aber nach 10 Sekunden. Ohne dieses Zeitlimit koennten Bereiche dauerhaft ueber
    /// Fenstern liegen, die Windows ohne Fokuswechsel oeffnet (z. B. Teams-Anrufe).
    public static void BringToFrontTemporarily()
    {
        SetDesktopMode(true);

        _manualTimer?.Dispose();
        _manualTimer = new System.Timers.Timer(10000) { AutoReset = false };
        _manualTimer.Elapsed += (_, _) =>
        {
            // Das Zeitlimit gilt nur fuer den Tray-Weg. Hat der Anwender
            // zwischenzeitlich den Desktop angezeigt, muessen die Bereiche
            // sichtbar bleiben — sonst saenken sie mitten im Anschauen ab.
            if (!_ueberDemDesktop) SetDesktopMode(false);
        };
        _manualTimer.Start();
    }

    public static void Attach(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);

        // Den GERADE gueltigen Zustand uebernehmen statt blind nach unten zu
        // schieben: Ein Bereich, der waehrend angezeigtem Desktop entsteht (neu
        // angelegt oder aus einem Tab abgetrennt), bliebe sonst unsichtbar.
        var einfuegen = _desktopMode
            ? (_ueberDemDesktop ? HWND_TOPMOST : HWND_TOP)
            : HWND_BOTTOM;
        SetWindowPos(hwnd, einfuegen, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        HwndSource.FromHwnd(hwnd)!.AddHook(ZOrderHook);

        lock (Attached) Attached.Add(hwnd);
        window.Closed += (_, _) => { lock (Attached) Attached.Remove(hwnd); };

        // Sind die Bereiche gerade von Hand ausgeblendet, darf ein neu
        // entstehender nicht als einziger sichtbar sein. NACH dem Anzeigen —
        // Show() wuerde ein frueheres Verstecken wieder aufheben.
        if (_ausgeblendet) window.Loaded += (_, _) => ShowWindow(hwnd, SW_HIDE);
        EnsureForegroundWatcher();
        EnsureWachTimer();
    }

    private static IntPtr ZOrderHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Nur im Normalmodus dauerhaft nach unten zwingen. Im Desktop-Modus wird
        // TOPMOST einmal beim Umschalten gesetzt — wuerde es hier bei jeder
        // Positionsaenderung erneut erzwungen, laegen die Bereiche ueber ihren
        // eigenen Kontextmenues.
        if (msg == WM_WINDOWPOSCHANGING && !_desktopMode)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            wp.hwndInsertAfter = HWND_BOTTOM;
            Marshal.StructureToPtr(wp, lParam, false);
        }
        return IntPtr.Zero;
    }

    /// Gehoert das Fenster zum Desktop selbst?
    private static bool IstDesktopFenster(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, 64);
        return sb.ToString() is "WorkerW" or "Progman" or "SHELLDLL_DefView" or "SysListView32";
    }

    /// <summary>
    /// Liegt der Desktop gerade UEBER dem normalen Fensterband?
    ///
    /// Von oben durch den Fensterstapel: Topmost-, Werkzeug-, unsichtbare,
    /// minimierte, winzige und eigene Fenster zaehlen nicht. Das erste
    /// verbleibende entscheidet — ist es ein Desktop-Fenster, wird gerade der
    /// Desktop angezeigt.
    ///
    /// ENTSCHEIDEND: Unsere eigenen Bereiche fallen wegen WS_EX_TOOLWINDOW und
    /// der Prozesspruefung in BEIDEN Zustaenden heraus. Die Antwort ist dadurch
    /// unabhaengig davon, ob wir selbst gerade hochgestuft haben — nur deshalb
    /// laesst sich dasselbe Merkmal fuers Hoch- UND Zurueckstufen benutzen und
    /// kann nicht schwingen.
    /// </summary>
    private static bool DesktopLiegtVorn()
    {
        var hwnd = GetTopWindow(IntPtr.Zero);
        for (var i = 0; hwnd != IntPtr.Zero && i < 300; i++, hwnd = GetWindow(hwnd, GW_HWNDNEXT))
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;

            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((ex & (WS_EX_TOPMOST | WS_EX_TOOLWINDOW)) != 0) continue;

            if (!GetWindowRect(hwnd, out var r) || r.Right - r.Left < 32 || r.Bottom - r.Top < 32) continue;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId) continue;

            return IstDesktopFenster(hwnd);
        }
        return false;
    }

    /// <summary>
    /// Sicherheitsnetz gegen ein ausbleibendes Ereignis.
    ///
    /// Die Erkennung ueber EVENT_SYSTEM_FOREGROUND ist billig, aber nicht
    /// verlaesslich: Auf dem Rechner des Anwenders meldete Windows beim
    /// „Desktop anzeigen" NIE eine Desktop-Fensterklasse — im Protokoll stand
    /// ueber die gesamte Laufzeit keine einzige Umschaltung. Die Bereiche
    /// blieben deshalb verdeckt, obwohl die Z-Reihenfolge laengst richtig war.
    ///
    /// Dieser Blick stuft ausdruecklich in BEIDE Richtungen. Eine Fassung, die
    /// nur hochstuft, waere schlimmer als der Ausgangsfehler gewesen: Feuert das
    /// Ereignis nie, laegen die Bereiche nach dem ersten „Desktop anzeigen"
    /// dauerhaft ueber allem — auch ueber einem Teams-Anruf. Genau davor sollte
    /// die Umstellung in v0.16.0 einmal schuetzen.
    /// </summary>
    private static void Nachschauen()
    {
        try
        {
            if (_ausgeblendet) return; // von Hand ausgeblendet — nicht hineinregieren

            lock (Attached) { if (Attached.Count == 0) return; }

            if (DesktopLiegtVorn()) SetDesktopMode(true, ueberDemDesktop: true);
            else if (_ueberDemDesktop) SetDesktopMode(false);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "DesktopPinning.Nachschauen");
        }
    }

    // --- Manuelles Ein- und Ausblenden (Doppelklick auf den Desktop) ---

    private static bool _ausgeblendet;

    /// Sind die Bereiche gerade ausgeblendet?
    public static bool IstAusgeblendet => _ausgeblendet;

    /// <summary>
    /// Blendet alle Bereiche aus oder wieder ein.
    ///
    /// Bewusst NICHT gespeichert: Nach einem Neustart sind die Bereiche immer
    /// da. Ein Zustand, der das Ausblenden ueberdauert, waere die perfekte
    /// Falle — man haelt das Werkzeug fuer kaputt und sucht an der falschen
    /// Stelle.
    /// </summary>
    public static void ToggleSichtbarkeit()
    {
        _ausgeblendet = !_ausgeblendet;

        List<IntPtr> fenster;
        lock (Attached) fenster = Attached.ToList();

        foreach (var hwnd in fenster)
        {
            if (!IsWindow(hwnd)) continue;
            ShowWindow(hwnd, _ausgeblendet ? SW_HIDE : SW_SHOWNOACTIVATE);

            if (!_ausgeblendet)
                SetWindowPos(hwnd, _desktopMode ? (_ueberDemDesktop ? HWND_TOPMOST : HWND_TOP) : HWND_BOTTOM,
                             0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        Services.StartupLog.Write(_ausgeblendet
            ? "Bereiche ausgeblendet (Doppelklick auf den Desktop)"
            : "Bereiche wieder eingeblendet");
    }

    private static void EnsureForegroundWatcher()
    {
        if (_foregroundProc != null) return;
        _foregroundProc = OnForegroundChanged;
        SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _foregroundProc, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    private static void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd,
        int objectId, int childId, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;

        var sb = new System.Text.StringBuilder(64);
        GetClassName(hwnd, sb, 64);
        var cls = sb.ToString();

        // Jede Fensterklasse EINMAL protokollieren. Bleibt „Desktop anzeigen"
        // wirkungslos, beantwortet genau diese Liste die entscheidende Frage:
        // meldet Windows den Desktop hier ueberhaupt? Kostet nach kurzer Zeit
        // nichts mehr, weil jede Klasse nur beim ersten Mal geschrieben wird.
        lock (GeseheneKlassen)
        {
            if (GeseheneKlassen.Count < 40 && GeseheneKlassen.Add(cls))
                Services.StartupLog.Write($"Vordergrund: {cls}");
        }

        if (cls is "WorkerW" or "Progman" or "SHELLDLL_DefView")
        {
            // Desktop liegt vorn → Bereiche darueber zeigen. Nur DIESER Fall
            // rechtfertigt TOPMOST (siehe SetDesktopMode).
            SetDesktopMode(true, ueberDemDesktop: true);
        }
        else
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId) return; // eigener Dialog/Bereich → Zustand halten
            SetDesktopMode(false);  // normale App vorn → Bereiche wieder ganz nach unten
        }
    }

    /// <summary>
    /// Schaltet zwischen „ganz unten" und „sichtbar" um.
    ///
    /// <paramref name="ueberDemDesktop"/> unterscheidet die beiden Ausloeser, und
    /// darin steckt der ganze Punkt:
    ///
    /// • „Desktop anzeigen" / Win+D / Klick auf den Desktop: Die Shell hebt das
    ///   Desktop-Fenster ueber das GESAMTE normale Band und haelt es dort.
    ///   HWND_TOP kommt dagegen nicht an — die Bereiche blieben verdeckt und
    ///   sahen aus, als waeren sie mitminimiert worden. Nur hier hilft TOPMOST.
    ///
    /// • Tray-Menue / Tray-Doppelklick: Da liegt der Desktop nicht oben, HWND_TOP
    ///   genuegt. Und es MUSS bei HWND_TOP bleiben — mit TOPMOST lagen die
    ///   Bereiche ueber Teams-Anrufen und Benachrichtigungen. Genau deshalb wurde
    ///   in v0.16.0 pauschal auf HWND_TOP umgestellt; das hat den Teams-Fall
    ///   geloest und Win+D dabei kaputtgemacht.
    /// </summary>
    private static void SetDesktopMode(bool active, bool ueberDemDesktop = false)
    {
        // Der Tray-Weg darf einen laufenden Desktop-Modus nicht zurueckstufen.
        if (active && !ueberDemDesktop) ueberDemDesktop = _ueberDemDesktop;

        if (_desktopMode == active && _ueberDemDesktop == ueberDemDesktop) return;
        _desktopMode = active;
        _ueberDemDesktop = active && ueberDemDesktop;

        // Eine Zeile im Protokoll. Bleiben die Bereiche bei „Desktop anzeigen"
        // trotzdem verdeckt, unterscheidet genau dieser Eintrag die beiden
        // moeglichen Ursachen: fehlt er, hat die Shell dieser Windows-Fassung
        // eine andere Fensterklasse gemeldet (dann gehoert sie in die Liste in
        // OnForegroundChanged). Steht er da, ist es die Z-Reihenfolge.
        Services.StartupLog.Write(active
            ? $"Bereiche sichtbar ({(_ueberDemDesktop ? "ueber dem Desktop" : "vorn")})"
            : "Bereiche wieder ganz unten");

        List<IntPtr> fenster;
        lock (Attached) fenster = Attached.ToList();

        foreach (var hwnd in fenster)
        {
            if (!IsWindow(hwnd)) continue;
            if (active)
            {
                SetWindowPos(hwnd, _ueberDemDesktop ? HWND_TOPMOST : HWND_TOP,
                             0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                // Sicherheitshalber ein evtl. frueher gesetztes Topmost aufheben.
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }
}
