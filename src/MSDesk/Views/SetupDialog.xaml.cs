using System.IO;
using System.Windows;
using MSDesk.Interop;
using MSDesk.Services;

namespace MSDesk.Views;

/// <summary>
/// Erststart: fragt Namen und Sicherungsort ab. Bewusst nur diese zwei Punkte —
/// alles andere ist sinnvoll vorbelegt und laesst sich spaeter in den Optionen
/// aendern. Der Sicherungsort wird, wenn moeglich, auf einem Cloud-Laufwerk
/// vorgeschlagen, damit die Sicherung einen Rechnerausfall ueberlebt.
/// </summary>
public partial class SetupDialog : Window
{
    private readonly ConfigService _config;

    public SetupDialog(ConfigService config)
    {
        _config = config;
        InitializeComponent();

        FirstNameBox.Text = config.Config.UserFirstName;
        LastNameBox.Text = config.Config.UserLastName;

        // Noch kein Name hinterlegt? Aus dem Windows-Konto vorbelegen.
        if (string.IsNullOrWhiteSpace(FirstNameBox.Text) && string.IsNullOrWhiteSpace(LastNameBox.Text))
            PrefillFromWindowsAccount();

        BackupBox.Text = string.IsNullOrWhiteSpace(config.Config.AutoBackupFolder)
            ? SuggestBackupFolder()
            : config.Config.AutoBackupFolder;

        UpdateCloudHint();
        BackupBox.TextChanged += (_, _) => UpdateCloudHint();

        VersionText.Text = $"MSDesk v{UpdateService.CurrentVersion}";
        Loaded += (_, _) => FirstNameBox.Focus();
    }

    /// Zeigt den Assistenten beim allerersten Start (danach nie wieder von selbst).
    public static void RunOnFirstStart(ConfigService config)
    {
        if (config.Config.SetupCompleted) return;
        try
        {
            new SetupDialog(config).ShowDialog();
        }
        catch (Exception ex)
        {
            App.LogCrash(ex, "SetupDialog.RunOnFirstStart");
        }
    }

    /// „Max Mustermann" aus dem angemeldeten Konto ableiten, sofern moeglich.
    private void PrefillFromWindowsAccount()
    {
        try
        {
            var display = Environment.UserName;
            var parts = display.Split(new[] { '.', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                FirstNameBox.Text = Capitalize(parts[0]);
                LastNameBox.Text = Capitalize(parts[^1]);
            }
            else if (parts.Length == 1)
            {
                FirstNameBox.Text = Capitalize(parts[0]);
            }
        }
        catch (Exception)
        {
            // Vorbelegung ist reiner Komfort — schlaegt sie fehl, bleibt das Feld leer.
        }
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    /// Bevorzugt einen Cloud-Ordner, sonst „Dokumente".
    private static string SuggestBackupFolder()
    {
        foreach (var variable in new[] { "OneDriveCommercial", "OneDriveConsumer", "OneDrive" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                return Path.Combine(root, "MSDesk-Sicherungen");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MSDesk-Sicherungen");
    }

    /// Rueckmeldung, ob der gewaehlte Ordner nach einem Cloud-Laufwerk aussieht.
    private void UpdateCloudHint()
    {
        var path = BackupBox.Text ?? "";
        var cloudy = new[] { "onedrive", "sharepoint", "dropbox", "nextcloud", "google drive", "gdrive" }
            .Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (cloudy)
        {
            CloudHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            CloudHint.Text = "Sieht nach einem Cloud-Ordner aus — die Sicherung überlebt damit auch einen Rechnerwechsel.";
        }
        else if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            CloudHint.Foreground = System.Windows.Media.Brushes.LightGreen;
            CloudHint.Text = "Netzwerkfreigabe — liegt nicht auf diesem Rechner, das ist gut.";
        }
        else
        {
            CloudHint.Foreground = System.Windows.Media.Brushes.Khaki;
            CloudHint.Text = "Hinweis: Dieser Ordner liegt auf dem Rechner selbst. Bei einem Ausfall wäre die Sicherung mit weg — ein Cloud-Ordner ist sicherer.";
        }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Ordner für die Sicherungen wählen",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(BackupBox.Text) ? BackupBox.Text : ""
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            BackupBox.Text = dialog.SelectedPath;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        // Auch „Später" gilt als erledigt — sonst erschiene der Assistent bei
        // jedem Start erneut. Erreichbar bleibt alles ueber die Optionen.
        _config.Config.SetupCompleted = true;
        _config.Save();
        Close();
    }

    private void Finish_Click(object sender, RoutedEventArgs e)
    {
        _config.Config.UserFirstName = FirstNameBox.Text.Trim();
        _config.Config.UserLastName = LastNameBox.Text.Trim();

        var folder = (BackupBox.Text ?? "").Trim();
        if (folder.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(folder);
                _config.Config.AutoBackupFolder = folder;
            }
            catch (Exception ex)
            {
                App.LogCrash(ex, "SetupDialog.CreateBackupFolder");
                ConfirmDialog.Info(
                    $"Der Ordner konnte nicht angelegt werden:\n{folder}\n\nDu kannst ihn später in den Optionen setzen.",
                    this);
            }
        }

        _config.Config.SetupCompleted = true;
        _config.Save();
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.Apply(this, 0.97, true);
    }
}
