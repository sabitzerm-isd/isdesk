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

    public ConfigService(string? pathOverride = null)
    {
        _path = pathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MSDesk", "config.json");
        _debounceTimer = new System.Timers.Timer(400) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Save();
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

        if (!File.Exists(_path))
        {
            Config = new AppConfig();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);
            Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch (Exception)
        {
            // Kaputte Datei: Sicherungskopie ablegen, mit Defaults weiterarbeiten (kein Crash).
            TryBackupBadFile();
            Config = new AppConfig();
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

    /// Liest die Datei zurueck und prueft, ob wirklich das Erwartete darin steht.
    private bool Angekommen(string erwartet)
    {
        try
        {
            return new FileInfo(_path) is { Exists: true } info
                && Math.Abs(info.Length - Encoding.UTF8.GetByteCount(erwartet)) <= 3; // BOM-Toleranz
        }
        catch (Exception)
        {
            return false;
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

                // 1. Weg: erst in eine Nebendatei, dann umbenennen. Dadurch kann
                //    ein Abbruch mitten im Schreiben die Konfiguration nicht
                //    zerstoeren.
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);

                // NACHPRUEFEN. Es hat sich gezeigt, dass ein Speichervorgang
                // ohne Fehlermeldung durchlaufen kann, ohne dass die Datei
                // tatsaechlich veraendert wird. Ohne diese Pruefung meldet das
                // Programm Erfolg, waehrend jede Einstellung verloren geht.
                if (!Angekommen(json))
                {
                    // 2. Weg: unmittelbar in die Zieldatei schreiben.
                    File.WriteAllText(_path, json);

                    StartupLog.Write(Angekommen(json)
                        ? $"Gespeichert ({json.Length} Zeichen) — auf dem zweiten Weg, Umbenennen kam nicht an."
                        : $"SPEICHERN KAM NICHT AN! Datei unveraendert: {_path}");
                    return;
                }

                // Nur die ersten Male protokollieren — danach waere es nur Rauschen.
                if (++SaveCount <= 3) StartupLog.Write($"Gespeichert ({json.Length} Zeichen) → {_path}");
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
