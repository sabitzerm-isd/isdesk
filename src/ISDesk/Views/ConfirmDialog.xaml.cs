using System.Windows;
using ISDesk.Interop;

namespace ISDesk.Views;

/// Topmost-Bestaetigungs-/Hinweisdialog im ISDesk-Stil. Ersetzt MessageBox,
/// weil die Bereichs-Fenster bottom-most sind und System-Dialoge sonst hinter
/// anderen Anwendungen aufgehen koennen.
public partial class ConfirmDialog : Window
{
    private ConfirmDialog(string message, string okText, string? checkboxText, bool showCancel)
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Content = okText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(checkboxText))
        {
            OptionCheck.Content = checkboxText;
            OptionCheck.Visibility = Visibility.Visible;
        }
    }

    public bool OptionChecked => OptionCheck.IsChecked == true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowBackdrop.Apply(this, 0.95, true);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// Bestaetigung mit OK/Abbrechen und optionaler Zusatz-Checkbox.
    /// Rueckgabe: (bestaetigt, Checkbox angehakt).
    public static (bool Confirmed, bool OptionChecked) Show(
        string message, Window? centerOn, string okText = "OK", string? checkboxText = null)
    {
        var dialog = new ConfirmDialog(message, okText, checkboxText, showCancel: true);
        Center(dialog, centerOn);
        var confirmed = dialog.ShowDialog() == true;
        return (confirmed, confirmed && dialog.OptionChecked);
    }

    /// Reiner Hinweis mit einem OK-Button.
    public static void Info(string message, Window? centerOn)
    {
        var dialog = new ConfirmDialog(message, "OK", null, showCancel: false);
        Center(dialog, centerOn);
        dialog.ShowDialog();
    }

    private static void Center(ConfirmDialog dialog, Window? centerOn)
    {
        if (centerOn != null)
        {
            dialog.Loaded += (_, _) =>
            {
                dialog.Left = centerOn.Left + (centerOn.ActualWidth - dialog.ActualWidth) / 2;
                dialog.Top = centerOn.Top + (centerOn.ActualHeight - dialog.ActualHeight) / 2;
            };
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
