using System.IO;
using System.IO.Compression;
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

    public ConfigService(string? pathOverride = null, string? sicherungsOrdner = null)
    {
        _path = pathOverride ?? Path.Combine(DefaultFolder, "config.json");
        _sicherungsOrdner = pathOverride == null ? BackupService.StandardOrdner() : sicherungsOrdner;
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
            TryBackupBadFile();
            StartupLog.Write("Konfiguration NICHT lesbar.");

            // Belegt 30.10. und 03.11.2026: nach einem Windows-Neustart lag
            // config.json mit 0 Byte da. Frueher ging es hier sofort mit
            // Standardwerten weiter — und das erste Speichern kopierte diese
            // ueber die .bak, die einzige gute Kopie. Deshalb zuerst die
            // Ersatzkopie, dann die neueste Tages-Sicherung.
            if (LadeErsatz()) return;

            // Nichts Brauchbares gefunden. Die .bak trotzdem beiseitelegen,
            // bevor das erste Speichern sie ueberschreibt.
            TryCopy(_path + ".bak", Path.Combine(Path.GetDirectoryName(_path)!, "config.bak.bad.json"));
            Config = new AppConfig();
            StartupLog.Write("Weder Ersatzkopie noch Sicherung lesbar — mit Standardwerten gestartet.");
        }
    }

    /// Ordner, in dem beim Start nach der neuesten Tages-Sicherung gesucht wird.
    /// Ohne eigenen Konfigurationspfad (echter Start) der Standardordner —
    /// Tests mit eigenem Pfad greifen so nie auf echte Sicherungen zu.
    private readonly string? _sicherungsOrdner;

    private bool LadeErsatz()
    {
        var bak = _path + ".bak";
        var ersatz = LiesKonfiguration(bak);
        var quelle = bak;

        if (ersatz == null)
        {
            ersatz = AusNeuesterSicherung(_sicherungsOrdner, out var zip);
            quelle = zip;
        }

        if (ersatz == null) return false;

        Config = ersatz;
        StartupLog.Write($"Aus Ersatz geladen: {quelle} ({Config.Fences.Count} Bereiche)");

        // Sofort zurueckschreiben, damit config.json wieder heil ist und nicht
        // erst der naechste Speichervorgang das nachholt.
        Save();
        return true;
    }

    private static AppConfig? LiesKonfiguration(string datei)
    {
        try
        {
            if (!File.Exists(datei)) return null;
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(datei), JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Konfiguration aus der neuesten MSDesk-Sicherung im Ordner. Genommen wird
    /// nur eine Sicherung mit Bereichen, deren Ordner der Bereiche es auf DIESEM
    /// Rechner gibt — liegt der Sicherungsordner gemeinsam mit Kollegen in der
    /// Cloud, wird so nicht deren Einrichtung geladen.
    /// </summary>
    internal static AppConfig? AusNeuesterSicherung(string? ordner, out string? datei)
    {
        datei = null;
        try
        {
            if (string.IsNullOrWhiteSpace(ordner) || !Directory.Exists(ordner)) return null;

            var zips = new DirectoryInfo(ordner).GetFiles("MSDesk-Sicherung*.zip")
                                                .OrderByDescending(f => f.LastWriteTimeUtc);
            foreach (var zipDatei in zips)
            {
                try
                {
                    using var zip = ZipFile.OpenRead(zipDatei.FullName);
                    var eintrag = zip.GetEntry("config.json");
                    if (eintrag == null) continue;

                    using var stream = eintrag.Open();
                    var cfg = JsonSerializer.Deserialize<AppConfig>(stream, JsonOptions);
                    if (cfg == null || cfg.Fences.Count == 0) continue;
                    if (string.IsNullOrWhiteSpace(cfg.BaseFolder) || !Directory.Exists(cfg.BaseFolder)) continue;

                    datei = zipDatei.FullName;
                    return cfg;
                }
                catch (Exception)
                {
                    // Diese Sicherung ist unbrauchbar — die naechstaeltere versuchen.
                }
            }
        }
        catch (Exception)
        {
            // Ordner nicht erreichbar (Cloud-Laufwerk fehlt) — kein Ersatz.
        }
        return null;
    }

    private static void TryCopy(string von, string nach)
    {
        try
        {
            if (File.Exists(von)) File.Copy(von, nach, overwrite: true);
        }
        catch
        {
            // best-effort
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
                // Gegen Datenverlust schuetzt die Sicherungskopie unten — Load
                // greift auf sie zurueck, wenn config.json unlesbar ist.
                // Mit Flush(true) bis auf die Platte: sonst kann ein Neustart
                // (Windows-Update) die bereits geleerte Datei mit 0 Byte
                // hinterlassen, waehrend der Inhalt noch im Zwischenspeicher lag.
                using (var stream = new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(new UTF8Encoding(false).GetBytes(json));
                    stream.Flush(flushToDisk: true);
                }

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
