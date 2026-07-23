using System.IO;
using System.Text.Json;
using ISDesk.Models;

namespace ISDesk.Services;

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
            "ISDesk", "config.json");
        _debounceTimer = new System.Timers.Timer(400) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => Save();
    }

    public void Load()
    {
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
            var dir = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            var json = JsonSerializer.Serialize(Config, JsonOptions);
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
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
