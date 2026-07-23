using System.Windows;
using ISDesk.Services;

namespace ISDesk.Views;

/// Dezenter Update-Hinweis unten rechts. „Jetzt installieren" laedt den Installer
/// und beendet ISDesk, damit das Setup ueberschreiben kann.
public partial class UpdateBanner : Window
{
    private readonly UpdateService _service;
    private readonly UpdateService.UpdateInfo _info;

    public UpdateBanner(UpdateService service, UpdateService.UpdateInfo info)
    {
        _service = service;
        _info = info;
        InitializeComponent();

        TitleText.Text = $"ISDesk {info.LatestVersion} ist verfügbar";
        var mb = info.Size > 0 ? $" ({info.Size / 1024 / 1024} MB)" : "";
        InfoText.Text = $"Du hast {UpdateService.CurrentVersion}. Jetzt aktualisieren{mb}?";

        Loaded += (_, _) =>
        {
            var wa = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                     ?? new System.Drawing.Rectangle(0, 0, 1280, 800);
            double dpi = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            Left = wa.Right / dpi - ActualWidth - 24;
            Top = wa.Bottom / dpi - ActualHeight - 24;
        };
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.Content = "Wird geladen…";
        InstallButton.IsEnabled = false;
        var path = await _service.DownloadAndRunAsync(_info);
        if (path == null)
        {
            InstallButton.Content = "Fehlgeschlagen";
            return;
        }
        Application.Current.Shutdown(); // Installer uebernimmt
    }
}
