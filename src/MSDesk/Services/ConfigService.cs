using System.IO;
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

    /// Blockiert alle weiteren Saves — noetig waehrend einer Wiederherstellung,
    /// damit die alte In-Memory-Config die zurueckgespielte Datei nicht ueberschreibt.
    public void SuppressSaves()
    {
        lock (_sync)
        {
            _suppressSaves = true;
            _debounceTimer.Stop();
        }
    }

    public void Save()
    {
        lock (_sync)
        {
            if (_suppressSaves) return;
            try
            {
                var dir = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                var json = JsonSerializer.Serialize(Config, JsonOptions);
                File.WriteAllText(tmp, json);
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                // Ohne diese Meldung blieb ein fehlgeschlagenes Speichern voellig
                // unbemerkt: der Aufruf kommt aus einem Timer-Rueckruf, dessen
                // Ausnahmen spurlos verschwinden.
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
