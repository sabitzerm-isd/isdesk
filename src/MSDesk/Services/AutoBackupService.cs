using MSDesk.Models;

namespace MSDesk.Services;

/// <summary>
/// Sichert einmal taeglich von selbst — ohne Fenster, ohne Klick.
///
/// Warum nicht einfach ein Zeitplan zu einer festen Uhrzeit: MSDesk laeuft
/// nicht rund um die Uhr. Der Rechner wird abends heruntergefahren, morgens
/// wieder gestartet, das Notebook zwischendurch zugeklappt. Eine feste Uhrzeit
/// wuerde regelmaessig verpasst. Deshalb wird der ABSTAND zur letzten Sicherung
/// geprueft: liegt sie mehr als einen Tag zurueck, wird gesichert — egal wann
/// der Rechner gerade laeuft.
///
/// Geschwindigkeit: Die Pruefung selbst ist ein Datumsvergleich und kostet
/// nichts. Die Sicherung laeuft im Zeitgeber-Faden, nicht im Bedienfaden — die
/// Oberflaeche bleibt waehrenddessen bedienbar. Der erste Blick erfolgt
/// bewusst erst einige Minuten nach dem Start, damit der Start selbst nicht
/// zusaetzlich belastet wird.
/// </summary>
public sealed class AutoBackupService : IDisposable
{
    /// Mindestabstand zwischen zwei selbsttaetigen Sicherungen.
    public static readonly TimeSpan Abstand = TimeSpan.FromHours(24);

    /// Erster Blick nach dem Start. Nicht sofort: waehrend des Starts sind
    /// Platte und Bedienfaden ohnehin beschaeftigt.
    private static readonly TimeSpan ErsterBlick = TimeSpan.FromMinutes(5);

    /// Danach stuendlich. Haeufiger braucht es nicht — es geht um Tage.
    private static readonly TimeSpan Takt = TimeSpan.FromHours(1);

    private readonly ConfigService _config;
    private readonly BackupService _backup;
    private System.Timers.Timer? _timer;
    private int _laeuft; // 0/1 statt bool: Interlocked braucht einen int

    public AutoBackupService(ConfigService config, BackupService backup)
    {
        _config = config;
        _backup = backup;
    }

    /// <summary>
    /// Ist eine selbsttaetige Sicherung faellig? Reine Entscheidung ohne
    /// Nebenwirkung — bewusst getrennt, damit sie pruefbar ist.
    /// </summary>
    public static bool IstFaellig(AppConfig config, DateTime jetztUtc)
    {
        if (!config.AutoBackupDaily) return false;
        if (string.IsNullOrWhiteSpace(config.AutoBackupFolder)) return false;
        if (config.LastAutoBackupUtc is not { } letzte) return true; // noch nie gesichert

        // Ein Zeitpunkt in der Zukunft kann nur aus einer verstellten Uhr oder
        // einer bearbeiteten Konfiguration stammen. Ohne diesen Fang bliebe die
        // Sicherung danach dauerhaft aus.
        if (letzte > jetztUtc) return true;

        return jetztUtc - letzte >= Abstand;
    }

    public void Start()
    {
        if (_timer != null) return;

        _timer = new System.Timers.Timer(ErsterBlick.TotalMilliseconds) { AutoReset = false };
        _timer.Elapsed += (_, _) =>
        {
            try
            {
                Pruefen();

                // Nach dem ersten Blick auf den Stundentakt umstellen. Der Fang
                // umschliesst das mit: wird MSDesk genau jetzt beendet, ist der
                // Zeitgeber zwischen dieser Pruefung und dem Start bereits
                // freigegeben — ohne Fang endete das Beenden mit einem Fehler.
                if (_timer is not { } t) return;
                t.Interval = Takt.TotalMilliseconds;
                t.AutoReset = true;
                t.Start();
            }
            catch (ObjectDisposedException)
            {
                // MSDesk wird gerade beendet — nichts mehr zu tun.
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "AutoBackupService.Elapsed");
            }
        };
        _timer.Start();
    }

    /// <summary>
    /// Prueft und sichert bei Bedarf. Faellt still aus, wenn etwas nicht geht:
    /// der Zielordner liegt oft in der Cloud und ist nicht immer erreichbar —
    /// dafuer darf keine Meldung erscheinen. Beim naechsten Takt wird es
    /// ohnehin erneut versucht.
    /// </summary>
    public void Pruefen()
    {
        // Ueberlappung ausschliessen: eine Sicherung kann laenger dauern als
        // ein Takt, und zwei gleichzeitige Laeufe wuerden dieselbe Datei
        // schreiben.
        if (Interlocked.Exchange(ref _laeuft, 1) == 1) return;

        try
        {
            if (!IstFaellig(_config.Config, DateTime.UtcNow)) return;

            var ergebnis = _backup.WriteToConfiguredFolder();
            if (!ergebnis.Erfolg)
            {
                StartupLog.Write($"Selbsttaetige Sicherung fehlgeschlagen: {ergebnis.Fehler}");
                return;
            }

            // Zeitpunkt erst NACH dem Erfolg merken: nach einem Fehlversuch
            // soll es beim naechsten Takt sofort wieder probiert werden.
            _config.Config.LastAutoBackupUtc = DateTime.UtcNow;
            _config.Save();

            StartupLog.Write(
                $"Selbsttaetige Sicherung: {ergebnis.Datei} ({ergebnis.Megabyte} MB)");
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "AutoBackupService.Pruefen");
        }
        finally
        {
            Interlocked.Exchange(ref _laeuft, 0);
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }
}
