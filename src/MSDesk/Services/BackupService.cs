using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using MSDesk.Models;
using MSDesk.Views;

namespace MSDesk.Services;

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

    /// Erkennungsmerkmale gaengiger Cloud-Ordner bzw. Netzwerkziele.
    private static readonly string[] OffMachineMarkers =
    {
        "onedrive", "sharepoint", "dropbox", "nextcloud", "owncloud",
        "google drive", "googledrive", "gdrive", "google", "icloud",
        "magentacloud", "hidrive", "cloud"
    };

    /// <summary>
    /// Liegt der Pfad ausserhalb dieses Rechners (Cloud-Ordner oder
    /// Netzwerkfreigabe)? Nur dann ueberlebt die Sicherung einen Ausfall.
    /// </summary>
    public static bool IsOffMachine(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true; // Netzwerkfreigabe
        return OffMachineMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public void CreateBackupInteractive(Window? centerOn)
    {
        var dialog = new SaveFileDialog
        {
            Title = "MSDesk-Sicherung erstellen",
            FileName = $"MSDesk-Sicherung-{DateTime.Now:yyyy-MM-dd}.zip",
            Filter = "MSDesk-Sicherung (*.zip)|*.zip",
            DefaultExt = "zip"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            WriteBackup(dialog.FileName);
            var mb = Math.Max(1, new FileInfo(dialog.FileName).Length / 1024 / 1024);
            var text = $"Sicherung erstellt ({mb} MB):\n{dialog.FileName}";
            if (_skippedLarge > 0) text += $"\n\n{_skippedLarge} große Datei(en) ausgelassen (nur Layout und Verknüpfungen werden gesichert).";
            ConfirmDialog.Info(text, centerOn);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "CreateBackup");
            ConfirmDialog.Info($"Sicherung fehlgeschlagen:\n{ex.Message}", centerOn);
        }
    }

    /// Ergebnis einer Sicherung ohne Fenster.
    public sealed record Ergebnis(bool Erfolg, string? Datei, long Bytes,
                                  int Ausgelassen, int Entfernt, string? Fehler)
    {
        public long Megabyte => Math.Max(1, Bytes / 1024 / 1024);
        public static Ergebnis Fehlgeschlagen(string grund)
            => new(false, null, 0, 0, 0, grund);
    }

    /// <summary>
    /// Schreibt eine Sicherung in den hinterlegten Ordner — OHNE jedes Fenster.
    /// Grundlage sowohl fuer den Knopf als auch fuer die taegliche Automatik;
    /// letztere darf unter keinen Umstaenden etwas aufpoppen lassen.
    /// </summary>
    public Ergebnis WriteToConfiguredFolder()
    {
        var folder = _config.Config.AutoBackupFolder;
        if (string.IsNullOrWhiteSpace(folder))
            return Ergebnis.Fehlgeschlagen("Kein Sicherungspfad hinterlegt — bitte oben den Ordner setzen.");

        try
        {
            Directory.CreateDirectory(folder);

            // Name mit Anwender, sofern hinterlegt: auf einem gemeinsamen
            // Laufwerk ist sonst nicht erkennbar, wessen Sicherung es ist.
            var wer = _config.Config.UserFullName;
            var kennung = string.IsNullOrWhiteSpace(wer) ? "" : "-" + SanitizeLeaf(wer);
            var praefix = $"MSDesk-Sicherung{kennung}-";
            var file = Path.Combine(folder, $"{praefix}{DateTime.Now:yyyy-MM-dd_HHmm}.zip");

            WriteBackup(file);

            // Aufgeraeumt wird NUR unter dem eigenen Namensanfang. Der
            // Sicherungsordner liegt bewusst in der Cloud und wird oft von
            // mehreren Kollegen genutzt — ein Muster ueber alle Dateien wuerde
            // dort taeglich und unwiderruflich die Sicherungen der anderen
            // wegraeumen.
            var removed = PruneOldBackups(folder, praefix, Math.Max(1, _config.Config.AutoBackupKeep));

            return new Ergebnis(true, file, new FileInfo(file).Length, _skippedLarge, removed, null);
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BackupService.WriteToConfiguredFolder");
            return Ergebnis.Fehlgeschlagen(ex.Message);
        }
    }

    /// Entfernt Zeichen, die in einem Dateinamen nicht vorkommen duerfen.
    private static string SanitizeLeaf(string value)
    {
        var ungueltig = Path.GetInvalidFileNameChars();
        var sauber = new string(value.Select(c => ungueltig.Contains(c) ? '_' : c).ToArray());
        return sauber.Replace(' ', '-').Trim('-', '.');
    }

    /// Ein-Klick-Sicherung in den hinterlegten Ordner (mit Zeitstempel im Namen).
    public void CreateBackupAuto(Window? centerOn)
    {
        var ergebnis = WriteToConfiguredFolder();
        if (!ergebnis.Erfolg)
        {
            ConfirmDialog.Info($"Sicherung fehlgeschlagen:\n{ergebnis.Fehler}", centerOn);
            return;
        }

        var text = $"Sicherung erstellt ({ergebnis.Megabyte} MB):\n{ergebnis.Datei}";
        if (ergebnis.Ausgelassen > 0)
            text += $"\n\n{ergebnis.Ausgelassen} große Datei(en) ausgelassen (nur Layout und Verknüpfungen werden gesichert).";
        if (ergebnis.Entfernt > 0)
            text += $"\n{ergebnis.Entfernt} ältere Sicherung(en) entfernt – es bleiben die neuesten {Math.Max(1, _config.Config.AutoBackupKeep)}.";
        ConfirmDialog.Info(text, centerOn);
    }

    /// Groessere Dateien kommen NICHT in die Sicherung: gesichert wird das Layout
    /// (Konfiguration) samt Verknuepfungen — echte Arbeitsdateien gehoeren ins
    /// normale Datei-Backup und wuerden die ZIP sonst auf hunderte MB aufblaehen.
    private const long MaxFileSize = 1024 * 1024; // 1 MB

    /// Anzahl der beim letzten Lauf uebersprungenen grossen Dateien.
    private int _skippedLarge;

    /// <summary>
    /// Schreibt die Sicherung erst unter einem Arbeitsnamen und benennt sie
    /// zuletzt um.
    ///
    /// Bricht das Packen mittendrin ab (eine Datei verschwindet, das Ziel ist
    /// voll), bliebe sonst eine unvollstaendige ZIP mit gueltigem Aufbau liegen.
    /// Die zaehlt beim Aufraeumen als eine der neuesten mit und verdraengt eine
    /// heile Sicherung — der Schaden faellt erst auf, wenn man sie braucht.
    /// Seit die Sicherung ohne Zutun laeuft, sieht das ausserdem niemand mehr.
    /// </summary>
    private void WriteBackup(string fileName)
    {
        var arbeitsdatei = fileName + ".unvollstaendig";
        try
        {
            WriteZip(arbeitsdatei);
        }
        catch (Exception)
        {
            try { if (File.Exists(arbeitsdatei)) File.Delete(arbeitsdatei); }
            catch (Exception) { /* dann bleibt sie eben liegen — sie heisst nicht wie eine Sicherung */ }
            throw;
        }

        if (File.Exists(fileName)) File.Delete(fileName);
        File.Move(arbeitsdatei, fileName);
    }

    private void WriteZip(string fileName)
    {
        _config.Save(); // aktuellen Stand auf die Platte bringen
        _skippedLarge = 0;

        if (File.Exists(fileName)) File.Delete(fileName);
        using var zip = ZipFile.Open(fileName, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(_config.ConfigPath, "config.json");

        // Vorschaubilder der Bildschirm-Anordnungen mitnehmen: nach einem
        // Neuaufsetzen sieht man damit sofort wieder, wie jede Konfiguration
        // aussah. Zusammen nur wenige hundert Kilobyte.
        try
        {
            if (Directory.Exists(LayoutPreview.Folder))
                foreach (var bild in Directory.EnumerateFiles(LayoutPreview.Folder, "*.jpg"))
                    zip.CreateEntryFromFile(bild, $"Vorschau/{Path.GetFileName(bild)}");
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "BackupService.Vorschau"); // Sicherung trotzdem fortsetzen
        }

        var baseFolder = _config.Config.BaseFolder;
        if (Directory.Exists(baseFolder))
        {
            foreach (var file in Directory.EnumerateFiles(baseFolder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (new FileInfo(file).Length > MaxFileSize) { _skippedLarge++; continue; }
                }
                catch (Exception) { continue; }

                var rel = Path.GetRelativePath(baseFolder, file).Replace('\\', '/');
                zip.CreateEntryFromFile(file, "Fences/" + rel);
            }
        }
    }

    /// <summary>
    /// Behaelt nur die neuesten EIGENEN Sicherungen im Ordner.
    ///
    /// Der Namensanfang ist entscheidend: er enthaelt den Namen des Anwenders,
    /// und genau dafuer steht er im Dateinamen. Ein Muster ueber alle
    /// „MSDesk-Sicherung*.zip" wuerde in einem gemeinsam genutzten Cloud- oder
    /// Netzordner taeglich und unwiderruflich die Sicherungen der Kollegen
    /// wegraeumen — ohne Papierkorb, ohne Rueckfrage, ohne Meldung.
    ///
    /// Von Hand ueber „Sicherung speichern unter…" abgelegte Dateien bleiben
    /// ebenfalls unangetastet, solange sie anders heissen.
    /// </summary>
    private static int PruneOldBackups(string folder, string praefix, int keep)
    {
        try
        {
            var old = new DirectoryInfo(folder)
                .GetFiles(praefix + "*.zip")
                // GetFiles vergleicht Muster nach Windows-Art (auch gegen den
                // 8.3-Kurznamen). Deshalb zusaetzlich ausdruecklich pruefen,
                // dass der Name wirklich so anfaengt.
                .Where(f => f.Name.StartsWith(praefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in old) f.Delete();
            return old.Count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public void RestoreBackupInteractive(Window? centerOn)
    {
        var dialog = new OpenFileDialog
        {
            Title = "MSDesk-Sicherung wiederherstellen",
            Filter = "MSDesk-Sicherung (*.zip)|*.zip"
        };
        if (dialog.ShowDialog() != true) return;

        var (confirmed, _) = ConfirmDialog.Show(
            "Sicherung wiederherstellen?\n\nDie aktuelle Konfiguration wird ersetzt, die gesicherten " +
            "Bereichs-Ordner werden zurückgespielt (bestehende Dateien gleichen Namens werden überschrieben). " +
            "MSDesk startet danach neu.",
            centerOn, okText: "Wiederherstellen");
        if (!confirmed) return;

        try
        {
            using var zip = ZipFile.OpenRead(dialog.FileName);

            var configEntry = zip.GetEntry("config.json")
                ?? throw new InvalidDataException("Die Datei ist keine MSDesk-Sicherung (config.json fehlt).");

            // Basisordner aus der GESICHERTEN Konfiguration lesen.
            AppConfig restored;
            using (var stream = configEntry.Open())
            {
                restored = JsonSerializer.Deserialize<AppConfig>(stream) ?? new AppConfig();
            }
            // Der gesicherte Ordner muss es auf DIESEM Rechner nicht geben — eine
            // Sicherung von „D:\Fences" landet auch auf einem Notebook ohne
            // Laufwerk D:. Deshalb wird ein nutzbarer Ort bestimmt und die
            // gesicherte Konfiguration darauf umgeschrieben, bevor sie zaehlt.
            var gesichert = restored.BaseFolder;
            var baseFolder = BaseFolderResolver.EnsureUsable(gesichert);

            // Ausschlaggebend ist, ob der ORDNER wechselt — nicht, wie viele
            // Pfade dabei umgeschrieben wurden. Bei einer Sicherung ohne Tabs
            // waeren das null, und die Korrektur des Ordners ginge verloren.
            var verlegt = !string.Equals(gesichert, baseFolder, StringComparison.OrdinalIgnoreCase);
            if (verlegt)
            {
                var umgezogen = string.IsNullOrWhiteSpace(gesichert)
                    ? 0
                    : BaseFolderResolver.Remap(restored, gesichert, baseFolder);
                restored.BaseFolder = baseFolder;
                StartupLog.Write(
                    $"Wiederherstellung: {gesichert} nicht nutzbar → {baseFolder} ({umgezogen} Pfade angepasst)");
            }

            // Ab hier nichts mehr speichern (sonst ueberschreibt die alte In-Memory-Config die Wiederherstellung).
            _config.SuppressSaves();
            _manager.CloseAllWithoutSave();

            // Alte Konfiguration aufheben, neue schreiben.
            if (File.Exists(_config.ConfigPath))
                File.Copy(_config.ConfigPath, _config.ConfigPath + ".vor-wiederherstellung.json", overwrite: true);

            if (verlegt)
            {
                // Angepasste Fassung schreiben statt der gesicherten — sonst
                // stuenden die alten Laufwerkspfade sofort wieder in der Datei.
                File.WriteAllText(_config.ConfigPath,
                    JsonSerializer.Serialize(restored, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                // Unveraendert uebernehmen: die Datei wandert Byte fuer Byte
                // zurueck, damit auch Angaben erhalten bleiben, die diese
                // Programmfassung noch gar nicht kennt.
                configEntry.ExtractToFile(_config.ConfigPath, overwrite: true);
            }

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

            // Vorschaubilder der Anordnungen ebenfalls zurueckspielen.
            try
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("Vorschau/", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                    var name = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    Directory.CreateDirectory(LayoutPreview.Folder);
                    entry.ExtractToFile(Path.Combine(LayoutPreview.Folder, name), overwrite: true);
                }
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "RestoreBackup.Vorschau"); // nicht wesentlich
            }

            // Sperre erneut setzen, bevor es zum Neustart geht. Sie loest sich
            // nach 30 Sekunden von selbst — ein Sicherheitsnetz, falls die
            // Wiederherstellung gar nicht durchlaeuft. Bei einer grossen
            // Sicherung oder einem Ziel in der Cloud dauert das Entpacken aber
            // laenger als das; die Sperre waere dann bereits offen, und das
            // Beenden wuerde den ALTEN Stand aus dem Speicher ueber die eben
            // zurueckgespielte Datei schreiben. Das Neusetzen kostet nichts und
            // gilt bis zum Beenden wenige Augenblicke spaeter.
            _config.SuppressSaves();

            ((App)Application.Current).RestartForRestore();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "RestoreBackup");
            ConfirmDialog.Info($"Wiederherstellung fehlgeschlagen:\n{ex.Message}", centerOn);
        }
    }
}
