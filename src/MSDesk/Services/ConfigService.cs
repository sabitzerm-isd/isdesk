using System.IO;
using System.Text;
using System.Text.Json;
using MSDesk.Models;

namespace MSDesk.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly object _sync = new();
    private bool _suppressSaves;

    public AppConfig Config { get; private set; } = new();

    public string ConfigPath => _path;

    /// <summary>
    /// Ablageort der Konfiguration.
    ///
    /// Bewusst „AppData\Local" statt „AppData\Roaming": Roaming wird von
    /// Profil-Synchronisation, Sicherungswerkzeugen und Schutzprogrammen
    /// angefasst. Bei einem Anwender wurden dort Aenderungen nachtraeglich auf
    /// aeltere Staende zurueckgesetzt — samt urspruenglichem Zeitstempel —,
    /// wodurch saemtliche Einstellungen verloren gingen, ohne dass MSDesk etwas
    /// davon bemerken konnte. Local ist genau fuer solche Daten vorgesehen.
    /// </summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MSDesk");

    /// Frueherer Ablageort (bis v0.28.6) — wird einmalig uebernommen.
    private static string LegacyFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSDesk");

    public ConfigService(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(DefaultFolder, "config.json");
        _debounceTimer = new System.Timers.Timer(400) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Save();
    }

    /// <summary>
    /// Uebernimmt Konfiguration und Symbol-Zwischenspeicher aus dem frueheren
    /// Ablageort. Laeuft nur, solange am neuen Ort noch nichts liegt.
    /// </summary>
    private void MigrateFromRoaming()
    {
        try
        {
            if (File.Exists(_path)) return;

            var alteDatei = Path.Combine(LegacyFolder, "config.json");
            if (!File.Exists(alteDatei)) return;

            Directory.CreateDirectory(DefaultFolder);
            File.Copy(alteDatei, _path, overwrite: false);
            StartupLog.Write($"Konfiguration uebernommen: {alteDatei} → {_path}");

            var alteIcons = Path.Combine(LegacyFolder, "FavIcons");
            var neueIcons = Path.Combine(DefaultFolder, "FavIcons");
            if (Directory.Exists(alteIcons) && !Directory.Exists(neueIcons))
            {
                Directory.CreateDirectory(neueIcons);
                foreach (var datei in Directory.GetFiles(alteIcons))
                    File.Copy(datei, Path.Combine(neueIcons, Path.GetFileName(datei)), overwrite: false);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ConfigService.MigrateFromRoaming");
        }
    }

    /// Uebernimmt die Einstellungen der Vorgaengerversion, die noch "ISDesk" hiess
    /// (Bereiche, Symbole, Regeln, Platz-Gedaechtnis). Laeuft nur, solange es noch
    /// keine eigene Konfiguration gibt — danach nie wieder.
    private void MigrateFromIsDesk()
    {
        try
        {
            if (File.Exists(_path)) return;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var oldFolder = Path.Combine(appData, "ISDesk");
            var oldConfig = Path.Combine(oldFolder, "config.json");
            if (!File.Exists(oldConfig)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.Copy(oldConfig, _path, overwrite: false);

            // Symbol-Zwischenspeicher der Lesezeichen mitnehmen (sonst laedt er neu).
            var oldIcons = Path.Combine(oldFolder, "FavIcons");
            var newIcons = Path.Combine(Path.GetDirectoryName(_path)!, "FavIcons");
            if (Directory.Exists(oldIcons) && !Directory.Exists(newIcons))
            {
                Directory.CreateDirectory(newIcons);
                foreach (var file in Directory.GetFiles(oldIcons))
                    File.Copy(file, Path.Combine(newIcons, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "ConfigService.MigrateFromIsDesk");
        }
    }

    public void Load()
    {
        MigrateFromIsDesk();
        MigrateFromRoaming();

        if (!File.Exists(_path))
        {
            Config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();

            // Festhalten, WAS tatsaechlich geladen wurde. Zusammen mit dem
            // Eintrag beim Speichern laesst sich damit belegen, ob das zuletzt
            // Geschriebene beim naechsten Start wirklich ankommt.
            var kennungen = Config.Fences.SelectMany(f => f.Layouts.Keys)
                                         .Distinct(StringComparer.Ordinal).ToList();
            StartupLog.Write($"Geladen: {json.Length} Zeichen, {Config.Fences.Count} Bereiche, " +
                             $"{kennungen.Count} Bildschirm-Kennungen [{string.Join(" | ", kennungen)}]");
        }
        catch (Exception)
        {
            // Kaputte Datei: Sicherungskopie ablegen, mit Defaults weiterarbeiten (kein Crash).
            TryBackupBadFile();
            Config = new AppConfig();
            StartupLog.Write("Konfiguration NICHT lesbar — mit Standardwerten gestartet.");
        }
    }

    private void TryBackupBadFile()
    {
        try
        {
            var bad = Path.Combine(Path.GetDirectoryName(_path)!, "config.bad.json");
            File.Copy(_path, bad, overwrite: true);
        }
        catch
        {
            // Sicherungskopie ist best-effort.
        }
    }

    /// <summary>
    /// Blockiert alle weiteren Saves — noetig waehrend einer Wiederherstellung,
    /// damit die alte In-Memory-Config die zurueckgespielte Datei nicht ueberschreibt.
    ///
    /// Die Sperre loest sich nach kurzer Zeit von selbst wieder: Bricht die
    /// Wiederherstellung ab, bliebe sonst dauerhaft gesperrt und es wuerde nie
    /// wieder etwas gespeichert — ohne dass das erkennbar waere.
    /// </summary>
    public void SuppressSaves()
    {
        lock (_sync)
        {
            _suppressSaves = true;
            _debounceTimer.Stop();
            StartupLog.Write("Speichern gesperrt (Wiederherstellung).");

            _unsuppressTimer?.Dispose();
            _unsuppressTimer = new System.Timers.Timer(30_000) { AutoReset = false };
            _unsuppressTimer.Elapsed += (_, _) =>
            {
                lock (_sync)
                {
                    if (!_suppressSaves) return;
                    _suppressSaves = false;
                    StartupLog.Write("Speicher-Sperre automatisch aufgehoben (Wiederherstellung lief nicht durch).");
                }
            };
            _unsuppressTimer.Start();
        }
    }

    private System.Timers.Timer? _unsuppressTimer;

    /// Zaehlt die erfolgreichen Speichervorgaenge (fuer das Startprotokoll).
    public int SaveCount { get; private set; }

    /// Liest die Datei frisch von der Platte (ohne Zwischenspeicher).
    private string? Zurueckgelesen()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            if (_suppressSaves)
            {
                StartupLog.Write("Speichern uebersprungen (gesperrt).");
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(Config, JsonOptions);

                // UNMITTELBAR in die Zieldatei schreiben. Frueher lief das ueber
                // eine Nebendatei mit anschliessendem Umbenennen — das ist bei
                // Abstuerzen sicherer, kam aber nachweislich nicht immer an.
                // Gegen Datenverlust schuetzt jetzt die Sicherungskopie unten.
                File.WriteAllText(_path, json, new UTF8Encoding(false));

                // NACHPRUEFEN: den tatsaechlichen Inhalt zurueklesen. Es hat sich
                // gezeigt, dass ein Speichervorgang ohne jede Fehlermeldung
                // durchlaufen kann, ohne dass die Datei veraendert wird — das
                // Programm meldet dann Erfolg, waehrend alles verloren geht.
                var gelesen = Zurueckgelesen();
                var stimmt = string.Equals(gelesen, json, StringComparison.Ordinal);

                if (!stimmt)
                {
                    StartupLog.Write(
                        $"SPEICHERN KAM NICHT AN! geschrieben {json.Length}, " +
                        $"in der Datei {gelesen?.Length.ToString() ?? "nicht lesbar"} Zeichen — {_path}");
                    return;
                }

                // Zusaetzliche Kopie: geht die Hauptdatei doch einmal verloren,
                // ist der letzte gute Stand noch vorhanden.
                try { File.Copy(_path, _path + ".bak", overwrite: true); } catch (Exception) { }

                // Nur die ersten Male protokollieren — danach waere es nur Rauschen.
                if (++SaveCount <= 3)
                    StartupLog.Write($"Gespeichert und geprueft ({json.Length} Zeichen) → {_path}");
            }
            catch (Exception ex)
            {
                // Ohne diese Meldung blieb ein fehlgeschlagenes Speichern voellig
                // unbemerkt: der Aufruf kommt aus einem Timer-Rueckruf, dessen
                // Ausnahmen spurlos verschwinden.
                StartupLog.Write($"SPEICHERN FEHLGESCHLAGEN -> {ex.GetType().Name}: {ex.Message}");
                App.LogCrash(ex, "ConfigService.Save");
            }
        }
    }

    public void SaveDebounced()
    {
        lock (_sync)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }
}
