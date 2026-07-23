using System.IO;
using System.Windows;
using ISDesk.Interop;
using ISDesk.Services;

namespace ISDesk.Views;

/// Auswahl eines Titel-/Tab-Symbols: mitgelieferte Galerie, eigene PNG-Datei
/// oder gar kein Symbol. Rueckgabewert = Galerie-Dateiname bzw. absoluter Pfad.
public partial class IconPickerDialog : Window
{
    private sealed record GalleryEntry(string Name, System.Windows.Media.ImageSource? Image);

    private string? _selected;
    private bool _confirmed;

    private IconPickerDialog()
    {
        InitializeComponent();
        IconGallery.ItemsSource = IconLibrary.GalleryFiles()
            .Select(name => new GalleryEntry(name, IconLibrary.Load(name)))
            .Where(e => e.Image != null)
            .ToList();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.Apply(this, 0.95, true);
    }

    private void GalleryIcon_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GalleryEntry entry) return;
        _selected = entry.Name;
        _confirmed = true;
        DialogResult = true;
    }

    private void CustomFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Eigenes Symbol (PNG) wählen",
            Filter = "PNG-Bilder (*.png)|*.png"
        };
        if (dialog.ShowDialog() != true) return;
        if (!File.Exists(dialog.FileName)) return;

        _selected = dialog.FileName;
        _confirmed = true;
        DialogResult = true;
    }

    private void NoIcon_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        _confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// Rueckgabe: (bestaetigt, Wert) — Wert null bedeutet "kein Symbol".
    public static (bool Ok, string? Value) Show(Window? centerOn)
    {
        var dialog = new IconPickerDialog();
        if (centerOn != null)
        {
            dialog.Loaded += (_, _) =>
            {
                dialog.Left = centerOn.Left + (centerOn.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = centerOn.Top + (centerOn.ActualHeight - dialog.ActualHeight) / 2;
                DialogPlacement.ClampToWorkArea(dialog);
            };
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Loaded += (_, _) => DialogPlacement.ClampToWorkArea(dialog);
        }
        dialog.ShowDialog();
        return (dialog._confirmed, dialog._selected);
    }
}
