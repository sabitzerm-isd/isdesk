using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using ISDesk.Models;
using ISDesk.Views;

namespace ISDesk.Services;

/// Sichert Konfiguration + alle Bereichs-Ordner in eine ZIP-Datei und stellt
/// sie wieder her (z. B. nach Neuaufsetzen des Rechners). Nach einer
/// Wiederherstellung startet die App neu.
public sealed class BackupService
{
    private readonly ConfigService _config;
    private readonly FenceManager _manager;

    public BackupService(ConfigService config, FenceManager manager)
    {
        _config = config;
        _manager = manager;
    }

    public void CreateBackupInteractive(Window? centerOn)
    {
        var dialog = new SaveFileDialog
        {
            Title = "ISDesk-Sicherung erstellen",
            FileName = $"ISDesk-Sicherung-{DateTime.Now:yyyy-MM-dd}.zip",
            Filter = "ISDesk-Sicherung (*.zip)|*.zip",
            DefaultExt = "zip"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            WriteBackup(dialog.FileName);
            ConfirmDialog.Info($"Sicherung erstellt:\n{dialog.FileName}", centerOn);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "CreateBackup");
            ConfirmDialog.Info($"Sicherung fehlgeschlagen:\n{ex.Message}", centerOn);
        }
    }

    /// Ein-Klick-Sicherung in den hinterlegten Ordner (mit Zeitstempel im Namen).
    public void CreateBackupAuto(Window? centerOn)
    {
        var folder = _config.Config.AutoBackupFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            ConfirmDialog.Info("Kein Sicherungspfad hinterlegt — bitte oben den Ordner setzen.", centerOn);
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"ISDesk-Sicherung-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
            WriteBackup(file);
            ConfirmDialog.Info($"Sicherung erstellt:\n{file}", centerOn);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "CreateBackupAuto");
            ConfirmDialog.Info($"Sicherung fehlgeschlagen:\n{ex.Message}", centerOn);
        }
    }

    private void WriteBackup(string fileName)
    {
        _config.Save(); // aktuellen Stand auf die Platte bringen

        if (File.Exists(fileName)) File.Delete(fileName);
        using var zip = ZipFile.Open(fileName, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(_config.ConfigPath, "config.json");

        var baseFolder = _config.Config.BaseFolder;
        if (Directory.Exists(baseFolder))
        {
            foreach (var file in Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(baseFolder, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, "Fences/" + rel);
            }
        }
    }

    public void RestoreBackupInteractive(Window? centerOn)
    {
        var dialog = new OpenFileDialog
        {
            Title = "ISDesk-Sicherung wiederherstellen",
            Filter = "ISDesk-Sicherung (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() != true) return;

        var (confirmed, _) = ConfirmDialog.Show(
            "Sicherung wiederherstellen?\n\nDie aktuelle Konfiguration wird ersetzt, die gesicherten " +
            "Bereichs-Ordner werden zurückgespielt (bestehende Dateien gleichen Namens werden überschrieben). " +
            "ISDesk startet danach neu.",
            centerOn, okText: "Wiederherstellen");
        if (!confirmed) return;

        try
        {
            using var zip = ZipFile.OpenRead(dialog.FileName);

            var configEntry = zip.GetEntry("config.json")
                ?? throw new InvalidDataException("Die Datei ist keine ISDesk-Sicherung (config.json fehlt).");

            // Basisordner aus der GESICHERTEN Konfiguration lesen.
            AppConfig restored;
            using (var stream = configEntry.Open())
            {
                restored = JsonSerializer.Deserialize<AppConfig>(stream) ?? new AppConfig();
            }
            var baseFolder = string.IsNullOrWhiteSpace(restored.BaseFolder) ? @"D:\Fences" : restored.BaseFolder;

            // Ab hier nichts mehr speichern (sonst ueberschreibt die alte In-Memory-Config die Wiederherstellung).
            _config.SuppressSaves();
            _manager.CloseAllWithoutSave();

            // Alte Konfiguration aufheben, neue schreiben.
            if (File.Exists(_config.ConfigPath))
                File.Copy(_config.ConfigPath, _config.ConfigPath + ".vor-wiederherstellung.json", overwrite: true);
            configEntry.ExtractToFile(_config.ConfigPath, overwrite: true);

            // Bereichs-Ordner zurueckspielen.
            Directory.CreateDirectory(baseFolder);
            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("Fences/", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                var rel = entry.FullName.Substring("Fences/".Length);
                var target = Path.GetFullPath(Path.Combine(baseFolder, rel));
                // Zip-Slip-Schutz: nur unterhalb des Basisordners schreiben.
                if (!target.StartsWith(Path.GetFullPath(baseFolder) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            ((App)Application.Current).RestartForRestore();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "RestoreBackup");
            ConfirmDialog.Info($"Wiederherstellung fehlgeschlagen:\n{ex.Message}", centerOn);
        }
    }
}
